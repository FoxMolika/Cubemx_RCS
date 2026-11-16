/* RCS TEMPLATE BEGIN NAV_INCLUDE */
#include "rcs_chassis.h"
#include "rcs_terrain.h"
/* RCS TEMPLATE END NAV_INCLUDE */

/* RCS TEMPLATE BEGIN NAV_FAULT */
    RCS_NAV_OBSERVATION_JUMP,
    RCS_NAV_TERRAIN
/* RCS TEMPLATE END NAV_FAULT */

/* RCS TEMPLATE BEGIN NAV_CONTEXT */
    rcs_terrain terrain;
    float correction_origin_x, correction_origin_y;
/* RCS TEMPLATE END NAV_CONTEXT */

/* RCS TEMPLATE BEGIN NAV_DECLARE */
/**
 * @brief 绑定坡道执行表及真实制动驱动，只允许空闲状态调用
 */
int rcs_nav_bind_terrain(rcs_navigator *n, const rcs_terrain_leg *legs, size_t count,
                          const rcs_terrain_config *config, rcs_terrain_brake_fn brake, void *context);
/* RCS TEMPLATE END NAV_DECLARE */
