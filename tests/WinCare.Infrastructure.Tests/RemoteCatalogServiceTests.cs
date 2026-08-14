using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Infrastructure.Plugins;
using Xunit;

namespace WinCare.Infrastructure.Tests;

public class RemoteCatalogServiceTests
{
    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    [Fact]
    public async Task GetCatalogAsync_LoadsFromHttpAndCachesLocally()
    {
        var tempCacheFile = Path.Combine(Path.GetTempPath(), $"wincare_test_cache_{Guid.NewGuid():N}.json");

        try
        {
            var mockCatalog = new RemotePluginCatalog
            {
                CatalogVersion = "1.0",
                Plugins = new System.Collections.Generic.List<RemotePluginItem>
                {
                    new RemotePluginItem { Id = "test.plugin", Name = "Test Plugin", Category = "Utilities" }
                }
            };

            var handler = new FakeHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(mockCatalog))
            });

            var httpClient = new HttpClient(handler);
            var service = new RemoteCatalogService(httpClient, cacheFilePath: tempCacheFile);

            var catalog = await service.GetCatalogAsync(forceRefresh: true);

            Assert.NotNull(catalog);
            Assert.Single(catalog.Plugins);
            Assert.Equal("test.plugin", catalog.Plugins[0].Id);
            Assert.True(File.Exists(tempCacheFile));
        }
        finally
        {
            if (File.Exists(tempCacheFile))
            {
                File.Delete(tempCacheFile);
            }
        }
    }

    [Fact]
    public async Task SearchPluginsAsync_FiltersByCategoryAndQuery()
    {
        var tempCacheFile = Path.Combine(Path.GetTempPath(), $"wincare_test_cache_{Guid.NewGuid():N}.json");

        try
        {
            var mockCatalog = new RemotePluginCatalog
            {
                Plugins = new System.Collections.Generic.List<RemotePluginItem>
                {
                    new RemotePluginItem { Id = "p1", Name = "Disk Cleaner", Category = "System Care", Description = "Cleans junk files" },
                    new RemotePluginItem { Id = "p2", Name = "Port Scanner", Category = "Security", Description = "Scans open TCP ports" }
                }
            };

            var handler = new FakeHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(mockCatalog))
            });

            var httpClient = new HttpClient(handler);
            var service = new RemoteCatalogService(httpClient, cacheFilePath: tempCacheFile);

            var securityPlugins = await service.SearchPluginsAsync("Port", category: "Security");
            Assert.Single(securityPlugins);
            Assert.Equal("p2", securityPlugins[0].Id);
        }
        finally
        {
            if (File.Exists(tempCacheFile))
            {
                File.Delete(tempCacheFile);
            }
        }
    }
}
