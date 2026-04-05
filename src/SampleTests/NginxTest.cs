using Chronos.Core;

namespace SampleTests;

/// <summary>Пример кодового теста: HTTP с хоста, где крутится compose (LocalTester / агент).</summary>
public sealed class NginxTest
{
    [Test]
    public async Task<CodeTestOutcome> RespondsOn8080(ComposeTestContext ctx, CancellationToken cancellationToken = default)
    {
        using var response = await ctx.Http.GetAsync("http://127.0.0.1:8080/", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return CodeTestOutcome.Fail($"HTTP {(int)response.StatusCode}");

        return CodeTestOutcome.Ok();
    }
}
