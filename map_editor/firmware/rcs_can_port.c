/**
 * @file rcs_can_port.c
 * @brief FIFO0中断收帧，按CAN实例选择过滤器，保留接收异常首因
 */
#include "rcs_can_port.h"
#include "rcs_feedback_config.h"
#include "rcs_feedback_port.h"
#if RCS_NO_CHASSIS
int rcs_can_port_start(CAN_HandleTypeDef *can) { (void)can; return 0; }
void rcs_can_port_poll(CAN_HandleTypeDef *can) { (void)can; }
int rcs_can_port_discard_rx(void) { return 1; }
void rcs_can_port_on_rx(CAN_HandleTypeDef *can, const CAN_RxHeaderTypeDef *header,
                        const uint8_t data[8]) { (void)can; (void)header; (void)data; }
#else
static CAN_HandleTypeDef *g_can;
int rcs_can_port_start(CAN_HandleTypeDef *can)
{
    CAN_FilterTypeDef filter = {0};
    uint32_t bank, split = (uint32_t)RCS_CAN_PORT_FILTER_SPLIT;
    if (!can || (can->Instance != CAN1 && can->Instance != CAN2)) return 0;
    if ((RCS_FEEDBACK_CAN_BUS == 1u && can->Instance != CAN1) ||
        (RCS_FEEDBACK_CAN_BUS == 2u && can->Instance != CAN2)) return 0;
    bank = can->Instance == CAN2 ? (uint32_t)RCS_CAN_PORT_FILTER_BANK_CAN2 :
                                  (uint32_t)RCS_CAN_PORT_FILTER_BANK_CAN1;
    if (split < 1u || split > 27u || bank > 27u ||
        (can->Instance == CAN1 && bank >= split) || (can->Instance == CAN2 && bank < split))
        return 0;
    filter.FilterBank = bank;
    filter.FilterMode = CAN_FILTERMODE_IDMASK;
    filter.FilterScale = CAN_FILTERSCALE_32BIT;
    filter.FilterIdHigh = 0x200u << 5;
    filter.FilterMaskIdHigh = 0x7F8u << 5;
    filter.FilterFIFOAssignment = CAN_RX_FIFO0;
    filter.FilterActivation = CAN_FILTER_ENABLE;
    filter.SlaveStartFilterBank = split;
    if (HAL_CAN_ConfigFilter(can, &filter) != HAL_OK) return 0;
    /* 仅复用本端口已经启动且仍在监听的同一句柄。真实HAL不接受重复Start；
     * 未由本端口拥有的监听总线不能凭一个HAL_ERROR就被静默接管。
     */
    if (g_can != can || HAL_CAN_GetState(can) != HAL_CAN_STATE_LISTENING)
        if (HAL_CAN_Start(can) != HAL_OK) return 0;
    /* 先绑定再开通知，第一帧中断也能分发。 */
    g_can = can;
    if (HAL_CAN_ActivateNotification(can, CAN_IT_RX_FIFO0_MSG_PENDING) != HAL_OK) {
        /* 已启动总线仍保留收帧分发，避免旧pending IRQ无人排空；返回失败禁止运行。 */
        rcs_feedback_port_invalidate(RCS_FEEDBACK_CAN_RX_FAILED);
        return 0;
    }
    return 1;
}
void rcs_can_port_on_rx(CAN_HandleTypeDef *can, const CAN_RxHeaderTypeDef *header,
                        const uint8_t data[8])
{
    if (!can || !header || !data) return;
    RCS_OnCanRx(can, header, data);
    rcs_feedback_port_on_rx(can, header, data);
}
int rcs_can_port_discard_rx(void)
{
    CAN_RxHeaderTypeDef header;
    uint8_t data[8];
    uint32_t saved;
    int ok = 1;
    if (!g_can) return 1;
    saved = __get_PRIMASK();
    __disable_irq();
    while (HAL_CAN_GetRxFifoFillLevel(g_can, CAN_RX_FIFO0) > 0u) {
        if (HAL_CAN_GetRxMessage(g_can, CAN_RX_FIFO0, &header, data) != HAL_OK) {
            rcs_feedback_port_invalidate(RCS_FEEDBACK_CAN_RX_FAILED);
            ok = 0;
            break;
        }
    }
    /* 这是显式重新定位的边界；清掉旧溢出标志，随后新IRQ仍会重新检测。 */
    __HAL_CAN_CLEAR_FLAG(g_can, CAN_FLAG_FOV0);
    __set_PRIMASK(saved);
    return ok;
}
void rcs_can_port_poll(CAN_HandleTypeDef *can)
{
    CAN_RxHeaderTypeDef header;
    uint8_t data[8];
    uint32_t saved;
    if (!can || can != g_can) return;
    /* 寄存器取帧的短窗口屏蔽IRQ，避免任务/ISR抢读。不开FOVR中断，
     * pending回调保存硬件FOVR首因后再清除，避免HAL提前清旗后丢失诊断。
     */
    saved = __get_PRIMASK();
    __disable_irq();
    if ((can->Instance->ESR & CAN_ESR_BOFF) != 0u)
        rcs_feedback_port_invalidate(RCS_FEEDBACK_CAN_BUS_OFF);
    if (__HAL_CAN_GET_FLAG(can, CAN_FLAG_FOV0)) {
        rcs_feedback_port_invalidate(RCS_FEEDBACK_FIFO_OVERRUN);
        __HAL_CAN_CLEAR_FLAG(can, CAN_FLAG_FOV0);
    }
    while (HAL_CAN_GetRxFifoFillLevel(can, CAN_RX_FIFO0) > 0u) {
        if (HAL_CAN_GetRxMessage(can, CAN_RX_FIFO0, &header, data) != HAL_OK) {
            rcs_feedback_port_invalidate(RCS_FEEDBACK_CAN_RX_FAILED);
            break;
        }
        rcs_can_port_on_rx(can, &header, data);
    }
    __set_PRIMASK(saved);
}
#if RCS_CAN_PORT_DEFINE_HAL_CALLBACK
void HAL_CAN_RxFifo0MsgPendingCallback(CAN_HandleTypeDef *can)
{
    rcs_can_port_poll(can);
}
#else
/* 强定义让位给统一分发器；万一它没被链接进来，这里以弱定义兜住中断，
 * 否则HAL的__weak空函数会让收帧退化成主循环轮询，出现5ms以上空窗。
 */
#if defined(__WEAK)
#define RCS_CAN_RX_WEAK __WEAK
#elif defined(__GNUC__)
#define RCS_CAN_RX_WEAK __attribute__((weak))
#else
#define RCS_CAN_RX_WEAK
#endif
RCS_CAN_RX_WEAK void HAL_CAN_RxFifo0MsgPendingCallback(CAN_HandleTypeDef *can)
{
    rcs_can_port_poll(can);
}
#undef RCS_CAN_RX_WEAK
#endif
#endif
