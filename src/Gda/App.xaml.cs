using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using Application = System.Windows.Application;

namespace Gda;

public partial class App : Application
{
    public bool Exiting { get; private set; }
    private Engine? engine;
    private Api? api;
    private Process? phone;
    private Mutex? instance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var index = Array.IndexOf(e.Args, "--data");
        var root = index >= 0 ? e.Args[index + 1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GDA-ZZZ");
        instance = new Mutex(true, "Local\\GDA-ZZZ-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root))))[..16], out var created);
        if (!created) { System.Windows.MessageBox.Show("GDA-ZZZ 已经运行，请从系统托盘打开。", "GDA-ZZZ"); Shutdown(); return; }
        try
        {
            engine = new Engine(root);
            if (!e.Args.Contains("--headless")) new MainWindow(engine).Show();
            else ShutdownMode = ShutdownMode.OnExplicitShutdown;
            api = new Api(engine);
            await api.Start();
            engine.Write("api.json", new { url = api.Url, key = api.Key });
            StartPhone();
        }
        catch (Exception ex)
        {
            if (engine != null) engine.Log("启动失败：" + ex.Message);
            else { System.Windows.MessageBox.Show(ex.Message, "GDA-ZZZ"); Shutdown(1); }
        }
    }
    private void StartPhone()
    {
        try
        {
            var path = Path.Combine(engine!.Root, "pairing.json");
            File.Delete(path);
            var info = Engine.ProcessInfo(Path.Combine(AppContext.BaseDirectory, "gda-phone.exe"));
            info.RedirectStandardInput = true;
            info.Environment["GDA_DATA"] = engine.Root;
            info.Environment["GDA_URL"] = api!.Url;
            info.Environment["GDA_KEY"] = api.Key;
            phone = new Process { StartInfo = info, EnableRaisingEvents = true };
            phone.OutputDataReceived += (_, e) =>
            {
                if (e.Data == "READY") { engine.PhoneStatus = "在手机 App 的连接电脑页面扫描此码。"; engine.Notify(); }
            };
            phone.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) engine.Log("手机连接：" + e.Data); };
            phone.Exited += (_, _) => { if (!Exiting) { engine.PhoneStatus = "手机连接服务已退出，桌面仍可使用。退出并重新打开 GDA 可重连。"; engine.Log(engine.PhoneStatus); } };
            phone.Start(); phone.BeginOutputReadLine(); phone.BeginErrorReadLine();
        }
        catch (Exception ex) { engine!.PhoneStatus = "手机连接暂不可用：" + ex.Message; engine.Log(engine.PhoneStatus); }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        Exiting = true;
        engine?.Dispose();
        try { if (phone is { HasExited: false }) phone.Kill(true); } catch (InvalidOperationException) { }
        phone?.Dispose();
        // The OS releases the in-process HTTP listener on exit. Blocking on its
        // asynchronous disposal here would deadlock the WPF dispatcher.
        instance?.Dispose();
        base.OnExit(e);
    }
}
