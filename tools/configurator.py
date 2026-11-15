"""Offline companion for preview/export, alongside the native CubeMX pack."""
import argparse,json,math
from pathlib import Path
from settings import ROOT,PARAMS,DEFAULTS,validate,preview,load,config_header
from version import VERSION

def inspect_ioc(path,values):
    text=Path(path).read_text(encoding='utf-8-sig');props={}
    for line in text.splitlines():
        if '=' in line and not line.startswith('#'):
            k,v=line.split('=',1);props[k]=v
    can='CAN'+str(int(values['RCS_CAN_BUS']))
    pins={k[:-7]:v for k,v in props.items() if k.endswith('.Signal') and v.startswith(can+'_')}
    warnings=[]
    if 'STM32F407' not in props.get('Mcu.Name',''):warnings.append('当前适配器仅验证 STM32F407，芯片不匹配')
    if not any(x.endswith('_RX') for x in pins.values()) or not any(x.endswith('_TX') for x in pins.values()):warnings.append(can+' RX/TX 引脚未完整分配')
    if props.get(can+'.CalculateBaudRate')!='1000000':warnings.append(can+' 未记录为 1 Mbps；需要核对实际时钟和位时序')
    if not any(can+'_RX0_IRQn' in k and v.startswith('true') for k,v in props.items()):warnings.append(can+' FIFO0 接收中断未启用或格式无法识别')
    return {'mcu':props.get('Mcu.Name'),'cubemx':props.get('MxCube.Version'),'can':can,'pins':pins,'warnings':warnings,
            'note':'仅静态检查 .ioc；不检测实际接线、CAN 收发器或电机在线状态。原文件未修改。'}

def module_files(values):
    """Only source/header payloads; no project files, application entry, or callbacks."""
    v=validate(values)
    files={p.name:p.read_bytes() for p in (ROOT/'firmware').iterdir()
           if p.is_file() and p.suffix in ('.c','.h')}
    files['rcs_config.h']=config_header(v).encode('utf-8')
    from style_check import check_files
    errors=check_files({name:data.decode('utf-8') for name,data in files.items()})
    if errors:raise ValueError('Generated C style failed:\n'+'\n'.join(errors))
    return files

def export(values,out):
    files=module_files(values);out=Path(out).resolve()
    # Never overlay an existing firmware project or previously generated content.
    if out.exists() and any(out.iterdir()):raise ValueError('导出目录必须为空，避免覆盖已有代码')
    out.mkdir(parents=True,exist_ok=True)
    for name,data in files.items():(out/name).write_bytes(data)
    return out

def gui():
    import tkinter as tk
    from tkinter import ttk,filedialog,messagebox
    root=tk.Tk();root.title('foxmolika 底盘生成器 '+VERSION+' · CubeMX 配套预览');root.geometry('1100x800')
    root.minsize(980,700)
    style=ttk.Style(root);style.theme_use('clam');style.configure('TLabel',font=('Microsoft YaHei UI',10));style.configure('TButton',font=('Microsoft YaHei UI',10),padding=6)
    ttk.Label(root,text='foxmolika 底盘配置与代码预览',font=('Microsoft YaHei UI',18,'bold')).pack(anchor='w',padx=20,pady=(16,4))
    ttk.Label(root,text='选择底盘、填写尺寸，生成独立 .c/.h 文件。接入接口和说明位于项目 docs 目录。').pack(anchor='w',padx=20,pady=(0,10))
    panes=ttk.Panedwindow(root,orient='horizontal');panes.pack(fill='both',expand=True,padx=16)
    left=ttk.Frame(panes);right=ttk.Frame(panes);panes.add(left,weight=3);panes.add(right,weight=2)
    tabs=ttk.Notebook(left);tabs.pack(fill='both',expand=True)
    frames={};variables={};labels={};combo_values={}
    for key,label,default,typ,lo,hi,group,options in PARAMS:
        tabgroup='底盘 / 几何' if group in ('底盘','几何') else '电机 / CAN' if group in ('电机','硬件') else '闭环 / 周期'
        if tabgroup not in frames:
            frames[tabgroup]=ttk.Frame(tabs,padding=12);tabs.add(frames[tabgroup],text=tabgroup)
        f=frames[tabgroup];row=len(f.grid_slaves())//2
        labelwidget=ttk.Label(f,text=label);labelwidget.grid(row=row,column=0,sticky='w',pady=6);labels[key]=labelwidget
        var=tk.StringVar(value=str(default));variables[key]=var
        if options:
            choices=[str(value)+' · '+comment for value,comment in options];combo_values[key]=choices
            widget=ttk.Combobox(f,textvariable=var,values=choices,state='readonly',width=22);var.set(next(x for x in choices if x.split(' · ')[0]==str(default)))
        else:widget=ttk.Entry(f,textvariable=var,width=24)
        widget.grid(row=row,column=1,sticky='ew',padx=(12,0),pady=6);f.columnconfigure(1,weight=1)
    canvas=tk.Canvas(right,width=370,height=280,bg='#f1f5f9',highlightthickness=0);canvas.pack(fill='x',padx=(12,0))
    ttk.Label(right,text='输入车体速度（m/s、rad/s）').pack(anchor='w',padx=12,pady=(12,4))
    speed=ttk.Frame(right);speed.pack(fill='x',padx=12);velocity=[]
    for i,(name,value) in enumerate([('前进 vx','0.2'),('左移 vy','0'),('逆时针 wz','0')]):
        ttk.Label(speed,text=name).grid(row=0,column=i);var=tk.StringVar(value=value);velocity.append(var);ttk.Entry(speed,textvariable=var,width=10).grid(row=1,column=i,padx=4)
    output=tk.Text(right,height=10,width=42,font=('Consolas',11),bg='#0f172a',fg='#e2e8f0',padx=12,pady=12,wrap='word');output.pack(fill='both',expand=True,padx=(12,0),pady=12)
    status=tk.StringVar(value='默认未解锁，PID 为零；预览不会连接或驱动硬件。')
    def collect():
        result={}
        for p in PARAMS:
            s=variables[p[0]].get().split(' · ')[0];result[p[0]]=float(s)
        return validate(result)
    def update():
        try:
            v=collect();rpm,scale=preview(v,*[float(x.get()) for x in velocity]);canvas.delete('all')
            cx,cy=185,145;canvas.create_line(cx,90,cx,30,arrow='last',width=3,fill='#334155');canvas.create_text(cx+45,35,text='车头 +x')
            canvas.create_rectangle(cx-70,cy-65,cx+70,cy+65,outline='#64748b',width=2)
            if v['RCS_LAYOUT']==0:positions=[(-90,-70),(90,-70),(-90,70),(90,70)]
            else:
                n=len(rpm);positions=[(-100*math.sin(math.radians(v['RCS_OMNI_THETA0_DEG'])+2*math.pi*i/n),-100*math.cos(math.radians(v['RCS_OMNI_THETA0_DEG'])+2*math.pi*i/n)) for i in range(n)]
            lines=[]
            for i,((x,y),value) in enumerate(zip(positions,rpm)):
                canvas.create_oval(cx+x-21,cy+y-21,cx+x+21,cy+y+21,fill='#2563eb',outline='');canvas.create_text(cx+x,cy+y,text='W'+str(i),fill='white')
                lines.append(f'W{i} / ID {v[f"RCS_MOTOR_ID_{i}"]}: {value:9.2f} rpm')
                lines.append(f'  转子反馈目标: {value*v[f"RCS_DIRECTION_{i}"]:9.2f}')
            output.delete('1.0','end');output.insert('end','\n'.join(lines)+f'\n\n同比例限速系数: {scale:.4f}')
            status.set('运动学预览完成。全向轮正方向为逆时针切线；麦轮为朝车头滚动。')
        except (ValueError,OverflowError) as e:messagebox.showerror('配置错误',str(e))
    def save():
        try:
            v=collect();path=filedialog.asksaveasfilename(initialdir=ROOT/'examples',defaultextension='.json',filetypes=[('JSON','*.json')])
            if path:Path(path).write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n',encoding='utf-8');status.set('配置已保存：'+path)
        except (ValueError,OSError) as e:messagebox.showerror('保存失败',str(e))
    def open_config():
        path=filedialog.askopenfilename(initialdir=ROOT/'examples',filetypes=[('JSON','*.json')])
        if not path:return
        try:
            v=load(path)
            for key,value in v.items():variables[key].set(next((s for s in combo_values.get(key,[]) if float(s.split(' · ')[0])==value),str(value)))
            update()
        except (ValueError,OSError) as e:messagebox.showerror('读取失败',str(e))
    def write_code():
        try:
            v=collect();folder=filedialog.askdirectory(initialdir=ROOT,title='选择空目录（所有生成文件放在该目录）')
            if folder:export(v,folder);status.set('已生成 3 个 .c 和 4 个 .h：'+folder);messagebox.showinfo('已导出','已生成 7 个独立 .c/.h 文件。接入接口见项目 docs/接入说明.md。')
        except (ValueError,OSError) as e:messagebox.showerror('导出失败',str(e))
    def check_ioc():
        path=filedialog.askopenfilename(filetypes=[('CubeMX','*.ioc')])
        if not path:return
        try:
            report=inspect_ioc(path,collect());output.delete('1.0','end');output.insert('end',json.dumps(report,ensure_ascii=False,indent=2));status.set('完成 .ioc 只读检查，原文件未修改。')
        except (ValueError,OSError) as e:messagebox.showerror('检查失败',str(e))
    actions=ttk.Frame(root);actions.pack(fill='x',padx=16,pady=10)
    for label,cmd in [('计算轮速',update),('读取配置',open_config),('保存配置',save),('检查 .ioc',check_ioc),('生成 .c/.h',write_code)]:ttk.Button(actions,text=label,command=cmd).pack(side='left',padx=4)
    ttk.Label(root,textvariable=status,wraplength=1050).pack(anchor='w',padx=20,pady=(0,14))
    update();root.mainloop()

if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--config');ap.add_argument('--out');ap.add_argument('--ioc');a=ap.parse_args()
    if a.out or a.ioc:
        v=load(a.config) if a.config else validate(DEFAULTS)
        if a.out:print(export(v,a.out))
        if a.ioc:print(json.dumps(inspect_ioc(a.ioc,v),ensure_ascii=False,indent=2))
    else:gui()
