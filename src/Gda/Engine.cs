using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gda;

public sealed class RunItem
{
    public string TaskId { get; set; } = Guid.NewGuid().ToString("N");
    public string RequestId { get; set; } = "";
    public string GameId { get; set; } = "zzz";
    public string Adapter { get; set; } = "onedragon";
    public string Status { get; set; } = "queued";
    public string Summary { get; set; } = "";
    public string? CurrentStep { get; set; }
    public string CreatedAt { get; set; } = DateTimeOffset.Now.ToString("O");
    public string? StartedAt { get; set; }
    public string? FinishedAt { get; set; }
    public string? Error { get; set; }
    public int? QueuePosition { get; set; }
    public JsonObject Body { get; set; } = new();
    public bool Active => Status is "queued" or "starting" or "running" or "stopping";
}

public sealed class Settings
{
    public string GamePath { get; set; } = "";
}

public sealed class Engine : IDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static readonly Dictionary<string, string> Tasks = new()
    {
        ["random_play"] = "影像店", ["scratch_card"] = "刮刮乐", ["coffee"] = "咖啡",
        ["engagement_reward"] = "日常奖励", ["ridu_weekly"] = "丽都周纪", ["city_fund"] = "丽都城募",
        ["daily_signin"] = "每日签到"
    };
    public readonly string Root;
    public readonly Provider Provider;
    public Settings Settings { get; }
    public event Action? Changed;
    public string PhoneStatus { get; set; } = "手机连接正在启动…";
    private readonly object gate = new();
    private readonly List<RunItem> history;
    private readonly Queue<string> logs = new();
    private Process? runner;
    private bool pumping;
    private bool disposed;
    public Engine(string root)
    {
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Root);
        Settings = Read<Settings>("settings.json") ?? new();
        history = Read<List<RunItem>>("tasks.json") ?? [];
        foreach (var item in history.Where(x => x.Active))
        {
            item.Status = "interrupted";
            item.Error = "电脑端已退出，任务未自动重跑。可重新提交。";
            item.FinishedAt = DateTimeOffset.Now.ToString("O");
        }
        if (!File.Exists(Settings.GamePath)) Settings.GamePath = FindGame();
        Provider = new Provider(this);
        SaveSettings();
        SaveHistory();
    }
    private T? Read<T>(string name)
    {
        var path = Path.Combine(Root, name);
        if (!File.Exists(path)) return default;
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json);
    }
    public void Write(string name, object value)
    {
        var path = Path.Combine(Root, name);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value, Json));
        File.Move(path + ".tmp", path, true);
    }
    public void SaveSettings() => Write("settings.json", Settings);
    private void SaveHistory() => Write("tasks.json", history);
    public void Notify() => Changed?.Invoke();
    public void Log(string text)
    {
        lock (gate)
        {
            var line = $"{DateTime.Now:HH:mm:ss}  {text}";
            logs.Enqueue(line);
            while (logs.Count > 300) logs.Dequeue();
            File.AppendAllText(Path.Combine(Root, "gda.log"), line + Environment.NewLine);
        }
        Notify();
    }
    public string LogText { get { lock (gate) return string.Join(Environment.NewLine, logs); } }
    public RunItem[] History { get { lock (gate) return history.ToArray(); } }
    public RunItem? Find(string id) { lock (gate) return history.FirstOrDefault(x => x.TaskId == id || x.RequestId == id); }
    public bool Busy { get { lock (gate) return history.Any(x => x.Active); } }
    public RunItem Submit(JsonObject body)
    {
        lock (gate)
        {
            var requestId = body["requestId"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N");
            var previous = history.FirstOrDefault(x => x.RequestId == requestId);
            if (previous != null) return previous;
            Validate(body);
            var ids = body["taskIds"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
            var item = new RunItem { RequestId = requestId, Summary = string.Join("、", ids.Select(x => Tasks[x])), Body = (JsonObject)body.DeepClone() };
            history.Add(item);
            SaveHistory();
            Log("已接收：" + item.Summary);
            if (!pumping) { pumping = true; _ = Task.Run(Pump); }
            return item;
        }
    }
    public static void Validate(JsonObject body)
    {
        if (body["gameId"]?.GetValue<string>() != "zzz") throw new InvalidOperationException("第一版只支持绝区零。");
        var ids = body["taskIds"]?.AsArray();
        if (ids == null || ids.Count == 0) throw new InvalidOperationException("请选择一个绝区零任务。");
        foreach (var id in ids)
            if (!Tasks.ContainsKey(id?.GetValue<string>() ?? "")) throw new InvalidOperationException("第一版尚未支持此任务：" + id);
    }
    private async Task Pump()
    {
        while (true)
        {
            RunItem item;
            lock (gate)
            {
                var next = history.FirstOrDefault(x => x.Status == "queued");
                if (next == null || disposed) { pumping = false; return; }
                item = next;
                item.Status = "starting";
                item.StartedAt = DateTimeOffset.Now.ToString("O");
                SaveHistory();
            }
            Notify();
            try
            {
                if (!Provider.Installed) throw new InvalidOperationException("请先安装绝区零 Provider。");
                if (!File.Exists(Settings.GamePath)) throw new InvalidOperationException("未找到绝区零，请选择游戏的 ZenlessZoneZero.exe。");
                await LaunchGame();
                var requestPath = Path.Combine(Root, "run.json");
                var stopPath = Path.Combine(Root, "stop");
                File.Delete(stopPath);
                var request = (JsonObject)item.Body.DeepClone();
                request["gamePath"] = Settings.GamePath;
                request["stopFile"] = stopPath;
                File.WriteAllText(requestPath, request.ToJsonString(Json));
                var start = ProcessInfo(Provider.Python, "-u", Path.Combine(AppContext.BaseDirectory, "provider", "runner.py"), "run", Provider.Path, requestPath);
                start.WorkingDirectory = Provider.Path;
                start.Environment["PYTHONIOENCODING"] = "utf-8";
                start.Environment["PYTHONUTF8"] = "1";
                using var process = new Process { StartInfo = start };
                process.OutputDataReceived += (_, e) => RunnerLine(item, e.Data);
                process.ErrorDataReceived += (_, e) => RunnerLine(item, e.Data);
                lock (gate)
                {
                    if (item.Status == "stopping") throw new OperationCanceledException();
                    process.Start(); runner = process; item.Status = "running";
                    SaveHistory();
                }
                process.BeginOutputReadLine(); process.BeginErrorReadLine(); Notify();
                await process.WaitForExitAsync();
                process.WaitForExit();
                lock (gate)
                {
                    runner = null;
                    if (item.Status == "stopping") item.Status = "stopped";
                    else if (process.ExitCode == 0) item.Status = "succeeded";
                    else { item.Status = "failed"; item.Error ??= "OneDragon 执行失败，退出码 " + process.ExitCode + "。请查看日志。"; }
                }
                if (item.Status == "succeeded" && item.Body["closeGameAfterFinish"]?.GetValue<bool>() == true) CloseGame();
            }
            catch (Exception ex)
            {
                lock (gate)
                {
                    item.Status = item.Status == "stopping" ? "stopped" : "failed";
                    item.Error = ex is OperationCanceledException ? null : ex.Message;
                    runner = null;
                }
            }
            lock (gate)
            {
                item.FinishedAt = DateTimeOffset.Now.ToString("O");
                SaveHistory();
            }
            Log($"{item.Summary}：{item.Status}{(item.Error == null ? "" : " · " + item.Error)}");
        }
    }
    private void RunnerLine(RunItem item, string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        if (line.StartsWith("GDA_STEP=")) { lock (gate) item.CurrentStep = line[9..]; }
        if (line.StartsWith("GDA_ERROR=")) { lock (gate) item.Error = line[10..]; }
        Log(line);
    }
    public RunItem Stop(string id)
    {
        lock (gate)
        {
            var item = Find(id) ?? throw new KeyNotFoundException("任务不存在。");
            if (item.Status == "queued") { item.Status = "cancelled"; item.FinishedAt = DateTimeOffset.Now.ToString("O"); }
            else if (item.Status is "starting" or "running")
            {
                item.Status = "stopping";
                File.WriteAllText(Path.Combine(Root, "stop"), "stop");
                var process = runner;
                if (process != null) _ = Task.Run(async () => { await Task.Delay(5000); try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
            }
            SaveHistory(); Notify(); return item;
        }
    }
    public object QueueSnapshot()
    {
        lock (gate)
        {
            var waiting = history.Where(x => x.Status == "queued").ToArray();
            for (var i = 0; i < waiting.Length; i++) waiting[i].QueuePosition = i + 1;
            return new { currentTask = history.FirstOrDefault(x => x.Status is "starting" or "running" or "stopping"), queuedTasks = waiting, waitingCount = waiting.Length, businessStates = new { }, pausedForRecovery = false };
        }
    }
    public async Task LaunchGame()
    {
        if (!File.Exists(Settings.GamePath)) throw new InvalidOperationException("请选择 ZenlessZoneZero.exe。");
        if (Process.GetProcessesByName("ZenlessZoneZero").Length != 0) { Log("绝区零已经在运行。"); return; }
        using var process = Process.Start(new ProcessStartInfo(Settings.GamePath) { UseShellExecute = true, WorkingDirectory = System.IO.Path.GetDirectoryName(Settings.GamePath)! });
        await Task.Delay(1000);
        if (Process.GetProcessesByName("ZenlessZoneZero").Length == 0)
            throw new InvalidOperationException("绝区零游戏进程未能启动或已经退出，请确认选择的是完整游戏客户端。路径：" + Settings.GamePath);
        Log("绝区零游戏进程已启动，等待游戏画面。");
    }
    public void CloseGame()
    {
        foreach (var process in Process.GetProcessesByName("ZenlessZoneZero"))
        {
            using (process)
            {
                var path = process.MainModule?.FileName;
                if (string.Equals(path, Settings.GamePath, StringComparison.OrdinalIgnoreCase)) process.Kill();
            }
        }
    }
    public static string FindGame()
    {
        foreach (var drive in DriveInfo.GetDrives().Where(x => x.IsReady && x.DriveType == DriveType.Fixed))
        foreach (var folder in new[] { "miHoYo Launcher\\games\\Zenless Zone Zero Game", "miHoYo Launcher\\games\\ZenlessZoneZero Game", "HoYoPlay\\games\\ZenlessZoneZero Game", "Program Files\\HoYoPlay\\games\\ZenlessZoneZero Game", "Program Files\\ZenlessZoneZero\\ZenlessZoneZero Game" })
        {
            var path = Path.Combine(drive.RootDirectory.FullName, folder, "ZenlessZoneZero.exe");
            if (File.Exists(path)) return path;
        }
        return "";
    }
    public static ProcessStartInfo ProcessInfo(string exe, params string[] args)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return info;
    }
    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            foreach (var item in history.Where(x => x.Active).ToArray()) Stop(item.TaskId);
            try { if (runner is { HasExited: false }) runner.Kill(true); } catch (InvalidOperationException) { }
        }
    }
}
