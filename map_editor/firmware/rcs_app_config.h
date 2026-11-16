/**
 * @file rcs_app_config.h
 * @brief 应用层调度周期与路线总闸
 *
 * 这里只留"生成器不该替用户决定"的少数开关。启动策略、位姿来源、故障处理
 * 都在 rcs_app_task.c 的 USER CODE 区里，改那里不会被重新导出覆盖。
 *
 * 全部用 #ifndef 包裹：推荐在 Keil 的 C/C++ → Define 里按现场覆盖本文件默认值。
 * 未确认实物参数前保持 RCS_APP_ROUTE_ENABLED 为 0：能上电、能诊断，但不会自己走。
 */
#pragma once

/* App_task() 可以每轮主循环调用，内部按该周期驱动导航。
 * 取值必须不小于 RCS_PERIOD_MS（底盘看门狗周期），且不大于 100。 */
#ifndef RCS_APP_PERIOD_MS
#define RCS_APP_PERIOD_MS                 10u
#endif

/* 1 = 允许执行路线。默认 0：先让 CAN 与反馈跑通再打开。 */
#ifndef RCS_APP_ROUTE_ENABLED
#define RCS_APP_ROUTE_ENABLED             0u
#endif

/* 1 = 位姿就绪后自动尝试启动；默认 0，需要显式 App_task_Start()。 */
#ifndef RCS_APP_AUTOSTART
#define RCS_APP_AUTOSTART                 0u
#endif

/* 自动启动的重试间隔，也用于 READY 等待反馈的诊断节流。 */
#ifndef RCS_APP_WAIT_FEEDBACK_MS
#define RCS_APP_WAIT_FEEDBACK_MS          2000u
#endif

/* ARMING 阶段等待"reset 后第一帧真实反馈"的上限；超时判为启动失败并锁定。 */
#ifndef RCS_APP_ARM_TIMEOUT_MS
#define RCS_APP_ARM_TIMEOUT_MS            200u
#endif
