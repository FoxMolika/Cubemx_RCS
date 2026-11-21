/**
 * @file rcs_feedback_port.c
 * @brief 导航反馈适配：校验真实增量，默认报告无数据
 *
 * 不把速度积分、上一周期值或理想轨迹当作编码器反馈。接入真实编码器时，
 * 在自己的源文件中重定义 rcs_feedback_port_hw_read()。
 */
#include "rcs_feedback_port.h"

#include "rcs_feedback_config.h"
#include "rcs_hal.h"

#if defined(__CC_ARM) || defined(__ARMCC_VERSION) || defined(__GNUC__)
#define RCS_WEAK __attribute__((weak))
#else
#define RCS_WEAK
#endif

#if RCS_FEEDBACK_APPLY_DIRECTION
static const int8_t k_feedback_direction[4] = RCS_FEEDBACK_DIRECTION;
#endif

/* 默认实现：没有真实采样就返回 0，绝不填"零增量 + 满掩码"。
 * 零增量加新鲜掩码等于告诉导航"车没动"，会让规划静默失败。 */
RCS_WEAK int rcs_feedback_port_hw_read(rcs_feedback_sample *sample)
{
    (void)sample;
    return 0;
}

int rcs_feedback_port_init(void)
{
    return 1;
}

/* 清空样本，任何提前返回都不会留下上一帧的残值。 */
static void clear_sample(rcs_feedback_sample *sample)
{
    int i;
    for (i = 0; i < 4; i++)
        sample->delta[i] = 0;
    sample->fresh_mask = 0u;
    sample->tick_ms = 0u;
}

int rcs_feedback_port_read(rcs_feedback_sample *sample)
{
    uint32_t age;
    int i;

    if (sample == 0)
        return 0;

    clear_sample(sample);
    if (!rcs_feedback_port_hw_read(sample))
        return 0;

    /* 掩码只允许低四位；缺轮或要求未满足都不算新鲜。 */
    if ((sample->fresh_mask & (uint8_t)~0x0Fu) != 0u)
        return 0;
    if ((sample->fresh_mask & (uint8_t)RCS_FEEDBACK_REQUIRED_MASK) !=
        (uint8_t)RCS_FEEDBACK_REQUIRED_MASK)
        return 0;

    /* 时间戳为 0 视为未打戳；超时或负向跳变都判过期。 */
    if (sample->tick_ms == 0u)
        return 0;
    age = (uint32_t)(HAL_GetTick() - sample->tick_ms);
    if (age > (uint32_t)RCS_FEEDBACK_TIMEOUT_MS)
        return 0;

    /* 单周期增量超范围说明丢步或接线错误，整帧拒绝而不是夹紧。 */
    for (i = 0; i < 4; i++) {
        if (sample->delta[i] > (int32_t)RCS_FEEDBACK_MAX_DELTA ||
            sample->delta[i] < -(int32_t)RCS_FEEDBACK_MAX_DELTA)
            return 0;
#if RCS_FEEDBACK_APPLY_DIRECTION
        sample->delta[i] *= (int32_t)k_feedback_direction[i];
#endif
    }
    return 1;
}
/* RCS GENERATED SHA256 864364a348c5440601bc7a332c54be11aabfba8505e6f2b7a859a6b3685f6d7c */
