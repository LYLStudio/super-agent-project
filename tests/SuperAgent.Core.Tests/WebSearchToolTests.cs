using System.Net;
using System.Text;
using System.Text.Json;
using SuperAgent.Core;
using Xunit;

namespace SuperAgent.Core.Tests;

public class WebSearchToolTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsBingSearchResultsAndSnippets()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal(
                "https://www.bing.com/search?q=dotnet%2010",
                request.RequestUri?.AbsoluteUri);
            return HtmlResponse(BingResult(
                "Download <strong>.NET 10</strong>",
                BingRedirectUrl("https://dotnet.microsoft.com/download?x=1"),
                "Get &amp; install .NET 10."));
        });
        using var client = new HttpClient(handler);
        var tool = new WebSearchTool(client);
        var arguments = JsonSerializer.SerializeToElement(new
        {
            query = "dotnet 10",
            max_results = 1
        });

        var result = await tool.ExecuteAsync(arguments, CancellationToken.None);

        Assert.Contains("找到 1 個結果", result);
        Assert.Contains("來源: Bing", result);
        Assert.Contains("Download .NET 10", result);
        Assert.Contains("https://dotnet.microsoft.com/download?x=1", result);
        Assert.Contains("Get & install .NET 10.", result);
        Assert.DoesNotContain("Official documentation", result);
        Assert.DoesNotContain("example.com", result);
    }

    [Fact]
    public async Task ExecuteAsync_CanOmitSearchSnippets()
    {
        var handler = new StubHttpMessageHandler(_ => HtmlResponse("""
            <li class="b_algo">
              <h2><a href="https://example.org/article">Example article</a></h2>
              <div class="b_caption"><p>A real search excerpt.</p></div>
            </li>
            """));
        using var client = new HttpClient(handler);
        var tool = new WebSearchTool(client);
        var arguments = JsonSerializer.SerializeToElement(new
        {
            query = "article",
            extract_content = false
        });

        var result = await tool.ExecuteAsync(arguments, CancellationToken.None);

        Assert.Contains("Example article", result);
        Assert.Contains("https://example.org/article", result);
        Assert.DoesNotContain("A real search excerpt.", result);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsHttpFailure()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var client = new HttpClient(handler);
        var tool = new WebSearchTool(client);
        var arguments = JsonSerializer.SerializeToElement(new { query = "outage" });

        var result = await tool.ExecuteAsync(arguments, CancellationToken.None);

        Assert.Contains("Error executing web search", result);
        Assert.Contains("503", result);
    }

    [Fact]
    public async Task ExecuteAsync_FallsBackToDuckDuckGoWhenBingIsBlocked()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.Host == "www.bing.com")
            {
                return HtmlResponse("<html><div id=\"b_captcha\">captcha</div></html>");
            }

            Assert.Equal("html.duckduckgo.com", request.RequestUri?.Host);
            return HtmlResponse("""
                <a class="result__a" href="https://example.org/fallback">Fallback result</a>
                <a class="result__snippet">Found through DuckDuckGo.</a>
                """);
        });
        using var client = new HttpClient(handler);
        var tool = new WebSearchTool(client);
        var arguments = JsonSerializer.SerializeToElement(new { query = "fallback" });

        var result = await tool.ExecuteAsync(arguments, CancellationToken.None);

        Assert.Contains("來源: DuckDuckGo", result);
        Assert.Contains("Fallback result", result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task ExecuteAsync_RejectsOutOfRangeResultCount(int maxResults)
    {
        var tool = new WebSearchTool(new HttpClient(new StubHttpMessageHandler(
            _ => throw new InvalidOperationException("Request should not be sent."))));
        var arguments = JsonSerializer.SerializeToElement(new
        {
            query = "anything",
            max_results = maxResults
        });

        var result = await tool.ExecuteAsync(arguments, CancellationToken.None);

        Assert.Contains("max_results 必須介於 1 到 10", result);
    }

    private static HttpResponseMessage HtmlResponse(string html) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html")
        };

    private static string BingResult(string title, string url, string snippet) =>
        $"""
        <ol id="b_results">
          <li class="b_algo">
            <h2><a href="{url}">{title}</a></h2>
            <div class="b_caption"><p>{snippet}</p></div>
          </li>
        </ol>
        """;

    private static string BingRedirectUrl(string destination)
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(destination))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"https://www.bing.com/ck/a?u=a1{encoded}";
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
