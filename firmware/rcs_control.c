#include "rcs_control.h"
#include <math.h>
#include <string.h>
static float clamp(float x,float a) { return x>a?a:x< -a?-a:x; }
static void stop(rcs_controller *c, rcs_state state, rcs_fault fault) {
    c->state=state; c->fault=fault; c->command_seen=0;
    memset(c->target_rpm,0,sizeof(c->target_rpm));
    memset(c->integral,0,sizeof(c->integral));
    /* Derivative acts on measurement: restart it from the latest sample. */
    memcpy(c->previous_feedback,c->feedback_rpm,sizeof(c->previous_feedback));
}
static int fresh(const rcs_controller *c,uint32_t now) {
    for(unsigned i=0;i<rcs_wheel_count(&c->cfg.geometry);i++)
        if(!c->feedback_seen[i] || (uint32_t)(now-c->feedback_ms[i])>=c->cfg.feedback_timeout_ms) return 0;
    return 1;
}
int rcs_control_init(rcs_controller *c,const rcs_control_config *cfg,rcs_send_fn send,void *ctx) {
    if(!c) return 0;
    memset(c,0,sizeof(*c)); c->fault=RCS_BAD_CONFIG;
    if(!cfg || !send || !rcs_geometry_valid(&cfg->geometry) ||
       !isfinite(cfg->kp) || !isfinite(cfg->ki) || !isfinite(cfg->kd) ||
       cfg->kp<0 || cfg->ki<0 || cfg->kd<0 ||
       !isfinite(cfg->current_limit_raw) || cfg->current_limit_raw<=0 || cfg->current_limit_raw>16384 ||
       cfg->period_ms<1 || cfg->period_ms>100 ||
       cfg->command_timeout_ms<=cfg->period_ms || cfg->command_timeout_ms>60000 ||
       cfg->feedback_timeout_ms<=cfg->period_ms || cfg->feedback_timeout_ms>60000) return 0;
    unsigned used=0;
    for(unsigned i=0;i<rcs_wheel_count(&cfg->geometry);i++) {
        unsigned id=cfg->motor_id[i];
        if(id<1 || id>4 || (used&(1u<<id)) || (cfg->direction[i]!=1 && cfg->direction[i]!=-1)) return 0;
        used|=1u<<id;
    }
    c->cfg=*cfg; c->send=send; c->send_context=ctx; c->fault=RCS_OK; return 1;
}
void rcs_control_feedback(rcs_controller *c,uint16_t id,const uint8_t d[8],uint8_t dlc,uint32_t now) {
    if(!c || !c->send || !d || dlc!=8) return;
    for(unsigned i=0;i<rcs_wheel_count(&c->cfg.geometry);i++) if(id==0x200u+c->cfg.motor_id[i]) {
        uint16_t raw=((uint16_t)d[2]<<8)|d[3];
        int32_t signed_rpm=raw<32768u?(int32_t)raw:(int32_t)raw-65536;
        c->feedback_rpm[i]=(float)signed_rpm*(float)c->cfg.direction[i];
        c->feedback_ms[i]=now; c->feedback_seen[i]=1; return;
    }
}
int rcs_control_arm(rcs_controller *c,uint32_t now) {
    if(!c || !c->send) return 0;
    if(c->state==RCS_ARMED) return 1;
    if(!fresh(c,now)) { stop(c,RCS_LOCKED,RCS_NO_FEEDBACK); return 0; }
    /* New command required after every successful arm; old commands cannot resume. */
    stop(c,RCS_ARMED,RCS_OK); c->command_ms=now; c->last_step_ms=now; c->step_seen=0; return 1;
}
void rcs_control_lock(rcs_controller *c) { if(c) stop(c,RCS_LOCKED,RCS_OK); }
int rcs_control_command(rcs_controller *c,float vx,float vy,float wz,uint32_t now) {
    if(!c || !c->send || c->state!=RCS_ARMED) return 0;
    if(!rcs_inverse(&c->cfg.geometry,vx,vy,wz,c->target_rpm)) {
        stop(c,RCS_FAULT,RCS_BAD_COMMAND); return 0;
    }
    c->command_seen=1; c->command_ms=now; return 1;
}
void rcs_control_step(rcs_controller *c,uint32_t now) {
    uint8_t frame[8]={0}; float dt;
    if(!c || !c->send) return;
    uint32_t elapsed=(uint32_t)(now-c->last_step_ms);
    if(c->step_seen && elapsed<c->cfg.period_ms) return;
    dt=(float)(c->step_seen?elapsed:c->cfg.period_ms)*0.001f;
    if(c->state==RCS_ARMED) {
        if(c->step_seen && elapsed>c->cfg.period_ms*3u) stop(c,RCS_FAULT,RCS_BAD_TIMING);
        else if(!fresh(c,now)) stop(c,RCS_FAULT,RCS_FEEDBACK_TIMEOUT);
        else if((uint32_t)(now-c->command_ms)>=c->cfg.command_timeout_ms) stop(c,RCS_FAULT,RCS_COMMAND_TIMEOUT);
    }
    c->last_step_ms=now; c->step_seen=1;
    if(c->state==RCS_ARMED && c->command_seen) {
        for(unsigned i=0;i<rcs_wheel_count(&c->cfg.geometry);i++) {
            float e=c->target_rpm[i]-c->feedback_rpm[i];
            float next=clamp(c->integral[i]+c->cfg.ki*e*dt,c->cfg.current_limit_raw);
            /* Derivative on measurement: setpoint steps do not create a current spike. */
            float u=c->cfg.kp*e+next-c->cfg.kd*(c->feedback_rpm[i]-c->previous_feedback[i])/dt;
            if(!isfinite(u)) { stop(c,RCS_FAULT,RCS_BAD_COMMAND); memset(frame,0,8); break; }
            /* Conditional integration: allow integration only when it does not worsen saturation. */
            if(fabsf(u)<=c->cfg.current_limit_raw || (u>0 && e<0) || (u<0 && e>0)) c->integral[i]=next;
            int16_t raw=(int16_t)(clamp(u,c->cfg.current_limit_raw)*(float)c->cfg.direction[i]);
            unsigned slot=2u*(c->cfg.motor_id[i]-1u);
            frame[slot]=(uint8_t)((uint16_t)raw>>8); frame[slot+1]=(uint8_t)raw;
        }
    }
    memcpy(c->previous_feedback,c->feedback_rpm,sizeof(c->previous_feedback));
    if(!c->send(c->send_context,0x200u,frame)) {
        stop(c,RCS_FAULT,RCS_TX_FAILED);
        memset(frame,0,8); (void)c->send(c->send_context,0x200u,frame);
    }
}
