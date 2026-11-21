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
/**
 * @brief 请求真实制动能力核验或保持，返回1仅表示驱动接受请求
 */
int rcs_route_brake(void *context, rcs_terrain_brake_request request);
#ifdef __cplusplus
}
#endif

/* RCS GENERATED SHA256 fcc89cc19b93c45ef2be50856a1a6d0704a1128c88e9ef92ba26a9f243e64a78 */
