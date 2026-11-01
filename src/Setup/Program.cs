using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(nint hwnd, string text, string title, uint type);
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var index = Array.IndexOf(args, "--target");
            var target = Path.GetFullPath(index >= 0 ? args[index + 1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "GDA-ZZZ"));
            var executable = Path.Combine(target, "GDA-ZZZ.exe");
            if (Process.GetProcessesByName("GDA-ZZZ").Any(x => string.Equals(x.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("请先从系统托盘退出 GDA-ZZZ，再运行安装程序。");
            Directory.CreateDirectory(target);
            using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.zip")!;
            using var zip = new ZipArchive(payload);
            zip.ExtractToDirectory(target, true);
            if (!args.Contains("--no-shortcut"))
            {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
                foreach (var folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.Programs) })
                {
                    dynamic shortcut = shell.CreateShortcut(Path.Combine(folder, "GDA-ZZZ.lnk"));
                    shortcut.TargetPath = executable;
                    shortcut.WorkingDirectory = target;
                    shortcut.Description = "GDA · 绝区零日常任务";
                    shortcut.Save();
                    Marshal.FinalReleaseComObject(shortcut);
                }
                Marshal.FinalReleaseComObject(shell);
            }
            if (!args.Contains("--quiet")) MessageBox(0, "GDA-ZZZ 已安装。\n打开桌面上的 GDA-ZZZ，即可安装绝区零 Provider、启动游戏并连接手机。\n\n安装位置：" + target, "GDA-ZZZ", 0x40);
            return 0;
        }
        catch (Exception error)
        {
            if (args.Contains("--quiet")) File.WriteAllText(Path.Combine(Path.GetTempPath(), "gda-zzz-setup-error.txt"), error.ToString());
            else MessageBox(0, error.Message, "GDA-ZZZ 安装失败", 0x10);
            return 1;
        }
    }
}
