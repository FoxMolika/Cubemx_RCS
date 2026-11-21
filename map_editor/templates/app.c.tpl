/** @file app.c
 * @brief 队内任务式入口：main 只初始化一次并持续调用 App_Task。
 */
#include "app.h"
#if !RCS_NO_CHASSIS
/* 弱引用：工程未在 CubeMX 中启用 CAN 时该符号不存在，链接不应因此失败；
   此时句柄为 0，由 App_task_Init 以 RCS_APP_ERROR_CONFIGURATION 明确拒绝。
   工程启用 CAN 后，CubeMX 生成的强符号会覆盖本引用。 */
extern CAN_HandleTypeDef @APP_CAN_HANDLE@ __attribute__((weak));
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
