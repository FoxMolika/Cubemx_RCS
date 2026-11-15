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

extern const rcs_action_spec rcs_route_actions[RCS_ROUTE_POINT_COUNT];

/* 导出函数 ------------------------------------------------------------------------------------------*/

/**
 * @brief 执行当前航点的机构动作回调
 * @param context 用户机构状态，可为空
 * @param point 当前航点下标
 * @param phase 启动、轮询或取消事件
 * @param elapsed_s 当前动作已运行时间，秒
 * @return BUSY 继续等待，DONE 完成，ERROR 机构异常
 */
rcs_action_result rcs_route_action(void *context, size_t point,
                                   rcs_action_phase phase, float elapsed_s);

#ifdef __cplusplus
}
#endif
