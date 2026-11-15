/**
 * @file rcs_hal.h
 * @brief STM32F4 HAL 的 CAN 适配与底盘对外接口
 *
 * 组件独占所选 CAN 的发送邮箱和 0x200 的四个电流槽，不替换全局 HAL 回调，
 * 也不修改 CubeMX 配置的引脚、时钟和过滤器。
 */
#pragma once

#include "stm32f4xx_hal.h"
#include "rcs_control.h"

#ifdef __cplusplus
extern "C" {
#endif

/* 导出函数 ------------------------------------------------------------------------------------------*/

/**
 * @brief 初始化底盘模块并绑定 CAN
 * @param can 已配置好的 CAN 句柄
 * @return 1 成功，0 句柄与配置的 CAN 通道不符或配置非法
 * @note 只初始化模块状态，不替代 HAL CAN 的引脚、时钟、过滤器、启动和通知设置。
 */
int RCS_Init(CAN_HandleTypeDef *can);

/**
 * @brief 提交一帧 CAN 接收数据
 * @param can 产生中断的 CAN 句柄
 * @param header 接收头
 * @param data 8 字节数据
 * @note 由现有的 CAN 接收中断分发调用；不覆盖全局 HAL 回调。
 */
void RCS_OnCanRx(CAN_HandleTypeDef *can, const CAN_RxHeaderTypeDef *header,
                 const uint8_t data[8]);

/**
 * @brief 执行一次底盘控制周期
 * @note 由同一个底盘任务按 RCS_PERIOD_MS 调用，锁定期间也要持续调用。
 */
void RCS_Process(void);

/**
 * @brief 解锁底盘
 * @return 1 成功，0 反馈不新鲜或未初始化
 * @note 成功只表示可以接受命令，仍需调用 RCS_SetVelocity 才会输出。
 */
int RCS_Arm(void);

/**
 * @brief 锁定底盘并发送零电流
 */
void RCS_Lock(void);

/**
 * @brief 下达车体速度命令
 * @param vx 前进速度，m/s
 * @param vy 左移速度，m/s
 * @param wz 逆时针角速度，rad/s
 * @return 1 成功，0 未解锁或命令非法
 * @note 需要通过周期调用不断续期，超过命令超时会进入故障状态。
 */
int RCS_SetVelocity(float vx, float vy, float wz);

/**
 * @brief 查询当前状态
 * @return 控制器状态
 */
rcs_state RCS_State(void);

/**
 * @brief 查询最近一次故障
 * @return 故障原因
 */
rcs_fault RCS_Fault(void);

#ifdef __cplusplus
}
#endif
