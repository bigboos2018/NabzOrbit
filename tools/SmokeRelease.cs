// Run on the shipping EXE (not a test build). On Mono, invoke its real entry point.
// On Windows, suppress the startup survey: GUI and adapter inventory only, even if a NIC exists.
using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using System.Windows.Forms;
class SmokeRelease
{
    static IEnumerable<Control> Walk(Control c)
    {
        foreach (Control t in c.Controls) {
            yield return t;
            foreach (var sub in Walk(t))yield return sub;
        }
    }
    [STAThread] static int Main(string[] args)
    {
        if (args.Length != 1)return 2;
        string path = Path.GetFullPath(args[0]);
        var asm = Assembly.LoadFrom(path);
        PortableExecutableKinds pe;
        ImageFileMachine arch;
        asm.ManifestModule.GetPEKind(out pe, out arch);
        if (asm.GetName().Version.ToString() != "2.1.0.0" || (pe & PortableExecutableKinds.ILOnly) == 0 ||
                (pe & PortableExecutableKinds.Required32Bit) != 0)throw new Exception("unexpected managed binary");
        if (!Encoding.ASCII.GetString(File.ReadAllBytes(path)).Contains("level=\"asInvoker\""))throw new Exception("missing asInvoker");
        string[] names = {"Nabz.Catalog", "Orbit.Cover", "Orbit.Regular", "Orbit.Bold", "Orbit.FontLicense"};
        if (!names.All(asm.GetManifestResourceNames().Contains))throw new Exception("missing embedded resource");
        bool windows = Environment.OSVersion.Platform == PlatformID.Win32NT;
        if (windows) {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
        }
        bool seen = false, okay = false;
        var start = DateTime.UtcNow;
        using (var t = new Timer {Interval = 220}) {
            t.Tick += delegate {
                var form = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.GetType().FullName == "Nabz.MainWindow");
                if (form == null) {
                    if (DateTime.UtcNow - start > TimeSpan.FromSeconds(8))Application.Exit();
                    return;
                }
                seen = true;
                var g = Walk(form).OfType<DataGridView>().Single();
                var opt = Walk(form).OfType<CheckBox>().Single();
                okay = g.Rows.Count == 12 && g.Columns.Count == 8 && !opt.Checked &&
                       g.Rows.Cast<DataGridViewRow>().All(r => Convert.ToString(r.Cells[5].Value) == "سنجیده نشده");
                Console.WriteLine("Release {0}; {1}/{2}; asInvoker; {3} unmeasured rows; {4} columns; private opt-in={5}", asm.GetName().Version,
                                  pe, arch, g.Rows.Count, g.Columns.Count, opt.Checked);
                t.Stop();
                form.Close();
            };
            EventHandler idle = null;
            idle = delegate {
                Application.Idle -= idle;
                t.Start();
            };
            Application.Idle += idle;
            if (windows) {
                using (var f = (Form)Activator.CreateInstance(asm.GetType("Nabz.MainWindow", true), BindingFlags.Instance | BindingFlags.NonPublic,
                               null, new object[] {false}, null))Application.Run(f);
            } else {
                var code = asm.EntryPoint.Invoke(null, new object[] {new string[0]});
                if (!Object.Equals(code, 0))return 3;
            }
        }
        Console.WriteLine("Release GUI open / inspect / close: " + (seen && okay ? "PASS" : "FAIL"));
        return seen && okay ? 0 : 1;
    }
}
