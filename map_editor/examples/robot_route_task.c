/* Example application task adapter. CubeMX-independent, no main/callbacks. */
#include "robot_route_task.h"
#include "rcs_route.h"
#include "rcs_hal.h"
static rcs_navigator navigator;
static int initialized;
static rcs_fault saved_chassis_fault;
static void fault_stop(void)
{
    if (saved_chassis_fault == RCS_OK)
        saved_chassis_fault = RCS_Fault();
    rcs_nav_stop(&navigator);
#ifdef RCS_TERRAIN_ENABLED
    /* 真实驱动若接受保持请求，则继续周期续期；未接受时仍按原保护锁定。
     * 不能为驻坡绕过RCS_FAULT；零电流也不代表坡上已静止。
     */
    if (rcs_terrain_request_hold(&navigator.terrain))
        return;
#endif
    RCS_Lock();
}
int RobotRoute_Init(void)
{
    RCS_Lock();
    if (initialized)
        rcs_nav_stop(&navigator); /* cancel before reinitializing */
    saved_chassis_fault = RCS_OK;
    initialized = rcs_route_init(&navigator, 0); /* binds generated waypoint actions */
    return initialized;
}
int RobotRoute_ResetToStart(void)
{
    RCS_Lock();
    return rcs_nav_reset(&navigator, rcs_route_points[0].x_m, rcs_route_points[0].y_m,
                         rcs_route_points[0].yaw_rad);
}
int RobotRoute_Start(void)
{
    if (!RCS_Arm())
        return 0;
    if (!rcs_nav_start(&navigator)) {
        RCS_Lock();
        return 0;
    }
    return 1;
}
void RobotRoute_Stop(void)
{
    rcs_nav_stop(&navigator);
#ifdef RCS_TERRAIN_ENABLED
    /* 真实驱动若接受保持请求，则继续周期续期；未接受时仍按原保护锁定。
     * 不能为驻坡绕过RCS_FAULT；零电流也不代表坡上已静止。
     */
    if (rcs_terrain_request_hold(&navigator.terrain))
        return;
#endif
    RCS_Lock();
}
void RobotRoute_Tick(const int32_t delta[4], uint8_t fresh, float dt)
{
    float velocity[3];
    if (RCS_State() == RCS_FAULT) {
        fault_stop();
        return;
    }
    if (!rcs_nav_update(&navigator, delta, fresh, dt, velocity)) {
        fault_stop();
        return;
    }
    if (navigator.state != RCS_NAV_RUNNING) {
#ifdef RCS_TERRAIN_ENABLED
        if (rcs_terrain_request_hold(&navigator.terrain))
            return;
#endif
        RCS_Lock();
        return;
    }
    if (!RCS_SetVelocity(velocity[0], velocity[1], velocity[2])) {
        fault_stop();
    }
}
const rcs_navigator *RobotRoute_Status(void)
{
    return &navigator;
}
/* Keep scheduling RCS_Process at RCS_PERIOD_MS even while locked. All RCS calls
 * are single-task APIs: schedule them in the same task, synchronize ISR handoff.
 * Never auto-call Start in Tick. Navigation has no CAN RX/angle unwrap adapter:
 * acquisition must aggregate raw deltas and reject stale/ambiguous samples. */

/* Inspect this before explicitly resetting after a fault; ordinary locking in the
 * adapter does not erase the retained chassis cause. Navigator fault_stage and
 * fault_action identify the first failing stage and group action index. */
rcs_fault RobotRoute_ChassisFault(void)
{
    return saved_chassis_fault;
}
