/** @file app.h
 * @brief 固定 main 模板入口；业务状态与动作由生成的 machism/route 管理。
 */
#pragma once
#include "rcs_app_task.h"
#ifdef __cplusplus
extern "C" {
#endif
int App_Init(void);
void App_Task(void);
int App_Start(void);
void App_Stop(void);
#ifdef __cplusplus
}
#endif

/* RCS GENERATED SHA256 add3009e6c63c3c19cb00218af50940d6fe3b2a90c8beb42b4b31b39854fe268 */
