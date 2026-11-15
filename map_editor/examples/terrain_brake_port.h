/**
 * @file terrain_brake_port.h
 * @brief 已有RCS速度环的制动接口示例，必须先实测电机与供电能力
 */
#pragma once
#include "rcs_terrain.h"
#ifdef __cplusplus
extern "C" {
#endif
/**
 * @brief 仅返回速度环是否接受请求，不证明实际车体已驻坡
 */
int FoxmolikaTerrainBrake(void *context, rcs_terrain_brake_request request);
#ifdef __cplusplus
}
#endif
