/**
 * @file rcs_route.c
 * @brief 路线配置、航点表与初始化入口
 * @note 位置为米，朝向为弧度；编码器计数是积分参考，不是停车判据。
 */
#include "rcs_route.h"
#include "rcs_actions.h"

/* 导出配置 ------------------------------------------------------------------------------------------*/

const rcs_nav_config rcs_route_config = {
    {
        (rcs_layout)0,
        0.1f,
        0.3f,
        0.3f,
        0.2f,
        0.0f,
        19.2032f,
        3000.0f
    },
    8192.0f,
    { 1, 1, 1, 1 },
    0.6f,
    1.57079633f,
    0.5f,
    3.14159265f,
    1.5f,
    2.0f,
    0.02f,
    0.0523598776f,
    0.1f,
    120.0f,
    1u
};

/* 航点与分段参考 ------------------------------------------------------------------------------------*/

const rcs_waypoint rcs_route_points[RCS_ROUTE_POINT_COUNT] = {
    { 0.6f, 0.6f, 0.0f, 0.4f, 0.0f },
    { 4.2f, 0.6f, 0.0f, 0.5f, 0.0f },
    { 5.2f, 1.6f, 1.57079633f, 0.4f, 0.0f },
    { 5.2f, 3.2f, 1.57079633f, 0.4f, 0.0f },
    { 0.7f, 3.2f, 3.14159265f, 0.5f, 0.0f },
};

/* 匀速估计不包含加减速、到点收敛、机构动作与停留。 */
const rcs_segment rcs_route_segments[RCS_ROUTE_SEGMENT_COUNT] = {
    {
        3.6f, 0.0f, 7.2f,
        { 1802669.77f, 1802669.77f, 1802669.77f, 1802669.77f }
    },
    {
        1.41421356f, 0.0f, 3.53553391f,
        { 0.0f, 1001483.21f, 1001483.21f, 0.0f }
    },
    {
        1.6f, 0.0f, 4.0f,
        { 801186.566f, 801186.566f, 801186.566f, 801186.566f }
    },
    {
        4.5f, 0.0f, 9.0f,
        { -2253337.22f, 2253337.22f, 2253337.22f, -2253337.22f }
    },
};

const char rcs_route_revision[] = "9b8134ee4f8e8ad267304a834e1136b5f1e7659e1516d7c75f6d48f5476534b9";
static const rcs_step point_0_1[] = {
    { RCS_STEP_WAIT, 0.5f, 30.0f, 1.57079633f, 3.14159265f, 0.0523598776f, 0.1f, 0.05f, 0, 0u },
};
static const rcs_step point_2_0[] = {
    { RCS_STEP_ROTATE_ABSOLUTE, 1.57079633f, 30.0f, 1.57079633f, 3.14159265f, 0.0523598776f, 0.1f,
        0.05f, 0, 0u },
};
static const rcs_step point_2_1[] = {
    { RCS_STEP_WAIT, 1.0f, 30.0f, 1.57079633f, 3.14159265f, 0.0523598776f, 0.1f, 0.05f, 0, 0u },
};
static const rcs_step point_4_0[] = {
    { RCS_STEP_ROTATE_ABSOLUTE, 3.14159265f, 30.0f, 1.57079633f, 3.14159265f, 0.0523598776f, 0.1f,
        0.05f, 0, 0u },
};
static const rcs_step point_4_1[] = {
    { RCS_STEP_WAIT, 1.0f, 30.0f, 1.57079633f, 3.14159265f, 0.0523598776f, 0.1f, 0.05f, 0, 0u },
};

/* 空组明确跳过纠错；旧CorrectionMode仅作迁移记录，不参与生成。 */
static const rcs_correction_group route_corrections[RCS_ROUTE_POINT_COUNT] = {
    { NULL, 0u },
    { NULL, 0u },
    { NULL, 0u },
    { NULL, 0u },
    { NULL, 0u },
};

const rcs_point_plan rcs_route_plan[RCS_ROUTE_POINT_COUNT] = {
    { NULL, 0u, point_0_1, 1u, 0.04f, 0.1f, 10.0f, 0.1f, 0.05f, 0u, 2u, 1u },
    { NULL, 0u, NULL, 0u, 0.04f, 0.1f, 10.0f, 0.1f, 0.05f, 0u, 2u, 0u },
    { point_2_0, 1u, point_2_1, 1u, 0.04f, 0.1f, 10.0f, 0.1f, 0.05f, 0u, 2u, 1u },
    { NULL, 0u, NULL, 0u, 0.04f, 0.1f, 10.0f, 0.1f, 0.05f, 0u, 2u, 0u },
    { point_4_0, 1u, point_4_1, 1u, 0.04f, 0.1f, 10.0f, 0.1f, 0.05f, 0u, 2u, 1u },
};

/* 导出函数 ------------------------------------------------------------------------------------------*/

/**
 * @brief 初始化导航，并绑定本路线的机构动作
 * @param navigator 导航实例；重新初始化前先停止旧实例
 * @param action_context 用户机构状态，可为空
 * @return 1 成功，0 配置或动作绑定失败；不会启动电机
 */
int rcs_route_init(rcs_navigator *navigator, void *action_context)
{
    if (!rcs_nav_init(navigator, &rcs_route_config, rcs_route_points, RCS_ROUTE_POINT_COUNT)) {
        return 0;
    }

    if (!rcs_nav_bind_plan(navigator, rcs_route_plan, RCS_ROUTE_POINT_COUNT,
                           rcs_route_action, action_context)) {
        return 0;
    }
    return rcs_nav_bind_corrections(navigator, route_corrections, RCS_ROUTE_POINT_COUNT,
                                   rcs_route_observe, action_context);
}

/* RCS GENERATED SHA256 c143921bd7f8eb6b8394b3f383b589980fa354e1c5bf67b78f1753def7d36aae */
