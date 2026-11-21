"""实际生成App/Machism/Nav及队内C++硬件的整应用验证；HAL/底盘执行层模拟，不连接硬件。"""
from pathlib import Path
import hashlib
import json
import subprocess
import sys
import uuid

from build_support import ROOT, compile_csharp
from settings import DEFAULTS, config_header
import test_hardware_runtime as runtime


FIXTURES = r'''
using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Text;using RcsMap;
static class HardwareAppFixtures {
 static DeviceInstance Team(string kind,int id){var d=HardwareCatalog.Create(HardwareCatalog.Builtins().First(t=>t.DriverKind==kind));d.Id=id.ToString("x32");d.Configured=true;return d;}
 static DeviceInstance Custom(){
  var t=new DeviceTemplate{Id="user_app_driver",Name="真实整应用驱动",DriverKind="custom",MaxInstances=1,PeriodMs=1};
  t.Includes="extern int user_init_count,user_tick_count,user_stop_count,user_enter_count,user_reject_count,user_delay_ms;\n";
  t.Declarations="static uint32_t ${Prefix}_started;\nstatic int ${Prefix}_done;";
  t.Hooks.Init="++user_init_count; ${Prefix}_started=0; ${Prefix}_done=0; return 1;";
  t.Hooks.Service="++user_tick_count; if (${Prefix}_started && (uint32_t)(now_ms-${Prefix}_started)>=(uint32_t)user_delay_ms) ${Prefix}_done=1;";
  t.Hooks.Command="if(strcmp(operation,\"go\") || value!=1.0f) { ++user_reject_count; return 0; } ++user_enter_count; ${Prefix}_started=HAL_GetTick(); ${Prefix}_done=user_delay_ms==0; return 1;";
  t.Hooks.Done="return ${Prefix}_done;";
  t.Hooks.Stop="++user_stop_count; ${Prefix}_started=0; ${Prefix}_done=0;";
  t.Operations.Add(new DeviceOperation{Id="go",Name="发送目标",DefaultValue=1,Min=0,Max=1});
  var d=HardwareCatalog.Create(t);d.Id=1.ToString("x32");d.Configured=true;return d;
 }
 static MapDocument Make(string scenario){
  var d=MapDocument.Demo();d.Version=8;d.Obstacles.Clear();d.Points=d.Points.Take(2).ToList();foreach(var p in d.Points){p.ArrivalActions.Clear();p.DepartureActions.Clear();}
  d.Application=new ApplicationSettings{Enabled=true,AutoStart=scenario=="required_ready"||scenario=="reinit",StartAtP0=false,BootDelayMs=0};
  var action=d.Application.States[0];action.Kind=0;action.Timeout=2;
  var custom=Custom();d.Devices=new List<DeviceInstance>{custom};
  d.Application.Includes="extern int user_cancel_count;";
  if(scenario=="cylinder"||scenario=="cylinder_stop"){
   var cylinder=Team("team_cylinder",2);cylinder.Values["TravelMs"]="20";d.Devices.Add(cylinder);HardwareOperations.BindState(d,action,cylinder,cylinder.Template.Operations.First(o=>o.Id=="push"),1);
  }else if(scenario=="disabled"){
   custom.Enabled=false;action.Kind=2;action.WaitSeconds=.03;action.Timeout=1;
  }else HardwareOperations.BindState(d,action,custom,custom.Template.Operations.First(),1);
  action.Cancel="++user_cancel_count;\n"+action.Cancel;
  if(scenario=="reject_after_done"||scenario=="gyro_fault"){
   var rejected=new MachineState{Name="拒绝第二命令",Kind=0,Timeout=1};HardwareOperations.BindState(d,rejected,custom,custom.Template.Operations.First(),0);rejected.Cancel="++user_cancel_count;\n"+rejected.Cancel;
   rejected.Transitions.Add(new MachineTransition{TargetId=d.Application.States[1].Id});action.Transitions[0].TargetId=rejected.Id;d.Application.States.Add(rejected);
  }
  if(scenario.StartsWith("gyro_")||scenario=="required_ready"){
   var gyro=Team("team_jy901",3);gyro.Values["Uart"]="huart1";gyro.Values["TimeoutMs"]="100";gyro.Values["Required"]=scenario=="required_ready"?"1":"0";d.Devices.Add(gyro);
  }
  return d;
 }
 static void Main(string[] args){
  string[] scenarios={"complete","stop","reinit","reject_after_done","cylinder","cylinder_stop","gyro_stopped","gyro_finished","gyro_fault","required_ready","disabled"};
  foreach(string scenario in scenarios){var d=Make(scenario);string folder=Path.Combine(args[0],scenario);Directory.CreateDirectory(folder);foreach(var f in Exporter.Generate(d))File.WriteAllText(Path.Combine(folder,f.Key),f.Value,new UTF8Encoding(false));foreach(var f in HardwareGenerator.Generate(d))File.WriteAllText(Path.Combine(folder,f.Key),f.Value,new UTF8Encoding(false));File.WriteAllText(Path.Combine(folder,"map.json"),d.Json(),new UTF8Encoding(false));}
  var pending=Make("complete");pending.Devices[0].Configured=false;bool rejectedPending=false;try{Exporter.Generate(pending);}catch{rejectedPending=true;}if(!rejectedPending)throw new Exception("unconfigured device was accepted");
  var missing=Make("complete");missing.Devices.Clear();bool rejectedMissing=false;try{Exporter.Generate(missing);}catch{rejectedMissing=true;}if(!rejectedMissing)throw new Exception("referenced missing device was accepted");
  Console.WriteLine("PASS actual app/hardware fixtures; unconfigured and missing referenced devices refused");
 }
}
'''


HAL_DECLARATIONS = r'''
#ifdef __cplusplus
extern "C" {
#endif
int RCS_Init(CAN_HandleTypeDef*);
int RCS_Arm(void);
void RCS_Lock(void);
int RCS_SetVelocity(float,float,float);
void RCS_Process(void);
rcs_state RCS_State(void);
rcs_fault RCS_Fault(void);
void RCS_OnCanRx(CAN_HandleTypeDef*,const CAN_RxHeaderTypeDef*,const uint8_t[8]);
#ifdef __cplusplus
}
#endif
'''


APP = r'''
static rcs_state chassis_state=RCS_LOCKED;
static rcs_fault chassis_fault=RCS_OK;
static int chassis_init,processes,arms,chassis_commands;
int user_cancel_count;
extern "C" int RCS_Init(CAN_HandleTypeDef *can){(void)can;++chassis_init;chassis_state=RCS_LOCKED;chassis_fault=RCS_OK;return 1;}
extern "C" int RCS_Arm(void){++arms;return 0;}
extern "C" void RCS_Lock(void){chassis_state=RCS_LOCKED;}
extern "C" int RCS_SetVelocity(float x,float y,float z){(void)x;(void)y;(void)z;++chassis_commands;return 0;}
extern "C" void RCS_Process(void){++processes;}
extern "C" rcs_state RCS_State(void){return chassis_state;}
extern "C" rcs_fault RCS_Fault(void){return chassis_fault;}
extern "C" void RCS_OnCanRx(CAN_HandleTypeDef*c,const CAN_RxHeaderTypeDef*h,const uint8_t d[8]){(void)c;(void)h;(void)d;}
int user_reject_count,user_delay_ms;
#undef C
#define C(x) do { ++checks; if(!(x)){fprintf(stderr,"FAIL line %d: %s app=%d mach=%d init=%d enter=%d reject=%d stop=%d cancel=%d tick=%u hw=%d\n",__LINE__,#x,App_task_State(),Machism_State(),user_init_count,user_enter_count,user_reject_count,user_stop_count,user_cancel_count,tick,rcs_hw_fault());exit(1);} }while(0)
static const char *custom_id="00000000000000000000000000000001";
static const char *cylinder_id="00000000000000000000000000000002";
static const char *gyro_id="00000000000000000000000000000003";
#if TEST_WITH_GYRO
#include <vector>
#define private public
#include "rcs_gyro.h"
#undef private
static void byte(uint8_t value){C(rx[huart1.number]!=NULL);*rx[huart1.number]=value;HAL_UART_RxCpltCallback(&huart1);}
static void frame(int16_t angle){uint8_t b[11]={0x55,0x53,0,0,0,0,(uint8_t)angle,(uint8_t)((uint16_t)angle>>8),0,0,0};for(unsigned i=0;i<10;i++)b[10]=(uint8_t)(b[10]+b[i]);for(unsigned i=0;i<11;i++)byte(b[i]);}
static uint32_t sequence(void){C(rcs_jy901::gyro_list.size()==1);return rcs_jy901::gyro_list[0]->alpha_sequence;}
#endif
static void until_started(void){for(unsigned i=0;i<100&&user_enter_count==0;i++){step(1);}C(user_enter_count==1);}
static void check_safe(void){C(arms==0&&chassis_commands==0&&chassis_state==RCS_LOCKED);}
int main(int argc,char **argv){
 (void)argc;const char *scenario=argv[1];(void)custom_id;(void)cylinder_id;(void)gyro_id;
 setup();user_delay_ms=5;
 if(!strcmp(scenario,"stop")||!strcmp(scenario,"reinit")||!strcmp(scenario,"gyro_stopped"))user_delay_ms=200;
 if(!strcmp(scenario,"reject_after_done")||!strcmp(scenario,"gyro_fault"))user_delay_ms=0;
 C(App_Init());C(chassis_init==1);C(App_task_State()==RCS_APP_READY);
 if(!strcmp(scenario,"disabled")){
  C(user_init_count==0);C(App_Start());C(App_task_State()==RCS_APP_RUNNING);C(!rcs_hw_command(custom_id,"go",1));C(!rcs_hw_command("missing","go",1));step(80);C(App_task_State()==RCS_APP_FINISHED);check_safe();puts("PASS disabled and missing hardware never pretend command success");return 0;
 }
 C(user_init_count==1);step(9);C(user_tick_count==9&&user_enter_count==0);C(App_task_State()==RCS_APP_READY);
#if TEST_WITH_GYRO
 C(rx[huart1.number]!=NULL);
 if(!strcmp(scenario,"required_ready")){
  step(91);C(App_task_State()==RCS_APP_READY&&user_enter_count==0&&rcs_hw_fault()==RCS_HW_OK);frame(16384);step(10);C(App_task_State()==RCS_APP_RUNNING);for(unsigned i=0;i<100&&App_task_State()==RCS_APP_RUNNING;i++){if(i%20==0)frame(16384);step(1);}C(user_enter_count==1&&App_task_State()==RCS_APP_FINISHED);check_safe();puts("PASS required JY901 readiness blocks startup until a valid real protocol frame");return 0;
 }
 frame(8192);rcs_observation sample;C(rcs_hw_read_observation(gyro_id,&sample)&&sample.sequence==1);
#endif
 C(App_Start());C(App_task_State()==RCS_APP_RUNNING);
 if(!strcmp(scenario,"cylinder")||!strcmp(scenario,"cylinder_stop")){
  for(unsigned i=0;i<60&&!(GPIOA->ODR&1u);i++){step(1);}C((GPIOA->ODR&1u)!=0);
  if(!strcmp(scenario,"cylinder_stop")){App_Stop();C(App_task_State()==RCS_APP_STOPPED);step(100);C(!(GPIOA->ODR&1u));}
  else{for(unsigned i=0;i<100&&App_task_State()==RCS_APP_RUNNING;i++){step(1);}C(App_task_State()==RCS_APP_FINISHED&&!(GPIOA->ODR&1u));}
  C(user_tick_count==(int)tick);check_safe();printf("PASS actual team cylinder App/Machism command, %s and safe output\n",scenario);return 0;
 }
 until_started();
 if(!strcmp(scenario,"reinit")){
  C(App_Init());C(user_cancel_count==1&&user_stop_count==1&&user_init_count==2);int before=user_tick_count;step(100);C(App_task_State()==RCS_APP_READY&&user_enter_count==1&&user_tick_count==before+100);C(user_cancel_count==1&&user_stop_count==1);check_safe();puts("PASS App reinit cancels active hardware/state exactly once and never autostarts again");return 0;
 }
 if(!strcmp(scenario,"stop")||!strcmp(scenario,"gyro_stopped")){
  App_Stop();C(App_task_State()==RCS_APP_STOPPED&&user_cancel_count==1&&user_stop_count==1);int before=user_tick_count;step(100);C(user_tick_count==before+100&&user_enter_count==1&&user_cancel_count==1&&user_stop_count==1);C(!rcs_hw_command(custom_id,"go",1));
#if TEST_WITH_GYRO
  uint32_t old=sequence();frame(16384);C(sequence()==old+1&&rcs_hw_read_observation(gyro_id,&sample));
#endif
  check_safe();puts("PASS App Stop preserves per-loop maintenance and UART sampling while forbidding motion/replay");return 0;
 }
 if(!strcmp(scenario,"reject_after_done")||!strcmp(scenario,"gyro_fault")){
  for(unsigned i=0;i<100&&App_task_State()!=RCS_APP_FAULT;i++){step(1);}C(App_task_State()==RCS_APP_FAULT);C(App_task_Fault()==RCS_APP_ERROR_HARDWARE&&rcs_hw_fault()==RCS_HW_COMMAND);C(user_enter_count==1&&user_reject_count==1&&user_stop_count==1);C(Machism_State()!=MACHISM_DONE);uint32_t first=App_task_FaultTick();step(10);C(user_cancel_count==1);int before=user_tick_count;step(100);C(App_task_State()==RCS_APP_FAULT&&App_task_FaultTick()==first&&user_tick_count==before+100);C(user_enter_count==1&&user_reject_count==1&&user_stop_count==1);
#if TEST_WITH_GYRO
  uint32_t old=sequence();frame(0);C(sequence()==old+1);C(!rcs_hw_read_observation(gyro_id,&sample));
#endif
  check_safe();puts("PASS rejected command after old DONE locks hardware App fault; faulted service/UART continue safely");return 0;
 }
 for(unsigned i=0;i<100&&App_task_State()==RCS_APP_RUNNING;i++){step(1);}C(App_task_State()==RCS_APP_FINISHED);C(user_enter_count==1&&user_stop_count==1&&user_cancel_count==0);C(App_task_Fault()==RCS_APP_ERROR_NONE);int before=user_tick_count;step(100);C(user_tick_count==before+100&&user_enter_count==1&&user_stop_count==1);C(!rcs_hw_command(custom_id,"go",1));
#if TEST_WITH_GYRO
 uint32_t old=sequence();frame(-16384);C(sequence()==old+1&&rcs_hw_read_observation(gyro_id,&sample));
#endif
 check_safe();puts("PASS complete app initializes custom driver and executes GUI-bound state once; FINISHED keeps maintenance");return 0;
}
'''


def run(args):
    result = subprocess.run(list(map(str, args)), cwd=ROOT, capture_output=True, timeout=120)
    if result.returncode:
        raise RuntimeError(" ".join(map(str, args)) + "\n" + (result.stdout + result.stderr).decode("utf-8", "replace"))
    return result.stdout.decode("utf-8", "replace").strip()


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    area = ROOT / "build" / ("hardware_app_" + uuid.uuid4().hex[:8])
    area.mkdir(parents=True)
    source = area / "Fixtures.cs"
    source.write_text(FIXTURES, encoding="utf-8")
    resources = {"assets/hardware/" + p.relative_to(ROOT / "map_editor/hardware").as_posix(): p for p in (ROOT / "map_editor/hardware").rglob("*") if p.is_file()}
    resources.update({"assets/templates/" + p.relative_to(ROOT / "map_editor/templates").as_posix(): p for p in (ROOT / "map_editor/templates").rglob("*.tpl")})
    resources.update({"assets/" + p.name: p for p in (ROOT / "map_editor/firmware").iterdir() if p.suffix in (".c", ".h")})
    sources = [p for p in (ROOT / "map_editor/src").glob("*.cs") if p.name != "Program.cs"]
    sources += [ROOT / "keil_headers" / name for name in ["ProjectSetup.cs", "ProjectBinding.cs", "KeilHeaders.cs"]]
    sources.append(source)
    fixture_exe = area / "fixtures.exe"
    output = compile_csharp(fixture_exe, sources, defines=["RCS_MAP_EDITOR"], references=["System.Xml.Linq.dll", "System.Xml.dll", "System.Windows.Forms.dll", "System.Drawing.dll", "System.Core.dll", "System.IO.Compression.dll", "System.IO.Compression.FileSystem.dll", "System.Runtime.Serialization.dll"], resources=resources)
    assert not output.strip(), output
    print(run([fixture_exe, area]), flush=True)
    hal = runtime.HAL.replace("typedef enum {RCS_OK=0} rcs_fault;", '#include "rcs_control.h"') + HAL_DECLARATIONS
    common = runtime.COMMON.replace("++tick;rcs_hw_service(tick);", "++tick;App_Task();")
    common = '#include "app.h"\n#include "machism.h"\n' + common
    reports = []
    for name in ["complete", "stop", "reinit", "reject_after_done", "cylinder", "cylinder_stop", "gyro_stopped", "gyro_finished", "gyro_fault", "required_ready", "disabled"]:
        generated = area / name
        stub = generated / "stub"
        stub.mkdir()
        (stub / "rcs_hal.h").write_text(hal, encoding="utf-8")
        (stub / "rcs_config.h").write_text(config_header(dict(DEFAULTS, RCS_KP=1)), encoding="utf-8")
        generated_names = [p.name for p in generated.iterdir() if p.suffix in (".c", ".cpp", ".h")]
        harness = generated / "harness.cpp"
        harness.write_text(common + APP, encoding="utf-8")
        gyro = name.startswith("gyro_") or name == "required_ready"
        flags = ["-Wall", "-Wextra", "-Werror", "-O2", "-DTEST_WITH_GYRO=" + str(int(gyro)), "-I", stub, "-I", generated, "-I", ROOT / "firmware"]
        objects = []
        for p in [*sorted(generated.glob("*.c")), ROOT / "firmware/rcs_chassis.c"]:
            obj = generated / (p.stem + ".o")
            run(["gcc", "-std=c99", *flags, "-c", p, "-o", obj])
            objects.append(obj)
        target = generated / "app.exe"
        run(["g++", "-std=c++17", *flags, *sorted(generated.glob("*.cpp")), *objects, "-lm", "-o", target])
        text = run([target, name])
        print(name + ": " + text, flush=True)
        reports.append({"case": name, "report": text, "generated_sha256": {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in (generated / n for n in generated_names)}, "harness_sha256": hashlib.sha256(harness.read_bytes()).hexdigest(), "map_sha256": hashlib.sha256((generated / "map.json").read_bytes()).hexdigest()})
    evidence = {"passed": True, "hardware_test": False, "scope": "actual App/Machism/Nav/hardware generation and real team cylinder/JY901 cores; HAL/chassis model only", "cases": reports, "area": str(area), "source_sha256": {str(p.relative_to(ROOT)).replace("\\", "/"): hashlib.sha256(p.read_bytes()).hexdigest() for p in [Path(__file__), ROOT / "tools/test_hardware_runtime.py", ROOT / "map_editor/src/HardwareIntegration.cs", ROOT / "map_editor/src/HardwareGenerator.cs", ROOT / "map_editor/src/HardwareOperations.cs", *(ROOT / "map_editor/templates").glob("*.tpl"), *(ROOT / "map_editor/hardware").rglob("*")] if p.is_file()}}
    (ROOT / "build/hardware_app_validation.json").write_text(json.dumps(evidence, ensure_ascii=False, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
