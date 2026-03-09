using System.IO;
using System.Reflection;

namespace ElanAP
{
    static class Info
    {
        public static string AssemblyVersion
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version.ToString(); }
        }

        public static string DefaultConfigPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
            "default.cfg");

        public static string GitHub = @"https://github.com/QuanNewData/ElanAP";

        public class Discord
        {
            public static string Tag = "";
        }
    }
}
