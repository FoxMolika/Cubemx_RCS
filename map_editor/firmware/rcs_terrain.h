/**
 * @file rcs_terrain.h
 * @brief 已知地图的直坡执行器：坡面行程、水平投影和前视限速
 * @note 使用米、弧度、秒。姿态过渡是标定模型，不是IMU测量，不修正真实打滑。
 */
#pragma once
#include <stddef.h>
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif
#define RCS_TERRAIN_ENABLED 1
typedef enum {
    RCS_TERRAIN_OK = 0,
    RCS_TERRAIN_CONFIG,
    RCS_TERRAIN_ENTRY,
    RCS_TERRAIN_TRACK,
    RCS_TERRAIN_BRAKE_UNAVAILABLE,
    RCS_TERRAIN_OVERSPEED
} rcs_terrain_fault;
typedef enum {
    RCS_TERRAIN_BRAKE_CHECK = 0,
    RCS_TERRAIN_BRAKE_HOLD = 1
} rcs_terrain_brake_request;
/* 返回1仅表示制动请求已由真实驱动接受，不证明车体已静止；返回0表示不可用。
 * 回调不得绕过底盘失联保护或阻塞；无法制动时不能把零电流称为驻坡。 */
typedef int (*rcs_terrain_brake_fn)(void *context, rcs_terrain_brake_request request);
/* 每个节点的s为累计坡面行程，h为累计水平行程；两者单调，不重复乘除cos。
 * pitch为标定过渡角；limit为坡面速度上限。z只表示地图规划高度。 */
typedef struct {
    float s_m, h_m, z_m, pitch_rad, limit_mps;
} rcs_terrain_node;
typedef struct {
    const rcs_terrain_node *nodes;
    size_t count;
    float x0_m, y0_m, bearing_rad;
    uint8_t inclined;
} rcs_terrain_leg;
typedef struct {
    float acceleration_mps2, deceleration_mps2, lateral_tolerance_m, heading_tolerance_rad;
    uint8_t brake_validated;
} rcs_terrain_config;
typedef struct {
    const rcs_terrain_leg *legs;
    size_t count, leg_index;
    rcs_terrain_config cfg;
    rcs_terrain_brake_fn brake;
    void *brake_context;
    float progress_m, horizontal_m, pitch_rad, z_plan_m;
    float last_dx_m, last_dy_m, speed_mps;
    rcs_terrain_fault fault;
    uint8_t initialized, active, hold_requested, hold_accepted;
} rcs_terrain;
/**
 * @brief 校验并绑定地形表，不启动电机
 */
int rcs_terrain_init(rcs_terrain *t, const rcs_terrain_leg *legs, size_t count,
                     const rcs_terrain_config *config, rcs_terrain_brake_fn brake, void *context);
/**
 * @brief 启动前核验用户声明及真实制动接口；没有IMU不影响基础模型
 */
int rcs_terrain_arm(rcs_terrain *t);
/**
 * @brief 按当前估计位置初始化新任务航段，不把坐标吸附到理想路径
 */
int rcs_terrain_begin(rcs_terrain *t, size_t leg, float x_m, float y_m);
/**
 * @brief 消费本周期编码器反解的世界方向速度，返回一次水平位移投影
 * @note vx/vy仍是车体平面上的速度旋转到地图方向，沿坡分量尚未投影。
 */
int rcs_terrain_integrate(rcs_terrain *t, float vx, float vy, float dt_s,
                          float *dx_m, float *dy_m);
/**
 * @brief 计算行驶阶段世界XY命令；坡上禁止横移和旋转，返回2表示已进入到达半径
 */
int rcs_terrain_command(rcs_terrain *t, float x_m, float y_m, float yaw_rad,
                        float arrival_radius_m, float requested_mps, float dt_s, float command[3]);
/**
 * @brief 请求真实驱动制动；仅记录请求是否接受，不宣称物理驻坡已确认
 */
int rcs_terrain_request_hold(rcs_terrain *t);
#ifdef __cplusplus
}
#endif
