using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RcsMap
{
    /// <summary>
    /// C 注释标记定义可替换块，默认正文仍能作为普通 C 编译。
    /// 每个插槽必须恰好出现一次；模板漂移须在写文件之前报告，不能静默漏接保护逻辑。
    /// </summary>
    internal static class TemplateBlocks
    {
        const string Prefix = "/* RCS TEMPLATE ";
        static readonly Regex Marker = new Regex(
            @"(?m)^/\* RCS TEMPLATE (BEGIN|END) ([A-Z0-9_]+) \*/\n");

        internal sealed class Block
        {
            internal string Name, Body;
            internal int Start, End;
        }

        static string LF(string text) { return text.Replace("\r\n", "\n").Replace("\r", "\n"); }

        static List<Block> Parse(string source, string label)
        {
            var markers = Marker.Matches(source).Cast<Match>().ToList();
            if (markers.Count != Regex.Matches(source, Regex.Escape(Prefix)).Count)
                throw new InvalidOperationException(label + "：模板标记格式错误。");

            var result = new List<Block>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < markers.Count; i += 2)
            {
                Match begin = markers[i];
                if (i + 1 == markers.Count || begin.Groups[1].Value != "BEGIN")
                    throw new InvalidOperationException(label + "：模板块未配对。");
                Match end = markers[i + 1];
                string name = begin.Groups[2].Value;
                if (end.Groups[1].Value != "END" || end.Groups[2].Value != name || !names.Add(name))
                    throw new InvalidOperationException(label + "：模板块嵌套、重复或结束名称不一致：" + name);
                result.Add(new Block {
                    Name = name,
                    Start = begin.Index,
                    End = end.Index + end.Length,
                    Body = source.Substring(begin.Index + begin.Length, end.Index - begin.Index - begin.Length)
                });
            }
            return result;
        }

        /// <summary>读取独立坡道片段。块外只允许空白，避免遗漏片段文件中的有效代码。</summary>
        internal static Dictionary<string, string> Definitions(string text, string label)
        {
            string source = LF(text);
            var blocks = Parse(source, label);
            int cursor = 0;
            foreach (var block in blocks)
            {
                if (!String.IsNullOrWhiteSpace(source.Substring(cursor, block.Start - cursor)))
                    throw new InvalidOperationException(label + "：片段块外存在未使用内容。");
                cursor = block.End;
            }
            if (!String.IsNullOrWhiteSpace(source.Substring(cursor)))
                throw new InvalidOperationException(label + "：片段末尾存在未使用内容。");
            return blocks.ToDictionary(b => b.Name, b => b.Body, StringComparer.Ordinal);
        }

        /// <summary>
        /// 先核对完整插槽清单，再替换原模板中的块。插入正文不再次扫描，因此用户文本不充当模板。
        /// overrides 为空时使用默认平地正文；坡道须覆盖该文件的全部插槽。
        /// </summary>
        internal static string Render(string text, string label, string[] expected,
                                      IDictionary<string, string> overrides)
        {
            string source = LF(text);
            var blocks = Parse(source, label);
            var required = new HashSet<string>(expected, StringComparer.Ordinal);
            if (required.Count != expected.Length || !required.SetEquals(blocks.Select(b => b.Name)) ||
                (overrides != null && !required.SetEquals(overrides.Keys)))
                throw new InvalidOperationException(label + "：模板插槽清单不匹配，未导出。");

            var output = new StringBuilder();
            int cursor = 0;
            foreach (var block in blocks)
            {
                output.Append(source, cursor, block.Start - cursor);
                output.Append(overrides == null ? block.Body : overrides[block.Name]);
                cursor = block.End;
            }
            return output.Append(source, cursor, source.Length - cursor).ToString();
        }
    }
}
