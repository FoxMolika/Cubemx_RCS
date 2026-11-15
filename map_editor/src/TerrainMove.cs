using System;
using System.Collections.Generic;
using System.Linq;

namespace RcsMap {
 // All positions are measured from the mouse-down snapshot, never from the
 // previous frame. Child objects use the same clamped delta as their surface.
 public sealed class TerrainMove {
  readonly TerrainSurface surface;readonly double x,y,fieldWidth,fieldHeight;
  readonly List<Waypoint> points=new List<Waypoint>();readonly List<Obstacle> obstacles=new List<Obstacle>();
  readonly List<double[]> pointXY=new List<double[]>(),obstacleXY=new List<double[]>();
  double minX,minY,maxX,maxY;
  public bool Changed {get;private set;}
  public string SurfaceId {get{return surface.Id;}}
  TerrainMove(MapDocument d,TerrainSurface s,bool carry){surface=s;x=s.X;y=s.Y;fieldWidth=d.Settings.Width;fieldHeight=d.Settings.Height;minX=x;minY=y;maxX=x+s.Width;maxY=y+s.Depth;
   if(carry){foreach(var p in d.Points.Where(p=>p.SurfaceId==s.Id)){points.Add(p);pointXY.Add(new[]{p.X,p.Y});Include(p.X,p.Y,p.X,p.Y);}foreach(var o in d.Obstacles.Where(o=>o.SurfaceId==s.Id)){obstacles.Add(o);obstacleXY.Add(new[]{o.X,o.Y});Include(o.X,o.Y,o.X+o.Width,o.Y+o.Height);}}
  }
  void Include(double ax,double ay,double bx,double by){minX=Math.Min(minX,ax);minY=Math.Min(minY,ay);maxX=Math.Max(maxX,bx);maxY=Math.Max(maxY,by);}
  public static TerrainMove Begin(MapDocument d,string id,bool carry){if(d==null||d.Terrain==null||TerrainGeometry.Key(id)==TerrainGeometry.Ground)return null;var s=TerrainGeometry.Surface(d,id);if(s==null||s.Width<=0||s.Depth<=0)return null;
   var move=new TerrainMove(d,s,carry);if(!new[]{move.minX,move.minY,move.maxX,move.maxY,move.fieldWidth,move.fieldHeight}.All(MapDocument.Finite)||move.fieldWidth<=0||move.fieldHeight<=0||move.maxX-move.minX>move.fieldWidth||move.maxY-move.minY>move.fieldHeight)return null;return move;
  }
  public void Update(double dx,double dy,bool snap,double grid){if(!MapDocument.Finite(dx)||!MapDocument.Finite(dy))return;double tx=x+dx,ty=y+dy;if(snap&&MapDocument.Finite(grid)&&grid>0){tx=Math.Round(tx/grid,MidpointRounding.AwayFromZero)*grid;ty=Math.Round(ty/grid,MidpointRounding.AwayFromZero)*grid;}
   tx=Math.Max(x-minX,Math.Min(fieldWidth-(maxX-x),tx));ty=Math.Max(y-minY,Math.Min(fieldHeight-(maxY-y),ty));Apply(tx-x,ty-y);
  }
  void Apply(double dx,double dy){surface.X=x+dx;surface.Y=y+dy;for(int i=0;i<points.Count;i++){points[i].X=pointXY[i][0]+dx;points[i].Y=pointXY[i][1]+dy;}for(int i=0;i<obstacles.Count;i++){obstacles[i].X=obstacleXY[i][0]+dx;obstacles[i].Y=obstacleXY[i][1]+dy;}Changed=Math.Abs(dx)>1e-7||Math.Abs(dy)>1e-7;}
  public void Cancel(){Apply(0,0);}
 }
 public sealed class TerrainHistory {
  readonly Stack<string> undo=new Stack<string>(),redo=new Stack<string>();string current;
  public bool CanUndo {get{return undo.Count>0;}} public bool CanRedo {get{return redo.Count>0;}}
  public TerrainHistory(MapDocument d){current=d.Json();}
  public void Observe(MapDocument d){string next=d.Json();if(next==current)return;undo.Push(current);if(undo.Count>100){var items=undo.Take(100).Reverse().ToArray();undo.Clear();foreach(var item in items)undo.Push(item);}redo.Clear();current=next;}
  public MapDocument Undo(){if(!CanUndo)return null;string previous=undo.Peek();var d=MapDocument.Parse(previous);redo.Push(current);undo.Pop();current=previous;return d;}
  public MapDocument Redo(){if(!CanRedo)return null;string next=redo.Peek();var d=MapDocument.Parse(next);undo.Push(current);redo.Pop();current=next;return d;}
 }
}
