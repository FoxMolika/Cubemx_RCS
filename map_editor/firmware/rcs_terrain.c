/**
 * @file rcs_terrain.c
 * @brief 可执行直坡模型：行程换算、连续坐标和提前减速
 * @note 仅支持已知直坡纵向通过；不包含扭矩前馈，不推断真实高度或打滑。
 */
#include "rcs_terrain.h"
#include <math.h>
#include <string.h>
#define TERRAIN_PI 3.14159265358979323846f
/**
 * @brief 检查正有限数
 */
static int valid_positive(float v)
{
    return isfinite(v) && v > 0;
}
/**
 * @brief 限制数值范围
 */
static float bounded(float v, float lo, float hi)
{
    return fmaxf(lo, fminf(hi, v));
}
/**
 * @brief 锁定首发地形故障并撤销运动命令
 */
static int terrain_fail(rcs_terrain *t, rcs_terrain_fault fault)
{
    if (t && t->fault == RCS_TERRAIN_OK)
        t->fault = fault;
    if (t)
        t->speed_mps = 0;
    return 0;
}
/**
 * @brief 在单调节点中找到行程所在区间，允许有限的端点外推
 */
static size_t interval_at(const rcs_terrain_leg *leg, float value, int horizontal)
{
    size_t i = 0;
    while (i + 2 < leg->count && value >
           (horizontal ? leg->nodes[i + 1].h_m : leg->nodes[i + 1].s_m))
        i++;
    return i;
}
/**
 * @brief 以同一标定表换算坡面和水平行程，避免接边坐标跳变
 */
static float convert(const rcs_terrain_leg *leg, float value, int horizontal)
{
    size_t i = interval_at(leg, value, horizontal);
    const rcs_terrain_node *a = leg->nodes + i, *b = a + 1;
    float from0 = horizontal ? a->h_m : a->s_m, from1 = horizontal ? b->h_m : b->s_m;
    float to0 = horizontal ? a->s_m : a->h_m, to1 = horizontal ? b->s_m : b->h_m;
    return to0 + (to1 - to0) * (value - from0) / (from1 - from0);
}
/**
 * @brief 更新当前规划高度与标定倾角
 */
static void update_context(rcs_terrain *t)
{
    const rcs_terrain_leg *leg = t->legs + t->leg_index;
    size_t i = interval_at(leg, t->progress_m, 0);
    const rcs_terrain_node *a = leg->nodes + i, *b = a + 1;
    float u = bounded((t->progress_m - a->s_m) / (b->s_m - a->s_m), 0, 1);
    t->pitch_rad = a->pitch_rad + (b->pitch_rad - a->pitch_rad) * u;
    t->z_plan_m = a->z_m + (b->z_m - a->z_m) * u;
    t->horizontal_m = convert(leg, t->progress_m, 0);
}
/**
 * @brief 校验完整地形表及参数，不进行传感器或电机操作
 */
int rcs_terrain_init(rcs_terrain *t, const rcs_terrain_leg *legs, size_t count,
                     const rcs_terrain_config *config, rcs_terrain_brake_fn brake, void *context)
{
    if (!t)
        return 0;
    memset(t, 0, sizeof(*t));
    if (!legs || !config || count < 2 || count > 512 ||
        !valid_positive(config->acceleration_mps2) || config->acceleration_mps2 > 10 ||
        !valid_positive(config->deceleration_mps2) || config->deceleration_mps2 > 10 ||
        !valid_positive(config->lateral_tolerance_m) || config->lateral_tolerance_m > .2f ||
        !valid_positive(config->heading_tolerance_rad) || config->heading_tolerance_rad > .35f ||
        config->brake_validated > 1)
        return terrain_fail(t, RCS_TERRAIN_CONFIG);
    size_t total = 0;
    for (size_t i = 1; i < count; i++) {
        const rcs_terrain_leg *leg = legs + i;
        if (!leg->nodes || leg->count < 2 || leg->count > 4096 ||
            !isfinite(leg->x0_m) || !isfinite(leg->y0_m) || !isfinite(leg->bearing_rad) ||
            leg->inclined > 1)
            return terrain_fail(t, RCS_TERRAIN_CONFIG);
        total += leg->count;
        if (total > 16384 || leg->nodes[0].s_m != 0 || leg->nodes[0].h_m != 0)
            return terrain_fail(t, RCS_TERRAIN_CONFIG);
        for (size_t j = 0; j < leg->count; j++) {
            const rcs_terrain_node *p = leg->nodes + j;
            if (!isfinite(p->s_m) || !isfinite(p->h_m) || !isfinite(p->z_m) ||
                !isfinite(p->pitch_rad) || fabsf(p->pitch_rad) > TERRAIN_PI / 4 + 1e-5f ||
                !valid_positive(p->limit_mps) || p->limit_mps > 20 ||
                p->s_m < 0 || p->s_m > 1500 || p->h_m < 0 || p->h_m > 1000 ||
                fabsf(p->z_m) > 100 ||
                (j && (p->s_m <= leg->nodes[j - 1].s_m || p->h_m <= leg->nodes[j - 1].h_m)))
                return terrain_fail(t, RCS_TERRAIN_CONFIG);
            if (j) {
                float ds = p->s_m - leg->nodes[j - 1].s_m;
                float dh = p->h_m - leg->nodes[j - 1].h_m;
                if (ds < dh * .999f || ds > dh * 1.416f)
                    return terrain_fail(t, RCS_TERRAIN_CONFIG);
            }
        }
    }
    t->legs = legs;
    t->count = count;
    t->cfg = *config;
    t->brake = brake;
    t->brake_context = context;
    t->initialized = 1;
    return 1;
}
/**
 * @brief 启动前检查制动声明和驱动就绪，不以零输出冒充驻坡
 */
int rcs_terrain_arm(rcs_terrain *t)
{
    if (!t || !t->initialized || t->fault != RCS_TERRAIN_OK)
        return 0;
    int needs_brake = 0;
    for (size_t i = 1; i < t->count; i++)
        needs_brake |= t->legs[i].inclined;
    if (!needs_brake)
        return 1;
    if (!t->cfg.brake_validated || !t->brake ||
        t->brake(t->brake_context, RCS_TERRAIN_BRAKE_CHECK) != 1)
        return terrain_fail(t, RCS_TERRAIN_BRAKE_UNAVAILABLE);
    return 1;
}
/**
 * @brief 按当前估计位置建立本段进度，保留原位置而非吸附
 */
int rcs_terrain_begin(rcs_terrain *t, size_t leg, float x_m, float y_m)
{
    if (!t || !t->initialized || t->fault || leg == 0 || leg >= t->count ||
        !isfinite(x_m) || !isfinite(y_m))
        return terrain_fail(t, RCS_TERRAIN_ENTRY);
    const rcs_terrain_leg *p = t->legs + leg;
    float co = cosf(p->bearing_rad), si = sinf(p->bearing_rad);
    float dx = x_m - p->x0_m, dy = y_m - p->y0_m;
    if (fabsf(-si * dx + co * dy) > t->cfg.lateral_tolerance_m)
        return terrain_fail(t, RCS_TERRAIN_ENTRY);
    t->leg_index = leg;
    t->progress_m = convert(p, co * dx + si * dy, 1);
    t->speed_mps = 0;
    t->active = 1;
    t->hold_requested = t->hold_accepted = 0;
    update_context(t);
    return 1;
}
/**
 * @brief 对沿坡速度进行一次水平投影；垂直方向的侧向分量保持独立
 */
int rcs_terrain_integrate(rcs_terrain *t, float vx, float vy, float dt_s,
                          float *dx_m, float *dy_m)
{
    if (dx_m)
        *dx_m = 0;
    if (dy_m)
        *dy_m = 0;
    if (!t || !t->active || t->fault || !dx_m || !dy_m ||
        !isfinite(vx) || !isfinite(vy) || !valid_positive(dt_s) || dt_s > .25f)
        return terrain_fail(t, RCS_TERRAIN_CONFIG);
    const rcs_terrain_leg *leg = t->legs + t->leg_index;
    if (leg->inclined && (!t->brake ||
        t->brake(t->brake_context, RCS_TERRAIN_BRAKE_CHECK) != 1))
        return terrain_fail(t, RCS_TERRAIN_BRAKE_UNAVAILABLE);
    float co = cosf(leg->bearing_rad), si = sinf(leg->bearing_rad);
    float along = co * vx + si * vy, across = -si * vx + co * vy;
    size_t at = interval_at(leg, t->progress_m, 0);
    float speed_limit = fminf(leg->nodes[at].limit_mps, leg->nodes[at + 1].limit_mps);
    if (leg->inclined && fabsf(along) > speed_limit * 1.5f + .05f)
        return terrain_fail(t, RCS_TERRAIN_OVERSPEED);
    float before = convert(leg, t->progress_m, 0);
    t->progress_m += along * dt_s;
    if (t->progress_m < -t->cfg.lateral_tolerance_m - .1f ||
        t->progress_m > leg->nodes[leg->count - 1].s_m + t->cfg.lateral_tolerance_m + .1f)
        return terrain_fail(t, RCS_TERRAIN_TRACK);
    update_context(t);
    float dh = t->horizontal_m - before;
    *dx_m = co * dh - si * across * dt_s;
    *dy_m = si * dh + co * across * dt_s;
    t->last_dx_m = *dx_m;
    t->last_dy_m = *dy_m;
    return 1;
}
/**
 * @brief 按前方每个限速节点和可用减速度计算提前减速上限
 */
static float forward_limit(const rcs_terrain *t)
{
    const rcs_terrain_leg *leg = t->legs + t->leg_index;
    size_t i = interval_at(leg, t->progress_m, 0);
    float cap = fminf(leg->nodes[i].limit_mps, leg->nodes[i + 1].limit_mps);
    for (size_t j = i + 1; j < leg->count; j++) {
        float distance = fmaxf(0, leg->nodes[j].s_m - t->progress_m);
        float v = leg->nodes[j].limit_mps;
        cap = fminf(cap, sqrtf(v * v + 2 * t->cfg.deceleration_mps2 * distance));
    }
    return cap;
}
/**
 * @brief 沿路径生成命令，在平地允许里程计轨迹跟随，坡上不横移或旋转
 */
int rcs_terrain_command(rcs_terrain *t, float x_m, float y_m, float yaw_rad,
                        float arrival_radius_m, float requested_mps, float dt_s, float command[3])
{
    if (command)
        command[0] = command[1] = command[2] = 0;
    if (!t || !t->active || t->fault || !command || !isfinite(x_m) || !isfinite(y_m) ||
        !isfinite(yaw_rad) || !valid_positive(arrival_radius_m) || !valid_positive(requested_mps) ||
        !valid_positive(dt_s) || dt_s > .25f)
        return terrain_fail(t, RCS_TERRAIN_CONFIG);
    const rcs_terrain_leg *leg = t->legs + t->leg_index;
    float co = cosf(leg->bearing_rad), si = sinf(leg->bearing_rad);
    float cross = -si * (x_m - leg->x0_m) + co * (y_m - leg->y0_m);
    float remain = leg->nodes[leg->count - 1].h_m - t->horizontal_m;
    int inclined = fabsf(t->pitch_rad) > 1e-5f;
    if (fabsf(cross) > t->cfg.lateral_tolerance_m)
        return terrain_fail(t, RCS_TERRAIN_TRACK);
    if (inclined && fabsf(remainderf(yaw_rad - leg->bearing_rad, TERRAIN_PI)) >
        t->cfg.heading_tolerance_rad)
        return terrain_fail(t, RCS_TERRAIN_ENTRY);
    float distance = hypotf(remain, cross), desired = 0;
    if (distance > arrival_radius_m) {
        float left = fabsf(leg->nodes[leg->count - 1].s_m - t->progress_m);
        float cap = fminf(requested_mps, forward_limit(t));
        cap = fminf(cap, sqrtf(2 * t->cfg.deceleration_mps2 * left));
        desired = copysignf(fminf(cap, 1.5f * fabsf(remain)), remain);
    }
    float rate = fabsf(desired) < fabsf(t->speed_mps) ?
                 t->cfg.deceleration_mps2 : t->cfg.acceleration_mps2;
    t->speed_mps += bounded(desired - t->speed_mps, -rate * dt_s, rate * dt_s);
    float lateral = inclined ? 0 : bounded(-1.5f * cross, -.08f, .08f);
    if (distance <= arrival_radius_m)
        lateral = 0;
    command[0] = co * t->speed_mps - si * lateral;
    command[1] = si * t->speed_mps + co * lateral;
    if (distance <= arrival_radius_m && fabsf(t->speed_mps) < 1e-5f)
        return 2;
    return 1;
}
/**
 * @brief 在估计处于坡面时请求制动，不承诺断电或失联后能物理驻坡
 */
int rcs_terrain_request_hold(rcs_terrain *t)
{
    if (!t || !t->initialized || !t->active || fabsf(t->pitch_rad) <= 1e-5f)
        return 0;
    t->hold_requested = 1;
    t->hold_accepted = t->brake && t->brake(t->brake_context, RCS_TERRAIN_BRAKE_HOLD) == 1;
    return t->hold_accepted;
}
