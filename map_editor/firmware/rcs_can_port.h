/**
 * @file rcs_can_port.h
 * @brief FIFO0中断逐帧收帧与任务侧补充排空
 * @note CubeMX负责引脚、时钟、位时序、NVIC和HAL_CAN_IRQHandler。
 */
#pragma once
#include "rcs_hal.h"
#ifdef __cplusplus
extern "C" {
#endif
int rcs_can_port_start(CAN_HandleTypeDef *can);
/* 主循环每轮可调，不按5ms速度环周期节流；与IRQ排空原子互斥。 */
void rcs_can_port_poll(CAN_HandleTypeDef *can);
/* 采样reset专用：原子丢弃绑定FIFO中的旧帧，不转发成新反馈。 */
int rcs_can_port_discard_rx(void);
/* 已有全局回调读帧后调用，同时分发给底盘反馈与真实角度采集。 */
void rcs_can_port_on_rx(CAN_HandleTypeDef *can, const CAN_RxHeaderTypeDef *header,
                        const uint8_t data[8]);
/* 默认强定义HAL_CAN_RxFifo0MsgPendingCallback。已有同名回调或其他CAN用户时，
 * 设RCS_CAN_PORT_DEFINE_HAL_CALLBACK=0，在原回调调用poll或逐帧on_rx；
 * 不静默覆盖已有全局实现，不允许两套代码重复消费同一FIFO。
 */
#ifdef __cplusplus
}
#endif
