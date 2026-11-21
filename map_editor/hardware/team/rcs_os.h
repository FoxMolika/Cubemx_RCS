/** @file rcs_os.h
 * @brief 驱动所需的毫秒时间与可选日志桥，不创建RTOS任务。
 */
#pragma once
#include "rcs_private_config.h"
typedef uint32_t TickType_t;
#define portMAX_DELAY UINT32_MAX
static inline TickType_t xTaskGetTickCount(void) { return HAL_GetTick(); }
static inline int rcs_kprintf(const char *format, uint32_t wait, ...)
{ (void)format; (void)wait; return 0; }
int rcs_hw_team_cylinder_allowed(size_t index);
