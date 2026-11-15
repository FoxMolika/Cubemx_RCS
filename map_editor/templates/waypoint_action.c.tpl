
/**
 * @brief P@INDEX@：@NAME@
 * @note 航点稳定 ID：@ID@
 * @param context 用户机构状态
 * @param phase 当前动作事件
 * @param elapsed_s 当前动作运行秒数
 * @return BUSY 继续等待，DONE 完成，ERROR 机构异常
 */
static rcs_action_result action_@ID@(void *context,
    rcs_action_phase phase, float elapsed_s)
{
    int complete = 0;

    (void)context;
    (void)elapsed_s;

    if (phase == RCS_ACTION_CANCEL) {
@CANCEL_REGION@        return RCS_ACTION_DONE;
    }

    if (phase == RCS_ACTION_ENTER) {
@ENTER_REGION@        return RCS_ACTION_BUSY;
    }

@POLL_REGION@
    if (complete) {
@DONE_REGION@        return RCS_ACTION_DONE;
    }

    return RCS_ACTION_BUSY;
}
