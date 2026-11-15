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
    { 4.2f, 1.2f, 0.0f, 0.5f, 0.0f },
    { 4.7f, 1.6f, 1.57079633f, 0.4f, 0.0f },
    { 5.2f, 3.2f, 1.57079633f, 0.4f, 0.0f },
    { 0.7f, 3.2f, 3.14159265f, 0.5f, 0.0f },
};

/* 匀速估计不包含加减速、到点收敛、机构动作与停留。 */
const rcs_segment rcs_route_segments[RCS_ROUTE_SEGMENT_COUNT] = {
    {
        3.64965752f, 0.0f, 7.29931504f,
        { 1502224.81f, 2103114.74f, 2103114.74f, 1502224.81f }
    },
    {
        0.640312424f, 0.0f, 1.60078106f,
        { 50074.1604f, 450667.443f, 450667.443f, 50074.1604f }
    },
    {
        1.67630546f, 0.0f, 4.19076365f,
        { 1051557.37f, 550815.764f, 550815.764f, 1051557.37f }
    },
    {
        4.5f, 0.0f, 9.0f,
        { -2253337.22f, 2253337.22f, 2253337.22f, -2253337.22f }
    },
};

const char rcs_route_revision[] = "7f0de546920d66081c0327c78da0cc4fada941b316d617d7af8ba577e5cd357e";
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

const rcs_point_plan rcs_route_plan[RCS_ROUTE_POINT_COUNT] = {
    { NULL, 0u, point_0_1, 1u, 0.04f, 0.1f, 10.0f, 0.1f, 0.05f, 1u, 2u, 1u },
    { NULL, 0u, NULL, 0u, 0.04f, 0.1f, 10.0f, 0.1f, 0.05f, 1u, 2u, 0u },
    { point_2_0, 1u, point_2_1, 1u, 0.04f, 0.1f, 10.0f, 0.1f, 0.05f, 1u, 2u, 1u },
    { NULL, 0u, NULL, 0u, 0.04f, 0.1f, 10.0f, 0.1f, 0.05f, 1u, 2u, 0u },
    { point_4_0, 1u, point_4_1, 1u, 0.04f, 0.1f, 10.0f, 0.1f, 0.05f, 1u, 2u, 1u },
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

    return rcs_nav_bind_plan(navigator, rcs_route_plan, RCS_ROUTE_POINT_COUNT,
                                rcs_route_action, action_context);
}

/* RCS GENERATED SHA256 8bc579d61c36ebacf89349a2aff389c829295d3cc2235e3f97184afac1a83394 */
