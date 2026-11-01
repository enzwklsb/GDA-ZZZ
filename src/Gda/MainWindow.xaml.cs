using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media.Imaging;
using QRCoder;
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;

namespace Gda;

public partial class MainWindow : Window
{
    private readonly Engine engine;
    private readonly System.Windows.Forms.NotifyIcon tray;
    private string qrCode = "";
    private string lastLog = "";
    private bool refreshing;
    public MainWindow(Engine engine)
    {
        InitializeComponent();
        this.engine = engine;
        tray = new System.Windows.Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "GDA-ZZZ · 绝区零", Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); });
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("打开 GDA", null, (_, _) => Dispatcher.Invoke(() => { Show(); WindowState = WindowState.Normal; Activate(); }));
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(() => Application.Current.Shutdown()));
        tray.ContextMenuStrip = menu;
        Closing += (_, e) => { if (!((App)Application.Current).Exiting) { e.Cancel = true; Hide(); } };
        Closed += (_, _) => tray.Dispose();
        engine.Changed += ScheduleRefresh;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Refresh(); timer.Start();
        Refresh();
    }
    private void ScheduleRefresh()
    {
        lock (this)
        {
            if (refreshing) return;
            refreshing = true;
        }
        Dispatcher.BeginInvoke(() => { lock (this) refreshing = false; Refresh(); });
    }
    private void Refresh()
    {
        var provider = engine.Provider;
        ProviderStatus.Text = provider.Installed ? "已安装 · OneDragon " + Provider.Version : provider.State switch { "DOWNLOADING" => "正在下载… " + provider.Progress + "%", "INSTALLING" => "正在安装环境…", "FAILED" => "安装失败，可点击重试", _ => "尚未安装" };
        InstallButton.IsEnabled = provider.Installing is not { IsCompleted: false };
        InstallButton.Content = provider.Installed ? "Provider 已安装" : "安装绝区零 Provider";
        InstallProgress.Value = provider.Installed ? 100 : provider.Progress;
        GamePath.Text = string.IsNullOrEmpty(engine.Settings.GamePath) ? "未找到游戏，请选择 ZenlessZoneZero.exe。" : engine.Settings.GamePath;
        var task = engine.History.LastOrDefault(x => x.Active) ?? engine.History.LastOrDefault();
        var status = task?.Status switch { "queued" => "等待执行", "starting" => "正在启动", "running" => "正在执行", "stopping" => "正在停止", "succeeded" => "已完成", "failed" => "执行失败", "cancelled" => "已取消", "stopped" => "已停止", "interrupted" => "已中断", _ => "" };
        var step = task?.CurrentStep != null && Engine.Tasks.TryGetValue(task.CurrentStep, out var name) ? name : "";
        TaskStatus.Text = task == null ? "选择任务后点击开始，也可在手机上提交。" : $"{task.Summary} · {status} {step}";
        var log = engine.LogText;
        if (log != lastLog) { lastLog = log; Logs.Text = log; Logs.ScrollToEnd(); }
        PhoneStatus.Text = engine.PhoneStatus;
        var path = Path.Combine(engine.Root, "pairing.json");
        if (!File.Exists(path)) return;
        try
        {
            var code = File.ReadAllText(path);
            if (code == qrCode) return;
            qrCode = code;
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(code, QRCodeGenerator.ECCLevel.M);
            var bytes = new PngByteQRCode(data).GetGraphic(5);
            var bitmap = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit();
            Qr.Source = bitmap;
        }
        catch (IOException) { }
    }
    private async void Install_Click(object sender, RoutedEventArgs e) { ErrorText.Text = ""; await engine.Provider.Install(); ErrorText.Text = engine.Provider.Error ?? ""; }
    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        try { ErrorText.Text = ""; await engine.LaunchGame(); }
        catch (Exception ex) { ErrorText.Text = ex.Message; engine.Log(ex.Message); }
    }
    private void Choose_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "选择绝区零游戏文件", Filter = "ZenlessZoneZero.exe|ZenlessZoneZero.exe", FileName = "ZenlessZoneZero.exe" };
        if (dialog.ShowDialog(this) == true) { engine.Settings.GamePath = dialog.FileName; engine.SaveSettings(); Refresh(); }
    }
    private void Start_Click(object sender, RoutedEventArgs e) => Act(() =>
    {
        var tasks = new JsonArray();
        if (Video.IsChecked == true) tasks.Add("random_play");
        if (Scratch.IsChecked == true) tasks.Add("scratch_card");
        if (Coffee.IsChecked == true) tasks.Add("coffee");
        if (Rewards.IsChecked == true) tasks.Add("engagement_reward");
        engine.Submit(new JsonObject { ["gameId"] = "zzz", ["taskIds"] = tasks });
    });
    private void Stop_Click(object sender, RoutedEventArgs e) => Act(() => { foreach (var item in engine.History.Where(x => x.Active)) engine.Stop(item.TaskId); });
    private void Copy_Click(object sender, RoutedEventArgs e) => Act(() => { if (qrCode.Length == 0) throw new InvalidOperationException(engine.PhoneStatus); Clipboard.SetText(qrCode); });
    private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
    private void Act(Action action) { try { ErrorText.Text = ""; action(); Refresh(); } catch (Exception ex) { ErrorText.Text = ex.Message; engine.Log(ex.Message); } }
}
