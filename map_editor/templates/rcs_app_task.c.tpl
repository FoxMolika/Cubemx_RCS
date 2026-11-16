/**
 * @file rcs_app_task.c
 * @brief 应用任务模板：周期分频、看门狗、状态机骨架与用户钩子
 * @note 结构由生成器填写；策略在 USER CODE 区改写，重新导出时按区域哈希保留。
 */
#include "rcs_app_task.h"

#include "rcs_app_config.h"
#include "rcs_can_port.h"
#include "rcs_config.h"
#include "rcs_feedback_port.h"
#include "rcs_hal.h"
#include "rcs_route.h"

#if (RCS_APP_PERIOD_MS) < 1u || (RCS_APP_PERIOD_MS) > 100u
#error "RCS: RCS_APP_PERIOD_MS must be 1..100"
#endif
#if (RCS_APP_PERIOD_MS) < (RCS_PERIOD_MS)
#error "RCS: RCS_APP_PERIOD_MS must not be smaller than RCS_PERIOD_MS"
#endif

static rcs_navigator g_nav;
static rcs_app_state g_state = RCS_APP_NOT_READY;
static CAN_HandleTypeDef *g_can;
static uint32_t g_chassis_tick;
static uint32_t g_app_tick;
static uint32_t g_arm_ms;
static uint8_t g_pose_valid;
static float g_pose_x, g_pose_y, g_pose_yaw;
#if RCS_APP_AUTOSTART
static uint32_t g_auto_tick;
#endif

/* 生成区提供的工具函数，用户钩子可以直接调用。 */
static void app_set_state(rcs_app_state state);
static void app_fault(void);

/* 用户区：机构上下文 ----------------------------------------------------------------*/
/* USER CODE BEGIN APP_CONTEXT */
/* 有机构动作时返回你的 action_context（机构状态指针）；没有就保持返回 0。
 * 本函数的返回值会直接传给 rcs_route_init()。 */
static void *app_context(void)
{
    return 0;
}
/* USER CODE END APP_CONTEXT */

/* 用户区：起点 ----------------------------------------------------------------------*/
/* USER CODE BEGIN APP_START_POSE */
/* 返回 1 表示已拿到本次真实起点（米、弧度），返回 0 表示还不能启动。
 * 位姿来自你的定位/人工输入；不要用地图第一个航点猜。 */
static int app_start_pose(float *x_m, float *y_m, float *yaw_rad)
{
    (void)x_m;
    (void)y_m;
    (void)yaw_rad;
    return 0;
}
/* USER CODE END APP_START_POSE */

/* 用户区：READY 前置条件 ------------------------------------------------------------*/
/* USER CODE BEGIN APP_READY_STEP */
/* 返回 1 才允许启动。默认要求路线开关打开，且有机构动作时必须给出上下文。 */
static int app_ready_ok(const rcs_feedback_sample *sample)
{
    if (!RCS_APP_ROUTE_ENABLED)
        return 0;
    if (sample == 0)
        return 0;
#if RCS_APP_HAS_ACTIONS
    if (app_context() == 0)
        return 0;
#endif
    return 1;
}
/* USER CODE END APP_READY_STEP */

/* 用户区：ARMING --------------------------------------------------------------------*/
/* USER CODE BEGIN APP_ARM_STEP */
/* reset 之后必须再吃进一帧真实反馈，rcs_nav_start 才会接受。
 * 返回 1 表示已转入运行。 */
static int app_arm_step(float dt_s)
{
    rcs_feedback_sample sample;
    float command[3] = {0.0f, 0.0f, 0.0f};

    g_arm_ms += (uint32_t)(dt_s * 1000.0f);
    if (!rcs_feedback_port_read(&sample)) {
        if (g_arm_ms >= (uint32_t)RCS_APP_ARM_TIMEOUT_MS)
            app_fault();
        return 0;
    }
    if (!rcs_nav_update(&g_nav, sample.delta, sample.fresh_mask, dt_s, command)) {
        app_fault();
        return 0;
    }
    if (!rcs_nav_start(&g_nav)) {
        app_fault();
        return 0;
    }
    return 1;
}
/* USER CODE END APP_ARM_STEP */

/* 用户区：运行周期 ------------------------------------------------------------------*/
/* USER CODE BEGIN APP_RUN_STEP */
/* 默认：读反馈 -> 推进导航 -> 下发速度；任一步失败立即停车。
 * 需要限速、软起停、状态上报时在这里加，不要改成阻塞等待。 */
static void app_run_step(float dt_s)
{
    rcs_feedback_sample sample;
    float command[3] = {0.0f, 0.0f, 0.0f};

    if (!rcs_feedback_port_read(&sample) ||
        !rcs_nav_update(&g_nav, sample.delta, sample.fresh_mask, dt_s, command)) {
        app_fault();
        return;
    }
    if (g_nav.state == RCS_NAV_FAULT) {
        app_fault();
        return;
    }
    if (g_nav.state == RCS_NAV_DONE) {
        rcs_nav_stop(&g_nav);
        RCS_Lock();
        app_set_state(RCS_APP_FINISHED);
        return;
    }
    if (!RCS_SetVelocity(command[0], command[1], command[2]))
        app_fault();
}
/* USER CODE END APP_RUN_STEP */

/* 用户区：故障处理 ------------------------------------------------------------------*/
/* USER CODE BEGIN APP_FAULT_ENTER */
/* 默认停车并锁定。带坡道的工程可以在这里改成请求驻坡保持。 */
static void app_on_fault(void)
{
    rcs_nav_stop(&g_nav);
#if RCS_APP_HAS_TERRAIN
    if (rcs_terrain_request_hold(&g_nav.terrain))
        return;
#endif
    RCS_Lock();
}
/* USER CODE END APP_FAULT_ENTER */

/* 用户区：状态上报 ------------------------------------------------------------------*/
/* USER CODE BEGIN APP_ON_STATE */
/* 每次状态变化调用一次：灯、蜂鸣、上位机都放这里，禁止阻塞。 */
static void app_on_state(rcs_app_state state)
{
    (void)state;
}
/* USER CODE END APP_ON_STATE */

/* 用户区：每周期附加工作 ------------------------------------------------------------*/
/* USER CODE BEGIN APP_TICK */
/* 按键扫描、屏幕、日志放这里；每个应用周期调用一次，必须非阻塞。 */
static void app_tick(void)
{
}
/* USER CODE END APP_TICK */

/* 生成区：状态机骨架 ----------------------------------------------------------------*/

static void app_set_state(rcs_app_state state)
{
    if (g_state == state)
        return;
    g_state = state;
    app_on_state(state);
}

static void app_fault(void)
{
    app_on_fault();
    app_set_state(RCS_APP_FAULT);
}

int App_task_Init(CAN_HandleTypeDef *can)
{
    /* 没有启用 CAN 时 HAL 不提供句柄；无底盘模式允许传 0，接口照常初始化，
     * 只是 RCS_Arm() 恒失败，所以这里永远停在 READY。 */
#if !RCS_NO_CHASSIS
    if (can == 0)
        return 0;
#endif

    g_state = RCS_APP_NOT_READY;
    g_pose_valid = 0u;
    g_arm_ms = 0u;

    /* 初始化前先锁定，避免旧状态残留命令。 */
    RCS_Lock();
    if (!RCS_Init(can))
        return 0;
    if (!rcs_can_port_start(can))
        return 0;
    if (!rcs_feedback_port_init())
        return 0;
    if (!rcs_route_init(&g_nav, app_context()))
        return 0;

    g_can = can;
    g_chassis_tick = HAL_GetTick();
    g_app_tick = g_chassis_tick;
    app_set_state(RCS_APP_READY);
    return 1;
}

void App_task_SetStartPose(float x_m, float y_m, float yaw_rad)
{
    g_pose_x = x_m;
    g_pose_y = y_m;
    g_pose_yaw = yaw_rad;
    g_pose_valid = 1u;
}

int App_task_Start(void)
{
    rcs_feedback_sample sample;

    if (g_state != RCS_APP_READY)
        return 0;

    /* 没有显式位姿时问一次用户钩子；两者都不给就拒绝启动。 */
    if (!g_pose_valid) {
        float x_m, y_m, yaw_rad;
        if (!app_start_pose(&x_m, &y_m, &yaw_rad))
            return 0;
        App_task_SetStartPose(x_m, y_m, yaw_rad);
    }

    if (!rcs_feedback_port_read(&sample))
        return 0;
    if (!app_ready_ok(&sample))
        return 0;
    if (!rcs_nav_reset(&g_nav, g_pose_x, g_pose_y, g_pose_yaw))
        return 0;
    if (!RCS_Arm()) {
        RCS_Lock();
        return 0;
    }

    g_arm_ms = 0u;
    app_set_state(RCS_APP_ARMING);
    return 1;
}

void App_task(void)
{
    uint32_t now;
    uint32_t elapsed;

    if (g_state == RCS_APP_NOT_READY)
        return;

    now = HAL_GetTick();

    /* 底盘周期：控制与收帧。锁定期间也要跑，否则看门狗会误判。 */
    if ((uint32_t)(now - g_chassis_tick) >= (uint32_t)RCS_PERIOD_MS) {
        g_chassis_tick = now;
        RCS_Process();
        rcs_can_port_poll(g_can);
    }

    /* 导航周期：只把真实间隔交给状态机，不假设固定步长。 */
    elapsed = (uint32_t)(now - g_app_tick);
    if (elapsed < (uint32_t)RCS_APP_PERIOD_MS)
        return;
    g_app_tick = now;
    app_tick();

    switch (g_state) {
    case RCS_APP_ARMING:
        if (app_arm_step((float)elapsed * 0.001f))
            app_set_state(RCS_APP_RUNNING);
        break;
    case RCS_APP_RUNNING:
        app_run_step((float)elapsed * 0.001f);
        break;
    default:
        break;
    }

#if RCS_APP_AUTOSTART
    /* 自动启动只是省一次按键：仍然走完整前置检查。 */
    if (g_state == RCS_APP_READY &&
        (uint32_t)(now - g_auto_tick) >= (uint32_t)RCS_APP_WAIT_FEEDBACK_MS) {
        g_auto_tick = now;
        (void)App_task_Start();
    }
#endif
}

void App_task_Stop(void)
{
    rcs_nav_stop(&g_nav);
    RCS_Lock();
    g_pose_valid = 0u;
    g_arm_ms = 0u;
    if (g_state != RCS_APP_NOT_READY)
        app_set_state(RCS_APP_READY);
}

rcs_app_state App_task_State(void)
{
    return g_state;
}

rcs_nav_fault App_task_NavFault(void)
{
    return g_nav.fault;
}

int App_task_IsReady(void)
{
    return g_state != RCS_APP_NOT_READY && g_state != RCS_APP_FAULT;
}
