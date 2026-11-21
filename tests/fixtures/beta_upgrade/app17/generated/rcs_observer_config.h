/**
 * @file rcs_observer_config.h
 * @brief 独立定位观测的编号、有效期、质量门限与坐标对齐
 *
 * 只放参数。数据读取与坐标换算在定位适配模块里实现，不要把驱动代码塞进本文件。
 * 默认 0xFF = 不接独立定位：只用编码器里程计，不启用纠错。
 */
#pragma once

#ifndef RCS_OBSERVER_SOURCE
#define RCS_OBSERVER_SOURCE               0xFFu
#endif

/* 观测有效期。超过该年龄的样本不能用于纠错。 */
#ifndef RCS_OBSERVER_MAX_AGE_S
#define RCS_OBSERVER_MAX_AGE_S            0.20f
#endif

/* 质量门限，0~1；低于门限的样本丢弃。 */
#ifndef RCS_OBSERVER_MIN_QUALITY
#define RCS_OBSERVER_MIN_QUALITY          0.60f
#endif

/* 单次观测允许的最大跳变，超过判为定位跳变故障。 */
#ifndef RCS_OBSERVER_MAX_JUMP_M
#define RCS_OBSERVER_MAX_JUMP_M           0.50f
#endif
#ifndef RCS_OBSERVER_MAX_JUMP_RAD
#define RCS_OBSERVER_MAX_JUMP_RAD         0.35f
#endif

/* 观测坐标系到地图坐标系的刚体对齐：先旋转再平移。不做滤波。 */
#ifndef RCS_OBSERVER_ORIGIN_X_M
#define RCS_OBSERVER_ORIGIN_X_M           0.0f
#endif
#ifndef RCS_OBSERVER_ORIGIN_Y_M
#define RCS_OBSERVER_ORIGIN_Y_M           0.0f
#endif
#ifndef RCS_OBSERVER_ROTATION_RAD
#define RCS_OBSERVER_ROTATION_RAD         0.0f
#endif
/* RCS GENERATED SHA256 9b91db2541cb8805c4bebdac19d70821bb44f9b9b534d63f86e00917cdf286a9 */
