#pragma once
/* 在既有收帧桩之上补 DWT，用来验证微秒窗口；主机测试默认没有 DWT。 */
#include "../beta_feedback_hal/rcs_hal.h"

typedef struct { volatile uint32_t CYCCNT; volatile uint32_t CTRL; } DWT_Type;
typedef struct { volatile uint32_t DEMCR; } CoreDebug_Type;

extern DWT_Type test_dwt;
extern CoreDebug_Type test_coredebug;
extern uint32_t SystemCoreClock;

#define DWT (&test_dwt)
#define CoreDebug (&test_coredebug)
#define DWT_CTRL_CYCCNTENA_Msk 1u
#define CoreDebug_DEMCR_TRCENA_Msk 1u
