/**
 * @file rcs_nav.h
 * @brief 编码器里程计、逐航点导航与非阻塞机构动作
 * @note 位置为米，角度为弧度；保持底盘看门狗有效，回调不得阻塞。
 */
#pragma once
#include "rcs_chassis.h"
#include <stddef.h>
#ifdef __cplusplus
extern "C" {
#endif
/* Map frame x/y in meters, yaw CCW radians; body x forward/y left.
 * counts_per_motor_rev is EFFECTIVE decoded counts (after quadrature).
 * For an encoder on the wheel shaft use gear_ratio=1 ONLY if geometry also
 * uses wheel-shaft RPM. Otherwise convert wheel counts to motor counts first.
 * encoder_direction maps raw encoder sign to logical chassis wheel sign. */
typedef struct {
    float x_m, y_m, yaw_rad, speed_mps, dwell_s;
} rcs_waypoint;
typedef struct {
    float distance_m, turn_rad, nominal_time_s;
    float raw_encoder_delta[4]; /* ideal integrated reference, NOT stop criteria */
} rcs_segment;
typedef struct {
    rcs_geometry geometry;
    float counts_per_motor_rev;
    int8_t encoder_direction[4];
    float max_speed_mps, max_wz_radps, accel_mps2, alpha_radps2;
    float position_kp, yaw_kp, position_tolerance_m, yaw_tolerance_rad;
    float max_dt_s, max_leg_s;
    uint8_t actions_required; /* generated route must bind callbacks before Start */
} rcs_nav_config;
typedef enum { RCS_ACTION_ENTER = 0, RCS_ACTION_TICK, RCS_ACTION_CANCEL } rcs_action_phase;
typedef enum { RCS_ACTION_ERROR = -1, RCS_ACTION_BUSY = 0, RCS_ACTION_DONE = 1 } rcs_action_result;
typedef struct {
    uint8_t enabled;
    float timeout_s;
} rcs_action_spec;
/* Task-context, non-blocking, no reentrant navigator calls. Return promptly.
 * ENTER once after pose+speed settle; TICK each valid cycle; CANCEL once on
 * active-action Stop/Reset/Fault. A callback must not use HAL_Delay or wait loops.
 * Software cannot preempt blocking user code; retain the chassis watchdog. */
typedef rcs_action_result (*rcs_action_fn)(void *context, size_t point, rcs_action_phase phase,
                                           float elapsed_s);
typedef enum { RCS_NAV_IDLE = 0, RCS_NAV_RUNNING, RCS_NAV_DONE, RCS_NAV_FAULT } rcs_nav_state;
typedef enum {
    RCS_NAV_OK = 0,
    RCS_NAV_CONFIG,
    RCS_NAV_POSE,
    RCS_NAV_FEEDBACK,
    RCS_NAV_TIMING,
    RCS_NAV_ENCODER_RANGE,
    RCS_NAV_TIMEOUT,
    RCS_NAV_ACTION_CONFIG,
    RCS_NAV_ACTION_FAILED,
    RCS_NAV_ACTION_TIMEOUT,
    RCS_NAV_ACTION_DRIFT
} rcs_nav_fault;
typedef struct {
    rcs_nav_config cfg;
    const rcs_waypoint *points;
    size_t count, target;
    float x_m, y_m, yaw_rad, command_world[3], leg_s, dwell_s;
    rcs_nav_state state;
    rcs_nav_fault fault;
    uint8_t pose_set, feedback_seen, initialized;
    const rcs_action_spec *actions;
    rcs_action_fn action;
    void *action_context;
    float action_s;
    uint8_t action_active, action_done;
} rcs_navigator;
/* No heap, no HAL calls, no motor activation, single-task API. Keep points alive. */
/**
 * @brief 初始化导航参数，不启动电机
 * @note 仅由导航任务串行调用，单位和参数约束见类型定义。
 */
int rcs_nav_init(rcs_navigator *n, const rcs_nav_config *cfg, const rcs_waypoint *points, size_t count);
/* Bind while IDLE only, specs/count must match the live waypoint table.
 * Use the generated rcs_route_init() helper to bind automatically. Stop before
 * reinitializing an existing navigator; init cannot inspect uninitialized memory. */
/**
 * @brief 绑定航点动作，只允许在空闲状态调用
 * @note 仅由导航任务串行调用，单位和参数约束见类型定义。
 */
int rcs_nav_bind_actions(rcs_navigator *n, const rcs_action_spec *specs, size_t count, rcs_action_fn callback,
                         void *context);
/* Physically place/locate the car first. Discard any encoder deltas before reset.
 * Reset clears fault and requires a NEW fresh feedback update before start. */
/**
 * @brief 按外部定位重置坐标，并要求下一周期取得新鲜反馈
 * @note 仅由导航任务串行调用，单位和参数约束见类型定义。
 */
int rcs_nav_reset(rcs_navigator *n, float x_m, float y_m, float yaw_rad);
/**
 * @brief 在起点定位和反馈有效后启动导航
 * @note 仅由导航任务串行调用，单位和参数约束见类型定义。
 */
int rcs_nav_start(rcs_navigator *n);
/**
 * @brief 停止导航并取消活动动作，下次启动前需要重新定位
 * @note 仅由导航任务串行调用，单位和参数约束见类型定义。
 */
void rcs_nav_stop(rcs_navigator *n);
/* delta: RAW signed incremental counts over exactly dt_s, W0..W3.
 * fresh_mask: each in-use bit MUST indicate a NEW measurement, even when zero.
 * Caller must validate sample AGE/sequence and supply true elapsed time, never
 * manufacture fresh bits for stale/replayed samples. Any missing wheel faults.
 * Always clears body_command first. Call periodically in IDLE and RUNNING.
 * Returns 0 on latched fault. Caller must actively send zeros/lock on 0.
 * A missed call cannot stop hardware: keep the chassis command watchdog active. */
/**
 * @brief 消费本周期原始编码器增量，更新里程计并输出车体速度
 * @note 仅由导航任务串行调用，单位和参数约束见类型定义。
 */
int rcs_nav_update(rcs_navigator *n, const int32_t delta[4], uint8_t fresh_mask, float dt_s,
                   float body_command[3]);
#ifdef __cplusplus
}
#endif
