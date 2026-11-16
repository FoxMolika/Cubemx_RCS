/**
 * @file rcs_actions.h
 * @brief 航点动作配置与回调接口
 */
#pragma once

#include "rcs_route.h"

#ifdef __cplusplus
extern "C" {
#endif

/* 导出配置 ------------------------------------------------------------------------------------------*/

/* 动作类型和顺序由rcs_route_plan描述。 */

/* 导出函数 ------------------------------------------------------------------------------------------*/

/**
 * @brief 执行当前航点的机构动作回调
 * @param context 用户机构状态，可为空
 * @param point 全局自定义动作分发编号（不是航点下标）
 * @param phase 启动、轮询或取消事件
 * @param elapsed_s 当前动作已运行时间，秒
 * @return BUSY 继续等待，DONE 完成，ERROR 机构异常
 */
rcs_action_result rcs_route_action(void *context, size_t point,
                                   rcs_action_phase phase, float elapsed_s);

/**
 * @brief 读取已导入硬件的独立定位，返回1有效、0失败
 */
int rcs_route_observe(void *context, unsigned source, rcs_observation *sample);
/* RCS TEMPLATE BEGIN BRAKE_DECLARE */
/* RCS TEMPLATE END BRAKE_DECLARE */
#ifdef __cplusplus
}
#endif
