/**
 * @file rcs_chassis.h
 * @brief 麦轮 / 全向轮底盘的运动学接口
 *
 * 车体坐标系：x 向前、y 向左、角速度逆时针为正。
 * 输入输出均为 SI 单位，电机侧使用 rpm。
 * 参数由 CubeMX 的 RCS.Chassis 组件写入 rcs_config.h。
 */
#pragma once

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* 导出类型 ------------------------------------------------------------------------------------------*/

/**
 * @brief 底盘布局
 */
typedef enum {
    RCS_MECANUM_X = 0, ///< 四麦轮 X 型：W0 左前、W1 右前、W2 左后、W3 右后
    RCS_OMNI3 = 1,     ///< 三全向轮，等角间隔 120°
    RCS_OMNI4 = 2      ///< 四全向轮，等角间隔 90°
} rcs_layout;

/**
 * @brief 底盘几何与传动参数
 */
typedef struct {
    rcs_layout layout;      ///< 底盘布局
    float wheel_diameter_m; ///< 轮子直径，m
    float wheelbase_m;      ///< 麦轮前后轮中心距，m
    float track_m;          ///< 麦轮左右轮中心距，m
    float omni_radius_m;    ///< 全向轮中心到底盘中心的距离，m
    float omni_theta0_rad;  ///< 全向轮 W0 位置角，rad
    float gear_ratio;       ///< 电机转速 / 轮转速
    float max_motor_rpm;    ///< 电机端转速上限，rpm
} rcs_geometry;

/* 导出函数 ------------------------------------------------------------------------------------------*/

/**
 * @brief 获取当前布局使用的轮子数量
 * @param geometry 底盘几何参数
 * @return 四麦轮 / 四全向轮返回 4，三全向轮返回 3，布局非法返回 0
 */
unsigned rcs_wheel_count(const rcs_geometry *geometry);

/**
 * @brief 检查几何参数是否可用于解算
 * @param geometry 底盘几何参数
 * @return 1 合法，0 非法
 */
int rcs_geometry_valid(const rcs_geometry *geometry);

/**
 * @brief 将车体速度解算为四个电机端转速
 * @param geometry 底盘几何参数
 * @param vx 前进速度，m/s
 * @param vy 左移速度，m/s
 * @param wz 逆时针角速度，rad/s
 * @param motor_rpm 输出电机端转速，按 W0..W3 排列
 * @return 1 成功；参数非法或结果非有限时清零并返回 0
 */
int rcs_inverse(const rcs_geometry *geometry, float vx, float vy, float wz, float motor_rpm[4]);

/**
 * @brief 将四个电机端转速解算为车体速度
 * @param geometry 底盘几何参数
 * @param motor_rpm 输入电机端转速，按 W0..W3 排列
 * @param velocity 输出车体速度 [vx, vy, wz]
 * @return 1 成功；参数非法或结果非有限时清零并返回 0
 */
int rcs_forward(const rcs_geometry *geometry, const float motor_rpm[4], float velocity[3]);

#ifdef __cplusplus
}
#endif
