/** @file rcs_hw.h
 * @brief 生成的统一硬件C接口；未知协议须由用户真实驱动实现。
 */
#pragma once
#include "rcs_hal.h"
#include "rcs_nav.h"
#include <stddef.h>
#ifdef __cplusplus
extern "C" {
#endif
enum { RCS_HW_OK=0, RCS_HW_CONFIG=1, RCS_HW_INIT=2, RCS_HW_TIMEOUT=3,
       RCS_HW_COMMAND=4, RCS_HW_EXCEPTION=5, RCS_HW_UART=6, RCS_HW_TIMING=7 };
int rcs_hw_init(void);
/* 接收已启动不等于采样就绪；所有必需定位传感器须有未过期的真实样本。 */
int rcs_hw_ready(void);
void rcs_hw_service(uint32_t now_ms);
void rcs_hw_set_enabled(int enabled);
void rcs_hw_stop(void);
int rcs_hw_fault(void);
int rcs_hw_command(const char *instance, const char *operation, float value);
/* 业务状态入口使用：拒绝命令即锁存故障并取消旧活动状态，防止沿用旧done。 */
int rcs_hw_require_command(const char *instance, const char *operation, float value);
int rcs_hw_done(const char *instance);
void rcs_hw_cancel(const char *instance);
void rcs_hw_on_can_rx(CAN_HandleTypeDef *can, const CAN_RxHeaderTypeDef *header,
                      const uint8_t data[8]);
#if defined(HAL_UART_MODULE_ENABLED)
void rcs_hw_on_uart_rx(UART_HandleTypeDef *uart, const uint8_t *data, size_t len);
void rcs_hw_uart_irq(UART_HandleTypeDef *uart);
void rcs_hw_uart_error(UART_HandleTypeDef *uart);
#endif
#if defined(HAL_TIM_MODULE_ENABLED)
void rcs_hw_on_timer(TIM_HandleTypeDef *timer);
#endif
int rcs_hw_read_observation(const char *instance, rcs_observation *sample);
@OBSERVATION_DECLARATIONS@
#ifdef __cplusplus
}
#endif
