using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using WinCare.Application.Activity;
using WinCare.Application.Commands;
using WinCare.Application.Native;
using WinCare.CommandCatalog;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

namespace WinCare.Application.Tests;

/// <summary>
/// Extension-pack composition in <see cref="CommandRuntime" />: pack handlers win for their own
/// command ids, and every id-ownership violation is rejected at composition time.
/// </summary>
public class CommandRuntimePackTests
{
    private const string CoreId = "system";

    /// <summary>
    /// A pack whose fragment is embedded in this test assembly (Data/commands.runtimepack.json),
    /// whose handler records every execution so tests can prove which handler actually ran.
    /// </summary>
    private sealed class RecordingPack : ICommandPack
    {
        public string PackId => "runtimepack";

        public ConcurrentBag<string> Invocations { get; } = new();

        public IEnumerable<ICommandHandler> CreateHandlers(ICommandOperationContext context)
        {
            Assert.NotNull(context);
            Assert.NotNull(context.State);
            Assert.NotNull(context.Process);

            yield return new RecordingHandler(this, "runtimepack-one");
            yield return new RecordingHandler(this, "runtimepack-two");
        }
    }

    private sealed class RecordingHandler : ICommandHandler
    {
        private readonly RecordingPack _pack;

        public RecordingHandler(RecordingPack pack, string commandId)
        {
            _pack = pack;
            CommandId = commandId;
        }

        public string CommandId { get; }

        public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
        {
            _pack.Invocations.Add(CommandId);
            return Task.FromResult(CommandHandlerOutcome.Succeeded("runtimepack.executed", $"Pack handler ran {CommandId}."));
        }
    }

    /// <summary>A pack that only reports handlers, for the violation cases. </summary>
    private sealed class StaticPack : ICommandPack
    {
        private readonly string _packId;
        private readonly ICommandHandler[] _handlers;

        public StaticPack(string packId, params ICommandHandler[] handlers)
        {
            _packId = packId;
            _handlers = handlers;
        }

        public string PackId => _packId;

        public IEnumerable<ICommandHandler> CreateHandlers(ICommandOperationContext context) => _handlers;
    }

    private sealed class StubHandler : ICommandHandler
    {
        public StubHandler(string commandId) => CommandId = commandId;

        public string CommandId { get; }

        public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(CommandHandlerOutcome.Succeeded("stub.executed", "Stub handler ran."));
    }

    private static CommandDefinition MakePackCommand(string id) =>
        new(id, "Pack command", "Summary", "All tools", "Commands", CommandRisk.Low,
            ReadOnly: true, AdministratorAccess.No, RestartExpectation.No,
            LegacySource: "runtimepack", MigrationStatus.Implemented,
            Keywords: Array.Empty<string>(), ExplicitRiskTier: null);

    private sealed class StubExecutor : ICommandOperationExecutor, ICommandOperationContext
    {
        public ConcurrentBag<string> Invocations { get; } = new();

        public ICommandStateStore State { get; }
        public IBoundedProcessRunner Process { get; }
        public INativeCoreService? NativeCore => null;
        public HttpClient HttpClient { get; }
        IActivityJournalService? ICommandOperationContext.Journal => null;

        public StubExecutor(ICommandStateStore? state = null, IBoundedProcessRunner? process = null)
        {
            State = state ?? new InMemoryStateStore();
            Process = process ?? new ThrowingProcessRunner();
            HttpClient = new HttpClient();
        }

        public Task<CommandHandlerOutcome> ExecuteAsync(
            CommandDefinition definition, CommandRequest request, CancellationToken cancellationToken)
        {
            Invocations.Add(definition.Id);
            return Task.FromResult(CommandHandlerOutcome.Succeeded("stub.executor", $"Core executor ran {definition.Id}."));
        }
    }

    private sealed class InMemoryStateStore : ICommandStateStore
    {
        private readonly ConcurrentDictionary<string, JsonElement> _values = new();

        public string Root => "in-memory";

        public Task<JsonElement> ReadAsync(string key, JsonElement fallback, CancellationToken cancellationToken) =>
            Task.FromResult(_values.GetValueOrDefault(key, fallback));

        public Task WriteAsync(string key, JsonElement value, CancellationToken cancellationToken)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task<JsonElement> UpdateAsync(
            string key, JsonElement fallback, Func<JsonElement, JsonElement> transform, CancellationToken cancellationToken)
        {
            JsonElement updated = transform(_values.GetValueOrDefault(key, fallback));
            _values[key] = updated;
            return Task.FromResult(updated);
        }

        public Task<JsonElement> ReadObjectAsync(string key, CancellationToken cancellationToken) =>
            ReadAsync(key, JsonDocument.Parse("{}").RootElement.Clone(), cancellationToken);

        public Task<JsonElement> ReadArrayAsync(string key, CancellationToken cancellationToken) =>
            ReadAsync(key, JsonDocument.Parse("[]").RootElement.Clone(), cancellationToken);

        public string ResolveExportPath(string requestedPath, string defaultFileName) => defaultFileName;
    }

    private sealed class ThrowingProcessRunner : IBoundedProcessRunner
    {
        public Task<ProcessExecutionResult> RunAsync(
            string fileName, IEnumerable<string> arguments, CancellationToken cancellationToken,
            TimeSpan? timeout = null, int maxCharacters = IBoundedProcessRunner.DefaultMaxCharacters,
            string? workingDirectory = null) =>
            throw new InvalidOperationException("The stub process runner must not be reached.");
    }

    [Fact]
    public void CreateDefault_WithoutPacks_StillAdmitsTheCoreCatalog()
    {
        StubExecutor executor = new();

        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(executor);

        CommandDefinition? definition = CommandCatalog.CommandCatalog.Find(CoreId);
        Assert.NotNull(definition);
        Assert.Equal(269, CommandCatalog.CommandCatalog.Load().Count);
    }

    [Fact]
    public async Task CreateDefault_WithoutPacks_RoutesThroughTheCoreExecutor()
    {
        StubExecutor executor = new();

        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(executor);

        CommandResult result = await dispatcher.ExecuteAsync(
            CommandRequest.Preview(CoreId), CommandExecutionOptions.Default, CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Contains(CoreId, executor.Invocations);
    }

    [Fact]
    public async Task CreateDefault_WithPacks_AdmitsThePackCommands()
    {
        RecordingPack pack = new();
        StubExecutor executor = new();

        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(
            executor, new ICommandPack[] { pack }, executor);

        CommandResult one = await dispatcher.ExecuteAsync(
            CommandRequest.Preview("runtimepack-one"), CommandExecutionOptions.Default, CancellationToken.None);
        CommandResult two = await dispatcher.ExecuteAsync(
            CommandRequest.Preview("runtimepack-two"), CommandExecutionOptions.Default, CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, one.Status);
        Assert.Equal(CommandResultStatus.Succeeded, two.Status);
        Assert.Contains("runtimepack-one", pack.Invocations);
        Assert.Contains("runtimepack-two", pack.Invocations);
    }

    [Fact]
    public async Task CreateDefault_WithPacks_RoutesCoreCommandsThroughTheCoreExecutor()
    {
        RecordingPack pack = new();
        StubExecutor executor = new();

        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(
            executor, new ICommandPack[] { pack }, executor);

        CommandResult result = await dispatcher.ExecuteAsync(
            CommandRequest.Preview(CoreId), CommandExecutionOptions.Default, CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Contains(CoreId, executor.Invocations);
        Assert.Empty(pack.Invocations);
    }

    [Fact]
    public void CreateDefault_WithPacks_RejectsACoreIdReDeclaration()
    {
        CommandPackFragment fragment = new("runtimepack", Array.AsReadOnly(new[]
        {
            MakePackCommand(CoreId),
        }));
        StubExecutor executor = new();

        // The fragment is built directly (not through CommandPackCatalog) so this proves the merge
        // guard in CommandCatalog.LoadWithPacks fires even for an otherwise well-formed fragment.
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            CommandCatalog.CommandCatalog.LoadWithPacks(new[] { fragment }));

        Assert.Contains(CoreId, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateDefault_WithTwoPacks_RejectsAnIdDeclaredByBoth()
    {
        StaticPack first = new("dupe-a", new StubHandler("dupe-shared"));
        StaticPack second = new("dupe-b", new StubHandler("dupe-shared"));
        StubExecutor executor = new();

        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            CommandRuntime.CreateDefault(executor, new ICommandPack[] { first, second }, executor));

        Assert.Contains("dupe-shared", ex.Message, StringComparison.Ordinal);
        Assert.Contains("more than one extension pack", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateDefault_RejectsAPackHandlerForAnIdThePackDoesNotDeclare()
    {
        StaticPack pack = new("runtimepack", new StubHandler("undeclared-id"));
        StubExecutor executor = new();

        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            CommandRuntime.CreateDefault(executor, new ICommandPack[] { pack }, executor));

        Assert.Contains("undeclared-id", ex.Message, StringComparison.Ordinal);
        Assert.Contains("does not declare in its catalog fragment", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateDefault_RejectsAPackWithNoId()
    {
        StaticPack pack = new("   ", new StubHandler("runtimepack-one"));
        StubExecutor executor = new();

        Assert.Throws<ArgumentException>(() =>
            CommandRuntime.CreateDefault(executor, new ICommandPack[] { pack }, executor));
    }

    [Fact]
    public void CreateDefault_RejectsANullPack()
    {
        StubExecutor executor = new();

        List<ICommandPack> packs = new() { null! };

        ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() =>
            CommandRuntime.CreateDefault(executor, packs, executor));

        Assert.Equal("pack", ex.ParamName);
    }

    [Fact]
    public void CreateDefault_RequiresAContext()
    {
        StubExecutor executor = new();

        ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() =>
            CommandRuntime.CreateDefault(executor, Array.Empty<ICommandPack>(), null!));

        Assert.Equal("context", ex.ParamName);
    }

    [Fact]
    public async Task CreateDefault_WithAnEmptyPackList_BehavesLikeTheCoreOnlyPath()
    {
        StubExecutor executor = new();

        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(
            executor, Array.Empty<ICommandPack>(), executor);

        CommandResult result = await dispatcher.ExecuteAsync(
            CommandRequest.Preview(CoreId), CommandExecutionOptions.Default, CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Contains(CoreId, executor.Invocations);
    }

    [Fact]
    public void CreateDefault_HandsTheContextToThePack()
    {
        RecordingPack pack = new();
        StubExecutor executor = new();

        // RecordingPack.CreateHandlers asserts the context and its State/Process members itself, so
        // composing the dispatcher is the whole assertion: a pack handed an unusable kernel surface
        // throws from CreateHandlers before CreateDefault can return.
        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(
            executor, new ICommandPack[] { pack }, executor);

        Assert.NotNull(dispatcher);
    }

    [Fact]
    public async Task CreateDefault_PackCommandsAreDispatchedThroughAdmission()
    {
        RecordingPack pack = new();
        StubExecutor executor = new();

        CommandDispatcher dispatcher = CommandRuntime.CreateDefault(
            executor, new ICommandPack[] { pack }, executor);

        // A pack command is still a catalog command: an unknown parameter key is subject to the same
        // dispatcher admission gate as a core command, not to a pack-specific one. Pack commands
        // declare no parameter schema, so the payload is admitted and the pack handler runs.
        JsonElement parameters = JsonDocument.Parse("{\"unexpected\": true}").RootElement.Clone();
        CommandRequest request = CommandRequest.Preview("runtimepack-one", parameters);

        CommandResult result = await dispatcher.ExecuteAsync(request, CommandExecutionOptions.Default, CancellationToken.None);

        Assert.Equal(CommandResultStatus.Succeeded, result.Status);
        Assert.Contains("runtimepack-one", pack.Invocations);
    }
}
