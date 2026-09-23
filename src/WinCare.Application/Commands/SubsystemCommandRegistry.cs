namespace WinCare.Application.Commands;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Thread-safe registry managing registration, resolution, and enumeration of autonomous <see cref="ISubsystemCommandExecutor"/> instances.
/// </summary>
public sealed class SubsystemCommandRegistry
{
    private readonly List<ISubsystemCommandExecutor> _executors = new();
    private readonly object _syncRoot = new();

    /// <summary>
    /// Registers a subsystem command executor into the container.
    /// </summary>
    /// <param name="executor">The executor to register.</param>
    public void Register(ISubsystemCommandExecutor executor)
    {
        if (executor == null) throw new ArgumentNullException(nameof(executor));
        lock (_syncRoot)
        {
            if (!_executors.Contains(executor))
            {
                _executors.Add(executor);
            }
        }
    }

    /// <summary>
    /// Unregisters an existing executor from the registry.
    /// </summary>
    public bool Unregister(ISubsystemCommandExecutor executor)
    {
        if (executor == null) return false;
        lock (_syncRoot)
        {
            return _executors.Remove(executor);
        }
    }

    /// <summary>
    /// Clears all registered subsystem executors.
    /// </summary>
    public void Clear()
    {
        lock (_syncRoot)
        {
            _executors.Clear();
        }
    }

    /// <summary>
    /// Gets a snapshot of all registered subsystem executors.
    /// </summary>
    public IReadOnlyList<ISubsystemCommandExecutor> Executors
    {
        get
        {
            lock (_syncRoot)
            {
                return _executors.ToList().AsReadOnly();
            }
        }
    }

    /// <summary>
    /// Resolves the first subsystem executor capable of handling the specified command ID.
    /// </summary>
    /// <param name="commandId">The command identifier.</param>
    /// <returns>The matching subsystem executor, or <c>null</c> if no handler claims it.</returns>
    public ISubsystemCommandExecutor? Resolve(string commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId)) return null;
        lock (_syncRoot)
        {
            return _executors.FirstOrDefault(e => e.CanHandle(commandId));
        }
    }

    /// <summary>
    /// Resolves all registered executors belonging to the specified subsystem name.
    /// </summary>
    /// <param name="subsystem">The subsystem name (e.g., "Storage", "Servicing").</param>
    /// <returns>Matching executors.</returns>
    public IReadOnlyList<ISubsystemCommandExecutor> GetBySubsystem(string subsystem)
    {
        if (string.IsNullOrWhiteSpace(subsystem)) return Array.Empty<ISubsystemCommandExecutor>();
        lock (_syncRoot)
        {
            return _executors
                .Where(e => string.Equals(e.Subsystem, subsystem, StringComparison.OrdinalIgnoreCase))
                .ToList()
                .AsReadOnly();
        }
    }

    /// <summary>
    /// Determines whether any registered subsystem executor can handle the specified command ID.
    /// </summary>
    public bool CanHandle(string commandId) => Resolve(commandId) != null;
}
