/** @file rcs_hw_callbacks.c
 * @brief 统一HAL回调归属；已有分发器可通过编译宏关闭相应强定义。
 */
#include "rcs_hw.h"
#include "rcs_hw_config.h"
#include "rcs_can_port.h"
#if !RCS_NO_CHASSIS && RCS_HW_DEFINE_CAN_CALLBACK
void HAL_CAN_RxFifo0MsgPendingCallback(CAN_HandleTypeDef *can)
{
    CAN_RxHeaderTypeDef header;
    uint8_t data[8];
    uint32_t saved;
    rcs_can_port_poll(can);
    if (can == 0) return;
    saved = __get_PRIMASK();
    __disable_irq();
    while (HAL_CAN_GetRxFifoFillLevel(can, CAN_RX_FIFO0) > 0u) {
        if (HAL_CAN_GetRxMessage(can, CAN_RX_FIFO0, &header, data) != HAL_OK) break;
        rcs_can_port_on_rx(can, &header, data);
    }
    __set_PRIMASK(saved);
}
#endif
#if defined(HAL_UART_MODULE_ENABLED) && RCS_HW_NEEDS_UART_CALLBACK && RCS_HW_DEFINE_UART_CALLBACK && !defined(RCS_HW_EXTERNAL_UART_BRIDGE)
void HAL_UART_RxCpltCallback(UART_HandleTypeDef *uart) { rcs_hw_uart_irq(uart); }
void HAL_UART_ErrorCallback(UART_HandleTypeDef *uart) { rcs_hw_uart_error(uart); }
#endif
#if defined(HAL_TIM_MODULE_ENABLED) && RCS_HW_NEEDS_TIMER_CALLBACK && RCS_HW_DEFINE_TIMER_CALLBACK && !defined(RCS_HW_CUBEMX_TIMER_BRIDGE)
void HAL_TIM_PeriodElapsedCallback(TIM_HandleTypeDef *timer) { rcs_hw_on_timer(timer); }
#endif
