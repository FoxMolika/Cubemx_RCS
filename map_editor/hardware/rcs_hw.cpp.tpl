/** @file rcs_hw.cpp
 * @brief 硬件实例调度及队内驱动薄适配；C++异常不跨越C接口。
 * @note 时间到达仅为估计；没有独立反馈时不得宣称实物已到位。
 */
#include "rcs_hw.h"
#include "rcs_hw_config.h"
#include <math.h>
#include <string.h>
#if RCS_HW_TEAM_CYLINDERS
#include "rcs_cylinder.h"
static cylinder_no_sensor hw_cylinders[RCS_HW_TEAM_CYLINDERS];
#endif
#if RCS_HW_TEAM_GYROS
#include "rcs_gyro.h"
static rcs_jy901 hw_gyros[RCS_HW_TEAM_GYROS];
#endif
#if RCS_HW_SPIN8_GYROS
#include "xs_gyro.h"
#endif
@EXTERNS@
@CUSTOM_CODE@

enum { HW_CYLINDER=1, HW_GYRO=2, HW_GPIO=3, HW_PWM=4, HW_CUSTOM=5, HW_SPIN8=6 };
struct hw_spec {
    const char *id;
    unsigned kind, period_ms, object_index;
    void *port;
    uint32_t u[6];
    float f[4];
};
struct hw_state {
    uint32_t serviced, command_tick;
    float target;
    bool active, done;
};
static const hw_spec hw_specs[RCS_HW_STORAGE] = {
@DEVICE_ROWS@};
static hw_state hw_states[RCS_HW_STORAGE];
static size_t hw_count = RCS_HW_DEVICE_COUNT;
struct hw_operation { size_t instance; const char *name; float min_value,max_value; };
static const hw_operation hw_operations[RCS_HW_OP_STORAGE] = {
@OPERATION_ROWS@};
static size_t hw_operation_count = RCS_HW_OP_COUNT;
static int hw_error;
static bool hw_initialized, hw_enabled, hw_servicing;
static uint32_t hw_enabled_tick;
static void latch(int error) { if (!hw_error) hw_error = error; }
static size_t find_device(const char *id)
{
    if (id) for (size_t i=0; i<hw_count; ++i) if (!strcmp(id,hw_specs[i].id)) return i;
    return hw_count;
}
static bool gpio_output(const hw_spec &spec)
{
    GPIO_TypeDef *port = static_cast<GPIO_TypeDef*>(spec.port);
    unsigned bit=0;
    while (bit<16u && (spec.u[0] & (1u<<bit))==0u) ++bit;
    return port && bit<16u && ((port->MODER>>(2u*bit))&3u)==1u;
}

#if defined(HAL_UART_MODULE_ENABLED)
typedef size_t (*hw_uart_fn)(uint8_t, const uint8_t*, size_t);
struct hw_uart_port { UART_HandleTypeDef *handle; hw_uart_fn rx; uint8_t number, byte; bool receiving; };
static hw_uart_port hw_uarts[RCS_HW_STORAGE];
static size_t hw_uart_count;
#if RCS_HW_TEAM_GYROS
rcs_err_t rcs_uart_driver_install(const rcs_uart_conf_t *conf, rcs_uart_t number)
{
    if (!conf || !conf->huartx || !conf->rxCallback || hw_uart_count>=RCS_HW_STORAGE) return RCS_FAIL;
    for (size_t i=0;i<hw_uart_count;++i) if (hw_uarts[i].handle==conf->huartx) return RCS_ERR_MULTIPLE_USED;
    hw_uart_port &port=hw_uarts[hw_uart_count++];
    port.handle=conf->huartx; port.rx=conf->rxCallback; port.number=number; port.receiving=false;
    /* 不在vector注册过程中开接收，待所有对象注册完成才统一启动。 */
    return RCS_OK;
}
#endif
#if RCS_HW_SPIN8_GYROS
static size_t hw_spin8_uart_rx(uint8_t number, const uint8_t *data, size_t len)
{
    (void)number;
    xs_gyro_feed(data, (uint32_t)len);
    return len;
}
/* 与队内陀螺仪共用同一张端口表：同一个串口被两台设备抢时直接拒绝。 */
static int hw_spin8_attach(UART_HandleTypeDef *handle)
{
    if (!handle || hw_uart_count>=RCS_HW_STORAGE) return 0;
    for (size_t i=0;i<hw_uart_count;++i) if (hw_uarts[i].handle==handle) return 0;
    hw_uart_port &port=hw_uarts[hw_uart_count++];
    port.handle=handle; port.rx=hw_spin8_uart_rx; port.number=0u; port.receiving=false;
    return 1;
}
#endif
#endif

#if RCS_HW_TEAM_CYLINDERS
int rcs_hw_team_cylinder_allowed(size_t object)
{
    if (!hw_enabled || hw_error) return 0;
    for (size_t i=0;i<hw_count;++i)
        if (hw_specs[i].kind==HW_CYLINDER && hw_specs[i].object_index==object)
            return hw_states[i].active ? 1 : 0;
    return 0;
}
static void clear_cylinders(void)
{
    cylinder_no_sensor::conf_list.clear(); cylinder_no_sensor::obj_list.clear();
    cylinder_no_sensor::last_push_time.clear(); cylinder_no_sensor::last_pull_time.clear();
    cylinder_no_sensor::input.clear(); cylinder_no_sensor::status.clear();
    cylinder_no_sensor::monitor_target.clear(); cylinder_no_sensor::monitor_current.clear();
}
#endif

#if RCS_HW_PWM_DEVICES
static uint32_t pwm_counter_hz(TIM_HandleTypeDef *timer)
{
    uint32_t hz, divider;
    bool apb2 = timer->Instance==TIM1 || timer->Instance==TIM8 || timer->Instance==TIM9 ||
                timer->Instance==TIM10 || timer->Instance==TIM11;
    if (apb2) { hz=HAL_RCC_GetPCLK2Freq(); divider=RCC->CFGR & RCC_CFGR_PPRE2; }
    else { hz=HAL_RCC_GetPCLK1Freq(); divider=RCC->CFGR & RCC_CFGR_PPRE1; }
    if (divider) hz*=2u;
    return hz / (timer->Init.Prescaler+1u);
}
static bool pwm_valid(const hw_spec &spec)
{
    TIM_HandleTypeDef *timer=static_cast<TIM_HandleTypeDef*>(spec.port);
    uint64_t ticks=(uint64_t)spec.u[1]*spec.u[2]/1000000u;
    return timer && pwm_counter_hz(timer)==spec.u[2] && ticks>0u &&
           (uint64_t)__HAL_TIM_GET_AUTORELOAD(timer)+1u==ticks;
}
#endif

extern "C" void rcs_hw_cancel(const char *id)
{
    size_t i=find_device(id);
    if (i==hw_count) return;
    const hw_spec &s=hw_specs[i];
    bool active=hw_states[i].active;
    hw_states[i].active=false; hw_states[i].done=false;
    try {
        if (s.kind==HW_CYLINDER || s.kind==HW_GPIO) {
            HAL_GPIO_WritePin(static_cast<GPIO_TypeDef*>(s.port),(uint16_t)s.u[0],
                s.u[4] ? GPIO_PIN_SET : GPIO_PIN_RESET);
        }
#if RCS_HW_PWM_DEVICES
        else if (s.kind==HW_PWM && active) {
            TIM_HandleTypeDef *timer=static_cast<TIM_HandleTypeDef*>(s.port);
            __HAL_TIM_SET_COMPARE(timer,s.u[0],0u);
            (void)HAL_TIM_PWM_Stop(timer,s.u[0]);
        }
#endif
        else if (s.kind==HW_CUSTOM && active) {
            switch(i) { @CUSTOM_STOP_CASES@ default: break; }
        }
    } catch (...) { latch(RCS_HW_EXCEPTION); }
}

extern "C" void rcs_hw_stop(void)
{
    hw_enabled=false;
    for (size_t i=0;i<hw_count;++i) rcs_hw_cancel(hw_specs[i].id);
}
extern "C" int rcs_hw_fault(void) { return hw_error; }

extern "C" int rcs_hw_ready(void)
{
    if(!hw_initialized || hw_error) return 0;
    for(size_t i=0;i<hw_count;++i) {
        if((hw_specs[i].kind==HW_GYRO || hw_specs[i].kind==HW_SPIN8) && hw_specs[i].u[3]) {
            rcs_observation sample;
            if(!rcs_hw_read_observation(hw_specs[i].id,&sample)) return 0;
        }
    }
    return 1;
}

extern "C" int rcs_hw_init(void)
{
    if (hw_servicing) return 0;
    rcs_hw_stop();
    hw_initialized=false; hw_error=RCS_HW_OK;
    try {
#if defined(HAL_UART_MODULE_ENABLED)
        for(size_t i=0;i<hw_uart_count;++i) {
            if(hw_uarts[i].receiving && HAL_UART_AbortReceive(hw_uarts[i].handle)!=HAL_OK) {
                latch(RCS_HW_UART); return 0;
            }
        }
        hw_uart_count=0;
#endif
#if RCS_HW_TEAM_CYLINDERS
        clear_cylinders();
#endif
#if RCS_HW_TEAM_GYROS
        rcs_jy901::alpha_clear_all();
#endif
        memset(hw_states,0,sizeof(hw_states));
        uint32_t now_ms=HAL_GetTick();
        for(size_t i=0;i<hw_count;++i) {
            const hw_spec &s=hw_specs[i]; hw_states[i].serviced=now_ms;
            if (s.kind==HW_CYLINDER || s.kind==HW_GPIO) {
                if(!gpio_output(s)) { latch(RCS_HW_CONFIG); return 0; }
                HAL_GPIO_WritePin(static_cast<GPIO_TypeDef*>(s.port),(uint16_t)s.u[0],
                    s.u[4] ? GPIO_PIN_SET : GPIO_PIN_RESET);
            }
#if RCS_HW_TEAM_CYLINDERS
            if(s.kind==HW_CYLINDER)
                hw_cylinders[s.object_index].init(static_cast<GPIO_TypeDef*>(s.port),(uint16_t)s.u[0],
                    s.u[1]!=0,s.u[2]!=0,s.u[3]);
#endif
#if RCS_HW_TEAM_GYROS
            if(s.kind==HW_GYRO) {
                UART_HandleTypeDef *uart=static_cast<UART_HandleTypeDef*>(s.port);
                if(uart->Init.BaudRate!=s.u[2] || hw_gyros[s.object_index].init(uart,(rcs_uart_t)s.u[0])!=RCS_OK) {
                    latch(RCS_HW_CONFIG); return 0;
                }
            }
#endif
#if RCS_HW_PWM_DEVICES
            if(s.kind==HW_PWM && !pwm_valid(s)) { latch(RCS_HW_CONFIG); return 0; }
#endif
#if RCS_HW_SPIN8_GYROS
            if(s.kind==HW_SPIN8) {
                UART_HandleTypeDef *uart=static_cast<UART_HandleTypeDef*>(s.port);
                if(uart->Init.BaudRate!=s.u[2] || !hw_spin8_attach(uart)) { latch(RCS_HW_CONFIG); return 0; }
                xs_gyro_init();
                xs_gyro_set_heading_zero(0.0f);
            }
#endif
            if(s.kind==HW_CUSTOM) {
                int ok=0; switch(i) { @CUSTOM_INIT_CASES@ default: break; }
                if(!ok) { latch(RCS_HW_INIT); return 0; }
            }
        }
#if defined(HAL_UART_MODULE_ENABLED)
        for(size_t i=0;i<hw_uart_count;++i) {
            hw_uart_port &port=hw_uarts[i];
            if(HAL_UART_Receive_IT(port.handle,&port.byte,1u)!=HAL_OK) { latch(RCS_HW_UART); return 0; }
            port.receiving=true;
        }
#endif
        hw_initialized=true;
        return 1;
    } catch (...) { latch(RCS_HW_EXCEPTION); rcs_hw_stop(); return 0; }
}

extern "C" void rcs_hw_set_enabled(int enabled)
{
    if (!enabled) { rcs_hw_stop(); return; }
    if (hw_initialized && !hw_error && !hw_enabled) { hw_enabled=true; hw_enabled_tick=HAL_GetTick(); }
}

extern "C" int rcs_hw_command(const char *id,const char *operation,float value)
{
    if (!hw_initialized || !hw_enabled || hw_error || !operation || !isfinite(value)) return 0;
    size_t i=find_device(id); if(i==hw_count) return 0;
    bool operation_allowed=false;
    for(size_t n=0;n<hw_operation_count;++n) {
        const hw_operation &op=hw_operations[n];
        if(op.instance==i && !strcmp(operation,op.name) && value>=op.min_value && value<=op.max_value)
            operation_allowed=true;
    }
    if(!operation_allowed) return 0;
    const hw_spec &s=hw_specs[i]; hw_state &state=hw_states[i];
    try {
        bool accepted=false;
#if RCS_HW_TEAM_CYLINDERS
        if(s.kind==HW_CYLINDER) {
            if(!strcmp(operation,"push")) {
                hw_cylinders[s.object_index].update(cylinder_no_sensor::PUSH); state.target=1; accepted=true;
            } else if(!strcmp(operation,"pull")) {
                hw_cylinders[s.object_index].update(cylinder_no_sensor::PULL); state.target=0; accepted=true;
            }
        }
#endif
        if(s.kind==HW_GPIO && !strcmp(operation,"write") && (value==0.0f || value==1.0f)) {
            HAL_GPIO_WritePin(static_cast<GPIO_TypeDef*>(s.port),(uint16_t)s.u[0],
                value!=0.0f ? GPIO_PIN_SET : GPIO_PIN_RESET);
            accepted=true; state.done=true;
        }
#if RCS_HW_PWM_DEVICES
        if(s.kind==HW_PWM && !strcmp(operation,"angle") && value>=s.f[2] && value<=s.f[3]) {
            TIM_HandleTypeDef *timer=static_cast<TIM_HandleTypeDef*>(s.port);
            float pulse=s.f[0]+(value-s.f[2])*(s.f[1]-s.f[0])/(s.f[3]-s.f[2]);
            uint32_t ticks=(uint32_t)(pulse*(float)s.u[2]/1000000.0f+0.5f);
            __HAL_TIM_SET_COMPARE(timer,s.u[0],ticks);
            if(!state.active && HAL_TIM_PWM_Start(timer,s.u[0])!=HAL_OK) {
                __HAL_TIM_SET_COMPARE(timer,s.u[0],0u); latch(RCS_HW_INIT); return 0;
            }
            accepted=true; state.done=false;
        }
#endif
        if(s.kind==HW_CUSTOM) {
            int ok=0; switch(i) { @CUSTOM_COMMAND_CASES@ default: break; }
            accepted=ok!=0;
        }
        if(accepted) {
            if(s.kind!=HW_GPIO) state.done=false;
            state.active=true; state.command_tick=HAL_GetTick(); return 1;
        }
        return 0;
    } catch(...) { latch(RCS_HW_EXCEPTION); rcs_hw_stop(); return 0; }
}

extern "C" int rcs_hw_require_command(const char *id,const char *operation,float value)
{
    if(rcs_hw_command(id,operation,value)) return 1;
    latch(RCS_HW_COMMAND);
    rcs_hw_stop();
    return 0;
}

extern "C" int rcs_hw_done(const char *id)
{
    size_t i=find_device(id);
    if(i==hw_count || !hw_initialized || hw_error || !hw_states[i].active) return 0;
    try {
        if(hw_specs[i].kind==HW_CUSTOM) { switch(i) { @CUSTOM_DONE_CASES@ default: return 0; } }
        return hw_states[i].done ? 1 : 0;
    } catch(...) { latch(RCS_HW_EXCEPTION); rcs_hw_stop(); return 0; }
}

extern "C" void rcs_hw_service(uint32_t now_ms)
{
    if(!hw_initialized || hw_servicing) return;
    hw_servicing=true;
    try {
        bool cylinder_due=false;
        for(size_t i=0;i<hw_count;++i) {
            const hw_spec &s=hw_specs[i]; hw_state &state=hw_states[i];
            uint32_t dt_ms=(uint32_t)(now_ms-state.serviced);
            if(dt_ms<s.period_ms) continue;
            state.serviced=now_ms;
            if(hw_enabled && state.active && dt_ms>s.period_ms*2u+1u) latch(RCS_HW_TIMING);
            if(s.kind==HW_CYLINDER) cylinder_due=true;
#if RCS_HW_TEAM_GYROS
            if(s.kind==HW_GYRO && hw_enabled && s.u[3]) {
                rcs_jy901 &gyro=hw_gyros[s.object_index];
                uint32_t stamp=gyro.alpha_valid ? gyro.alpha_tick : hw_enabled_tick;
                if((uint32_t)(now_ms-stamp)>s.u[1]) latch(RCS_HW_TIMEOUT);
            }
#endif
#if RCS_HW_PWM_DEVICES
            if(s.kind==HW_PWM && state.active && (uint32_t)(now_ms-state.command_tick)>=s.u[3]) state.done=true;
#endif
#if RCS_HW_SPIN8_GYROS
            if(s.kind==HW_SPIN8) {
                xs_gyro_update();
                if(hw_enabled && s.u[3]) {
                    uint32_t age=xs_gyro_age_ms();
                    if(age==0xFFFFFFFFu || age>s.u[1]) latch(RCS_HW_TIMEOUT);
                }
            }
#endif
            if(s.kind==HW_CUSTOM) { switch(i) { @CUSTOM_SERVICE_CASES@ default: break; } }
        }
#if RCS_HW_TEAM_CYLINDERS
        if(cylinder_due && hw_enabled && !hw_error) {
            (void)cylinder_no_sensor::excute();
            for(size_t i=0;i<hw_count;++i) if(hw_specs[i].kind==HW_CYLINDER && hw_states[i].active) {
                cylinder_no_sensor::stat_t status=hw_cylinders[hw_specs[i].object_index].get();
                hw_states[i].done=hw_states[i].target!=0.0f ? status==cylinder_no_sensor::PUSHED : status==cylinder_no_sensor::PULLED;
            }
        }
#else
        (void)cylinder_due;
#endif
        if(hw_error) rcs_hw_stop();
    } catch(...) { latch(RCS_HW_EXCEPTION); rcs_hw_stop(); }
    hw_servicing=false;
}

extern "C" void rcs_hw_on_can_rx(CAN_HandleTypeDef *can,const CAN_RxHeaderTypeDef *header,
                                  const uint8_t data[8])
{
    (void)can; (void)header; (void)data;
    if(!hw_initialized) return;
    try { @CUSTOM_CAN_CASES@ } catch(...) { latch(RCS_HW_EXCEPTION); }
}
#if defined(HAL_UART_MODULE_ENABLED)
extern "C" void rcs_hw_on_uart_rx(UART_HandleTypeDef *uart,const uint8_t *data,size_t len)
{
    if(!hw_initialized || !uart) return;
    try {
        if(data && len) for(size_t i=0;i<hw_uart_count;++i)
            if(hw_uarts[i].handle==uart && hw_uarts[i].rx) (void)hw_uarts[i].rx(hw_uarts[i].number,data,len);
        @CUSTOM_UART_CASES@
    } catch(...) { latch(RCS_HW_EXCEPTION); }
}
extern "C" void rcs_hw_uart_irq(UART_HandleTypeDef *uart)
{
    for(size_t i=0;i<hw_uart_count;++i) if(hw_uarts[i].handle==uart) {
        hw_uart_port &port=hw_uarts[i];
        port.receiving=false;
        rcs_hw_on_uart_rx(uart,&port.byte,1u);
        if(HAL_UART_Receive_IT(uart,&port.byte,1u)!=HAL_OK) latch(RCS_HW_UART);
        else port.receiving=true;
        return;
    }
    rcs_hw_on_uart_rx(uart,NULL,0u);
}
extern "C" void rcs_hw_uart_error(UART_HandleTypeDef *uart)
{
    for(size_t i=0;i<hw_uart_count;++i) if(hw_uarts[i].handle==uart) { latch(RCS_HW_UART); return; }
}
#endif
#if defined(HAL_TIM_MODULE_ENABLED)
extern "C" void rcs_hw_on_timer(TIM_HandleTypeDef *timer)
{
    (void)timer;
    if(!hw_initialized) return;
    try { @CUSTOM_TIMER_CASES@ } catch(...) { latch(RCS_HW_EXCEPTION); }
}
#endif

extern "C" int rcs_hw_read_observation(const char *id,rcs_observation *sample)
{
    size_t i=find_device(id);
    if(i==hw_count || !sample || !hw_initialized || hw_error) return 0;
    memset(sample,0,sizeof(*sample));
    try {
#if RCS_HW_TEAM_GYROS
        const hw_spec &s=hw_specs[i];
        if(s.kind==HW_GYRO) {
            uint32_t saved=__get_PRIMASK(); __disable_irq();
            rcs_jy901 &gyro=hw_gyros[s.object_index];
            bool valid=gyro.alpha_valid;
            uint32_t tick=gyro.alpha_tick, seq=gyro.alpha_sequence;
            float yaw=gyro.get_yaw_deg();
            __set_PRIMASK(saved);
            uint32_t age=(uint32_t)(HAL_GetTick()-tick);
            if(!valid || age>s.u[1]) return 0;
            yaw=yaw*s.f[1]*0.0174532925199433f+s.f[0];
            sample->yaw_rad=atan2f(sinf(yaw),cosf(yaw));
            sample->age_s=(float)age*0.001f; sample->quality=1.0f;
            sample->sequence=seq; sample->axes=4u;
            return 1;
        }
#endif
#if RCS_HW_SPIN8_GYROS
        const hw_spec &sg=hw_specs[i];
        if(sg.kind==HW_SPIN8) {
            if(!xs_gyro_online()) return 0;
            uint32_t age=xs_gyro_age_ms();
            if(age>sg.u[1]) return 0;
            float yaw=xs_gyro_yaw_rad()*sg.f[1]+sg.f[0];
            sample->yaw_rad=atan2f(sinf(yaw),cosf(yaw));
            sample->age_s=(float)age*0.001f; sample->quality=1.0f;
            sample->sequence=xs_gyro_sequence(); sample->axes=4u;
            return 1;
        }
#endif
        switch(i) { @CUSTOM_OBSERVATION_CASES@ default: return 0; }
    } catch(...) { latch(RCS_HW_EXCEPTION); return 0; }
}
@OBSERVATION_FUNCTIONS@
