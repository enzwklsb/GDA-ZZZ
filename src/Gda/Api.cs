using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Gda;

public sealed class Api(Engine engine) : IAsyncDisposable
{
    private WebApplication? app;
    public string Key { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public string Url { get; private set; } = "";
    public async Task Start()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers["X-Gda-Key"] != Key) { context.Response.StatusCode = 401; return; }
            try { await next(); }
            catch (KeyNotFoundException ex) { context.Response.StatusCode = 404; await context.Response.WriteAsJsonAsync(new { detail = ex.Message }); }
            catch (Exception ex) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { detail = ex.Message }); }
        });
        app.MapGet("/profile", () => new { apiVersion = "3.3", mobileApiRevision = 10, eventStreamVersion = 0, pcVersion = "GDA-ZZZ 0.1.0", dryRun = false, executionMode = "REAL", games = new[] { new { gameId = "zzz", displayName = "绝区零" } } });
        app.MapGet("/health", () => new { status = "ok" });
        app.MapGet("/agent/providers", Providers);
        app.MapPost("/agent/providers/refresh", Providers);
        app.MapPost("/agent/providers/update", () => { _ = engine.Provider.Install(); return new { message = "正在安装绝区零 Provider。" }; });
        app.MapGet("/components", () => new { components = new[] { new { id = "onedragon", installed = engine.Provider.Installed, path = engine.Provider.Path } } });
        app.MapPost("/components/{id}/install", (string id) =>
        {
            if (id is not ("onedragon" or "zzz")) throw new InvalidOperationException("第一版只支持绝区零。");
            _ = engine.Provider.Install();
            return new { status = engine.Provider.Installed ? "installed" : "installing", jobId = engine.Provider.Installed ? null : "onedragon", logs = new[] { "正在安装 OneDragon 官方环境包。" } };
        });
        app.MapGet("/components/jobs/{id}", () => new { status = engine.Provider.Installed ? "installed" : engine.Provider.State == "FAILED" ? "failed" : "installing", error = engine.Provider.Error, logs = new[] { engine.Provider.Error ?? (engine.Provider.State + " " + engine.Provider.Progress + "%") } });
        app.MapGet("/catalog/zzz", Catalog);
        app.MapGet("/catalog/zzz/runtime-options", () => new { predefinedTeams = Array.Empty<object>(), autoBattleProfiles = new[] { "全配队通用" }, teamSyncReady = false });
        app.MapGet("/agent/queue", engine.QueueSnapshot);
        app.MapGet("/agent/tasks/by-request/{id}", (string id) => engine.Find(id) ?? throw new KeyNotFoundException("任务不存在。"));
        app.MapGet("/agent/tasks/{id}", (string id) => engine.Find(id) ?? throw new KeyNotFoundException("任务不存在。"));
        app.MapDelete("/agent/queue/{id}", (string id) => engine.Stop(id));
        app.MapPost("/agent/tasks/{id}/stop", (string id) => engine.Stop(id));
        app.MapPost("/agent/manual", (JsonObject body) => Accepted(engine.Submit(body)));
        app.MapPost("/agent/manual-batch", (JsonObject body) =>
        {
            var items = body["items"]?.AsArray() ?? throw new InvalidOperationException("缺少任务。");
            foreach (var item in items) Engine.Validate(item!.AsObject());
            var id = body["requestId"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N");
            var results = items.Select((item, index) => { var child = (JsonObject)item!.DeepClone(); child["requestId"] = id + ":" + index; return Accepted(engine.Submit(child)); }).ToArray();
            return new { status = "queued", results };
        });
        app.MapPost("/agent/managed-run", (JsonObject body) =>
        {
            body["gameId"] = "zzz"; body["taskIds"] = new JsonArray("random_play", "scratch_card", "coffee", "engagement_reward");
            return Accepted(engine.Submit(body));
        });
        app.MapPost("/agent/control/zzz/stop", () => { foreach (var task in engine.History.Where(x => x.Active)) engine.Stop(task.TaskId); return new { message = "已停止绝区零任务。" }; });
        app.MapPost("/agent/control/zzz/close", () => { foreach (var task in engine.History.Where(x => x.Active)) engine.Stop(task.TaskId); engine.CloseGame(); return new { message = "已关闭绝区零。" }; });
        await app.StartAsync();
        Url = app.Urls.Single();
    }
    private object Providers() => new { games = new[] { new {
        gameId = "zzz", displayName = "绝区零", providerId = "onedragon", iconId = "zzz", ownership = "MANAGED",
        installState = engine.Provider.Installed ? "READY" : string.IsNullOrEmpty(engine.Provider.State) ? "NOT_INSTALLED" : engine.Provider.State,
        usable = engine.Provider.Installed, executionReady = engine.Provider.Installed,
        progress = engine.Provider.Progress, error = engine.Provider.Error,
        testedVersion = Provider.Version, detectedVersion = engine.Provider.Installed ? Provider.Version : null,
        gameRegistrationState = File.Exists(engine.Settings.GamePath) ? "CONFIGURED" : "NOT_CONFIGURED"
    } } };
    private static object Accepted(RunItem item) => new { status = item.Status, gameId = "zzz", requestId = item.RequestId, accepted = new[] { new { task = item } } };
    private static object Catalog() => new {
        chargeTargets = Array.Empty<object>(), cultivation = new { gameVersion = "2.6", entities = Array.Empty<object>() },
        dailyTasks = Engine.Tasks.Where(x => x.Key != "charge_plan").Select(x => new { id = x.Key, displayName = x.Value }),
        weeklyCapabilities = Array.Empty<object>(), standaloneTasks = Array.Empty<object>(),
        predefinedTeams = Array.Empty<object>(), autoBattleProfiles = new[] { "全配队通用" }, teamSyncReady = false
    };
    public async ValueTask DisposeAsync() { if (app != null) await app.DisposeAsync(); }
}
