using System;

namespace WinCare.Domain.Attributes
{
    /// <summary>
    /// Marks a command handler class with its target catalog command ID for compile-time source generation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class CommandHandlerAttribute : Attribute
    {
        /// <summary>
        /// Gets the target catalog command ID handled by the decorated class.
        /// </summary>
        public string CommandId { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CommandHandlerAttribute"/> class with the specified command ID.
        /// </summary>
        /// <param name="commandId">The unique command identifier.</param>
        public CommandHandlerAttribute(string commandId)
        {
            CommandId = commandId ?? throw new ArgumentNullException(nameof(commandId));
        }
    }
}
