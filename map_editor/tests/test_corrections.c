/* 独立观测纠错的确定性测试；不代表真实传感器、打滑或实车验证。 */
#include "rcs_nav.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
static unsigned checks, reads, enters, sequence_mode;
static int available;
static rcs_observation sample;
static rcs_nav_config config = {
 {RCS_OMNI3,.1f,.3f,.3f,.2f,0,19.2032f,3000},8192,{1,1,1,1},
 .3f,1,.3f,2,2,3,.005f,.01f,.05f,60,1};
static rcs_waypoint points[2] = {{0,0,0,.2f,0},{.2f,0,0,.2f,0}};
static rcs_point_plan plans[2];
static rcs_correction specs[2];
static rcs_correction_group groups[2];
static rcs_step arrival;
#define CHECK(x) do {checks++;if (!(x)){fprintf(stderr,"FAIL correction %d: %s\n",__LINE__,#x);exit(1);}}while(0)
static int read_observation(void *ctx,unsigned source,rcs_observation *out)
{
 (void)ctx;CHECK(source==0);reads++;
 if(sequence_mode==0)sample.sequence++;
 *out=sample;return available;
}
static rcs_action_result action(void *ctx,size_t index,rcs_action_phase phase,float elapsed)
{
 (void)ctx;(void)index;(void)elapsed;if(phase==RCS_ACTION_ENTER)enters++;return RCS_ACTION_DONE;
}
static void setup(rcs_navigator *n,unsigned count,unsigned kind)
{
 reads=enters=sequence_mode=0;available=1;memset(&sample,0,sizeof sample);sample.axes=7;sample.quality=1;
 memset(plans,0,sizeof plans);memset(groups,0,sizeof groups);memset(&arrival,0,sizeof arrival);
 arrival.kind=RCS_STEP_CUSTOM;arrival.timeout_s=2;arrival.settle_s=.01f;arrival.drift_limit_m=.2f;
 for(unsigned i=0;i<2;i++){plans[i].arrival_radius_m=.04f;plans[i].correction_speed_mps=.05f;
  plans[i].correction_timeout_s=5;plans[i].settle_s=.01f;plans[i].drift_limit_m=.2f;
  plans[i].correction_mode=2;plans[i].correction_retries=2;plans[i].departure_at_end=1;}
 plans[0].arrival=&arrival;plans[0].arrival_count=1;
 specs[0]=(rcs_correction){0,kind==1?3:kind==2?4:7,(uint8_t)kind,
  2,.1f,.8f,.2f,.5f,.08f,.5f,.005f,.01f,.02f};specs[1]=specs[0];
 groups[0].steps=specs;groups[0].count=count;
 CHECK(rcs_nav_init(n,&config,points,2));CHECK(rcs_nav_bind_plan(n,plans,2,action,NULL));
 CHECK(rcs_nav_bind_corrections(n,groups,2,read_observation,NULL));CHECK(rcs_nav_reset(n,0,0,0));
 float out[3];int32_t delta[4]={0};CHECK(rcs_nav_update(n,delta,7,.01f,out));CHECK(rcs_nav_start(n));
}
static int tick(rcs_navigator *n,float out[3])
{
 int32_t delta[4]={0};int result=rcs_nav_update(n,delta,7,.01f,out);
 CHECK(!result||hypotf(out[0],out[1])*fabsf(out[2])<1e-7f);return result;
}
static void enter_correction(rcs_navigator *n)
{
 float out[3];for(unsigned i=0;i<30&&n->stage!=RCS_STAGE_CORRECTION;i++)CHECK(tick(n,out));
 CHECK(n->stage==RCS_STAGE_CORRECTION&&enters==1);CHECK(reads==0);
}
static void empty_and_refresh(void)
{
 rcs_navigator n;float out[3];setup(&n,0,0);n.x_m=.03f;
 for(unsigned i=0;i<30&&n.target==0;i++){CHECK(tick(&n,out));CHECK(out[0]==0&&out[1]==0&&out[2]==0);}
 CHECK(n.target==1&&reads==0&&enters==1);CHECK(fabsf(n.x_m-.03f)<1e-6f);
 setup(&n,1,0);sample.x_m=.1f;sample.y_m=.02f;sample.yaw_rad=.15f;enter_correction(&n);
 CHECK(tick(&n,out));CHECK(out[0]==0&&out[1]==0&&out[2]==0);
 CHECK(fabsf(n.x_m-.1f)<1e-6f&&fabsf(n.y_m-.02f)<1e-6f&&fabsf(n.yaw_rad-.15f)<1e-6f);
 CHECK(n.action_index==1&&enters==1);CHECK(tick(&n,out));CHECK(n.stage==RCS_STAGE_DEPARTURE);
 setup(&n,1,0);specs[0].axes=3;sample.x_m=.1f;sample.yaw_rad=.2f;enter_correction(&n);
 CHECK(tick(&n,out));CHECK(n.yaw_rad==0&&n.x_m==.1f);
 puts("PASS empty group skips legacy correction; refresh rebases pose without motion or action replay");
}
static void align(void)
{
 rcs_navigator n;float out[3];setup(&n,1,1);sample.x_m=.1f;enter_correction(&n);
 CHECK(tick(&n,out));CHECK(out[0]<0&&out[1]==0&&out[2]==0);sample.x_m=0;
 for(unsigned i=0;i<100&&n.stage==RCS_STAGE_CORRECTION;i++)CHECK(tick(&n,out));
 CHECK(n.stage==RCS_STAGE_DEPARTURE&&enters==1);
 setup(&n,1,2);sample.yaw_rad=.2f;enter_correction(&n);CHECK(tick(&n,out));
 CHECK(out[0]==0&&out[1]==0&&out[2]<0);sample.yaw_rad=0;
 for(unsigned i=0;i<100&&n.stage==RCS_STAGE_CORRECTION;i++)CHECK(tick(&n,out));
 CHECK(n.stage==RCS_STAGE_DEPARTURE&&enters==1);
 setup(&n,1,1);sample.x_m=.1f;plans[0].drift_limit_m=.02f;enter_correction(&n);CHECK(tick(&n,out));sample.x_m=.13f;CHECK(!tick(&n,out));CHECK(n.fault==RCS_NAV_ACTION_DRIFT&&out[0]==0);
 puts("PASS active XY/yaw alignment uses independent observed error and never mixes translation/spin");
}
static void failures(void)
{
 for(unsigned k=0;k<10;k++){rcs_navigator n;float out[3]={1,1,1};setup(&n,1,1);enter_correction(&n);
  if(k==0)available=0;
  if(k==1)sample.age_s=.2f;
  if(k==2)sample.quality=.1f;
  if(k==3)sample.axes=4;
  if(k==4)sample.x_m=NAN;
  if(k==5)sample.x_m=.5f;
  if(k==6)sample.quality=INFINITY;
  if(k==7)sample.age_s=-1;
  if(k==8)sample.axes=255;
  if(k==9)sample.yaw_rad=100;
  CHECK(!tick(&n,out));CHECK(n.state==RCS_NAV_FAULT);CHECK(out[0]==0&&out[1]==0&&out[2]==0);
  CHECK(n.fault==(k==5?RCS_NAV_OBSERVATION_JUMP:RCS_NAV_OBSERVATION));CHECK(enters==1);
  rcs_nav_fault first=n.fault;available=1;sample.x_m=0;CHECK(!tick(&n,out));CHECK(n.fault==first);
 }
 rcs_navigator n;float out[3];setup(&n,1,1);specs[0].timeout_s=.025f;enter_correction(&n);sample.x_m=.1f;
 CHECK(tick(&n,out));CHECK(tick(&n,out));CHECK(!tick(&n,out));CHECK(n.fault==RCS_NAV_TIMEOUT);
 puts("PASS missing/invalid/low-quality/stale/jumping feedback and timeout latch fault with zero command");
}
static void sequence(void)
{
 rcs_navigator n;float out[3];setup(&n,1,1);sample.x_m=.1f;sequence_mode=1;sample.sequence=100;enter_correction(&n);
 CHECK(tick(&n,out));for(unsigned i=0;i<15&&n.state!=RCS_NAV_FAULT;i++)tick(&n,out);
 CHECK(n.fault==RCS_NAV_OBSERVATION&&out[0]==0);
 setup(&n,1,1);sample.x_m=.1f;sequence_mode=1;sample.sequence=100;enter_correction(&n);
 CHECK(tick(&n,out));sample.sequence=90;CHECK(!tick(&n,out));CHECK(n.fault==RCS_NAV_OBSERVATION);
 setup(&n,1,1);sample.x_m=.1f;sequence_mode=1;sample.sequence=0xfffffffeu;enter_correction(&n);
 CHECK(tick(&n,out));sample.sequence=1;CHECK(tick(&n,out));CHECK(n.state==RCS_NAV_RUNNING);
 setup(&n,2,0);sequence_mode=1;enter_correction(&n);CHECK(tick(&n,out));CHECK(n.action_index==1);
 CHECK(tick(&n,out));CHECK(n.action_index==1);sample.sequence++;CHECK(tick(&n,out));CHECK(n.action_index==2);
 puts("PASS repeated sequence expires without reapplication; replay rejected; wrap accepted; second refresh needs new sample");
}
static void malformed(void)
{
 for(unsigned k=0;k<6;k++){rcs_navigator n;setup(&n,1,1);
  CHECK(rcs_nav_reset(&n,0,0,0));
  if(k==0)specs[0].source=32;
  if(k==1)specs[0].axes=4;
  if(k==2)specs[0].max_age_s=NAN;
  if(k==3)groups[0].count=33;
  if(k==4)specs[0].min_quality=2;
  if(k==5)groups[0].steps=NULL;
  CHECK(!rcs_nav_bind_corrections(&n,groups,2,read_observation,NULL));CHECK(n.fault==RCS_NAV_OBSERVATION_CONFIG);
 }
 puts("PASS malformed correction specifications rejected before activation");
}
int main(void){empty_and_refresh();align();failures();sequence();malformed();printf("PASS %u independent correction assertions\n",checks);return 0;}
