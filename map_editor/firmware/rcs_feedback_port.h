/**
 * @file rcs_feedback_port.h
 * @brief C620逐帧解绕、按逻辑轮累计、原子消费，不积分rpm或伪造新鲜反馈
 */
#pragma once
#include "rcs_hal.h"
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif
typedef struct {
    int32_t delta[4];   /* 上次消费后累计的原始转子计数，逻辑W0..W3 */
    uint8_t fresh_mask; /* 在用轮均有未消费的真实新帧 */
    uint32_t tick_ms;   /* 本批最旧的采样时刻，允许0及uint32回绕 */
    uint32_t sequence;  /* 真实接收序号，读取不增加 */
} rcs_feedback_sample;
typedef enum {
    RCS_FEEDBACK_OK = 0,
    RCS_FEEDBACK_BAD_CONFIG,
    RCS_FEEDBACK_STALE,
    RCS_FEEDBACK_AMBIGUOUS_GAP,
    RCS_FEEDBACK_BAD_ANGLE,
    RCS_FEEDBACK_DELTA_RANGE,
    RCS_FEEDBACK_FIFO_OVERRUN,
    RCS_FEEDBACK_CAN_BUS_OFF,
    RCS_FEEDBACK_CAN_RX_FAILED
} rcs_feedback_fault;
int rcs_feedback_port_init(void);
void rcs_feedback_port_on_rx(CAN_HandleTypeDef *can, const CAN_RxHeaderTypeDef *header,
                              const uint8_t data[8]);
/* 定位/reset时建立新采样边界；下一帧只建基准，等待全部在用轮新帧再启动。 */
void rcs_feedback_port_reset(void);
/* 成功后原子清累计/fresh；缺新帧或故障返回0，不能重复消费旧增量。 */
int rcs_feedback_port_read(rcs_feedback_sample *sample);
rcs_feedback_fault rcs_feedback_port_fault(void);
/* 收帧层发现异常时锁存首因，只有显式reset/init清除。 */
void rcs_feedback_port_invalidate(rcs_feedback_fault fault);
/* 只读调试取值：不改变任何状态，供 g_dbg 快照使用。 */
uint32_t rcs_feedback_port_sequence(void);
/* 进入收帧函数的次数，含锁存后被丢弃的帧；用于区分“没收到”和“收到但丢掉”。 */
uint32_t rcs_feedback_port_rx_count(void);
/* 该轮尚未被应用消费的原始增量；轮次越界返回0。 */
int32_t rcs_feedback_port_pending(unsigned wheel);
/* 距该轮上次被消费的毫秒数；从未收到返回0xFFFFFFFF。 */
uint32_t rcs_feedback_port_age_ms(unsigned wheel);
/* 最近一次同轮帧间隔（微秒）；DWT未启用时恒为0，判据本身仍按毫秒。 */
uint32_t rcs_feedback_port_gap_us_last(void);
/* 本次reset以来最大的同轮帧间隔（微秒），用于定位空窗长度。 */
uint32_t rcs_feedback_port_gap_us_max(void);
/* 1 = DWT周期计数已启用，微秒读数可信。 */
uint32_t rcs_feedback_port_dwt_ready(void);
#ifdef __cplusplus
}
#endif
