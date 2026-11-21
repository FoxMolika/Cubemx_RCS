/**
 * @file rcs_app_task.h
 * @brief 应用任务入口、状态机与工程结构常量
 *
 * main.c 只需要：初始化后 App_task_Init(&hcan1)，再把 App_task() 放进 while(1)。
 * 结构常量由生成器按地图与工程配置填写；策略在 rcs_app_task.c 的 USER CODE 区。
 */
#pragma once

#include "rcs_hal.h"
#include "rcs_nav.h"

#ifdef __cplusplus
extern "C" {
#endif

/* 工程结构：由生成器填写，勿手改。 */
#define RCS_APP_WHEELS      4u
#define RCS_APP_HAS_ACTIONS 0u
#define RCS_APP_HAS_TERRAIN 1u
#define RCS_APP_POINT_COUNT 2u
#define RCS_APP_REVISION    "9727618f04d0129407bc70353b4300db25991733ede05c573a8ca1c6d9e93780"

typedef enum {
    RCS_APP_NOT_READY = 0,
    RCS_APP_READY,
    RCS_APP_ARMING,
    RCS_APP_RUNNING,
    RCS_APP_FINISHED,
    RCS_APP_FAULT
} rcs_app_state;

/* 绑定 CAN 并初始化底盘、收帧、反馈与路线。返回 1 成功。 */
int App_task_Init(CAN_HandleTypeDef *can);

/* 本次真实起点（米、弧度）。要在实际摆放或定位之后调用。 */
void App_task_SetStartPose(float x_m, float y_m, float yaw_rad);

/* 启动：通过前置检查后进入 ARMING，下一周期转入 RUNNING。 */
int App_task_Start(void);

/* 主循环体：每轮调用一次，内部分频底盘周期与导航周期。 */
void App_task(void);

/* 停车并回到 READY；再启动前需要重新给位姿。 */
void App_task_Stop(void);

rcs_app_state App_task_State(void);
rcs_nav_fault App_task_NavFault(void);

/* 初始化完成且未进入故障。 */
int App_task_IsReady(void);

#ifdef __cplusplus
}
#endif

/* RCS GENERATED SHA256 fc1552b40e11f717f0312ef9b91d8b3ccf2c0b5ec6dbbcb73006675f6840dd2d */
