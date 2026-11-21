/** @file rcs_app_task.h
 * @brief 应用调度入口；主循环使用 app.h 的 App_Init/App_Task 即可。
 */
#pragma once
#include "rcs_hal.h"
#include "rcs_nav.h"
#ifdef __cplusplus
extern "C" {
#endif
#define RCS_APP_WHEELS 4u
#define RCS_APP_HAS_ACTIONS 0u
#define RCS_APP_HAS_TERRAIN 1u
#define RCS_APP_POINT_COUNT 2u
#define RCS_APP_REVISION "6afd49705abb6fbb352281955bd5c735b646cfc0c5298e388aab748847ae2c76"
typedef enum { RCS_APP_NOT_READY=0, RCS_APP_READY, RCS_APP_ARMING,
    RCS_APP_RUNNING, RCS_APP_FINISHED, RCS_APP_FAULT, RCS_APP_STOPPED } rcs_app_state;
typedef enum { RCS_APP_ERROR_NONE=0, RCS_APP_ERROR_CONFIGURATION,
    RCS_APP_ERROR_INITIALIZATION, RCS_APP_ERROR_FEEDBACK, RCS_APP_ERROR_CHASSIS,
    RCS_APP_ERROR_NAVIGATION, RCS_APP_ERROR_MACHINE } rcs_app_fault;
int App_task_Init(CAN_HandleTypeDef *can);
void App_task_SetStartPose(float x_m, float y_m, float yaw_rad);
int App_task_Start(void);
void App_task(void);
/* 停止后不自动重启；重新确认摆放后显式 Init + Start。 */
void App_task_Stop(void);
rcs_app_state App_task_State(void);
rcs_nav_fault App_task_NavFault(void);
rcs_app_fault App_task_Fault(void);
rcs_fault App_task_ChassisFault(void);
int App_task_MachineFault(void);
int App_task_FeedbackFault(void);
uint32_t App_task_FaultTick(void);
const rcs_navigator *App_task_Navigator(void);
int App_task_Holding(void);
int App_task_IsReady(void);
#ifdef __cplusplus
}
#endif

/* RCS GENERATED SHA256 2446a383f047b99313a263727c41ebdb267b09b60da33e02ab6c3b4012850266 */
