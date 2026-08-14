using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using WinCare.SourceGenerators;
using Xunit;

namespace WinCare.Infrastructure.Tests
{
    public class CommandDispatcherGeneratorTests
    {
        [Fact]
        public void CommandDispatcherGenerator_Generates_TryRouteStatic_For_Decorated_Classes()
        {
            var sourceCode = @"
namespace WinCare.Domain.Attributes
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class CommandHandlerAttribute : System.Attribute
    {
        public string CommandId { get; }
        public CommandHandlerAttribute(string commandId) => CommandId = commandId;
    }
}

namespace WinCare.Domain.Commands
{
    public interface ICommandHandler { }
}

namespace WinCare.Infrastructure.Handlers
{
    using WinCare.Domain.Attributes;
    using WinCare.Domain.Commands;

    [CommandHandler(""test.sample.command"")]
    public class SampleCommandHandler : ICommandHandler
    {
    }
}
";

            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var references = new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
            };

            var compilation = CSharpCompilation.Create(
                "TestAssembly",
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generator = new CommandDispatcherGenerator();
            var driver = CSharpGeneratorDriver.Create(generator);

            driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

            Assert.Empty(diagnostics);

            var runResult = driver.GetRunResult();
            Assert.NotEmpty(runResult.GeneratedTrees);

            var generatedSource = runResult.GeneratedTrees.First().ToString();
            Assert.Contains("public static partial class GeneratedCommandDispatcher", generatedSource);
            Assert.Contains("case \"test.sample.command\":", generatedSource);
            Assert.Contains("handlerTypeName = \"WinCare.Infrastructure.Handlers.SampleCommandHandler\";", generatedSource);
            Assert.Contains("return true;", generatedSource);
        }
    }
}
