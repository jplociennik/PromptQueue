using System.Net;
using System.Text.Json;

namespace PromptQueue.TestSupport;

/// <summary>Asercje na odpowiedziach HTTP: kształt ValidationProblem (400) i ProblemDetails (np. 500).</summary>
public static class ProblemDetailsAssertions
{
    public static async Task ShouldBeValidationProblemAsync(this HttpResponseMessage response, string expectedErrorKey)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var hasErrorKey = document.RootElement.TryGetProperty("errors", out var errors)
            && errors.TryGetProperty(expectedErrorKey, out _);

        using var scope = new AssertionScope();
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        hasErrorKey.Should().BeTrue($"ValidationProblem powinien zawierać błąd dla klucza '{expectedErrorKey}'");
    }

    public static async Task ShouldBeProblemAsync(this HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        using var _ = new AssertionScope();
        response.StatusCode.Should().Be(expectedStatus);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }
}
