/**
 * @file rcs_hal.c
 * @brief STM32F4 HAL 的 CAN 适配与底盘对外接口
 *
 * 接收路径：CAN 中断调用 RCS_OnCanRx 写入快照，任务侧在短临界区内取走。
 * 发送路径：独占发送邮箱，发送前检查总线关闭和邮箱占用，避免旧命令残留。
 */
#include "rcs_hal.h"
#include "rcs_config.h"

#include <string.h>

/* 私有宏 --------------------------------------------------------------------------------------------*/

#ifndef RCS_TX_ABORT_WAIT_MS
#define RCS_TX_ABORT_WAIT_MS 2u  ///< 等待在途帧发完的时间，1 Mbps 下一帧约 0.13 ms
#endif

/* 私有类型 ------------------------------------------------------------------------------------------*/

/**
 * @brief 单轮接收快照
 */
typedef struct {
    uint8_t data[8];        ///< 8 字节反馈数据
    uint8_t valid;          ///< 是否有新数据
    uint32_t time;          ///< 接收时间戳，ms
} rcs_rx_slot;

/* 私有变量 ------------------------------------------------------------------------------------------*/

static rcs_controller g_controller;         ///< 控制器实例
static CAN_HandleTypeDef *g_can;            ///< 绑定的 CAN 句柄
static volatile rcs_rx_slot g_inbox[4];     ///< 四轮接收快照

/* 私有函数 ------------------------------------------------------------------------------------------*/

/**
 * @brief 在临界区内取走接收快照并提交给控制器
 */
static void sync_rx(void)
{
    rcs_rx_slot snapshot[4];
    uint32_t primask = __get_PRIMASK();

    __disable_irq();
    for (unsigned i = 0; i < 4; i++) {
        snapshot[i] = g_inbox[i];
        g_inbox[i].valid = 0;
    }
    __set_PRIMASK(primask);

    for (unsigned i = 0; i < 4; i++) {
        if (!snapshot[i].valid)
            continue;

        rcs_control_feedback(&g_controller, (uint16_t)(0x201u + i),
                             snapshot[i].data, 8, snapshot[i].time);
    }
}

/**
 * @brief 通过 HAL 发送一帧 0x200 电流命令
 * @param context CAN 句柄
 * @param id 标准帧 ID
 * @param data 8 字节数据
 * @return 1 成功，0 总线关闭或发送失败
 */
static int transmit(void *context, uint16_t id, const uint8_t data[8])
{
    CAN_HandleTypeDef *can = (CAN_HandleTypeDef *)context;
    CAN_TxHeaderTypeDef header = {0};
    uint32_t mailbox;

    /* 硬件总线关闭标志，不依赖 HAL 错误中断。 */
    if ((can->Instance->ESR & CAN_ESR_BOFF) != 0u)
        return 0;

    if (HAL_CAN_GetTxMailboxesFreeLevel(can) != 3u) {
        (void)HAL_CAN_AbortTxRequest(can, CAN_TX_MAILBOX0
                                         | CAN_TX_MAILBOX1
                                         | CAN_TX_MAILBOX2);

        /* 已经在总线上发送的帧无法中止，限时等它发完。 */
        uint32_t start = HAL_GetTick();
        uint32_t spins = 0;

        while (HAL_CAN_GetTxMailboxesFreeLevel(can) != 3u) {
            if ((uint32_t)(HAL_GetTick() - start) >= RCS_TX_ABORT_WAIT_MS
                || ++spins >= 200000u)
                return 0;
        }
    }

    header.StdId = id;
    header.IDE = CAN_ID_STD;
    header.RTR = CAN_RTR_DATA;
    header.DLC = 8;
    header.TransmitGlobalTime = DISABLE;
    return HAL_CAN_AddTxMessage(can, &header, (uint8_t *)data, &mailbox)
           == HAL_OK;
}

/* 导出函数 ------------------------------------------------------------------------------------------*/

int RCS_Init(CAN_HandleTypeDef *can)
{
    const rcs_control_config config = RCS_CONTROL_CONFIG;
    uint32_t primask = __get_PRIMASK();

    __disable_irq();
    g_can = 0;
    memset((void *)g_inbox, 0, sizeof(g_inbox));
    __set_PRIMASK(primask);

    memset(&g_controller, 0, sizeof(g_controller));
    g_controller.fault = RCS_BAD_CONFIG;

    if (!can)
        return 0;

    if (RCS_CAN_BUS == 1 && can->Instance != CAN1)
        return 0;

    if (RCS_CAN_BUS == 2 && can->Instance != CAN2)
        return 0;

    if (!rcs_control_init(&g_controller, &config, transmit, can))
        return 0;

    g_can = can;
    return 1;
}

void RCS_OnCanRx(CAN_HandleTypeDef *can, const CAN_RxHeaderTypeDef *header,
                 const uint8_t data[8])
{
    if (!g_can || can != g_can || !header || !data)
        return;

    if (header->IDE != CAN_ID_STD || header->RTR != CAN_RTR_DATA)
        return;

    if (header->DLC != 8 || header->StdId < 0x201u || header->StdId > 0x204u)
        return;

    unsigned index = header->StdId - 0x201u;

    for (unsigned i = 0; i < 8; i++)
        g_inbox[index].data[i] = data[i];

    g_inbox[index].time = HAL_GetTick();
    g_inbox[index].valid = 1;
}

void RCS_Process(void)
{
    if (!g_can)
        return;

    sync_rx();
    rcs_control_step(&g_controller, HAL_GetTick());
}

int RCS_Arm(void)
{
    if (!g_can)
        return 0;

    sync_rx();
    return rcs_control_arm(&g_controller, HAL_GetTick());
}

void RCS_Lock(void)
{
    if (!g_can)
        return;

    uint8_t zero[8] = {0};
    rcs_control_lock(&g_controller);

    if (!transmit(g_can, 0x200u, zero))
        g_controller.fault = RCS_TX_FAILED;
}

int RCS_SetVelocity(float vx, float vy, float wz)
{
    if (!g_can)
        return 0;

    return rcs_control_command(&g_controller, vx, vy, wz, HAL_GetTick());
}

rcs_state RCS_State(void)
{
    return g_controller.state;
}

rcs_fault RCS_Fault(void)
{
    return g_controller.fault;
}
