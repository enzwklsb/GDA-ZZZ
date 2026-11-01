using System.Text;
using System.Text.Json.Nodes;

// Isolated process fixture, never distributed with GDA or used for real-game acceptance.
Console.OutputEncoding = Encoding.UTF8;
if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "ZenlessZoneZero", StringComparison.OrdinalIgnoreCase))
{
    await Task.Delay(TimeSpan.FromMinutes(3));
    return 0;
}
var body = JsonNode.Parse(File.ReadAllText(args[^1]))!;
Console.WriteLine("GDA_STEP=隔离进程验证");
var until = DateTime.UtcNow.AddMilliseconds(body["testDelay"]?.GetValue<int>() ?? 500);
while (DateTime.UtcNow < until)
{
    if (File.Exists(body["stopFile"]!.GetValue<string>())) return 2;
    await Task.Delay(30);
}
if (body["testFail"]?.GetValue<bool>() == true) { Console.WriteLine("GDA_ERROR=隔离进程失败原因"); return 7; }
return 0;
