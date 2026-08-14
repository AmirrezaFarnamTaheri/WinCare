using System;

namespace WinCare.Domain.Attributes
{
    /// <summary>
    /// Marks a command handler class with its target catalog command ID for compile-time source generation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class CommandHandlerAttribute : Attribute
    {
        public string CommandId { get; }

        public CommandHandlerAttribute(string commandId)
        {
            CommandId = commandId ?? throw new ArgumentNullException(nameof(commandId));
        }
    }
}
