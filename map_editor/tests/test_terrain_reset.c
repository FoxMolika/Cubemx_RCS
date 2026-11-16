/* 真实导出文件回归：故障仍需显式复位、新鲜反馈和人工启动，不能自动恢复。 */
#include "rcs_route.h"
#include <stdio.h>
#include <stdlib.h>
#define CHECK(x) do { if (!(x)) { fprintf(stderr, "reset FAIL %d: %s\n", __LINE__, #x); exit(1); } } while (0)
int test_terrain_brake(void *context, rcs_terrain_brake_request request)
{
    (void)context;
    (void)request;
    return 1; /* 测试端口；不模拟真实驻坡能力。 */
}
int main(void)
{
    rcs_navigator n;
    int32_t zero[4] = {0};
    float command[3];
    CHECK(rcs_route_init(&n, NULL));
    CHECK(rcs_nav_reset(&n, rcs_route_points[0].x_m, rcs_route_points[0].y_m, 0));
    CHECK(rcs_nav_update(&n, zero, 15, .01f, command));
    CHECK(rcs_nav_start(&n));
    CHECK(rcs_terrain_begin(&n.terrain, 1, n.x_m, n.y_m));
    CHECK(!rcs_terrain_command(&n.terrain, n.x_m, n.y_m + 1, 0, .04f, .1f, .01f, command));
    CHECK(n.terrain.fault == RCS_TERRAIN_TRACK);
    n.target = 1;
    CHECK(!rcs_nav_update(&n, zero, 15, .01f, command));
    CHECK(n.fault == RCS_NAV_TERRAIN);
    CHECK(!rcs_nav_start(&n));
    CHECK(rcs_nav_reset(&n, rcs_route_points[0].x_m, rcs_route_points[0].y_m, 0));
#ifdef EXPECT_LEGACY_BUG
    CHECK(n.terrain.fault == RCS_TERRAIN_TRACK && n.terrain.active);
    CHECK(rcs_nav_update(&n, zero, 15, .01f, command));
    CHECK(!rcs_nav_start(&n));
    puts("PASS reproduced baseline bug: explicit reset retains terrain fault and blocks restart");
#else
    CHECK(n.terrain.fault == RCS_TERRAIN_OK && !n.terrain.active);
    CHECK(n.state == RCS_NAV_IDLE && !n.feedback_seen);
    CHECK(!rcs_nav_start(&n));
    CHECK(rcs_nav_update(&n, zero, 15, .01f, command));
    CHECK(rcs_nav_start(&n));
    puts("PASS terrain recovery: explicit reset clears old leg/fault, fresh feedback and manual start required");
#endif
    return 0;
}
