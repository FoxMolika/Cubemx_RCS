/**
  ******************************************************************************
  * @file    xs_gyro.h
  * @brief   维特 JY61P / HWT101 串口陀螺仪：与 foxmolika 硬件模板对接的分层封装
  ******************************************************************************
  *
  * 【来源】
  *   解析内核与状态机取自 XS_spin8 示例工程 BSP/Src/gyro.c（691 行版本）：
  *     - 11 字节帧 + 累加和校验，校验失败从第 2 字节重新找 0x55 对齐
  *     - 角度帧 0x53、角速度帧 0x52、加速度帧 0x51
  *     - 增量折到 ±180 后再累加，跨 ±180 不跳变、转多圈不丢
  *     - 掉线重连跳变保护（>500ms 且单步 >30° 判为模块复位）
  *     - 中断只入环形缓冲，主循环解析
  *   删除的部分（示例工程专属，插件工程里没有对应物）：
  *     - Gyro_SignMonitor()：依赖 odometry.c 的轮式航向，改由模板参数 Direction 表达
  *     - dbg_gyro_* 观察量：依赖 app_dbg.h，改用本头文件里的查询函数
  *     - 写死的 USART6 / huart6：改为 init 传入句柄
  *     - 自己挂 HAL_UART_RxCpltCallback：拆到 xs_gyro_port.c，按接入方式取舍
  *
  * 【文件分工】
  *   xs_gyro.h/.c   纯解析与状态，不知道谁负责收字节
  *   xs_gyro_port.c 单字节中断接收 + HAL 回调（个人模板接入方式用）
  *
  * 【接入 foxmolika 的两种用法】
  *   个人模板（DriverKind=custom）：模板源文件要三个，Init 调 xs_gyro_port_start()，
  *     工程里必须把 RCS_HW_DEFINE_UART_CALLBACK 定义为 0，避免与生成的 HAL 回调重定义。
  *   内置模板（新增 DriverKind）：只交付 xs_gyro.h/.c，由 rcs_hw.cpp 的统一 UART
  *     分发器调用 xs_gyro_feed()，此时不要编译 xs_gyro_port.c。
  ******************************************************************************
  */

#ifndef XS_GYRO_H
#define XS_GYRO_H

#include <stdint.h>
#include <stdbool.h>

#include "main.h"

#ifdef __cplusplus
extern "C" {
#endif

/*==============================================================================
 * 1. 编译期参数（可在工程 C/C++ Define 里覆盖，不改本文件）
 *============================================================================*/

/** 多久没收到角度帧判为离线，单位 ms。100Hz 输出时正常间隔 10ms，
 *  给到 200ms 是为了容忍主循环被 Flash 操作或长 CAN 帧阻塞。 */
#ifndef XS_GYRO_TIMEOUT_MS
#define XS_GYRO_TIMEOUT_MS          200u
#endif

/** 接收环形缓冲大小，必须是 2 的幂（内部用掩码取模）。
 *  1024 字节 ≈ 200Hz 下 460ms、100Hz 下 930ms 的缓冲。 */
#ifndef XS_GYRO_RING_SIZE
#define XS_GYRO_RING_SIZE           1024u
#endif

/** 上电默认 yaw / wz 符号（+1 或 -1）。插件里一般用模板参数 Direction 处理，
 *  本宏只作为没调 xs_gyro_set_sign() 时的初值。 */
#ifndef XS_GYRO_YAW_SIGN
#define XS_GYRO_YAW_SIGN            (+1.0f)
#endif

/** 断线重连判定：超过这么久没收到角度帧，再收到时检查增量是否异常 */
#define XS_GYRO_RELATCH_MS          500u

/** 重连时允许的最大单步增量，单位度。超过就认为模块被断电复位过。 */
#define XS_GYRO_JUMP_LIMIT_DEG      30.0f

/*==============================================================================
 * 2. 状态位（xs_gyro_state() 的返回值）
 *============================================================================*/

#define XS_GYRO_STATE_ONLINE        0x01u   /**< bit0：最近 XS_GYRO_TIMEOUT_MS 内收到过角度帧 */
#define XS_GYRO_STATE_ZEROED        0x02u   /**< bit1：零点已经对齐 */
#define XS_GYRO_STATE_READY         (XS_GYRO_STATE_ONLINE | XS_GYRO_STATE_ZEROED)

/*==============================================================================
 * 3. 接口
 *============================================================================*/

/**
 * @brief  清空全部解析状态（不碰串口，不启动接收）
 * @note   不阻塞、不发任何串口数据。接收由调用方决定：
 *         xs_gyro_port_start()（个人模板）或统一 UART 分发器（内置模板）。
 *         本函数只复位状态机，因此可以在通信已启动后安全重入。
 */
void xs_gyro_init(void);

/**
 * @brief  逐字节喂入解析状态机（可在中断里调用）
 * @param  data 字节缓冲
 * @param  len  字节数，0 允许
 * @note   只做环形缓冲入队，不解析、不校验、不调用 HAL。
 */
void xs_gyro_feed(const uint8_t *data, uint32_t len);

/**
 * @brief  主循环任务：把环形缓冲搬空并解析成航向/角速度
 * @note   由 rcs_hw_service() 的周期钩子调用；不做限速，搬空即返回。
 */
void xs_gyro_update(void);

/**
 * @brief  停机：清空缓冲与帧状态，并让航向失效（不关闭串口外设）
 * @note   由模板的 Stop 钩子调用；之后需要重新 xs_gyro_init() 才能恢复。
 */
void xs_gyro_stop(void);

/** @brief 最近 XS_GYRO_TIMEOUT_MS 内收到过角度帧 */
bool xs_gyro_online(void);

/** @brief 在线且零点已对齐（等价于 state == 3） */
bool xs_gyro_heading_ready(void);

/** @brief 当前航向，单位 rad，范围 (-pi, pi] */
float xs_gyro_yaw_rad(void);

/** @brief 当前角速度，单位 rad/s，符号已乘 xs_gyro_sign()；离线返回 0 */
float xs_gyro_wz_rad_s(void);

/**
 * @brief 设定零点：让"此刻的朝向"等于 heading_rad
 * @note  第一帧角度到达前调用会挂起，收到第一帧后自动折算，不会跳变。
 */
void xs_gyro_set_heading_zero(float heading_rad);

/** @brief 运行时设定 yaw / wz 符号系数（>=0 取 +1，<0 取 -1） */
void xs_gyro_set_sign(float sign);

/** @brief 当前符号系数（+1 / -1） */
float xs_gyro_sign(void);

/** @brief 状态位组合：XS_GYRO_STATE_xxx */
uint8_t xs_gyro_state(void);

/** @brief 累计有效角度帧数（100Hz 模块每秒约 +100），同时可作观测序号 */
uint32_t xs_gyro_sequence(void);

/** @brief 校验失败帧数 */
uint32_t xs_gyro_bad_frames(void);

/** @brief 环形缓冲溢出字节数 */
uint32_t xs_gyro_overflow(void);

/** @brief 最近一帧角度的年龄 ms；从未收到返回 0xFFFFFFFF */
uint32_t xs_gyro_age_ms(void);

/*==============================================================================
 * 4. 端口层接口（实现在 xs_gyro_port.c；内置模板接入时不需要该文件）
 *============================================================================*/

/**
 * @brief  复位解析状态并启动 1 字节中断接收
 * @param  uart 必须是 CubeMX 生成的 HAL_UART 对象，如 &huart6
 * @return 1 成功；0 句柄为空或 HAL 拒绝启动
 */
int xs_gyro_port_start(UART_HandleTypeDef *uart);

/** @brief 中止接收并让航向失效（不关闭串口外设） */
void xs_gyro_port_stop(void);

/** @brief 重新挂接收失败的累计次数（调试用，正常应为 0） */
uint32_t xs_gyro_port_arm_failures(void);

#ifdef __cplusplus
}
#endif

#endif /* XS_GYRO_H */
