/** @file app.c
 * @brief 队内任务式入口：main 只初始化一次并持续调用 App_Task。
 */
#include "app.h"
#if !RCS_NO_CHASSIS
extern CAN_HandleTypeDef @APP_CAN_HANDLE@;
#endif
int App_Init(void)
{
#if RCS_NO_CHASSIS
    return App_task_Init(0);
#else
    return App_task_Init(&@APP_CAN_HANDLE@);
#endif
}
void App_Task(void) { App_task(); }
int App_Start(void) { return App_task_Start(); }
void App_Stop(void) { App_task_Stop(); }
