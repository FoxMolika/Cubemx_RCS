/* Compile the real port .c files against a finite three-frame FIFO and controlled tick. */
#include "rcs_feedback_port.h"
#include "rcs_feedback_config.h"
#include "rcs_can_port.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#ifndef EXPECT_VALID
#define EXPECT_VALID 1
#endif
#define CHECK(x) do { ++checks; if (!(x)) { fprintf(stderr,"FAIL line %d: %s\n",__LINE__,#x); exit(1); } } while(0)
static unsigned checks;
CAN_TypeDef test_can1, test_can2;
static CAN_HandleTypeDef can1 = { CAN1 }, can2 = { CAN2 };
static const unsigned ids[4] = RCS_FEEDBACK_MOTOR_IDS;
static uint32_t now_ms, primask, notify;
static unsigned forwarded, filter_calls, rx_calls, fifo_count, start_calls;
static int fail_filter, fail_start, fail_notify, fail_rx;
static CAN_FilterTypeDef last_filter;
typedef struct { CAN_RxHeaderTypeDef h; uint8_t data[8]; } frame;
static frame fifo[3];
static int restore_hook;
static CAN_HandleTypeDef *selected(void) { return RCS_FEEDBACK_CAN_BUS == 2 ? &can2 : &can1; }
uint32_t HAL_GetTick(void) { return now_ms; }
uint32_t __get_PRIMASK(void) { return primask; }
void __disable_irq(void) { primask = 1u; }
static void send_wheel(unsigned wheel, unsigned angle);
void __set_PRIMASK(uint32_t value)
{
    primask = value;
    /* Deterministic IRQ arrival exactly when a task-side read leaves its critical section. */
    if (!value && restore_hook) {
        restore_hook = 0;
        send_wheel(0, 111u);
    }
}
int HAL_CAN_ConfigFilter(CAN_HandleTypeDef *can, CAN_FilterTypeDef *filter)
{
    (void)can; ++filter_calls; last_filter = *filter; return fail_filter ? HAL_ERROR : HAL_OK;
}
int HAL_CAN_Start(CAN_HandleTypeDef *can)
{
    ++start_calls;
    if (fail_start || can->Instance->TEST_STATE != HAL_CAN_STATE_READY) return HAL_ERROR;
    can->Instance->TEST_STATE = HAL_CAN_STATE_LISTENING;
    return HAL_OK;
}
int HAL_CAN_ActivateNotification(CAN_HandleTypeDef *can, uint32_t bits)
{
    (void)can; notify = bits; return fail_notify ? HAL_ERROR : HAL_OK;
}
uint32_t HAL_CAN_GetRxFifoFillLevel(CAN_HandleTypeDef *can, uint32_t which)
{
    (void)can; CHECK(which == CAN_RX_FIFO0); CHECK(primask == 1u); return fifo_count;
}
int HAL_CAN_GetRxMessage(CAN_HandleTypeDef *can, uint32_t which, CAN_RxHeaderTypeDef *h, uint8_t data[8])
{
    unsigned i; (void)can; CHECK(which == 0); CHECK(primask == 1u); ++rx_calls;
    if (fail_rx) return HAL_ERROR;
    CHECK(fifo_count > 0);
    *h = fifo[0].h; memcpy(data, fifo[0].data, 8);
    for (i=1; i<fifo_count; ++i) fifo[i-1] = fifo[i];
    --fifo_count; return HAL_OK;
}
void RCS_OnCanRx(CAN_HandleTypeDef *can, const CAN_RxHeaderTypeDef *h, const uint8_t data[8])
{
    CHECK(can != 0 && h != 0 && data != 0); ++forwarded;
}
#if !RCS_CAN_PORT_DEFINE_HAL_CALLBACK
/* Existing application callback explicitly delegates when the default wrapper is disabled. */
void HAL_CAN_RxFifo0MsgPendingCallback(CAN_HandleTypeDef *can) { rcs_can_port_poll(can); }
#endif
static void raw(CAN_HandleTypeDef *can, unsigned id, unsigned angle, unsigned ide, unsigned rtr, unsigned dlc)
{
    CAN_RxHeaderTypeDef h = {0};
    uint8_t data[8] = {0};
    h.StdId = id; h.IDE=ide; h.RTR=rtr; h.DLC=dlc;
    data[0]=(uint8_t)(angle>>8); data[1]=(uint8_t)angle;
    rcs_feedback_port_on_rx(can,&h,data);
}
static void send_wheel(unsigned wheel, unsigned angle) { raw(selected(),0x200u+ids[wheel],angle,0,0,8); }
static void all(unsigned angle) { unsigned i; for(i=0;i<RCS_FEEDBACK_WHEELS;++i) send_wheel(i,angle); }
static void fresh(unsigned angle)
{
    CHECK(rcs_feedback_port_init());
    test_can1.ESR=test_can1.RF0R=test_can2.ESR=test_can2.RF0R=0;
    now_ms=100; all(angle);
}
static void take_zero(void)
{
    rcs_feedback_sample s; unsigned i;
    CHECK(rcs_feedback_port_read(&s));
    CHECK(s.fresh_mask == (uint8_t)((1u<<RCS_FEEDBACK_WHEELS)-1u));
    for(i=0;i<4;++i) CHECK(s.delta[i]==0);
    CHECK(!rcs_feedback_port_read(&s)); CHECK(s.fresh_mask==0);
}
static void push(unsigned wheel, unsigned angle)
{
    frame *f;
    CHECK(fifo_count < 3u); f=&fifo[fifo_count++]; memset(f,0,sizeof(*f));
    f->h.StdId=0x200u+ids[wheel]; f->h.DLC=8;
    f->data[0]=(uint8_t)(angle>>8); f->data[1]=(uint8_t)angle;
}
int main(void)
{
    rcs_feedback_sample s; unsigned i; uint32_t seq;
    CHECK(!rcs_feedback_port_read(0));
#if !EXPECT_VALID
    CHECK(!rcs_feedback_port_init());
    CHECK(rcs_feedback_port_fault()==RCS_FEEDBACK_BAD_CONFIG);
    memset(&s,0x33,sizeof(s)); CHECK(!rcs_feedback_port_read(&s)); CHECK(s.fresh_mask==0);
    printf("PASS %u invalid-config assertions\n",checks); return 0;
#endif
    fresh(8190); take_zero();
    now_ms=101; all(2); CHECK(rcs_feedback_port_read(&s));
    for(i=0;i<RCS_FEEDBACK_WHEELS;++i) CHECK(s.delta[i]==4);
    seq=s.sequence;
    CHECK(!rcs_feedback_port_read(&s)); CHECK(s.fresh_mask==0);
    now_ms=102; all(8190); CHECK(rcs_feedback_port_read(&s));
    CHECK((uint32_t)(s.sequence-seq)==RCS_FEEDBACK_WHEELS);
    for(i=0;i<RCS_FEEDBACK_WHEELS;++i) CHECK(s.delta[i]==-4);

    fresh(100); take_zero(); now_ms=101;
    send_wheel(0,110); CHECK(!rcs_feedback_port_read(&s));
    for(i=1;i<RCS_FEEDBACK_WHEELS;++i) send_wheel(i,100+10*(i+1));
    CHECK(rcs_feedback_port_read(&s));
    for(i=0;i<RCS_FEEDBACK_WHEELS;++i) CHECK(s.delta[i]==(int32_t)(10*(i+1)));
    if(RCS_FEEDBACK_WHEELS==3) CHECK(s.delta[3]==0);

    fresh(100); take_zero();
    now_ms=101; all(110); rcs_feedback_port_reset();
    CHECK(!rcs_feedback_port_read(&s)); now_ms=102; all(1000); take_zero();
    now_ms=103; all(1005); CHECK(rcs_feedback_port_read(&s));
    for(i=0;i<RCS_FEEDBACK_WHEELS;++i) CHECK(s.delta[i]==5);

    fresh(100); take_zero();
    now_ms=101; all(110); restore_hook=1;
    CHECK(rcs_feedback_port_read(&s));
    for(i=0;i<RCS_FEEDBACK_WHEELS;++i) CHECK(s.delta[i]==10);
    now_ms=102;
    for(i=1;i<RCS_FEEDBACK_WHEELS;++i) send_wheel(i,111);
    CHECK(rcs_feedback_port_read(&s));
    for(i=0;i<RCS_FEEDBACK_WHEELS;++i) CHECK(s.delta[i]==1);
    CHECK(primask==0);
    primask=1; CHECK(!rcs_feedback_port_read(&s)); CHECK(primask==1); primask=0;

    fresh(100); take_zero();
    raw(selected()==&can1?&can2:&can1,0x200+ids[0],101,0,0,8);
    raw(selected(),0x200+ids[0],101,1,0,8);
    raw(selected(),0x200+ids[0],101,0,1,8);
    raw(selected(),0x200+ids[0],101,0,0,7);
    raw(selected(),0x299,101,0,0,8);
    CHECK(!rcs_feedback_port_read(&s)); CHECK(rcs_feedback_port_fault()==RCS_FEEDBACK_OK);

    fresh(100); take_zero(); now_ms=201;
    CHECK(!rcs_feedback_port_read(&s)); CHECK(rcs_feedback_port_fault()==RCS_FEEDBACK_STALE);
    rcs_feedback_port_invalidate(RCS_FEEDBACK_FIFO_OVERRUN);
    CHECK(rcs_feedback_port_fault()==RCS_FEEDBACK_STALE);
    rcs_feedback_port_reset(); CHECK(rcs_feedback_port_fault()==RCS_FEEDBACK_OK);

    fresh(100); take_zero(); now_ms=100+(uint32_t)RCS_FEEDBACK_MAX_GAP_MS+1;
    send_wheel(0,101); CHECK(rcs_feedback_port_fault()==RCS_FEEDBACK_AMBIGUOUS_GAP);
    CHECK(!rcs_feedback_port_read(&s));
    fresh(100); take_zero(); now_ms=101; send_wheel(0,4196);
    CHECK(rcs_feedback_port_fault()==RCS_FEEDBACK_AMBIGUOUS_GAP);
    fresh(100); take_zero(); now_ms=101; send_wheel(0,8192);
    CHECK(rcs_feedback_port_fault()==RCS_FEEDBACK_BAD_ANGLE);
    fresh(100); take_zero(); now_ms=101; send_wheel(0,1500);
    CHECK(rcs_feedback_port_fault()==RCS_FEEDBACK_DELTA_RANGE);

    fresh(100); take_zero();
    for(i=0;i<(unsigned)RCS_FEEDBACK_MAX_DELTA+2u;++i) {
        ++now_ms; send_wheel(0,(101u+i)%8192u);
    }
    CHECK(rcs_feedback_port_fault()==RCS_FEEDBACK_DELTA_RANGE);

    CHECK(rcs_feedback_port_init()); now_ms=UINT32_MAX-2u; all(100); take_zero();
    now_ms=UINT32_MAX-1u; all(101); CHECK(rcs_feedback_port_read(&s));
    now_ms=0; all(103); CHECK(rcs_feedback_port_read(&s)); CHECK(s.tick_ms==0);
    for(i=0;i<RCS_FEEDBACK_WHEELS;++i) CHECK(s.delta[i]==2);
    now_ms=101; CHECK(!rcs_feedback_port_read(&s)); CHECK(rcs_feedback_port_fault()==RCS_FEEDBACK_STALE);
    CHECK(rcs_feedback_port_init()); now_ms=0; all(400); take_zero();

    fresh(100); take_zero();
    CHECK(rcs_can_port_start(selected()));
    CHECK(last_filter.FilterBank==(selected()==&can2?14u:0u));
    CHECK(last_filter.SlaveStartFilterBank==14u);
    CHECK(last_filter.FilterIdHigh==(0x200u<<5));
    CHECK(last_filter.FilterMaskIdHigh==(0x7F8u<<5));
    CHECK(notify==CAN_IT_RX_FIFO0_MSG_PENDING);
    i=start_calls; CHECK(rcs_can_port_start(selected())); CHECK(start_calls==i);
    CHECK(!rcs_can_port_start(0));
    CHECK(!rcs_can_port_start(selected()==&can1?&can2:&can1));
    rcs_feedback_port_reset(); now_ms=200;
    push(0,100); push(1,100); push(2,100);
    HAL_CAN_RxFifo0MsgPendingCallback(selected()); CHECK(fifo_count==0);
    if (RCS_FEEDBACK_WHEELS==4) { push(3,100); HAL_CAN_RxFifo0MsgPendingCallback(selected()); }
    take_zero();
    forwarded=rx_calls=fifo_count=0; now_ms=200;
    rcs_feedback_port_reset();
    for(i=0;i<10000;++i) {
        unsigned wheel=i%RCS_FEEDBACK_WHEELS, round=i/RCS_FEEDBACK_WHEELS;
        now_ms=200+round;
        push(wheel,(100+round)%8192);
        HAL_CAN_RxFifo0MsgPendingCallback(selected());
        CHECK(fifo_count==0); CHECK(primask==0);
    }
    CHECK(forwarded==10000 && rx_calls==10000);
    CHECK(rcs_feedback_port_read(&s));
    CHECK(rcs_feedback_port_fault()==RCS_FEEDBACK_OK);
    for(i=0;i<RCS_FEEDBACK_WHEELS;++i) CHECK(s.delta[i]==(int32_t)((9999u-i)/RCS_FEEDBACK_WHEELS));
    CHECK(!rcs_feedback_port_read(&s));

    /* Frames queued before reset must not be replayed as post-reset freshness. */
    push(0,700); push(1,700);
    i=forwarded;
    rcs_feedback_port_reset(); CHECK(fifo_count==0); CHECK(forwarded==i);
    CHECK(!rcs_feedback_port_read(&s));
    ++now_ms; all(900); take_zero();
    push(0,901); fail_rx=1; rcs_feedback_port_reset(); fail_rx=0; fifo_count=0;
    CHECK(rcs_feedback_port_fault()==RCS_FEEDBACK_CAN_RX_FAILED);
    CHECK(!rcs_feedback_port_read(&s));

    rcs_feedback_port_reset(); selected()->Instance->RF0R=CAN_FLAG_FOV0;
    rcs_can_port_poll(selected()); CHECK(rcs_feedback_port_fault()==RCS_FEEDBACK_FIFO_OVERRUN);
    CHECK(selected()->Instance->RF0R==0);
    rcs_feedback_port_reset(); selected()->Instance->ESR=CAN_ESR_BOFF;
    rcs_can_port_poll(selected()); CHECK(rcs_feedback_port_fault()==RCS_FEEDBACK_CAN_BUS_OFF);
    selected()->Instance->ESR=0; rcs_feedback_port_reset();
    push(0,100); fail_rx=1; rcs_can_port_poll(selected()); fail_rx=0; fifo_count=0;
    CHECK(rcs_feedback_port_fault()==RCS_FEEDBACK_CAN_RX_FAILED);
    rcs_feedback_port_reset(); fail_filter=1; CHECK(!rcs_can_port_start(selected())); fail_filter=0;
    selected()->Instance->TEST_STATE=HAL_CAN_STATE_READY;
    fail_start=1; CHECK(!rcs_can_port_start(selected())); fail_start=0;
    fail_notify=1; CHECK(!rcs_can_port_start(selected())); fail_notify=0;
    CHECK(rcs_feedback_port_fault()==RCS_FEEDBACK_CAN_RX_FAILED);
    rcs_feedback_port_reset(); i=start_calls;
    CHECK(rcs_can_port_start(selected())); CHECK(start_calls==i);

    printf("PASS %u feedback/CAN assertions; wheels=%u bus=%u wrapper=%u\n",
           checks,(unsigned)RCS_FEEDBACK_WHEELS,(unsigned)RCS_FEEDBACK_CAN_BUS,
           (unsigned)RCS_CAN_PORT_DEFINE_HAL_CALLBACK);
    return 0;
}
