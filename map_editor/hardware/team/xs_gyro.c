/**
  ******************************************************************************
  * @file    xs_gyro.c
  * @brief   维特 JY61P / HWT101 串口陀螺仪解析内核（自 XS_spin8 裁剪）
  ******************************************************************************
  *
  * 数据流：
  *
  *   xs_gyro_feed()（中断上下文，只入队）
  *        │
  *        ▼
  *   xs_gyro_update()（主循环，逐字节解析）
  *        │  凑满 11 字节 -> 累加和校验 -> 按帧类型解码
  *        ▼
  *   0x53 角度帧：raw(±32768) -> 度(±180) -> 拆增量 -> 累加成连续航向
  *   0x52 角速度帧：raw(±32768) -> °/s(±2000) -> rad/s
  *
  * 与示例工程 gyro.c 的差异只有三处：
  *   1. 串口句柄由 xs_gyro_init() 传入，不再写死 huart6/USART6
  *   2. 删除 Gyro_SignMonitor()（它读 odometry 的轮式航向）与 dbg_* 观察量
  *   3. 接收回调挪到 xs_gyro_port.c，本文件不再定义 HAL 回调
  ******************************************************************************
  */

#include "xs_gyro.h"

#include <math.h>

#include "main.h"

/*==============================================================================
 * 常量
 *============================================================================*/

#define XS_GYRO_HDR             0x55u   /**< 帧头 */
#define XS_GYRO_FLAG_ACC        0x51u   /**< 加速度帧 */
#define XS_GYRO_FLAG_GYRO       0x52u   /**< 角速度帧 */
#define XS_GYRO_FLAG_ANGLE      0x53u   /**< 角度帧 */
#define XS_GYRO_FRAME_LEN       11u     /**< 帧头 + 类型 + 8 数据 + 校验 */

#define XS_GYRO_PI              3.14159265f
#define XS_GYRO_TWO_PI          6.28318531f
#define XS_GYRO_DEG2RAD         0.01745329252f
#define XS_GYRO_RAD2DEG         57.29577951f

/** 角度帧标定：raw / 32768 × 180 */
#define XS_GYRO_ANGLE_LSB_DEG   (180.0f / 32768.0f)

/** 角速度帧标定：raw / 32768 × 2000（量程 ±2000°/s） */
#define XS_GYRO_RATE_LSB_DPS    (2000.0f / 32768.0f)

#if ((XS_GYRO_RING_SIZE & (XS_GYRO_RING_SIZE - 1u)) != 0u)
#error "XS_GYRO_RING_SIZE 必须是 2 的幂（内部用掩码取模）"
#endif

/*==============================================================================
 * 私有变量
 *============================================================================*/

/** 组帧状态机 */
static uint8_t  s_frame[XS_GYRO_FRAME_LEN];
static uint8_t  s_frame_len = 0u;

/** 最近一帧的原始 yaw（度，±180）与累加后的连续 yaw（度，无界） */
static float    s_raw_deg = 0.0f;
static float    s_yaw_deg = 0.0f;

/** 最近一帧的角速度，°/s，模块原始符号 */
static float    s_wz_dps = 0.0f;

/** 航向零点（度）。GetYaw = (s_yaw_deg - s_zero_deg) × SIGN */
static float    s_zero_deg = 0.0f;

/** 当前符号系数 */
static float    s_sign = XS_GYRO_YAW_SIGN;

/** 零点"待生效"标志：还没收到第一帧角度时调用 SetHeadingZero，
 *  只能先记住想要的航向，等第一帧到了才知道从哪个原始角度起算。 */
static bool     s_zero_pending = false;
static float    s_zero_pending_rad = 0.0f;

/** 时间戳（ms） */
static volatile uint32_t s_yaw_ms = 0u;
static volatile uint32_t s_wz_ms = 0u;

/** 状态与计数 */
static volatile uint32_t s_frames = 0u;     /**< 有效角度帧数 */
static volatile uint32_t s_bad = 0u;        /**< 校验失败次数 */
static volatile uint32_t s_overflow = 0u;   /**< 环形缓冲溢出字节数 */
static volatile uint8_t  s_state = 0u;      /**< XS_GYRO_STATE_xxx */
static bool              s_have_yaw = false;

/** 环形缓冲：中断写 s_ring_w，主循环读 s_ring_r，单生产者单消费者无需关中断 */
static volatile uint8_t  s_ring[XS_GYRO_RING_SIZE];
static volatile uint16_t s_ring_w = 0u;
static volatile uint16_t s_ring_r = 0u;

/*==============================================================================
 * 小工具
 *============================================================================*/

/** 把角度折到 (-pi, pi] */
static float xs_wrap_pi(float a)
{
    while (a >  XS_GYRO_PI)  { a -= XS_GYRO_TWO_PI; }
    while (a < -XS_GYRO_PI)  { a += XS_GYRO_TWO_PI; }
    return a;
}

/** 入队（中断上下文）。满了丢新字节：旧数据比新数据更有价值，
 *  丢新字节最多让这一帧作废，丢旧字节会让整帧错位。 */
static void ring_push(uint8_t b)
{
    uint16_t w = (uint16_t)((s_ring_w + 1u) & (XS_GYRO_RING_SIZE - 1u));

    if (w == s_ring_r)
    {
        s_overflow++;
        return;
    }

    s_ring[s_ring_w] = b;
    s_ring_w = w;
}

/** 出队（主循环上下文），返回 false 表示队列空 */
static bool ring_pop(uint8_t *b)
{
    if (s_ring_r == s_ring_w)
    {
        return false;
    }

    *b = s_ring[s_ring_r];
    s_ring_r = (uint16_t)((s_ring_r + 1u) & (XS_GYRO_RING_SIZE - 1u));
    return true;
}

/*==============================================================================
 * 组帧与解码
 *============================================================================*/

/**
 * @brief  处理一帧校验通过的数据
 */
static void xs_decode(const uint8_t *f)
{
    int16_t raw;

    switch (f[1])
    {
    case XS_GYRO_FLAG_ANGLE:
        /* Roll[2..3] Pitch[4..5] Yaw[6..7] Version[8..9] */
        raw = (int16_t)((uint16_t)f[6] | ((uint16_t)f[7] << 8));

        {
            float now_deg = (float)raw * XS_GYRO_ANGLE_LSB_DEG;

            if (!s_have_yaw)
            {
                /* 第一帧只建基准，不做差：此时"上一帧"没有意义 */
                s_raw_deg  = now_deg;
                s_yaw_deg  = now_deg;
                s_have_yaw = true;

                /* 上电时（还没有任何帧）就调过 SetHeadingZero：
                 * 现在才具备"从哪个原始角度起算"的信息，在这里补上 */
                if (s_zero_pending)
                {
                    s_zero_deg = s_yaw_deg
                               - s_zero_pending_rad * s_sign * XS_GYRO_RAD2DEG;
                    s_zero_pending = false;
                    s_state |= XS_GYRO_STATE_ZEROED;
                }
            }
            else
            {
                /* 关键：先把增量折到 ±180 以内，再累加。
                 * 179.9° -> -179.9° 的跳变会被解释成 +0.2°，而不是 -359.8° */
                float d = now_deg - s_raw_deg;

                while (d >  180.0f) { d -= 360.0f; }
                while (d < -180.0f) { d += 360.0f; }

                /* 掉线后重连的防错：模块被断电复位过，它自己的 yaw 会回到 0，
                 * 算出来的"增量"其实是一整圈跳变。判据：断了超过 500ms，
                 * 且这次增量超过 30°。处理：不累加，并清掉零点已对齐标志，
                 * 由上层退回轮式航向，而不是拿着错的绝对航向乱转。 */
                if (((HAL_GetTick() - s_yaw_ms) > XS_GYRO_RELATCH_MS) &&
                    (fabsf(d) > XS_GYRO_JUMP_LIMIT_DEG))
                {
                    s_state &= (uint8_t)(~XS_GYRO_STATE_ZEROED);
                }
                else
                {
                    s_yaw_deg += d;
                }

                s_raw_deg = now_deg;
            }
        }

        s_yaw_ms = HAL_GetTick();
        s_frames++;
        break;

    case XS_GYRO_FLAG_GYRO:
        /* Wx[2..3] Wy[4..5] Wz[6..7] */
        raw = (int16_t)((uint16_t)f[6] | ((uint16_t)f[7] << 8));
        s_wz_dps = (float)raw * XS_GYRO_RATE_LSB_DPS;
        s_wz_ms  = HAL_GetTick();
        break;

    case XS_GYRO_FLAG_ACC:
    default:
        /* 加速度帧用不上，直接丢 */
        break;
    }
}

/**
 * @brief  逐字节喂进状态机
 *
 * 校验失败时不能简单地把整帧扔掉、从零开始等 0x55：
 * 如果 0x55 恰好出现在数据区里，会被当成帧头，
 * 于是后面每一帧都错位、每一帧都校验失败，看起来像"永远收不到数据"。
 * 正确做法是从本帧的第 2 个字节开始找下一个 0x55，把尾巴留下来重新对齐。
 */
static void xs_feed_byte(uint8_t b)
{
    uint8_t i;
    uint8_t sum;

    if (s_frame_len == 0u)
    {
        if (b == XS_GYRO_HDR)
        {
            s_frame[0] = b;
            s_frame_len = 1u;
        }
        return;
    }

    s_frame[s_frame_len++] = b;

    if (s_frame_len < XS_GYRO_FRAME_LEN)
    {
        return;
    }

    s_frame_len = 0u;

    sum = 0u;
    for (i = 0u; i < (XS_GYRO_FRAME_LEN - 1u); i++)
    {
        sum = (uint8_t)(sum + s_frame[i]);
    }

    if (sum == s_frame[XS_GYRO_FRAME_LEN - 1u])
    {
        xs_decode(s_frame);
        return;
    }

    s_bad++;

    for (i = 1u; i < XS_GYRO_FRAME_LEN; i++)
    {
        if (s_frame[i] == XS_GYRO_HDR)
        {
            break;
        }
    }

    if (i < XS_GYRO_FRAME_LEN)
    {
        uint8_t n = (uint8_t)(XS_GYRO_FRAME_LEN - i);
        uint8_t k;

        for (k = 0u; k < n; k++)
        {
            s_frame[k] = s_frame[i + k];
        }

        s_frame_len = n;
    }
}

/*==============================================================================
 * 对外接口
 *============================================================================*/

void xs_gyro_init(void)
{
    s_ring_w    = 0u;
    s_ring_r    = 0u;
    s_frame_len = 0u;

    s_raw_deg   = 0.0f;
    s_yaw_deg   = 0.0f;
    s_wz_dps    = 0.0f;
    s_zero_deg  = 0.0f;
    s_sign      = XS_GYRO_YAW_SIGN;

    s_zero_pending     = false;
    s_zero_pending_rad = 0.0f;

    s_yaw_ms    = 0u;
    s_wz_ms     = 0u;

    s_frames    = 0u;
    s_bad       = 0u;
    s_overflow  = 0u;
    s_state     = 0u;
    s_have_yaw  = false;
}

void xs_gyro_feed(const uint8_t *data, uint32_t len)
{
    uint32_t i;

    if (data == NULL)
    {
        return;
    }

    for (i = 0u; i < len; i++)
    {
        ring_push(data[i]);
    }
}

void xs_gyro_update(void)
{
    uint8_t b;

    /* 把缓冲里的字节全部喂给状态机。一次最多搬空整个缓冲，
     * 主循环再快也不会在这里死循环（ring_pop 空就返回 false）。 */
    while (ring_pop(&b))
    {
        xs_feed_byte(b);
    }
}

void xs_gyro_stop(void)
{
    s_ring_r    = s_ring_w;
    s_frame_len = 0u;
    s_have_yaw  = false;
    s_state     = 0u;
    s_zero_pending = false;
}

bool xs_gyro_online(void)
{
    if (!s_have_yaw)
    {
        return false;
    }

    return ((HAL_GetTick() - s_yaw_ms) <= XS_GYRO_TIMEOUT_MS);
}

bool xs_gyro_heading_ready(void)
{
    return (xs_gyro_state() & XS_GYRO_STATE_READY) == XS_GYRO_STATE_READY;
}

float xs_gyro_yaw_rad(void)
{
    /* 先减零点（度），再乘符号，最后折到 (-pi, pi]
     * 注意顺序：符号作用在"相对零点"上，而不是作用在原始值上，
     * 否则零点本身也会被镜像，重设零点时会跳一大截。 */
    float rad = (s_yaw_deg - s_zero_deg) * s_sign * XS_GYRO_DEG2RAD;

    return xs_wrap_pi(rad);
}

float xs_gyro_wz_rad_s(void)
{
    if (!xs_gyro_online())
    {
        return 0.0f;
    }

    /* 角速度帧也可能单独丢，超过超时时间就当 0，避免转向阻尼用旧值踩死 */
    if ((HAL_GetTick() - s_wz_ms) > XS_GYRO_TIMEOUT_MS)
    {
        return 0.0f;
    }

    return s_wz_dps * s_sign * XS_GYRO_DEG2RAD;
}

void xs_gyro_set_heading_zero(float heading_rad)
{
    if (!s_have_yaw)
    {
        /* 还没收到任何角度帧：先记住目标航向，等第一帧到了再折算零点。
         * 此时故意不置 ZEROED 位——零点还没真正生效。 */
        s_zero_pending     = true;
        s_zero_pending_rad = heading_rad;
        return;
    }

    s_zero_deg = s_yaw_deg - heading_rad * s_sign * XS_GYRO_RAD2DEG;
    s_state |= XS_GYRO_STATE_ZEROED;
}

void xs_gyro_set_sign(float sign)
{
    s_sign = (sign >= 0.0f) ? 1.0f : -1.0f;
}

float xs_gyro_sign(void)
{
    return s_sign;
}

uint8_t xs_gyro_state(void)
{
    uint8_t st = s_state;

    if (xs_gyro_online())
    {
        st |= XS_GYRO_STATE_ONLINE;
    }
    else
    {
        st &= (uint8_t)(~XS_GYRO_STATE_ONLINE);
    }

    return st;
}

uint32_t xs_gyro_sequence(void)
{
    return s_frames;
}

uint32_t xs_gyro_bad_frames(void)
{
    return s_bad;
}

uint32_t xs_gyro_overflow(void)
{
    return s_overflow;
}

uint32_t xs_gyro_age_ms(void)
{
    if (!s_have_yaw)
    {
        return 0xFFFFFFFFu;
    }

    return (uint32_t)(HAL_GetTick() - s_yaw_ms);
}
