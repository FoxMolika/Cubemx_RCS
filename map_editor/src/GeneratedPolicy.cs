using System;
using System.Text.RegularExpressions;
namespace RcsMap {
 static class GeneratedPolicy {
  public static void Check(string name,string text) {
   if(!text.StartsWith("/**")||!text.Contains("@file "+name)||!text.Contains("@brief"))throw new Exception(name+" 缺少文件说明。");
   if(name.EndsWith(".h")&&!text.Contains("#pragma once"))throw new Exception(name+" 缺少头文件保护。");
   bool user=false;int number=0;
   foreach(string raw in text.Replace("\r\n","\n").Split('\n')) {
    number++;string line=raw.TrimEnd('\r');
    if(Regex.IsMatch(line,@"^/\* USER CODE BEGIN [A-Z0-9_]+ \*/$")){user=true;continue;}
    if(Regex.IsMatch(line,@"^/\* USER CODE END [A-Z0-9_]+ \*/$")){user=false;continue;}
    if(user||Regex.IsMatch(line,@"^/\* (RCS REGION DEFAULT |RCS GENERATED SHA256 )"))continue;
    if(line.Length>110||line.Contains("\t")||line!=line.TrimEnd())throw new Exception(name+":"+number+" 生成区域排版不合规；用户代码区不会被改写。");
   }
  }
 }
}
