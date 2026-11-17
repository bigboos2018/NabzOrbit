using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.6",
        FrameworkDisplayName = ".NET Framework 4.6")]
[assembly: System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
[assembly: AssemblyTitle("Nabz Orbit")]
[assembly: AssemblyDescription("Guarded DNS trials and network-quality diagnostics for Windows")]
[assembly: AssemblyProduct("Nabz Orbit")]
[assembly: AssemblyVersion("2.1.0.0")]
[assembly: AssemblyFileVersion("2.1.0.0")]

namespace Nabz
{
internal static class Program
{
    [STAThread] static int Main(string[] args)
    {
        if (args.Length > 0)
            return (int)Worker.Run(args);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainWindow());
        return 0;
    }
    internal static Task<Code> Elevated(string args)
    {
        return Task.Run(delegate {
            if (!Adapters.IsWindows)
                return Code.BadInput;
            try {
                var si = new ProcessStartInfo(Application.ExecutablePath, args) {
                    UseShellExecute = true, Verb = "runas", WorkingDirectory = Environment.SystemDirectory
                };
                using (var p = Process.Start(si)) {
                    p.WaitForExit();
                    return Enum.IsDefined(typeof(Code), p.ExitCode) ? (Code)p.ExitCode : Code.Failed;
                }
            } catch (Win32Exception e) {
                return e.NativeErrorCode == 1223 ? Code.Cancelled : Code.Failed;
            } catch {
                return Code.Failed;
            }
        });
    }
}
}
