/**
 * @file rcs_chassis.c
 * @brief 麦轮 / 全向轮底盘的运动学解算
 *
 * 车体速度与四个电机端转速互算。输入 SI 单位，输出电机端 rpm。
 * 所有函数在参数非法或结果非有限时返回 0，并把输出清零。
 */
#include "rcs_chassis.h"

#include <math.h>
#include <stddef.h>

/* 私有宏 --------------------------------------------------------------------------------------------*/

#define RCS_PI 3.14159265358979323846f

/* 私有函数 ------------------------------------------------------------------------------------------*/

/**
 * @brief 把数组清零
 * @param values 目标数组，允许为 NULL
 * @param count 元素个数
 */
static void rcs_zero(float *values, unsigned count)
{
    if (!values)
        return;

    for (unsigned i = 0; i < count; i++)
        values[i] = 0.0f;
}

/* 导出函数 ------------------------------------------------------------------------------------------*/

/**
 * @brief 获取当前布局使用的轮子数量
 * @param geometry 底盘几何参数
 * @return 四麦轮 / 四全向轮返回 4，三全向轮返回 3，布局非法返回 0
 */
unsigned rcs_wheel_count(const rcs_geometry *geometry)
{
    if (!geometry)
        return 0u;

    if (geometry->layout == RCS_OMNI3)
        return 3u;

    if (geometry->layout == RCS_MECANUM_X || geometry->layout == RCS_OMNI4)
        return 4u;

    return 0u;
}

/**
 * @brief 检查几何参数是否可用于解算
 * @param geometry 底盘几何参数
 * @return 1 合法，0 非法
 */
int rcs_geometry_valid(const rcs_geometry *geometry)
{
    if (!rcs_wheel_count(geometry))
        return 0;

    if (!isfinite(geometry->wheel_diameter_m) || geometry->wheel_diameter_m <= 0.0f)
        return 0;

    if (!isfinite(geometry->gear_ratio) || geometry->gear_ratio <= 0.0f)
        return 0;

    if (!isfinite(geometry->max_motor_rpm) || geometry->max_motor_rpm <= 0.0f)
        return 0;

    if (geometry->layout == RCS_MECANUM_X) {
        if (!isfinite(geometry->wheelbase_m) || geometry->wheelbase_m <= 0.0f)
            return 0;
        if (!isfinite(geometry->track_m) || geometry->track_m <= 0.0f)
            return 0;
        return 1;
    }

    if (!isfinite(geometry->omni_radius_m) || geometry->omni_radius_m <= 0.0f)
        return 0;

    if (!isfinite(geometry->omni_theta0_rad))
        return 0;

    return 1;
}

/**
 * @brief 将车体速度解算为四个电机端转速
 * @param geometry 底盘几何参数
 * @param vx 前进速度，m/s
 * @param vy 左移速度，m/s
 * @param wz 逆时针角速度，rad/s
 * @param motor_rpm 输出电机端转速，按 W0..W3 排列
 * @return 1 成功；参数非法或结果非有限时清零并返回 0
 */
int rcs_inverse(const rcs_geometry *geometry, float vx, float vy, float wz, float motor_rpm[4])
{
    float wheel_speed[4] = {0.0f};
    float peak = 0.0f;
    float scale;

    rcs_zero(motor_rpm, 4);

    if (!motor_rpm || !rcs_geometry_valid(geometry))
        return 0;

    if (!isfinite(vx) || !isfinite(vy) || !isfinite(wz))
        return 0;

    unsigned count = rcs_wheel_count(geometry);

    if (geometry->layout == RCS_MECANUM_X) {
        float rotation = (geometry->wheelbase_m + geometry->track_m) * 0.5f * wz;
        wheel_speed[0] = vx - vy - rotation;
        wheel_speed[1] = vx + vy + rotation;
        wheel_speed[2] = vx + vy - rotation;
        wheel_speed[3] = vx - vy + rotation;
    } else {
        for (unsigned i = 0; i < count; i++) {
            float angle = geometry->omni_theta0_rad + 2.0f * RCS_PI * (float)i / (float)count;
            wheel_speed[i] = -sinf(angle) * vx + cosf(angle) * vy + geometry->omni_radius_m * wz;
        }
    }

    scale = 60.0f * geometry->gear_ratio / (RCS_PI * geometry->wheel_diameter_m);
    if (!isfinite(scale))
        return 0;

    for (unsigned i = 0; i < count; i++) {
        motor_rpm[i] = wheel_speed[i] * scale;

        if (!isfinite(motor_rpm[i])) {
            rcs_zero(motor_rpm, 4);
            return 0;
        }

        if (fabsf(motor_rpm[i]) > peak)
            peak = fabsf(motor_rpm[i]);
    }

    if (peak > geometry->max_motor_rpm) {
        for (unsigned i = 0; i < count; i++)
            motor_rpm[i] *= geometry->max_motor_rpm / peak;
    }

    return 1;
}

/**
 * @brief 将四个电机端转速解算为车体速度
 * @param geometry 底盘几何参数
 * @param motor_rpm 输入电机端转速，按 W0..W3 排列
 * @param velocity 输出车体速度 [vx, vy, wz]
 * @return 1 成功；参数非法或结果非有限时清零并返回 0
 */
int rcs_forward(const rcs_geometry *geometry, const float motor_rpm[4], float velocity[3])
{
    float wheel_speed[4] = {0.0f};
    float scale;

    rcs_zero(velocity, 3);

    if (!velocity || !motor_rpm || !rcs_geometry_valid(geometry))
        return 0;

    unsigned count = rcs_wheel_count(geometry);

    scale = RCS_PI * geometry->wheel_diameter_m / (60.0f * geometry->gear_ratio);
    if (!isfinite(scale))
        return 0;

    for (unsigned i = 0; i < count; i++) {
        if (!isfinite(motor_rpm[i]))
            return 0;

        wheel_speed[i] = motor_rpm[i] * scale;

        if (!isfinite(wheel_speed[i]))
            return 0;
    }

    if (geometry->layout == RCS_MECANUM_X) {
        velocity[0] = (wheel_speed[0] + wheel_speed[1] + wheel_speed[2] + wheel_speed[3]) * 0.25f;
        velocity[1] = (-wheel_speed[0] + wheel_speed[1] + wheel_speed[2] - wheel_speed[3]) * 0.25f;
        velocity[2] = (-wheel_speed[0] + wheel_speed[1] - wheel_speed[2] + wheel_speed[3]) /
                      (2.0f * (geometry->wheelbase_m + geometry->track_m));
    } else {
        for (unsigned i = 0; i < count; i++) {
            float angle = geometry->omni_theta0_rad + 2.0f * RCS_PI * (float)i / (float)count;
            velocity[0] += -sinf(angle) * wheel_speed[i] * 2.0f / (float)count;
            velocity[1] += cosf(angle) * wheel_speed[i] * 2.0f / (float)count;
            velocity[2] += wheel_speed[i] / ((float)count * geometry->omni_radius_m);
        }
    }

    for (unsigned i = 0; i < 3; i++) {
        if (!isfinite(velocity[i])) {
            rcs_zero(velocity, 3);
            return 0;
        }
    }

    return 1;
}
