/**
 * @file rcs_can_port.h
 * @brief CAN 接收过滤器、启动与轮询收帧
 *
 * 只做过滤器与接收搬运。引脚、波特率、时钟仍由 CubeMX 的 .ioc 管理，
 * 不替换全局 HAL 回调（与 rcs_hal.h 的约定一致）。
 */
#pragma once

#include "rcs_hal.h"

#ifdef __cplusplus
extern "C" {
#endif

/* 配置接收过滤器并启动 CAN。返回 1 成功。
 * 过滤器接受 0x200~0x207，覆盖 0x201~0x204 的四轮反馈。 */
int rcs_can_port_start(CAN_HandleTypeDef *can);

/* 把 FIFO0 里的帧逐条交给 RCS_OnCanRx。在主循环或底盘任务里周期调用。 */
void rcs_can_port_poll(CAN_HandleTypeDef *can);

/* 中断方式下的入口：在 HAL_CAN_RxFifo0MsgPendingCallback 里调用。 */
void rcs_can_port_on_rx(CAN_HandleTypeDef *can, const CAN_RxHeaderTypeDef *header,
                        const uint8_t data[8]);

#ifdef __cplusplus
}
#endif