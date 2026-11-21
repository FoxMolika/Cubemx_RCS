/** @file app.c
 * @brief 队内任务式入口：main 只初始化一次并持续调用 App_Task。
 */
#include "app.h"
#if !RCS_NO_CHASSIS
extern CAN_HandleTypeDef hcan1;
#endif
int App_Init(void)
{
#if RCS_NO_CHASSIS
    return App_task_Init(0);
#else
    return App_task_Init(&hcan1);
#endif
}
void App_Task(void) { App_task(); }
int App_Start(void) { return App_task_Start(); }
void App_Stop(void) { App_task_Stop(); }

/* RCS GENERATED SHA256 6f091dd77f919fc9fa6b0df482acae34ec7c25143b510af1e6eadd999b5e3b9b */
