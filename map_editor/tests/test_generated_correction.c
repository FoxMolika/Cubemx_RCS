/* 将实际导出的六个C/H与测试定位适配器编译链接，验证回调确实接入执行。 */
#include "rcs_route.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
static unsigned reads;
int test_observer_read(rcs_observation *sample)
{
 sample->x_m=.61f;sample->y_m=.6f;sample->yaw_rad=0;sample->age_s=0;
 sample->quality=1;sample->sequence=++reads;sample->axes=7;return 1;
}
int main(void)
{
 rcs_navigator n;float out[3];int32_t delta[4]={0};
 if(!rcs_route_init(&n,NULL)||!n.corrections||rcs_route_plan[0].correction_mode!=0)return 1;
 if(!rcs_nav_reset(&n,.6f,.6f,0)||!rcs_nav_update(&n,delta,15,.01f,out)||!rcs_nav_start(&n))return 2;
 for(unsigned i=0;i<100&&!reads;i++)if(!rcs_nav_update(&n,delta,15,.01f,out))return 3;
 if(reads!=1||fabsf(n.x_m-.61f)>1e-6f||fabsf(n.y_m-.6f)>1e-6f)return 4;
 if(out[0]!=0||out[1]!=0||out[2]!=0||n.target!=0)return 5;
 puts("PASS actual generated route binds real observer adapter, refreshes XY once and emits zero motion");return 0;
}
