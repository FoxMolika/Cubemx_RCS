#include "rcs_hal.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>
CAN_TypeDef test_can1,test_can2;
static uint32_t tick,auto_tick,mask,free_boxes=3;static unsigned aborts,frames,wire_busy,abort_frees=1;static uint8_t sent[8];
uint32_t __get_PRIMASK(void){return mask;}
void __disable_irq(void){mask=1;}
void __set_PRIMASK(uint32_t value){mask=value;}
uint32_t HAL_GetTick(void){tick+=auto_tick;return tick;}
/* wire_busy models a frame already on the bus: abort cannot cancel it, it finishes later. */
uint32_t HAL_CAN_GetTxMailboxesFreeLevel(CAN_HandleTypeDef*c){(void)c;if(wire_busy){wire_busy--;return free_boxes<3?2:free_boxes;}return free_boxes;}
int HAL_CAN_AbortTxRequest(CAN_HandleTypeDef*c,uint32_t m){(void)c;assert(m==7);aborts++;if(abort_frees)free_boxes=3;return HAL_OK;}
int HAL_CAN_AddTxMessage(CAN_HandleTypeDef*c,CAN_TxHeaderTypeDef*h,uint8_t*d,uint32_t*m){(void)c;assert(h->StdId==0x200 && h->DLC==8);*m=1;memcpy(sent,d,8);frames++;return HAL_OK;}
static int zero(void){for(unsigned i=0;i<8;i++)if(sent[i])return 0;return 1;}
static void feed(CAN_HandleTypeDef*c){
    CAN_RxHeaderTypeDef h={0};uint8_t d[8]={0};h.DLC=8;
    for(unsigned i=0;i<4;i++){h.StdId=0x201+i;RCS_OnCanRx(c,&h,d);}
}
int main(void){
    CAN_HandleTypeDef can1={CAN1},can2={CAN2};
    assert(!RCS_Init(&can2));assert(!RCS_Arm());assert(RCS_Init(&can1));assert(!RCS_Arm());
    feed(&can2);assert(!RCS_Arm());feed(&can1);mask=1;assert(RCS_Arm());assert(mask==1);
    assert(RCS_SetVelocity(.1f,0,0));RCS_Process();assert(!zero());
    free_boxes=2;RCS_Lock();assert(aborts==1 && zero());assert(RCS_State()==RCS_LOCKED);
    /* Bus-off comes from the hardware ESR register (no HAL error interrupt required). */
    tick=5;feed(&can1);assert(RCS_Arm());assert(RCS_SetVelocity(.1f,0,0));test_can1.ESR=CAN_ESR_BOFF;RCS_Process();assert(RCS_State()==RCS_FAULT && RCS_Fault()==RCS_TX_FAILED);
    /* Automatic bus-off recovery clears ESR; zero frames flow again, motion still needs re-arm. */
    test_can1.ESR=0;tick=10;feed(&can1);unsigned before=frames;RCS_Process();assert(frames==before+1&&zero());assert(!RCS_SetVelocity(.1f,0,0));
    /* A frame already on the wire cannot be aborted: wait for it instead of faulting. */
    tick=15;feed(&can1);assert(RCS_Arm());assert(RCS_SetVelocity(.1f,0,0));RCS_Process();assert(!zero());
    free_boxes=2;wire_busy=5;RCS_Lock();assert(zero()&&RCS_Fault()==RCS_OK&&RCS_State()==RCS_LOCKED);
    /* A mailbox that never empties is bounded by time and reported. */
    free_boxes=2;abort_frees=0;auto_tick=1;before=frames;RCS_Lock();assert(frames==before&&RCS_Fault()==RCS_TX_FAILED);
    free_boxes=3;abort_frees=1;auto_tick=0;
    /* Invalid CAN frame must not make feedback fresh after re-init. */
    assert(RCS_Init(&can1));CAN_RxHeaderTypeDef h={0};uint8_t d[8]={0};h.DLC=7;h.StdId=0x201;RCS_OnCanRx(&can1,&h,d);assert(!RCS_Arm());
    puts("PASS HAL bridge: bus selection, IRQ snapshot, immediate lock, pending TX abort+wait, ESR bus-off + recovery, frame validation");return frames?0:1;
}
