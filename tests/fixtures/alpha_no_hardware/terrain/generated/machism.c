/**
 * @file machism.c
 * @brief 按稳定状态ID生成的状态机 每周期最多一次转移
 * @note 状态转移按界面列表优先级；不假定任意C表达式互斥。用户回调必须非阻塞。
 */
#include "machism.h"
#include <math.h>
#include <string.h>
/* RCS REGION DEFAULT MACHINE_INCLUDES c2a764457246a070186a986a847753752ba946e9305e763e5d621c56c7da75c0 */
/* USER CODE BEGIN MACHINE_INCLUDES */
/* 用户状态机需要的头文件。 */
/* USER CODE END MACHINE_INCLUDES */

typedef void (*machine_enter)(void *context);
typedef void (*machine_tick)(void *context, float elapsed_s, float dt_s);
typedef int (*machine_complete)(void *context, float elapsed_s);
typedef int (*machine_condition)(void *context, float elapsed_s, int complete);
typedef struct {
    int kind;
    float timeout_s;
    float wait_s;
    machine_enter enter;
    machine_tick tick;
    machine_complete complete;
    machine_enter exit;
    machine_enter cancel;
} machine_spec;
typedef struct { size_t from, to; machine_condition condition; } machine_transition;
static void machine_00000000000000000000000000000001_enter(void *context)
{
    (void)context;
/* RCS REGION DEFAULT STATE_00000000000000000000000000000001_ENTER 0863a11fa66bf754f919f015072b9d89072be77c30623b1a995eb523de28ab34 */
/* USER CODE BEGIN STATE_00000000000000000000000000000001_ENTER */
/* 用户代码；必须非阻塞。 */
/* USER CODE END STATE_00000000000000000000000000000001_ENTER */
}
static void machine_00000000000000000000000000000001_exit(void *context)
{
    (void)context;
/* RCS REGION DEFAULT STATE_00000000000000000000000000000001_EXIT 0863a11fa66bf754f919f015072b9d89072be77c30623b1a995eb523de28ab34 */
/* USER CODE BEGIN STATE_00000000000000000000000000000001_EXIT */
/* 用户代码；必须非阻塞。 */
/* USER CODE END STATE_00000000000000000000000000000001_EXIT */
}
static void machine_00000000000000000000000000000001_cancel(void *context)
{
    (void)context;
/* RCS REGION DEFAULT STATE_00000000000000000000000000000001_CANCEL 0863a11fa66bf754f919f015072b9d89072be77c30623b1a995eb523de28ab34 */
/* USER CODE BEGIN STATE_00000000000000000000000000000001_CANCEL */
/* 用户代码；必须非阻塞。 */
/* USER CODE END STATE_00000000000000000000000000000001_CANCEL */
}
static void machine_00000000000000000000000000000001_tick(void *context, float elapsed_s, float dt_s)
{
    (void)context;
    (void)elapsed_s;
    (void)dt_s;
/* RCS REGION DEFAULT STATE_00000000000000000000000000000001_TICK 0863a11fa66bf754f919f015072b9d89072be77c30623b1a995eb523de28ab34 */
/* USER CODE BEGIN STATE_00000000000000000000000000000001_TICK */
/* 用户代码；必须非阻塞。 */
/* USER CODE END STATE_00000000000000000000000000000001_TICK */
}
static int machine_00000000000000000000000000000001_complete(void *context, float elapsed_s)
{
    (void)context;
    (void)elapsed_s;
/* RCS REGION DEFAULT STATE_00000000000000000000000000000001_COMPLETE 2d88854a9c20a5962cf07f585e9c1d5b069c16efee38e806624d45e7eb443c2a */
/* USER CODE BEGIN STATE_00000000000000000000000000000001_COMPLETE */
    return (1) ? 1 : 0;
/* USER CODE END STATE_00000000000000000000000000000001_COMPLETE */
}
static int transition_00000000000000000000000000000003(void *context, float elapsed_s, int complete)
{
    (void)context;
    (void)elapsed_s;
    (void)complete;
/* RCS REGION DEFAULT TRANSITION_00000000000000000000000000000003_CONDITION 7ad565af87f7d954a445b712c7715b9a15c6a5011ea239b9a89dbaf684c41bdd */
/* USER CODE BEGIN TRANSITION_00000000000000000000000000000003_CONDITION */
    return (complete) ? 1 : 0;
/* USER CODE END TRANSITION_00000000000000000000000000000003_CONDITION */
}
static void machine_00000000000000000000000000000002_enter(void *context)
{
    (void)context;
/* RCS REGION DEFAULT STATE_00000000000000000000000000000002_ENTER 0863a11fa66bf754f919f015072b9d89072be77c30623b1a995eb523de28ab34 */
/* USER CODE BEGIN STATE_00000000000000000000000000000002_ENTER */
/* 用户代码；必须非阻塞。 */
/* USER CODE END STATE_00000000000000000000000000000002_ENTER */
}
static void machine_00000000000000000000000000000002_exit(void *context)
{
    (void)context;
/* RCS REGION DEFAULT STATE_00000000000000000000000000000002_EXIT 0863a11fa66bf754f919f015072b9d89072be77c30623b1a995eb523de28ab34 */
/* USER CODE BEGIN STATE_00000000000000000000000000000002_EXIT */
/* 用户代码；必须非阻塞。 */
/* USER CODE END STATE_00000000000000000000000000000002_EXIT */
}
static void machine_00000000000000000000000000000002_cancel(void *context)
{
    (void)context;
/* RCS REGION DEFAULT STATE_00000000000000000000000000000002_CANCEL 0863a11fa66bf754f919f015072b9d89072be77c30623b1a995eb523de28ab34 */
/* USER CODE BEGIN STATE_00000000000000000000000000000002_CANCEL */
/* 用户代码；必须非阻塞。 */
/* USER CODE END STATE_00000000000000000000000000000002_CANCEL */
}
static void machine_00000000000000000000000000000002_tick(void *context, float elapsed_s, float dt_s)
{
    (void)context;
    (void)elapsed_s;
    (void)dt_s;
/* RCS REGION DEFAULT STATE_00000000000000000000000000000002_TICK 0863a11fa66bf754f919f015072b9d89072be77c30623b1a995eb523de28ab34 */
/* USER CODE BEGIN STATE_00000000000000000000000000000002_TICK */
/* 用户代码；必须非阻塞。 */
/* USER CODE END STATE_00000000000000000000000000000002_TICK */
}
static int machine_00000000000000000000000000000002_complete(void *context, float elapsed_s)
{
    (void)context;
    (void)elapsed_s;
/* RCS REGION DEFAULT STATE_00000000000000000000000000000002_COMPLETE 2d88854a9c20a5962cf07f585e9c1d5b069c16efee38e806624d45e7eb443c2a */
/* USER CODE BEGIN STATE_00000000000000000000000000000002_COMPLETE */
    return (1) ? 1 : 0;
/* USER CODE END STATE_00000000000000000000000000000002_COMPLETE */
}

static const machine_spec machine_states[MACHISM_STATE_COUNT] = {
    { 1, 3600.0f, 1.0f,
        machine_00000000000000000000000000000001_enter,
        machine_00000000000000000000000000000001_tick, machine_00000000000000000000000000000001_complete,
        machine_00000000000000000000000000000001_exit, machine_00000000000000000000000000000001_cancel },
    { 3, 120.0f, 1.0f,
        machine_00000000000000000000000000000002_enter,
        machine_00000000000000000000000000000002_tick, machine_00000000000000000000000000000002_complete,
        machine_00000000000000000000000000000002_exit, machine_00000000000000000000000000000002_cancel },
};
static const machine_transition machine_transitions[1u] = {
    { 0u, 1u, transition_00000000000000000000000000000003 },
};
static machism_ports g_ports;
static int g_lifecycle, g_fault;
static size_t g_active;
static float g_elapsed;
static int g_enter_pending, g_route_active, g_route_done, g_updating;

static void stop_route(void)
{
    int active = g_route_active;
    g_route_active = 0;
    if (active && g_ports.route_stop != NULL)
        g_ports.route_stop(g_ports.context);
}

static void fail_machine(int reason)
{
    int cancel = g_lifecycle == MACHISM_RUNNING && !g_enter_pending;
    g_fault = reason;
    g_lifecycle = MACHISM_FAULT;
    stop_route();
    if (cancel && g_active < MACHISM_STATE_COUNT)
        machine_states[g_active].cancel(g_ports.context);
}

int Machism_Init(const machism_ports *ports)
{
    /* 回调进行中不能替换上下文或清空当前状态；Stop仍可立即取消。 */
    if (g_updating)
        return 0;
    if (g_lifecycle == MACHISM_RUNNING)
        Machism_Stop();
    memset(&g_ports, 0, sizeof(g_ports));
    if (ports != NULL)
        g_ports = *ports;
#if MACHISM_HAS_ROUTE
    if (g_ports.route_start == NULL || g_ports.route_tick == NULL || g_ports.route_stop == NULL) {
        g_fault = MACHISM_ERROR_CONFIG;
        g_lifecycle = MACHISM_FAULT;
        return 0;
    }
#endif
    g_fault = MACHISM_ERROR_NONE;
    g_active = (size_t)0u;
    g_enter_pending = g_route_active = g_route_done = 0;
    g_elapsed = 0.0f;
    g_lifecycle = MACHISM_READY;
    return 1;
}

int Machism_Start(void)
{
    if (g_updating || (g_lifecycle != MACHISM_READY && g_lifecycle != MACHISM_STOPPED))
        return 0;
    g_active = (size_t)0u;
    g_elapsed = 0.0f;
    g_enter_pending = 1;
    g_route_active = g_route_done = 0;
    g_fault = MACHISM_ERROR_NONE;
    g_lifecycle = MACHISM_RUNNING;
    return 1;
}

static void machine_step(float dt_s)
{
    const machine_spec *state;
    size_t i;
    int complete = 0;
    if (g_lifecycle != MACHISM_RUNNING)
        return;
    if (!isfinite(dt_s) || dt_s <= 0.0f || dt_s > 1.0f) {
        fail_machine(MACHISM_ERROR_TIMING);
        return;
    }
    state = &machine_states[g_active];
    if (g_enter_pending) {
        g_enter_pending = 0;
        state->enter(g_ports.context);
        if (g_lifecycle != MACHISM_RUNNING)
            return;
        if (state->kind == 1) {
            int started;
            g_route_active = 1;
            started = g_ports.route_start(g_ports.context);
            if (g_lifecycle != MACHISM_RUNNING)
                return;
            if (!started) {
                fail_machine(MACHISM_ERROR_ROUTE_START);
                return;
            }
        }
    }
    g_elapsed += dt_s;
    if (g_elapsed >= state->timeout_s) {
        fail_machine(MACHISM_ERROR_TIMEOUT);
        return;
    }
    state->tick(g_ports.context, g_elapsed, dt_s);
    if (g_lifecycle != MACHISM_RUNNING)
        return;
    if (state->kind == 1) {
        if (!g_route_done) {
            int result = g_ports.route_tick(g_ports.context, dt_s);
            if (g_lifecycle != MACHISM_RUNNING)
                return;
            if (result < 0) {
                fail_machine(MACHISM_ERROR_ROUTE_TICK);
                return;
            }
            g_route_done = result > 0;
        }
        complete = g_route_done;
    } else if (state->kind == 2) {
        complete = g_elapsed >= state->wait_s;
    } else if (state->kind == 3) {
        state->exit(g_ports.context);
        if (g_lifecycle != MACHISM_RUNNING)
            return;
        stop_route();
        if (g_lifecycle != MACHISM_RUNNING)
            return;
        g_lifecycle = MACHISM_DONE;
        return;
    } else {
        complete = state->complete(g_ports.context, g_elapsed);
        if (g_lifecycle != MACHISM_RUNNING)
            return;
    }
    /* 第一条为真的转移胜出；目标状态的enter留到下一周期，防止同周期链式重入。 */
    for (i = 0; i < (size_t)1u; ++i) {
        const machine_transition *edge = &machine_transitions[i];
        int matched;
        if (edge->condition == NULL || edge->from != g_active)
            continue;
        matched = edge->condition(g_ports.context, g_elapsed, complete);
        if (g_lifecycle != MACHISM_RUNNING)
            return;
        if (matched) {
            state->exit(g_ports.context);
            if (g_lifecycle != MACHISM_RUNNING)
                return;
            stop_route();
            if (g_lifecycle != MACHISM_RUNNING)
                return;
            g_active = edge->to;
            g_elapsed = 0.0f;
            g_enter_pending = 1;
            g_route_done = 0;
            return;
        }
    }
}

void Machism_Task(float dt_s)
{
    /* 用户回调不能递归推进同一状态机。 */
    if (g_updating)
        return;
    g_updating = 1;
    machine_step(dt_s);
    g_updating = 0;
}

void Machism_Stop(void)
{
    int was_updating;
    if (g_lifecycle == MACHISM_NOT_READY || g_lifecycle == MACHISM_FAULT)
        return;
    if (g_lifecycle != MACHISM_RUNNING) {
        g_lifecycle = MACHISM_STOPPED;
        return;
    }
    g_lifecycle = MACHISM_STOPPED;
    was_updating = g_updating;
    g_updating = 1;
    stop_route();
    if (!g_enter_pending)
        machine_states[g_active].cancel(g_ports.context);
    g_updating = was_updating;
}
int Machism_State(void) { return g_lifecycle; }
size_t Machism_ActiveState(void) { return g_active; }
int Machism_Fault(void) { return g_fault; }

/* RCS GENERATED SHA256 cfd52f3c1e176299c4dd918a395d1cc54a6e2acabe2414e5c6c3f94ae8c4bc38 */
