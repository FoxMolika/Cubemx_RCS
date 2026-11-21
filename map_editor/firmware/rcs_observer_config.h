/**
 * @file rcs_observer_config.h
 * @brief 旧0.8.3定位宏兼容项，不是当前纠错引擎的配置入口
 *
 * 当前生成器从地图“定位硬件/纠错模板”生成实际观测配置和读取绑定。
 * 本文件保留旧宏供自有适配器引用；单独修改这些宏不会自动启用定位或纠错。
 * 真实读取和坐标换算必须由适配器实现，没有独立硬件时保持空纠错模板。
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
