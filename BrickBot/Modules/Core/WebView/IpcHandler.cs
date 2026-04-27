using System.Text.Json;
using BrickBot.Modules.Core.Events;
using BrickBot.Modules.Core.Ipc;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BrickBot.Modules.Core.WebView;

public sealed class IpcHandler
{
    private readonly IFacadeRegistry _registry;
    private readonly IProfileEventBus _eventBus;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ILogger<IpcHandler> _logger;
    private WebView2? _webView;

    public IpcHandler(
        IFacadeRegistry registry,
        IProfileEventBus eventBus,
        JsonSerializerOptions jsonOptions,
        ILogger<IpcHandler> logger)
    {
        _registry = registry;
        _eventBus = eventBus;
        _jsonOptions = jsonOptions;
        _logger = logger;

        _eventBus.Subscribe(OnEventEmittedAsync);
    }

    public void Attach(WebView2 webView)
    {
        _webView = webView;
        webView.WebMessageReceived += OnWebMessageReceived;
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        // Frontend posts a JSON string via postMessage(JSON.stringify(message)).
        // TryGetWebMessageAsString returns the raw string; WebMessageAsJson would wrap it
        // in extra quotes (e.g. "\"{...}\"") which breaks deserialization.
        var json = args.TryGetWebMessageAsString();
        if (string.IsNullOrEmpty(json))
        {
            // Fallback: frontend posted a plain object — WebMessageAsJson is the right shape.
            json = args.WebMessageAsJson;
        }

        try
        {
            var request = JsonSerializer.Deserialize<IpcRequest>(json, _jsonOptions);
            if (request is null)
            {
                _logger.LogWarning("Received null IPC request");
                return;
            }

            var facade = _registry.Get(request.Module);
            var response = await facade.HandleAsync(request).ConfigureAwait(true);
            await SendAsync(response).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle IPC message: {Message}", json);
        }
    }

    private Task OnEventEmittedAsync(EventEnvelope envelope)
    {
        if (_webView is null) return Task.CompletedTask;

        var notification = new
        {
            category = "NOTIFICATION",
            module = envelope.Module,
            type = envelope.Type,
            payload = envelope.Payload,
        };

        return SendRawAsync(notification);
    }

    private Task SendAsync(IpcResponse response) => SendRawAsync(response);

    private Task SendRawAsync(object payload)
    {
        var webView = _webView;
        if (webView is null || webView.IsDisposed || !webView.IsHandleCreated)
        {
            return Task.CompletedTask;
        }

        var json = JsonSerializer.Serialize(payload, _jsonOptions);

        try
        {
            if (webView.InvokeRequired)
            {
                // BeginInvoke (fire-and-forget) instead of Invoke. This avoids a deadlock at
                // shutdown: FormClosed → SaveWindowState.Wait() blocks the UI thread → the
                // emit handler tries to Invoke back onto that same blocked UI thread → hang.
                webView.BeginInvoke(new Action(() =>
                {
                    if (!webView.IsDisposed)
                    {
                        webView.CoreWebView2?.PostWebMessageAsString(json);
                    }
                }));
            }
            else
            {
                webView.CoreWebView2?.PostWebMessageAsString(json);
            }
        }
        catch (ObjectDisposedException) { /* WebView torn down mid-send — drop it */ }
        catch (InvalidOperationException) { /* Invoke after the form's handle is gone */ }

        return Task.CompletedTask;
    }
}
