/* 实际导出8文件的理想轮式反馈仿真；不代表实车摩擦/制动验证。 */
#include "rcs_route.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
static unsigned checks,brake_checks,holds;
#define CHECK(x) do{checks++;if(!(x)){fprintf(stderr,"FAIL terrain %d: %s state%d fault%d terrain%d\n",__LINE__,#x,n.state,n.fault,n.terrain.fault);exit(1);}}while(0)
int test_terrain_brake(void *context,rcs_terrain_brake_request request)
{
 (void)context;if(request==RCS_TERRAIN_BRAKE_CHECK)brake_checks++;else holds++;return 1;
}
int main(void)
{
 rcs_navigator n;float command[3]={0},residual[4]={0};int32_t zero[4]={0};unsigned saw_ramp=0,saw_correction=0,saw_seam=0;
 CHECK(rcs_route_init(&n,NULL));CHECK(n.terrain.initialized);CHECK(rcs_nav_reset(&n,rcs_route_points[0].x_m,rcs_route_points[0].y_m,rcs_route_points[0].yaw_rad));
 unsigned wheels=rcs_wheel_count(&n.cfg.geometry);uint8_t fresh=(uint8_t)((1u<<wheels)-1u);
 CHECK(rcs_nav_update(&n,zero,fresh,.01f,command));CHECK(rcs_nav_start(&n));CHECK(brake_checks==1);
 float previous_x=n.x_m,previous_y=n.y_m,previous_pitch=0;unsigned frames=0;
 float ux=rcs_route_points[n.count-1].x_m-rcs_route_points[0].x_m,uy=rcs_route_points[n.count-1].y_m-rcs_route_points[0].y_m;
 float length=hypotf(ux,uy);ux/=length;uy/=length;
 while(n.state==RCS_NAV_RUNNING&&frames++<20000){float rpm[4];int32_t delta[4]={0};CHECK(rcs_inverse(&n.cfg.geometry,command[0],command[1],command[2],rpm));
  for(unsigned i=0;i<wheels;i++){float v=rpm[i]/60*n.cfg.counts_per_motor_rev*.01f+residual[i];delta[i]=(int32_t)lroundf(v);residual[i]=v-delta[i];}
  CHECK(rcs_nav_update(&n,delta,fresh,.01f,command));CHECK(hypotf(command[0],command[1])*fabsf(command[2])<1e-6f);if(n.count==2)CHECK(fabsf(-uy*(n.x_m-rcs_route_points[0].x_m)+ux*(n.y_m-rcs_route_points[0].y_m))<.002f);CHECK(hypotf(n.x_m-previous_x,n.y_m-previous_y)<.01f);if(n.count==2)CHECK(ux*(n.x_m-previous_x)+uy*(n.y_m-previous_y)>=-.0001f);previous_x=n.x_m;previous_y=n.y_m;
  if(n.stage==RCS_STAGE_CORRECTION||n.stage==RCS_STAGE_CORRECT_XY||n.stage==RCS_STAGE_CORRECT_YAW)saw_correction++;
  if(fabsf(n.terrain.pitch_rad)>.01f)saw_ramp++;
  if(fabsf(n.terrain.pitch_rad-previous_pitch)>.00001f)saw_seam++;
  CHECK(fabsf(n.terrain.pitch_rad-previous_pitch)<.03f);previous_pitch=n.terrain.pitch_rad;
 }
 CHECK(n.state==RCS_NAV_DONE);CHECK(saw_ramp>100&&saw_seam>10&&saw_correction==0);CHECK(n.count==2||n.count==4);CHECK(hypotf(n.x_m-rcs_route_points[n.count-1].x_m,n.y_m-rcs_route_points[n.count-1].y_m)<=rcs_route_plan[n.count-1].arrival_radius_m+.002f);CHECK(holds==0);
 printf("PASS %u terrain assertions, %u cycles: actual8files, continuousup/down, noaddedwaypoints/correction, brakebinding\n",checks,frames);return 0;
}
