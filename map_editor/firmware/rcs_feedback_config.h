/**
 * @file rcs_feedback_config.h
 * @brief 从底盘配置派生C620角度采集，不另加定位硬件
 * @note 本端口不乘方向，方向只在导航encoder_direction换算一次。
 */
#pragma once
#include <stdint.h>
#if defined(__has_include)
#if __has_include("rcs_config.h")
#include "rcs_config.h"
#elif __has_include("rcs_app_config.h")
#include "rcs_app_config.h"
#endif
#else
#include "rcs_config.h"
#endif
#ifndef RCS_FEEDBACK_WHEELS
#if defined(RCS_LAYOUT)
#define RCS_FEEDBACK_WHEELS ((RCS_LAYOUT) == 1 ? 3u : 4u)
#elif defined(RCS_APP_WHEELS)
#define RCS_FEEDBACK_WHEELS RCS_APP_WHEELS
#else
#define RCS_FEEDBACK_WHEELS 4u
#endif
#endif
#ifndef RCS_FEEDBACK_CAN_BUS
#if defined(RCS_CAN_BUS)
#define RCS_FEEDBACK_CAN_BUS RCS_CAN_BUS
#elif defined(RCS_APP_CAN_BUS)
#define RCS_FEEDBACK_CAN_BUS RCS_APP_CAN_BUS
#else
#define RCS_FEEDBACK_CAN_BUS 1u
#endif
#endif
#ifndef RCS_FEEDBACK_MOTOR_IDS
#if defined(RCS_MOTOR_ID_0)
#define RCS_FEEDBACK_MOTOR_IDS { RCS_MOTOR_ID_0, RCS_MOTOR_ID_1, RCS_MOTOR_ID_2, RCS_MOTOR_ID_3 }
#elif defined(RCS_APP_MOTOR_IDS)
#define RCS_FEEDBACK_MOTOR_IDS RCS_APP_MOTOR_IDS
#else
#define RCS_FEEDBACK_MOTOR_IDS { 1u, 2u, 3u, 4u }
#endif
#endif
/* 新名称与底盘RCS_FEEDBACK_TIMEOUT_MS分开，避免跨模块宏冲突。 */
#ifndef RCS_FEEDBACK_SAMPLE_TIMEOUT_MS
#if defined(RCS_FEEDBACK_TIMEOUT_MS)
#define RCS_FEEDBACK_SAMPLE_TIMEOUT_MS RCS_FEEDBACK_TIMEOUT_MS
#else
#define RCS_FEEDBACK_SAMPLE_TIMEOUT_MS 100u
#endif
#endif
#ifndef RCS_FEEDBACK_MAX_RPM
#if defined(RCS_MAX_MOTOR_RPM)
#define RCS_FEEDBACK_MAX_RPM RCS_MAX_MOTOR_RPM
#else
#define RCS_FEEDBACK_MAX_RPM 3000.0f
#endif
#endif
/* 默认取最大转速下四分之一圈的间隔，运行时再计入1ms量化误差。 */
#ifndef RCS_FEEDBACK_MAX_GAP_MS
#define RCS_FEEDBACK_MAX_GAP_MS ((uint32_t)(15000.0f / (float)RCS_FEEDBACK_MAX_RPM))
#endif
/* 微秒判定窗口：最大转速下3/8圈，给半圈歧义留1/8圈余量。
 * 13680rpm时毫秒分档只剩1ms，1.035ms的真实帧距会被整数毫秒量化成2ms而误报。 */
#ifndef RCS_FEEDBACK_GAP_COUNTS
#define RCS_FEEDBACK_GAP_COUNTS 3072.0f
#endif
#ifndef RCS_FEEDBACK_MAX_GAP_US
#define RCS_FEEDBACK_MAX_GAP_US ((uint32_t)((RCS_FEEDBACK_GAP_COUNTS) * 60000000.0f / \
    ((float)RCS_FEEDBACK_MAX_RPM * 8192.0f)))
#endif
#ifndef RCS_FEEDBACK_MAX_DELTA
#define RCS_FEEDBACK_MAX_DELTA ((int32_t)((float)RCS_FEEDBACK_MAX_RPM * 8192.0f * \
    (float)RCS_FEEDBACK_SAMPLE_TIMEOUT_MS / 60000.0f + 8192.0f))
#endif
/* 已有全局HAL回调时设0，在原分发点调用poll或逐帧on_rx。重复定义须明确报错。 */
#ifndef RCS_CAN_PORT_DEFINE_HAL_CALLBACK
#define RCS_CAN_PORT_DEFINE_HAL_CALLBACK 1
#endif
#ifndef RCS_CAN_PORT_FILTER_SPLIT
#define RCS_CAN_PORT_FILTER_SPLIT 14u
#endif
#ifndef RCS_CAN_PORT_FILTER_BANK_CAN1
#define RCS_CAN_PORT_FILTER_BANK_CAN1 0u
#endif
#ifndef RCS_CAN_PORT_FILTER_BANK_CAN2
#define RCS_CAN_PORT_FILTER_BANK_CAN2 RCS_CAN_PORT_FILTER_SPLIT
#endif
