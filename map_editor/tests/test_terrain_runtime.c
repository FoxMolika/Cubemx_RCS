/* 坡面执行器算术和故障测试；制动接口为模拟，不是实际电机制动证明。 */
#include "rcs_terrain.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
static unsigned checks,check_calls,hold_calls;
static int brake_ready=1;
#define CHECK(x) do{checks++;if(!(x)){fprintf(stderr,"FAIL terrain runtime %d: %s\n",__LINE__,#x);exit(1);}}while(0)
static int brake(void *ctx,rcs_terrain_brake_request request)
{
 (void)ctx;if(request==RCS_TERRAIN_BRAKE_CHECK)check_calls++;else hold_calls++;return brake_ready;
}
static rcs_terrain_node nodes[3]={{0,0,0,.463647609f,2},{1.118033989f,1,.5f,.463647609f,2},{2.236067978f,2,1,.463647609f,2}};
static rcs_terrain_leg legs[2]={{0},{nodes,3,0,0,0,1}};
static rcs_terrain_config cfg={.3f,.3f,.04f,.1f,1};
static void setup(rcs_terrain *t)
{
 brake_ready=1;check_calls=hold_calls=0;CHECK(rcs_terrain_init(t,legs,2,&cfg,brake,NULL));CHECK(rcs_terrain_arm(t));CHECK(rcs_terrain_begin(t,1,0,0));
}
static void arithmetic(void)
{
 rcs_terrain t;float dx,dy,out[3];setup(&t);CHECK(rcs_terrain_integrate(&t,1,0,.1f,&dx,&dy));CHECK(fabsf(t.progress_m-.1f)<1e-6f);CHECK(fabsf(dx-.089442719f)<1e-6f&&dy==0);CHECK(fabsf(t.z_plan_m-.04472136f)<1e-6f);
 CHECK(rcs_terrain_integrate(&t,-1,0,.1f,&dx,&dy));CHECK(fabsf(t.progress_m)<1e-6f&&dx<0);CHECK(rcs_terrain_begin(&t,1,.5f,0));CHECK(fabsf(t.horizontal_m-.5f)<1e-6f&&fabsf(t.progress_m-.55901699f)<1e-6f);
 CHECK(rcs_terrain_integrate(&t,.5f,.02f,.1f,&dx,&dy));CHECK(fabsf(dy-.002f)<1e-6f);CHECK(rcs_terrain_command(&t,.5f,0,3.14159265f,.02f,.2f,.01f,out));CHECK(out[0]>0&&out[1]==0&&out[2]==0);
 CHECK(rcs_terrain_request_hold(&t));CHECK(t.hold_requested&&t.hold_accepted&&hold_calls==1);
 setup(&t);legs[1].bearing_rad=1.570796327f;CHECK(rcs_terrain_begin(&t,1,0,0));CHECK(rcs_terrain_integrate(&t,0,1,.1f,&dx,&dy));CHECK(fabsf(dx)<1e-6f&&fabsf(dy-.089442719f)<1e-6f);legs[1].bearing_rad=0;
 puts("PASS surface arclength is integrated once; horizontal projection, reverse, offset start and rotated direction");
}
static void faults(void)
{
 rcs_terrain t;float dx=1,dy=1,out[3]={1,1,1};setup(&t);CHECK(!rcs_terrain_begin(&t,1,0,.1f));CHECK(t.fault==RCS_TERRAIN_ENTRY);
 setup(&t);CHECK(!rcs_terrain_command(&t,0,0,.5f,.02f,.2f,.01f,out));CHECK(t.fault==RCS_TERRAIN_ENTRY&&out[0]==0);
 setup(&t);CHECK(!rcs_terrain_integrate(&t,NAN,0,.01f,&dx,&dy));CHECK(dx==0&&dy==0&&t.fault==RCS_TERRAIN_CONFIG);
 setup(&t);CHECK(!rcs_terrain_integrate(&t,4,0,.01f,&dx,&dy));CHECK(t.fault==RCS_TERRAIN_OVERSPEED);
 setup(&t);brake_ready=0;CHECK(!rcs_terrain_integrate(&t,.1f,0,.01f,&dx,&dy));CHECK(t.fault==RCS_TERRAIN_BRAKE_UNAVAILABLE);CHECK(!rcs_terrain_request_hold(&t));CHECK(t.hold_requested&&!t.hold_accepted);
 CHECK(rcs_terrain_init(&t,legs,2,&cfg,NULL,NULL));CHECK(!rcs_terrain_arm(&t));CHECK(t.fault==RCS_TERRAIN_BRAKE_UNAVAILABLE);
 rcs_terrain_config no=cfg;no.brake_validated=0;CHECK(rcs_terrain_init(&t,legs,2,&no,brake,NULL));CHECK(!rcs_terrain_arm(&t));
 setup(&t);CHECK(!rcs_terrain_integrate(&t,-2,0,.1f,&dx,&dy));CHECK(t.fault==RCS_TERRAIN_TRACK);
 puts("PASS entry/lateral/heading, nonfinite samples, overspeed, unavailable/lost braking and rollback bounds fail closed");
}
static void malformed(void)
{
 rcs_terrain t;rcs_terrain_node saved=nodes[1];nodes[1].s_m=.5f;CHECK(!rcs_terrain_init(&t,legs,2,&cfg,brake,NULL));nodes[1]=saved;nodes[1].h_m=0;CHECK(!rcs_terrain_init(&t,legs,2,&cfg,brake,NULL));nodes[1]=saved;
 nodes[1].pitch_rad=2;CHECK(!rcs_terrain_init(&t,legs,2,&cfg,brake,NULL));nodes[1]=saved;nodes[1].limit_mps=0;CHECK(!rcs_terrain_init(&t,legs,2,&cfg,brake,NULL));nodes[1]=saved;
 rcs_terrain_config bad=cfg;bad.deceleration_mps2=0;CHECK(!rcs_terrain_init(&t,legs,2,&bad,brake,NULL));bad=cfg;bad.acceleration_mps2=INFINITY;CHECK(!rcs_terrain_init(&t,legs,2,&bad,brake,NULL));
 puts("PASS malformed/nonmonotone geometry and invalid limits rejected before execution");
}
static void lookahead(void)
{
 rcs_terrain t;rcs_terrain_node flat[4]={{0,0,0,0,.5f},{1,1,0,0,.5f},{2,2,0,0,.1f},{3,3,0,0,.1f}};
 rcs_terrain_leg path[2]={{0},{flat,4,0,0,0,0}};rcs_terrain_config slow={1,.1f,.04f,.1f,0};float out[3];CHECK(rcs_terrain_init(&t,path,2,&slow,NULL,NULL));CHECK(rcs_terrain_arm(&t));CHECK(rcs_terrain_begin(&t,1,0,0));
 t.progress_m=.9f;t.horizontal_m=.9f;for(unsigned i=0;i<100;i++){CHECK(rcs_terrain_command(&t,.9f,0,0,.01f,1,.01f,out));CHECK(out[0]<=.5f+.0001f);}float early=out[0];
 t.progress_m=1.9f;t.horizontal_m=1.9f;for(unsigned i=0;i<500;i++)CHECK(rcs_terrain_command(&t,1.9f,0,0,.01f,1,.01f,out));CHECK(out[0]<early&&out[0]<=.1001f);
 t.progress_m=3;t.horizontal_m=3;for(unsigned i=0;i<200;i++)CHECK(rcs_terrain_command(&t,3,0,0,.01f,1,.01f,out));CHECK(out[0]==0);CHECK(rcs_terrain_command(&t,3,0,0,.01f,1,.01f,out)==2);CHECK(!rcs_terrain_request_hold(&t));
 puts("PASS future lower limits reduce speed before entry; flat elevated motion needs no slope brake port; endpoint stops");
}
int main(void){arithmetic();faults();malformed();lookahead();printf("PASS %u terrain runtime assertions\n",checks);return 0;}
