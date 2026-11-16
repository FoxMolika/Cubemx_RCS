using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
namespace RcsMap {
 public static class UserRegions {
  const string Digest="/* RCS GENERATED SHA256 ";
  static string LF(string s){return s.Replace("\r\n","\n").Replace("\r","\n");}
  public static string Hash(string s){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-","").ToLowerInvariant();}
  class Region {public string Key,Body,Default;public int Start,Length;}
  static List<Region> Regions(string s){
   var list=new List<Region>();var open=Regex.Matches(s,@"(?m)^/\* USER CODE BEGIN ([A-Z0-9_]+) \*/\n");
   if(open.Count!=Regex.Matches(s,@"/\* USER CODE BEGIN ").Count)throw new Exception("USER CODE 起始标记格式不正确。");
   foreach(Match m in open){string key=m.Groups[1].Value;string end="/* USER CODE END "+key+" */";int close=s.IndexOf(end,m.Index+m.Length,StringComparison.Ordinal);
    if(close<0||s.IndexOf("/* USER CODE BEGIN ",m.Index+m.Length,close-(m.Index+m.Length),StringComparison.Ordinal)>=0||list.Any(r=>r.Key==key))throw new Exception("USER CODE 标记缺失、嵌套或重复："+key);
    var meta=Regex.Match(s,@"(?m)^/\* RCS REGION DEFAULT "+key+@" ([0-9a-f]{64}) \*/$");if(!meta.Success)throw new Exception("缺少用户区域校验："+key);
    list.Add(new Region{Key=key,Body=s.Substring(m.Index+m.Length,close-m.Index-m.Length),Default=meta.Groups[1].Value,Start=m.Index+m.Length,Length=close-m.Index-m.Length});
   }
   if(list.Count!=Regex.Matches(s,@"/\* USER CODE END ").Count)throw new Exception("USER CODE 结束标记不匹配。");return list;
  }
  public static string Block(string key,string body){body=LF(body??"").TrimEnd()+"\n";if(body.Contains("USER CODE BEGIN")||body.Contains("USER CODE END")||body.Contains("RCS REGION DEFAULT")||body.Contains("RCS GENERATED SHA256"))throw new Exception("动作代码不能包含生成器保留标记。");
   return "/* RCS REGION DEFAULT "+key+" "+Hash(body)+" */\n/* USER CODE BEGIN "+key+" */\n"+body+"/* USER CODE END "+key+" */\n";}
  // 模板自带 USER CODE 区时使用：按区域正文补上 DEFAULT 哈希行，之后的 Seal/Merge 逻辑不变。
  // 只有模板作者能决定默认体，所以默认值必须来自模板本身，而不是调用方拼字符串。
  public static string Stamp(string text){
   var open=Regex.Matches(text,@"(?m)^/\* USER CODE BEGIN ([A-Z0-9_]+) \*/\n");
   if(open.Count!=Regex.Matches(text,@"(?m)^/\* USER CODE BEGIN ").Count)throw new Exception("USER CODE 起始标记格式不正确。");
   foreach(Match m in open.Cast<Match>().OrderByDescending(x=>x.Index)){
    string key=m.Groups[1].Value;string end="/* USER CODE END "+key+" */";
    int close=text.IndexOf(end,m.Index+m.Length,StringComparison.Ordinal);
    if(close<0)throw new Exception("USER CODE 标记缺失："+key);
    string body=LF(text.Substring(m.Index+m.Length,close-m.Index-m.Length)).TrimEnd()+"\n";
    text=text.Insert(m.Index,"/* RCS REGION DEFAULT "+key+" "+Hash(body)+" */\n");
   }
   return text;
  }
  static string Skeleton(string s){foreach(var r in Regions(s).OrderByDescending(r=>r.Start))s=s.Remove(r.Start,r.Length);return s;}
  static string Unsigned(string text){string s=LF(text);int at=s.LastIndexOf(Digest,StringComparison.Ordinal);if(at<0)throw new Exception("旧文件缺少可保留式更新标识，请导出新目录。");string body=s.Substring(0,at);var m=Regex.Match(s.Substring(at),@"\A/\* RCS GENERATED SHA256 ([0-9a-f]{64}) \*/\n?\z");if(!m.Success||Hash(Skeleton(body))!=m.Groups[1].Value)throw new Exception("生成区域在 USER CODE 外被修改，保留原文件；请导出新目录。");return body;}
  public static string Seal(string text){string s=LF(text);return s+Digest+Hash(Skeleton(s))+" */\n";}
  // 仅兼容已知默认注释翻译。表达式或其他代码变化仍然触发双边冲突保护。
  static bool SameDefault(Region previous,Region next){if(previous.Default==next.Default)return true;
   if(next.Key.EndsWith("_POLL",StringComparison.Ordinal))return previous.Default==Hash(next.Body.Replace("/* 设置complete；机构故障返回RCS_ACTION_ERROR。 */","/* Set complete, or return RCS_ACTION_ERROR for a mechanism fault. */"));
   if(next.Key=="RCS_ACTION_GLOBALS")return previous.Default==Hash(next.Body.Replace("/* 公共机构状态或action_context类型。 */","/* Shared state or your action_context type. */"));return false;
  }
  public static string Merge(string oldText,string newText){
   string old=Unsigned(oldText),fresh=Unsigned(newText);var previous=Regions(old);var next=Regions(fresh);
   foreach(var r in previous.Where(r=>Hash(r.Body)!=r.Default)){
    var n=next.FirstOrDefault(x=>x.Key==r.Key);if(n==null)throw new Exception("被删除的航点仍有自定义 USER CODE，代码已保留。请先另存旧代码或导出新目录："+r.Key);
    if(!SameDefault(r,n)&&Hash(r.Body)!=Hash(n.Body))throw new Exception("地图和 .c 的同一用户区域都被修改，请先统一或导出新目录："+r.Key);
   }
   foreach(var n in next.OrderByDescending(r=>r.Start)){var oldRegion=previous.FirstOrDefault(r=>r.Key==n.Key);if(oldRegion!=null&&Hash(oldRegion.Body)!=oldRegion.Default)fresh=fresh.Remove(n.Start,n.Length).Insert(n.Start,oldRegion.Body);}
   return Seal(fresh);
  }
  public static void Check(string text){Unsigned(text);}
 }
}
