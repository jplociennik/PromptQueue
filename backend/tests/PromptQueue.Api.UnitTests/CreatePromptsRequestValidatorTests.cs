using PromptQueue.Api.Prompts;
using PromptQueue.TestSupport;

namespace PromptQueue.Api.UnitTests;

/// <summary>Testy jednostkowe walidatora batch-POST: limity listy i promptu oraz reguła długości liczonej po trymowaniu.</summary>
public class CreatePromptsRequestValidatorTests
{
    [Fact]
    public void Should_ReturnError_WhenListIsEmpty()
    {
        var request = new CreatePromptsRequestBuilder().WithNoPrompts().Build();

        var errors = CreatePromptsRequestValidator.Validate(request);

        errors.Should().ContainKey("prompts");
    }

    [Fact]
    public void Should_ReturnError_WhenListIsNull()
    {
        var request = new CreatePromptsRequest(null!);

        var errors = CreatePromptsRequestValidator.Validate(request);

        errors.Should().ContainKey("prompts");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_ReturnError_WhenPromptIsBlank(string? content)
    {
        var request = new CreatePromptsRequestBuilder().WithPrompts([content!]).Build();

        var errors = CreatePromptsRequestValidator.Validate(request);

        errors.Should().ContainKey("prompts[0]");
    }

    [Fact]
    public void Should_ReturnError_WhenTrimmedPromptExceedsMaxLength()
    {
        var request = new CreatePromptsRequestBuilder().WithOversizedPrompt().Build();

        var errors = CreatePromptsRequestValidator.Validate(request);

        errors.Should().ContainKey("prompts[0]");
    }

    [Fact]
    public void Should_ReturnError_WhenCountExceedsMax()
    {
        var request = new CreatePromptsRequestBuilder()
            .WithPromptCount(CreatePromptsRequestValidator.MaxPromptsPerRequest + 1)
            .Build();

        var errors = CreatePromptsRequestValidator.Validate(request);

        errors.Should().ContainKey("prompts");
    }

    [Fact]
    public void Should_ReturnNoErrors_WhenRequestIsValid()
    {
        var request = new CreatePromptsRequestBuilder().Build();

        var errors = CreatePromptsRequestValidator.Validate(request);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Should_ReturnNoErrors_WhenPromptHasSurroundingWhitespaceWithinLimit()
    {
        var request = new CreatePromptsRequestBuilder().WithPrompts("  hello  ").Build();

        var errors = CreatePromptsRequestValidator.Validate(request);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Should_ReturnNoErrors_WhenRawLengthExceedsMaxButTrimmedFits()
    {
        // Treść z białymi znakami brzegowymi: raw > MaxPromptLength, po trymie == MaxPromptLength (≤ limit).
        var trimmedToLimit = new string('x', CreatePromptsRequestValidator.MaxPromptLength);
        var request = new CreatePromptsRequestBuilder().WithPrompts($"   {trimmedToLimit}   ").Build();

        var errors = CreatePromptsRequestValidator.Validate(request);

        errors.Should().BeEmpty();
    }
}
