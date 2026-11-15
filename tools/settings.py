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
CONFIG_TAIL=(ROOT/'templates/rcs_config_checks.h.tpl').read_text(encoding='utf-8')
CONFIG_PREFIX=(ROOT/'templates/rcs_config_prefix.h.tpl').read_text(encoding='utf-8')

def config_header(values):
    v=validate(values)
    return CONFIG_PREFIX+''.join(f'#define {k} {format(x,".9g")}\n' for k,x in v.items())+CONFIG_TAIL

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
