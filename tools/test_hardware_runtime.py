"""实际C#硬件生成器→队内C++核心/适配层编译执行；HAL仅模拟，不连接实车。"""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
import uuid

from build_support import ROOT, compile_csharp

FIXTURES = r'''
using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Reflection;using System.Text;using RcsMap;
static class HardwareRuntimeFixtures {
 static DeviceInstance Make(string kind,int n){var d=HardwareCatalog.Create(HardwareCatalog.Builtins().First(t=>t.DriverKind==kind));d.Id=n.ToString("x32");d.Configured=true;return d;}
 static void Export(string dir,params DeviceInstance[] devices){var d=MapDocument.Demo();d.Version=8;d.Devices=devices.ToList();Directory.CreateDirectory(dir);foreach(var file in HardwareGenerator.Generate(d))File.WriteAllText(Path.Combine(dir,file.Key),file.Value,new UTF8Encoding(false));File.WriteAllText(Path.Combine(dir,"map.json"),d.Json(),new UTF8Encoding(false));}
 static void Main(string[] args){string root=args[0];
  var a=Make("team_cylinder",1);var b=Make("team_cylinder",2);a.Values["TravelMs"]=b.Values["TravelMs"]="20";b.Values["Pin"]="1";Export(Path.Combine(root,"cylinder"),a,b);
  var g=Make("team_jy901",3);var h=Make("team_jy901",4);g.Values["Uart"]="huart1";g.Values["TimeoutMs"]=h.Values["TimeoutMs"]="40";Export(Path.Combine(root,"gyro"),g,h);
  var p=Make("pwm_servo",5);var q=Make("pwm_servo",6);p.Values["SettleMs"]=q.Values["SettleMs"]="20";q.Values["Channel"]="2";Export(Path.Combine(root,"pwm"),p,q);
  var c=new DeviceTemplate{Id="user_test",Name="真实自定义测试",DriverKind="custom",MaxInstances=2,PeriodMs=1};
  c.Includes="#include <stdexcept>\nextern int user_init_count,user_tick_count,user_stop_count,user_enter_count;\nextern int user_throw;";
  c.Declarations="static int ${Prefix}_done;";
  c.Hooks.Init="++user_init_count; ${Prefix}_done=0; return 1;";
  c.Hooks.Service="++user_tick_count; if (user_throw) throw std::runtime_error(\"injected\");";
  c.Hooks.Command="if (strcmp(operation,\"go\")) { return 0; } ++user_enter_count; ${Prefix}_done=1; return 1;";
  c.Hooks.Done="return ${Prefix}_done;";c.Hooks.Stop="++user_stop_count; ${Prefix}_done=0;";
  c.Operations.Add(new DeviceOperation{Id="go",Name="go",Min=0,Max=1});
  var custom=HardwareCatalog.Create(c);custom.Id=7.ToString("x32");custom.Configured=true;Export(Path.Combine(root,"custom"),custom);
  var ci=new DeviceTemplate{Id="user_c_import",Name="导入C驱动",DriverKind="custom",PeriodMs=1};
  ci.Files.Add(new DeviceFile{Path="import_driver.h",Content="#pragma once\nint imported_init(void);\nint imported_set(float);\nint imported_done(void);\nvoid imported_stop(void);\n"});
  ci.Files.Add(new DeviceFile{Path="import_driver.c",Content="#include \"import_driver.h\"\nstatic int state;\nint imported_init(void){state=0;return 1;}\nint imported_set(float v){state=v==1.0f;return state;}\nint imported_done(void){return state;}\nvoid imported_stop(void){state=0;}\n"});
  ci.Includes="#include \"import_driver.h\"";ci.Hooks.Init="return imported_init();";ci.Hooks.Command="return imported_set(value);";ci.Hooks.Done="return imported_done();";ci.Hooks.Stop="imported_stop();";ci.Operations.Add(new DeviceOperation{Id="go",Name="go",Min=0,Max=1});
  var imported=HardwareCatalog.Create(ci);imported.Id=9.ToString("x32");imported.Configured=true;Export(Path.Combine(root,"import_c"),imported);
  var ga=Make("gpio_output",10);ga.Values["Pin"]="5";Export(Path.Combine(root,"gpio"),ga);
  var ma=Make("team_cylinder",11);ma.Values["TravelMs"]="20";var mg=Make("team_jy901",12);mg.Values["Uart"]="huart1";mg.Values["Required"]="0";var mp=Make("pwm_servo",13);mp.Values["SettleMs"]="20";Export(Path.Combine(root,"mixed"),ma,mg,mp);
  var off=Make("team_cylinder",8);off.Enabled=false;Export(Path.Combine(root,"disabled"),off);
  Console.WriteLine("PASS fixtures emitted by actual HardwareGenerator");
 }
}
'''

HAL = r'''
#pragma once
#include <stdint.h>
#include <stddef.h>
#define HAL_GPIO_MODULE_ENABLED
#define HAL_UART_MODULE_ENABLED
#define HAL_TIM_MODULE_ENABLED
#define RCS_NO_CHASSIS 1
typedef enum {RCS_OK=0} rcs_fault;
typedef struct {void *Instance;} CAN_HandleTypeDef;
typedef struct {uint32_t StdId,IDE,RTR,DLC;} CAN_RxHeaderTypeDef;
typedef struct {uint32_t MODER,ODR;} GPIO_TypeDef;
typedef enum {GPIO_PIN_RESET=0,GPIO_PIN_SET=1} GPIO_PinState;
extern GPIO_TypeDef test_gpio[9];
#define GPIOA (&test_gpio[0])
#define GPIOB (&test_gpio[1])
#define GPIOC (&test_gpio[2])
#define GPIOD (&test_gpio[3])
#define GPIOE (&test_gpio[4])
#define GPIOF (&test_gpio[5])
#define GPIOG (&test_gpio[6])
#define GPIOH (&test_gpio[7])
#define GPIOI (&test_gpio[8])
typedef struct {struct {uint32_t BaudRate;} Init; unsigned number;} UART_HandleTypeDef;
typedef struct {uint32_t ARR,CCR[4],active[4];} TIM_TypeDef;
extern TIM_TypeDef test_timers[14];
#define TIM1 (&test_timers[0])
#define TIM8 (&test_timers[7])
#define TIM9 (&test_timers[8])
#define TIM10 (&test_timers[9])
#define TIM11 (&test_timers[10])
typedef struct {TIM_TypeDef *Instance;struct {uint32_t Prescaler;} Init;} TIM_HandleTypeDef;
typedef struct {uint32_t CFGR;} RCC_TypeDef;
extern RCC_TypeDef test_rcc;
#define RCC (&test_rcc)
#define RCC_CFGR_PPRE1 7u
#define RCC_CFGR_PPRE2 56u
#define HAL_OK 0
#define HAL_ERROR 1
#define __HAL_TIM_GET_AUTORELOAD(t) ((t)->Instance->ARR)
#define __HAL_TIM_SET_COMPARE(t,c,v) ((t)->Instance->CCR[(c)/4u]=(v))
#ifdef __cplusplus
extern "C" {
#endif
extern UART_HandleTypeDef huart1,huart3;
extern TIM_HandleTypeDef htim3;
uint32_t HAL_GetTick(void);
uint32_t __get_PRIMASK(void);
void __disable_irq(void);
void __set_PRIMASK(uint32_t);
void HAL_GPIO_WritePin(GPIO_TypeDef*,uint16_t,GPIO_PinState);
int HAL_UART_Receive_IT(UART_HandleTypeDef*,uint8_t*,uint16_t);
int HAL_UART_AbortReceive(UART_HandleTypeDef*);
int HAL_TIM_PWM_Start(TIM_HandleTypeDef*,uint32_t);
int HAL_TIM_PWM_Stop(TIM_HandleTypeDef*,uint32_t);
uint32_t HAL_RCC_GetPCLK1Freq(void);
uint32_t HAL_RCC_GetPCLK2Freq(void);
void HAL_UART_RxCpltCallback(UART_HandleTypeDef*);
void HAL_UART_ErrorCallback(UART_HandleTypeDef*);
#ifdef __cplusplus
}
#endif
'''

NAV = r'''
#pragma once
#include <stdint.h>
typedef struct {float x_m,y_m,yaw_rad,age_s,quality;uint32_t sequence;uint8_t axes;} rcs_observation;
'''

COMMON = r'''
#include "rcs_hw.h"
#include <stdio.h>
#include <stdlib.h>
#include <math.h>
#include <string.h>
GPIO_TypeDef test_gpio[9];TIM_TypeDef test_timers[14];RCC_TypeDef test_rcc;
extern "C" { UART_HandleTypeDef huart1={{115200},0},huart3={{115200},2};TIM_HandleTypeDef htim3={&test_timers[2],{7}}; }
static uint32_t tick,mask;static uint8_t *rx[6];static int receive_fail,abort_count,writes;
int user_init_count,user_tick_count,user_stop_count,user_enter_count,user_throw;
static unsigned checks;
#define C(x) do { ++checks; if(!(x)){fprintf(stderr,"FAIL line %d: %s\n",__LINE__,#x);exit(1);} }while(0)
extern "C" uint32_t HAL_GetTick(void){return tick;}
extern "C" uint32_t __get_PRIMASK(void){return mask;}
extern "C" void __disable_irq(void){mask=1;}
extern "C" void __set_PRIMASK(uint32_t m){mask=m;}
extern "C" void HAL_GPIO_WritePin(GPIO_TypeDef*p,uint16_t pin,GPIO_PinState s){++writes;if(s)p->ODR|=pin;else p->ODR&=~(uint32_t)pin;}
extern "C" int HAL_UART_Receive_IT(UART_HandleTypeDef*u,uint8_t*p,uint16_t n){if(receive_fail||n!=1)return HAL_ERROR;rx[u->number]=p;return HAL_OK;}
extern "C" int HAL_UART_AbortReceive(UART_HandleTypeDef*u){rx[u->number]=NULL;abort_count++;return HAL_OK;}
extern "C" int HAL_TIM_PWM_Start(TIM_HandleTypeDef*t,uint32_t c){if(t->Instance->active[c/4u])return HAL_ERROR;t->Instance->active[c/4u]=1;return HAL_OK;}
extern "C" int HAL_TIM_PWM_Stop(TIM_HandleTypeDef*t,uint32_t c){t->Instance->active[c/4u]=0;return HAL_OK;}
extern "C" uint32_t HAL_RCC_GetPCLK1Freq(void){return 8000000;}
extern "C" uint32_t HAL_RCC_GetPCLK2Freq(void){return 8000000;}
static const char *id1="00000000000000000000000000000001",*id2="00000000000000000000000000000002";
static void setup(void){(void)id1;(void)id2;for(unsigned i=0;i<9;i++)test_gpio[i].MODER=0x55555555u;htim3.Instance->ARR=19999;}
static inline void step(unsigned ms){for(unsigned i=0;i<ms;i++){++tick;rcs_hw_service(tick);}}
'''

CYLINDER = r'''
int main(void){setup();C(rcs_hw_init());C(rcs_hw_fault()==0);int before=writes;step(20);C(writes==before);C(!rcs_hw_command(id1,"push",1));rcs_hw_set_enabled(1);C(!rcs_hw_done(id1));C(rcs_hw_command(id1,"push",1));C(!rcs_hw_done(id1));step(35);C(rcs_hw_done(id1));C((GPIOA->ODR&1u)!=0);C((GPIOA->ODR&2u)==0);C(rcs_hw_command(id1,"pull",0));C(!rcs_hw_done(id1));step(35);C(rcs_hw_done(id1));rcs_hw_cancel(id1);C(!rcs_hw_done(id1));C(rcs_hw_command(id2,"push",1));step(35);C((GPIOA->ODR&2u)!=0&&!(GPIOA->ODR&1u));rcs_hw_stop();C((GPIOA->ODR&3u)==0);C(!rcs_hw_command(id1,"push",1));step(20);C((GPIOA->ODR&3u)==0);for(int i=0;i<50;i++){C(rcs_hw_init());rcs_hw_set_enabled(1);C(rcs_hw_command(id1,"push",1));step(35);C(rcs_hw_done(id1));rcs_hw_stop();}C(rcs_hw_fault()==0);printf("PASS %u real team cylinder adapter assertions\n",checks);}
'''

GYRO = r'''
#include <vector>
#define private public
#include "rcs_gyro.h"
#undef private
static const char *id3="00000000000000000000000000000003",*id4="00000000000000000000000000000004";
static void byte(UART_HandleTypeDef*u,uint8_t b){C(rx[u->number]!=NULL);*rx[u->number]=b;HAL_UART_RxCpltCallback(u);}
static void frame(UART_HandleTypeDef*u,int16_t angle,int bad){uint8_t b[11]={0x55,0x53,0,0,0,0,(uint8_t)angle,(uint8_t)((uint16_t)angle>>8),0,0,0};for(unsigned i=0;i<10;i++)b[10]=(uint8_t)(b[10]+b[i]);if(bad)b[10]^=1;for(unsigned i=0;i<11;i++)byte(u,b[i]);}
int main(void){setup();C(rcs_hw_init());C(!rcs_hw_ready());rcs_observation a,b;C(!rcs_hw_read_observation(id3,&a));frame(&huart1,16384,0);C(rcs_hw_read_observation(id3,&a));C(a.axes==4&&a.sequence==1&&fabsf(a.yaw_rad-1.57079632679f)<.001f);C(!rcs_hw_read_observation(id4,&b));C(!rcs_hw_ready());frame(&huart3,-16384,0);C(rcs_hw_read_observation(id4,&b)&&b.sequence==1&&b.yaw_rad<0);C(rcs_hw_ready());frame(&huart1,0,1);C(rcs_hw_read_observation(id3,&a)&&a.sequence==1);byte(&huart1,0x55);byte(&huart1,0x52);for(int i=0;i<9;i++)byte(&huart1,0);frame(&huart1,0,0);C(rcs_hw_read_observation(id3,&a)&&a.sequence==2&&fabsf(a.yaw_rad)<.001f);rcs_jy901::gyro_list[0]->alpha_sequence=UINT32_MAX;frame(&huart1,8192,0);C(rcs_hw_read_observation(id3,&a)&&a.sequence==0);tick=UINT32_MAX-5;frame(&huart1,0,0);tick=3;C(rcs_hw_read_observation(id3,&a)&&fabsf(a.age_s-.009f)<.001f);tick=100;C(!rcs_hw_read_observation(id3,&a));C(!rcs_hw_ready());
uint8_t burst[320]={0};burst[309]=0x55;burst[310]=0x53;uint8_t sum=0;for(unsigned i=309;i<319;i++)sum=(uint8_t)(sum+burst[i]);burst[319]=sum;rcs_hw_on_uart_rx(&huart1,burst,sizeof(burst));C(rcs_hw_read_observation(id3,&a));for(int i=0;i<100;i++){C(rcs_hw_init());C(rcs_jy901::gyro_list.size()==2);C(!rcs_hw_read_observation(id3,&a));frame(&huart1,0,0);C(rcs_hw_read_observation(id3,&a)&&a.sequence==1);}C(abort_count>=200);rcs_hw_set_enabled(1);step(46);C(rcs_hw_fault()==RCS_HW_TIMEOUT);C(!rcs_hw_command(id3,"anything",0));C(rcs_hw_init());frame(&huart1,0,0);receive_fail=1;byte(&huart1,0x55);C(rcs_hw_fault()==RCS_HW_UART);printf("PASS %u real team gyro/checksum/replay/wrap/reinit assertions\n",checks);}
'''

PWM = r'''
int main(void){setup();C(rcs_hw_init());C(htim3.Instance->CCR[0]==0&&!htim3.Instance->active[0]);rcs_hw_set_enabled(1);const char*a="00000000000000000000000000000005",*b="00000000000000000000000000000006";C(rcs_hw_command(a,"angle",90));C(htim3.Instance->CCR[0]==1500&&htim3.Instance->active[0]);C(!rcs_hw_done(a));step(20);C(rcs_hw_done(a));C(rcs_hw_command(a,"angle",180));C(htim3.Instance->CCR[0]==2500);C(!rcs_hw_done(a));C(rcs_hw_command(b,"angle",0));C(htim3.Instance->CCR[1]==500);C(!rcs_hw_command(a,"angle",181));rcs_hw_cancel(a);C(!htim3.Instance->active[0]&&htim3.Instance->active[1]);rcs_hw_stop();C(!htim3.Instance->active[1]);C(rcs_hw_fault()==0);htim3.Init.Prescaler=3;C(!rcs_hw_init()&&rcs_hw_fault()==RCS_HW_CONFIG);printf("PASS %u PWM clock/channel/stop assertions\n",checks);}
'''

CUSTOM = r'''
int main(void){setup();const char *id="00000000000000000000000000000007";C(rcs_hw_init());C(user_init_count==1);C(!rcs_hw_command(id,"go",0));step(3);C(user_tick_count==3);rcs_hw_set_enabled(1);C(rcs_hw_command(id,"go",1));C(rcs_hw_done(id));rcs_hw_cancel(id);C(user_stop_count==1);rcs_hw_stop();C(user_stop_count==1);step(2);C(user_tick_count==5);C(rcs_hw_init());C(user_init_count==2);user_throw=1;step(1);C(rcs_hw_fault()==RCS_HW_EXCEPTION);user_throw=0;C(rcs_hw_init());C(rcs_hw_fault()==0);rcs_hw_set_enabled(1);C(rcs_hw_command(id,"go",1));C(rcs_hw_done(id));int before_stop=user_stop_count;C(!rcs_hw_require_command(id,"not_declared",1));C(rcs_hw_fault()==RCS_HW_COMMAND);C(!rcs_hw_done(id)&&user_stop_count==before_stop+1);printf("PASS %u custom lifecycle/C ABI exception assertions\n",checks);}
'''

IMPORT_C = r'''
int main(void){setup();const char *id="00000000000000000000000000000009";C(rcs_hw_init());rcs_hw_set_enabled(1);C(rcs_hw_command(id,"go",1));C(rcs_hw_done(id));rcs_hw_cancel(id);C(!rcs_hw_done(id));printf("PASS %u imported C/H C ABI assertions\n",checks);}
'''
GPIO = r'''
int main(void){setup();const char*id="0000000000000000000000000000000a";C(rcs_hw_init());C(!(GPIOA->ODR&32u));rcs_hw_set_enabled(1);C(rcs_hw_command(id,"write",1));C((GPIOA->ODR&32u)&&rcs_hw_done(id));C(!rcs_hw_command(id,"write",2));rcs_hw_stop();C(!(GPIOA->ODR&32u));C(!rcs_hw_command(id,"write",1));printf("PASS %u GPIO command and stop assertions\n",checks);}
'''
MIXED = r'''
int main(void){setup();C(rcs_hw_init());C(rcs_hw_ready());rcs_hw_set_enabled(1);const char *a="0000000000000000000000000000000b",*b="0000000000000000000000000000000d";C(rcs_hw_command(a,"push",1));C(rcs_hw_command(b,"angle",90));step(35);C(rcs_hw_done(a)&&rcs_hw_done(b));C((GPIOA->ODR&1u)&&htim3.Instance->CCR[0]==1500);rcs_hw_stop();C(!(GPIOA->ODR&1u)&&!htim3.Instance->active[0]);C(rcs_hw_init());C(rcs_hw_fault()==0);printf("PASS %u mixed team/PWM lifecycle assertions\n",checks);}
'''
DISABLED = r'''
int main(void){setup();C(rcs_hw_init());rcs_hw_set_enabled(1);C(!rcs_hw_command(id1,"push",1));step(100);C(rcs_hw_fault()==0);rcs_hw_stop();printf("PASS %u disabled-hardware stub assertions\n",checks);}
'''


def run(args):
    p = subprocess.run(list(map(str, args)), capture_output=True, cwd=ROOT, timeout=120)
    if p.returncode:
        raise RuntimeError(" ".join(map(str,args)) + "\n" + (p.stdout+p.stderr).decode("utf-8", "replace"))
    return p.stdout.decode("utf-8", "replace").strip()


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    area = ROOT / "build" / ("hardware_runtime_" + uuid.uuid4().hex[:8])
    area.mkdir(parents=True)
    source = area / "Fixtures.cs"
    source.write_text(FIXTURES, encoding="utf-8")
    resources = {"assets/hardware/" + p.relative_to(ROOT / "map_editor/hardware").as_posix(): p for p in (ROOT / "map_editor/hardware").rglob("*") if p.is_file()}
    resources.update({"assets/templates/"+p.relative_to(ROOT/"map_editor/templates").as_posix():p for p in (ROOT/"map_editor/templates").rglob("*.tpl")})
    resources.update({"assets/"+p.name:p for p in (ROOT/"map_editor/firmware").iterdir() if p.suffix in (".h",".c")})
    sources = [p for p in (ROOT / "map_editor/src").glob("*.cs") if p.name != "Program.cs"]
    sources += [ROOT/"keil_headers"/name for name in ["ProjectSetup.cs","ProjectBinding.cs","KeilHeaders.cs"]]
    sources.append(source)
    exe = area / "fixtures.exe"
    output = compile_csharp(exe, sources, defines=["RCS_MAP_EDITOR"], references=["System.Xml.Linq.dll","System.Xml.dll","System.Windows.Forms.dll","System.Drawing.dll","System.Core.dll","System.IO.Compression.dll","System.IO.Compression.FileSystem.dll","System.Runtime.Serialization.dll"], resources=resources)
    assert not output.strip(), output
    print(run([exe, area]), flush=True)
    reports = []
    for name, body in [("cylinder",CYLINDER),("gyro",GYRO),("pwm",PWM),("custom",CUSTOM),("disabled",DISABLED),("import_c",IMPORT_C),("gpio",GPIO),("mixed",MIXED)]:
        generated = area / name
        stub = generated / "stub"
        stub.mkdir()
        (stub/"rcs_hal.h").write_text(HAL,encoding="utf-8")
        (stub/"rcs_nav.h").write_text(NAV,encoding="utf-8")
        (stub/"rcs_can_port.h").write_text('#pragma once\n#include "rcs_hal.h"\n',encoding="utf-8")
        harness = generated / "harness.cpp"
        harness.write_text(COMMON+body,encoding="utf-8")
        objects = []
        flags=["-Wall","-Wextra","-Werror","-O2","-I",stub,"-I",generated]
        for p in sorted(generated.glob("*.c")):
            obj=p.with_suffix(".o")
            run(["gcc","-std=c99",*flags,"-c",p,"-o",obj]);objects.append(obj)
        target=generated/"runtime.exe"
        run(["g++","-std=c++17",*flags,*sorted(generated.glob("*.cpp")),*objects,"-lm","-o",target])
        text=run([target]);print(text,flush=True)
        reports.append({"case":name,"report":text,"files_sha256":{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in generated.iterdir() if p.suffix in (".c",".cpp",".h")}})
    evidence={"passed":True,"hardware_test":False,"scope":"actual C# hardware generator and real team C++ cores with HAL simulation","cases":reports,"area":str(area),"source_sha256":{str(p.relative_to(ROOT)).replace("\\","/"):hashlib.sha256(p.read_bytes()).hexdigest() for p in [Path(__file__),ROOT/"map_editor/src/HardwareGenerator.cs",*(ROOT/"map_editor/hardware").rglob("*")] if p.is_file()}}
    (ROOT/"build/hardware_runtime_validation.json").write_text(json.dumps(evidence,ensure_ascii=False,indent=2),encoding="utf-8")


if __name__ == "__main__":
    main()
