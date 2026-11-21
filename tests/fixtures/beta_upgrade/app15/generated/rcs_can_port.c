/**
 * @file rcs_can_port.c
 * @brief CAN 过滤器、启动与收帧搬运
 *
 * 轮询方式默认开启，不占用全局 HAL 回调；需要中断时再用 rcs_can_port_on_rx()。
 */
#include "rcs_can_port.h"

#if RCS_NO_CHASSIS

/* 未启用 CAN：本模块退化为空实现，工程仍可编译与链接。
 * 返回 1 表示"没有需要启动的总线"，调用方据此继续初始化应用层。 */
int rcs_can_port_start(CAN_HandleTypeDef *can)
{
    (void)can;
    return 1;
}

void rcs_can_port_poll(CAN_HandleTypeDef *can)
{
    (void)can;
}

void rcs_can_port_on_rx(CAN_HandleTypeDef *can, const CAN_RxHeaderTypeDef *header,
                        const uint8_t data[8])
{
    (void)can;
    (void)header;
    (void)data;
}

#else /* !RCS_NO_CHASSIS */

/* 本底盘：0x200 发四路电流，0x201~0x204 回四轮反馈。
 * 掩码只比较低三位以外的位，因此同时接受 0x200~0x207。 */
#define RCS_CAN_ID_BASE      0x200u
#define RCS_CAN_ID_MASK      0x7F8u

int rcs_can_port_start(CAN_HandleTypeDef *can)
{
    CAN_FilterTypeDef filter;

    if (can == 0)
        return 0;

    filter.FilterBank = 0;
    filter.FilterMode = CAN_FILTERMODE_IDMASK;
    filter.FilterScale = CAN_FILTERSCALE_32BIT;
    filter.FilterIdHigh = (uint32_t)(RCS_CAN_ID_BASE << 5);
    filter.FilterIdLow = 0u;
    filter.FilterMaskIdHigh = (uint32_t)(RCS_CAN_ID_MASK << 5);
    filter.FilterMaskIdLow = 0u;
    filter.FilterFIFOAssignment = CAN_RX_FIFO0;
    filter.FilterActivation = CAN_FILTER_ENABLE;
    filter.SlaveStartFilterBank = 14u;

    if (HAL_CAN_ConfigFilter(can, &filter) != HAL_OK)
        return 0;
    if (HAL_CAN_Start(can) != HAL_OK)
        return 0;
    return 1;
}

void rcs_can_port_poll(CAN_HandleTypeDef *can)
{
    CAN_RxHeaderTypeDef header;
    uint8_t data[8];

    if (can == 0)
        return;

    /* 每轮把 FIFO 抽干，避免积压导致反馈时间戳落后。 */
    while (HAL_CAN_GetRxFifoFillLevel(can, CAN_RX_FIFO0) > 0u) {
        if (HAL_CAN_GetRxMessage(can, CAN_RX_FIFO0, &header, data) != HAL_OK)
            return;
        RCS_OnCanRx(can, &header, data);
    }
}

void rcs_can_port_on_rx(CAN_HandleTypeDef *can, const CAN_RxHeaderTypeDef *header,
                        const uint8_t data[8])
{
    if (can == 0 || header == 0 || data == 0)
        return;
    RCS_OnCanRx(can, header, data);
}

#endif /* RCS_NO_CHASSIS */

/* RCS GENERATED SHA256 9654ab31a796a6f41f33d464fe9c13297340508d14f265b011979a9a5c8c3768 */
