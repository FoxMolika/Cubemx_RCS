#ifndef RCS_HAL_H
#define RCS_HAL_H
#include "stm32f4xx_hal.h"
#if !defined(HAL_CAN_MODULE_ENABLED)
#error "RCS: CAN HAL is disabled. Enable CAN1/CAN2 in CubeMX, or run RCS_ProjectSetup then Generate Code."
#else
#include "rcs_control.h"
#ifdef __cplusplus
extern "C" {
#endif
/* Owns all four command slots on this CAN bus, including unused zero slots.
 * Does not replace global HAL callbacks or change CubeMX clocks/pins/filters.
 * Bus-off is read from the CAN ESR register; enable CAN AutoBusOff in CubeMX
 * so the peripheral can recover by itself after the bus is repaired. */
int RCS_Init(CAN_HandleTypeDef *can);
void RCS_OnCanRx(CAN_HandleTypeDef *can,const CAN_RxHeaderTypeDef *header,const uint8_t data[8]);
void RCS_Process(void); /* single task, every RCS_PERIOD_MS */
int RCS_Arm(void);      /* only after valid feedback from every configured motor */
void RCS_Lock(void);
int RCS_SetVelocity(float vx,float vy,float wz); /* renew before command timeout */
rcs_state RCS_State(void);
rcs_fault RCS_Fault(void);
#ifdef __cplusplus
}
#endif
#endif /* HAL_CAN_MODULE_ENABLED */
#endif /* RCS_HAL_H */
