/**
 * @file rcs_hal.c
 * @brief STM32F4 HAL 的 CAN 适配与底盘对外接口
 *
 * 接收路径：CAN 中断调用 RCS_OnCanRx 写入快照，任务侧在短临界区内取走。
 * 发送路径：独占发送邮箱，发送前检查总线关闭和邮箱占用，避免旧命令残留。
 */
#include "rcs_hal.h"

#if !RCS_NO_CHASSIS
#include "rcs_config.h"

#include <string.h>

/* 私有宏 --------------------------------------------------------------------------------------------*/

#ifndef RCS_TX_ABORT_WAIT_MS
#define RCS_TX_ABORT_WAIT_MS 2u ///< 等待在途帧发完的时间，1 Mbps 下一帧约 0.13 ms
#endif

/* 私有类型 ------------------------------------------------------------------------------------------*/

/**
 * @brief 单轮接收快照
 */
typedef struct {
    uint8_t data[8]; ///< 8 字节反馈数据
    uint8_t valid;   ///< 是否有新数据
    uint32_t time;   ///< 接收时间戳，ms
} rcs_rx_slot;

/* 私有变量 ------------------------------------------------------------------------------------------*/

static rcs_controller g_controller;     ///< 控制器实例
static CAN_HandleTypeDef *g_can;        ///< 绑定的 CAN 句柄
static volatile rcs_rx_slot g_inbox[4]; ///< 四轮接收快照

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

        rcs_control_feedback(&g_controller, (uint16_t)(0x201u + i), snapshot[i].data, 8, snapshot[i].time);
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
        (void)HAL_CAN_AbortTxRequest(can, CAN_TX_MAILBOX0 | CAN_TX_MAILBOX1 | CAN_TX_MAILBOX2);

        /* 已经在总线上发送的帧无法中止，限时等它发完。 */
        uint32_t start = HAL_GetTick();
        uint32_t spins = 0;

        while (HAL_CAN_GetTxMailboxesFreeLevel(can) != 3u) {
            if ((uint32_t)(HAL_GetTick() - start) >= RCS_TX_ABORT_WAIT_MS || ++spins >= 200000u)
                return 0;
        }
    }

    header.StdId = id;
    header.IDE = CAN_ID_STD;
    header.RTR = CAN_RTR_DATA;
    header.DLC = 8;
    header.TransmitGlobalTime = DISABLE;
    return HAL_CAN_AddTxMessage(can, &header, (uint8_t *)data, &mailbox) == HAL_OK;
}

/* 导出函数 ------------------------------------------------------------------------------------------*/

/**
 * @brief 初始化底盘模块并绑定 CAN
 * @param can 已配置好的 CAN 句柄
 * @return 1 成功，0 句柄与配置的 CAN 通道不符或配置非法
 * @note 只初始化模块状态，不替代 HAL CAN 的引脚、时钟、过滤器、启动和通知设置。
 */
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

/**
 * @brief 提交一帧 CAN 接收数据
 * @param can 产生中断的 CAN 句柄
 * @param header 接收头
 * @param data 8 字节数据
 * @note 由现有的 CAN 接收中断分发调用；不覆盖全局 HAL 回调。
 */
void RCS_OnCanRx(CAN_HandleTypeDef *can, const CAN_RxHeaderTypeDef *header, const uint8_t data[8])
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

/**
 * @brief 执行一次底盘控制周期
 * @note 由同一个底盘任务按 RCS_PERIOD_MS 调用，锁定期间也要持续调用。
 */
void RCS_Process(void)
{
    if (!g_can)
        return;

    sync_rx();
    rcs_control_step(&g_controller, HAL_GetTick());
}

/**
 * @brief 解锁底盘
 * @return 1 成功，0 反馈不新鲜或未初始化
 * @note 成功只表示可以接受命令，仍需调用 RCS_SetVelocity 才会输出。
 */
int RCS_Arm(void)
{
    if (!g_can)
        return 0;

    sync_rx();
    return rcs_control_arm(&g_controller, HAL_GetTick());
}

/**
 * @brief 锁定底盘并发送零电流
 */
void RCS_Lock(void)
{
    if (!g_can)
        return;

    uint8_t zero[8] = {0};
    rcs_control_lock(&g_controller);

    if (!transmit(g_can, 0x200u, zero))
        g_controller.fault = RCS_TX_FAILED;
}

/**
 * @brief 下达车体速度命令
 * @param vx 前进速度，m/s
 * @param vy 左移速度，m/s
 * @param wz 逆时针角速度，rad/s
 * @return 1 成功，0 未解锁或命令非法
 * @note 需要通过周期调用不断续期，超过命令超时会进入故障状态。
 */
int RCS_SetVelocity(float vx, float vy, float wz)
{
    if (!g_can)
        return 0;

    return rcs_control_command(&g_controller, vx, vy, wz, HAL_GetTick());
}

/**
 * @brief 查询当前状态
 * @return 控制器状态
 */
rcs_state RCS_State(void)
{
    return g_controller.state;
}

/**
 * @brief 查询最近一次故障
 * @return 故障原因
 */
rcs_fault RCS_Fault(void)
{
    return g_controller.fault;
}

#else /* RCS_NO_CHASSIS */

/* 无底盘模式：接口齐全但不驱动电机。RCS_Arm 恒失败，上层不会误判为可动；
 * CAN 回帧入口保留为空实现，未启用 CAN 的工程可以直接链接通过。 */

/**
 * @brief 无底盘模式的初始化，仅记录已完成
 * @param can 未使用，允许为空
 * @return 始终为 1
 */
int RCS_Init(CAN_HandleTypeDef *can)
{
    (void)can;
    return 1;
}

/**
 * @brief 无底盘模式忽略回帧
 * @param can 未使用
 * @param header 未使用
 * @param data 未使用
 */
void RCS_OnCanRx(CAN_HandleTypeDef *can, const CAN_RxHeaderTypeDef *header, const uint8_t data[8])
{
    (void)can;
    (void)header;
    (void)data;
}

/**
 * @brief 无底盘模式的周期处理，无动作
 */
void RCS_Process(void)
{
}

/**
 * @brief 无底盘模式不接受解锁
 * @return 始终为 0
 */
int RCS_Arm(void)
{
    return 0;
}

/**
 * @brief 无底盘模式的锁定，无动作
 */
void RCS_Lock(void)
{
}

/**
 * @brief 无底盘模式拒绝速度命令
 * @param vx 未使用
 * @param vy 未使用
 * @param wz 未使用
 * @return 始终为 0
 */
int RCS_SetVelocity(float vx, float vy, float wz)
{
    (void)vx;
    (void)vy;
    (void)wz;
    return 0;
}

/**
 * @brief 无底盘模式始终报告已锁定
 * @return RCS_LOCKED
 */
rcs_state RCS_State(void)
{
    return RCS_LOCKED;
}

/**
 * @brief 无底盘模式不产生故障
 * @return RCS_OK
 */
rcs_fault RCS_Fault(void)
{
    return RCS_OK;
}

#endif /* RCS_NO_CHASSIS */
