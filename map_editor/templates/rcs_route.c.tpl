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
        (rcs_layout)@LAYOUT@,
        @DIAMETER@,
        @WHEELBASE@,
        @TRACK@,
        @OMNI_RADIUS@,
        @THETA0@,
        @GEAR@,
        @MAX_RPM@
    },
    @CPR@,
    { @DIRECTIONS@ },
    @MAX_SPEED@,
    @MAX_WZ@,
    @ACCEL@,
    @ALPHA@,
    @POSITION_KP@,
    @YAW_KP@,
    @POSITION_TOLERANCE@,
    @YAW_TOLERANCE@,
    @MAX_DT@,
    @LEG_TIMEOUT@,
    @ACTIONS_REQUIRED@
};

/* 航点与分段参考 ------------------------------------------------------------------------------------*/

const rcs_waypoint rcs_route_points[RCS_ROUTE_POINT_COUNT] = {
@POINT_ROWS@};

/* 匀速估计不包含加减速、到点收敛、机构动作与停留。 */
const rcs_segment rcs_route_segments[RCS_ROUTE_SEGMENT_COUNT] = {
@SEGMENT_ROWS@};

const char rcs_route_revision[] = "@REVISION@";
@PLAN_ARRAYS@
@CORRECTION_ARRAYS@
const rcs_point_plan rcs_route_plan[RCS_ROUTE_POINT_COUNT] = {
@PLAN_ROWS@};

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
