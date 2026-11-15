#include "rcs_route.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#define PI 3.14159265358979323846f
static unsigned checks;
#define CHECK(x) do { ++checks; if(!(x)) { fprintf(stderr,"FAIL line %d: %s\n",__LINE__,#x);exit(1); } } while(0)
static void ready(rcs_navigator *n) {
    int32_t zero[4]={0};float out[3];
    CHECK(rcs_nav_init(n,&rcs_route_config,rcs_route_points,RCS_ROUTE_POINT_COUNT));
    CHECK(!rcs_nav_start(n));
    CHECK(rcs_nav_reset(n,rcs_route_points[0].x_m,rcs_route_points[0].y_m,rcs_route_points[0].yaw_rad));
    CHECK(!rcs_nav_start(n));CHECK(rcs_nav_update(n,zero,15,.01f,out));CHECK(rcs_nav_start(n));
}
static void faults(void) {
    rcs_navigator n;float out[3]={9,9,9};int32_t zero[4]={0};
    ready(&n);CHECK(!rcs_nav_update(&n,zero,0,.01f,out));CHECK(n.fault==RCS_NAV_FEEDBACK);CHECK(out[0]==0&&out[1]==0&&out[2]==0);
    CHECK(!rcs_nav_start(&n));CHECK(!rcs_nav_update(&n,zero,15,.01f,out));CHECK(n.state==RCS_NAV_FAULT);
    CHECK(rcs_nav_reset(&n,rcs_route_points[0].x_m,rcs_route_points[0].y_m,rcs_route_points[0].yaw_rad));CHECK(!rcs_nav_start(&n));
    float dt[]={0,-1,NAN,INFINITY,1};
    for(unsigned i=0;i<sizeof(dt)/sizeof(dt[0]);i++){ready(&n);CHECK(!rcs_nav_update(&n,zero,15,dt[i],out));CHECK(n.fault==RCS_NAV_TIMING);}
    ready(&n);int32_t jump[4]={INT32_MAX,0,0,0};CHECK(!rcs_nav_update(&n,jump,15,.01f,out));CHECK(n.fault==RCS_NAV_ENCODER_RANGE);
    ready(&n);rcs_nav_stop(&n);CHECK(n.state==RCS_NAV_IDLE);CHECK(!rcs_nav_start(&n));CHECK(rcs_nav_update(&n,zero,15,.01f,out));CHECK(out[0]==0&&out[1]==0&&out[2]==0);
    ready(&n);n.cfg.max_leg_s=.025f;n.cfg.position_tolerance_m=.00001f;n.target=1;CHECK(rcs_nav_update(&n,zero,15,.01f,out));CHECK(rcs_nav_update(&n,zero,15,.01f,out));CHECK(!rcs_nav_update(&n,zero,15,.01f,out));CHECK(n.fault==RCS_NAV_TIMEOUT);
    rcs_nav_config c=rcs_route_config;c.counts_per_motor_rev=0;CHECK(!rcs_nav_init(&n,&c,rcs_route_points,RCS_ROUTE_POINT_COUNT));CHECK(n.state==RCS_NAV_FAULT);
    c=rcs_route_config;c.encoder_direction[0]=0;CHECK(!rcs_nav_init(&n,&c,rcs_route_points,RCS_ROUTE_POINT_COUNT));
    CHECK(!rcs_nav_init(NULL,&rcs_route_config,rcs_route_points,RCS_ROUTE_POINT_COUNT));
    CHECK(!rcs_nav_init(&n,&rcs_route_config,rcs_route_points,0));
    out[0]=out[1]=out[2]=1;CHECK(!rcs_nav_update(NULL,zero,15,.01f,out));CHECK(out[0]==0&&out[1]==0&&out[2]==0);
    ready(&n);CHECK(rcs_nav_reset(&n,0,0,0));CHECK(rcs_nav_update(&n,zero,15,.01f,out));CHECK(!rcs_nav_start(&n));CHECK(n.fault==RCS_NAV_POSE);
}
static void odometry(void) {
    rcs_navigator n;float out[3],rpm[4],actual[3];int32_t delta[4]={0};float dt=.05f;
    CHECK(rcs_nav_init(&n,&rcs_route_config,rcs_route_points,RCS_ROUTE_POINT_COUNT));CHECK(rcs_nav_reset(&n,0,0,PI/2));
    CHECK(rcs_inverse(&n.cfg.geometry,.2f,.1f,.3f,rpm));
    for(unsigned i=0;i<rcs_wheel_count(&n.cfg.geometry);i++){
        delta[i]=(int32_t)lroundf(rpm[i]/60*n.cfg.counts_per_motor_rev*dt*n.cfg.encoder_direction[i]);
        rpm[i]=(float)delta[i]*60/(n.cfg.counts_per_motor_rev*dt)*n.cfg.encoder_direction[i];
    }
    CHECK(rcs_forward(&n.cfg.geometry,rpm,actual));CHECK(rcs_nav_update(&n,delta,15,dt,out));
    float da=actual[2]*dt,a=sinf(da)/da,b=(1-cosf(da))/da;
    CHECK(fabsf(n.x_m-(-dt*(b*actual[0]+a*actual[1])))<1e-5f);
    CHECK(fabsf(n.y_m-(dt*(a*actual[0]-b*actual[1])))<1e-5f);
    CHECK(fabsf(n.yaw_rad-PI/2-da)<1e-5f);CHECK(out[0]==0&&out[1]==0&&out[2]==0);
    /* Regression: near-zero turns must not lose (1-cos(angle))/angle in float. */
    rcs_nav_config precise=rcs_route_config;precise.counts_per_motor_rev=10000000.0f;
    CHECK(rcs_nav_init(&n,&precise,rcs_route_points,RCS_ROUTE_POINT_COUNT));CHECK(rcs_nav_reset(&n,0,0,0));
    CHECK(rcs_inverse(&n.cfg.geometry,.1f,0,.004f,rpm));
    for(unsigned i=0;i<rcs_wheel_count(&n.cfg.geometry);i++){
        delta[i]=(int32_t)lroundf(rpm[i]/60*n.cfg.counts_per_motor_rev*dt*n.cfg.encoder_direction[i]);
        rpm[i]=(float)delta[i]*60/(n.cfg.counts_per_motor_rev*dt)*n.cfg.encoder_direction[i];
    }
    CHECK(rcs_forward(&n.cfg.geometry,rpm,actual));CHECK(rcs_nav_update(&n,delta,15,dt,out));
    double tiny=(double)actual[2]*dt,bb=(1.0-cos(tiny))/tiny,aa=sin(tiny)/tiny;
    CHECK(fabs((double)n.y_m-dt*(bb*actual[0]+aa*actual[1]))<1e-9);
}
static void simulation(void) {
    rcs_navigator n;ready(&n);float out[3]={0},residual[4]={0},elapsed=0,dt=.01f;unsigned frames=0;
    while(n.state==RCS_NAV_RUNNING&&frames<50000u){
        float rpm[4];int32_t delta[4]={0};CHECK(rcs_inverse(&n.cfg.geometry,out[0],out[1],out[2],rpm));
        for(unsigned i=0;i<rcs_wheel_count(&n.cfg.geometry);i++){
            float counts=rpm[i]/60*n.cfg.counts_per_motor_rev*dt*n.cfg.encoder_direction[i]+residual[i];
            delta[i]=(int32_t)lroundf(counts);residual[i]=counts-(float)delta[i];
        }
        CHECK(rcs_nav_update(&n,delta,15,dt,out));CHECK(isfinite(out[0])&&isfinite(out[1])&&isfinite(out[2]));
        CHECK(hypotf(out[0],out[1])<=n.cfg.max_speed_mps+.001f);CHECK(fabsf(out[2])<=n.cfg.max_wz_radps+.001f);
        elapsed+=dt;frames++;
    }
    CHECK(n.state==RCS_NAV_DONE);CHECK(n.target==RCS_ROUTE_POINT_COUNT);CHECK(out[0]==0&&out[1]==0&&out[2]==0);
    const rcs_waypoint *last=&rcs_route_points[RCS_ROUTE_POINT_COUNT-1];
    CHECK(hypotf(last->x_m-n.x_m,last->y_m-n.y_m)<=n.cfg.position_tolerance_m+.002f);
    CHECK(fabsf(remainderf(last->yaw_rad-n.yaw_rad,2*PI))<=n.cfg.yaw_tolerance_rad+.002f);
    float dwell=0;for(unsigned i=0;i<RCS_ROUTE_POINT_COUNT;i++)dwell+=rcs_route_points[i].dwell_s;CHECK(elapsed>dwell);
    printf("layout=%d closed-loop ideal encoder simulation: %.2fs, %u frames, last pose %.3f %.3f %.3f\n",(int)n.cfg.geometry.layout,(double)elapsed,frames,(double)n.x_m,(double)n.y_m,(double)n.yaw_rad);
}
int main(void){faults();odometry();simulation();printf("PASS %u checks\n",checks);return 0;}
