/**
 * @file rcs_feedback_port.c
 * @brief 使用现有C620 CAN转子角采集真实增量，不增加定位硬件
 */
#include "rcs_feedback_port.h"
#include "rcs_feedback_config.h"
#include "rcs_can_port.h"
#include <limits.h>
#include <math.h>
#include <string.h>
typedef struct {
    uint16_t angle;
    int32_t pending;
    uint32_t tick;
    uint8_t seen;
} feedback_wheel;
static const uint32_t k_motor_ids[4] = RCS_FEEDBACK_MOTOR_IDS;
static volatile feedback_wheel g_wheels[4];
static volatile uint32_t g_sequence;
static volatile uint8_t g_fresh, g_initialized;
static volatile rcs_feedback_fault g_fault = RCS_FEEDBACK_BAD_CONFIG;
static uint32_t enter(void)
{
    uint32_t saved = __get_PRIMASK();
    __disable_irq();
    return saved;
}
static void latch(rcs_feedback_fault fault)
{
    if (g_fault == RCS_FEEDBACK_OK && fault != RCS_FEEDBACK_OK)
        g_fault = fault;
}
void rcs_feedback_port_invalidate(rcs_feedback_fault fault)
{
    uint32_t saved = enter();
    latch(fault);
    __set_PRIMASK(saved);
}
rcs_feedback_fault rcs_feedback_port_fault(void) { return g_fault; }
static void reset_locked(void)
{
    unsigned i;
    for (i = 0; i < 4; ++i) {
        g_wheels[i].angle = 0;
        g_wheels[i].pending = 0;
        g_wheels[i].tick = 0;
        g_wheels[i].seen = 0;
    }
    g_fresh = 0;
    g_fault = g_initialized ? RCS_FEEDBACK_OK : RCS_FEEDBACK_BAD_CONFIG;
}
void rcs_feedback_port_reset(void)
{
    uint32_t saved = enter();
    int flushed;
    /* 软件边界还要丢弃已经排队的旧帧，不能在reset后把旧FIFO当新采样。 */
    flushed = rcs_can_port_discard_rx();
    reset_locked();
    /* 显式reset可清旧故障，但不能抹掉本次排空才出现的失败。 */
    if (!flushed) latch(RCS_FEEDBACK_CAN_RX_FAILED);
    __set_PRIMASK(saved);
}
int rcs_feedback_port_init(void)
{
    unsigned i, j;
    float rpm = (float)RCS_FEEDBACK_MAX_RPM;
    uint32_t saved = enter();
    int valid = !RCS_NO_CHASSIS && (RCS_FEEDBACK_WHEELS == 3u || RCS_FEEDBACK_WHEELS == 4u) &&
        (RCS_FEEDBACK_CAN_BUS == 1u || RCS_FEEDBACK_CAN_BUS == 2u) &&
        RCS_FEEDBACK_SAMPLE_TIMEOUT_MS >= 1u && RCS_FEEDBACK_SAMPLE_TIMEOUT_MS <= 60000u &&
        isfinite(rpm) && rpm > 0 && rpm <= 100000.0f;
    /* 先验证除数，再计算派生值；毫秒时间戳的1ms量化误差也须小于半转。 */
    if (valid)
        valid = RCS_FEEDBACK_MAX_GAP_MS > 0u &&
            RCS_FEEDBACK_MAX_GAP_MS <= RCS_FEEDBACK_SAMPLE_TIMEOUT_MS &&
            rpm * ((float)RCS_FEEDBACK_MAX_GAP_MS + 1.0f) < 30000.0f &&
            RCS_FEEDBACK_MAX_DELTA > 0;
    for (i = 0; i < RCS_FEEDBACK_WHEELS && i < 4u; ++i) {
        if (k_motor_ids[i] < 1u || k_motor_ids[i] > 4u) valid = 0;
        for (j = 0; j < i; ++j)
            if (k_motor_ids[i] == k_motor_ids[j]) valid = 0;
    }
    g_initialized = valid ? 1u : 0u;
    g_sequence = 0;
    reset_locked();
    __set_PRIMASK(saved);
    return valid;
}
void rcs_feedback_port_on_rx(CAN_HandleTypeDef *can, const CAN_RxHeaderTypeDef *header,
                              const uint8_t data[8])
{
#if !RCS_NO_CHASSIS
    unsigned wheel;
    uint16_t angle;
    uint32_t now, saved, gap;
    int32_t delta;
    int64_t pending;
    float possible;
    if (!g_initialized || !can || !header || !data) return;
    if ((RCS_FEEDBACK_CAN_BUS == 1u && can->Instance != CAN1) ||
        (RCS_FEEDBACK_CAN_BUS == 2u && can->Instance != CAN2)) return;
    if (header->IDE != CAN_ID_STD || header->RTR != CAN_RTR_DATA || header->DLC != 8u) return;
    for (wheel = 0; wheel < RCS_FEEDBACK_WHEELS; ++wheel)
        if (header->StdId == 0x200u + k_motor_ids[wheel]) break;
    if (wheel == RCS_FEEDBACK_WHEELS) return;
    angle = (uint16_t)(((uint16_t)data[0] << 8) | data[1]);
    now = HAL_GetTick();
    saved = enter();
    if (g_fault != RCS_FEEDBACK_OK) { __set_PRIMASK(saved); return; }
    if (angle >= 8192u) {
        latch(RCS_FEEDBACK_BAD_ANGLE);
    } else if (!g_wheels[wheel].seen) {
        /* 首帧只有新基准，不能虚构之前位移。 */
        g_wheels[wheel].angle = angle;
        g_wheels[wheel].tick = now;
        g_wheels[wheel].seen = 1;
        g_fresh |= (uint8_t)(1u << wheel);
        ++g_sequence;
    } else {
        gap = (uint32_t)(now - g_wheels[wheel].tick);
        delta = (int32_t)angle - g_wheels[wheel].angle;
        if (delta > 4096) delta -= 8192;
        else if (delta < -4096) delta += 8192;
        possible = (float)RCS_FEEDBACK_MAX_RPM * 8192.0f * ((float)gap + 1.0f) / 60000.0f + 2.0f;
        pending = (int64_t)g_wheels[wheel].pending + delta;
        if (gap > (uint32_t)RCS_FEEDBACK_MAX_GAP_MS || delta == 4096 || delta == -4096)
            latch(RCS_FEEDBACK_AMBIGUOUS_GAP);
        else if ((float)delta > possible || (float)delta < -possible ||
                 pending > RCS_FEEDBACK_MAX_DELTA || pending < -(int64_t)RCS_FEEDBACK_MAX_DELTA ||
                 pending > INT32_MAX || pending < INT32_MIN)
            latch(RCS_FEEDBACK_DELTA_RANGE);
        else {
            g_wheels[wheel].pending = (int32_t)pending;
            g_wheels[wheel].angle = angle;
            g_wheels[wheel].tick = now;
            g_fresh |= (uint8_t)(1u << wheel);
            ++g_sequence;
        }
    }
    __set_PRIMASK(saved);
#else
    (void)can; (void)header; (void)data;
#endif
}
int rcs_feedback_port_read(rcs_feedback_sample *sample)
{
    unsigned i;
    uint8_t required = (uint8_t)((1u << RCS_FEEDBACK_WHEELS) - 1u);
    uint32_t oldest_age = 0, now, saved;
    int ready;
    if (!sample) return 0;
    memset(sample, 0, sizeof(*sample));
    saved = enter();
    now = HAL_GetTick();
    ready = g_initialized && g_fault == RCS_FEEDBACK_OK && (g_fresh & required) == required;
    for (i = 0; i < RCS_FEEDBACK_WHEELS && i < 4u; ++i) {
        uint32_t age = (uint32_t)(now - g_wheels[i].tick);
        if (g_wheels[i].seen && age > (uint32_t)RCS_FEEDBACK_SAMPLE_TIMEOUT_MS) {
            latch(RCS_FEEDBACK_STALE);
            ready = 0;
        }
        if (age > oldest_age) oldest_age = age;
    }
    if (ready) {
        for (i = 0; i < RCS_FEEDBACK_WHEELS; ++i) {
            sample->delta[i] = g_wheels[i].pending;
            g_wheels[i].pending = 0;
        }
        sample->tick_ms = now - oldest_age;
        sample->fresh_mask = required;
        sample->sequence = g_sequence;
        g_fresh = 0;
    }
    __set_PRIMASK(saved);
    return ready;
}

/* RCS GENERATED SHA256 fdaee754d3d6cbc34ce09915cb0070aa823710cb61159d7795efa1931b914f66 */
