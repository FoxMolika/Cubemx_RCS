# 应用层与 App_task

导出目录里除了路线与导航，还包含一层应用任务骨架。它不参与导航计算，只负责"什么时候允许跑、跑起来之后
每周期做什么、出事怎么收"。本节说明这 9 个文件各自的职责与改动位置。

## 文件

| 文件 | 职责 | 能否手改 |
|---|---|---|
| `rcs_app_task.c/.h` | 状态机骨架、分频、看门狗、`USER CODE` 区 | 只改用户区 |
| `rcs_feedback_port.c/.h` | 轮子计数增量适配，默认报告无新鲜反馈 | 无需改（用弱符号接入） |
| `rcs_can_port.c/.h` | CAN 过滤器、启动、轮询收帧 | 必要时改过滤器 |
| `rcs_app_config.h` | 应用周期、路线总闸、自动启动、超时 | 建议用 Keil Define 覆盖 |
| `rcs_feedback_config.h` | 新鲜度上限、必需掩码、增量上限、方向 | 同上 |
| `rcs_observer_config.h` | 定位源编号、有效期、质量门限、坐标对齐 | 接独立定位时填 |

## main.c 只需要三行

```c
#include "rcs_app_task.h"

/* USER CODE BEGIN 2 */
App_task_Init(&hcan1);                 /* 绑定 CAN，初始化底盘、收帧、反馈与路线 */
App_task_SetStartPose(x, y, yaw);      /* 本次实际摆放位置；不确定就不要给，车不会动 */
/* USER CODE END 2 */

while (1) {
    /* USER CODE BEGIN 3 */
    App_task();
    /* USER CODE END 3 */
}
```

启动触发（按键、上位机、裁判盒）调一次 `App_task_Start()`。返回 1 只表示进入 ARMING，下一周期才转 RUNNING。
不要再自己调用 `RCS_Process()`、`RCS_Arm()` 或 `rcs_nav_*`，否则会和 `App_task()` 抢同一套状态。

## 状态

| 状态 | 含义 | 离开方式 |
|---|---|---|
| `RCS_APP_NOT_READY` | 未初始化 | `App_task_Init()` 成功 |
| `RCS_APP_READY` | 已初始化、底盘锁定 | `App_task_Start()` |
| `RCS_APP_ARMING` | 已 reset、已解锁，等第一帧真实反馈 | 反馈到达转 RUNNING；超时转 FAULT |
| `RCS_APP_RUNNING` | 导航运行中 | 到达转 FINISHED；任一失败转 FAULT |
| `RCS_APP_FINISHED` | 走完并锁定 | `App_task_Stop()` |
| `RCS_APP_FAULT` | 故障并锁定 | 重新 `App_task_Init()` |

启动分两拍是必须的：`rcs_nav_reset()` 会清 `feedback_seen`，`rcs_nav_start()` 要求它非零，中间必须再来一帧真实反馈。

## 用户区

| 区域 | 默认行为 | 通常改什么 |
|---|---|---|
| `APP_CONTEXT` | 返回 0 | 返回机构状态指针，传给 `rcs_route_init()` |
| `APP_START_POSE` | 返回 0（永不启动） | 读定位、人工输入或固定工位 |
| `APP_READY_STEP` | 路线总闸 + 有动作必须有上下文 | 加急停、工位确认 |
| `APP_ARM_STEP` | 等一帧反馈后 `rcs_nav_start()` | 一般不改 |
| `APP_RUN_STEP` | 读反馈 → 推导航 → 下发速度 | 限速、软起停、断点续跑 |
| `APP_FAULT_ENTER` | `nav_stop` + `RCS_Lock()` | 故障上报、闪烁、驻坡保持 |
| `APP_ON_STATE` | 空 | 状态灯、蜂鸣、上位机 |
| `APP_TICK` | 空 | 按键、屏幕（禁止阻塞） |

区域旁的 `/* RCS REGION DEFAULT <key> <sha256> */` 是判断"改没改过"的依据，不要手改。

## 接入真实编码器

默认 `rcs_feedback_port_hw_read()` 是弱符号，永远返回 0。在自己任意 `.c` 里给同签名强符号即可：

```c
int rcs_feedback_port_hw_read(rcs_feedback_sample *sample)
{
    int i;
    if (!my_encoder_fresh())                    /* 本轮确实收到四轮新帧 */
        return 0;
    for (i = 0; i < 4; i++)
        sample->delta[i] = my_encoder_delta(i); /* 原始有符号计数 */
    sample->fresh_mask = 0x0Fu;
    sample->tick_ms = HAL_GetTick();            /* 采样时刻，不能填 0 */
    return 1;
}
```

`tick_ms` 填 0 会被当成"没有时间戳"直接拒绝；不允许用速度积分或上一周期值补数。

## 上机检查

1. CubeMX 里 CAN1 已启用，`App_task_Init(&hcan1)` 已调用。
2. `10 ≤ RCS_APP_PERIOD_MS ≤ 100`，且不小于 `RCS_PERIOD_MS`。
3. 编码器钩子四轮 `fresh_mask` 都为 1，`tick_ms` 正确。
4. 车摆在航点 0 上，起点误差小于 `position_tolerance_m`。
5. 先保持 `RCS_APP_ROUTE_ENABLED=0` 上电：停在 READY、底盘锁定、`App_task_NavFault()` 为 0。
6. 置 1 后启动：ARMING → RUNNING，第一条速度命令方向正确。
7. 拔反馈：应在 50 ms 内进入 FAULT 并锁定。
