/* Actual editor-exported tables + ideal incremental wheel encoder plant. */
#include "rcs_route.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
unsigned audit_enter[3],audit_done[3],audit_cancel[3];
static unsigned checks;
#define CHECK(x) do {checks++;if(!(x)){fprintf(stderr,"FAIL %d: %s state%d fault%d stage%d target%u\n",__LINE__,#x,n.state,n.fault,n.stage,(unsigned)n.target);exit(1);}}while(0)
int main(void)
{
 rcs_navigator n;float cmd[3]={0},residual[4]={0};int32_t delta[4]={0};
 CHECK(rcs_route_init(&n,NULL));
 CHECK(rcs_nav_reset(&n,rcs_route_points[0].x_m,rcs_route_points[0].y_m,rcs_route_points[0].yaw_rad));
 unsigned wheels=rcs_wheel_count(&n.cfg.geometry),mask=(1u<<wheels)-1;
 CHECK(rcs_nav_update(&n,delta,(uint8_t)mask,.01f,cmd));CHECK(rcs_nav_start(&n));
 unsigned stages=0,i;
 for(i=0;i<30000&&n.state==RCS_NAV_RUNNING;i++) {
  float rpm[4];CHECK(rcs_inverse(&n.cfg.geometry,cmd[0],cmd[1],cmd[2],rpm));
  for(unsigned j=0;j<wheels;j++) {
   float count=rpm[j]/60*n.cfg.counts_per_motor_rev*.01f*n.cfg.encoder_direction[j]+residual[j];
   delta[j]=(int32_t)lroundf(count);residual[j]=count-delta[j];
  }
  CHECK(rcs_nav_update(&n,delta,(uint8_t)mask,.01f,cmd));
  CHECK(hypotf(cmd[0],cmd[1])*fabsf(cmd[2])<1e-7f);stages|=1u<<n.stage;
 }
 CHECK(n.state==RCS_NAV_DONE);CHECK(stages==127);
 for(unsigned j=0;j<3;j++)CHECK(audit_enter[j]==1&&audit_done[j]==1&&audit_cancel[j]==0);
 CHECK(n.yaw_unwrapped < -4.6f && n.yaw_unwrapped > -4.8f);
 CHECK(hypotf(n.x_m-rcs_route_points[2].x_m,n.y_m-rcs_route_points[2].y_m)<=n.cfg.position_tolerance_m*1.01f);
 printf("PASS layout%d actual export: %u assertions, %u cycles, actions once, CW360, all stages, no mixed motion\n",n.cfg.geometry.layout,checks,i);
 return 0;
}
