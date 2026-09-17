using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Configuration;
using Jellyfin.Plugin.MindTheGaps.Services.Webhook;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Common.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

[Collection("PluginConfiguration")]
public class WebhookNotifierTests
{
    [Fact]
    public async Task NotifyAsync_PostsStructuredPayload()
    {
        var handler = new RecordingHandler(HttpStatusCode.NoContent);
        InstallPluginConfiguration(new PluginConfiguration { WebhookUrl = "https://hooks.example.test/secret" });
        try
        {
            var notifier = new WebhookNotifier(
                new StubFactory(handler),
                HostProxy.Create("Library", "server-1"),
                NullLogger<WebhookNotifier>.Instance);

            await notifier.NotifyAsync(
                "scan",
                "Scan finished",
                new Dictionary<string, object?> { ["gaps"] = 3 },
                CancellationToken.None);

            Assert.Equal(1, handler.Calls);
            Assert.Equal("https://hooks.example.test/secret", handler.Request!.RequestUri!.ToString());
            Assert.Equal("application/json", handler.Request.Content!.Headers.ContentType!.MediaType);
            using var document = JsonDocument.Parse(handler.Body!);
            Assert.Equal("[Library] Scan finished", document.RootElement.GetProperty("content").GetString());
            Assert.Equal("scan", document.RootElement.GetProperty("event").GetString());
            Assert.Equal("Library", document.RootElement.GetProperty("server").GetString());
            Assert.Equal("server-1", document.RootElement.GetProperty("serverId").GetString());
            Assert.Equal(3, document.RootElement.GetProperty("gaps").GetInt32());
        }
        finally
        {
            SetPluginInstance(null);
        }
    }

    [Fact]
    public async Task NotifyAsync_UnsupportedScheme_DoesNotSend()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        InstallPluginConfiguration(new PluginConfiguration { WebhookUrl = "ftp://hooks.example.test/secret" });
        try
        {
            var notifier = new WebhookNotifier(
                new StubFactory(handler),
                HostProxy.Create("Library", "server-1"),
                NullLogger<WebhookNotifier>.Instance);

            await notifier.NotifyAsync("scan", "ignored", new Dictionary<string, object?>(), CancellationToken.None);

            Assert.Equal(0, handler.Calls);
        }
        finally
        {
            SetPluginInstance(null);
        }
    }

    [Fact]
    public async Task NotifyAsync_ResponseFailureOrTransportException_DoesNotThrow()
    {
        var failure = new RecordingHandler(HttpStatusCode.BadRequest);
        InstallPluginConfiguration(new PluginConfiguration { WebhookUrl = "https://hooks.example.test/secret" });
        try
        {
            var notifier = new WebhookNotifier(
                new StubFactory(failure),
                HostProxy.Create("Library", "server-1"),
                NullLogger<WebhookNotifier>.Instance);
            await notifier.NotifyAsync("scan", "failure", new Dictionary<string, object?>(), CancellationToken.None);
            Assert.Equal(1, failure.Calls);

            var throwing = new RecordingHandler(new HttpRequestException("offline"));
            notifier = new WebhookNotifier(
                new StubFactory(throwing),
                HostProxy.Create("Library", "server-1"),
                NullLogger<WebhookNotifier>.Instance);
            await notifier.NotifyAsync("scan", "offline", new Dictionary<string, object?>(), CancellationToken.None);
            Assert.Equal(1, throwing.Calls);
        }
        finally
        {
            SetPluginInstance(null);
        }
    }

    private static void InstallPluginConfiguration(PluginConfiguration config)
    {
        var plugin = (Plugin)RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
        SetPluginInstance(plugin);
        typeof(BasePlugin<PluginConfiguration>).GetProperty("Configuration", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(plugin, config);
    }

    private static void SetPluginInstance(Plugin? plugin)
        => typeof(Plugin).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, plugin);

    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode? _status;
        private readonly Exception? _exception;

        public RecordingHandler(HttpStatusCode status) => _status = status;

        public RecordingHandler(Exception exception) => _exception = exception;

        public int Calls { get; private set; }

        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (_exception is not null)
            {
                throw _exception;
            }

            return new HttpResponseMessage(_status!.Value);
        }
    }

    private class HostProxy : DispatchProxy
    {
        private string _friendlyName = "Jellyfin";
        private string _systemId = string.Empty;

        public static IServerApplicationHost Create(string friendlyName, string systemId)
        {
            var proxy = (HostProxy)DispatchProxy.Create<IServerApplicationHost, HostProxy>();
            proxy._friendlyName = friendlyName;
            proxy._systemId = systemId;
            return (IServerApplicationHost)proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod?.Name switch
            {
                "get_FriendlyName" => _friendlyName,
                "get_SystemId" => _systemId,
                _ => targetMethod?.ReturnType == typeof(void)
                    ? null
                    : targetMethod?.ReturnType.IsValueType == true
                        ? Activator.CreateInstance(targetMethod.ReturnType)
                        : null
            };
    }
}
