/* 实际任务与零速端口的集成测试；模拟驱动不证明真实驻坡能力。 */
#include "robot_route_task.h"
#include "terrain_brake_port.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
static rcs_state drive_state = RCS_LOCKED;
static rcs_fault drive_fault = RCS_OK;
static int available = 1;
static unsigned checks, locks, holds, arms;
static float command[3];
#define CHECK(x) do {checks++;if(!(x)){fprintf(stderr,"terrain task FAIL %d: %s\n",__LINE__,#x);exit(1);}} while(0)
int RCS_Arm(void) {arms++;drive_state=RCS_ARMED;return 1;}
void RCS_Lock(void) {locks++;drive_state=RCS_LOCKED;command[0]=command[1]=command[2]=0;}
int RCS_SetVelocity(float x,float y,float w) {if(drive_state!=RCS_ARMED)return 0;command[0]=x;command[1]=y;command[2]=w;return 1;}
rcs_state RCS_State(void) {return drive_state;}
rcs_fault RCS_Fault(void) {return drive_fault;}
int test_terrain_brake(void *context,rcs_terrain_brake_request request)
{
 if(request==RCS_TERRAIN_BRAKE_HOLD)holds++;
 return available ? FoxmolikaTerrainBrake(context,request) : 0;
}
static void reach_ramp(void)
{
 int32_t zero[4]={0};float residual[4]={0};drive_fault=RCS_OK;available=1;
 CHECK(RobotRoute_Init());CHECK(RobotRoute_ResetToStart());
 const rcs_navigator *n=RobotRoute_Status();unsigned wheels=rcs_wheel_count(&n->cfg.geometry);uint8_t mask=(uint8_t)((1u<<wheels)-1u);
 RobotRoute_Tick(zero,mask,.01f);CHECK(RobotRoute_Start());
 unsigned tick=0;
 while(fabsf(n->terrain.pitch_rad)<.04f&&tick++<6000){float rpm[4];int32_t delta[4]={0};CHECK(rcs_inverse(&n->cfg.geometry,command[0],command[1],command[2],rpm));
  for(unsigned i=0;i<wheels;i++){float v=rpm[i]/60*n->cfg.counts_per_motor_rev*.01f+residual[i];delta[i]=(int32_t)lroundf(v);residual[i]=v-delta[i];}
  RobotRoute_Tick(delta,mask,.01f);CHECK(n->state==RCS_NAV_RUNNING);
 }
 CHECK(fabsf(n->terrain.pitch_rad)>.04f);CHECK(drive_state==RCS_ARMED);
}
int main(void)
{
 int32_t zero[4]={0};reach_ramp();unsigned before=locks,requested=holds;
 RobotRoute_Stop();CHECK(locks==before&&holds>requested&&drive_state==RCS_ARMED);CHECK(hypotf(command[0],command[1])==0&&command[2]==0);
 requested=holds;RobotRoute_Tick(zero,15,.01f);CHECK(holds>requested&&locks==before);CHECK(RobotRoute_Status()->state==RCS_NAV_IDLE);
 available=0;RobotRoute_Tick(zero,15,.01f);CHECK(locks>before&&drive_state==RCS_LOCKED);
 reach_ramp();before=locks;unsigned arm_before=arms;drive_state=RCS_FAULT;drive_fault=RCS_FEEDBACK_TIMEOUT;RobotRoute_Tick(zero,15,.01f);
 CHECK(locks>before&&drive_state==RCS_LOCKED);CHECK(arms==arm_before);CHECK(RobotRoute_ChassisFault()==RCS_FEEDBACK_TIMEOUT);
 RobotRoute_Tick(zero,15,.01f);CHECK(arms==arm_before);CHECK(RobotRoute_ChassisFault()==RCS_FEEDBACK_TIMEOUT);
 printf("PASS %u terrain task assertions: real task and brake port; accepted hold renewed; unavailable/fault locks; retained cause; no auto arm\n",checks);return 0;
}
