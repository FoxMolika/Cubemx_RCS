using System;
using System.Collections.Generic;
namespace RcsMap {
 public class PhasePreview {
  class Part {public double X,Y,Yaw,Seconds;public string Name;}
  List<Part> parts=new List<Part>();int index;double time,x,y,yaw,startX,startY,startYaw;
  public bool Done {get{return index>=parts.Count;}} public string Status="";
  public PhasePreview(MapDocument doc){x=startX=doc.Points[0].X;y=startY=doc.Points[0].Y;yaw=startYaw=doc.Points[0].Yaw;double px=x,py=y,heading=yaw;
   for(int i=0;i<doc.Points.Count;i++){var p=doc.Points[i];if(i>0){double distance=Math.Sqrt((p.X-px)*(p.X-px)+(p.Y-py)*(p.Y-py));parts.Add(new Part{X=p.X,Y=p.Y,Yaw=heading,Seconds=Math.Max(.1,distance/p.Speed),Name="P"+i+" 平移到点"});}px=p.X;py=p.Y;parts.Add(new Part{X=px,Y=py,Yaw=heading,Seconds=.15,Name="停稳"});
    for(int group=0;group<2;group++){if(group==1){parts.Add(new Part{X=px,Y=py,Yaw=heading,Seconds=.2,Name="独立纠偏（理想无误差）"});if(i==doc.Points.Count-1&&!p.RunFinalDeparture)continue;}foreach(var a in group==0?p.ArrivalActions:p.DepartureActions){if(!a.Enabled)continue;double duration=.5;if(a.Kind==1||a.Kind==2){double delta=PhaseMath.RotationDelta(doc,a,heading);double ramp=a.MaxWz*a.MaxWz/a.Alpha;duration=(Math.Abs(delta)<=ramp?2*Math.Sqrt(Math.Abs(delta)/a.Alpha):2*a.MaxWz/a.Alpha+(Math.Abs(delta)-ramp)/a.MaxWz)+a.SettleMs/1000;heading+=delta;}else if(a.Kind==3)duration=a.Value;parts.Add(new Part{X=px,Y=py,Yaw=heading,Seconds=Math.Max(.01,duration),Name=(group==0?"到达后：":"起步前：")+a.Name+(a.Kind==0?"（机构占位）":"")});}}
    parts.Add(new Part{X=px,Y=py,Yaw=heading,Seconds=.1,Name="离开条件验收"});}
  }
  public double[] Tick(double dt){if(Done)return new[]{x,y,yaw*Math.PI/180};time+=dt;var p=parts[index];double t=Math.Min(1,time/p.Seconds);x=startX+(p.X-startX)*t;y=startY+(p.Y-startY)*t;yaw=startYaw+(p.Yaw-startYaw)*t;Status=p.Name;if(t>=1){index++;time=0;startX=x;startY=y;startYaw=yaw;}return new[]{x,y,yaw*Math.PI/180};}
 }
}
