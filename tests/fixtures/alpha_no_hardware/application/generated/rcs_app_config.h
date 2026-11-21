/** @file rcs_app_config.h
 * @brief 由“应用与状态机”填写的启动配置；重新导出会更新生成区。
 */
#pragma once
#define RCS_APP_ROUTE_ENABLED 1u
#define RCS_APP_AUTOSTART 1u
#define RCS_APP_START_AT_P0 1u
#define RCS_APP_WAIT_FEEDBACK_MS 2000u
#define RCS_APP_PERIOD_MS 10u
#define RCS_APP_CAN_BUS 1u
#define RCS_APP_MOTOR_IDS { 1, 2, 3, 4 }
#define RCS_APP_START_X_M 0.6f
#define RCS_APP_START_Y_M 0.6f
#define RCS_APP_START_YAW_RAD 0.0f
#define RCS_APP_ARM_TIMEOUT_MS 200u

/* RCS GENERATED SHA256 1d21d3431ee271a1ac777c72c0328088c9806dd235bfa0211888d3dd916d1537 */
