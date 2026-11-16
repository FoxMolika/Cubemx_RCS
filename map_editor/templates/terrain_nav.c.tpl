/* RCS TEMPLATE BEGIN NAV_FAIL */
        zeros(n->command_world);
        (void)rcs_terrain_request_hold(&n->terrain);
        cancel_action(n);
/* RCS TEMPLATE END NAV_FAIL */

/* RCS TEMPLATE BEGIN NAV_RESET */
    n->observation_seen = 0;
    n->feedback_seen = 0;
    n->terrain.active = 0;
    n->terrain.fault = RCS_TERRAIN_OK;
    n->state = RCS_NAV_IDLE;
/* RCS TEMPLATE END NAV_RESET */

/* RCS TEMPLATE BEGIN NAV_START */
    if (!rcs_terrain_arm(&n->terrain))
        return fail(n, RCS_NAV_TERRAIN);
    n->state = RCS_NAV_RUNNING;
/* RCS TEMPLATE END NAV_START */

/* RCS TEMPLATE BEGIN NAV_STOP */
    cancel_action(n);
    (void)rcs_terrain_request_hold(&n->terrain);
    zeros(n->command_world);
/* RCS TEMPLATE END NAV_STOP */

/* RCS TEMPLATE BEGIN NAV_MOVE */
        if (n->target > 0) {
            int status = rcs_terrain_command(&n->terrain, n->x_m, n->y_m, n->yaw_rad,
                                              p.arrival_radius_m, point->speed_mps, dt,
                                              n->command_world);
            if (!status)
                return fail(n, RCS_NAV_TERRAIN);
            if (status == 2)
                stage(n, RCS_STAGE_SETTLE);
        } else if (dist > p.arrival_radius_m)
            translate(n, dx, dy, point->speed_mps, dt);
/* RCS TEMPLATE END NAV_MOVE */

/* RCS TEMPLATE BEGIN NAV_INTEGRATE */
    if (n->state == RCS_NAV_RUNNING && n->stage == RCS_STAGE_MOVE && n->target > 0) {
        if (!n->terrain.active || n->terrain.leg_index != n->target) {
            if (!rcs_terrain_begin(&n->terrain, n->target, n->x_m, n->y_m))
                return fail(n, RCS_NAV_TERRAIN);
        }
        float world_vx = co * vel[0] - si * vel[1];
        float world_vy = si * vel[0] + co * vel[1];
        float dx, dy;
        if (!rcs_terrain_integrate(&n->terrain, world_vx, world_vy, dt, &dx, &dy))
            return fail(n, RCS_NAV_TERRAIN);
        n->x_m += dx;
        n->y_m += dy;
    } else {
        n->x_m += co * bx - si * by;
        n->y_m += si * bx + co * by;
    }
/* RCS TEMPLATE END NAV_INTEGRATE */

/* RCS TEMPLATE BEGIN NAV_BIND */

/**
 * @brief 绑定逐航点地形数据，不启动电机
 */
int rcs_nav_bind_terrain(rcs_navigator *n, const rcs_terrain_leg *legs, size_t count,
                          const rcs_terrain_config *config, rcs_terrain_brake_fn brake, void *context)
{
    if (!n || !n->initialized || n->state != RCS_NAV_IDLE || count != n->count)
        return 0;
    if (!rcs_terrain_init(&n->terrain, legs, count, config, brake, context))
        return fail(n, RCS_NAV_TERRAIN);
    return 1;
}
/* RCS TEMPLATE END NAV_BIND */
