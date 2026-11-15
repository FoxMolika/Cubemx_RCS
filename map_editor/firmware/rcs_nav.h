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
/* 地图坐标XY单位米，yaw为逆时针弧度；车体X向前、Y向左。
 * 编码器计数为解码后的有效计数。使用轮轴编码器时须与几何减速比和转速单位一致；
 * 否则先换算为电机端计数。encoder_direction将原始符号转换为逻辑轮方向。 */
typedef struct {
    float x_m, y_m, yaw_rad, speed_mps, dwell_s;
} rcs_waypoint;
typedef struct {
    float distance_m, turn_rad, nominal_time_s;
    float raw_encoder_delta[4]; /* 理想积分参考，不作为停车判据 */
} rcs_segment;
typedef struct {
    rcs_geometry geometry;
    float counts_per_motor_rev;
    int8_t encoder_direction[4];
    float max_speed_mps, max_wz_radps, accel_mps2, alpha_radps2;
    float position_kp, yaw_kp, position_tolerance_m, yaw_tolerance_rad;
    float max_dt_s, max_leg_s;
    uint8_t actions_required; /* 生成路线启动前须绑定动作回调 */
} rcs_nav_config;
typedef enum { RCS_ACTION_ENTER = 0, RCS_ACTION_TICK, RCS_ACTION_CANCEL } rcs_action_phase;
typedef enum { RCS_ACTION_ERROR = -1, RCS_ACTION_BUSY = 0, RCS_ACTION_DONE = 1 } rcs_action_result;
typedef struct {
    uint8_t enabled;
    float timeout_s;
} rcs_action_spec;
/* 回调在任务上下文非阻塞执行，不允许重入导航接口。
 * 停稳后ENTER一次，每个有效周期TICK；活动动作停止/重置/故障时CANCEL一次。
 * 禁止HAL_Delay或等待循环；软件无法抢占阻塞用户代码，必须保留底盘看门狗。 */
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
    RCS_NAV_ACTION_DRIFT,
    RCS_NAV_OBSERVATION_CONFIG,
    RCS_NAV_OBSERVATION,
    RCS_NAV_OBSERVATION_JUMP
} rcs_nav_fault;
/* 阶段引擎使用米、弧度、秒；调用者负责保持配置数组有效。 */
typedef enum {
    RCS_STEP_CUSTOM = 0,
    RCS_STEP_ROTATE_ABSOLUTE = 1,
    RCS_STEP_ROTATE_RELATIVE = 2,
    RCS_STEP_WAIT = 3
} rcs_step_kind;
typedef struct {
    rcs_step_kind kind;
    float value, timeout_s, max_wz_radps, alpha_radps2, tolerance_rad;
    float settle_s, drift_limit_m;
    int8_t direction;
    size_t custom_index;
} rcs_step;
typedef struct {
    const rcs_step *arrival;
    size_t arrival_count;
    const rcs_step *departure;
    size_t departure_count;
    float arrival_radius_m, correction_speed_mps, correction_timeout_s;
    float settle_s, drift_limit_m;
    uint8_t correction_mode, correction_retries, departure_at_end;
} rcs_point_plan;
typedef enum {
    RCS_STAGE_MOVE = 0,
    RCS_STAGE_SETTLE,
    RCS_STAGE_ARRIVAL,
    RCS_STAGE_CORRECT_XY,
    RCS_STAGE_CORRECT_YAW,
    RCS_STAGE_DEPARTURE,
    RCS_STAGE_EXIT,
    RCS_STAGE_CORRECTION
} rcs_nav_stage;
/* 独立定位适配器契约：米/弧度，地图坐标系，坐标原点已换算到车体中心。
 * axes: XY=3，朝向=4，两者=7。未观测轴填0。quality为0～1。
 * sequence每次真实采样递增，允许uint32回绕；age_s为采样到当前的年龄。
 * 驱动电机编码器不能冒充独立观测。安装偏移/对齐由真实驱动适配器完成。 */
typedef struct {
    float x_m, y_m, yaw_rad, age_s, quality;
    uint32_t sequence;
    uint8_t axes;
} rcs_observation;
typedef int (*rcs_observer_fn)(void *context, unsigned source, rcs_observation *sample);
/* kind: 0仅刷新定位估计；1驱动XY对齐；2原地朝向对齐。 */
typedef struct {
    uint8_t source, axes, kind;
    float timeout_s, max_age_s, min_quality, max_jump_m, max_jump_rad;
    float speed_mps, max_wz_radps, position_tolerance_m, yaw_tolerance_rad, settle_s;
} rcs_correction;
typedef struct {
    const rcs_correction *steps;
    size_t count;
} rcs_correction_group;

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
    const rcs_point_plan *plans;
    rcs_nav_stage stage, fault_stage;
    size_t action_index, fault_action;
    rcs_step current_step;
    float stage_s, settled_s, heading_target, yaw_unwrapped;
    float spin_target, anchor_x, anchor_y;
    uint8_t correction_pass;
    const rcs_correction_group *corrections;
    rcs_observer_fn observer;
    void *observer_context;
    uint32_t observation_seen, observation_sequence[32];
    float observation_age[32];
    float correction_origin_x, correction_origin_y;
    uint8_t correction_started;

} rcs_navigator;
/* 不使用堆，不调用HAL，不启动电机；接口单任务串行调用，航点表须保持有效。 */
/**
 * @brief 初始化导航参数，不启动电机
 * @note 仅由导航任务串行调用，单位和参数约束见类型定义。
 */
int rcs_nav_init(rcs_navigator *n, const rcs_nav_config *cfg, const rcs_waypoint *points, size_t count);
/* 仅空闲时绑定，数量须匹配航点表。生成路线使用rcs_route_init自动绑定。
 * 再次初始化前先停止旧导航实例；init不能检查尚未初始化的内存。 */
/**
 * @brief 绑定航点动作，只允许在空闲状态调用
 * @note 仅由导航任务串行调用，单位和参数约束见类型定义。
 */
int rcs_nav_bind_actions(rcs_navigator *n, const rcs_action_spec *specs, size_t count, rcs_action_fn callback,
                         void *context);
/* 流程表替代隐式航点转向；回调接收custom_index，不是航点下标。
 * 旋转由阶段引擎执行，用户机构回调不得混入运动。 */
int rcs_nav_bind_plan(rcs_navigator *n, const rcs_point_plan *plans, size_t count, rcs_action_fn callback,
                      void *context);
/**
 * @brief 绑定逐航点显式纠错模板和真实定位读取回调，只允许空闲时调用
 * @note 绑定空组后直接跳过纠错；旧correction_mode不再参与该路线执行。
 */
int rcs_nav_bind_corrections(rcs_navigator *n, const rcs_correction_group *groups,
                             size_t count, rcs_observer_fn observer, void *context);
/* 先在真实已知位置摆放或定位车辆，丢弃重置之前的编码器增量。
 * 重置清除故障，启动前仍需下一次新鲜反馈；不能把重置当作独立定位测量。 */
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
/* delta为本次dt_s内W0～W3的原始有符号增量。fresh_mask须表示真实新采样，
 * 即使增量为零也必须有新鲜度依据；调用者检查年龄、序号和实际周期，禁止伪造。
 * 任一使用轮缺反馈将锁定故障。每次调用先清零输出；空闲/运行时都须周期调用。
 * 返回0时调用者须主动下发零命令或锁定底盘；漏调用不能停车，必须保留看门狗。 */
/**
 * @brief 消费本周期原始编码器增量，更新里程计并输出车体速度
 * @note 仅由导航任务串行调用，单位和参数约束见类型定义。
 */
int rcs_nav_update(rcs_navigator *n, const int32_t delta[4], uint8_t fresh_mask, float dt_s,
                   float body_command[3]);
#ifdef __cplusplus
}
#endif
