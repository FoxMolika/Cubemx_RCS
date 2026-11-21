/** @file rcs_uart.h
 * @brief 队内UART安装接口桥，接收归alpha统一分发，无独立HAL全局回调。
 */
#pragma once
#include "rcs_private_config.h"
typedef uint8_t rcs_uart_t;
enum { MODE_NONE=0, MODE_IT=1 };
typedef size_t (*rcs_uart_rx_fn)(rcs_uart_t uart, const uint8_t *data, size_t len);
typedef struct {
    UART_HandleTypeDef *huartx;
    int txMode;
    size_t txBufferSize;
    uint32_t txTimeout;
    int rxMode;
    size_t rxBufferSize;
    rcs_uart_rx_fn rxCallback;
} rcs_uart_conf_t;
rcs_err_t rcs_uart_driver_install(const rcs_uart_conf_t *conf, rcs_uart_t uart);
