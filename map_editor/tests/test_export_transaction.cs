using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using RcsMap;

// 真实Exporter事务 + 密封模块夹具；故障只经内部操作对象注入，不使用产品环境变量。
static class ExportTransactionTests
{
    static int checks;
    static readonly List<string> scenarios = new List<string>();
    sealed class Win32Io : IOException
    {
        internal Win32Io(int code, string message) : base(message) { HResult = unchecked((int)(0x80070000u | (uint)code)); }
    }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security,
                                   uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    static void Check(bool ok, string description) { if (!ok) throw new Exception(description); checks++; }
    static string Signature(string directory)
    {
        return String.Join("\n", Directory.GetFiles(directory).OrderBy(p=>p)
            .Select(p=>Path.GetFileName(p)+":"+Convert.ToBase64String(File.ReadAllBytes(p))).ToArray());
    }
    static string Seed(string area, string name)
    {
        string output=Path.Combine(area,name,"route");
        Exporter.Export(new MapDocument(),output);
        return output;
    }
    static Exception Failure(Action action)
    {
        try { action(); } catch (Exception error) { return error; }
        throw new Exception("expected failure");
    }
    static bool IsTemp(string source) { return Path.GetFileName(source).StartsWith(".rcs-export-"); }
    static bool IsBackup(string source) { return Path.GetFileName(source).StartsWith(".rcs-route-backup-"); }
    static void Scenario(string name) { scenarios.Add(name); Console.WriteLine("PASS "+name); }

    static void RealSharingLock(string area)
    {
        string source=Path.Combine(area,"real-lock-source"),dest=Path.Combine(area,"real-lock-dest");
        Directory.CreateDirectory(source);File.WriteAllText(Path.Combine(source,"keep.txt"),"keep");
        // 不授予FILE_SHARE_DELETE的目录句柄可阻止重命名；首次退避时释放，无随机等待。
        IntPtr handle=CreateFile(source,0x80000000,3,IntPtr.Zero,3,0x02000000,IntPtr.Zero);
        Check(handle!=new IntPtr(-1),"open directory handle");
        var fileLock=new FileStream(Path.Combine(source,"keep.txt"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite);
        int moves=0;var delays=new List<int>();
        var operations=new Exporter.ExportOperations {
            Move=(a,b)=>{moves++;Directory.Move(a,b);},
            Delay=ms=>{delays.Add(ms);fileLock.Dispose();CloseHandle(handle);handle=new IntPtr(-1);}
        };
        try { Exporter.MoveDirectory(source,dest,"real sharing lock",operations,null); }
        finally { fileLock.Dispose();if(handle!=new IntPtr(-1))CloseHandle(handle); }
        Check(moves==2&&delays.SequenceEqual(new[]{60}),"one failed attempt then released lock succeeds (moves="+moves+", delays="+String.Join(",",delays)+")");
        Check(File.ReadAllText(Path.Combine(dest,"keep.txt"))=="keep","lock retry preserves bytes");
        Scenario("real short-lived directory lock: release at first retry, second move succeeds");
    }

    static void PermanentDenial(string area)
    {
        string output=Seed(area,"denied"),before=Signature(output);int moves=0;var delays=new List<int>();
        var operations=new Exporter.ExportOperations {
            Move=(a,b)=>{moves++;throw new Win32Io(5,"injected permanent denial");}, Delay=delays.Add
        };
        Exception error=Failure(()=>Exporter.Export(new MapDocument{Terrain=true},output,operations));
        Check(moves==3&&delays.SequenceEqual(new[]{60,120}),"permanent denial is bounded");
        Check(Signature(output)==before,"permanent denial leaves old content");
        Check(error.InnerException!=null&&(error.InnerException.HResult&0xFFFF)==5,"original denial retained");
        Check((int)error.Data["ExportAttempts"]==3&&error.Message.Contains("HRESULT=0x80070005"),"structured denial diagnostics");
        Check(!Directory.GetDirectories(Path.GetDirectoryName(output),".rcs-export-*").Any(),"failed staging cleaned");
        Scenario("permanent access denied: three attempts, bounded backoff, original directory unchanged");
    }

    static void ForeignTarget(string area, bool file)
    {
        string output=Seed(area,file?"foreign-file":"foreign-dir"),before=Signature(output),backup=null;
        int install=0;
        var operations=new Exporter.ExportOperations {
            Move=(a,b)=>{
                if(IsTemp(a)){install++;throw new Win32Io(32,"injected temporary conflict");}
                if(a==output)backup=b;
                Directory.Move(a,b);
            },
            Delay=ms=>{if(file)File.WriteAllText(output,"foreign owner");else{Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"user.txt"),"foreign owner");}}
        };
        Exception error=Failure(()=>Exporter.Export(new MapDocument{Terrain=true},output,operations));
        Check(install==1,"destination created during delay prevents another install attempt");
        Check(backup!=null&&Signature(backup)==before,"old directory retained in backup when target is occupied");
        Check(File.ReadAllText(file?output:Path.Combine(output,"user.txt"))=="foreign owner","foreign content never overwritten");
        Check((string)error.Data["BackupDirectory"]==backup&&error.Data["RollbackFailure"]!=null,"backup and rollback conflict reported");
        Scenario(file?"foreign target file created during backoff: stop and preserve backup":"foreign target directory created during backoff: stop and preserve backup");
    }

    static void Rollback(string area, bool failRollback)
    {
        string output=Seed(area,failRollback?"rollback-fails":"rollback-ok"),before=Signature(output),backup=null;
        int install=0,rollback=0;
        var operations=new Exporter.ExportOperations {
            Move=(a,b)=>{
                if(IsTemp(a)){install++;throw new Win32Io(32,"PRIMARY_INSTALL_ERROR");}
                if(IsBackup(a)){rollback++;if(failRollback)throw new Win32Io(33,"ROLLBACK_ERROR");}
                if(a==output)backup=b;
                Directory.Move(a,b);
            }, Delay=ms=>{}
        };
        Exception error=Failure(()=>Exporter.Export(new MapDocument{Terrain=true},output,operations));
        Check(install==3,"installation retry bounded");
        if(failRollback){
            Check(rollback==3&&!Directory.Exists(output),"rollback failure bounded and target remains absent");
            Check(Signature(backup)==before,"rollback failure preserves actual original backup bytes");
            Check((string)error.Data["BackupDirectory"]==backup&&error.Data["RollbackFailure"].ToString().Contains("ROLLBACK_ERROR"),"rollback diagnostics retain backup and cause");
            Check(error.InnerException.ToString().Contains("PRIMARY_INSTALL_ERROR"),"installation cause retained under rollback failure");
        } else {
            Check(rollback==1&&Signature(output)==before&&!Directory.Exists(backup),"failed install restores old directory");
            Check(error.ToString().Contains("PRIMARY_INSTALL_ERROR"),"original installation cause retained after successful rollback");
        }
        Scenario(failRollback?"installation and rollback both fail: original backup and both causes retained":"installation fails: original directory restored before error returns");
    }

    static void CleanupFailure(string area)
    {
        string output=Seed(area,"cleanup-fails"),before=Signature(output);
        var operations=new Exporter.ExportOperations {
            Move=(a,b)=>{throw new Win32Io(87,"FIRST_ERROR_NO_RETRY");}, Delay=ms=>{throw new Exception("must not delay");},
            Delete=(p,recursive)=>{throw new IOException("SECOND_CLEANUP_ERROR");}
        };
        Exception error=Failure(()=>Exporter.Export(new MapDocument{Terrain=true},output,operations));
        Check(error.ToString().Contains("FIRST_ERROR_NO_RETRY"),"cleanup does not replace first cause");
        Check(error.Data["CleanupFailure"].ToString().Contains("SECOND_CLEANUP_ERROR"),"cleanup failure attached");
        Check(Directory.Exists((string)error.Data["TemporaryDirectory"]),"diagnostic temporary path retained");
        Check(Signature(output)==before,"cleanup failure leaves old output intact");
        Scenario("cleanup failure attaches diagnostics without masking primary failure");
    }

    static void ConcurrentEdit(string area)
    {
        string output=Seed(area,"concurrent-edit"),path=Path.Combine(output,"rcs_actions.c");int moves=0;
        var operations=new Exporter.ExportOperations {
            Move=(a,b)=>{moves++;throw new Win32Io(32,"temporary denial");},
            Delay=ms=>File.AppendAllText(path,"/* external edit */\n")
        };
        Exception error=Failure(()=>Exporter.Export(new MapDocument{Terrain=true},output,operations));
        Check(moves==1&&File.ReadAllText(path).Contains("external edit"),"retry rechecks original content and preserves concurrent edit");
        Check(error.InnerException.Message.Contains("其他程序修改"),"content change diagnosed before another move");
        Scenario("source edited during backoff: no second move, external edit preserved");
    }

    static void MissingOrOccupied(string area)
    {
        int moves=0;var operations=new Exporter.ExportOperations{Move=(a,b)=>{moves++;Directory.Move(a,b);},Delay=ms=>{}};
        string source=Path.Combine(area,"missing"),dest=Path.Combine(area,"occupied");File.WriteAllText(dest,"foreign");
        Failure(()=>Exporter.MoveDirectory(source,dest,"precondition",operations,null));
        Directory.CreateDirectory(source);Failure(()=>Exporter.MoveDirectory(source,dest,"precondition",operations,null));
        Check(moves==0&&File.ReadAllText(dest)=="foreign","initial missing source or target file invokes no native move");
        Scenario("initial preconditions: missing source and occupied target fail before move");
    }

    static void Run(string[] args)
    {
        string area=Path.GetFullPath(args[0]);Directory.CreateDirectory(area);
        RealSharingLock(area);PermanentDenial(area);ForeignTarget(area,false);ForeignTarget(area,true);
        Rollback(area,false);Rollback(area,true);CleanupFailure(area);ConcurrentEdit(area);MissingOrOccupied(area);
        Console.WriteLine("PASS "+checks+" assertions across "+scenarios.Count+" deterministic export transaction scenarios");
    }
    static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch(Exception error) {
            Console.Error.WriteLine(error.GetType().Name+": "+error.Message);
            if(error.InnerException!=null)Console.Error.WriteLine("Inner: "+error.InnerException.GetType().Name+": "+error.InnerException.Message);
            return 2;
        }
    }
}
