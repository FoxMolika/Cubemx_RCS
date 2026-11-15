/* 编译期约束与配置初始化 --------------------------------------------------------------------------*/

#if (RCS_LAYOUT) < 0 || (RCS_LAYOUT) > 2
#error "RCS: RCS_LAYOUT must be 0 (Mecanum X), 1 (Omni3) or 2 (Omni4)"
#endif
#if (RCS_CAN_BUS) != 1 && (RCS_CAN_BUS) != 2
#error "RCS: RCS_CAN_BUS must be 1 or 2"
#endif
#if (RCS_MOTOR_ID_0) < 1 || (RCS_MOTOR_ID_0) > 4 || (RCS_MOTOR_ID_1) < 1 || (RCS_MOTOR_ID_1) > 4 ||          \
    (RCS_MOTOR_ID_2) < 1 || (RCS_MOTOR_ID_2) > 4 ||                                                          \
    ((RCS_LAYOUT) != 1 && ((RCS_MOTOR_ID_3) < 1 || (RCS_MOTOR_ID_3) > 4))
#error "RCS: motor IDs of used wheels must be 1..4"
#endif
#if (RCS_MOTOR_ID_0) == (RCS_MOTOR_ID_1) || (RCS_MOTOR_ID_0) == (RCS_MOTOR_ID_2) ||                          \
    (RCS_MOTOR_ID_1) == (RCS_MOTOR_ID_2)
#error "RCS: duplicate motor ID among W0/W1/W2"
#endif
#if (RCS_LAYOUT) != 1 && ((RCS_MOTOR_ID_3) == (RCS_MOTOR_ID_0) || (RCS_MOTOR_ID_3) == (RCS_MOTOR_ID_1) ||    \
                          (RCS_MOTOR_ID_3) == (RCS_MOTOR_ID_2))
#error "RCS: W3 motor ID duplicates another wheel"
#endif
#if ((RCS_DIRECTION_0) != 1 && (RCS_DIRECTION_0) != -1) ||                                                   \
    ((RCS_DIRECTION_1) != 1 && (RCS_DIRECTION_1) != -1) ||                                                   \
    ((RCS_DIRECTION_2) != 1 && (RCS_DIRECTION_2) != -1) ||                                                   \
    ((RCS_DIRECTION_3) != 1 && (RCS_DIRECTION_3) != -1)
#error "RCS: every RCS_DIRECTION_n must be +1 or -1"
#endif
#if (RCS_CURRENT_LIMIT_RAW) < 1 || (RCS_CURRENT_LIMIT_RAW) > 16384
#error "RCS: RCS_CURRENT_LIMIT_RAW must be 1..16384"
#endif
#if (RCS_PERIOD_MS) < 1 || (RCS_PERIOD_MS) > 100
#error "RCS: RCS_PERIOD_MS must be 1..100"
#endif
#if (RCS_COMMAND_TIMEOUT_MS) <= (RCS_PERIOD_MS) || (RCS_COMMAND_TIMEOUT_MS) > 60000
#error "RCS: RCS_COMMAND_TIMEOUT_MS must be > RCS_PERIOD_MS and <= 60000"
#endif
#if (RCS_FEEDBACK_TIMEOUT_MS) <= (RCS_PERIOD_MS) || (RCS_FEEDBACK_TIMEOUT_MS) > 60000
#error "RCS: RCS_FEEDBACK_TIMEOUT_MS must be > RCS_PERIOD_MS and <= 60000"
#endif

#include "rcs_control.h"
#define RCS_CONTROL_CONFIG                                                                                   \
    {                                                                                                        \
        {(rcs_layout)RCS_LAYOUT,                                                                             \
         RCS_WHEEL_DIAMETER_MM / 1000.0f,                                                                    \
         RCS_WHEELBASE_MM / 1000.0f,                                                                         \
         RCS_TRACK_MM / 1000.0f,                                                                             \
         RCS_OMNI_RADIUS_MM / 1000.0f,                                                                       \
         RCS_OMNI_THETA0_DEG * 0.0174532925199433f,                                                          \
         RCS_GEAR_RATIO,                                                                                     \
         RCS_MAX_MOTOR_RPM},                                                                                 \
            {RCS_MOTOR_ID_0, RCS_MOTOR_ID_1, RCS_MOTOR_ID_2, RCS_MOTOR_ID_3},                                \
            {RCS_DIRECTION_0, RCS_DIRECTION_1, RCS_DIRECTION_2, RCS_DIRECTION_3}, RCS_KP, RCS_KI, RCS_KD,    \
            RCS_CURRENT_LIMIT_RAW, RCS_PERIOD_MS, RCS_COMMAND_TIMEOUT_MS, RCS_FEEDBACK_TIMEOUT_MS            \
    }
