/**
 * @file foxmolika_observation_adapter.c
 * @brief 独立定位接入位置；未实现时返回失败，禁止伪造成功
 */
#include "foxmolika_observation_adapter.h"
/**
 * @brief 用队内真实定位驱动填充观测；保持非阻塞
 * @note 未实现。不能每次调用就递增sequence；它必须对应真实采样。
 */
int TeamGps_ReadObservation(rcs_observation *sample)
{
    (void)sample;
    /* 从真实驱动读取车体中心的地图坐标，填入米/弧度、年龄、质量和采样序号。
     * 完成安装偏移和初始坐标对齐后，才将硬件档案MapAligned/BindingReady置true。
     * 不允许用驱动电机编码器的积分结果填充此独立观测。
     */
    return 0;
}
