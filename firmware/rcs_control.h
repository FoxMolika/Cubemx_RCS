/**
 * @file rcs_control.h
 * @brief 底盘速度环、超时与锁定状态机
 *
 * 控制器只做计算和状态管理，CAN 收发由 rcs_hal 提供。
 * 所有接口在同一个底盘任务中串行调用；ISR 只提交反馈。
 */
#pragma once

#include "rcs_chassis.h"

#ifdef __cplusplus
extern "C" {
#endif

/* 导出类型 ------------------------------------------------------------------------------------------*/

/**
 * @brief 控制器配置
 */
typedef struct {
    rcs_geometry geometry;        ///< 底盘几何与传动参数
    uint8_t motor_id[4];          ///< 各轮电机 ID，1..4，独占 CAN 0x200 的四个槽
    int8_t direction[4];          ///< 轮逻辑正方向到电机转子符号，只能为 ±1
    float kp;                     ///< 速度环比例增益
    float ki;                     ///< 速度环积分增益，按秒
    float kd;                     ///< 速度环微分增益，按秒，作用在测量值上
    float current_limit_raw;      ///< C620 电流指令幅值，原始计数
    uint32_t period_ms;           ///< 控制周期，ms
    uint32_t command_timeout_ms;  ///< 命令超时，ms
    uint32_t feedback_timeout_ms; ///< 反馈超时，ms
} rcs_control_config;

/**
 * @brief 控制器状态
 */
typedef enum {
    RCS_LOCKED = 0, ///< 已锁定，不输出电流
    RCS_ARMED = 1,  ///< 已解锁，可接受速度命令
    RCS_FAULT = 2   ///< 故障，输出已停止，需重新解锁
} rcs_state;

/**
 * @brief 故障原因
 */
typedef enum {
    RCS_OK = 0,           ///< 无故障
    RCS_BAD_CONFIG,       ///< 配置非法
    RCS_NO_FEEDBACK,      ///< 解锁时反馈不新鲜
    RCS_COMMAND_TIMEOUT,  ///< 命令超时
    RCS_FEEDBACK_TIMEOUT, ///< 反馈超时
    RCS_TX_FAILED,        ///< CAN 发送失败
    RCS_BAD_COMMAND,      ///< 速度命令非法或计算结果非有限
    RCS_BAD_TIMING        ///< 控制周期抖动过大
} rcs_fault;

/**
 * @brief CAN 发送函数
 * @param context 发送上下文
 * @param id 标准帧 ID
 * @param data 8 字节数据
 * @return 非 0 表示发送层已接受该帧
 */
typedef int (*rcs_send_fn)(void *context, uint16_t id, const uint8_t data[8]);

/**
 * @brief 控制器实例
 */
typedef struct {
    rcs_control_config cfg;     ///< 配置副本
    rcs_state state;            ///< 当前状态
    rcs_fault fault;            ///< 最近一次故障
    float feedback_rpm[4];      ///< 各轮反馈转速
    float target_rpm[4];        ///< 各轮目标转速
    float integral[4];          ///< 积分项
    float previous_feedback[4]; ///< 上一周期反馈，用于测量值微分
    uint32_t feedback_ms[4];    ///< 各轮反馈时间戳
    uint32_t command_ms;        ///< 最近一次命令时间戳
    uint32_t last_step_ms;      ///< 最近一次控制周期时间戳
    uint8_t feedback_seen[4];   ///< 各轮是否收到过反馈
    uint8_t command_seen;       ///< 当前解锁后是否已下达命令
    uint8_t step_seen;          ///< 是否已执行过控制周期
    rcs_send_fn send;           ///< 发送函数
    void *send_context;         ///< 发送上下文
} rcs_controller;

/* 导出函数 ------------------------------------------------------------------------------------------*/

/**
 * @brief 初始化控制器
 * @param controller 控制器实例
 * @param config 配置
 * @param send CAN 发送函数
 * @param context 发送上下文
 * @return 1 成功，0 配置或参数非法
 */
int rcs_control_init(rcs_controller *controller, const rcs_control_config *config, rcs_send_fn send,
                     void *context);

/**
 * @brief 提交一帧电机反馈
 * @param controller 控制器实例
 * @param id 反馈帧 ID，0x201..0x204
 * @param data 8 字节数据
 * @param dlc 数据长度，必须为 8
 * @param now 当前毫秒时间戳
 */
void rcs_control_feedback(rcs_controller *controller, uint16_t id, const uint8_t data[8], uint8_t dlc,
                          uint32_t now);

/**
 * @brief 解锁控制器
 *
 * LOCKED 和 FAULT 状态都可调用。成功会清除故障，但要求所有在用轮反馈新鲜；
 * 解锁后仍必须下达新命令才会输出。
 *
 * @param controller 控制器实例
 * @param now 当前毫秒时间戳
 * @return 1 成功，0 反馈不新鲜或参数非法
 */
int rcs_control_arm(rcs_controller *controller, uint32_t now);

/**
 * @brief 锁定控制器并清除旧命令
 * @param controller 控制器实例
 */
void rcs_control_lock(rcs_controller *controller);

/**
 * @brief 下达车体速度命令
 * @param controller 控制器实例
 * @param vx 前进速度，m/s
 * @param vy 左移速度，m/s
 * @param wz 逆时针角速度，rad/s
 * @param now 当前毫秒时间戳
 * @return 1 成功，0 未解锁或命令非法
 */
int rcs_control_command(rcs_controller *controller, float vx, float vy, float wz, uint32_t now);

/**
 * @brief 执行一次控制周期
 *
 * 每个配置周期调用一次，锁定期间也要持续调用，以便发出零电流。
 * 微分项作用在测量值上，换命令和解锁不会产生电流尖峰。
 *
 * @param controller 控制器实例
 * @param now 当前毫秒时间戳
 */
void rcs_control_step(rcs_controller *controller, uint32_t now);

#ifdef __cplusplus
}
#endif
