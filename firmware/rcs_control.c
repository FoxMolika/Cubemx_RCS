/**
 * @file rcs_control.c
 * @brief 底盘速度环、超时与锁定状态机
 *
 * 目标转速来自运动学解算，反馈来自 C620。PID 的微分项作用在测量值上，
 * 换命令或重新解锁不会产生电流尖峰。所有时间参数单位为毫秒。
 */
#include "rcs_control.h"

#include <math.h>
#include <string.h>

/* 私有函数 ------------------------------------------------------------------------------------------*/

/**
 * @brief 把数值限制在 ±limit 内
 * @param value 输入值
 * @param limit 绝对值上限
 * @return 限制后的值
 */
static float clamp(float value, float limit)
{
    if (value > limit)
        return limit;
    if (value < -limit)
        return -limit;
    return value;
}

/**
 * @brief 停止输出并复位控制器
 * @param controller 控制器实例
 * @param state 目标状态
 * @param fault 故障原因
 */
static void stop(rcs_controller *controller, rcs_state state, rcs_fault fault)
{
    controller->state = state;
    controller->fault = fault;
    controller->command_seen = 0;
    memset(controller->target_rpm, 0, sizeof(controller->target_rpm));
    memset(controller->integral, 0, sizeof(controller->integral));
    memcpy(controller->previous_feedback, controller->feedback_rpm, sizeof(controller->previous_feedback));
}

/**
 * @brief 检查所有在用轮的反馈是否新鲜
 * @param controller 控制器实例
 * @param now 当前毫秒时间戳
 * @return 1 新鲜，0 有轮子缺反馈或超时
 */
static int feedback_fresh(const rcs_controller *controller, uint32_t now)
{
    for (unsigned i = 0; i < rcs_wheel_count(&controller->cfg.geometry); i++) {
        if (!controller->feedback_seen[i])
            return 0;

        if ((uint32_t)(now - controller->feedback_ms[i]) >= controller->cfg.feedback_timeout_ms)
            return 0;
    }

    return 1;
}

/* 导出函数 ------------------------------------------------------------------------------------------*/

/**
 * @brief 初始化控制器
 * @param controller 控制器实例
 * @param config 配置
 * @param send CAN 发送函数
 * @param context 发送上下文
 * @return 1 成功，0 配置或参数非法
 */
int rcs_control_init(rcs_controller *controller, const rcs_control_config *config, rcs_send_fn send,
                     void *context)
{
    if (!controller)
        return 0;

    memset(controller, 0, sizeof(*controller));
    controller->fault = RCS_BAD_CONFIG;

    if (!config || !send || !rcs_geometry_valid(&config->geometry))
        return 0;

    if (!isfinite(config->kp) || !isfinite(config->ki) || !isfinite(config->kd))
        return 0;

    if (config->kp < 0.0f || config->ki < 0.0f || config->kd < 0.0f)
        return 0;

    if (!isfinite(config->current_limit_raw) || config->current_limit_raw <= 0.0f ||
        config->current_limit_raw > 16384.0f)
        return 0;

    if (config->period_ms < 1 || config->period_ms > 100)
        return 0;

    if (config->command_timeout_ms <= config->period_ms || config->command_timeout_ms > 60000)
        return 0;

    if (config->feedback_timeout_ms <= config->period_ms || config->feedback_timeout_ms > 60000)
        return 0;

    unsigned used_ids = 0;

    for (unsigned i = 0; i < rcs_wheel_count(&config->geometry); i++) {
        unsigned id = config->motor_id[i];

        if (id < 1 || id > 4)
            return 0;

        if (used_ids & (1u << id))
            return 0;

        if (config->direction[i] != 1 && config->direction[i] != -1)
            return 0;

        used_ids |= 1u << id;
    }

    controller->cfg = *config;
    controller->send = send;
    controller->send_context = context;
    controller->fault = RCS_OK;
    return 1;
}

/**
 * @brief 提交一帧电机反馈
 * @param controller 控制器实例
 * @param id 反馈帧 ID，0x201..0x204
 * @param data 8 字节数据
 * @param dlc 数据长度，必须为 8
 * @param now 当前毫秒时间戳
 */
void rcs_control_feedback(rcs_controller *controller, uint16_t id, const uint8_t data[8], uint8_t dlc,
                          uint32_t now)
{
    if (!controller || !controller->send || !data || dlc != 8)
        return;

    for (unsigned i = 0; i < rcs_wheel_count(&controller->cfg.geometry); i++) {
        if (id != 0x200u + controller->cfg.motor_id[i])
            continue;

        uint16_t raw = ((uint16_t)data[2] << 8) | data[3];
        int32_t signed_rpm = raw < 32768u ? (int32_t)raw : (int32_t)raw - 65536;
        controller->feedback_rpm[i] = (float)signed_rpm * (float)controller->cfg.direction[i];
        controller->feedback_ms[i] = now;
        controller->feedback_seen[i] = 1;
        return;
    }
}

/**
 * @brief 解锁控制器
 *
 * LOCKED 和 FAULT 状态都可调用。成功会清除故障，但要求所有在用轮反馈新鲜；
 * 解锁后仍必须下达新命令才会输出。
 *
 * @param controller 控制器实例
 * @param now 当前毫秒时间戳
 * @return 1 成功，0 反馈不新鲜或参数非法
 */
int rcs_control_arm(rcs_controller *controller, uint32_t now)
{
    if (!controller || !controller->send)
        return 0;

    if (controller->state == RCS_ARMED)
        return 1;

    if (!feedback_fresh(controller, now)) {
        stop(controller, RCS_LOCKED, RCS_NO_FEEDBACK);
        return 0;
    }

    /* 每次解锁都要求一条新命令，旧命令不能复活。 */
    stop(controller, RCS_ARMED, RCS_OK);
    controller->command_ms = now;
    controller->last_step_ms = now;
    controller->step_seen = 0;
    return 1;
}

/**
 * @brief 锁定控制器并清除旧命令
 * @param controller 控制器实例
 */
void rcs_control_lock(rcs_controller *controller)
{
    if (controller)
        stop(controller, RCS_LOCKED, RCS_OK);
}

/**
 * @brief 下达车体速度命令
 * @param controller 控制器实例
 * @param vx 前进速度，m/s
 * @param vy 左移速度，m/s
 * @param wz 逆时针角速度，rad/s
 * @param now 当前毫秒时间戳
 * @return 1 成功，0 未解锁或命令非法
 */
int rcs_control_command(rcs_controller *controller, float vx, float vy, float wz, uint32_t now)
{
    if (!controller || !controller->send || controller->state != RCS_ARMED)
        return 0;

    if (!rcs_inverse(&controller->cfg.geometry, vx, vy, wz, controller->target_rpm)) {
        stop(controller, RCS_FAULT, RCS_BAD_COMMAND);
        return 0;
    }

    controller->command_seen = 1;
    controller->command_ms = now;
    return 1;
}

/**
 * @brief 执行一次控制周期
 *
 * 每个配置周期调用一次，锁定期间也要持续调用，以便发出零电流。
 * 微分项作用在测量值上，换命令和解锁不会产生电流尖峰。
 *
 * @param controller 控制器实例
 * @param now 当前毫秒时间戳
 */
void rcs_control_step(rcs_controller *controller, uint32_t now)
{
    uint8_t frame[8] = {0};
    float dt;

    if (!controller || !controller->send)
        return;

    uint32_t elapsed = (uint32_t)(now - controller->last_step_ms);
    if (controller->step_seen && elapsed < controller->cfg.period_ms)
        return;

    dt = (float)(controller->step_seen ? elapsed : controller->cfg.period_ms) * 0.001f;

    if (controller->state == RCS_ARMED) {
        if (controller->step_seen && elapsed > controller->cfg.period_ms * 3u) {
            stop(controller, RCS_FAULT, RCS_BAD_TIMING);
        } else if (!feedback_fresh(controller, now)) {
            stop(controller, RCS_FAULT, RCS_FEEDBACK_TIMEOUT);
        } else if ((uint32_t)(now - controller->command_ms) >= controller->cfg.command_timeout_ms) {
            stop(controller, RCS_FAULT, RCS_COMMAND_TIMEOUT);
        }
    }

    controller->last_step_ms = now;
    controller->step_seen = 1;

    if (controller->state == RCS_ARMED && controller->command_seen) {
        for (unsigned i = 0; i < rcs_wheel_count(&controller->cfg.geometry); i++) {
            float error = controller->target_rpm[i] - controller->feedback_rpm[i];
            float integral = clamp(controller->integral[i] + controller->cfg.ki * error * dt,
                                   controller->cfg.current_limit_raw);
            float output =
                controller->cfg.kp * error + integral -
                controller->cfg.kd * (controller->feedback_rpm[i] - controller->previous_feedback[i]) / dt;

            if (!isfinite(output)) {
                stop(controller, RCS_FAULT, RCS_BAD_COMMAND);
                memset(frame, 0, 8);
                break;
            }

            /* 条件积分：只在不会加深饱和时接受积分项。 */
            if (fabsf(output) <= controller->cfg.current_limit_raw || (output > 0.0f && error < 0.0f) ||
                (output < 0.0f && error > 0.0f))
                controller->integral[i] = integral;

            int16_t raw = (int16_t)(clamp(output, controller->cfg.current_limit_raw) *
                                    (float)controller->cfg.direction[i]);
            unsigned slot = 2u * (controller->cfg.motor_id[i] - 1u);
            frame[slot] = (uint8_t)((uint16_t)raw >> 8);
            frame[slot + 1] = (uint8_t)raw;
        }

        memcpy(controller->previous_feedback, controller->feedback_rpm,
               sizeof(controller->previous_feedback));
    }

    if (!controller->send(controller->send_context, 0x200u, frame)) {
        stop(controller, RCS_FAULT, RCS_TX_FAILED);
        memset(frame, 0, 8);
        (void)controller->send(controller->send_context, 0x200u, frame);
    }
}
