#include "rcs_hal.h"
#if defined(HAL_CAN_MODULE_ENABLED)
#include "rcs_config.h"
#include <string.h>
#ifndef RCS_TX_ABORT_WAIT_MS
#define RCS_TX_ABORT_WAIT_MS 2u   /* one 1 Mbps frame needs ~0.13 ms; tick granularity is 1 ms */
#endif
static rcs_controller controller;
static CAN_HandleTypeDef *bus;
typedef struct { uint8_t data[8], valid; uint32_t time; } rx_slot;
static volatile rx_slot inbox[4];
static void sync_rx(void) {
    rx_slot snapshot[4];
    uint32_t mask=__get_PRIMASK(); __disable_irq();
    for(unsigned i=0;i<4;i++) { snapshot[i]=inbox[i]; inbox[i].valid=0; }
    __set_PRIMASK(mask);
    for(unsigned i=0;i<4;i++) if(snapshot[i].valid)
        rcs_control_feedback(&controller,(uint16_t)(0x201u+i),snapshot[i].data,8,snapshot[i].time);
}
static int transmit(void *ctx,uint16_t id,const uint8_t data[8]) {
    CAN_HandleTypeDef *can=(CAN_HandleTypeDef*)ctx;
    CAN_TxHeaderTypeDef h={0}; uint32_t mailbox;
    /* Hardware bus-off flag: independent of HAL error interrupts and never stale. */
    if((can->Instance->ESR&CAN_ESR_BOFF)!=0u) return 0;
    /* Never leave stale nonzero commands queued after lock/fault.
     * This transport therefore requires exclusive ownership of CAN TX. */
    if(HAL_CAN_GetTxMailboxesFreeLevel(can)!=3u) {
        (void)HAL_CAN_AbortTxRequest(can,CAN_TX_MAILBOX0|CAN_TX_MAILBOX1|CAN_TX_MAILBOX2);
        /* A frame already on the wire cannot be aborted; wait for it to finish. */
        uint32_t start=HAL_GetTick(),spins=0;
        while(HAL_CAN_GetTxMailboxesFreeLevel(can)!=3u) {
            if((uint32_t)(HAL_GetTick()-start)>=RCS_TX_ABORT_WAIT_MS || ++spins>=200000u) return 0;
        }
    }
    h.StdId=id; h.IDE=CAN_ID_STD; h.RTR=CAN_RTR_DATA; h.DLC=8; h.TransmitGlobalTime=DISABLE;
    return HAL_CAN_AddTxMessage(can,&h,(uint8_t*)data,&mailbox)==HAL_OK;
}
int RCS_Init(CAN_HandleTypeDef *can) {
    const rcs_control_config cfg=RCS_CONTROL_CONFIG;
    uint32_t mask=__get_PRIMASK(); __disable_irq();
    bus=0; memset((void*)inbox,0,sizeof(inbox));
    __set_PRIMASK(mask);
    memset(&controller,0,sizeof(controller)); controller.fault=RCS_BAD_CONFIG;
    if(!can || (RCS_CAN_BUS==1 && can->Instance!=CAN1) || (RCS_CAN_BUS==2 && can->Instance!=CAN2)) return 0;
    if(!rcs_control_init(&controller,&cfg,transmit,can)) return 0;
    bus=can; return 1;
}
void RCS_OnCanRx(CAN_HandleTypeDef *can,const CAN_RxHeaderTypeDef *h,const uint8_t data[8]) {
    if(!bus || can!=bus || !h || !data || h->IDE!=CAN_ID_STD || h->RTR!=CAN_RTR_DATA ||
       h->DLC!=8 || h->StdId<0x201u || h->StdId>0x204u) return;
    unsigned i=h->StdId-0x201u;
    for(unsigned j=0;j<8;j++) inbox[i].data[j]=data[j];
    inbox[i].time=HAL_GetTick(); inbox[i].valid=1;
}
void RCS_Process(void) { if(bus) { sync_rx(); rcs_control_step(&controller,HAL_GetTick()); } }
int RCS_Arm(void) { if(!bus) return 0; sync_rx(); return rcs_control_arm(&controller,HAL_GetTick()); }
void RCS_Lock(void) {
    if(bus) {
        uint8_t zero[8]={0}; rcs_control_lock(&controller);
        if(!transmit(bus,0x200u,zero)) controller.fault=RCS_TX_FAILED;
    }
}
int RCS_SetVelocity(float vx,float vy,float wz) {
    return bus?rcs_control_command(&controller,vx,vy,wz,HAL_GetTick()):0;
}
rcs_state RCS_State(void) { return controller.state; }
rcs_fault RCS_Fault(void) { return controller.fault; }
#endif
