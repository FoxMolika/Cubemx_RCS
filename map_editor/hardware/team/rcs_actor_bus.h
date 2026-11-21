/** @file rcs_actor_bus.h
 * @brief alpha使用应用调度替代队内RTOS总线，仅保留原驱动所需基类。
 */
#pragma once
#include "rcs_private_config.h"
#include "rcs_os.h"
class rcs_actor {
public:
    virtual ~rcs_actor() = default;
    using excute_fptr = rcs_err_t (*)(void);
    using reset_fptr = rcs_err_t (*)(void);
    using monitor_fptr = float (*)(uint8_t);
};
