/** @file rcs_nav.c
 * @brief 航点阶段引擎：行驶、停稳、到达动作、显式纠错、离开动作。
 * @note 单任务非阻塞，不调用HAL；底盘反馈看门狗仍由调用者维护。
 */
#include "rcs_nav.h"
#include <math.h>
#include <string.h>
#define PI 3.14159265358979323846f
/**
 * @brief 将角度归一化到最短等价区间
 */
static float wrap(float a)
{
    return remainderf(a, 2.0f * PI);
}
/**
 * @brief 将数值限制在闭区间内
 */
static float clamp(float v, float lo, float hi)
{
    return fminf(hi, fmaxf(lo, v));
}
/**
 * @brief 检查输入为正有限数
 */
static int positive(float v)
{
    return isfinite(v) && v > 0;
}
/**
 * @brief 清零三轴命令
 */
static void zeros(float *p)
{
    if (p)
        p[0] = p[1] = p[2] = 0;
}
/**
 * @brief 取消当前活动动作，避免重复清理
 */
static void cancel_action(rcs_navigator *n)
{
    if (!n || !n->action_active)
        return;
    n->action_active = 0;
    if (n->current_step.kind == RCS_STEP_CUSTOM && n->action)
        (void)n->action(n->action_context, n->current_step.custom_index, RCS_ACTION_CANCEL, n->action_s);
}
/**
 * @brief 锁定首发故障、清零命令并取消活动动作
 */
static int fail(rcs_navigator *n, rcs_nav_fault f)
{
    if (n) {
        if (n->state != RCS_NAV_FAULT) {
            n->fault = f;
            n->fault_stage = n->stage;
            n->fault_action = n->action_index;
        }
        n->state = RCS_NAV_FAULT;
        n->feedback_seen = 0;
        zeros(n->command_world);
        cancel_action(n);
    }
    return 0;
}
/**
 * @brief 校验并初始化导航，不启动车辆
 */
int rcs_nav_init(rcs_navigator *n, const rcs_nav_config *c, const rcs_waypoint *p, size_t count)
{
    if (!n)
        return 0;
    memset(n, 0, sizeof(*n));
    if (!c || !p || count < 2u || count > 512u || !rcs_geometry_valid(&c->geometry) ||
        !positive(c->counts_per_motor_rev) || c->counts_per_motor_rev > 10000000.0f ||
        !positive(c->max_speed_mps) || !positive(c->max_wz_radps) || !positive(c->accel_mps2) ||
        !positive(c->alpha_radps2) || !positive(c->position_kp) || !positive(c->yaw_kp) ||
        !positive(c->position_tolerance_m) || !positive(c->yaw_tolerance_rad) || c->yaw_tolerance_rad > PI ||
        !positive(c->max_dt_s) || c->max_dt_s > 0.25f || !positive(c->max_leg_s) || c->actions_required > 1u)
        return fail(n, RCS_NAV_CONFIG);
    unsigned wheels = rcs_wheel_count(&c->geometry);
    if (wheels < 3u || wheels > 4u)
        return fail(n, RCS_NAV_CONFIG);
    for (unsigned i = 0; i < wheels; i++)
        if (c->encoder_direction[i] != 1 && c->encoder_direction[i] != -1)
            return fail(n, RCS_NAV_CONFIG);
    for (size_t i = 0; i < count; i++)
        if (!isfinite(p[i].x_m) || !isfinite(p[i].y_m) || fabsf(p[i].x_m) > 1000.0f ||
            fabsf(p[i].y_m) > 1000.0f || !isfinite(p[i].yaw_rad) || fabsf(p[i].yaw_rad) > 2.0f * PI ||
            !positive(p[i].speed_mps) || p[i].speed_mps > c->max_speed_mps || !isfinite(p[i].dwell_s) ||
            p[i].dwell_s < 0.0f || p[i].dwell_s > 3600.0f)
            return fail(n, RCS_NAV_CONFIG);
    n->cfg = *c;
    n->points = p;
    n->count = count;
    n->state = RCS_NAV_IDLE;
    n->initialized = 1;
    return 1;
}

/**
 * @brief 切换阶段并清除上阶段的运动输出
 */
static void stage(rcs_navigator *n, rcs_nav_stage value)
{
    n->stage = value;
    if (value == RCS_STAGE_DEPARTURE) {
        n->anchor_x = n->x_m;
        n->anchor_y = n->y_m;
    }
    n->stage_s = 0;
    n->correction_started = 0;
    n->settled_s = 0;
    n->action_index = 0;
    n->action_active = 0;
    n->action_s = 0;
    zeros(n->command_world);
}
/**
 * @brief 取得当前航点的阶段配置
 */
static rcs_point_plan plan_for(const rcs_navigator *n)
{
    rcs_point_plan p = {0};
    if (n->plans)
        return n->plans[n->target];
    p.arrival_radius_m = n->cfg.position_tolerance_m;
    p.correction_speed_mps = fminf(n->cfg.max_speed_mps, .1f);
    p.correction_timeout_s = n->cfg.max_leg_s;
    p.settle_s = .05f;
    p.drift_limit_m = fmaxf(.05f, 2 * n->cfg.position_tolerance_m);
    p.correction_mode = 0;
    p.correction_retries = 2;
    p.departure_at_end = 1;
    return p;
}
/**
 * @brief 校验单个动作的类型与参数
 */
static int valid_step(const rcs_step *a)
{
    if (a->kind < RCS_STEP_CUSTOM || a->kind > RCS_STEP_WAIT || !isfinite(a->value) ||
        !positive(a->timeout_s) || a->timeout_s > 3600 || !positive(a->settle_s) ||
        !positive(a->drift_limit_m) || a->direction < -1 || a->direction > 1)
        return 0;
    if (a->kind == RCS_STEP_WAIT)
        return a->value >= 0 && a->value <= a->timeout_s;
    if (a->kind == RCS_STEP_ROTATE_ABSOLUTE || a->kind == RCS_STEP_ROTATE_RELATIVE)
        return fabsf(a->value) <= 20 * PI && positive(a->max_wz_radps) && positive(a->alpha_radps2) &&
               positive(a->tolerance_rad) && a->tolerance_rad <= PI;
    return 1;
}
/**
 * @brief 绑定两组动作与纠偏配置，仅允许空闲状态调用
 */
int rcs_nav_bind_plan(rcs_navigator *n, const rcs_point_plan *p, size_t count, rcs_action_fn fn, void *ctx)
{
    if (!n || !n->initialized || n->state != RCS_NAV_IDLE)
        return 0;
    if (!p || count != n->count)
        return fail(n, RCS_NAV_ACTION_CONFIG);
    for (size_t i = 0; i < count; i++) {
        const rcs_point_plan *q = p + i;
        if (q->arrival_count > 64 || q->departure_count > 64 || (q->arrival_count && !q->arrival) ||
            (q->departure_count && !q->departure) || !positive(q->arrival_radius_m) ||
            q->arrival_radius_m < n->cfg.position_tolerance_m || !positive(q->correction_speed_mps) ||
            q->correction_speed_mps > n->cfg.max_speed_mps || !positive(q->correction_timeout_s) ||
            !positive(q->settle_s) || !positive(q->drift_limit_m) || q->correction_mode > 3 ||
            q->correction_retries > 10 || q->departure_at_end > 1)
            return fail(n, RCS_NAV_ACTION_CONFIG);
        for (unsigned g = 0; g < 2; g++) {
            const rcs_step *a = g ? q->departure : q->arrival;
            size_t size = g ? q->departure_count : q->arrival_count;
            for (size_t j = 0; j < size; j++)
                if (!valid_step(a + j) || (a[j].kind == RCS_STEP_CUSTOM && !fn))
                    return fail(n, RCS_NAV_ACTION_CONFIG);
        }
    }
    n->plans = p;
    n->action = fn;
    n->action_context = ctx;
    return 1;
}
/**
 * @brief 绑定旧格式动作，以独立阶段执行
 */
int rcs_nav_bind_actions(rcs_navigator *n, const rcs_action_spec *a, size_t count, rcs_action_fn fn,
                         void *ctx)
{
    if (!n || !n->initialized || n->state != RCS_NAV_IDLE)
        return 0;
    if (!a || count != n->count)
        return fail(n, RCS_NAV_ACTION_CONFIG);
    unsigned enabled = 0;
    for (size_t i = 0; i < count; i++) {
        if (a[i].enabled > 1 || !isfinite(a[i].timeout_s) || a[i].timeout_s < 0 || a[i].timeout_s > 3600 ||
            (a[i].enabled && !positive(a[i].timeout_s)))
            return fail(n, RCS_NAV_ACTION_CONFIG);
        enabled += a[i].enabled;
    }
    if (enabled && !fn)
        return fail(n, RCS_NAV_ACTION_CONFIG);
    n->actions = a;
    n->action = fn;
    n->action_context = ctx;
    return 1;
}
/**
 * @brief 按外部定位重置导航，要求新反馈后才能启动
 */
int rcs_nav_reset(rcs_navigator *n, float x, float y, float yaw)
{
    if (!n || !n->initialized)
        return 0;
    if (!isfinite(x) || !isfinite(y) || !isfinite(yaw) || fabsf(x) > 1000 || fabsf(y) > 1000 ||
        fabsf(yaw) > 2 * PI)
        return fail(n, RCS_NAV_POSE);
    cancel_action(n);
    n->x_m = x;
    n->y_m = y;
    n->yaw_rad = wrap(yaw);
    n->yaw_unwrapped = n->yaw_rad;
    n->heading_target = n->yaw_rad;
    n->target = 0;
    n->pose_set = 1;
    n->observation_seen = 0;
    n->feedback_seen = 0;
    n->state = RCS_NAV_IDLE;
    n->fault = RCS_NAV_OK;
    n->correction_pass = 0;
    n->leg_s = n->dwell_s = 0;
    stage(n, RCS_STAGE_MOVE);
    return 1;
}
/**
 * @brief 在定位与新反馈有效后显式启动任务
 */
int rcs_nav_start(rcs_navigator *n)
{
    if (!n || !n->initialized || n->state != RCS_NAV_IDLE || !n->pose_set || !n->feedback_seen)
        return 0;
    if (n->cfg.actions_required && !n->plans && (!n->actions || !n->action))
        return fail(n, RCS_NAV_ACTION_CONFIG);
    if (hypotf(n->points[0].x_m - n->x_m, n->points[0].y_m - n->y_m) > n->cfg.position_tolerance_m ||
        fabsf(wrap(n->points[0].yaw_rad - n->yaw_rad)) > n->cfg.yaw_tolerance_rad)
        return fail(n, RCS_NAV_POSE);
    n->state = RCS_NAV_RUNNING;
    n->target = 0;
    n->heading_target = n->yaw_rad;
    stage(n, RCS_STAGE_MOVE);
    return 1;
}
/**
 * @brief 停止任务、取消动作并保留已有故障
 */
void rcs_nav_stop(rcs_navigator *n)
{
    if (!n)
        return;
    cancel_action(n);
    zeros(n->command_world);
    n->pose_set = n->feedback_seen = 0;
    if (n->state != RCS_NAV_FAULT)
        n->state = RCS_NAV_IDLE;
}
/**
 * @brief 检查实测平移与旋转速度已停稳
 */
static int stopped(const float vel[3])
{
    return hypotf(vel[0], vel[1]) < .03f && fabsf(vel[2]) < .05f;
}
/**
 * @brief 累计连续满足条件的停稳时间
 */
static int settle(rcs_navigator *n, int condition, float dt, float duration)
{
    n->settled_s = condition ? n->settled_s + dt : 0;
    return n->settled_s + 1e-6f >= duration;
}
/**
 * @brief 执行独立平移并限制平移加速度
 */
static void translate(rcs_navigator *n, float dx, float dy, float speed, float dt)
{
    float distance = hypotf(dx, dy), vx = 0, vy = 0;
    if (distance > n->cfg.position_tolerance_m) {
        speed = fminf(speed, fminf(n->cfg.position_kp * distance, sqrtf(2 * n->cfg.accel_mps2 * distance)));
        vx = dx / distance * speed;
        vy = dy / distance * speed;
    }
    float ax = vx - n->command_world[0], ay = vy - n->command_world[1], norm = hypotf(ax, ay),
          lim = n->cfg.accel_mps2 * dt;
    if (norm > lim) {
        ax *= lim / norm;
        ay *= lim / norm;
    }
    n->command_world[0] += ax;
    n->command_world[1] += ay;
    n->command_world[2] = 0;
}
/**
 * @brief 执行独立旋转并等待角度与实测速度收敛
 */
static int spin(rcs_navigator *n, const rcs_step *a, const float vel[3], float dt)
{
    float error = n->spin_target - n->yaw_unwrapped;
    float w = fabsf(error) <= a->tolerance_rad
                  ? 0
                  : clamp(n->cfg.yaw_kp * error, -a->max_wz_radps, a->max_wz_radps);
    n->command_world[0] = n->command_world[1] = 0;
    n->command_world[2] += clamp(w - n->command_world[2], -a->alpha_radps2 * dt, a->alpha_radps2 * dt);
    return settle(n, fabsf(error) <= a->tolerance_rad && fabsf(n->command_world[2]) < 1e-5f && stopped(vel),
                  dt, a->settle_s);
}
/**
 * @brief 锁存一次旋转目标与漂移基准
 */
static void begin_spin(rcs_navigator *n, const rcs_step *a)
{
    float delta;
    if (a->kind == RCS_STEP_ROTATE_RELATIVE) {
        delta = a->value;
        if (a->direction > 0)
            delta = fabsf(delta);
        if (a->direction < 0)
            delta = -fabsf(delta);
    } else {
        delta = wrap(a->value - n->yaw_rad);
        if (a->direction == 0 && fabsf(fabsf(delta) - PI) < 1e-6f)
            delta = PI;
        if (a->direction > 0 && delta < 0)
            delta += 2 * PI;
        if (a->direction < 0 && delta > 0)
            delta -= 2 * PI;
    }
    n->spin_target = n->yaw_unwrapped + delta;
    n->anchor_x = n->x_m;
    n->anchor_y = n->y_m;
    n->settled_s = 0;
}
/**
 * @brief 将旧动作描述转换为独立阶段动作
 */
static rcs_step legacy_step(const rcs_navigator *n, int arrival)
{
    rcs_step a = {0};
    a.kind = arrival ? RCS_STEP_ROTATE_ABSOLUTE : RCS_STEP_CUSTOM;
    a.value = n->points[n->target].yaw_rad;
    a.timeout_s = arrival ? n->cfg.max_leg_s : n->actions[n->target].timeout_s;
    a.max_wz_radps = n->cfg.max_wz_radps;
    a.alpha_radps2 = n->cfg.alpha_radps2;
    a.tolerance_rad = n->cfg.yaw_tolerance_rad;
    a.settle_s = .05f;
    a.drift_limit_m = fmaxf(.05f, 2 * n->cfg.position_tolerance_m);
    a.custom_index = n->target;
    return a;
}
/**
 * @brief 按顺序执行到达后或起步前动作组
 */
static int run_group(rcs_navigator *n, const rcs_point_plan *p, const float vel[3], float dt)
{
    int arrival = n->stage == RCS_STAGE_ARRIVAL;
    const rcs_step *list = arrival ? p->arrival : p->departure;
    size_t count = arrival ? p->arrival_count : p->departure_count;
    if (!n->plans)
        count = arrival ? (n->target ? 1u : 0u) : ((n->actions && n->actions[n->target].enabled) ? 1u : 0u);
    if (n->action_index >= count)
        return 1;
    int complete = 0;
    if (!n->action_active) {
        n->current_step = n->plans ? list[n->action_index] : legacy_step(n, arrival);
        n->action_active = 1;
        n->action_s = 0;
        n->settled_s = 0;
        n->anchor_x = n->x_m;
        n->anchor_y = n->y_m;
        if (n->current_step.kind == RCS_STEP_CUSTOM) {
            rcs_action_result r =
                n->action(n->action_context, n->current_step.custom_index, RCS_ACTION_ENTER, 0);
            if (r != RCS_ACTION_BUSY && r != RCS_ACTION_DONE)
                return fail(n, RCS_NAV_ACTION_FAILED), -1;
            if (r == RCS_ACTION_DONE)
                complete = 1;
        } else if (n->current_step.kind != RCS_STEP_WAIT)
            begin_spin(n, &n->current_step);
    } else if (n->current_step.kind == RCS_STEP_CUSTOM) {
        rcs_action_result r =
            n->action(n->action_context, n->current_step.custom_index, RCS_ACTION_TICK, n->action_s);
        if (r != RCS_ACTION_BUSY && r != RCS_ACTION_DONE)
            return fail(n, RCS_NAV_ACTION_FAILED), -1;
        complete = r == RCS_ACTION_DONE;
    }
    const rcs_step *a = &n->current_step;
    n->action_s += dt;
    if (a->kind == RCS_STEP_WAIT)
        complete = n->action_s >= a->value;
    if (!complete && n->action_s > a->timeout_s)
        return fail(n, RCS_NAV_ACTION_TIMEOUT), -1;
    if (hypotf(n->x_m - n->anchor_x, n->y_m - n->anchor_y) > a->drift_limit_m)
        return fail(n, RCS_NAV_ACTION_DRIFT), -1;
    if (a->kind != RCS_STEP_WAIT && a->kind != RCS_STEP_CUSTOM)
        complete = spin(n, a, vel, dt);
    if (complete) {
        if (a->kind == RCS_STEP_ROTATE_ABSOLUTE || a->kind == RCS_STEP_ROTATE_RELATIVE)
            n->heading_target = wrap(n->spin_target);
        n->action_active = 0;
        n->action_index++;
        n->settled_s = 0;
        zeros(n->command_world);
    }
    return 0;
}
/**
 * @brief 绑定独立观测纠错表；空组直接跳过，不执行旧隐式纠错
 * @note 表和回调须保持有效。回调返回1表示样本可用，0表示读取失败。
 */
int rcs_nav_bind_corrections(rcs_navigator *n, const rcs_correction_group *groups,
                             size_t count, rcs_observer_fn observer, void *context)
{
    if (!n || !n->initialized || n->state != RCS_NAV_IDLE)
        return 0;
    if (!groups || count != n->count)
        return fail(n, RCS_NAV_OBSERVATION_CONFIG);
    for (size_t i = 0; i < count; i++) {
        const rcs_correction_group *g = groups + i;
        if (g->count > 32 || (g->count && (!g->steps || !observer)))
            return fail(n, RCS_NAV_OBSERVATION_CONFIG);
        for (size_t j = 0; j < g->count; j++) {
            const rcs_correction *c = g->steps + j;
            if (c->source >= 32 || c->kind > 2 ||
                (c->axes != 3 && c->axes != 4 && c->axes != 7) ||
                (c->kind == 1 && c->axes != 3) || (c->kind == 2 && c->axes != 4) ||
                !positive(c->timeout_s) || c->timeout_s > 3600 ||
                !positive(c->max_age_s) || c->max_age_s > .5f ||
                !isfinite(c->min_quality) || c->min_quality < 0 || c->min_quality > 1 ||
                !positive(c->max_jump_m) || c->max_jump_m > 1 ||
                !positive(c->max_jump_rad) || c->max_jump_rad > PI ||
                !positive(c->speed_mps) || c->speed_mps > n->cfg.max_speed_mps ||
                !positive(c->max_wz_radps) || c->max_wz_radps > n->cfg.max_wz_radps ||
                !positive(c->position_tolerance_m) || !positive(c->yaw_tolerance_rad) ||
                c->yaw_tolerance_rad > PI || !positive(c->settle_s))
                return fail(n, RCS_NAV_OBSERVATION_CONFIG);
        }
    }
    n->corrections = groups;
    n->observer = observer;
    n->observer_context = context;
    n->observation_seen = 0;
    return 1;
}

/**
 * @brief 检查独立样本；重复序号只能在有效期内沿用，不重复更新定位
 */
static int observe(rcs_navigator *n, const rcs_correction *c, float dt, int *fresh)
{
    rcs_observation o = {0};
    uint32_t bit = (uint32_t)1u << c->source;
    (void)dt;
    *fresh = 0;
    if (!n->observer || n->observer(n->observer_context, c->source, &o) != 1 ||
        (o.axes != 3 && o.axes != 4 && o.axes != 7) ||
        (o.axes & c->axes) != c->axes || !isfinite(o.x_m) || !isfinite(o.y_m) ||
        !isfinite(o.yaw_rad) || fabsf(o.yaw_rad) > 2 * PI || !isfinite(o.age_s) || o.age_s < 0 ||
        o.age_s > c->max_age_s || !isfinite(o.quality) || o.quality < c->min_quality ||
        o.quality > 1 || fabsf(o.x_m) > 1000 || fabsf(o.y_m) > 1000)
        return fail(n, RCS_NAV_OBSERVATION);
    if (n->observation_seen & bit) {
        uint32_t advance = o.sequence - n->observation_sequence[c->source];
        if (advance == 0) {
            if (n->observation_age[c->source] > c->max_age_s)
                return fail(n, RCS_NAV_OBSERVATION);
            return 1;
        }
        if (advance > 0x7fffffffu)
            return fail(n, RCS_NAV_OBSERVATION);
    }
    float yaw_delta = wrap(o.yaw_rad - n->yaw_rad);
    if (((c->axes & 3u) && hypotf(o.x_m - n->x_m, o.y_m - n->y_m) > c->max_jump_m) ||
        ((c->axes & 4u) && fabsf(yaw_delta) > c->max_jump_rad))
        return fail(n, RCS_NAV_OBSERVATION_JUMP);
    if (c->axes & 3u) {
        n->x_m = o.x_m;
        n->y_m = o.y_m;
    }
    if (c->axes & 4u) {
        n->yaw_unwrapped += yaw_delta;
        n->yaw_rad = wrap(n->yaw_unwrapped);
    }
    n->observation_sequence[c->source] = o.sequence;
    n->observation_age[c->source] = o.age_s;
    n->observation_seen |= bit;
    *fresh = 1;
    return 1;
}

/**
 * @brief 顺序执行显式纠错；刷新估计不运动，位置与朝向分别对齐
 */
static int correct(rcs_navigator *n, const float vel[3], float dt)
{
    const rcs_correction_group *group = n->corrections + n->target;
    if (n->action_index >= group->count)
        return 1;
    const rcs_correction *c = group->steps + n->action_index;
    int fresh, complete = 0;
    n->action_s += dt;
    if (n->action_s > c->timeout_s)
        return fail(n, RCS_NAV_TIMEOUT), -1;
    if (!observe(n, c, dt, &fresh))
        return -1;
    if (!n->correction_started) {
        n->correction_origin_x = n->x_m;
        n->correction_origin_y = n->y_m;
        n->correction_started = 1;
    }
    rcs_point_plan point_plan = plan_for(n);
    if (c->kind != 0 &&
        hypotf(n->x_m - n->correction_origin_x, n->y_m - n->correction_origin_y) >
        point_plan.drift_limit_m)
        return fail(n, RCS_NAV_ACTION_DRIFT), -1;
    if (c->kind == 0) {
        zeros(n->command_world);
        complete = fresh;
    } else if (c->kind == 1) {
        const rcs_waypoint *p = n->points + n->target;
        float dx = p->x_m - n->x_m, dy = p->y_m - n->y_m;
        int within = hypotf(dx, dy) <= c->position_tolerance_m;
        translate(n, within ? 0 : dx, within ? 0 : dy, within ? 0 : c->speed_mps, dt);
        complete = settle(n, within && stopped(vel) &&
                          hypotf(n->command_world[0], n->command_world[1]) < 1e-5f,
                          dt, c->settle_s) && fresh;
    } else {
        float error = wrap(n->heading_target - n->yaw_rad);
        float w = fabsf(error) <= c->yaw_tolerance_rad ? 0 :
                  clamp(n->cfg.yaw_kp * error, -c->max_wz_radps, c->max_wz_radps);
        n->command_world[0] = n->command_world[1] = 0;
        n->command_world[2] += clamp(w - n->command_world[2],
                                  -n->cfg.alpha_radps2 * dt, n->cfg.alpha_radps2 * dt);
        complete = settle(n, fabsf(error) <= c->yaw_tolerance_rad && stopped(vel) &&
                          fabsf(n->command_world[2]) < 1e-5f, dt, c->settle_s) && fresh;
    }
    if (complete) {
        n->action_index++;
        n->action_s = n->settled_s = 0;
        n->correction_started = 0;
        zeros(n->command_world);
    }
    return 0;
}

/**
 * @brief 持续更新里程计并推进当前独立阶段
 */
int rcs_nav_update(rcs_navigator *n, const int32_t delta[4], uint8_t fresh, float dt, float out[3])
{
    zeros(out);
    if (!n || !n->initialized || n->state == RCS_NAV_FAULT || !out)
        return 0;
    if (!delta)
        return fail(n, RCS_NAV_FEEDBACK);
    if (!isfinite(dt) || dt < .0001f || dt > n->cfg.max_dt_s)
        return fail(n, RCS_NAV_TIMING);
    unsigned wheels = rcs_wheel_count(&n->cfg.geometry), mask = (1u << wheels) - 1u;
    if ((fresh & mask) != mask)
        return fail(n, RCS_NAV_FEEDBACK);
    float rpm[4] = {0}, vel[3] = {0};
    for (unsigned i = 0; i < wheels; i++) {
        rpm[i] = (float)delta[i] * 60 / (n->cfg.counts_per_motor_rev * dt) * n->cfg.encoder_direction[i];
        if (!isfinite(rpm[i]) ||
            fabsf(rpm[i]) > n->cfg.geometry.max_motor_rpm * 1.25f + 120 / (n->cfg.counts_per_motor_rev * dt))
            return fail(n, RCS_NAV_ENCODER_RANGE);
    }
    if (!rcs_forward(&n->cfg.geometry, rpm, vel))
        return fail(n, RCS_NAV_ENCODER_RANGE);
    n->feedback_seen = 1;
    if (!n->pose_set)
        return 1;
    float da = vel[2] * dt, a = 1, b = 0;
    if (fabsf(da) > .0001f) {
        float half = sinf(da * .5f);
        a = sinf(da) / da;
        b = 2 * half * half / da;
    } else {
        a = 1 - da * da / 6;
        b = da * .5f;
    }
    float bx = dt * (a * vel[0] - b * vel[1]), by = dt * (b * vel[0] + a * vel[1]);
    float co = cosf(n->yaw_rad), si = sinf(n->yaw_rad);
    n->x_m += co * bx - si * by;
    n->y_m += si * bx + co * by;
    n->yaw_unwrapped += da;
    n->yaw_rad = wrap(n->yaw_unwrapped);
    if (!isfinite(n->x_m) || !isfinite(n->y_m) || !isfinite(n->yaw_rad) || fabsf(n->x_m) > 1000 ||
        fabsf(n->y_m) > 1000)
        return fail(n, RCS_NAV_POSE);
    for (unsigned i = 0; i < 32; i++)
        if (n->observation_seen & ((uint32_t)1u << i))
            n->observation_age[i] += dt;
    if (n->state != RCS_NAV_RUNNING)
        return 1;
    const rcs_waypoint *point = n->points + n->target;
    rcs_point_plan p = plan_for(n);
    float dx = point->x_m - n->x_m, dy = point->y_m - n->y_m, dist = hypotf(dx, dy);
    n->stage_s += dt;
    if (n->stage == RCS_STAGE_MOVE) {
        if (n->stage_s > n->cfg.max_leg_s)
            return fail(n, RCS_NAV_TIMEOUT);
        if (dist > p.arrival_radius_m)
            translate(n, dx, dy, point->speed_mps, dt);
        else {
            translate(n, 0, 0, 0, dt);
            if (hypotf(n->command_world[0], n->command_world[1]) < 1e-5f)
                stage(n, RCS_STAGE_SETTLE);
        }
    } else if (n->stage == RCS_STAGE_SETTLE) {
        if (n->stage_s > n->cfg.max_leg_s)
            return fail(n, RCS_NAV_TIMEOUT);
        if (dist > p.arrival_radius_m)
            stage(n, RCS_STAGE_MOVE);
        else if (settle(n, stopped(vel), dt, p.settle_s))
            stage(n, RCS_STAGE_ARRIVAL);
    } else if (n->stage == RCS_STAGE_ARRIVAL || n->stage == RCS_STAGE_DEPARTURE) {
        int result = run_group(n, &p, vel, dt);
        if (result < 0)
            return 0;
        if (result > 0) {
            if (n->stage == RCS_STAGE_ARRIVAL) {
                n->correction_pass = 0;
                if (n->corrections)
                    stage(n, n->corrections[n->target].count ?
                          RCS_STAGE_CORRECTION : RCS_STAGE_DEPARTURE);
                else
                    stage(n, RCS_STAGE_CORRECT_XY);
            } else
                stage(n, RCS_STAGE_EXIT);
        }
    } else if (n->stage == RCS_STAGE_CORRECTION) {
        int result = correct(n, vel, dt);
        if (result < 0)
            return 0;
        if (result > 0)
            stage(n, RCS_STAGE_DEPARTURE);
    } else if (n->stage == RCS_STAGE_CORRECT_XY) {
        if (n->stage_s > p.correction_timeout_s)
            return fail(n, RCS_NAV_TIMEOUT);
        if (p.correction_mode == 0 || dist <= n->cfg.position_tolerance_m) {
            translate(n, 0, 0, 0, dt);
            if (settle(n, stopped(vel) && hypotf(n->command_world[0], n->command_world[1]) < 1e-5f, dt,
                       p.settle_s)) {
                if (p.correction_mode == 2) {
                    stage(n, RCS_STAGE_CORRECT_YAW);
                    rcs_step spin_step = legacy_step(n, 1);
                    spin_step.value = n->heading_target;
                    begin_spin(n, &spin_step);
                } else
                    stage(n, RCS_STAGE_DEPARTURE);
            }
        } else if (p.correction_mode == 3)
            return fail(n, RCS_NAV_POSE);
        else
            translate(n, dx, dy, p.correction_speed_mps, dt);
    } else if (n->stage == RCS_STAGE_CORRECT_YAW) {
        if (n->stage_s > p.correction_timeout_s)
            return fail(n, RCS_NAV_TIMEOUT);
        if (hypotf(n->x_m - n->anchor_x, n->y_m - n->anchor_y) > p.drift_limit_m)
            return fail(n, RCS_NAV_ACTION_DRIFT);
        rcs_step a = legacy_step(n, 1);
        if (spin(n, &a, vel, dt)) {
            if (dist > n->cfg.position_tolerance_m) {
                if (n->correction_pass++ >= p.correction_retries)
                    return fail(n, RCS_NAV_POSE);
                stage(n, RCS_STAGE_CORRECT_XY);
            } else
                stage(n, RCS_STAGE_DEPARTURE);
        }
    } else if (n->stage == RCS_STAGE_EXIT) {
        if (n->stage_s > p.correction_timeout_s)
            return fail(n, RCS_NAV_TIMEOUT);
        float tolerance = p.correction_mode ? n->cfg.position_tolerance_m : p.arrival_radius_m;
        if (n->corrections ?
            hypotf(n->x_m - n->anchor_x, n->y_m - n->anchor_y) > p.drift_limit_m :
            (dist > tolerance || fabsf(wrap(n->heading_target - n->yaw_rad)) > n->cfg.yaw_tolerance_rad))
            return fail(n, RCS_NAV_POSE);
        if (settle(n, stopped(vel), dt, p.settle_s) && (n->plans || n->stage_s >= point->dwell_s)) {
            n->target++;
            if (n->target == n->count) {
                n->state = RCS_NAV_DONE;
                zeros(n->command_world);
            } else
                stage(n, RCS_STAGE_MOVE);
        }
    }
    if (n->state == RCS_NAV_RUNNING && n->stage == RCS_STAGE_DEPARTURE && n->target + 1 == n->count &&
        !p.departure_at_end)
        stage(n, RCS_STAGE_EXIT);
    if (n->state != RCS_NAV_RUNNING)
        return 1;
    co = cosf(n->yaw_rad);
    si = sinf(n->yaw_rad);
    out[0] = co * n->command_world[0] + si * n->command_world[1];
    out[1] = -si * n->command_world[0] + co * n->command_world[1];
    out[2] = n->command_world[2];
    return 1;
}

/* RCS GENERATED SHA256 197e64eccee532d55de7f0538249fa20ea1c89f594957905914f86c2d5b76f40 */
