using System;
using System.IO;

namespace RcsMap {
    public static class ProjectExport {
        public static string Resolve(MapDocument document, string mapFile) {
            string value = document.ProjectIoc;
            if (String.IsNullOrWhiteSpace(value)) return null;
            if (Path.IsPathRooted(value)) return Path.GetFullPath(value);
            if (String.IsNullOrWhiteSpace(mapFile)) throw new IOException("此地图的相对工程路径没有基准，请重新关联工程。");
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(mapFile)), value.Replace('/', Path.DirectorySeparatorChar)));
        }
        public static void Remember(MapDocument document, string mapFile, string ioc) {
            document.ProjectIoc = String.IsNullOrWhiteSpace(ioc) ? null :
                String.IsNullOrWhiteSpace(mapFile) ? Path.GetFullPath(ioc) :
                ProjectBinding.Relative(Path.GetDirectoryName(Path.GetFullPath(mapFile)), ioc);
        }
        public static string Associate(string ioc) { return global::ProjectSetup.Apply(ioc, true); }
        public static void Save(MapDocument document, string oldFile, string newFile) {
            string previous = document.ProjectIoc;
            try { Remember(document, newFile, Resolve(document, oldFile)); Editor.AtomicSave(newFile, document.Json()); }
            catch { document.ProjectIoc = previous; throw; }
        }
        public static string Destination(string ioc) {
            return ProjectBinding.Inside(Path.GetDirectoryName(Path.GetFullPath(ioc)), ProjectBinding.RouteRelative);
        }
        public static string Export(MapDocument document, string ioc) {
            string destination=Destination(ioc);
            bool hardware=HardwareExport.Applies(document,destination);
            if(hardware)HardwareExport.Recover(document,destination,ProjectBinding.Load(Path.GetDirectoryName(Path.GetFullPath(ioc))));
            Exporter.Generate(document); // Validate the route before changing project settings.
            Exporter.CheckDestination(document, Destination(ioc));
            Associate(ioc);
            var binding = ProjectBinding.Load(Path.GetDirectoryName(Path.GetFullPath(ioc)));
            binding.Preflight();
            if(hardware) {
                HardwareExport.ExportBound(document,binding);
                return "应用、路线与硬件已导出并登记到关联工程：\r\n"+binding.RouteDirectory+
                    "\r\n硬件："+HardwareBinding.Relative+"\r\n"+(HardwareExport.LastReport??"CubeMX再次生成会自动补回硬件C/C++/头文件。");
            }
            Exporter.Export(document, binding.RouteDirectory);
            RevisionGuard.Bind(binding);
            string format = ProjectBinding.RequiredRouteFiles(binding.RouteDirectory, false).Length == 21 ? "21个文件" : "19个文件";
            try {
                var projects = binding.Projects();
                if (projects.Count == 0 || !ProjectBinding.HasChassis(ProjectBinding.ReadXml(projects[0])))
                    return "已导出路径" + format + "到：\r\n" + binding.RouteDirectory +
                        "\r\n尚未生成含底盘的Keil工程。重新打开.ioc并Generate Code，生成后工具会自动加入路径文件和包含目录。";
                foreach (string project in projects) global::KeilHeaders.Fix(project, message => { });
                return "已导出路径" + format + "，并自动加入Keil工程和包含目录：\r\n" + binding.RouteDirectory +
                    "\r\nKeil已打开时请重新加载工程；CubeMX再次生成会自动补回。main按使用说明调用App_Init/App_Task；业务在应用与状态机中编辑。";
            } catch (Exception error) {
                throw new IOException("路径文件已导出并保留，但Keil登记尚未完成：" + binding.RouteDirectory +
                    "。请关闭占用程序后重试导出，或重新Generate Code。原因：" + error.Message, error);
            }
        }
    }
}
