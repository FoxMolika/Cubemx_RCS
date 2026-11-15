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
    { 0.6f, 0.6f, 0.0f, 0.4f, 0.5f },
    { 4.2f, 0.6f, 0.0f, 0.5f, 0.0f },
    { 5.2f, 1.6f, 1.57079633f, 0.4f, 1.0f },
    { 5.2f, 3.2f, 1.57079633f, 0.4f, 0.0f },
    { 0.7f, 3.2f, 3.14159265f, 0.5f, 1.0f },
};

/* 匀速估计不包含加减速、到点收敛、机构动作与停留。 */
const rcs_segment rcs_route_segments[RCS_ROUTE_SEGMENT_COUNT] = {
    {
        3.6f, 0.0f, 7.2f,
        { 1802669.77f, 1802669.77f, 1802669.77f, 1802669.77f }
    },
    {
        1.41421356f, 1.57079633f, 3.53553391f,
        { 401595.09f, 873532.933f, 401595.09f, 873532.933f }
    },
    {
        1.6f, 0.0f, 4.0f,
        { 801186.566f, 801186.566f, 801186.566f, 801186.566f }
    },
    {
        4.5f, 1.57079633f, 9.0f,
        { -235968.922f, 3105006.97f, 2633069.13f, 235968.922f }
    },
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

    return rcs_nav_bind_actions(navigator, rcs_route_actions, RCS_ROUTE_POINT_COUNT,
                                rcs_route_action, action_context);
}
/* RCS GENERATED SHA256 7a40e0fa31d3c9e7e87ec6664c87b4b5d8a37a970157abb1e8490fa0c008f35e */
