using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace HevcPlayer
{
    // Where the bundled libmpv-2.dll lives after first-run extraction.
    internal static class HevcEngine
    {
        public static string InstallDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HevcPlayer");

        public static string BinDir => Path.Combine(InstallDir, "bin");
        public static string LibmpvPath => Path.Combine(BinDir, "libmpv-2.dll");

        public static void EnsureLib()
        {
            Directory.CreateDirectory(BinDir);
            if (File.Exists(LibmpvPath) && new FileInfo(LibmpvPath).Length > 1_000_000) return;

            var asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream("libmpv-2.dll"))
            {
                if (s == null) throw new FileNotFoundException("Embedded libmpv-2.dll missing");
                string tmp = LibmpvPath + ".tmp";
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                    s.CopyTo(fs);
                if (File.Exists(LibmpvPath)) File.Delete(LibmpvPath);
                File.Move(tmp, LibmpvPath);
            }
        }
    }

    // Minimal P/Invoke to the mpv client API (https://github.com/mpv-player/mpv/blob/master/include/mpv/client.h)
    internal static class NativeMpv
    {
        const string LIB = "libmpv-2.dll";

        // mpv_format
        public const int MPV_FORMAT_STRING = 1;
        public const int MPV_FORMAT_FLAG   = 3;
        public const int MPV_FORMAT_INT64  = 4;
        public const int MPV_FORMAT_DOUBLE = 5;

        // mpv_event_id
        public const int MPV_EVENT_NONE        = 0;
        public const int MPV_EVENT_SHUTDOWN    = 1;
        public const int MPV_EVENT_END_FILE    = 7;
        public const int MPV_EVENT_FILE_LOADED = 8;

        static IntPtr _handle;

        static NativeMpv()
        {
            NativeLibrary.SetDllImportResolver(typeof(NativeMpv).Assembly, (name, asm, path) =>
            {
                if (name == LIB || name == "libmpv-2" || name == "mpv-2")
                {
                    if (_handle == IntPtr.Zero)
                    {
                        HevcEngine.EnsureLib();
                        _handle = NativeLibrary.Load(HevcEngine.LibmpvPath);
                    }
                    return _handle;
                }
                return IntPtr.Zero;
            });
        }

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_create();

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_initialize(IntPtr ctx);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_terminate_destroy(IntPtr ctx);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_option(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, ref long data);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_option_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_set_property_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string data);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_get_property_string(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_get_property(IntPtr ctx, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, out double data);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern void mpv_free(IntPtr data);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern int mpv_command(IntPtr ctx, IntPtr args);

        [DllImport(LIB, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);
    }

    internal sealed class MpvPlayer : IDisposable
    {
        IntPtr ctx;
        public bool Ok => ctx != IntPtr.Zero;

        // Create the mpv context and (optionally) embed it into a native window handle.
        public void Init(IntPtr wid, bool headless)
        {
            HevcEngine.EnsureLib();
            ctx = NativeMpv.mpv_create();
            if (ctx == IntPtr.Zero) throw new Exception("mpv_create failed");

            Opt("config", "no");
            Opt("terminal", "no");
            Opt("msg-level", "all=no");
            Opt("input-default-bindings", "yes");
            Opt("input-vo-keyboard", "yes");
            Opt("keep-open", "yes");
            Opt("hwdec", "auto-safe");          // use GPU decode when available, fall back to software
            Opt("ytdl", "no");

            if (headless)
            {
                Opt("vo", "null");
                Opt("ao", "null");
            }
            else
            {
                Opt("osc", "no");               // we draw our own controls
                Opt("force-window", "yes");
                if (wid != IntPtr.Zero)
                {
                    long w = wid.ToInt64();
                    NativeMpv.mpv_set_option(ctx, "wid", NativeMpv.MPV_FORMAT_INT64, ref w);
                }
            }

            NativeMpv.mpv_initialize(ctx);
        }

        void Opt(string name, string val) => NativeMpv.mpv_set_option_string(ctx, name, val);

        public void SetProp(string name, string val)
        {
            if (ctx != IntPtr.Zero) NativeMpv.mpv_set_property_string(ctx, name, val);
        }

        public string GetString(string name)
        {
            if (ctx == IntPtr.Zero) return null;
            IntPtr p = NativeMpv.mpv_get_property_string(ctx, name);
            if (p == IntPtr.Zero) return null;
            string s = Marshal.PtrToStringUTF8(p);
            NativeMpv.mpv_free(p);
            return s;
        }

        public double GetDouble(string name)
        {
            if (ctx == IntPtr.Zero) return 0;
            double d;
            return NativeMpv.mpv_get_property(ctx, name, NativeMpv.MPV_FORMAT_DOUBLE, out d) == 0 ? d : 0;
        }

        public int Command(params string[] args)
        {
            if (ctx == IntPtr.Zero) return -1;
            IntPtr[] ptrs = new IntPtr[args.Length + 1];
            for (int i = 0; i < args.Length; i++) ptrs[i] = Utf8(args[i]);
            ptrs[args.Length] = IntPtr.Zero;
            GCHandle h = GCHandle.Alloc(ptrs, GCHandleType.Pinned);
            int r;
            try { r = NativeMpv.mpv_command(ctx, h.AddrOfPinnedObject()); }
            finally
            {
                h.Free();
                foreach (var p in ptrs) if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
            }
            return r;
        }

        static IntPtr Utf8(string s)
        {
            byte[] b = Encoding.UTF8.GetBytes((s ?? "") + "\0");
            IntPtr p = Marshal.AllocHGlobal(b.Length);
            Marshal.Copy(b, 0, p, b.Length);
            return p;
        }

        // ---- high-level helpers ----
        public void LoadFile(string path) => Command("loadfile", path, "replace");
        public void AppendFile(string path) => Command("loadfile", path, "append");
        public void PlaylistNext() => Command("playlist-next", "weak");
        public void PlaylistPrev() => Command("playlist-prev", "weak");
        public void CycleSubs() => Command("cycle", "sub");
        public void CycleAudio() => Command("cycle", "audio");
        public void SeekRelative(double sec) => Command("seek", sec.ToString(System.Globalization.CultureInfo.InvariantCulture), "relative");
        public void SeekAbsolute(double sec) => Command("seek", sec.ToString(System.Globalization.CultureInfo.InvariantCulture), "absolute");

        public bool Paused
        {
            get { return GetString("pause") == "yes"; }
            set { SetProp("pause", value ? "yes" : "no"); }
        }
        public void TogglePause() => Command("cycle", "pause");

        public double Volume { set { SetProp("volume", ((int)value).ToString()); } }
        public bool Mute { set { SetProp("mute", value ? "yes" : "no"); } }

        public double TimePos => GetDouble("time-pos");
        public double Duration => GetDouble("duration");

        // Wait (blocking) until the current file is loaded or fails — used by headless --probe.
        public int WaitFileLoaded(double timeoutSec)
        {
            double deadline = timeoutSec;
            while (deadline > 0)
            {
                IntPtr ev = NativeMpv.mpv_wait_event(ctx, 0.5);
                int id = Marshal.ReadInt32(ev);
                if (id == NativeMpv.MPV_EVENT_FILE_LOADED) return 0;
                if (id == NativeMpv.MPV_EVENT_END_FILE) return 2;
                if (id == NativeMpv.MPV_EVENT_SHUTDOWN) return 3;
                deadline -= 0.5;
            }
            return 1; // timeout
        }

        public void Dispose()
        {
            if (ctx != IntPtr.Zero)
            {
                try { NativeMpv.mpv_terminate_destroy(ctx); } catch { }
                ctx = IntPtr.Zero;
            }
        }
    }
}
