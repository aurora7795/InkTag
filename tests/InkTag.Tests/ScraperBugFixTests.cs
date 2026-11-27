using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using InkTag.Core.Scrapers;
using Xunit;

namespace InkTag.Tests;

public class ScraperBugFixTests
{
    private sealed class TrackingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;
        public int Calls { get; private set; }

        public TrackingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> factory)
        {
            _factory = factory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_factory(request));
        }
    }

    private sealed class DisposalTrackingContent : StringContent
    {
        public bool Disposed { get; private set; }
        public DisposalTrackingContent(string content) : base(content) { }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    [Fact]
    public void NormalizeIssueNumber_PreservesZero()
    {
        Assert.Equal("0", ComicVineProvider.NormalizeIssueNumber("0"));
        Assert.Equal("0", ComicVineProvider.NormalizeIssueNumber("00"));
        Assert.Equal("0", ComicVineProvider.NormalizeIssueNumber("#0"));
        Assert.Equal("5", ComicVineProvider.NormalizeIssueNumber("#005"));
    }

    [Fact]
    public async Task ComicVineProvider_DoesNotCacheErrorPayload()
    {
        string cacheFile = Path.Combine(Path.GetTempPath(), $"cv_cache_{Guid.NewGuid():N}.json");
        try
        {
            using var cache = new ScraperCacheService(cacheFile);
            int callCount = 0;
            var handler = new TrackingHttpMessageHandler(_ =>
            {
                callCount++;
                string body = callCount == 1
                    ? "{\"status_code\": 100, \"error\": \"Invalid API Key\"}"
                    : "{\"status_code\": 1, \"results\": []}";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
            });

            var rateLimited = new RateLimitedHttpClient(new HttpClient(handler));
            var provider = new ComicVineProvider(rateLimited, cache);

            await provider.SearchAsync(new ComicSearchQuery { Series = "Batman", IssueNumber = "1" }, "test-key");
            await provider.SearchAsync(new ComicSearchQuery { Series = "Batman", IssueNumber = "1" }, "test-key");

            Assert.Equal(2, callCount);
        }
        finally
        {
            if (File.Exists(cacheFile)) File.Delete(cacheFile);
        }
    }

    [Fact]
    public async Task RateLimitedHttpClient_DisposesResponse_OnHttpError()
    {
        var trackingContent = new DisposalTrackingContent("Server Error");
        var handler = new TrackingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = trackingContent });

        var client = new RateLimitedHttpClient(new HttpClient(handler));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetStringAsync("https://example.com/test"));
        Assert.True(trackingContent.Disposed);
    }

    [Fact]
    public void ScraperCacheService_SetAfterDispose_DoesNotThrow()
    {
        string cacheFile = Path.Combine(Path.GetTempPath(), $"cv_dispose_{Guid.NewGuid():N}.json");
        try
        {
            var cache = new ScraperCacheService(cacheFile);
            cache.Dispose();
            var ex = Record.Exception(() => cache.Set("key", "value"));
            Assert.Null(ex);
        }
        finally
        {
            if (File.Exists(cacheFile)) File.Delete(cacheFile);
        }
    }
}
