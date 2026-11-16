/**
 * @file rcs_feedback_port.h
 * @brief 导航反馈硬件适配接口
 *
 * 驱动层必须给出真实的轮子计数增量和采样时刻。默认实现刻意报告"无新鲜反馈"，
 * 防止尚未接好编码器时导航错误启动。
 */
#pragma once

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct {
    int32_t delta[4];   /* 本采样周期每轮有符号计数增量，W0~W3 */
    uint8_t fresh_mask; /* bit0..3 表示对应轮有真实新采样 */
    uint32_t tick_ms;   /* 该样本的采样时刻，HAL_GetTick 时基 */
} rcs_feedback_sample;

/* 初始化适配层。返回 1 成功。 */
int rcs_feedback_port_init(void);

/* 读取一帧并做新鲜度、掩码、增量范围校验。
 * 返回 1 = sample 可用；返回 0 = 没有可用真实反馈（调用方必须停车）。 */
int rcs_feedback_port_read(rcs_feedback_sample *sample);

/* 硬件读取钩子，弱符号。默认返回 0（无数据）；用户在自己的 .c 里重定义即可接入，
 * 不需要改动本文件，重新生成也不会覆盖。 */
int rcs_feedback_port_hw_read(rcs_feedback_sample *sample);

#ifdef __cplusplus
}
#endif