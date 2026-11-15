/* Example application task adapter. Add to your own task, never main.c. */
#ifndef ROBOT_ROUTE_TASK_H
#define ROBOT_ROUTE_TASK_H
#include "rcs_nav.h"
/* RCS_Init must already be called by your chassis module. */
int RobotRoute_Init(void);
/* Physically locate at P0 and clear old acquisition deltas before this call. */
int RobotRoute_ResetToStart(void);
/* Explicit operator trigger only, AFTER a fresh Tick following reset. */
int RobotRoute_Start(void);
void RobotRoute_Stop(void);
/* Task context. Raw deltas W0..W3; age/sequence-validated mask and real dt. */
void RobotRoute_Tick(const int32_t delta[4],uint8_t fresh_mask,float dt_s);
const rcs_navigator *RobotRoute_Status(void);
#endif
