// 只读取构建产物的托管资源并输出SHA256，不执行被检查程序集中的入口。
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

static class AuditPayload
{
    static int Main(string[] args)
    {
        if (args.Length != 1) return 2;
        Console.OutputEncoding = new UTF8Encoding(false);
        var assembly = Assembly.Load(File.ReadAllBytes(args[0]));
        var hashes = new Dictionary<string, string>();
        foreach (string name in assembly.GetManifestResourceNames())
        {
            using (var stream = assembly.GetManifestResourceStream(name))
            using (var hash = SHA256.Create())
                hashes[name] = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        Console.WriteLine(new JavaScriptSerializer().Serialize(hashes));
        return 0;
    }
}
