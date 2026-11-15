#pragma once
#include "rcs_control.h"
int RCS_Arm(void);
void RCS_Lock(void);
int RCS_SetVelocity(float vx, float vy, float wz);
rcs_state RCS_State(void);
rcs_fault RCS_Fault(void);
