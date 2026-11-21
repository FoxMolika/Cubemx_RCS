/**
 * @file rcs_app_task.c
 * @brief 应用调度：实际编码器反馈、导航与用户状态机的连接层。
 * @note 主循环持续调用；用户区禁止阻塞。停止与故障均不会自行重新启动。
 */
#include "rcs_app_task.h"
#include "rcs_app_config.h"
#include "rcs_config.h"
#include "rcs_can_port.h"
#include "rcs_feedback_port.h"
#include "rcs_route.h"
#include "machism.h"
#include <math.h>

static rcs_navigator g_nav;
static rcs_app_state g_state = RCS_APP_NOT_READY;
static CAN_HandleTypeDef *g_can;
static uint32_t g_chassis_tick, g_app_tick, g_boot_tick, g_arm_ms;
static uint8_t g_initialized, g_pose_valid, g_auto_consumed;
static uint8_t g_route_active, g_arming, g_holding;
static float g_pose_x, g_pose_y, g_pose_yaw;
static rcs_app_fault g_first_fault;
static rcs_fault g_chassis_fault;
static rcs_nav_fault g_nav_fault;
static int g_machine_fault;
static int g_feedback_fault;
static uint32_t g_fault_tick;
static uint32_t g_last_task_tick, g_loop_ms, g_loop_max_ms;
volatile foxmolika_debug_t g_dbg;
static void app_fault(rcs_app_fault reason);
static void app_debug_update(void);

/* USER CODE BEGIN APP_CONTEXT */
/* 有机构动作时返回你的 action_context（机构状态指针）；没有就保持返回 0。
 * 本函数的返回值会直接传给 rcs_route_init()。 */
static void *app_context(void)
{
    return 0;
}
/* USER CODE END APP_CONTEXT */

/* USER CODE BEGIN APP_START_POSE */
/* P0 是操作员声明的实际摆放点，并非传感器测量；未选择时等待显式位姿。 */
static int app_start_pose(float *x_m, float *y_m, float *yaw_rad)
{
#if RCS_APP_START_AT_P0
    *x_m = RCS_APP_START_X_M;
    *y_m = RCS_APP_START_Y_M;
    *yaw_rad = RCS_APP_START_YAW_RAD;
    return 1;
#else
    (void)x_m; (void)y_m; (void)yaw_rad;
    return 0;
#endif
}
/* USER CODE END APP_START_POSE */

/* USER CODE BEGIN APP_READY_STEP */
static int app_ready_ok(const rcs_feedback_sample *sample)
{
    if (!RCS_APP_ROUTE_ENABLED) return 0;
#if MACHISM_HAS_ROUTE
    if (sample == 0) return 0;
#else
    (void)sample;
#endif
    return 1;
}
/* USER CODE END APP_READY_STEP */

/* USER CODE BEGIN APP_ARM_STEP */
/* reset 后等待新一轮完整反馈，不重复消费启动前的编码器差值。 */
static int app_arm_step(float dt_s)
{
    rcs_feedback_sample sample;
    float command[3] = {0.0f, 0.0f, 0.0f};
    g_arm_ms += (uint32_t)(dt_s * 1000.0f);
    if (!rcs_feedback_port_read(&sample)) {
        if (g_arm_ms >= RCS_APP_ARM_TIMEOUT_MS) app_fault(RCS_APP_ERROR_FEEDBACK);
        else if (!RCS_SetVelocity(0.0f, 0.0f, 0.0f)) app_fault(RCS_APP_ERROR_CHASSIS);
        return 0;
    }
    if (!rcs_nav_update(&g_nav, sample.delta, sample.fresh_mask, dt_s, command) ||
        !rcs_nav_start(&g_nav)) {
        app_fault(RCS_APP_ERROR_NAVIGATION);
        return 0;
    }
    return 1;
}
/* USER CODE END APP_ARM_STEP */

/* USER CODE BEGIN APP_RUN_STEP */
/* 编码器只做里程推算；没有独立观测设备时，不会假称消除了打滑误差。 */
static void app_run_step(float dt_s)
{
    rcs_feedback_sample sample;
    float command[3] = {0.0f, 0.0f, 0.0f};
    if (!rcs_feedback_port_read(&sample)) { app_fault(RCS_APP_ERROR_FEEDBACK); return; }
    if (!rcs_nav_update(&g_nav, sample.delta, sample.fresh_mask, dt_s, command) ||
        g_nav.state == RCS_NAV_FAULT) { app_fault(RCS_APP_ERROR_NAVIGATION); return; }
    if (g_nav.state != RCS_NAV_DONE &&
        !RCS_SetVelocity(command[0], command[1], command[2])) app_fault(RCS_APP_ERROR_CHASSIS);
}
/* USER CODE END APP_RUN_STEP */

/* USER CODE BEGIN APP_FAULT_ENTER */
/* 首次故障后通知。停车/取消由调度器完成，此处可加非阻塞灯光与日志。 */
static void app_on_fault(void) { }
/* USER CODE END APP_FAULT_ENTER */

/* USER CODE BEGIN APP_ON_STATE */
/* 每次状态变化调用一次：灯、蜂鸣、上位机都放这里，禁止阻塞。 */
static void app_on_state(rcs_app_state state)
{
    (void)state;
}
/* USER CODE END APP_ON_STATE */

/* USER CODE BEGIN APP_TICK */
/* 按键扫描、屏幕、日志放这里；每个应用周期调用一次，必须非阻塞。 */
static void app_tick(void)
{
}
/* USER CODE END APP_TICK */

static void app_set_state(rcs_app_state state)
{
    if (g_state != state) { g_state = state; app_on_state(state); }
}

static void app_capture_fault(void)
{
    rcs_fault fault = RCS_Fault();
    if (g_chassis_fault == RCS_OK && fault != RCS_OK) g_chassis_fault = fault;
    if (g_nav_fault == RCS_NAV_OK && g_nav.fault != RCS_NAV_OK) g_nav_fault = g_nav.fault;
    if (!g_machine_fault && Machism_Fault()) g_machine_fault = Machism_Fault();
    if (!g_feedback_fault && rcs_feedback_port_fault() != RCS_FEEDBACK_OK)
        g_feedback_fault = (int)rcs_feedback_port_fault();
}

static void app_service_hold(void)
{
#if RCS_APP_HAS_TERRAIN
    if (g_holding && RCS_State() == RCS_ARMED && RCS_Fault() == RCS_OK &&
        rcs_terrain_request_hold(&g_nav.terrain)) return;
#endif
    app_capture_fault();
    g_holding = 0u;
    RCS_Lock();
}

static void app_stop_route(void *context)
{
    (void)context;
    if (g_route_active) {
        /* 后续路线状态沿用当前里程估计，不能偷偷把车的位置重置到P0。 */
        g_pose_x = g_nav.x_m; g_pose_y = g_nav.y_m; g_pose_yaw = g_nav.yaw_rad;
        g_route_active = 0u; g_arming = 0u; rcs_nav_stop(&g_nav);
    }
#if RCS_APP_HAS_TERRAIN
    if (RCS_State() == RCS_ARMED && RCS_Fault() == RCS_OK &&
        rcs_terrain_request_hold(&g_nav.terrain)) { g_holding = 1u; return; }
#endif
    app_capture_fault();
    g_holding = 0u;
    RCS_Lock();
}

static void app_fault(rcs_app_fault reason)
{
    app_capture_fault();
    if (g_first_fault == RCS_APP_ERROR_NONE) { g_first_fault = reason; g_fault_tick = HAL_GetTick(); }
    if (g_state == RCS_APP_FAULT) return;
    g_auto_consumed = 1u;
    app_set_state(RCS_APP_FAULT);
    /* Machism_Task 内部可能正在调用本函数；取消放在返回调度器之后执行。 */
    app_on_fault();
}

static int app_route_start(void *context)
{
    (void)context;
    if (g_state != RCS_APP_RUNNING || g_route_active || !g_pose_valid) return 0;
    rcs_feedback_port_reset();
    if (rcs_feedback_port_fault() != RCS_FEEDBACK_OK) return 0;
    if (!rcs_nav_reset(&g_nav, g_pose_x, g_pose_y, g_pose_yaw) || !RCS_Arm()) return 0;
    g_arm_ms = 0u; g_route_active = 1u; g_arming = 1u; g_holding = 0u;
    return RCS_SetVelocity(0.0f, 0.0f, 0.0f);
}

static int app_route_tick(void *context, float dt_s)
{
    (void)context;
    if (!g_route_active || g_state == RCS_APP_FAULT) return -1;
    if (g_arming) {
        if (app_arm_step(dt_s)) g_arming = 0u;
        return g_state == RCS_APP_FAULT ? -1 : 0;
    }
    app_run_step(dt_s);
    if (g_state == RCS_APP_FAULT) return -1;
    return g_nav.state == RCS_NAV_DONE ? 1 : 0;
}

static int app_configuration_ok(void)
{
    static const unsigned ids[4] = RCS_APP_MOTOR_IDS;
    static const unsigned chassis_ids[4] = { RCS_MOTOR_ID_0, RCS_MOTOR_ID_1,
        RCS_MOTOR_ID_2, RCS_MOTOR_ID_3 };
    static const int directions[4] = { RCS_DIRECTION_0, RCS_DIRECTION_1,
        RCS_DIRECTION_2, RCS_DIRECTION_3 };
    const rcs_geometry *g = &rcs_route_config.geometry;
    unsigned i;
    if (!RCS_APP_ROUTE_ENABLED || !MACHISM_HAS_ROUTE) return 1;
    if (g->layout != RCS_LAYOUT || RCS_APP_CAN_BUS != RCS_CAN_BUS ||
        fabsf(g->wheel_diameter_m * 1000.0f - (float)RCS_WHEEL_DIAMETER_MM) > 0.01f ||
        fabsf(g->gear_ratio - (float)RCS_GEAR_RATIO) > 0.0001f ||
        fabsf(g->max_motor_rpm - (float)RCS_MAX_MOTOR_RPM) > 0.01f ||
        (RCS_KP == 0.0f && RCS_KI == 0.0f && RCS_KD == 0.0f)) return 0;
    if (g->layout == RCS_MECANUM_X) {
        if (fabsf(g->wheelbase_m * 1000.0f - (float)RCS_WHEELBASE_MM) > 0.01f ||
            fabsf(g->track_m * 1000.0f - (float)RCS_TRACK_MM) > 0.01f) return 0;
    } else if (fabsf(g->omni_radius_m * 1000.0f - (float)RCS_OMNI_RADIUS_MM) > 0.01f ||
        fabsf(g->omni_theta0_rad - (float)RCS_OMNI_THETA0_DEG * 0.0174532925199433f) > 0.0001f) return 0;
    for (i = 0; i < RCS_APP_WHEELS; ++i)
        if (ids[i] != chassis_ids[i] || rcs_route_config.encoder_direction[i] != directions[i]) return 0;
    return 1;
}

int App_task_Init(CAN_HandleTypeDef *can)
{
    machism_ports ports;
    int reinitializing = g_initialized;
    if (g_initialized) { Machism_Stop(); app_stop_route(0); }
    g_initialized = 0u; g_state = RCS_APP_NOT_READY;
    g_first_fault = RCS_APP_ERROR_NONE; g_chassis_fault = RCS_OK;
    g_nav_fault = RCS_NAV_OK; g_machine_fault = g_feedback_fault = 0; g_fault_tick = 0u;
    g_route_active = g_arming = g_holding = g_pose_valid = 0u;
    g_auto_consumed = (uint8_t)(reinitializing != 0);
    RCS_Lock();
    if (!app_configuration_ok() || RCS_APP_PERIOD_MS < RCS_PERIOD_MS || RCS_APP_PERIOD_MS > 100u ||
        RCS_APP_PERIOD_MS >= RCS_COMMAND_TIMEOUT_MS ||
        (float)RCS_APP_PERIOD_MS > rcs_route_config.max_dt_s * 1000.0f) {
        app_fault(RCS_APP_ERROR_CONFIGURATION); return 0;
    }
#if !RCS_NO_CHASSIS
    if (can == 0 || RCS_APP_CAN_BUS != RCS_CAN_BUS) { app_fault(RCS_APP_ERROR_CONFIGURATION); return 0; }
#else
    if (RCS_APP_ROUTE_ENABLED && MACHISM_HAS_ROUTE) { app_fault(RCS_APP_ERROR_CONFIGURATION); return 0; }
#endif
    if (!RCS_Init(can)) { app_fault(RCS_APP_ERROR_INITIALIZATION); return 0; }
#if !RCS_NO_CHASSIS
    if (!rcs_feedback_port_init() || !rcs_can_port_start(can)) {
        app_fault(RCS_APP_ERROR_INITIALIZATION); return 0;
    }
#endif
    if (!rcs_route_init(&g_nav, app_context())) { app_fault(RCS_APP_ERROR_INITIALIZATION); return 0; }
    ports.context = app_context(); ports.route_start = app_route_start;
    ports.route_tick = app_route_tick; ports.route_stop = app_stop_route;
    if (!Machism_Init(&ports)) { app_fault(RCS_APP_ERROR_MACHINE); return 0; }
    g_can = can; g_chassis_tick = HAL_GetTick(); g_app_tick = g_boot_tick = g_chassis_tick;
    g_initialized = 1u; app_set_state(RCS_APP_READY);
    return 1;
}

void App_task_SetStartPose(float x_m, float y_m, float yaw_rad)
{
    if (!isfinite(x_m) || !isfinite(y_m) || !isfinite(yaw_rad)) return;
    g_pose_x = x_m; g_pose_y = y_m; g_pose_yaw = yaw_rad; g_pose_valid = 1u;
}

int App_task_Start(void)
{
    rcs_feedback_sample sample;
    if (!g_initialized || g_state != RCS_APP_READY || !RCS_APP_ROUTE_ENABLED) return 0;
#if MACHISM_HAS_ROUTE
    if (!g_pose_valid) {
        float x_m, y_m, yaw_rad;
        if (!app_start_pose(&x_m, &y_m, &yaw_rad)) return 0;
        App_task_SetStartPose(x_m, y_m, yaw_rad);
    }
    if (!g_pose_valid || !rcs_feedback_port_read(&sample) || !app_ready_ok(&sample)) return 0;
#else
    (void)sample;
    (void)app_start_pose; /* 无路线状态机不需要定位起点。 */
    if (!app_ready_ok(0)) return 0;
#endif
    if (!Machism_Start()) return 0;
    g_auto_consumed = 1u; app_set_state(RCS_APP_RUNNING);
    return 1;
}

/**
 * @brief 刷新只读调试快照；不改变控制状态，也不调用任何阻塞接口
 * @note 每个主循环执行一次，因此 wheel_age_ms 可直接反映反馈分发节奏。
 */
static void app_debug_update(void)
{
    unsigned i;
    g_dbg.app_state = (uint8_t)g_state;
    g_dbg.app_fault = (uint8_t)g_first_fault;
    g_dbg.feedback_fault = (uint8_t)g_feedback_fault;
    g_dbg.chassis_fault = (uint8_t)g_chassis_fault;
    g_dbg.nav_fault = (uint8_t)g_nav_fault;
    g_dbg.machine_fault = (uint8_t)g_machine_fault;
    g_dbg.holding = g_holding ? 1u : 0u;
    g_dbg.ready = g_state == RCS_APP_READY ? 1u : 0u;
    g_dbg.pose_x = g_pose_x;
    g_dbg.pose_y = g_pose_y;
    g_dbg.pose_yaw = g_pose_yaw;
    g_dbg.fault_tick = g_fault_tick;
    g_dbg.feedback_sequence = rcs_feedback_port_sequence();
    g_dbg.rx_count = rcs_feedback_port_rx_count();
    g_dbg.loop_ms = g_loop_ms;
    g_dbg.loop_max_ms = g_loop_max_ms;
    g_dbg.gap_us_last = rcs_feedback_port_gap_us_last();
    g_dbg.gap_us_max = rcs_feedback_port_gap_us_max();
    g_dbg.dwt_ready = (uint8_t)rcs_feedback_port_dwt_ready();
    for (i = 0; i < 4u; ++i) {
        g_dbg.wheel_pending[i] = rcs_feedback_port_pending(i);
        g_dbg.wheel_age_ms[i] = rcs_feedback_port_age_ms(i);
    }
}

void App_task(void)
{
    uint32_t now, elapsed;
    if (!g_initialized) return;
    /* 主循环间隔优先记录在取帧之前：loop_max_ms 大于 RCS_PERIOD_MS 即有阻塞。 */
    now = HAL_GetTick();
    g_loop_ms = g_last_task_tick ? (uint32_t)(now - g_last_task_tick) : 0u;
    if (g_loop_ms > g_loop_max_ms) g_loop_max_ms = g_loop_ms;
    g_last_task_tick = now;
    rcs_can_port_poll(g_can);
    if ((uint32_t)(now - g_chassis_tick) >= RCS_PERIOD_MS) {
        g_chassis_tick = now; RCS_Process();
        if (RCS_State() == RCS_FAULT) app_fault(RCS_APP_ERROR_CHASSIS);
    }
    app_debug_update();
    elapsed = (uint32_t)(now - g_app_tick);
    if (elapsed < RCS_APP_PERIOD_MS) return;
    g_app_tick = now;
    if (g_holding) app_service_hold();
    if (g_state == RCS_APP_FAULT) { Machism_Stop(); app_stop_route(0); return; }
    if (g_state == RCS_APP_STOPPED || g_state == RCS_APP_FINISHED) return;
    app_tick();
    if (g_state == RCS_APP_RUNNING) {
        Machism_Task((float)elapsed * 0.001f);
        if (g_state == RCS_APP_FAULT) { Machism_Stop(); app_stop_route(0); return; }
        if (Machism_State() == MACHISM_FAULT) {
            app_fault(RCS_APP_ERROR_MACHINE); Machism_Stop(); app_stop_route(0);
        } else if (Machism_State() == MACHISM_DONE) {
            app_stop_route(0); app_set_state(RCS_APP_FINISHED);
        }
    } else if (g_state == RCS_APP_READY && !g_auto_consumed && RCS_APP_AUTOSTART &&
               @APP_AUTOSTART_DELAY_CONDITION@) {
        (void)App_task_Start();
    }
}

void App_task_Stop(void)
{
    g_auto_consumed = 1u;
    app_capture_fault(); Machism_Stop(); app_stop_route(0);
    g_pose_valid = 0u;
    if (g_state != RCS_APP_FAULT && g_state != RCS_APP_NOT_READY) app_set_state(RCS_APP_STOPPED);
}

rcs_app_state App_task_State(void) { return g_state; }
rcs_nav_fault App_task_NavFault(void) { return g_nav_fault ? g_nav_fault : g_nav.fault; }
rcs_app_fault App_task_Fault(void) { return g_first_fault; }
rcs_fault App_task_ChassisFault(void) { return g_chassis_fault; }
int App_task_MachineFault(void) { return g_machine_fault; }
int App_task_FeedbackFault(void) { return g_feedback_fault; }
uint32_t App_task_FaultTick(void) { return g_fault_tick; }
const rcs_navigator *App_task_Navigator(void) { return &g_nav; }
int App_task_Holding(void) { return g_holding; }
int App_task_IsReady(void) { return g_state == RCS_APP_READY; }
