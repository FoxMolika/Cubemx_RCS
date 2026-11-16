/* RCS TEMPLATE BEGIN BRAKE_FUNCTION */

@BRAKE_HEADER@/**
 * @brief 地形制动适配入口：默认未接入，禁止把零电流伪装成驻坡
 */
int rcs_route_brake(void *context, rcs_terrain_brake_request request)
{
    (void)context;
    (void)request;
@BRAKE_REGION@@BRAKE_RETURN@}
/* RCS TEMPLATE END BRAKE_FUNCTION */
