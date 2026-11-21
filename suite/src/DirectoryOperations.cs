using System;
using System.IO;

namespace RcsSuite
{
    // 只包住单次文件系统动作，不重跑安装/导入；内部操作对象用于确定性故障注入。
    internal static class DirectoryOperations
    {
        internal sealed class Operations
        {
            internal Action<string,string> MoveDirectory = Directory.Move;
            internal Action<string,string> MoveFile = File.Move;
            internal Action<string,string,string> ReplaceFile = File.Replace;
            internal Action<string,bool> DeleteDirectory = Directory.Delete;
            internal Action<int> Delay = System.Threading.Thread.Sleep;
        }
        internal static bool Exists(string path) { return Directory.Exists(path) || File.Exists(path); }
        internal static int NativeCode(Exception error)
        {
            while (error.InnerException != null) error = error.InnerException;
            return error.HResult & 0xffff;
        }
        static bool Retryable(Exception error)
        {
            int code = error.HResult & 0xffff;
            // 5不保证临时；最多3次，永不改ACL。32/33是共享/锁冲突。
            return code == 5 || code == 32 || code == 33;
        }
        internal static void NoLink(string path)
        {
            if (Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("安装事务保留链接目录/文件：" + path);
        }
        static void Guard(string path)
        {
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            if (String.Equals(full, Path.GetPathRoot(full).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new IOException("安装事务不能移动或删除磁盘根目录。");
            NoLink(full); NoLink(Path.GetDirectoryName(full));
        }
        static IOException Failure(string step, string source, string target, int attempts, Exception original)
        {
            string code = "0x" + ((uint)original.HResult).ToString("X8");
            var error = new IOException(String.Format(
                "安装文件操作失败[{0}]：源={1}；目标={2}；尝试={3}次；HRESULT={4}；源存在={5}；目标存在={6}。",
                step, source, target, attempts, code, Exists(source), target != null && Exists(target)), original);
            error.Data["InstallStep"] = step; error.Data["Source"] = source;
            error.Data["Destination"] = target ?? ""; error.Data["Attempts"] = attempts;
            error.Data["OriginalHResult"] = code;
            return error;
        }
        internal static void Move(string source, string target, string step, Operations operations,
                                  Action recheck, bool file = false)
        {
            int attempts = 0; Exception last = null;
            while (attempts < 3)
            {
                try {
                    Guard(source); Guard(target);
                    if (!(file ? File.Exists(source) : Directory.Exists(source)))
                        throw new DirectoryNotFoundException("移动源不存在。");
                    if (Exists(target)) throw new IOException("移动目标已被创建，保留它：" + target);
                    if (recheck != null) recheck();
                } catch (Exception error) { last = error; break; }
                attempts++;
                try {
                    if (file) operations.MoveFile(source, target); else operations.MoveDirectory(source, target);
                    return;
                } catch (IOException error) { last = error; }
                  catch (UnauthorizedAccessException error) { last = error; }
                if (!Retryable(last) || attempts >= 3 || !Exists(source) || Exists(target)) break;
                operations.Delay(attempts * 60);
            }
            throw Failure(step, source, target, attempts, last);
        }
        internal static void Delete(string path, bool recursive, string step, Operations operations, Action recheck)
        {
            int attempts = 0; Exception last = null;
            while (attempts < 3)
            {
                try {
                    Guard(path);
                    if (!Directory.Exists(path)) {
                        if (File.Exists(path)) throw new IOException("待删除目录被替换成文件，保留它：" + path);
                        return;
                    }
                    if (recheck != null) recheck();
                } catch (Exception error) { last = error; break; }
                attempts++;
                try { operations.DeleteDirectory(path, recursive); return; }
                catch (IOException error) { last = error; }
                catch (UnauthorizedAccessException error) { last = error; }
                if (!Retryable(last) || attempts >= 3) break;
                operations.Delay(attempts * 60);
            }
            throw Failure(step, path, null, attempts, last);
        }
        internal static void Replace(string source, string target, string backup, string step,
                                     Operations operations, Action recheck)
        {
            int attempts = 0; Exception last = null;
            while (attempts < 3)
            {
                try {
                    Guard(source); Guard(target);
                    if (backup != null) {
                        Guard(backup);
                        if (Exists(backup)) throw new IOException("修复备份路径已存在：" + backup);
                    }
                    recheck();
                } catch (Exception error) { last = error; break; }
                attempts++;
                try { operations.ReplaceFile(source, target, backup); return; }
                catch (IOException error) { last = error; }
                catch (UnauthorizedAccessException error) { last = error; }
                if (!Retryable(last) || attempts >= 3) break;
                operations.Delay(attempts * 60);
            }
            throw Failure(step, source, target, attempts, last);
        }
    }
}
