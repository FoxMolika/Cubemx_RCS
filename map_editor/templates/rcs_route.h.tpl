/**
 * @file rcs_route.h
 * @brief 路线参数、航点与动作绑定接口
 */
#pragma once

#include "rcs_nav.h"

#ifdef __cplusplus
extern "C" {
#endif

/* 导出配置 ------------------------------------------------------------------------------------------*/

#define RCS_ROUTE_POINT_COUNT @POINT_COUNT@u
#define RCS_ROUTE_SEGMENT_COUNT @SEGMENT_COUNT@u

extern const rcs_nav_config rcs_route_config;
extern const rcs_waypoint rcs_route_points[RCS_ROUTE_POINT_COUNT];
extern const rcs_segment rcs_route_segments[RCS_ROUTE_SEGMENT_COUNT];

/* 导出函数 ------------------------------------------------------------------------------------------*/

/**
 * @brief 初始化导航并绑定航点动作，不启动电机
 * @param navigator 导航实例；重新初始化前先停止旧实例
 * @param action_context 用户机构状态，可为空
 * @return 1 成功，0 配置或动作绑定失败
 */
int rcs_route_init(rcs_navigator *navigator, void *action_context);

#ifdef __cplusplus
}
#endif

extern const char rcs_route_revision[];
extern const rcs_point_plan rcs_route_plan[RCS_ROUTE_POINT_COUNT];
