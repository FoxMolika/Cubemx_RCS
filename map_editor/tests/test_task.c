#include "robot_route_task.h"
#include "rcs_route.h"
#include <stdio.h>
#include <stdlib.h>
static int arm_ok=1,send_ok=1,armed,locks,sends;
#define CHECK(x) do { if(!(x)){fprintf(stderr,"task FAIL %d\n",__LINE__);exit(1);} }while(0)
int RCS_Arm(void){armed=arm_ok;return arm_ok;}
void RCS_Lock(void){armed=0;locks++;}
int RCS_SetVelocity(float x,float y,float w){(void)x;(void)y;(void)w;sends++;return armed&&send_ok;}
int main(void){
    int32_t zero[4]={0};CHECK(RobotRoute_Init());CHECK(!armed);CHECK(!RobotRoute_Start());CHECK(!armed);
    CHECK(RobotRoute_ResetToStart());CHECK(!RobotRoute_Start());CHECK(!armed);
    RobotRoute_Tick(zero,15,.01f);arm_ok=0;CHECK(!RobotRoute_Start());CHECK(!armed);
    arm_ok=1;CHECK(RobotRoute_Start());CHECK(armed);RobotRoute_Tick(zero,15,.01f);CHECK(sends>0);
    RobotRoute_Tick(zero,0,.01f);CHECK(!armed);CHECK(RobotRoute_Status()->state==RCS_NAV_FAULT);CHECK(!RobotRoute_Start());CHECK(!armed);
    CHECK(RobotRoute_ResetToStart());RobotRoute_Tick(zero,15,.01f);CHECK(RobotRoute_Start());send_ok=0;RobotRoute_Tick(zero,15,.01f);CHECK(!armed);CHECK(RobotRoute_Status()->state==RCS_NAV_IDLE);
    RobotRoute_Stop();CHECK(!armed&&locks>0);puts("PASS task adapter arm gating, feedback failure and command failure");return 0;
}
