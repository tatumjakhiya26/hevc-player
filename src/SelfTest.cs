using System;
using System.IO;
using System.Reflection;

namespace HevcPlayer
{
    // Headless verification (no desktop needed). Output teed to %TEMP%.
    //   --selftest        libmpv extracts, loads, mpv_create works
    //   --probe <file>     really open a file headless and read codec/size/duration
    internal static class SelfTest
    {
        static StreamWriter _w;
        static void Open(string file)
        {
            try { _w = new StreamWriter(Path.Combine(Path.GetTempPath(), file), false) { AutoFlush = true }; }
            catch { _w = null; }
        }
        static void Line(string s)
        {
            try { Console.WriteLine(s); } catch { }
            try { _w?.WriteLine(s); } catch { }
        }
        static void Close() { try { _w?.Flush(); _w?.Dispose(); } catch { } _w = null; }

        public static int Run()
        {
            Open("hevc_selftest.log");
            int pass = 0, fail = 0;
            void Check(string name, bool ok)
            {
                if (ok) { pass++; Line("  OK   " + name); }
                else { fail++; Line(" FAIL  " + name); }
            }

            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var s = asm.GetManifestResourceStream("libmpv-2.dll"))
                    Check("embedded libmpv-2.dll present", s != null && s.Length > 10_000_000);

                HevcEngine.EnsureLib();
                Check("extract libmpv-2.dll",
                    File.Exists(HevcEngine.LibmpvPath) && new FileInfo(HevcEngine.LibmpvPath).Length > 10_000_000);

                using (var p = new MpvPlayer())
                {
                    p.Init(IntPtr.Zero, headless: true);
                    Check("mpv create + initialize", p.Ok);
                }

                Check("install dir resolves", !string.IsNullOrEmpty(HevcEngine.InstallDir));

                Line("");
                Line(fail == 0 ? ("SELFTEST_OK (" + pass + " checks)")
                               : ("SELFTEST_FAIL (" + fail + " failed, " + pass + " passed)"));
            }
            catch (Exception ex) { Line("SELFTEST_EXCEPTION " + ex); fail++; }

            Close();
            return fail == 0 ? 0 : 1;
        }

        public static int Probe(string file)
        {
            Open("hevc_probe.log");
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
            {
                Line("PROBE: file not found: " + file);
                Close();
                return 2;
            }
            try
            {
                using (var p = new MpvPlayer())
                {
                    p.Init(IntPtr.Zero, headless: true);
                    if (!p.Ok) { Line("PROBE_FAIL mpv init"); Close(); return 1; }

                    p.LoadFile(file);
                    int r = p.WaitFileLoaded(20);
                    if (r != 0) { Line("PROBE_FAIL file not loaded (code " + r + ")"); Close(); return 1; }

                    string codec = p.GetString("video-codec");
                    double w = p.GetDouble("width");
                    double h = p.GetDouble("height");
                    double dur = p.GetDouble("duration");
                    Line("  video-codec = " + codec);
                    Line("  size        = " + (int)w + "x" + (int)h);
                    Line("  duration    = " + dur.ToString("0.00") + "s");
                    Line("PROBE_OK");
                }
                Close();
                return 0;
            }
            catch (Exception ex) { Line("PROBE_EXCEPTION " + ex.Message); Close(); return 1; }
        }
    }
}
