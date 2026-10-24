using System.Text.Json;
using WinCare.CommandCatalog.Models;

namespace WinCare.CommandCatalog.Tests;

/// <summary>
/// Parameter-bound, range, and canonicalization coverage for the shared parameter validator.
/// </summary>
/// <remarks>
/// The validator is the single parameter trust boundary for direct dispatch and for imported
/// playbooks, so a defect here is admitted on both paths. Every case below reproduced a
/// concrete misbehaviour in the previous implementation.
/// </remarks>
public sealed class CommandParameterValidatorTests
{
    private static JsonElement Parameters(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static IReadOnlyList<CommandParameterDefinition> Schema(params CommandParameterDefinition[] parameters) =>
        Array.AsReadOnly(parameters);

    [Fact]
    public void A_long_beyond_double_precision_is_compared_exactly_against_its_bound()
    {
        // 2^53 + 1 is not representable as a double. The previous range check converted both
        // the value and the bound to double, rounded them onto the same number, and admitted a
        // value that exceeded the declared maximum.
        CommandParameterDefinition definition = new(
            "Bytes", CommandParameterKind.Long, Required: true, Maximum: "9007199254740992");

        IReadOnlyList<string> errors = CommandParameterValidator.Validate(
            "long-bound", Parameters("{\"Bytes\":9007199254740993}"), Schema(definition));

        Assert.Contains(errors, error => error.Contains("outside its supported range", StringComparison.Ordinal));
    }

    [Fact]
    public void A_long_at_its_exact_bound_is_accepted()
    {
        CommandParameterDefinition definition = new(
            "Bytes", CommandParameterKind.Long, Required: true, Maximum: "9007199254740992");

        IReadOnlyList<string> errors = CommandParameterValidator.Validate(
            "long-bound", Parameters("{\"Bytes\":9007199254740992}"), Schema(definition));

        Assert.Empty(errors);
    }

    [Fact]
    public void A_long_below_a_declared_minimum_is_rejected()
    {
        CommandParameterDefinition definition = new(
            "Bytes", CommandParameterKind.Long, Required: true, Minimum: "1");

        IReadOnlyList<string> errors = CommandParameterValidator.Validate(
            "long-bound", Parameters("{\"Bytes\":0}"), Schema(definition));

        Assert.Contains(errors, error => error.Contains("outside its supported range", StringComparison.Ordinal));
    }

    [Fact]
    public void A_value_larger_than_the_long_range_is_rejected_as_a_type_error()
    {
        CommandParameterDefinition definition = new("Bytes", CommandParameterKind.Long, Required: true);

        IReadOnlyList<string> errors = CommandParameterValidator.Validate(
            "long-bound", Parameters("{\"Bytes\":99999999999999999999}"), Schema(definition));

        Assert.Contains(errors, error => error.Contains("must be an integer", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unreadable_bound_blocks_the_request_instead_of_faulting_dispatch()
    {
        // The previous implementation parsed the bound with double.Parse directly. A schema
        // carrying an unreadable bound threw out of the validator, and the validator runs
        // before the dispatcher's own error handling, so the fault escaped the dispatch call
        // instead of becoming a blocked request.
        CommandParameterDefinition definition = new(
            "Count", CommandParameterKind.Integer, Required: true, Minimum: "not-a-number");

        IReadOnlyList<string> errors = CommandParameterValidator.Validate(
            "bad-bound", Parameters("{\"Count\":5}"), Schema(definition));

        Assert.Contains(errors, error => error.Contains("unreadable range", StringComparison.Ordinal));
    }

    [Fact]
    public void A_duplicate_declared_name_is_reported_instead_of_throwing()
    {
        // Building the declared-name lookup with ToDictionary threw on a name that appears
        // twice under different casing, and that throw escaped dispatch as a fault.
        CommandParameterDefinition first = new("Value", CommandParameterKind.Text, Required: true);
        CommandParameterDefinition second = new("value", CommandParameterKind.Text);

        IReadOnlyList<string> errors = CommandParameterValidator.Validate(
            "duplicate-name", Parameters("{\"Value\":\"x\"}"), Schema(first, second));

        Assert.Contains(errors, error => error.Contains("declared more than once", StringComparison.Ordinal));
    }

    [Fact]
    public void A_required_parameter_supplied_under_a_different_case_is_accepted()
    {
        // The declared-name lookup has always been case-insensitive, so the payload validated.
        // The required-parameter check used a case-sensitive lookup and rejected the very same
        // request as missing its parameter.
        CommandParameterDefinition definition = new(
            "PackageNames", CommandParameterKind.StringList, Required: true);

        IReadOnlyList<string> errors = CommandParameterValidator.Validate(
            "case-insensitive", Parameters("{\"packagenames\":[\"a\"]}"), Schema(definition));

        Assert.Empty(errors);
    }

    [Fact]
    public void A_required_parameter_that_is_absent_is_still_reported()
    {
        CommandParameterDefinition definition = new(
            "PackageNames", CommandParameterKind.StringList, Required: true);

        IReadOnlyList<string> errors = CommandParameterValidator.Validate(
            "case-insensitive", Parameters("{}"), Schema(definition));

        Assert.Contains(errors, error => error.Contains("is required", StringComparison.Ordinal));
    }

    [Fact]
    public void An_integer_outside_its_declared_range_is_rejected()
    {
        CommandParameterDefinition definition = new(
            "Limit", CommandParameterKind.Integer, Required: true, Minimum: "1", Maximum: "5000");

        IReadOnlyList<string> errors = CommandParameterValidator.Validate(
            "integer-range", Parameters("{\"Limit\":5001}"), Schema(definition));

        Assert.Contains(errors, error => error.Contains("outside its supported range", StringComparison.Ordinal));
    }

    [Fact]
    public void A_number_outside_its_declared_range_is_rejected()
    {
        CommandParameterDefinition definition = new(
            "Ratio", CommandParameterKind.Number, Required: true, Minimum: "0", Maximum: "1");

        IReadOnlyList<string> errors = CommandParameterValidator.Validate(
            "number-range", Parameters("{\"Ratio\":1.5}"), Schema(definition));

        Assert.Contains(errors, error => error.Contains("outside its supported range", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("1e2147483647")]
    [InlineData("1e100000000")]
    public void An_unbounded_exponent_must_be_rejected_without_allocating_a_giant_integer(string bound)
    {
        CommandParameterDefinition definition = new(
            "Count", CommandParameterKind.Long, Required: true, Minimum: bound);

        IReadOnlyList<string> errors = CommandParameterValidator.Validate(
            "exponent-bound", Parameters("{\"Count\":1}"), Schema(definition));

        Assert.Contains(errors, error => error.Contains("unreadable range", StringComparison.Ordinal));
    }

    [Fact]
    public void A_reasonable_exponent_bound_is_compared_without_losing_precision()
    {
        CommandParameterDefinition definition = new(
            "Count", CommandParameterKind.Long, Required: true, Maximum: "1e30");

        IReadOnlyList<string> errors = CommandParameterValidator.Validate(
            "exponent-bound", Parameters("{\"Count\":9223372036854775807}"), Schema(definition));

        Assert.Empty(errors);
    }

    [Fact]
    public void An_undeclared_parameter_is_reported()
    {
        CommandParameterDefinition definition = new("Limit", CommandParameterKind.Integer);

        IReadOnlyList<string> errors = CommandParameterValidator.Validate(
            "undeclared", Parameters("{\"Other\":1}"), Schema(definition));

        Assert.Contains(errors, error => error.Contains("is not declared", StringComparison.Ordinal));
    }

    [Fact]
    public void A_command_without_a_declared_schema_accepts_its_payload_unchanged()
    {
        IReadOnlyList<string> errors = CommandParameterValidator.Validate(
            "no-schema", Parameters("{\"Anything\":1}"));

        Assert.Empty(errors);
    }
}
