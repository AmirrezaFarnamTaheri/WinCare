using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using WinCare.Application.Activity;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.Commands;
using WinCare.Infrastructure.Native;
using WinCare.Infrastructure.Plugins;

string output = Path.GetFullPath(args[0]);
string sandbox = Path.Combine(output, "sandbox-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(sandbox);
if(args.Length > 1 && args[1] == "--export-only")
{
    Type exportType = typeof(CommandStateStore).Assembly.GetType("WinCare.Infrastructure.Commands.WindowsCommandExecutor", true)!;
    MethodInfo exportMethod = exportType.GetMethod("WriteJsonExportAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
    string exportPath = Path.Combine(sandbox,"harmless-export.json");
    string outcome="Succeeded";
    string message="";
    try { await (Task)exportMethod.Invoke(null,[exportPath,JsonSerializer.SerializeToElement(new { audit = true }),CancellationToken.None])!; }
    catch(Exception ex) { outcome=ex.GetType().Name;message=ex.Message; }
    await File.WriteAllTextAsync(Path.Combine(output,"export-fixture.json"),JsonSerializer.Serialize(new {outcome,message,targetExists=File.Exists(exportPath),temporaryFiles=Directory.GetFiles(sandbox,"*.tmp").Length},new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine(outcome);
    return;
}
var state = new CommandStateStore(Path.Combine(sandbox, "commands"));
var native = new NativeCoreService();
var journal = new ActivityJournalService(Path.Combine(sandbox,"activity.json"));
Type type = typeof(CommandStateStore).Assembly.GetType("WinCare.Infrastructure.Commands.WindowsCommandExecutor", true)!;
var executor = (ICommandOperationExecutor)Activator.CreateInstance(type,
    BindingFlags.Instance | BindingFlags.NonPublic, null, [native, null, state, null], null)!;
var dispatcher = CommandRuntime.CreateDefault(executor, native, journal);
var results = new List<object>();
foreach (string id in new[]{"system","storage","startup","network","security","health","catalog","presets","applications","internals-memory","memory-anomalies"})
{
    var definition = WinCare.CommandCatalog.CommandCatalog.Load().Single(c => c.Id == id);
    if (!definition.ReadOnly) throw new InvalidOperationException("Read-only audit whitelist violation.");
    Stopwatch watch = Stopwatch.StartNew();
    var result = await dispatcher.ExecuteAsync(CommandRequest.Preview(id),
        new CommandExecutionOptions(false, DateTimeOffset.UtcNow.AddSeconds(10)), CancellationToken.None);
    results.Add(new {id,status=result.Status.ToString(),result.Code,result.Message,milliseconds=watch.ElapsedMilliseconds,
        shape=result.Data?.ValueKind.ToString(),properties=result.Data is { ValueKind: JsonValueKind.Object } data ? data.EnumerateObject().Select(p=>p.Name).ToArray() : []});
    Console.WriteLine($"{id}: {result.Status} {watch.ElapsedMilliseconds} ms");
}
await journal.FlushAsync();
((IDisposable)executor).Dispose();

// Two independent app stores writing their own harmless fixture, not user data.
var store1=new CommandStateStore(Path.Combine(sandbox,"concurrency"));
var store2=new CommandStateStore(Path.Combine(sandbox,"concurrency"));
await store1.WriteAsync("counter",JsonSerializer.SerializeToElement(0),CancellationToken.None);
using var barrier=new Barrier(2);
var updates = new[]{store1,store2}.Select(store => Task.Run(async () => {
    try { await store.UpdateAsync("counter",JsonSerializer.SerializeToElement(0), current => {
        if (!barrier.SignalAndWait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Fixture barrier");
        return JsonSerializer.SerializeToElement(current.GetInt32()+1);
    },CancellationToken.None); return "Succeeded"; }
    catch(Exception ex) { return ex.GetType().Name; }
}));
string[] updateOutcomes=await Task.WhenAll(updates);
int finalCounter=(await store1.ReadAsync("counter",default,CancellationToken.None)).GetInt32();

// A directory cannot be overwritten by the plugin-state JSON file.
string blockedPath=Path.Combine(sandbox,"directory-not-file");
Directory.CreateDirectory(blockedPath);
var pluginState=new PluginStateRepository(blockedPath);
bool pluginWriteThrew=false;
try { pluginState.SaveEnabledPluginIds(["audit.example"]); } catch { pluginWriteThrew=true; }
var summary=new {commands=results,concurrentCounter=new {expected=2,actual=finalCounter,updateOutcomes},
    pluginPersistence=new {writeThrew=pluginWriteThrew,fileExists=File.Exists(blockedPath)},sandbox};
await File.WriteAllTextAsync(Path.Combine(output,"backend-probes.json"),JsonSerializer.Serialize(summary,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Concurrent count expected 2, actual {finalCounter}; plugin write raised error: {pluginWriteThrew}");
