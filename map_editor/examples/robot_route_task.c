/* Example application task adapter. CubeMX-independent, no main/callbacks. */
#include "robot_route_task.h"
#include "rcs_route.h"
#include "rcs_hal.h"
static rcs_navigator navigator;
static int initialized;
int RobotRoute_Init(void) {
    RCS_Lock();
    if(initialized) rcs_nav_stop(&navigator); /* cancel before reinitializing */
    initialized=rcs_route_init(&navigator,0); /* binds generated waypoint actions */
    return initialized;
}
int RobotRoute_ResetToStart(void) {
    RCS_Lock();
    return rcs_nav_reset(&navigator,rcs_route_points[0].x_m,rcs_route_points[0].y_m,rcs_route_points[0].yaw_rad);
}
int RobotRoute_Start(void) {
    if(!RCS_Arm()) return 0;
    if(!rcs_nav_start(&navigator)) { RCS_Lock();return 0; }
    return 1;
}
void RobotRoute_Stop(void) { rcs_nav_stop(&navigator);RCS_Lock(); }
void RobotRoute_Tick(const int32_t delta[4],uint8_t fresh,float dt) {
    float velocity[3];
    if(!rcs_nav_update(&navigator,delta,fresh,dt,velocity)) { RCS_Lock();return; }
    if(navigator.state!=RCS_NAV_RUNNING) {
        RCS_Lock();return;
    }
    if(!RCS_SetVelocity(velocity[0],velocity[1],velocity[2])) {
        rcs_nav_stop(&navigator);RCS_Lock();
    }
}
const rcs_navigator *RobotRoute_Status(void) { return &navigator; }
/* Keep scheduling RCS_Process at RCS_PERIOD_MS even while locked. All RCS calls
 * are single-task APIs: schedule them in the same task, synchronize ISR handoff.
 * Never auto-call Start in Tick. Navigation has no CAN RX/angle unwrap adapter:
 * acquisition must aggregate raw deltas and reject stale/ambiguous samples. */
