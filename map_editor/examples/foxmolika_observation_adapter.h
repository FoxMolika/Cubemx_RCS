/**
 * @file foxmolika_observation_adapter.h
 * @brief 独立定位接入示例；不是某型号传感器驱动
 */
#pragma once
#include "rcs_nav.h"
#ifdef __cplusplus
extern "C" {
#endif
/**
 * @brief 返回车体中心地图坐标观测，1有效、0失败
 */
int TeamGps_ReadObservation(rcs_observation *sample);
#ifdef __cplusplus
}
#endif
