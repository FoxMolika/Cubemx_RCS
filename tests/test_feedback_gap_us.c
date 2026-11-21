/**
 * @file test_feedback_gap_us.c
 * @brief 微秒窗口回归：13680rpm 下 1.035ms 帧距必须通过，2.2ms 必须锁存
 */
#include <stdio.h>
#include "rcs_feedback_port.h"
#include "rcs_feedback_config.h"

CAN_TypeDef test_can1, test_can2;
DWT_Type test_dwt;
CoreDebug_Type test_coredebug;
uint32_t SystemCoreClock = 168000000u;

static uint32_t g_ms, g_us;
static CAN_HandleTypeDef g_can = { &test_can1 };

uint32_t HAL_GetTick(void) { return g_ms; }
uint32_t __get_PRIMASK(void) { return 0u; }
void __disable_irq(void) { }
void __set_PRIMASK(uint32_t value) { (void)value; }
int rcs_can_port_discard_rx(void) { return 1; }

/* 帧内只带角度；ID 按 1..4 顺序对应 W0..W3。 */
static void frame(unsigned wheel, uint16_t angle)
{
    CAN_RxHeaderTypeDef header;
    uint8_t data[8] = { 0 };
    header.StdId = 0x200u + (uint32_t)wheel + 1u;
    header.ExtId = 0u;
    header.IDE = 0u;
    header.RTR = 0u;
    header.DLC = 8u;
    data[0] = (uint8_t)(angle >> 8);
    data[1] = (uint8_t)(angle & 0xFFu);
    rcs_feedback_port_on_rx(&g_can, &header, data);
}

static void step_us(uint32_t delta_us)
{
    g_us += delta_us;
    g_ms = g_us / 1000u;
    test_dwt.CYCCNT = g_us * 168u;
}

static int failures;

static void expect(int condition, const char *label)
{
    if (!condition) {
        printf("FAIL %s\n", label);
        ++failures;
    }
}

int main(void)
{
    unsigned wheel, round;
    uint32_t sequence;
    if (!rcs_feedback_port_init()) {
        printf("FAIL init rejected 13680rpm config\n");
        return 1;
    }
    expect(rcs_feedback_port_dwt_ready() == 1u, "DWT should be enabled");
    expect((uint32_t)RCS_FEEDBACK_MAX_GAP_US > 1000u, "microsecond window must exceed 1ms");
    expect((uint32_t)RCS_FEEDBACK_MAX_GAP_US < 2048u, "microsecond window must stay under half turn");

    g_us = 100000u;
    g_ms = 100u;
    test_dwt.CYCCNT = g_us * 168u;
    for (wheel = 0; wheel < 4u; ++wheel) frame(wheel, 0u);
    for (round = 0; round < 10u; ++round) {
        step_us(1035u);
        for (wheel = 0; wheel < 4u; ++wheel) frame(wheel, 0u);
    }
    expect(rcs_feedback_port_fault() == RCS_FEEDBACK_OK, "1.035ms frames must be accepted");
    sequence = rcs_feedback_port_sequence();
    expect(sequence == 44u, "10 healthy rounds should accept 44 frames");
    expect(rcs_feedback_port_gap_us_max() >= 1000u && rcs_feedback_port_gap_us_max() <= 1100u,
           "measured gap should be about 1.035ms");

    step_us(1600u);
    for (wheel = 0; wheel < 4u; ++wheel) frame(wheel, 0u);
    expect(rcs_feedback_port_fault() == RCS_FEEDBACK_OK, "1.6ms must stay inside the window");

    step_us(2200u);
    frame(0u, 0u);
    expect(rcs_feedback_port_fault() == RCS_FEEDBACK_AMBIGUOUS_GAP, "2.2ms must latch AMBIGUOUS_GAP");
    expect(rcs_feedback_port_gap_us_last() >= 2190u && rcs_feedback_port_gap_us_last() <= 2210u,
           "latched gap should report about 2200us");

    if (failures) return 1;
    printf("PASS microsecond gap window at 13680rpm: 1035us accepted, 2200us latched\n");
    return 0;
}
