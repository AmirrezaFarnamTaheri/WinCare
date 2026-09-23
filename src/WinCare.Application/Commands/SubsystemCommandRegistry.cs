namespace WinCare.Application.Commands;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Registry managing registration and resolution of autonomous <see cref="ISubsystemCommandExecutor"/> instances.
/// </summary>
public sealed class SubsystemCommandRegistry
{
    private readonly List<ISubsystemCommandExecutor> _executors = new();

    public void Register(ISubsystemCommandExecutor executor)
    {
        if (executor == null) throw new ArgumentNullException(nameof(executor));
        if (!_executors.Contains(executor))
        {
            _executors.Add(executor);
        }
    }

    public IReadOnlyList<ISubsystemCommandExecutor> Executors => _executors.AsReadOnly();

    public ISubsystemCommandExecutor? Resolve(string commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId)) return null;
        return _executors.FirstOrDefault(e => e.CanHandle(commandId));
    }
}
