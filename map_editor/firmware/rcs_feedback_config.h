/**
 * @file rcs_feedback_config.h
 * @brief 轮子计数增量、新鲜度与方向配置
 *
 * 只描述"怎么判断反馈可信"。编码器接入点在 rcs_feedback_port_hw_read()。
 * 开关同样用 #ifndef 包裹，可在 Keil Define 中覆盖。
 */
#pragma once

/* 1 = 读取端返回的是已按逻辑轮方向归一后的计数，本模块再乘一次方向表。
 * 0 = 读取端返回原始有符号计数，方向交给 rcs_config.h 的轮方向处理。
 * 两者同时取反会得到反向里程计，默认 0，避免与生成配置重复。 */
#ifndef RCS_FEEDBACK_APPLY_DIRECTION
#define RCS_FEEDBACK_APPLY_DIRECTION      0u
#endif

/* 仅在 RCS_FEEDBACK_APPLY_DIRECTION 为 1 时使用。 */
#ifndef RCS_FEEDBACK_DIRECTION
#define RCS_FEEDBACK_DIRECTION            { 1, 1, 1, 1 }
#endif

/* 样本时间戳到当前时刻超过该值即判为过期，单位 ms。 */
#ifndef RCS_FEEDBACK_TIMEOUT_MS
#define RCS_FEEDBACK_TIMEOUT_MS           50u
#endif

/* 单周期允许的最大计数增量；超限视为丢步或接线错误，整帧拒绝。 */
#ifndef RCS_FEEDBACK_MAX_DELTA
#define RCS_FEEDBACK_MAX_DELTA            20000
#endif

/* 要求的有效轮掩码，0x0F 表示四轮都必须是真实新采样。 */
#ifndef RCS_FEEDBACK_REQUIRED_MASK
#define RCS_FEEDBACK_REQUIRED_MASK        0x0Fu
#endif