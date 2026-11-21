/**
 * @file rcs_actions.c
 * @brief 航点机构动作：启动、轮询、完成与取消清理
 * @note 用户代码只写在 USER CODE 区域；所有回调必须快速返回，不能阻塞等待。
 */
#include "rcs_actions.h"

/* 用户驱动头文件与状态 ------------------------------------------------------------------------------*/

/* RCS REGION DEFAULT RCS_ACTION_INCLUDES c836ed0541a11ce54984c64aaea6351eeeb6a7489e9b17085d97f5f7a96675a4 */
/* USER CODE BEGIN RCS_ACTION_INCLUDES */
/* 预留用户驱动头文件，例如 #include "gripper.h" */
/* USER CODE END RCS_ACTION_INCLUDES */

/* RCS REGION DEFAULT RCS_ACTION_GLOBALS 24234a7bab9e847df0f8607f7dd41bd5898c4c2d22c168547e6cdc0338cdb27a */
/* USER CODE BEGIN RCS_ACTION_GLOBALS */
/* 公共机构状态或action_context类型。 */
/* USER CODE END RCS_ACTION_GLOBALS */

/* 动作配置 ------------------------------------------------------------------------------------------*/

/* 动作配置由rcs_route_plan描述；分发编号标识自定义机构动作。 */

/* 航点私有动作 --------------------------------------------------------------------------------------*/

/* 导出函数 ------------------------------------------------------------------------------------------*/

/**
 * @brief 将导航事件分发到对应航点的动作函数
 * @param context 用户机构状态，可为空
 * @param point 全局自定义动作分发编号（不是航点下标）
 * @param phase 启动、轮询或取消事件
 * @param elapsed_s 当前动作已运行时间，秒
 * @return BUSY 继续等待，DONE 完成，ERROR 机构异常
 */
rcs_action_result rcs_route_action(void *context, size_t point,
                                   rcs_action_phase phase, float elapsed_s)
{
    (void)context;
    (void)phase;
    (void)elapsed_s;
    switch (point) {
    default:
        return RCS_ACTION_ERROR;
    }
}


/**
 * @brief 读取车体中心的独立定位观测；无数据返回0，禁止伪造完成
 * @note 适配器负责真实采样序号、年龄、质量、安装偏移与地图坐标对齐。
 */
int rcs_route_observe(void *context, unsigned source, rcs_observation *sample)
{
    (void)context;
    (void)sample;
    switch (source) {
    default:
        return 0;
    }
}


/**
 * @brief 地形制动适配入口：默认未接入，禁止把零电流伪装成驻坡
 */
int rcs_route_brake(void *context, rcs_terrain_brake_request request)
{
    (void)context;
    (void)request;
/* RCS REGION DEFAULT RCS_TERRAIN_BRAKE d7ad1cca99ce7c812a55c7c22534dfbdbf9a49893173c804ce5cadb6e8093c7d */
/* USER CODE BEGIN RCS_TERRAIN_BRAKE */
    /* 可在此补充真实硬件检查；未返回时使用下方配置绑定。 */
/* USER CODE END RCS_TERRAIN_BRAKE */
    /* 先接入真实驱动并实测，再在坡道参数中确认制动能力。 */
    return 0;
}

/* RCS GENERATED SHA256 d6596a9de3ff88250ed3607b3f042915fbbec9e9f535eb7edcab5f734cac5e16 */
