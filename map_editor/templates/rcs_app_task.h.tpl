/** @file rcs_app_task.h
 * @brief 应用调度入口；主循环使用 app.h 的 App_Init/App_Task 即可。
 */
#pragma once
#include "rcs_hal.h"
#include "rcs_nav.h"
#ifdef __cplusplus
extern "C" {
#endif
#define RCS_APP_WHEELS @APP_WHEELS@u
#define RCS_APP_HAS_ACTIONS @APP_HAS_ACTIONS@u
#define RCS_APP_HAS_TERRAIN @APP_HAS_TERRAIN@u
#define RCS_APP_POINT_COUNT @APP_POINT_COUNT@u
#define RCS_APP_REVISION "@REVISION@"
typedef enum { RCS_APP_NOT_READY=0, RCS_APP_READY, RCS_APP_ARMING,
    RCS_APP_RUNNING, RCS_APP_FINISHED, RCS_APP_FAULT, RCS_APP_STOPPED } rcs_app_state;
typedef enum { RCS_APP_ERROR_NONE=0, RCS_APP_ERROR_CONFIGURATION,
    RCS_APP_ERROR_INITIALIZATION, RCS_APP_ERROR_FEEDBACK, RCS_APP_ERROR_CHASSIS,
    RCS_APP_ERROR_NAVIGATION, RCS_APP_ERROR_MACHINE } rcs_app_fault;
/* 只读调试快照：主循环每个周期刷新一次，Watch 里展开 g_dbg 即可看全。
 * 字段为 volatile，读取不依赖优化等级；写入不改变任何控制状态。 */
typedef struct {
    volatile uint8_t  app_state;
    volatile uint8_t  app_fault;
    volatile uint8_t  feedback_fault;
    volatile uint8_t  chassis_fault;
    volatile uint8_t  nav_fault;
    volatile uint8_t  machine_fault;
    volatile uint8_t  port_fault;
    volatile uint8_t  holding;
    volatile uint8_t  ready;
    volatile float    pose_x;
    volatile float    pose_y;
    volatile float    pose_yaw;
    volatile uint32_t fault_tick;
    volatile uint32_t feedback_sequence;
    volatile uint32_t rx_count;
    volatile uint32_t loop_ms;
    volatile uint32_t loop_max_ms;
    volatile uint32_t gap_us_last;
    volatile uint32_t gap_us_max;
    volatile uint8_t  dwt_ready;
    volatile int32_t  wheel_pending[4];
    volatile uint32_t wheel_age_ms[4];
} foxmolika_debug_t;
extern volatile foxmolika_debug_t g_dbg;
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
