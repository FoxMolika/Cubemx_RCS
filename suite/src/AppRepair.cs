using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RcsSuite
{
    internal static class AppRepair
    {
        sealed class Change
        {
            internal string Source, Target, Backup, Before, After;
        }

        internal static string ManagedPath(string root, string name)
        {
            string path = Payload.In(root, name);
            for (string part = path; part != null; part = Path.GetDirectoryName(part))
                DirectoryOperations.NoLink(part);
            if (Directory.Exists(path))
                throw new IOException("安装文件的位置已有目录，已保留：" + path);
            return path;
        }

        static string ReadHash(string path)
        {
            return File.Exists(path) ? Payload.HashFile(path) : null;
        }

        static void CheckHash(string path, string expected)
        {
            DirectoryOperations.NoLink(path);
            DirectoryOperations.NoLink(Path.GetDirectoryName(path));
            if (Directory.Exists(path) || ReadHash(path) != expected)
                throw new IOException("修复期间文件发生变化，已保留：" + path);
        }

        internal static string Apply(string root, string stage, Dictionary<string,string> staged,
                                     DirectoryOperations.Operations operations)
        {
            string backupRoot = Path.Combine(root, ".rcs-repair-backup-" + Guid.NewGuid().ToString("N"));
            var changes = new List<Change>();
            // Keep the existing receipt until all application files are in place.
            foreach (string name in staged.Keys.OrderBy(n => n == Payload.Marker ? 1 : 0))
            {
                string target = ManagedPath(root, name);
                string before = ReadHash(target);
                if (before == staged[name]) continue;
                changes.Add(new Change {
                    Source = Payload.In(stage, name), Target = target, Before = before,
                    After = staged[name], Backup = before == null ? null : Payload.In(backupRoot, name)
                });
            }
            var applied = new List<Change>();
            try {
                foreach (var change in changes)
                {
                    applied.Add(change);
                    Directory.CreateDirectory(Path.GetDirectoryName(change.Target));
                    Action recheck = () => {
                        CheckHash(change.Source, change.After);
                        CheckHash(change.Target, change.Before);
                    };
                    if (change.Backup == null)
                        DirectoryOperations.Move(change.Source, change.Target, "补齐安装文件", operations, recheck, true);
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(change.Backup));
                        DirectoryOperations.Replace(change.Source, change.Target, change.Backup, "覆盖安装文件", operations, recheck);
                        CheckHash(change.Backup, change.Before);
                    }
                    CheckHash(change.Target, change.After);
                }
            } catch (Exception failure) {
                var rollbackErrors = new List<Exception>();
                for (int i = applied.Count - 1; i >= 0; i--)
                {
                    var change = applied[i];
                    try {
                        if (ReadHash(change.Target) == change.Before && !Directory.Exists(change.Target)) continue;
                        if (change.Backup == null) {
                            CheckHash(change.Target, change.After);
                            File.Delete(change.Target);
                        } else {
                            Action recheck = () => {
                                CheckHash(change.Backup, change.Before);
                                CheckHash(change.Target, change.After);
                            };
                            DirectoryOperations.Replace(change.Backup, change.Target, null, "还原安装文件", operations, recheck);
                        }
                    } catch (Exception error) { rollbackErrors.Add(error); }
                }
                if (rollbackErrors.Count != 0) {
                    rollbackErrors.Insert(0, failure);
                    throw new IOException("覆盖失败，部分文件未能还原。备份位置：" + backupRoot,
                                          new AggregateException(rollbackErrors));
                }
                throw;
            }
            return Directory.Exists(backupRoot) ? backupRoot : null;
        }
    }
}
