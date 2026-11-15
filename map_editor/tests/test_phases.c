/* Deterministic tests of phase transitions and a closed-loop ideal wheel plant.
 * This is not a physical-CAN or ground-slip validation. */
#include "rcs_nav.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#define PI 3.14159265358979323846f
static unsigned checks,entered[4],cancelled[4],seen_stages;
#define CHECK(x) do { ++checks; if(!(x)) { fprintf(stderr,"FAIL line %d: %s\n",__LINE__,#x);exit(1); } } while(0)
static const rcs_nav_config config={
 {RCS_OMNI3,.1f,.3f,.3f,.2f,0,19.2032f,3000},8192,{1,1,1,1},
 .3f,1,.3f,2,2,3,.005f,.01f,.05f,60,1};
static rcs_waypoint points[3]={{0,0,0,.2f,0},{.2f,0,-1,.2f,0},{.2f,.2f,1,.2f,0}};
static rcs_action_result action(void *ctx,size_t index,rcs_action_phase phase,float elapsed)
{
 (void)ctx;CHECK(index<4);
 if(phase==RCS_ACTION_ENTER){entered[index]++;return RCS_ACTION_BUSY;}
 if(phase==RCS_ACTION_CANCEL){cancelled[index]++;return RCS_ACTION_DONE;}
 return elapsed>=.03f?RCS_ACTION_DONE:RCS_ACTION_BUSY;
}
static rcs_step step(rcs_step_kind kind,float value,size_t index)
{
 rcs_step s={kind,value,30,1,2,.01f,.05f,.05f,0,index};return s;
}
static rcs_point_plan plan(void)
{
 rcs_point_plan p={0};p.arrival_radius_m=.02f;p.correction_speed_mps=.05f;
 p.correction_timeout_s=20;p.settle_s=.05f;p.drift_limit_m=.05f;
 p.correction_mode=2;p.correction_retries=2;p.departure_at_end=1;return p;
}
static void init(rcs_navigator *n,rcs_point_plan *p,size_t count)
{
 memset(entered,0,sizeof entered);memset(cancelled,0,sizeof cancelled);
 CHECK(rcs_nav_init(n,&config,points,count));CHECK(rcs_nav_bind_plan(n,p,count,action,NULL));
 CHECK(rcs_nav_reset(n,0,0,0));float out[3];int32_t delta[4]={0};
 CHECK(!rcs_nav_start(n));CHECK(rcs_nav_update(n,delta,7,.01f,out));CHECK(rcs_nav_start(n));
}
static int tick(rcs_navigator *n,float cmd[3],float residual[4])
{
 float rpm[4];int32_t delta[4]={0};CHECK(rcs_inverse(&config.geometry,cmd[0],cmd[1],cmd[2],rpm));
 for(unsigned i=0;i<3;i++){float count=rpm[i]/60*8192*.01f+residual[i];delta[i]=(int32_t)lroundf(count);residual[i]=count-delta[i];}
 rcs_nav_stage old_stage=n->stage;size_t old_index=n->action_index;
 int result=rcs_nav_update(n,delta,7,.01f,cmd);
 if(n->stage!=old_stage||n->action_index!=old_index)CHECK(cmd[0]==0&&cmd[1]==0&&cmd[2]==0);
 CHECK(!result || hypotf(cmd[0],cmd[1])*fabsf(cmd[2])<1e-7f);
 if(n->stage==RCS_STAGE_MOVE) CHECK(fabsf(cmd[2])<1e-7f);
 seen_stages|=1u<<n->stage;
 return result;
}
static void full_route(void)
{
 rcs_point_plan plans[3]={plan(),plan(),plan()};rcs_step a[2]={step(RCS_STEP_ROTATE_RELATIVE,PI/2,0),step(RCS_STEP_CUSTOM,0,0)};
 rcs_step b=step(RCS_STEP_CUSTOM,0,1),c=step(RCS_STEP_WAIT,.05f,0);
 plans[0].arrival=a;plans[0].arrival_count=2;plans[0].departure=&b;plans[0].departure_count=1;
 plans[1].departure=&c;plans[1].departure_count=1;
 rcs_navigator n;init(&n,plans,3);float cmd[3]={0},residual[4]={0};unsigned i;
 for(i=0;i<30000&&n.state==RCS_NAV_RUNNING;i++)CHECK(tick(&n,cmd,residual));
 CHECK(n.state==RCS_NAV_DONE);CHECK(entered[0]==1&&entered[1]==1);
 CHECK(cancelled[0]==0&&cancelled[1]==0);CHECK(fabsf(remainderf(n.yaw_rad-PI/2,2*PI))<.02f);
 CHECK(hypotf(n.x_m-.2f,n.y_m-.2f)<.006f);
 CHECK(seen_stages==127);puts("PASS whole phase route: no mixed translation/spin, intended heading retained, actions once");
}
static void multi_turn(void)
{
 const float turns[]={2*PI,2.5f*PI,2*PI};
 for(unsigned k=0;k<3;k++) {
  rcs_point_plan plans[2]={plan(),plan()};rcs_step a=step(RCS_STEP_ROTATE_RELATIVE,turns[k],0);
  a.direction=k==2?-1:0;plans[0].arrival=&a;plans[0].arrival_count=1;
  rcs_navigator n;init(&n,plans,2);float cmd[3]={0},residual[4]={0};
  for(unsigned i=0;i<15000&&n.target==0;i++)CHECK(tick(&n,cmd,residual));
  CHECK(n.target==1);float expected=k==2?-turns[k]:turns[k];
  CHECK(fabsf(n.yaw_unwrapped-expected)<.03f);
 }
 puts("PASS relative 360/450 degrees and explicit CW360 preserve full latched travel");
}
static void fault_cancel(void)
{
 rcs_point_plan plans[2]={plan(),plan()};rcs_step a=step(RCS_STEP_CUSTOM,0,0);plans[0].arrival=&a;plans[0].arrival_count=1;
 rcs_navigator n;init(&n,plans,2);float cmd[3]={0},residual[4]={0};
 for(unsigned i=0;i<100&&!n.action_active;i++)CHECK(tick(&n,cmd,residual));
 CHECK(n.action_active);int32_t delta[4]={0};CHECK(!rcs_nav_update(&n,delta,3,.01f,cmd));
 CHECK(n.fault==RCS_NAV_FEEDBACK&&n.fault_stage==RCS_STAGE_ARRIVAL);CHECK(cancelled[0]==1);
 rcs_nav_stop(&n);rcs_nav_stop(&n);CHECK(cancelled[0]==1&&n.fault==RCS_NAV_FEEDBACK);CHECK(!rcs_nav_start(&n));
 puts("PASS feedback fault cancels once, preserves first reason and blocks automatic restart");
}
static void exit_gate(void)
{
 rcs_point_plan plans[2]={plan(),plan()};rcs_step a=step(RCS_STEP_CUSTOM,0,0);plans[0].departure=&a;plans[0].departure_count=1;
 rcs_navigator n;init(&n,plans,2);float cmd[3]={0},residual[4]={0};
 for(unsigned i=0;i<200&&n.stage!=RCS_STAGE_EXIT;i++)CHECK(tick(&n,cmd,residual));
 CHECK(n.stage==RCS_STAGE_EXIT&&entered[0]==1);n.x_m+=.03f;
 CHECK(!tick(&n,cmd,residual));CHECK(n.fault==RCS_NAV_POSE&&n.fault_stage==RCS_STAGE_EXIT);
 CHECK(entered[0]==1);CHECK(cmd[0]==0&&cmd[1]==0&&cmd[2]==0);
 puts("PASS departure drift fails exit gate without replaying actions or correction");
}
static void spin_drift_timeout(void)
{
 rcs_point_plan plans[2]={plan(),plan()};rcs_step a=step(RCS_STEP_ROTATE_ABSOLUTE,PI/2,0);plans[0].arrival=&a;plans[0].arrival_count=1;
 rcs_navigator n;init(&n,plans,2);float cmd[3]={0},residual[4]={0};
 for(unsigned i=0;i<200&&!n.action_active;i++)CHECK(tick(&n,cmd,residual));
 n.x_m+=.06f;CHECK(!tick(&n,cmd,residual));CHECK(n.fault==RCS_NAV_ACTION_DRIFT);
 a.timeout_s=.1f;init(&n,plans,2);int32_t delta[4]={0};
 for(unsigned i=0;i<200&&n.state==RCS_NAV_RUNNING;i++)(void)rcs_nav_update(&n,delta,7,.01f,cmd);
 CHECK(n.fault==RCS_NAV_ACTION_TIMEOUT);CHECK(cmd[0]==0&&cmd[1]==0&&cmd[2]==0);
 puts("PASS spin drift and stalled-angle timeout stop output");
}
static void correction_retries(void)
{
 rcs_point_plan plans[2]={plan(),plan()};rcs_step a=step(RCS_STEP_CUSTOM,0,0);plans[0].arrival=&a;plans[0].arrival_count=1;
 rcs_navigator n;init(&n,plans,2);float cmd[3]={0},residual[4]={0};unsigned injected=0;
 for(unsigned i=0;i<5000&&n.target==0;i++) {
  if(n.stage==RCS_STAGE_CORRECT_YAW&&!injected) {n.x_m+=.02f;injected=1;}
  CHECK(tick(&n,cmd,residual));
 }
 CHECK(injected&&n.target==1&&entered[0]==1&&n.correction_pass==1);
 init(&n,plans,2);memset(cmd,0,sizeof cmd);memset(residual,0,sizeof residual);
 for(unsigned i=0;i<10000&&n.state==RCS_NAV_RUNNING;i++) {
  if(n.stage==RCS_STAGE_CORRECT_YAW)n.x_m=.02f;
  (void)tick(&n,cmd,residual);
 }
 CHECK(n.state==RCS_NAV_FAULT&&n.fault==RCS_NAV_POSE&&entered[0]==1);
 puts("PASS bounded correction retries neither replay arrival actions nor loop forever");
}
static void invalid_feedback(void)
{
 rcs_point_plan plans[2]={plan(),plan()};rcs_navigator n;float out[3]={1,1,1};int32_t delta[4]={0};
 init(&n,plans,2);CHECK(!rcs_nav_update(&n,delta,0,.01f,out));CHECK(n.fault==RCS_NAV_FEEDBACK);
 CHECK(out[0]==0&&out[1]==0&&out[2]==0);
 CHECK(!rcs_nav_update(&n,delta,7,.01f,out));CHECK(!rcs_nav_start(&n));
 init(&n,plans,2);CHECK(!rcs_nav_update(&n,delta,7,NAN,out));CHECK(n.fault==RCS_NAV_TIMING);
 init(&n,plans,2);CHECK(!rcs_nav_reset(&n,0,0,NAN));CHECK(n.fault==RCS_NAV_POSE);
 puts("PASS fresh mask zero, NaN dt/yaw and invalid-feedback latch");
}
static void final_departure(void)
{
 rcs_point_plan plans[2]={plan(),plan()};rcs_waypoint save=points[1];points[1]=points[0];
 rcs_step a=step(RCS_STEP_CUSTOM,0,2);plans[1].departure=&a;plans[1].departure_count=1;plans[1].departure_at_end=0;
 rcs_navigator n;init(&n,plans,2);float cmd[3]={0},residual[4]={0};
 for(unsigned i=0;i<1000&&n.state==RCS_NAV_RUNNING;i++)CHECK(tick(&n,cmd,residual));
 CHECK(n.state==RCS_NAV_DONE&&entered[2]==0);points[1]=save;
 puts("PASS empty groups and configured final-departure skip");
}
static void half_turn_tie(void)
{
 rcs_point_plan plans[2]={plan(),plan()};rcs_step a=step(RCS_STEP_ROTATE_ABSOLUTE,-PI,0);
 plans[0].arrival=&a;plans[0].arrival_count=1;rcs_navigator n;init(&n,plans,2);
 float cmd[3]={0},residual[4]={0};
 for(unsigned i=0;i<200&&!n.action_active;i++)CHECK(tick(&n,cmd,residual));
 CHECK(n.action_active && n.spin_target>3.0f && cmd[2]>0);
 puts("PASS absolute180 equal-length default direction matches protractor");
}
static void wait_at_timeout(void)
{
 rcs_point_plan plans[2]={plan(),plan()};rcs_step a=step(RCS_STEP_WAIT,.05f,0);
 a.timeout_s=.05f;plans[0].arrival=&a;plans[0].arrival_count=1;
 rcs_navigator n;init(&n,plans,2);float cmd[3]={0},residual[4]={0};
 for(unsigned i=0;i<300&&n.target==0;i++)CHECK(tick(&n,cmd,residual));
 CHECK(n.target==1 && n.state==RCS_NAV_RUNNING);
 puts("PASS completed wait wins over timeout on the same tick");
}
int main(void)
{
 full_route();multi_turn();half_turn_tie();wait_at_timeout();fault_cancel();exit_gate();spin_drift_timeout();correction_retries();invalid_feedback();final_departure();
 printf("PASS %u phase assertions\n",checks);return 0;
}
