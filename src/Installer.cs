using System;
using System.Diagnostics;
using System.IO;

namespace HevcPlayer
{
    // First-run self-install: a stable copy in %LOCALAPPDATA% + Desktop/Start-Menu
    // shortcuts, so the user never has to hunt for the exe again.
    internal static class Installer
    {
        public static string InstallDir => HevcEngine.InstallDir;
        public static string InstalledExePath => Path.Combine(InstallDir, "HevcPlayer.exe");
        public static string CurrentExePath =>
            Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule.FileName;

        public static void EnsureInstalled()
        {
            Directory.CreateDirectory(InstallDir);

            string current = CurrentExePath;
            if (!PathEquals(current, InstalledExePath))
            {
                try { File.Copy(current, InstalledExePath, true); }
                catch { /* locked or already there — fine */ }
            }

            string target = File.Exists(InstalledExePath) ? InstalledExePath : current;
            CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Program.AppName + ".lnk"), target);
            CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                Program.AppName + ".lnk"), target);
        }

        static bool PathEquals(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        static void CreateShortcut(string lnkPath, string targetExe)
        {
            try
            {
                if (File.Exists(lnkPath)) return;
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return;
                dynamic shell = Activator.CreateInstance(t);
                dynamic sc = shell.CreateShortcut(lnkPath);
                sc.TargetPath = targetExe;
                sc.WorkingDirectory = Path.GetDirectoryName(targetExe);
                sc.IconLocation = targetExe + ",0";
                sc.Description = "Play HEVC / H.265, MKV, 4K and AV1 video";
                sc.Save();
            }
            catch { }
        }
    }
}
