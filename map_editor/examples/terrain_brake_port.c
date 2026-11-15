/**
 * @file terrain_brake_port.c
 * @brief 用已有电机速度闭环请求零速保持，不增加定位硬件，不绕过失联保护
 * @note 只有实测确认电机制动/回馈及供电能力后才能使用；断电不保证驻坡。
 */
#include "terrain_brake_port.h"
#include "rcs_hal.h"
/**
 * @brief 查询就绪或续期零速目标；RCS_FAULT时不能重新解锁
 * @note 持续调度RCS_Process；位置/电流/温度限制仍由实际底盘驱动保护。
 */
int FoxmolikaTerrainBrake(void *context, rcs_terrain_brake_request request)
{
    (void)context;
    if (RCS_State() != RCS_ARMED || RCS_Fault() != RCS_OK)
        return 0;
    if (request == RCS_TERRAIN_BRAKE_CHECK)
        return 1;
    if (request == RCS_TERRAIN_BRAKE_HOLD)
        return RCS_SetVelocity(0, 0, 0);
    return 0;
}
