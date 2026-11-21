"""实际生成machism源码的生命周期、转移优先级与回调次数验证；不访问实车。"""
from pathlib import Path
import argparse,hashlib,json,subprocess,sys,uuid
ROOT=Path(__file__).resolve().parents[1]
HARNESS=r'''
#include "machism.h"
#include <stdio.h>
#include <stdlib.h>
int machine_test_enter,machine_test_tick,machine_test_exit,machine_test_cancel;
static int starts,ticks,stops,start_ok=1,tick_result,port_stop_phase;
static int checks;
#define C(x) do { ++checks; if (!(x)) { fprintf(stderr,"FAIL line %d: %s\n",__LINE__,#x); exit(1); } } while(0)
static int begin(void *p){(void)p;starts++;if(port_stop_phase==1)Machism_Stop();return start_ok;}
static int tick(void *p,float dt){(void)p;C(dt>0);ticks++;if(port_stop_phase==2)Machism_Stop();return tick_result;}
static void stop(void *p){(void)p;stops++;if(port_stop_phase==3)Machism_Stop();}
static void clear(void){starts=ticks=stops=0;machine_test_enter=machine_test_tick=machine_test_exit=machine_test_cancel=0;start_ok=1;tick_result=0;port_stop_phase=0;}
int main(void){machism_ports ports={0,begin,tick,stop};clear();C(Machism_Init(&ports));C(Machism_State()==MACHISM_READY);C(Machism_Start());C(!Machism_Start());C(starts==0);Machism_Task(.01f);C(starts==1&&ticks==1&&machine_test_enter==1&&machine_test_tick==1);tick_result=1;Machism_Task(.01f);C(starts==1&&ticks==2&&stops==1);C(Machism_ActiveState()==1&&Machism_State()==MACHISM_RUNNING);C(machine_test_exit==1&&machine_test_cancel==0);Machism_Task(.01f);C(Machism_State()==MACHISM_DONE);Machism_Task(.01f);C(starts==1&&ticks==2);C(!Machism_Start());Machism_Stop();C(Machism_State()==MACHISM_STOPPED);tick_result=0;C(Machism_Start());Machism_Task(.01f);Machism_Stop();C(machine_test_cancel==1&&starts==2);int cancelled=machine_test_cancel;Machism_Stop();C(machine_test_cancel==cancelled);
clear();C(Machism_Init(&ports));C(Machism_Start());Machism_Task(.01f);C(Machism_Init(&ports));C(machine_test_cancel==1&&stops==1&&Machism_State()==MACHISM_READY);
clear();C(Machism_Init(&ports));C(Machism_Start());Machism_Task(0);C(Machism_State()==MACHISM_FAULT&&Machism_Fault()==MACHISM_ERROR_TIMING);C(machine_test_cancel==0);Machism_Stop();C(Machism_State()==MACHISM_FAULT&&!Machism_Start());
clear();start_ok=0;C(Machism_Init(&ports));C(Machism_Start());Machism_Task(.01f);C(Machism_Fault()==MACHISM_ERROR_ROUTE_START&&starts==1&&stops==1&&machine_test_cancel==1);
clear();tick_result=-1;C(Machism_Init(&ports));C(Machism_Start());Machism_Task(.01f);C(Machism_Fault()==MACHISM_ERROR_ROUTE_TICK&&stops==1&&machine_test_cancel==1);Machism_Task(.01f);C(stops==1&&machine_test_cancel==1);
clear();C(Machism_Init(&ports));C(Machism_Start());for(int i=0;i<4000&&Machism_State()==MACHISM_RUNNING;i++)Machism_Task(1.0f);C(Machism_Fault()==MACHISM_ERROR_TIMEOUT&&starts==1&&stops==1&&machine_test_cancel==1);C(!Machism_Init(0));C(Machism_Fault()==MACHISM_ERROR_CONFIG);for(int phase=1;phase<=3;phase++){clear();port_stop_phase=phase;tick_result=phase==3?1:0;C(Machism_Init(&ports));C(Machism_Start());Machism_Task(.01f);C(Machism_State()==MACHISM_STOPPED&&Machism_Fault()==MACHISM_ERROR_NONE);C(starts==1&&stops==1&&machine_test_cancel==1);C(Machism_ActiveState()==0);Machism_Task(.01f);C(stops==1&&machine_test_cancel==1);}
printf("PASS %d machism runtime assertions\n",checks);return 0;}
'''
WAIT=r'''
#include "machism.h"
#include <assert.h>
#include <stdio.h>
int machine_test_enter,machine_test_tick,machine_test_exit,machine_test_cancel;
int main(void){assert(!MACHISM_HAS_ROUTE);assert(Machism_Init(0));assert(Machism_Start());Machism_Task(.01f);assert(Machism_ActiveState()==0);Machism_Task(.04f);assert(Machism_ActiveState()==1&&Machism_State()==MACHISM_RUNNING);assert(machine_test_enter==1&&machine_test_exit==1);Machism_Task(.01f);assert(Machism_State()==MACHISM_DONE);puts("PASS wait state without route ports");return 0;}
'''
NONE=r'''
#include "machism.h"
#include <assert.h>
#include <stdio.h>
int main(void){assert(!MACHISM_HAS_ROUTE);assert(Machism_Init(0));assert(Machism_Start());Machism_Task(.01f);assert(Machism_State()==MACHISM_DONE);Machism_Stop();assert(Machism_State()==MACHISM_STOPPED);puts("PASS terminal state without transitions or ports");return 0;}
'''

LIFECYCLE=r'''
#include "machism.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
static int calls[10],stop_phase,checks,block_transition;
#define C(x) do { ++checks; if(!(x)){fprintf(stderr,"FAIL lifecycle line %d: %s\n",__LINE__,#x);exit(1);} } while(0)
int machine_callback(int phase){
 ++calls[phase];
 C(!Machism_Init(0));
 C(!Machism_Start());
 if(phase==stop_phase){Machism_Stop();C(Machism_State()==MACHISM_STOPPED);C(!Machism_Start());C(!Machism_Init(0));}
 if(phase==6||phase==9){Machism_Stop();C(!Machism_Start());C(!Machism_Init(0));}
 Machism_Task(.01f);
 return phase==4&&!block_transition?1:phase==4?0:1;
}
int main(void){
 for(int phase=1;phase<=5;phase++){
  memset(calls,0,sizeof calls);stop_phase=phase;C(Machism_Init(0));C(Machism_Start());Machism_Task(.01f);
  C(Machism_State()==MACHISM_STOPPED&&Machism_ActiveState()==0);C(calls[6]==1&&calls[7]==0&&calls[9]==0);
  for(int later=phase+1;later<=5;later++)C(calls[later]==0);
  Machism_Task(.01f);Machism_Stop();C(calls[6]==1);
 }
 memset(calls,0,sizeof calls);stop_phase=8;C(Machism_Init(0));C(Machism_Start());Machism_Task(.01f);C(Machism_ActiveState()==1);Machism_Task(.01f);
 C(Machism_State()==MACHISM_STOPPED&&calls[8]==1&&calls[9]==1&&calls[6]==0);Machism_Task(.01f);C(calls[9]==1);
 memset(calls,0,sizeof calls);stop_phase=0;C(Machism_Init(0));C(Machism_Start());Machism_Task(.01f);Machism_Task(.01f);C(Machism_State()==MACHISM_DONE&&calls[6]==0&&calls[9]==0);
 C(Machism_Init(0));C(Machism_Start());stop_phase=0;Machism_Task(.01f);C(Machism_ActiveState()==1);Machism_Stop();C(Machism_State()==MACHISM_STOPPED&&calls[9]==0);
 /* 外部Stop也保护cancel回调上下文；未进入的目标状态不能收到cancel。 */
 C(Machism_Init(0));C(Machism_Start());stop_phase=2;Machism_Task(.01f);C(Machism_State()==MACHISM_STOPPED);
 memset(calls,0,sizeof calls);stop_phase=0;block_transition=1;C(Machism_Init(0));C(Machism_Start());Machism_Task(.01f);C(Machism_State()==MACHISM_RUNNING&&Machism_ActiveState()==0);Machism_Stop();C(Machism_State()==MACHISM_STOPPED&&calls[6]==1);Machism_Stop();C(calls[6]==1);
 printf("PASS %d callback stop/reentrancy assertions\n",checks);return 0;
}
'''

def run(args):
 p=subprocess.run([str(x) for x in args],cwd=ROOT,capture_output=True,text=True,encoding='utf-8',errors='replace',timeout=120)
 if p.returncode:raise RuntimeError(str(args)+'\n'+p.stdout+p.stderr)
 return p.stdout.strip()
def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--editor',default='map_editor/dist/foxmolika_MapEditor.exe');a=parser.parse_args();editor=(ROOT/a.editor).resolve();area=ROOT/'build'/('machine_'+uuid.uuid4().hex[:8]);area.mkdir(parents=True)
 print(run([editor,'--machine-selftest',area]),flush=True);results=[]
 for kind,harness in [('runtime',HARNESS),('priority',HARNESS),('wait',WAIT),('none',NONE),('lifecycle',LIFECYCLE)]:
  folder=area/kind;src=folder/'harness.c';src.write_text(harness,encoding='utf-8');exe=folder/'test.exe'
  run(['gcc','-std=c99','-Wall','-Wextra','-Werror','-I',folder,folder/'machism.c',src,'-lm','-o',exe]);text=run([exe]);results.append(text);print(kind+': '+text,flush=True)
 report={'passed':True,'scope':'actual editor generation and C machism lifecycle; no hardware execution','editor_sha256':hashlib.sha256(editor.read_bytes()).hexdigest(),'runtime_reports':results,'area':str(area),'source_sha256':{str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in [ROOT/'map_editor/src/ApplicationModel.cs',ROOT/'map_editor/src/ApplicationEditor.cs',ROOT/'map_editor/src/MachineGenerator.cs',ROOT/'map_editor/src/ApplicationTests.cs',ROOT/'map_editor/templates/machism.c.tpl',ROOT/'map_editor/templates/machism.h.tpl']}}
 (ROOT/'build/machine_validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
if __name__=='__main__':main()
