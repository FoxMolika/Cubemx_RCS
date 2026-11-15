/**
 * @file rcs_actions.c
 * @brief 航点机构动作：启动、轮询、完成与取消清理
 * @note 用户代码只写在 USER CODE 区域；所有回调必须快速返回，不能阻塞等待。
 */
#include "rcs_actions.h"

/* 用户驱动头文件与状态 ------------------------------------------------------------------------------*/

@INCLUDES_REGION@
@GLOBALS_REGION@
/* 动作配置 ------------------------------------------------------------------------------------------*/

/* 动作配置由rcs_route_plan描述；分发编号标识自定义机构动作。 */

/* 航点私有动作 --------------------------------------------------------------------------------------*/
@FUNCTIONS@
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
@CASES@    default:
        return RCS_ACTION_ERROR;
    }
}

@OBSERVER_SOURCE@
