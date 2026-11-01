using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace Gda;

public sealed class Provider(Engine engine)
{
    public const string Version = "v2.6.0";
    public const string DownloadUrl = "https://github.com/OneDragon-Anything/ZenlessZoneZero-OneDragon/releases/download/v2.6.0/ZenlessZoneZero-OneDragon-v2.6.0-Full-Environment.zip";
    public string Path => System.IO.Path.Combine(engine.Root, "OneDragon");
    public string Python => System.IO.Path.Combine(Path, ".runtime", "python", "python.exe");
    public bool Installed => File.Exists(System.IO.Path.Combine(Path, ".installed"));
    public string State { get; private set; } = "";
    public string? Error { get; private set; }
    public int Progress { get; private set; }
    public Task? Installing { get; private set; }
    private readonly object gate = new();
    public Task Install()
    {
        lock (gate)
        {
            if (Installed) return Task.CompletedTask;
            if (Installing is { IsCompleted: false }) return Installing;
            Installing = Task.Run(InstallCore);
            return Installing;
        }
    }
    private async Task InstallCore()
    {
        try
        {
            Error = null; Progress = 0; State = "DOWNLOADING"; engine.Notify();
            var downloads = System.IO.Path.Combine(engine.Root, "downloads");
            Directory.CreateDirectory(downloads);
            var archive = System.IO.Path.Combine(downloads, "onedragon-v2.6.0.zip");
            if (!File.Exists(archive))
            {
                engine.Log("正在下载 OneDragon 官方完整环境包（约 715 MB）…");
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                using var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync();
                await using (var output = File.Create(archive + ".download"))
                {
                    var buffer = new byte[131072];
                    long received = 0;
                    using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                    int count;
                    while ((count = await input.ReadAsync(buffer, deadline.Token)) != 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, count), deadline.Token);
                        received += count;
                        var progress = (int)(received * 80 / (response.Content.Headers.ContentLength ?? 749317429));
                        if (progress != Progress) { Progress = progress; engine.Notify(); }
                    }
                }
                File.Move(archive + ".download", archive, true);
            }
            State = "INSTALLING"; Progress = 80; engine.Log("正在解压 OneDragon 和 Python 环境…");
            Directory.CreateDirectory(Path);
            Extract(archive, Path, stripRoot: true);
            var pythonArchive = System.IO.Path.Combine(Path, ".install", "cpython-3.11.zip");
            var environmentArchive = System.IO.Path.Combine(Path, ".install", "ZenlessZoneZero-OneDragon-Environment.zip");
            Extract(pythonArchive, System.IO.Path.Combine(Path, ".runtime", "python"), true);
            var wheels = System.IO.Path.Combine(Path, ".runtime", "wheels");
            Extract(environmentArchive, wheels, false);
            var pip = Engine.ProcessInfo(Python, "-m", "pip", "install", "--no-index", "--no-deps", "--disable-pip-version-check");
            foreach (var wheel in Directory.GetFiles(wheels, "*.whl")) pip.ArgumentList.Add(wheel);
            await Run(pip);
            Progress = 95; engine.Log("正在确认 OneDragon 可以实际导入…");
            var info = Engine.ProcessInfo(Python, "-u", System.IO.Path.Combine(AppContext.BaseDirectory, "provider", "runner.py"), "check", Path);
            info.WorkingDirectory = Path;
            info.Environment["PYTHONIOENCODING"] = "utf-8";
            using var process = Process.Start(info) ?? throw new InvalidOperationException("无法启动 Python。");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var outputText = await stdout + await stderr;
            if (process.ExitCode != 0) throw new InvalidOperationException(outputText);
            engine.Log(outputText.Trim());
            File.WriteAllText(System.IO.Path.Combine(Path, ".installed"), Version);
            State = "READY"; Progress = 100; engine.Log("绝区零 Provider 已安装，可以启动游戏和执行任务。");
        }
        catch (Exception ex) { State = "FAILED"; Error = ex.Message; engine.Log("Provider 安装失败：" + ex.Message); }
        engine.Notify();
    }
    private async Task Run(ProcessStartInfo info)
    {
        using var process = Process.Start(info) ?? throw new InvalidOperationException("无法启动安装程序。");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var text = await stdout + await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException(text);
        engine.Log("Python 依赖安装完成。");
    }
    private static void Extract(string archive, string destination, bool stripRoot)
    {
        using var zip = ZipFile.OpenRead(archive);
        var root = System.IO.Path.GetFullPath(destination) + System.IO.Path.DirectorySeparatorChar;
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (stripRoot) { var slash = name.IndexOf('/'); if (slash < 0) continue; name = name[(slash + 1)..]; }
            if (name.StartsWith(".git/") || name.Length == 0 || name.EndsWith('/')) continue;
            var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, name));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("ZIP 包含非法路径。");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path, true);
        }
    }
}
