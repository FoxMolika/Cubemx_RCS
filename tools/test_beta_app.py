"""真实生成App/Machism/Nav/CAN反馈全链路主机测试；HAL及电机执行为理想模型。"""
from pathlib import Path
import argparse,copy,hashlib,json,subprocess,uuid,sys
sys.path.insert(0,str(Path(__file__).resolve().parent))
from settings import DEFAULTS,config_header
ROOT=Path(__file__).resolve().parents[1]
HARNESS=r'''
#include "app.h"
#include "rcs_app_config.h"
#include "rcs_config.h"
#include "rcs_route.h"
#include "machism.h"
#include "rcs_feedback_port.h"
#include "rcs_can_port.h"
#include <assert.h>
#include <math.h>
#include <stdio.h>
#include <string.h>
CAN_TypeDef test_can1,test_can2;
CAN_HandleTypeDef hcan1={CAN1},hcan2={CAN2};
static uint32_t now_ms,primask;
static rcs_state chassis_state=RCS_LOCKED;
static rcs_fault chassis_fault=RCS_OK;
static int commands,nonzero,locks,arms,processes,rx,notified,cancelled,entered,held;
static float command[3],rotor[4];
static uint32_t last_rx[4];
static uint8_t seen;
static const unsigned ids[4]=RCS_APP_MOTOR_IDS;
static CAN_RxHeaderTypeDef queued;
static uint8_t queued_data[8];
static int queued_count;
static unsigned tests;
#define CHECK(x) do { ++tests; if(!(x)){fprintf(stderr,"FAIL %d %s state=%d nav=%d fault=%d fb=%d t=%u\n",__LINE__,#x,App_task_State(),App_task_Navigator()->state,App_task_Fault(),rcs_feedback_port_fault(),now_ms);return 1;} } while(0)
uint32_t HAL_GetTick(void){return now_ms;}
uint32_t __get_PRIMASK(void){return primask;}
void __disable_irq(void){primask=1;}
void __set_PRIMASK(uint32_t x){primask=x;}
int HAL_CAN_ConfigFilter(CAN_HandleTypeDef *c,CAN_FilterTypeDef *f){return c&&f&&f->FilterBank==(RCS_CAN_BUS==2?14u:0u)?HAL_OK:HAL_ERROR;}
int HAL_CAN_Start(CAN_HandleTypeDef *c){if(!c||c->Instance->TEST_STATE!=HAL_CAN_STATE_READY)return HAL_ERROR;c->Instance->TEST_STATE=HAL_CAN_STATE_LISTENING;return HAL_OK;}
int HAL_CAN_ActivateNotification(CAN_HandleTypeDef *c,uint32_t n){notified=c&&n==CAN_IT_RX_FIFO0_MSG_PENDING;return notified?HAL_OK:HAL_ERROR;}
uint32_t HAL_CAN_GetRxFifoFillLevel(CAN_HandleTypeDef *c,uint32_t f){(void)c;(void)f;return (uint32_t)queued_count;}
int HAL_CAN_GetRxMessage(CAN_HandleTypeDef *c,uint32_t f,CAN_RxHeaderTypeDef *h,uint8_t data[8]){(void)c;(void)f;if(!queued_count)return HAL_ERROR;*h=queued;memcpy(data,queued_data,8);queued_count=0;return HAL_OK;}
int RCS_Init(CAN_HandleTypeDef *c){(void)c;seen=0;chassis_state=RCS_LOCKED;chassis_fault=RCS_OK;return 1;}
void RCS_OnCanRx(CAN_HandleTypeDef *c,const CAN_RxHeaderTypeDef *h,const uint8_t data[8]){(void)c;(void)data;for(unsigned i=0;i<RCS_APP_WHEELS;i++)if(h->StdId==0x200u+ids[i]){seen|=(uint8_t)(1u<<i);last_rx[i]=now_ms;rx++;}}
void RCS_Process(void){processes++;if(chassis_state==RCS_ARMED)for(unsigned i=0;i<RCS_APP_WHEELS;i++)if(now_ms-last_rx[i]>100u){chassis_fault=RCS_FEEDBACK_TIMEOUT;chassis_state=RCS_FAULT;}}
int RCS_Arm(void){if(seen!=(uint8_t)((1u<<RCS_APP_WHEELS)-1u))return 0;chassis_state=RCS_ARMED;arms++;return 1;}
void RCS_Lock(void){chassis_state=RCS_LOCKED;command[0]=command[1]=command[2]=0;locks++;}
int RCS_SetVelocity(float x,float y,float w){if(chassis_state!=RCS_ARMED)return 0;command[0]=x;command[1]=y;command[2]=w;commands++;if(fabsf(x)+fabsf(y)+fabsf(w)>1e-6f)nonzero++;return 1;}
rcs_state RCS_State(void){return chassis_state;}
rcs_fault RCS_Fault(void){return chassis_fault;}
void beta_enter(void){entered++;}
void beta_cancel(void){cancelled++;}
int test_terrain_brake(void *c,rcs_terrain_brake_request request){(void)c;(void)request;held++;return RCS_SetVelocity(0,0,0);}
static CAN_HandleTypeDef *selected(void){return RCS_CAN_BUS==2?&hcan2:&hcan1;}
static void step(int feedback){
 float rpm[4]={0};rcs_inverse(&rcs_route_config.geometry,command[0],command[1],command[2],rpm);
 ++now_ms;
 if(feedback&&!RCS_NO_CHASSIS)for(unsigned i=0;i<RCS_APP_WHEELS;i++){
  rotor[i]+=rpm[i]*8192.0f/60000.0f*rcs_route_config.encoder_direction[i];
  int angle=(int)floorf(rotor[i])%8192;if(angle<0)angle+=8192;
  memset(&queued,0,sizeof queued);queued.StdId=0x200u+ids[i];queued.DLC=8u;memset(queued_data,0,8);queued_data[0]=(uint8_t)((unsigned)angle>>8);queued_data[1]=(uint8_t)angle;queued_count=1;
#if !RCS_NO_CHASSIS
  HAL_CAN_RxFifo0MsgPendingCallback(selected());
#else
  (void)selected();
#endif
 }
 App_Task();
}
int main(int argc,char **argv){
 (void)argc;const char *scenario=argv[1];CHECK(App_Init());CHECK(!MACHISM_HAS_ROUTE||notified);CHECK(App_task_State()==RCS_APP_READY);
 if(strcmp(scenario,"pure_machine")==0){for(int i=0;i<1000;i++){step(0);}CHECK(App_task_State()==RCS_APP_FINISHED&&arms==0&&nonzero==0);puts("PASS no-chassis wait state machine autostarts without feedback or start pose");return 0;}
 if(strcmp(scenario,"disabled")==0){for(int i=0;i<4000;i++){step(1);}CHECK(App_task_State()==RCS_APP_READY);CHECK(arms==0&&nonzero==0);puts("PASS unconfigured map never starts");return 0;}
 for(int i=0;i<5000&&App_task_State()!=RCS_APP_RUNNING;i++){step(1);}
 CHECK(App_task_State()==RCS_APP_RUNNING);CHECK(Machism_State()==MACHISM_RUNNING);
 for(int i=0;i<100&&arms==0;i++){step(1);}CHECK(arms==1);CHECK(App_task_State()==RCS_APP_RUNNING);
 if(strcmp(scenario,"complete")==0){for(int i=0;i<20000&&App_task_State()==RCS_APP_RUNNING;i++){step(1);}CHECK(App_task_State()==RCS_APP_FINISHED);CHECK(nonzero>0&&rx>100&&processes>0);CHECK(App_task_Fault()==RCS_APP_ERROR_NONE);int prior=arms;for(int i=0;i<3000;i++){step(1);}CHECK(arms==prior&&App_task_State()==RCS_APP_FINISHED);puts("PASS autostart omni3 rotation/wait and whole route with null context");return 0;}
 if(strcmp(scenario,"stop")==0){for(int i=0;i<100;i++){step(1);}App_Stop();CHECK(App_task_State()==RCS_APP_STOPPED);int prior=arms;for(int i=0;i<3000;i++){step(1);}CHECK(arms==prior&&App_task_State()==RCS_APP_STOPPED&&chassis_state==RCS_LOCKED);puts("PASS stop remains stopped and cannot autostart");return 0;}
 if(strcmp(scenario,"feedback_fault")==0){for(int i=0;i<100;i++){step(1);}for(int i=0;i<250;i++){step(0);}CHECK(App_task_State()==RCS_APP_FAULT);CHECK(App_task_Fault()!=RCS_APP_ERROR_NONE);uint32_t first=App_task_FaultTick();int prior=arms;for(int i=0;i<3000;i++){step(1);}CHECK(arms==prior&&App_task_State()==RCS_APP_FAULT&&App_task_FaultTick()==first);puts("PASS missing feedback latches first fault and never restarts");return 0;}
 if(strcmp(scenario,"reinit")==0){for(int i=0;i<500&&entered==0;i++){step(1);}CHECK(entered==1);CHECK(App_Init());CHECK(cancelled==1);int prior=arms;for(int i=0;i<3000;i++){step(1);}CHECK(App_task_State()==RCS_APP_READY&&arms==prior&&cancelled==1);puts("PASS active waypoint action cancels exactly once during reinitialization");return 0;}
 if(strcmp(scenario,"hold")==0){for(int i=0;i<30000&&fabsf(App_task_Navigator()->terrain.pitch_rad)<.02f;i++){step(1);}CHECK(fabsf(App_task_Navigator()->terrain.pitch_rad)>.02f);App_Stop();CHECK(App_task_Holding()&&chassis_state==RCS_ARMED);int before=held,prior=arms;for(int i=0;i<200;i++){step(1);}CHECK(held>before+5&&App_task_Holding()&&arms==prior);chassis_fault=RCS_TX_FAILED;chassis_state=RCS_FAULT;for(int i=0;i<20;i++){step(1);}CHECK(App_task_State()==RCS_APP_FAULT&&!App_task_Holding());CHECK(App_task_ChassisFault()==RCS_TX_FAILED&&arms==prior);puts("PASS slope hold renewed while stopped; chassis fault forbids automatic rearm");return 0;}
 CHECK(0);return 1;
}
'''
def run(args):
 p=subprocess.run(list(map(str,args)),cwd=ROOT,capture_output=True,text=True,encoding='utf-8',errors='replace',timeout=120)
 if p.returncode:raise RuntimeError(' '.join(map(str,args))+'\n'+p.stdout+p.stderr)
 return p.stdout.strip()
def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--editor',default='map_editor/dist/foxmolika_MapEditor.exe');args=parser.parse_args();editor=(ROOT/args.editor).resolve();area=ROOT/'build'/('beta_app_'+uuid.uuid4().hex[:8]);area.mkdir(parents=True)
 fixture=area/'seed.json';run([editor,'--machine-fixture',fixture]);seed=json.loads(fixture.read_text(encoding='utf-8'))
 seed['Settings']['Layout']=1;seed['Application'].update(BootDelayMs=10,CanBus=2,MotorId0=3,MotorId1=1,MotorId2=2,MotorId3=4);seed['Obstacles']=[];seed['Points']=seed['Points'][:2]
 for i,p in enumerate(seed['Points']):p.update(X=1000+200*i,Y=1000,Yaw=0,Speed=200,ArrivalActions=[],DepartureActions=[])
 seed['Points'][0]['DepartureActions']=[dict(seed['Points'][0]['DepartureActions'][0])] if seed['Points'][0]['DepartureActions'] else []
 # 直接保留原型动作的完整字段，测试内建动作不要求机构context。
 prototype=json.loads(fixture.read_text(encoding='utf-8'))['Points'][1]
 base=next(a for p in json.loads(fixture.read_text(encoding='utf-8'))['Points'] for a in p['ArrivalActions']+p['DepartureActions'])
 rotation=copy.deepcopy(base);rotation.update(Id=uuid.uuid4().hex,Kind=2,Value=90,Name='rotate');wait=copy.deepcopy(base);wait.update(Id=uuid.uuid4().hex,Kind=3,Value=.03,Name='wait',Timeout=2)
 seed['Points'][0]['DepartureActions']=[rotation,wait]
 terrain=area/'terrain';run([editor,'--terrain-drive-fixture',terrain]);slope=json.loads((terrain/'terrain-layout1.json').read_text(encoding='utf-8'));slope['Version']=7;slope['Application']=copy.deepcopy(seed['Application'])
 reports=[]
 for scenario in ['complete','stop','feedback_fault','reinit','disabled','hold','pure_machine']:
  doc=copy.deepcopy(slope if scenario=='hold' else seed)
  if scenario=='disabled':doc.pop('Application');doc['Version']=3
  if scenario=='pure_machine':doc['Application']['States'][0].update(Kind=2,WaitSeconds=.03,Timeout=1);doc['Application']['StartAtP0']=False
  if scenario=='reinit':
   custom=copy.deepcopy(base);custom.update(Id=uuid.uuid4().hex,Kind=0,Name='active mechanism',Start='beta_enter();',Condition='elapsed_s > 10.0f',Cancel='beta_cancel();',Done='/* done */',Timeout=30)
   doc['Points'][0]['ArrivalActions']=[custom];doc['ActionIncludes']='void beta_enter(void);\nvoid beta_cancel(void);'
  case=area/scenario;case.mkdir();mapfile=case/'map.rcsmap.json';mapfile.write_text(json.dumps(doc,ensure_ascii=False),encoding='utf-8');out=case/'generated';run([editor,'--export',mapfile,out])
  stub=case/'stub';stub.mkdir();text=(ROOT/'tests/beta_feedback_hal/rcs_hal.h').read_text(encoding='utf-8')+'\n#include "rcs_control.h"\nint RCS_Init(CAN_HandleTypeDef*);\nint RCS_Arm(void);\nvoid RCS_Lock(void);\nint RCS_SetVelocity(float,float,float);\nvoid RCS_Process(void);\nrcs_state RCS_State(void);\nrcs_fault RCS_Fault(void);\n'
  (stub/'rcs_hal.h').write_text(text,encoding='utf-8')
  cfg=dict(DEFAULTS);st=doc['Settings'];app=doc.get('Application',{})
  cfg.update(RCS_LAYOUT=st['Layout'],RCS_CAN_BUS=app.get('CanBus',1),RCS_KP=1,RCS_WHEEL_DIAMETER_MM=st['Diameter'],RCS_WHEELBASE_MM=st['Wheelbase'],RCS_TRACK_MM=st['Track'],RCS_OMNI_RADIUS_MM=st['OmniRadius'],RCS_OMNI_THETA0_DEG=st['Theta0'],RCS_GEAR_RATIO=st['Gear'],RCS_MAX_MOTOR_RPM=st['MaxRpm'])
  for i in range(4):cfg['RCS_MOTOR_ID_'+str(i)]=app.get('MotorId'+str(i),i+1);cfg['RCS_DIRECTION_'+str(i)]=st['Dir'+str(i)]
  (stub/'rcs_config.h').write_text(config_header(cfg),encoding='utf-8')
  body=HARNESS if scenario=='hold' else HARNESS.replace('if(strcmp(scenario,"hold")==0){', '#if 0\n if(strcmp(scenario,"hold")==0){').replace('CHECK(0);return 1;','#endif\n CHECK(0);return 1;')
  # 类型只在坡道编译中存在；平地用同签名占位不构造虚假terrain模块。
  if scenario!='hold':body=body.replace('int test_terrain_brake(void *c,rcs_terrain_brake_request request)', 'int test_terrain_brake(void *c,int request)')
  harness=case/'harness.c';harness.write_text(body,encoding='utf-8');exe=case/'app.exe'
  compile=['gcc','-std=c99','-Wall','-Wextra','-Werror','-O2','-I',stub,'-I',out,'-I',ROOT/'firmware',*sorted(out.glob('*.c')),ROOT/'firmware/rcs_chassis.c',harness,'-lm','-o',exe]
  if scenario=='pure_machine':compile.insert(1,'-DRCS_NO_CHASSIS=1')
  run(compile);report=run([exe,scenario]);reports.append({'scenario':scenario,'report':report,'generated_sha256':{f.name:hashlib.sha256(f.read_bytes()).hexdigest() for f in sorted(out.iterdir())},'map_sha256':hashlib.sha256(mapfile.read_bytes()).hexdigest()});print(report,flush=True)
 evidence={'source_sha256':{str(f.relative_to(ROOT)).replace('\\','/'):hashlib.sha256(f.read_bytes()).hexdigest() for f in [Path(__file__),*(ROOT/'map_editor/templates'/n for n in ['app.c.tpl','app.h.tpl','rcs_app_task.c.tpl','rcs_app_task.h.tpl','rcs_app_config.h.tpl','rcs_feedback_config.h.tpl','machism.c.tpl','machism.h.tpl']),*(ROOT/'map_editor/firmware'/n for n in ['rcs_can_port.c','rcs_feedback_port.c','rcs_nav.c','rcs_nav.h','rcs_terrain.c','rcs_terrain.h']),ROOT/'tests/beta_feedback_hal/rcs_hal.h']},'passed':True,'hardware':False,'scope':'real generated app/machism/nav/CAN/feedback with HAL and ideal chassis model','cases':reports,'editor_sha256':hashlib.sha256(editor.read_bytes()).hexdigest(),'area':str(area)}
 (ROOT/'build/beta_app_validation.json').write_text(json.dumps(evidence,ensure_ascii=False,indent=2),encoding='utf-8')
if __name__=='__main__':main()
