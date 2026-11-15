#ifndef RCS_CONTROL_H
#define RCS_CONTROL_H
#include "rcs_chassis.h"
#ifdef __cplusplus
extern "C" {
#endif
typedef struct {
    rcs_geometry geometry;
    uint8_t motor_id[4]; /* 1..4, exclusive ownership of CAN 0x200 */
    int8_t direction[4]; /* logical wheel positive -> motor rotor sign */
    float kp, ki, kd, current_limit_raw;
    uint32_t period_ms, command_timeout_ms, feedback_timeout_ms;
} rcs_control_config;
typedef enum { RCS_LOCKED=0, RCS_ARMED=1, RCS_FAULT=2 } rcs_state;
typedef enum { RCS_OK=0, RCS_BAD_CONFIG, RCS_NO_FEEDBACK, RCS_COMMAND_TIMEOUT,
               RCS_FEEDBACK_TIMEOUT, RCS_TX_FAILED, RCS_BAD_COMMAND, RCS_BAD_TIMING } rcs_fault;
/* Return nonzero only if the frame was accepted by the transport. */
typedef int (*rcs_send_fn)(void *context, uint16_t id, const uint8_t data[8]);
typedef struct {
    rcs_control_config cfg;
    rcs_state state;
    rcs_fault fault;
    float feedback_rpm[4], target_rpm[4], integral[4], previous_feedback[4];
    uint32_t feedback_ms[4], command_ms, last_step_ms;
    uint8_t feedback_seen[4], command_seen, step_seen;
    rcs_send_fn send;
    void *send_context;
} rcs_controller;
/* All methods are single-task APIs; ISR must hand off feedback atomically. */
int rcs_control_init(rcs_controller *c, const rcs_control_config *cfg,
                     rcs_send_fn send, void *context);
void rcs_control_feedback(rcs_controller *c, uint16_t id,
                           const uint8_t data[8], uint8_t dlc, uint32_t now);
/* Allowed from LOCKED and from FAULT: a successful arm clears the fault, but only
 * with fresh feedback from every wheel, and motion still needs a NEW command. */
int rcs_control_arm(rcs_controller *c, uint32_t now);
void rcs_control_lock(rcs_controller *c);
int rcs_control_command(rcs_controller *c, float vx, float vy, float wz, uint32_t now);
/* Call once per configured period, including while locked. The derivative term
 * acts on measured speed, so command steps and re-arming never kick the output. */
void rcs_control_step(rcs_controller *c, uint32_t now);
#ifdef __cplusplus
}
#endif
#endif
