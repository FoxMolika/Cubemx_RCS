/** @file rcs_types.h
 * @brief 队内驱动错误码桥；RCS_OK沿用底盘枚举，避免预处理宏污染。
 */
#pragma once
typedef int rcs_err_t;
#define RCS_FAIL -1
#define RCS_ERR_INVALID_PARAM -2
#define RCS_ERR_OVERFLOW -3
#define RCS_ERR_MULTIPLE_USED -4
#define RCS_ERR_HW -5
#define RCS_ERR_BUSY -6
#define RCS_ERR_NO_MEM -7
#define RCS_ERR_NOT_ALLOWED -8
