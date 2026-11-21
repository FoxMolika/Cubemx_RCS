/**
 * @file machism.h
 * @brief 用户状态机端口与单实例生命周期
 */
#pragma once
#include <stddef.h>
#ifdef __cplusplus
extern "C" {
#endif
#define MACHISM_HAS_ROUTE @MACHINE_HAS_ROUTE@
#define MACHISM_STATE_COUNT @MACHINE_STATE_COUNT@u
#define MACHISM_REVISION "@REVISION@"
typedef struct machism_ports {
    void *context;
    int (*route_start)(void *context);
    int (*route_tick)(void *context, float dt_s);
    void (*route_stop)(void *context);
} machism_ports;
enum { MACHISM_NOT_READY=0, MACHISM_READY=1, MACHISM_RUNNING=2,
       MACHISM_DONE=3, MACHISM_FAULT=4, MACHISM_STOPPED=5 };
enum { MACHISM_ERROR_NONE=0, MACHISM_ERROR_CONFIG=1, MACHISM_ERROR_TIMING=2,
       MACHISM_ERROR_TIMEOUT=3, MACHISM_ERROR_ROUTE_START=4, MACHISM_ERROR_ROUTE_TICK=5 };
int Machism_Init(const machism_ports *ports);
int Machism_Start(void);
void Machism_Task(float dt_s);
void Machism_Stop(void);
int Machism_State(void);
size_t Machism_ActiveState(void);
int Machism_Fault(void);
#ifdef __cplusplus
}
#endif
