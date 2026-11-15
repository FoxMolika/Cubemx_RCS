/**
 * @file rcs_actions.c
 * @brief 航点机构动作：启动、轮询、完成与取消清理
 * @note 用户代码只写在 USER CODE 区域；所有回调必须快速返回，不能阻塞等待。
 */
#include "rcs_actions.h"

/* 用户驱动头文件与状态 ------------------------------------------------------------------------------*/

/* RCS REGION DEFAULT RCS_ACTION_INCLUDES c836ed0541a11ce54984c64aaea6351eeeb6a7489e9b17085d97f5f7a96675a4 */
/* USER CODE BEGIN RCS_ACTION_INCLUDES */
/* 预留用户驱动头文件，例如 #include "gripper.h" */
/* USER CODE END RCS_ACTION_INCLUDES */

/* RCS REGION DEFAULT RCS_ACTION_GLOBALS b010ec9149378da5e826cb3226f1c62aa94d5c715d8757fa04c7103d1fe90af5 */
/* USER CODE BEGIN RCS_ACTION_GLOBALS */
/* Shared state or your action_context type. */
/* USER CODE END RCS_ACTION_GLOBALS */

/* 动作配置 ------------------------------------------------------------------------------------------*/

/* Actions are described by rcs_route_plan; dispatch IDs identify custom actions. */

/* 航点私有动作 --------------------------------------------------------------------------------------*/

/* 导出函数 ------------------------------------------------------------------------------------------*/

/**
 * @brief 将导航事件分发到对应航点的动作函数
 * @param context 用户机构状态，可为空
 * @param point 全局自定义动作分发编号（不是航点下标）
 * @param phase 启动、轮询或取消事件
 * @param elapsed_s 当前动作已运行时间，秒
 * @return BUSY 继续等待，DONE 完成，ERROR 机构异常
 */
rcs_action_result rcs_route_action(void *context, size_t point,
                                   rcs_action_phase phase, float elapsed_s)
{
    (void)context;
    (void)phase;
    (void)elapsed_s;
    switch (point) {
    default:
        return RCS_ACTION_ERROR;
    }
}

/* RCS GENERATED SHA256 ae0d6a2f4f9ec61ed8212c37d3b883937d12accd983aefb16897cd321f803af4 */
