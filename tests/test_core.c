#include "rcs_control.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>
#include <float.h>
static unsigned checks;
#define CHECK(x) do { checks++; if(!(x)){ fprintf(stderr,"FAIL line %d: %s\n",__LINE__,#x);exit(1); } } while(0)
static void near(float a,float b,float eps){CHECK(fabsf(a-b)<eps);}
static rcs_geometry geom={RCS_MECANUM_X,0.2f,0.6f,0.4f,0.2f,0.0f,20.0f,20000.0f};
static uint8_t last[8];static unsigned sends;static int fail_send;
static int send_frame(void *ctx,uint16_t id,const uint8_t d[8]){(void)ctx;CHECK(id==0x200);memcpy(last,d,8);sends++;return !fail_send;}
static int16_t slot(unsigned i){return (int16_t)(((uint16_t)last[2*i]<<8)|last[2*i+1]);}
static int zero(void){for(unsigned i=0;i<8;i++)if(last[i])return 0;return 1;}
static rcs_control_config config(void){
    rcs_control_config c={geom,{1,2,3,4},{1,1,1,1},1,0,0,2000,5,200,100};return c;
}
static void feed(rcs_controller *c,uint32_t t,int16_t rpm){
    uint8_t d[8]={0};d[2]=(uint8_t)((uint16_t)rpm>>8);d[3]=(uint8_t)rpm;
    for(unsigned i=0;i<4;i++)rcs_control_feedback(c,(uint16_t)(0x201+i),d,8,t);
}
static void derivative_tests(void);
static void math_tests(void){
    float rpm[4],v[3];rcs_geometry g=geom;
    CHECK(rcs_inverse(&g,1,0,0,rpm));for(unsigned i=0;i<4;i++)near(rpm[i],1909.859317f,0.002f);
    CHECK(rcs_inverse(&g,0,1,0,rpm));near(rpm[0],-1909.859317f,.002f);near(rpm[1],1909.859317f,.002f);near(rpm[2],rpm[1],.002f);near(rpm[3],rpm[0],.002f);
    CHECK(rcs_inverse(&g,0,0,1,rpm));near(rpm[0],-954.929659f,.002f);near(rpm[1],954.929659f,.002f);near(rpm[2],rpm[0],.002f);near(rpm[3],rpm[1],.002f);
    CHECK(rcs_inverse(&g,1,1,1,rpm));near(rpm[0],-954.929659f,.002f);near(rpm[1],4774.648293f,.003f);near(rpm[2],2864.788976f,.003f);near(rpm[3],954.929659f,.002f);
    g.max_motor_rpm=2000;CHECK(rcs_inverse(&g,1,1,1,rpm));near(rpm[0],-400,.002f);near(rpm[1],2000,.002f);near(rpm[2],1200,.002f);near(rpm[3],400,.002f);
    g=geom;g.layout=RCS_OMNI3;CHECK(rcs_inverse(&g,1,0,0,rpm));near(rpm[0],0,.002f);near(rpm[1],-1653.986686f,.003f);near(rpm[2],1653.986686f,.003f);CHECK(rpm[3]==0);
    CHECK(rcs_inverse(&g,0,0,1,rpm));for(unsigned i=0;i<3;i++)near(rpm[i],381.971863f,.002f);
    g.layout=RCS_OMNI4;CHECK(rcs_inverse(&g,1,0,1,rpm));near(rpm[0],381.971863f,.002f);near(rpm[1],-1527.887454f,.003f);near(rpm[2],381.971863f,.003f);near(rpm[3],2291.831181f,.003f);
    /* Independent physical scale: half diameter -> twice RPM. */
    g=geom;g.wheel_diameter_m=.1f;CHECK(rcs_inverse(&g,1,0,0,rpm));near(rpm[0],3819.718634f,.004f);
    for(unsigned layout=0;layout<3;layout++){
        g=geom;g.layout=(rcs_layout)layout;g.omni_theta0_rad=.37f;
        for(int i=-4;i<=4;i++)for(int j=-3;j<=3;j++){
            float x=i*.17f,y=j*.13f,z=(i-j)*.11f;CHECK(rcs_inverse(&g,x,y,z,rpm));CHECK(rcs_forward(&g,rpm,v));near(v[0],x,.00001f);near(v[1],y,.00001f);near(v[2],z,.00002f);
        }
    }
    g=geom;g.wheel_diameter_m=0;CHECK(!rcs_inverse(&g,1,0,0,rpm));for(unsigned i=0;i<4;i++)CHECK(rpm[i]==0);
    g=geom;CHECK(!rcs_inverse(&g,NAN,0,0,rpm));CHECK(!rcs_inverse(&g,FLT_MAX,FLT_MAX,FLT_MAX,rpm));CHECK(!rcs_inverse(&g,0,0,0,NULL));
    rpm[0]=NAN;CHECK(!rcs_forward(&g,rpm,v));for(unsigned i=0;i<3;i++)CHECK(v[i]==0);
}
static void control_tests(void){
    rcs_controller c;rcs_control_config cfg=config();sends=0;fail_send=0;
    CHECK(rcs_control_init(&c,&cfg,send_frame,NULL));CHECK(c.state==RCS_LOCKED);rcs_control_step(&c,0);CHECK(zero());CHECK(!rcs_control_arm(&c,0));
    CHECK(!rcs_control_command(&c,1,0,0,0));feed(&c,10,0);CHECK(rcs_control_arm(&c,10));rcs_control_step(&c,10);CHECK(zero());
    CHECK(rcs_control_command(&c,.1f,0,0,10));rcs_control_step(&c,15);near((float)slot(0),190,.1f);CHECK(slot(0)==slot(3));
    rcs_control_step(&c,110);CHECK(c.state==RCS_FAULT);CHECK(zero());feed(&c,111,0);CHECK(!rcs_control_command(&c,.1f,0,0,111));rcs_control_step(&c,115);CHECK(zero());CHECK(rcs_control_arm(&c,116));rcs_control_step(&c,116);CHECK(zero());
    CHECK(rcs_control_command(&c,.2f,0,0,116));rcs_control_step(&c,121);CHECK(!zero());rcs_control_lock(&c);rcs_control_step(&c,126);CHECK(zero());
    feed(&c,200,0);CHECK(rcs_control_arm(&c,200));CHECK(rcs_control_command(&c,.1f,0,0,200));
    for(uint32_t t=200;t<=400;t+=5){feed(&c,t,0);rcs_control_step(&c,t);}CHECK(c.state==RCS_FAULT);CHECK(c.fault==RCS_COMMAND_TIMEOUT);CHECK(zero());
    feed(&c,500,0);CHECK(rcs_control_arm(&c,500));CHECK(rcs_control_command(&c,NAN,0,0,500)==0);rcs_control_step(&c,500);CHECK(c.fault==RCS_BAD_COMMAND);CHECK(zero());
    feed(&c,600,0);CHECK(rcs_control_arm(&c,600));CHECK(rcs_control_command(&c,1,0,0,600));fail_send=1;rcs_control_step(&c,600);CHECK(c.fault==RCS_TX_FAILED);CHECK(zero());fail_send=0;
    cfg.direction[0]=-1;cfg.motor_id[0]=4;cfg.motor_id[3]=1;CHECK(rcs_control_init(&c,&cfg,send_frame,NULL));feed(&c,1000,-100);near(c.feedback_rpm[0],100,.1f);CHECK(rcs_control_arm(&c,1000));CHECK(rcs_control_command(&c,0,0,0,1000));rcs_control_step(&c,1000);CHECK(slot(3)==100);CHECK(slot(0)==100);
    cfg=config();cfg.motor_id[1]=1;CHECK(!rcs_control_init(&c,&cfg,send_frame,NULL));cfg=config();cfg.current_limit_raw=20000;CHECK(!rcs_control_init(&c,&cfg,send_frame,NULL));
    cfg=config();cfg.ki=10;cfg.kp=100;CHECK(rcs_control_init(&c,&cfg,send_frame,NULL));feed(&c,0,0);CHECK(rcs_control_arm(&c,0));
    for(uint32_t t=0;t<1000;t+=5){feed(&c,t,0);CHECK(rcs_control_command(&c,1,0,0,t));rcs_control_step(&c,t);}near(c.integral[0],0,.001f);CHECK(slot(0)==2000);
    /* uint32 millisecond rollover and stale feedback DLC rejection. */
    CHECK(rcs_control_init(&c,&cfg,send_frame,NULL));feed(&c,UINT32_MAX-10u,0);CHECK(rcs_control_arm(&c,UINT32_MAX-10u));CHECK(rcs_control_command(&c,.1f,0,0,UINT32_MAX-10u));rcs_control_step(&c,UINT32_MAX-10u);feed(&c,4u,0);rcs_control_step(&c,4u);CHECK(c.state==RCS_ARMED);
    uint8_t bad[8]={0};rcs_control_feedback(&c,0x201,bad,7,10);CHECK(c.feedback_ms[0]==4u);
    /* Exact feedback timeout, without triggering timing guard first. */
    cfg=config();CHECK(rcs_control_init(&c,&cfg,send_frame,NULL));feed(&c,0,0);CHECK(rcs_control_arm(&c,0));CHECK(rcs_control_command(&c,.1f,0,0,0));for(uint32_t t=0;t<=100;t+=5)rcs_control_step(&c,t);CHECK(c.fault==RCS_FEEDBACK_TIMEOUT);CHECK(zero());
    derivative_tests();
}
static void derivative_tests(void){
    /* Pure D controller: setpoint steps must not produce output (no derivative kick). */
    rcs_controller c;rcs_control_config cfg=config();cfg.kp=0;cfg.ki=0;cfg.kd=.01f;
    CHECK(rcs_control_init(&c,&cfg,send_frame,NULL));feed(&c,0,0);CHECK(rcs_control_arm(&c,0));
    CHECK(rcs_control_command(&c,1,0,0,0));rcs_control_step(&c,0);CHECK(zero());
    feed(&c,5,0);CHECK(rcs_control_command(&c,-1,0,0,5));rcs_control_step(&c,5);CHECK(zero());
    /* Measured acceleration is damped: +100 rpm in 5 ms -> -kd*100/0.005 = -200. */
    feed(&c,10,100);CHECK(rcs_control_command(&c,-1,0,0,10));rcs_control_step(&c,10);near((float)slot(0),-200,1.5f);
    feed(&c,15,100);CHECK(rcs_control_command(&c,-1,0,0,15));rcs_control_step(&c,15);CHECK(zero());
    /* Re-arm while wheels already spin: the first step after arming sees no stale history. */
    rcs_control_lock(&c);feed(&c,20,500);rcs_control_step(&c,20);CHECK(zero());
    feed(&c,25,500);CHECK(rcs_control_arm(&c,25));CHECK(rcs_control_command(&c,1,0,0,25));rcs_control_step(&c,25);CHECK(zero());
    /* Re-arming from FAULT is allowed only with fresh feedback and still needs a new command. */
    rcs_control_step(&c,500);CHECK(c.state==RCS_FAULT);CHECK(!rcs_control_arm(&c,500));
    feed(&c,600,0);CHECK(rcs_control_arm(&c,600));CHECK(c.fault==RCS_OK);rcs_control_step(&c,600);CHECK(zero());
}
int main(void){math_tests();control_tests();printf("PASS %u checks: kinematics, limits, PID, CAN framing, timeouts, lock/rearm, timing, rollover\n",checks);return 0;}
