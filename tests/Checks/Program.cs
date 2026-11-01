using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gda;

var root = Path.Combine(Path.GetTempPath(), "gda-zzz-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var fixture = Path.GetFullPath(args[0]);
var passed = 0;
void Check(bool okay, string name) { if (!okay) throw new Exception("FAIL " + name); passed++; Console.WriteLine("PASS " + name); }
async Task Wait(Func<bool> condition) { var deadline = DateTime.UtcNow.AddSeconds(20); while (!condition()) { if (DateTime.UtcNow > deadline) throw new Exception("timeout"); await Task.Delay(30); } }
JsonObject Body(string id, int delay = 500, bool fail = false) => new() { ["gameId"] = "zzz", ["taskIds"] = new JsonArray("scratch_card"), ["requestId"] = id, ["testDelay"] = delay, ["testFail"] = fail };
using (var engine = new Engine(root))
{
    var provider = engine.Provider.Path;
    var python = Path.GetDirectoryName(engine.Provider.Python)!;
    Directory.CreateDirectory(python);
    foreach (var path in Directory.GetFiles(fixture)) File.Copy(path, Path.Combine(python, Path.GetFileName(path)));
    var game = Path.Combine(python, "ZenlessZoneZero.exe");
    File.Copy(Path.Combine(python, "python.exe"), game);
    engine.Settings.GamePath = game;
    engine.SaveSettings();
    File.WriteAllText(Path.Combine(provider, ".installed"), "test fixture");
    var rejected = false;
    try { engine.Submit(new JsonObject { ["gameId"] = "genshin", ["taskIds"] = new JsonArray("scratch_card") }); } catch (InvalidOperationException) { rejected = true; }
    Check(rejected && engine.History.Length == 0, "unsupported game does not enter queue");
    var first = engine.Submit(Body("one", 1300));
    var second = engine.Submit(Body("two"));
    Check(engine.Submit(Body("one")) == first && engine.History.Length == 2, "request replay returns existing task");
    await Wait(() => !first.Active && !second.Active);
    Check(first.Status == "succeeded" && second.Status == "succeeded" && DateTimeOffset.Parse(second.StartedAt!) >= DateTimeOffset.Parse(first.FinishedAt!), "tasks run one at a time in order using real child processes");
    Check(first.CurrentStep == "隔离进程验证", "UTF-8 child-process output is preserved");
    var again = engine.Submit(Body("again"));
    await Wait(() => !again.Active);
    Check(again.Status == "succeeded", "a new request may repeat a completed daily task");
    var failed = engine.Submit(Body("failure", fail: true));
    await Wait(() => !failed.Active);
    Check(failed.Status == "failed" && failed.Error == "隔离进程失败原因", "child-process failure is returned with its real reason");
    var slow = engine.Submit(Body("slow", 15000));
    var waiting = engine.Submit(Body("waiting"));
    engine.Stop(waiting.TaskId);
    Check(waiting.Status == "cancelled", "queued task can be cancelled without running");
    await Wait(() => slow.Status == "running");
    engine.Stop(slow.TaskId);
    await Wait(() => !slow.Active);
    Check(slow.Status == "stopped", "running child process stops and returns stopped state");
    await using var api = new Api(engine);
    await api.Start();
    using var http = new HttpClient();
    var unauthorized = await http.GetAsync(api.Url + "/profile");
    Check(unauthorized.StatusCode == HttpStatusCode.Unauthorized, "local control API rejects unpaired access");
    http.DefaultRequestHeaders.Add("X-Gda-Key", api.Key);
    var profile = await http.GetFromJsonAsync<JsonObject>(api.Url + "/profile");
    Check(profile!["apiVersion"]!.GetValue<string>() == "3.3" && profile["mobileApiRevision"]!.GetValue<int>() == 10, "profile matches unchanged Android app");
    var batch = new JsonObject { ["requestId"] = "batch", ["items"] = new JsonArray(Body("ignored-a"), Body("ignored-b")) };
    var response = await http.PostAsJsonAsync(api.Url + "/agent/manual-batch", batch);
    var responseAgain = await http.PostAsJsonAsync(api.Url + "/agent/manual-batch", batch);
    Check(response.IsSuccessStatusCode && responseAgain.IsSuccessStatusCode && engine.History.Count(x => x.RequestId.StartsWith("batch:")) == 2, "Android batch replay does not duplicate tasks");
    await Wait(() => !engine.Busy);
    var snapshot = await http.GetFromJsonAsync<JsonObject>(api.Url + "/agent/queue");
    Check(snapshot!.ContainsKey("currentTask") && snapshot["queuedTasks"] is JsonArray && snapshot["businessStates"] is JsonObject, "queue response matches Android parser");
    engine.CloseGame();
}
using (var restored = new Engine(root)) Check(restored.Find("one")?.Status == "succeeded" && restored.Find("failure")?.Error == "隔离进程失败原因", "task lookup and results survive restart");
Console.WriteLine($"TOTAL {passed}; PASS {passed}; FAIL 0. Isolated process tests, not real-game acceptance.");
// Remove only this test's generated directory after confirming its absolute location.
if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()) + "gda-zzz-checks-", StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
