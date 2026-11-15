/* Minimal host-only HAL stub. Never include this file in a firmware project. */
#ifndef TEST_HAL_H
#define TEST_HAL_H
#include <stdint.h>
typedef struct {volatile uint32_t ESR;} CAN_TypeDef;
typedef struct {CAN_TypeDef *Instance;} CAN_HandleTypeDef;
typedef struct {uint32_t StdId,ExtId,IDE,RTR,DLC,TransmitGlobalTime;} CAN_TxHeaderTypeDef;
typedef struct {uint32_t StdId,ExtId,IDE,RTR,DLC;} CAN_RxHeaderTypeDef;
extern CAN_TypeDef test_can1,test_can2;
#define CAN1 (&test_can1)
#define CAN2 (&test_can2)
#define CAN_ESR_BOFF 4u
#define CAN_TX_MAILBOX0 1u
#define CAN_TX_MAILBOX1 2u
#define CAN_TX_MAILBOX2 4u
#define CAN_ID_STD 0u
#define CAN_RTR_DATA 0u
#define DISABLE 0u
#define HAL_OK 0
uint32_t __get_PRIMASK(void);
void __disable_irq(void);
void __set_PRIMASK(uint32_t);
uint32_t HAL_GetTick(void);
uint32_t HAL_CAN_GetTxMailboxesFreeLevel(CAN_HandleTypeDef*);
int HAL_CAN_AbortTxRequest(CAN_HandleTypeDef*,uint32_t);
int HAL_CAN_AddTxMessage(CAN_HandleTypeDef*,CAN_TxHeaderTypeDef*,uint8_t*,uint32_t*);
#endif
