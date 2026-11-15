using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
namespace RcsSuite {
 static class CubeLocator {
  internal static string Normalize(string p){if(String.IsNullOrWhiteSpace(p))return null;try{p=Environment.ExpandEnvironmentVariables(p.Trim().Trim('"'));if(File.Exists(p)&&Path.GetFileName(p).Equals("STM32CubeMX.exe",StringComparison.OrdinalIgnoreCase))p=Path.GetDirectoryName(p);return Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar);}catch{return null;}}
  internal static bool Valid(string p){p=Normalize(p);return p!=null&&File.Exists(Path.Combine(p,"STM32CubeMX.exe"))&&File.Exists(Path.Combine(p,@"jre\bin\java.exe"));}
  static void Add(List<string> a,string p){p=Normalize(p);if(p!=null&&Valid(p)&&!a.Any(x=>String.Equals(x,p,StringComparison.OrdinalIgnoreCase)))a.Add(p);}
  static string ExeFrom(string s){if(String.IsNullOrWhiteSpace(s))return null;var m=Regex.Match(s,"^\\s*\"([^\"]+\\.exe)\"",RegexOptions.IgnoreCase);if(m.Success)return m.Groups[1].Value;m=Regex.Match(s,"^(.+?\\.exe)(?:,.*|\\s+.*)?$",RegexOptions.IgnoreCase);return m.Success?m.Groups[1].Value:null;}
  internal static List<string> Find(){var a=new List<string>();Add(a,Environment.GetEnvironmentVariable("STM32CubeMX_PATH"));
   foreach(RegistryHive hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(RegistryView view in new[]{RegistryView.Registry64,RegistryView.Registry32}){
    try{using(var root=RegistryKey.OpenBaseKey(hive,view))using(var key=root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))if(key!=null)foreach(string sub in key.GetSubKeyNames()){
     try{using(var entry=key.OpenSubKey(sub)){string display=Convert.ToString(entry.GetValue("DisplayName"));if(display.IndexOf("STM32CubeMX",StringComparison.OrdinalIgnoreCase)<0)continue;Add(a,Convert.ToString(entry.GetValue("InstallLocation")));Add(a,ExeFrom(Convert.ToString(entry.GetValue("DisplayIcon"))));string uninstall=ExeFrom(Convert.ToString(entry.GetValue("UninstallString")));if(uninstall!=null)Add(a,Path.GetDirectoryName(uninstall));}}catch{}
    }}catch{}
   }
   string user=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
   foreach(string baseDir in new[]{local,Path.Combine(user,@"AppData\Local")}){Add(a,Path.Combine(baseDir,@"Programs\STM32CubeMX"));Add(a,Path.Combine(baseDir,@"STM32CubeMX"));}
   foreach(string program in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)}){
    Add(a,Path.Combine(program,@"STMicroelectronics\STM32Cube\STM32CubeMX"));Add(a,Path.Combine(program,@"STMicroelectronics\STM32CubeMX"));}
   string ini=Path.Combine(user,@".stm32cubemx\plugins\updater\updater.ini");if(File.Exists(ini))foreach(string line in File.ReadAllLines(ini)){string s=line.Trim().TrimStart('\uFEFF');if(s.StartsWith("SoftwarePath=",StringComparison.OrdinalIgnoreCase))Add(a,s.Substring(s.IndexOf('=')+1));}
   foreach(string p in (Environment.GetEnvironmentVariable("PATH")??"").Split(';'))Add(a,p);
   return a;
  }
 }
}
