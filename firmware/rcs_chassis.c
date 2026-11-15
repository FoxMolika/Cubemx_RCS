#include "rcs_chassis.h"
#include <math.h>
#include <stddef.h>
#define RCS_PI 3.14159265358979323846f
static void clear(float *p, unsigned n) { if(p) for(unsigned i=0;i<n;i++) p[i]=0.0f; }
unsigned rcs_wheel_count(const rcs_geometry *g) {
    return !g ? 0u : g->layout==RCS_OMNI3 ? 3u :
        (g->layout==RCS_MECANUM_X || g->layout==RCS_OMNI4) ? 4u : 0u;
}
int rcs_geometry_valid(const rcs_geometry *g) {
    if(!rcs_wheel_count(g) || !isfinite(g->wheel_diameter_m) || g->wheel_diameter_m<=0.0f ||
       !isfinite(g->gear_ratio) || g->gear_ratio<=0.0f ||
       !isfinite(g->max_motor_rpm) || g->max_motor_rpm<=0.0f) return 0;
    if(g->layout==RCS_MECANUM_X)
        return isfinite(g->wheelbase_m) && g->wheelbase_m>0.0f &&
               isfinite(g->track_m) && g->track_m>0.0f;
    return isfinite(g->omni_radius_m) && g->omni_radius_m>0.0f &&
           isfinite(g->omni_theta0_rad);
}
int rcs_inverse(const rcs_geometry *g, float vx, float vy, float wz, float rpm[4]) {
    float v[4]={0}, peak=0, k;
    clear(rpm,4);
    if(!rpm || !rcs_geometry_valid(g) || !isfinite(vx) || !isfinite(vy) || !isfinite(wz)) return 0;
    unsigned n=rcs_wheel_count(g);
    if(g->layout==RCS_MECANUM_X) {
        float z=(g->wheelbase_m+g->track_m)*0.5f*wz;
        v[0]=vx-vy-z; v[1]=vx+vy+z; v[2]=vx+vy-z; v[3]=vx-vy+z;
    } else {
        for(unsigned i=0;i<n;i++) {
            float a=g->omni_theta0_rad+2.0f*RCS_PI*(float)i/(float)n;
            v[i]=-sinf(a)*vx+cosf(a)*vy+g->omni_radius_m*wz;
        }
    }
    k=60.0f*g->gear_ratio/(RCS_PI*g->wheel_diameter_m);
    if(!isfinite(k)) return 0;
    for(unsigned i=0;i<n;i++) {
        rpm[i]=v[i]*k;
        if(!isfinite(rpm[i])) { clear(rpm,4); return 0; }
        if(fabsf(rpm[i])>peak) peak=fabsf(rpm[i]);
    }
    if(peak>g->max_motor_rpm)
        for(unsigned i=0;i<n;i++) rpm[i]*=g->max_motor_rpm/peak;
    return 1;
}
int rcs_forward(const rcs_geometry *g, const float rpm[4], float out[3]) {
    float v[4]={0}, k;
    clear(out,3);
    if(!out || !rpm || !rcs_geometry_valid(g)) return 0;
    unsigned n=rcs_wheel_count(g);
    k=RCS_PI*g->wheel_diameter_m/(60.0f*g->gear_ratio);
    if(!isfinite(k)) return 0;
    for(unsigned i=0;i<n;i++) {
        if(!isfinite(rpm[i])) return 0;
        v[i]=rpm[i]*k;
        if(!isfinite(v[i])) return 0;
    }
    if(g->layout==RCS_MECANUM_X) {
        out[0]=(v[0]+v[1]+v[2]+v[3])*0.25f;
        out[1]=(-v[0]+v[1]+v[2]-v[3])*0.25f;
        out[2]=(-v[0]+v[1]-v[2]+v[3])/(2.0f*(g->wheelbase_m+g->track_m));
    } else {
        for(unsigned i=0;i<n;i++) {
            float a=g->omni_theta0_rad+2.0f*RCS_PI*(float)i/(float)n;
            out[0]+=-sinf(a)*v[i]*2.0f/(float)n;
            out[1]+=cosf(a)*v[i]*2.0f/(float)n;
            out[2]+=v[i]/((float)n*g->omni_radius_m);
        }
    }
    for(unsigned i=0;i<3;i++) if(!isfinite(out[i])) { clear(out,3); return 0; }
    return 1;
}
