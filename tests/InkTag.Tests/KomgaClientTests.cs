using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InkTag.Core;
using InkTag.Core.Configuration;
using InkTag.Core.Komga;
using InkTag.Core.Logging;
using Xunit;

namespace InkTag.Tests;

public class KomgaClientTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Handler { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(Handler(request));
        }
    }

    [Fact]
    public void KomgaClient_TranslatePath_AppliesPrefixMapping()
    {
        var mappings = new List<KomgaPathMapping>
        {
            new KomgaPathMapping
            {
                LocalPrefix = "/Volumes/ComicsShare",
                ServerPrefix = "/data/comics"
            }
        };

        string localFile = "/Volumes/ComicsShare/Batman (2016)/001.cbz";
        string translated = KomgaClient.TranslatePath(localFile, mappings);

        Assert.Equal("/data/comics/Batman (2016)/001.cbz", translated);
    }

    [Fact]
    public void KomgaClient_TranslatePath_HandlesWindowsBackslashes()
    {
        var mappings = new List<KomgaPathMapping>
        {
            new KomgaPathMapping
            {
                LocalPrefix = @"Z:\Comics",
                ServerPrefix = "/data/comics"
            }
        };

        string localFile = @"Z:\Comics\Spider-Man\001.cbz";
        string translated = KomgaClient.TranslatePath(localFile, mappings);

        Assert.Equal("/data/comics/Spider-Man/001.cbz", translated);
    }

    [Fact]
    public async Task KomgaClient_TestConnectionAsync_ReturnsTrueOnSuccess()
    {
        var mockHandler = new MockHttpMessageHandler
        {
            Handler = req =>
            {
                Assert.Contains("/api/v2/users/me", req.RequestUri?.ToString() ?? "");
                Assert.True(req.Headers.Contains("X-API-Key") || req.Headers.Contains("X-Auth-Token"));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"id\": \"user-1\", \"email\": \"admin@komga.org\"}", System.Text.Encoding.UTF8, "application/json")
                };
            }
        };

        using var httpClient = new HttpClient(mockHandler);
        using var client = new KomgaClient("http://localhost:25600", apiKey: "test-token", httpClient: httpClient);

        bool success = await client.TestConnectionAsync();
        Assert.True(success);
    }

    private class TransientThenSuccessHandler : HttpMessageHandler
    {
        private int _attempts;
        public int Attempts => _attempts;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _attempts++;
            if (_attempts == 1)
            {
                throw new HttpRequestException("No route to host", new System.Net.Sockets.SocketException((int)SocketError.HostUnreachable));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\": \"user-1\"}", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    [Fact]
    public async Task KomgaClient_TestConnectionAsync_RetriesAfterTransientNetworkFailure()
    {
        using var mockHandler = new TransientThenSuccessHandler();
        using var httpClient = new HttpClient(mockHandler);
        using var client = new KomgaClient("http://localhost:25600", apiKey: "test-token", httpClient: httpClient);

        bool success = await client.TestConnectionAsync();

        Assert.True(success);
        Assert.Equal(2, mockHandler.Attempts);
    }

    private class AlwaysUnreachableHandler : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            throw new HttpRequestException("No route to host", new System.Net.Sockets.SocketException((int)SocketError.HostUnreachable));
        }
    }

    [Fact]
    public async Task KomgaClient_TestConnectionAsync_GivesUpAfterMaxRetries()
    {
        using var mockHandler = new AlwaysUnreachableHandler();
        using var httpClient = new HttpClient(mockHandler);
        using var client = new KomgaClient("http://localhost:25600", apiKey: "test-token", httpClient: httpClient);

        bool success = await client.TestConnectionAsync();

        Assert.False(success);
        // Fails on the first endpoint probed (v2/users/me) each time, for the initial try plus 2 retries.
        Assert.Equal(3, mockHandler.Attempts);
    }

    [Fact]
    public async Task KomgaClient_FindBookAndAnalyze_TriggersTargetedEndpoint()
    {
        bool analyzeCalled = false;
        var mockHandler = new MockHttpMessageHandler
        {
            Handler = req =>
            {
                string url = req.RequestUri?.ToString() ?? "";
                if (url.Contains("/api/v1/books?search="))
                {
                    var page = new KomgaPageWrapper<KomgaBookDto>
                    {
                        Content = new List<KomgaBookDto>
                        {
                            new KomgaBookDto
                            {
                                Id = "book-123",
                                SeriesId = "series-456",
                                Name = "001.cbz",
                                Url = "/data/comics/Daredevil/001.cbz"
                            }
                        }
                    };
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(page))
                    };
                }
                if (url.EndsWith("/api/v1/books/book-123/analyze"))
                {
                    Assert.Equal(HttpMethod.Post, req.Method);
                    analyzeCalled = true;
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };

        using var httpClient = new HttpClient(mockHandler);
        using var client = new KomgaClient("http://localhost:25600", apiKey: "test-key", httpClient: httpClient);

        var book = await client.FindBookByFilePathAsync("/data/comics/Daredevil/001.cbz");
        Assert.NotNull(book);
        Assert.Equal("book-123", book!.Id);

        bool analyzed = await client.AnalyzeBookAsync(book.Id);
        Assert.True(analyzed);
        Assert.True(analyzeCalled);
    }

    [Fact]
    public async Task KomgaSyncService_SyncsStoryArcCollection()
    {
        bool collectionCreated = false;
        var mockHandler = new MockHttpMessageHandler
        {
            Handler = req =>
            {
                string url = req.RequestUri?.ToString() ?? "";
                if (url.Contains("/api/v1/books?search="))
                {
                    var page = new KomgaPageWrapper<KomgaBookDto>
                    {
                        Content = new List<KomgaBookDto>
                        {
                            new KomgaBookDto
                            {
                                Id = "book-999",
                                SeriesId = "series-777",
                                Name = "001.cbz",
                                Url = "/comics/Avengers/001.cbz"
                            }
                        }
                    };
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(page))
                    };
                }
                if (url.EndsWith("/api/v1/books/book-999/analyze"))
                {
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }
                if (url.Contains("/api/v1/collections?search="))
                {
                    // Return empty collections search
                    var emptyPage = new KomgaPageWrapper<KomgaCollectionDto> { Content = new() };
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(emptyPage))
                    };
                }
                if (url.EndsWith("/api/v1/collections") && req.Method == HttpMethod.Post)
                {
                    collectionCreated = true;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"id\":\"col-1\",\"name\":\"Infinity Gauntlet\"}")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };

        using var httpClient = new HttpClient(mockHandler);
        var settingsService = new AppSettingsService();
        settingsService.Settings.KomgaServerUrl = "http://localhost:25600";
        settingsService.Settings.KomgaApiKey = "token";
        settingsService.Settings.KomgaSyncStoryArcsToCollections = true;

        using var client = new KomgaClient(settingsService, httpClient);
        var syncService = new KomgaSyncService(settingsService, client);

        var comicInfo = new ComicInfo
        {
            Series = "Avengers",
            Number = "1",
            StoryArc = "Infinity Gauntlet"
        };

        var report = await syncService.SyncComicFileAsync("/comics/Avengers/001.cbz", comicInfo);

        Assert.True(report.IsSuccess);
        Assert.Equal(1, report.BooksAnalyzed);
        Assert.Equal(1, report.CollectionsSynced);
        Assert.True(collectionCreated);
    }

    [Fact]
    public async Task KomgaClient_FindBookAndSeries_DoNotReturnBlindFirstElementFallback()
    {
        var mockHandler = new MockHttpMessageHandler
        {
            Handler = req =>
            {
                string url = req.RequestUri?.ToString() ?? "";
                if (url.Contains("/api/v1/books"))
                {
                    var page = new KomgaPageWrapper<KomgaBookDto>
                    {
                        Content = new List<KomgaBookDto>
                        {
                            new KomgaBookDto { Id = "wrong-book", Name = "Unrelated Book", Url = "/data/other/Unrelated.cbz" }
                        }
                    };
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(page), System.Text.Encoding.UTF8, "application/json")
                    };
                }
                if (url.Contains("/api/v1/series"))
                {
                    var page = new KomgaPageWrapper<KomgaSeriesDto>
                    {
                        Content = new List<KomgaSeriesDto>
                        {
                            new KomgaSeriesDto { Id = "wrong-series", Name = "Unrelated Series", Url = "/data/other" }
                        }
                    };
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(page), System.Text.Encoding.UTF8, "application/json")
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };

        using var httpClient = new HttpClient(mockHandler);
        using var client = new KomgaClient("http://localhost:25600", apiKey: "test-token", httpClient: httpClient);

        var book = await client.FindBookByFilePathAsync("/data/comics/Batman/001.cbz");
        var series = await client.FindSeriesByPathOrNameAsync("/data/comics/Batman", "Batman");

        Assert.Null(book);
        Assert.Null(series);
    }

    [Fact]
    public async Task KomgaClient_AnalyzeBookAsync_EscapesBookId()
    {
        string? capturedUrl = null;
        var mockHandler = new MockHttpMessageHandler
        {
            Handler = req =>
            {
                capturedUrl = req.RequestUri?.AbsoluteUri;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
        };

        using var httpClient = new HttpClient(mockHandler);
        using var client = new KomgaClient("http://localhost:25600", apiKey: "test-token", httpClient: httpClient);

        bool ok = await client.AnalyzeBookAsync("id/with spaces");
        Assert.True(ok);
        Assert.Equal("http://localhost:25600/api/v1/books/id%2Fwith%20spaces/analyze", capturedUrl);
    }
}

[Collection("AppLogger")]
public class KomgaClientUrlSecurityTests
{
    [Theory]
    [InlineData("komga.example.com:25600", "http://komga.example.com:25600", "Inferred unencrypted 'http://' scheme for remote host 'komga.example.com'")]
    [InlineData("http://remote-server.local:25600/opds", "http://remote-server.local:25600", "Using unencrypted HTTP for remote host 'remote-server.local'")]
    public void CleanServerUrl_WarnsWhenUsingHttpOnRemoteHost(string input, string expectedUrl, string expectedLogFragment)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "InkTagKomgaUrlTests_" + Guid.NewGuid().ToString("N"));
        string customLogPath = Path.Combine(tempDir, "test.log");

        try
        {
            AppLogger.Initialize(customLogPath);

            string cleaned = KomgaClient.CleanServerUrl(input);

            Assert.Equal(expectedUrl, cleaned);
            Assert.True(File.Exists(customLogPath));
            string logContent = ReadSharedLog(customLogPath);
            Assert.Contains(expectedLogFragment, logContent);
        }
        finally
        {
            AppLogger.CloseWriter();
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Theory]
    [InlineData("http://localhost:25600")]
    [InlineData("http://127.0.0.1:25600")]
    [InlineData("localhost:25600")]
    [InlineData("127.0.0.1:25600")]
    [InlineData("https://komga.example.com:25600")]
    public void CleanServerUrl_DoesNotWarnForLoopbackOrHttps(string input)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "InkTagKomgaUrlTests_" + Guid.NewGuid().ToString("N"));
        string customLogPath = Path.Combine(tempDir, "test.log");

        try
        {
            AppLogger.Initialize(customLogPath);

            KomgaClient.CleanServerUrl(input);

            if (File.Exists(customLogPath))
            {
                string logContent = ReadSharedLog(customLogPath);
                Assert.DoesNotContain("[Komga] Inferred unencrypted", logContent);
                Assert.DoesNotContain("[Komga] Using unencrypted HTTP", logContent);
            }
        }
        finally
        {
            AppLogger.CloseWriter();
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    private static string ReadSharedLog(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }
}
