namespace WinCare.Domain.Plugins;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// JSON-RPC 2.0 request payload for out-of-process plugin host communication.
/// </summary>
public sealed record PluginIpcRequest(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonElement Parameters);

/// <summary>
/// JSON-RPC 2.0 response payload received from out-of-process plugin worker.
/// </summary>
public sealed record PluginIpcResponse(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("result")] JsonElement? Result,
    [property: JsonPropertyName("error")] PluginIpcError? Error);

/// <summary>
/// JSON-RPC 2.0 error specification.
/// </summary>
public sealed record PluginIpcError(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("data")] JsonElement? Data);

/// <summary>
/// Process host managing out-of-process sandboxed extension workers.
/// </summary>
public interface IPluginProcessHost : IAsyncDisposable
{
    /// <summary>
    /// Starts the sandboxed plugin host worker process asynchronously.
    /// </summary>
    Task StartAsync(string pluginExecutablePath, CancellationToken cancellationToken);

    /// <summary>
    /// Invokes a method on the isolated plugin process via IPC.
    /// </summary>
    Task<JsonElement> InvokeAsync(string method, JsonElement parameters, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a value indicating whether the isolated worker process is currently alive and responsive.
    /// </summary>
    bool IsAlive { get; }
}
