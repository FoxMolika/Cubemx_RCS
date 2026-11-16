using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RcsMap
{
    /// <summary>源码装配的唯一入口：具名块决定结构，@参数@只替换一次。</summary>
    internal static class SourceTemplates
    {
        // 清单独立于模板正文；误删任何一个块时，平地和坡道导出都会明确失败。
        static readonly Dictionary<string, string[]> Slots = new Dictionary<string, string[]> {
            { "rcs_nav.c", new[] { "NAV_FAIL", "NAV_RESET", "NAV_START", "NAV_STOP",
                                  "NAV_INTEGRATE", "NAV_MOVE", "NAV_BIND" } },
            { "rcs_nav.h", new[] { "NAV_INCLUDE", "NAV_FAULT", "NAV_CONTEXT", "NAV_DECLARE" } },
            { "rcs_route.c", new[] { "TERRAIN_TABLES", "ROUTE_BIND" } },
            { "rcs_actions.c", new[] { "BRAKE_FUNCTION" } },
            { "rcs_actions.h", new[] { "BRAKE_DECLARE" } }
        };

        internal static string Substitute(string template, IDictionary<string, string> values)
        {
            return Regex.Replace(template, @"@([A-Z0-9_]+)@", match => {
                string value;
                if (!values.TryGetValue(match.Groups[1].Value, out value))
                    throw new InvalidOperationException("缺少模板参数：" + match.Value);
                return value;
            });
        }

        internal static string Render(string file, IDictionary<string, string> values, bool terrain)
        {
            bool navigation = file == "rcs_nav.c" || file == "rcs_nav.h";
            string resource = navigation ? file : "templates/" + file + ".tpl";
            string[] expected;
            if (!Slots.TryGetValue(file, out expected)) expected = new string[0];
            Dictionary<string, string> overrides = null;
            if (terrain && expected.Length > 0)
            {
                string fragments = "templates/terrain_" + file.Substring(4) + ".tpl";
                overrides = TemplateBlocks.Definitions(Exporter.Resource(fragments), fragments);
            }
            string assembled = TemplateBlocks.Render(Exporter.Resource(resource), resource, expected, overrides);
            return Substitute(assembled, values);
        }
    }
}
