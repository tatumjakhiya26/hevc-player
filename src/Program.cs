using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace HevcPlayer
{
    internal static class Program
    {
        public const string AppName = "HevcPlayer";

        [STAThread]
        static int Main(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "--selftest")
                {
                    AttachConsole(-1);
                    return SelfTest.Run();
                }
                if (a == "--probe")
                {
                    AttachConsole(-1);
                    return SelfTest.Probe(i + 1 < args.Length ? args[i + 1] : null);
                }
                if (a == "--render")
                {
                    string path = (i + 1 < args.Length) ? args[i + 1] : "ui.png";
                    bool demo = false; string frame = null;
                    for (int j = i + 2; j < args.Length; j++)
                    {
                        if (args[j].Equals("demo", StringComparison.OrdinalIgnoreCase)) demo = true;
                        else { try { if (File.Exists(args[j])) frame = args[j]; } catch { } }
                    }
                    InitWinForms();
                    RenderToPng(path, demo, frame);
                    return 0;
                }
            }

            InitWinForms();
            string file = FirstFileArg(args);
            try { Installer.EnsureInstalled(); } catch { /* best-effort */ }
            Application.Run(new MainForm(file, false));
            return 0;
        }

        static string FirstFileArg(string[] args)
        {
            foreach (var a in args)
            {
                if (a.StartsWith("--")) continue;
                try { if (File.Exists(a)) return a; } catch { }
            }
            return null;
        }

        static void InitWinForms()
        {
            try { Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
        }

        static void RenderToPng(string path, bool demo = false, string frame = null)
        {
            using (var f = new MainForm(null, true))   // preview mode: chrome only, no mpv
            {
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-5000, -5000);
                f.ShowInTaskbar = false;
                f.Show();
                Application.DoEvents();
                if (demo) f.SetDemoState(frame);
                Application.DoEvents();
                Thread.Sleep(450);
                Application.DoEvents();
                using (var bmp = new Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                    bmp.Save(path, ImageFormat.Png);
                }
                f.Close();
            }
        }

        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int dwProcessId);
    }
}
