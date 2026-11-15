"""Single parameter source for CubeMX XML, desktop preview and C export."""
from pathlib import Path
import json, math
from version import VERSION

ROOT=Path(__file__).resolve().parents[1]
# macro, Chinese label, default, type, minimum, maximum, group, options
PARAMS=[
 ('RCS_LAYOUT','底盘布局',0,'list',0,2,'底盘',[(0,'四麦轮 X 型'),(1,'三全向轮 120 度'),(2,'四全向轮 90 度')]),
 ('RCS_WHEEL_DIAMETER_MM','轮子直径 mm',100,'float',1,1000,'几何',None),
 ('RCS_WHEELBASE_MM','前后轮中心距 mm（麦轮）',300,'float',1,3000,'几何',None),
 ('RCS_TRACK_MM','左右轮中心距 mm（麦轮）',300,'float',1,3000,'几何',None),
 ('RCS_OMNI_RADIUS_MM','底盘中心至轮中心 mm（全向）',200,'float',1,1500,'几何',None),
 ('RCS_OMNI_THETA0_DEG','全向轮 W0 位置角 deg',0,'float',-360,360,'几何',None),
 ('RCS_GEAR_RATIO','总减速比（电机转速/轮转速）',19.2032,'float',0.01,1000,'电机',None),
 ('RCS_MAX_MOTOR_RPM','电机端转速上限 rpm',3000,'float',1,20000,'电机',None),
 ('RCS_CAN_BUS','全部底盘电机所在 CAN',1,'list',1,2,'硬件',[(1,'CAN1'),(2,'CAN2')]),
 *[(f'RCS_MOTOR_ID_{i}',f'轮 W{i} 电机 ID',i+1,'integer',1,4,'硬件',None) for i in range(4)],
 *[(f'RCS_DIRECTION_{i}',f'轮 W{i} 方向',1,'list',-1,1,'硬件',[(-1,'反向 -1'),(1,'正向 +1')]) for i in range(4)],
 ('RCS_KP','速度环 Kp（初值为零，需调参）',0,'float',0,100,'闭环',None),
 ('RCS_KI','速度环 Ki（按秒积分）',0,'float',0,100,'闭环',None),
 ('RCS_KD','速度环 Kd（按秒微分）',0,'float',0,100,'闭环',None),
 ('RCS_CURRENT_LIMIT_RAW','C620 电流指令幅值（原始计数）',2000,'integer',1,16384,'闭环',None),
 ('RCS_PERIOD_MS','控制周期 ms',5,'integer',1,100,'控制',None),
 ('RCS_COMMAND_TIMEOUT_MS','命令超时 ms',200,'integer',2,60000,'控制',None),
 ('RCS_FEEDBACK_TIMEOUT_MS','反馈超时 ms',100,'integer',2,60000,'控制',None),
]
DEFAULTS={p[0]:p[2] for p in PARAMS}

def validate(values):
    if not isinstance(values,dict): raise ValueError('配置必须是 JSON 对象')
    unknown=set(values)-set(DEFAULTS)
    if unknown: raise ValueError('未知参数: '+', '.join(sorted(unknown)))
    result=dict(DEFAULTS)
    for key,label,default,typ,lo,hi,group,options in PARAMS:
        v=values.get(key,default)
        if isinstance(v,bool) or not isinstance(v,(int,float)) or not math.isfinite(v):
            raise ValueError(f'{label}: 必须是有限数字')
        if typ in ('integer','list') and int(v)!=v: raise ValueError(f'{label}: 必须是整数')
        if not lo<=v<=hi: raise ValueError(f'{label}: 范围 {lo}..{hi}')
        if options and v not in [x[0] for x in options]: raise ValueError(f'{label}: 非法选项')
        result[key]=int(v) if typ in ('integer','list') else float(v)
    n=3 if result['RCS_LAYOUT']==1 else 4
    if len({result[f'RCS_MOTOR_ID_{i}'] for i in range(n)})!=n: raise ValueError('在用轮子的电机 ID 不能重复')
    for timeout in ('RCS_COMMAND_TIMEOUT_MS','RCS_FEEDBACK_TIMEOUT_MS'):
        if result[timeout]<=result['RCS_PERIOD_MS']: raise ValueError('超时必须大于控制周期')
    return result

# Compile-time guards: CubeMX only enforces per-field Min/Max, so cross-field
# rules are rejected here before the firmware can silently refuse RCS_Init.
CONFIG_TAIL=r'''
#if (RCS_LAYOUT) < 0 || (RCS_LAYOUT) > 2
#error "RCS: RCS_LAYOUT must be 0 (Mecanum X), 1 (Omni3) or 2 (Omni4)"
#endif
#if (RCS_CAN_BUS) != 1 && (RCS_CAN_BUS) != 2
#error "RCS: RCS_CAN_BUS must be 1 or 2"
#endif
#if (RCS_MOTOR_ID_0) < 1 || (RCS_MOTOR_ID_0) > 4 || (RCS_MOTOR_ID_1) < 1 || (RCS_MOTOR_ID_1) > 4 || \
    (RCS_MOTOR_ID_2) < 1 || (RCS_MOTOR_ID_2) > 4 || ((RCS_LAYOUT) != 1 && ((RCS_MOTOR_ID_3) < 1 || (RCS_MOTOR_ID_3) > 4))
#error "RCS: motor IDs of used wheels must be 1..4"
#endif
#if (RCS_MOTOR_ID_0) == (RCS_MOTOR_ID_1) || (RCS_MOTOR_ID_0) == (RCS_MOTOR_ID_2) || (RCS_MOTOR_ID_1) == (RCS_MOTOR_ID_2)
#error "RCS: duplicate motor ID among W0/W1/W2"
#endif
#if (RCS_LAYOUT) != 1 && ((RCS_MOTOR_ID_3) == (RCS_MOTOR_ID_0) || (RCS_MOTOR_ID_3) == (RCS_MOTOR_ID_1) || (RCS_MOTOR_ID_3) == (RCS_MOTOR_ID_2))
#error "RCS: W3 motor ID duplicates another wheel"
#endif
#if ((RCS_DIRECTION_0) != 1 && (RCS_DIRECTION_0) != -1) || ((RCS_DIRECTION_1) != 1 && (RCS_DIRECTION_1) != -1) || \
    ((RCS_DIRECTION_2) != 1 && (RCS_DIRECTION_2) != -1) || ((RCS_DIRECTION_3) != 1 && (RCS_DIRECTION_3) != -1)
#error "RCS: every RCS_DIRECTION_n must be +1 or -1"
#endif
#if (RCS_CURRENT_LIMIT_RAW) < 1 || (RCS_CURRENT_LIMIT_RAW) > 16384
#error "RCS: RCS_CURRENT_LIMIT_RAW must be 1..16384"
#endif
#if (RCS_PERIOD_MS) < 1 || (RCS_PERIOD_MS) > 100
#error "RCS: RCS_PERIOD_MS must be 1..100"
#endif
#if (RCS_COMMAND_TIMEOUT_MS) <= (RCS_PERIOD_MS) || (RCS_COMMAND_TIMEOUT_MS) > 60000
#error "RCS: RCS_COMMAND_TIMEOUT_MS must be > RCS_PERIOD_MS and <= 60000"
#endif
#if (RCS_FEEDBACK_TIMEOUT_MS) <= (RCS_PERIOD_MS) || (RCS_FEEDBACK_TIMEOUT_MS) > 60000
#error "RCS: RCS_FEEDBACK_TIMEOUT_MS must be > RCS_PERIOD_MS and <= 60000"
#endif

#include "rcs_control.h"
#define RCS_CONTROL_CONFIG { \
 { (rcs_layout)RCS_LAYOUT, RCS_WHEEL_DIAMETER_MM/1000.0f, \
   RCS_WHEELBASE_MM/1000.0f, RCS_TRACK_MM/1000.0f, RCS_OMNI_RADIUS_MM/1000.0f, \
   RCS_OMNI_THETA0_DEG*0.0174532925199433f, RCS_GEAR_RATIO, RCS_MAX_MOTOR_RPM }, \
 {RCS_MOTOR_ID_0,RCS_MOTOR_ID_1,RCS_MOTOR_ID_2,RCS_MOTOR_ID_3}, \
 {RCS_DIRECTION_0,RCS_DIRECTION_1,RCS_DIRECTION_2,RCS_DIRECTION_3}, \
 RCS_KP,RCS_KI,RCS_KD,RCS_CURRENT_LIMIT_RAW, \
 RCS_PERIOD_MS,RCS_COMMAND_TIMEOUT_MS,RCS_FEEDBACK_TIMEOUT_MS }
#endif
'''

def config_header(values):
    v=validate(values)
    return '/* Generated by RCS Chassis '+VERSION+'. Edit configuration, not this file. */\n#ifndef RCS_CONFIG_H\n#define RCS_CONFIG_H\n'+''.join(f'#define {k} {format(x,".9g")}\n' for k,x in v.items())+CONFIG_TAIL

def preview(v,vx,vy,wz):
    v=validate(v)
    if not all(math.isfinite(x) for x in (vx,vy,wz)): raise ValueError('速度必须是有限数字')
    if v['RCS_LAYOUT']==0:
        z=(v['RCS_WHEELBASE_MM']+v['RCS_TRACK_MM'])/2000*wz
        speed=[vx-vy-z,vx+vy+z,vx+vy-z,vx-vy+z]
    else:
        n=3 if v['RCS_LAYOUT']==1 else 4
        angles=[math.radians(v['RCS_OMNI_THETA0_DEG'])+2*math.pi*i/n for i in range(n)]
        speed=[-math.sin(a)*vx+math.cos(a)*vy+v['RCS_OMNI_RADIUS_MM']/1000*wz for a in angles]
    rpm=[s*60*v['RCS_GEAR_RATIO']/(math.pi*v['RCS_WHEEL_DIAMETER_MM']/1000) for s in speed]
    scale=min(1,v['RCS_MAX_MOTOR_RPM']/max(map(abs,rpm))) if any(rpm) else 1
    return [x*scale for x in rpm],scale

def load(path): return validate(json.loads(Path(path).read_text(encoding='utf-8-sig')))
