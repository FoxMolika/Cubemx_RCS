#ifndef RCS_CHASSIS_H
#define RCS_CHASSIS_H
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif
/* SI units. x forward, y left, yaw counterclockwise.
 * Mecanum X: FL, FR, RL, RR. Omni: position angles theta0+i*360/N;
 * positive rolling direction (-sin(theta), cos(theta)), tangent CCW. */
typedef enum { RCS_MECANUM_X=0, RCS_OMNI3=1, RCS_OMNI4=2 } rcs_layout;
typedef struct {
    rcs_layout layout;
    float wheel_diameter_m, wheelbase_m, track_m, omni_radius_m;
    float omni_theta0_rad, gear_ratio, max_motor_rpm;
} rcs_geometry;
unsigned rcs_wheel_count(const rcs_geometry *g);
int rcs_geometry_valid(const rcs_geometry *g);
/* Returns 1 on success. Invalid input yields zeros and returns 0. */
int rcs_inverse(const rcs_geometry *g, float vx, float vy, float wz,
                float motor_rpm[4]);
int rcs_forward(const rcs_geometry *g, const float motor_rpm[4],
                float velocity[3]);
#ifdef __cplusplus
}
#endif
#endif
