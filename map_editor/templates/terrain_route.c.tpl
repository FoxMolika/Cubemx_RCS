/* RCS TEMPLATE BEGIN TERRAIN_TABLES */
@TERRAIN_TABLES@
/* RCS TEMPLATE END TERRAIN_TABLES */

/* RCS TEMPLATE BEGIN ROUTE_BIND */
    if (!rcs_nav_bind_corrections(navigator, route_corrections, RCS_ROUTE_POINT_COUNT,
                                  rcs_route_observe, action_context)) {
        return 0;
    }
    return rcs_nav_bind_terrain(navigator, terrain_legs, RCS_ROUTE_POINT_COUNT,
                               &terrain_config, rcs_route_brake, action_context);
/* RCS TEMPLATE END ROUTE_BIND */
