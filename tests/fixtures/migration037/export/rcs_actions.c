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

/* RCS REGION DEFAULT RCS_ACTION_GLOBALS b010ec9149378da5e826cb3226f1c62aa94d5c715d8757fa04c7103d1fe90af5 */
/* USER CODE BEGIN RCS_ACTION_GLOBALS */
/* Shared state or your action_context type. */
/* USER CODE END RCS_ACTION_GLOBALS */

/* 动作配置 ------------------------------------------------------------------------------------------*/

const rcs_action_spec rcs_route_actions[RCS_ROUTE_POINT_COUNT] = {
    { 0u, 10.0f },
    { 1u, 10.0f },
    { 0u, 10.0f },
    { 0u, 10.0f },
    { 0u, 10.0f },
};

/* 航点私有动作 --------------------------------------------------------------------------------------*/

/**
 * @brief P0：机构动作
 * @note 航点稳定 ID：4525f527c4f9489aa51689222161ad77
 * @param context 用户机构状态
 * @param phase 当前动作事件
 * @param elapsed_s 当前动作运行秒数
 * @return BUSY 继续等待，DONE 完成，ERROR 机构异常
 */
static rcs_action_result action_4525f527c4f9489aa51689222161ad77(void *context,
    rcs_action_phase phase, float elapsed_s)
{
    int complete = 0;

    (void)context;
    (void)elapsed_s;

    if (phase == RCS_ACTION_CANCEL) {
/* RCS REGION DEFAULT WP_4525F527C4F9489AA51689222161AD77_CANCEL 41e53b987eee3b97b20417072ef3e7c6b618735d6c4701df01c577289233b9ec */
/* USER CODE BEGIN WP_4525F527C4F9489AA51689222161AD77_CANCEL */
/* 预留：停止、故障或超时后的机构清理。 */
/* USER CODE END WP_4525F527C4F9489AA51689222161AD77_CANCEL */
        return RCS_ACTION_DONE;
    }

    if (phase == RCS_ACTION_ENTER) {
/* RCS REGION DEFAULT WP_4525F527C4F9489AA51689222161AD77_ENTER 7f3ccad11a6c5b6ef64e8d24fb02d83068d6e212832e6a5133b5a53f87c6aebc */
/* USER CODE BEGIN WP_4525F527C4F9489AA51689222161AD77_ENTER */
/* 预留：夹爪、舵机或升降启动函数，只调用一次。 */
/* USER CODE END WP_4525F527C4F9489AA51689222161AD77_ENTER */
        return RCS_ACTION_BUSY;
    }

/* RCS REGION DEFAULT WP_4525F527C4F9489AA51689222161AD77_POLL fc8dce06c217993007de56f60e8304d24361853891e46662036c2ca13e4702d0 */
/* USER CODE BEGIN WP_4525F527C4F9489AA51689222161AD77_POLL */
/* Set complete, or return RCS_ACTION_ERROR for a mechanism fault. */
 complete = (1) ? 1 : 0;
/* USER CODE END WP_4525F527C4F9489AA51689222161AD77_POLL */

    if (complete) {
/* RCS REGION DEFAULT WP_4525F527C4F9489AA51689222161AD77_DONE 4d6d50d863ade80a25df60fae106baa9402bb3c1f1cf358211f715895bde7ac1 */
/* USER CODE BEGIN WP_4525F527C4F9489AA51689222161AD77_DONE */
/* 预留：动作完成后的清理，例如停止升降电机。 */
/* USER CODE END WP_4525F527C4F9489AA51689222161AD77_DONE */
        return RCS_ACTION_DONE;
    }

    return RCS_ACTION_BUSY;
}

/**
 * @brief P1：机构动作
 * @note 航点稳定 ID：e07acefe30c34344863a0734450dd96a
 * @param context 用户机构状态
 * @param phase 当前动作事件
 * @param elapsed_s 当前动作运行秒数
 * @return BUSY 继续等待，DONE 完成，ERROR 机构异常
 */
static rcs_action_result action_e07acefe30c34344863a0734450dd96a(void *context,
    rcs_action_phase phase, float elapsed_s)
{
    int complete = 0;

    (void)context;
    (void)elapsed_s;

    if (phase == RCS_ACTION_CANCEL) {
/* RCS REGION DEFAULT WP_E07ACEFE30C34344863A0734450DD96A_CANCEL 09afab509b821a4dfc65f47a69037153f7921fe29fd260f22255157af637ae71 */
/* USER CODE BEGIN WP_E07ACEFE30C34344863A0734450DD96A_CANCEL */
/* LEGACY_CANCEL */
/* USER CODE END WP_E07ACEFE30C34344863A0734450DD96A_CANCEL */
        return RCS_ACTION_DONE;
    }

    if (phase == RCS_ACTION_ENTER) {
/* RCS REGION DEFAULT WP_E07ACEFE30C34344863A0734450DD96A_ENTER 55df80c54326495e890cc8c90ef37ce861f62d86a93fee420f961b9587b46578 */
/* USER CODE BEGIN WP_E07ACEFE30C34344863A0734450DD96A_ENTER */
/* LEGACY_ENTER */
/* USER CODE END WP_E07ACEFE30C34344863A0734450DD96A_ENTER */
        return RCS_ACTION_BUSY;
    }

/* RCS REGION DEFAULT WP_E07ACEFE30C34344863A0734450DD96A_POLL e8bd5458ed2b18f696384ea65b78e27f0bf4448ec80ab22b69bf4a9addb9e37b */
/* USER CODE BEGIN WP_E07ACEFE30C34344863A0734450DD96A_POLL */
/* Set complete, or return RCS_ACTION_ERROR for a mechanism fault. */
 complete = (elapsed_s >= 0.01f) ? 1 : 0;
/* USER CODE END WP_E07ACEFE30C34344863A0734450DD96A_POLL */

    if (complete) {
/* RCS REGION DEFAULT WP_E07ACEFE30C34344863A0734450DD96A_DONE f500712a8b15af6606b6d69aa3e056fe6c9a170042977a9d934bb6068320088f */
/* USER CODE BEGIN WP_E07ACEFE30C34344863A0734450DD96A_DONE */
/* LEGACY_DONE */
/* USER CODE END WP_E07ACEFE30C34344863A0734450DD96A_DONE */
        return RCS_ACTION_DONE;
    }

    return RCS_ACTION_BUSY;
}

/**
 * @brief P2：机构动作
 * @note 航点稳定 ID：16cb922544b043bdbc7dab84be9be4b7
 * @param context 用户机构状态
 * @param phase 当前动作事件
 * @param elapsed_s 当前动作运行秒数
 * @return BUSY 继续等待，DONE 完成，ERROR 机构异常
 */
static rcs_action_result action_16cb922544b043bdbc7dab84be9be4b7(void *context,
    rcs_action_phase phase, float elapsed_s)
{
    int complete = 0;

    (void)context;
    (void)elapsed_s;

    if (phase == RCS_ACTION_CANCEL) {
/* RCS REGION DEFAULT WP_16CB922544B043BDBC7DAB84BE9BE4B7_CANCEL 41e53b987eee3b97b20417072ef3e7c6b618735d6c4701df01c577289233b9ec */
/* USER CODE BEGIN WP_16CB922544B043BDBC7DAB84BE9BE4B7_CANCEL */
/* 预留：停止、故障或超时后的机构清理。 */
/* USER CODE END WP_16CB922544B043BDBC7DAB84BE9BE4B7_CANCEL */
        return RCS_ACTION_DONE;
    }

    if (phase == RCS_ACTION_ENTER) {
/* RCS REGION DEFAULT WP_16CB922544B043BDBC7DAB84BE9BE4B7_ENTER 7f3ccad11a6c5b6ef64e8d24fb02d83068d6e212832e6a5133b5a53f87c6aebc */
/* USER CODE BEGIN WP_16CB922544B043BDBC7DAB84BE9BE4B7_ENTER */
/* 预留：夹爪、舵机或升降启动函数，只调用一次。 */
/* USER CODE END WP_16CB922544B043BDBC7DAB84BE9BE4B7_ENTER */
        return RCS_ACTION_BUSY;
    }

/* RCS REGION DEFAULT WP_16CB922544B043BDBC7DAB84BE9BE4B7_POLL fc8dce06c217993007de56f60e8304d24361853891e46662036c2ca13e4702d0 */
/* USER CODE BEGIN WP_16CB922544B043BDBC7DAB84BE9BE4B7_POLL */
/* Set complete, or return RCS_ACTION_ERROR for a mechanism fault. */
 complete = (1) ? 1 : 0;
/* USER CODE END WP_16CB922544B043BDBC7DAB84BE9BE4B7_POLL */

    if (complete) {
/* RCS REGION DEFAULT WP_16CB922544B043BDBC7DAB84BE9BE4B7_DONE 4d6d50d863ade80a25df60fae106baa9402bb3c1f1cf358211f715895bde7ac1 */
/* USER CODE BEGIN WP_16CB922544B043BDBC7DAB84BE9BE4B7_DONE */
/* 预留：动作完成后的清理，例如停止升降电机。 */
/* USER CODE END WP_16CB922544B043BDBC7DAB84BE9BE4B7_DONE */
        return RCS_ACTION_DONE;
    }

    return RCS_ACTION_BUSY;
}

/**
 * @brief P3：机构动作
 * @note 航点稳定 ID：e6ee18d4b9514186bd260f71c26cbebb
 * @param context 用户机构状态
 * @param phase 当前动作事件
 * @param elapsed_s 当前动作运行秒数
 * @return BUSY 继续等待，DONE 完成，ERROR 机构异常
 */
static rcs_action_result action_e6ee18d4b9514186bd260f71c26cbebb(void *context,
    rcs_action_phase phase, float elapsed_s)
{
    int complete = 0;

    (void)context;
    (void)elapsed_s;

    if (phase == RCS_ACTION_CANCEL) {
/* RCS REGION DEFAULT WP_E6EE18D4B9514186BD260F71C26CBEBB_CANCEL 41e53b987eee3b97b20417072ef3e7c6b618735d6c4701df01c577289233b9ec */
/* USER CODE BEGIN WP_E6EE18D4B9514186BD260F71C26CBEBB_CANCEL */
/* 预留：停止、故障或超时后的机构清理。 */
/* USER CODE END WP_E6EE18D4B9514186BD260F71C26CBEBB_CANCEL */
        return RCS_ACTION_DONE;
    }

    if (phase == RCS_ACTION_ENTER) {
/* RCS REGION DEFAULT WP_E6EE18D4B9514186BD260F71C26CBEBB_ENTER 7f3ccad11a6c5b6ef64e8d24fb02d83068d6e212832e6a5133b5a53f87c6aebc */
/* USER CODE BEGIN WP_E6EE18D4B9514186BD260F71C26CBEBB_ENTER */
/* 预留：夹爪、舵机或升降启动函数，只调用一次。 */
/* USER CODE END WP_E6EE18D4B9514186BD260F71C26CBEBB_ENTER */
        return RCS_ACTION_BUSY;
    }

/* RCS REGION DEFAULT WP_E6EE18D4B9514186BD260F71C26CBEBB_POLL fc8dce06c217993007de56f60e8304d24361853891e46662036c2ca13e4702d0 */
/* USER CODE BEGIN WP_E6EE18D4B9514186BD260F71C26CBEBB_POLL */
/* Set complete, or return RCS_ACTION_ERROR for a mechanism fault. */
 complete = (1) ? 1 : 0;
/* USER CODE END WP_E6EE18D4B9514186BD260F71C26CBEBB_POLL */

    if (complete) {
/* RCS REGION DEFAULT WP_E6EE18D4B9514186BD260F71C26CBEBB_DONE 4d6d50d863ade80a25df60fae106baa9402bb3c1f1cf358211f715895bde7ac1 */
/* USER CODE BEGIN WP_E6EE18D4B9514186BD260F71C26CBEBB_DONE */
/* 预留：动作完成后的清理，例如停止升降电机。 */
/* USER CODE END WP_E6EE18D4B9514186BD260F71C26CBEBB_DONE */
        return RCS_ACTION_DONE;
    }

    return RCS_ACTION_BUSY;
}

/**
 * @brief P4：机构动作
 * @note 航点稳定 ID：1183cbcb130a4722be6630b5975f828e
 * @param context 用户机构状态
 * @param phase 当前动作事件
 * @param elapsed_s 当前动作运行秒数
 * @return BUSY 继续等待，DONE 完成，ERROR 机构异常
 */
static rcs_action_result action_1183cbcb130a4722be6630b5975f828e(void *context,
    rcs_action_phase phase, float elapsed_s)
{
    int complete = 0;

    (void)context;
    (void)elapsed_s;

    if (phase == RCS_ACTION_CANCEL) {
/* RCS REGION DEFAULT WP_1183CBCB130A4722BE6630B5975F828E_CANCEL 41e53b987eee3b97b20417072ef3e7c6b618735d6c4701df01c577289233b9ec */
/* USER CODE BEGIN WP_1183CBCB130A4722BE6630B5975F828E_CANCEL */
/* 预留：停止、故障或超时后的机构清理。 */
/* USER CODE END WP_1183CBCB130A4722BE6630B5975F828E_CANCEL */
        return RCS_ACTION_DONE;
    }

    if (phase == RCS_ACTION_ENTER) {
/* RCS REGION DEFAULT WP_1183CBCB130A4722BE6630B5975F828E_ENTER 7f3ccad11a6c5b6ef64e8d24fb02d83068d6e212832e6a5133b5a53f87c6aebc */
/* USER CODE BEGIN WP_1183CBCB130A4722BE6630B5975F828E_ENTER */
/* 预留：夹爪、舵机或升降启动函数，只调用一次。 */
/* USER CODE END WP_1183CBCB130A4722BE6630B5975F828E_ENTER */
        return RCS_ACTION_BUSY;
    }

/* RCS REGION DEFAULT WP_1183CBCB130A4722BE6630B5975F828E_POLL fc8dce06c217993007de56f60e8304d24361853891e46662036c2ca13e4702d0 */
/* USER CODE BEGIN WP_1183CBCB130A4722BE6630B5975F828E_POLL */
/* Set complete, or return RCS_ACTION_ERROR for a mechanism fault. */
 complete = (1) ? 1 : 0;
/* USER CODE END WP_1183CBCB130A4722BE6630B5975F828E_POLL */

    if (complete) {
/* RCS REGION DEFAULT WP_1183CBCB130A4722BE6630B5975F828E_DONE 4d6d50d863ade80a25df60fae106baa9402bb3c1f1cf358211f715895bde7ac1 */
/* USER CODE BEGIN WP_1183CBCB130A4722BE6630B5975F828E_DONE */
/* 预留：动作完成后的清理，例如停止升降电机。 */
/* USER CODE END WP_1183CBCB130A4722BE6630B5975F828E_DONE */
        return RCS_ACTION_DONE;
    }

    return RCS_ACTION_BUSY;
}

/* 导出函数 ------------------------------------------------------------------------------------------*/

/**
 * @brief 将导航事件分发到对应航点的动作函数
 * @param context 用户机构状态，可为空
 * @param point 当前航点下标
 * @param phase 启动、轮询或取消事件
 * @param elapsed_s 当前动作已运行时间，秒
 * @return BUSY 继续等待，DONE 完成，ERROR 机构异常
 */
rcs_action_result rcs_route_action(void *context, size_t point,
                                   rcs_action_phase phase, float elapsed_s)
{
    switch (point) {
    case 0u:
        return action_4525f527c4f9489aa51689222161ad77(context, phase, elapsed_s);
    case 1u:
        return action_e07acefe30c34344863a0734450dd96a(context, phase, elapsed_s);
    case 2u:
        return action_16cb922544b043bdbc7dab84be9be4b7(context, phase, elapsed_s);
    case 3u:
        return action_e6ee18d4b9514186bd260f71c26cbebb(context, phase, elapsed_s);
    case 4u:
        return action_1183cbcb130a4722be6630b5975f828e(context, phase, elapsed_s);
    default:
        return RCS_ACTION_ERROR;
    }
}
/* RCS GENERATED SHA256 16427da4b65b7078d1a1a979e0002ee04b3f43f949226a620c75db62083b2f8b */
