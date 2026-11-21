/** @file rcs_app_config.h
 * @brief 由“应用与状态机”填写的启动配置；重新导出会更新生成区。
 */
#pragma once
#define RCS_APP_ROUTE_ENABLED @APP_ENABLED@u
#define RCS_APP_AUTOSTART @APP_AUTOSTART@u
#define RCS_APP_START_AT_P0 @APP_START_AT_P0@u
#define RCS_APP_WAIT_FEEDBACK_MS @APP_BOOT_DELAY_MS@u
#define RCS_APP_PERIOD_MS @APP_PERIOD_MS@u
#define RCS_APP_CAN_BUS @APP_CAN_BUS@u
#define RCS_APP_MOTOR_IDS { @APP_MOTOR_IDS@ }
#define RCS_APP_START_X_M @APP_START_X@
#define RCS_APP_START_Y_M @APP_START_Y@
#define RCS_APP_START_YAW_RAD @APP_START_YAW@
#define RCS_APP_ARM_TIMEOUT_MS 200u
