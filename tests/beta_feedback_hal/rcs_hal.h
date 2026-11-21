#pragma once
#include <stdint.h>
#ifndef RCS_NO_CHASSIS
#define RCS_NO_CHASSIS 0
#endif
typedef struct { uint32_t ESR, RF0R, TEST_STATE; } CAN_TypeDef;
extern CAN_TypeDef test_can1, test_can2;
#define CAN1 (&test_can1)
#define CAN2 (&test_can2)
typedef struct { CAN_TypeDef *Instance; } CAN_HandleTypeDef;
typedef struct { uint32_t StdId, ExtId, IDE, RTR, DLC; } CAN_RxHeaderTypeDef;
typedef struct {
    uint32_t FilterBank, FilterMode, FilterScale, FilterIdHigh, FilterIdLow;
    uint32_t FilterMaskIdHigh, FilterMaskIdLow, FilterFIFOAssignment, FilterActivation;
    uint32_t SlaveStartFilterBank;
} CAN_FilterTypeDef;
#define CAN_ID_STD 0u
#define CAN_RTR_DATA 0u
#define CAN_RX_FIFO0 0u
#define CAN_FILTERMODE_IDMASK 0u
#define CAN_FILTERSCALE_32BIT 1u
#define CAN_FILTER_ENABLE 1u
#define CAN_IT_RX_FIFO0_MSG_PENDING 2u
#define CAN_ESR_BOFF 4u
#define CAN_FLAG_FOV0 8u
#define HAL_OK 0
#define HAL_ERROR 1
#define HAL_CAN_STATE_READY 0u
#define HAL_CAN_STATE_LISTENING 1u
#define HAL_CAN_GetState(can) ((can)->Instance->TEST_STATE)
#define __HAL_CAN_GET_FLAG(can,flag) (((can)->Instance->RF0R & (flag)) != 0u)
#define __HAL_CAN_CLEAR_FLAG(can,flag) ((can)->Instance->RF0R &= ~(flag))
uint32_t HAL_GetTick(void);
uint32_t __get_PRIMASK(void);
void __disable_irq(void);
void __set_PRIMASK(uint32_t value);
int HAL_CAN_ConfigFilter(CAN_HandleTypeDef *can, CAN_FilterTypeDef *filter);
int HAL_CAN_Start(CAN_HandleTypeDef *can);
int HAL_CAN_ActivateNotification(CAN_HandleTypeDef *can, uint32_t bits);
uint32_t HAL_CAN_GetRxFifoFillLevel(CAN_HandleTypeDef *can, uint32_t fifo);
int HAL_CAN_GetRxMessage(CAN_HandleTypeDef *can, uint32_t fifo, CAN_RxHeaderTypeDef *header, uint8_t data[8]);
void RCS_OnCanRx(CAN_HandleTypeDef *can, const CAN_RxHeaderTypeDef *header, const uint8_t data[8]);
void HAL_CAN_RxFifo0MsgPendingCallback(CAN_HandleTypeDef *can);
