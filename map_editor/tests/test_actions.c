#include "rcs_actions.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
static unsigned checks;
#define CHECK(x) do{++checks;if(!(x)){fprintf(stderr,"action FAIL %d: %s\n",__LINE__,#x);exit(1);}}while(0)
int test_enter,test_done,test_cancel;
static int entered,ticked,cancelled,mode;
static const int32_t zero[4]={0};
static float out[3];
static rcs_action_spec specs[RCS_ROUTE_POINT_COUNT];
static rcs_action_result cb(void *ctx,size_t p,rcs_action_phase phase,float seconds){
 CHECK(ctx==&mode);CHECK(p==0);CHECK(seconds>=0);
 if(phase==RCS_ACTION_CANCEL){++cancelled;return RCS_ACTION_DONE;}
 if(phase==RCS_ACTION_ENTER){++entered;return mode==3?RCS_ACTION_DONE:RCS_ACTION_BUSY;}
 ++ticked;
 return mode==1?RCS_ACTION_DONE:mode==2?RCS_ACTION_ERROR:mode==4?(rcs_action_result)77:RCS_ACTION_BUSY;
}
static void init(rcs_navigator *n,int binding){
 rcs_nav_config c=rcs_route_config;c.actions_required=1;
 entered=ticked=cancelled=mode=0;memset(specs,0,sizeof(specs));specs[0].enabled=1;specs[0].timeout_s=1;
 CHECK(rcs_nav_init(n,&c,rcs_route_points,RCS_ROUTE_POINT_COUNT));
 if(binding)CHECK(rcs_nav_bind_actions(n,specs,RCS_ROUTE_POINT_COUNT,cb,&mode));
 CHECK(rcs_nav_reset(n,rcs_route_points[0].x_m,rcs_route_points[0].y_m,rcs_route_points[0].yaw_rad));
 CHECK(rcs_nav_update(n,zero,15,.01f,out));
}
static void step(rcs_navigator *n){CHECK(rcs_nav_update(n,zero,15,.01f,out));CHECK(out[0]==0&&out[1]==0&&out[2]==0);}
static void active(rcs_navigator *n){init(n,1);CHECK(rcs_nav_start(n));step(n);CHECK(entered==1&&ticked==0&&n->action_active);}
int main(void){rcs_navigator n;
 init(&n,0);CHECK(!rcs_nav_start(&n));CHECK(n.fault==RCS_NAV_ACTION_CONFIG);
 init(&n,0);CHECK(!rcs_nav_bind_actions(&n,specs,RCS_ROUTE_POINT_COUNT,0,&mode));CHECK(n.fault==RCS_NAV_ACTION_CONFIG);
 init(&n,0);specs[0].timeout_s=0;CHECK(!rcs_nav_bind_actions(&n,specs,RCS_ROUTE_POINT_COUNT,cb,&mode));
 active(&n);CHECK(!rcs_nav_bind_actions(&n,specs,RCS_ROUTE_POINT_COUNT,cb,&mode));for(int i=0;i<8;i++)step(&n);CHECK(entered==1&&ticked==8&&cancelled==0&&n.target==0);
 mode=1;step(&n);CHECK(n.action_done&&!n.action_active&&n.target==0);unsigned ticks=(unsigned)ticked;
 for(int i=0;i<200&&n.target==0;i++){step(&n);}
 CHECK(n.target==1&&entered==1&&(unsigned)ticked==ticks&&cancelled==0);
 active(&n);rcs_nav_stop(&n);rcs_nav_stop(&n);CHECK(cancelled==1&&n.state==RCS_NAV_IDLE);CHECK(!rcs_nav_start(&n));
 active(&n);CHECK(rcs_nav_reset(&n,rcs_route_points[0].x_m,rcs_route_points[0].y_m,rcs_route_points[0].yaw_rad));CHECK(cancelled==1&&!n.action_active);
 active(&n);CHECK(!rcs_nav_update(&n,zero,0,.01f,out));CHECK(n.fault==RCS_NAV_FEEDBACK&&cancelled==1&&ticked==0);rcs_nav_stop(&n);CHECK(cancelled==1);
 active(&n);specs[0].timeout_s=.02f;step(&n);CHECK(!rcs_nav_update(&n,zero,15,.01f,out));CHECK(n.fault==RCS_NAV_ACTION_TIMEOUT&&cancelled==1&&ticked==1&&out[0]==0);
 active(&n);n.x_m+=.2f;CHECK(!rcs_nav_update(&n,zero,15,.01f,out));CHECK(n.fault==RCS_NAV_ACTION_DRIFT&&cancelled==1&&ticked==0);
 active(&n);n.yaw_rad+=.3f;CHECK(!rcs_nav_update(&n,zero,15,.01f,out));CHECK(n.fault==RCS_NAV_ACTION_DRIFT&&cancelled==1);
 active(&n);mode=2;CHECK(!rcs_nav_update(&n,zero,15,.01f,out));CHECK(n.fault==RCS_NAV_ACTION_FAILED&&cancelled==1);
 active(&n);mode=4;CHECK(!rcs_nav_update(&n,zero,15,.01f,out));CHECK(n.fault==RCS_NAV_ACTION_FAILED&&cancelled==1);
 init(&n,1);mode=3;CHECK(rcs_nav_start(&n));step(&n);CHECK(n.action_done&&!n.action_active&&entered==1);rcs_nav_stop(&n);CHECK(cancelled==0);
 // Exercise the actual generated USER CODE, including completion and cancellation.
 CHECK(rcs_route_init(&n,0));CHECK(rcs_nav_reset(&n,rcs_route_points[0].x_m,rcs_route_points[0].y_m,rcs_route_points[0].yaw_rad));step(&n);CHECK(rcs_nav_start(&n));step(&n);CHECK(test_enter==1&&test_done==0);step(&n);step(&n);CHECK(test_done==1&&test_cancel==0);rcs_nav_stop(&n);CHECK(test_cancel==0);
 CHECK(rcs_nav_reset(&n,rcs_route_points[0].x_m,rcs_route_points[0].y_m,rcs_route_points[0].yaw_rad));step(&n);CHECK(rcs_nav_start(&n));step(&n);CHECK(test_enter==2);rcs_nav_stop(&n);rcs_nav_stop(&n);CHECK(test_cancel==1);
 printf("PASS %u waypoint action checks (generated C + lifecycle faults)\n",checks);return 0;
}
