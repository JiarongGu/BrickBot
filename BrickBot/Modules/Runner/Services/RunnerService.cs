using BrickBot.Modules.Capture.Services;
using BrickBot.Modules.Core;
using BrickBot.Modules.Core.Events;
using BrickBot.Modules.Core.Exceptions;
using BrickBot.Modules.Runner.Models;
using BrickBot.Modules.Script.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace BrickBot.Modules.Runner.Services;

public sealed class RunnerService : IRunnerService
{
    private readonly ICaptureService _capture;
    private readonly IWindowFinder _windowFinder;
    private readonly IScriptEngine _scriptEngine;
    private readonly IScriptFileService _scriptFiles;
    private readonly IScriptDispatcher _dispatcher;
    private readonly IRunLog _log;
    private readonly IProfileEventBus _eventBus;
    private readonly ILogger<RunnerService> _logger;

    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Thread? _thread;
    private RunnerState _state = new(RunnerStatus.Idle);

    public RunnerService(
        ICaptureService capture,
        IWindowFinder windowFinder,
        IScriptEngine scriptEngine,
        IScriptFileService scriptFiles,
        IScriptDispatcher dispatcher,
        IRunLog log,
        IProfileEventBus eventBus,
        ILogger<RunnerService> logger)
    {
        _capture = capture;
        _windowFinder = windowFinder;
        _scriptEngine = scriptEngine;
        _scriptFiles = scriptFiles;
        _dispatcher = dispatcher;
        _log = log;
        _eventBus = eventBus;
        _logger = logger;
    }

    public IReadOnlyList<string> ListActions() => _dispatcher.GetRegisteredActions();

    public void InvokeAction(string actionName) => _dispatcher.EnqueueInvocation(actionName);

    public RunnerState State { get { lock (_lock) return _state; } }

    public void Start(RunRequest request)
    {
        lock (_lock)
        {
            if (_state.Status == RunnerStatus.Running)
            {
                throw new OperationException("RUNNER_ALREADY_RUNNING");
            }

            var window = _windowFinder.GetByHandle(request.WindowHandle)
                ?? throw new OperationException("RUNNER_WINDOW_NOT_FOUND");

            // Resolve the compiled main BEFORE we mark the run as started so a missing
            // or uncompiled main fails fast. Libraries resolve lazily via require() so
            // we don't pay the load cost for libraries the script never imports.
            var mainSource = _scriptFiles.LoadCompiledMain(request.ProfileId, request.MainName);
            var profileId = request.ProfileId;
            var run = new ScriptRunRequest(
                mainSource,
                libName => _scriptFiles.LoadCompiledLibrary(profileId, libName));

            var availableLibraries = _scriptFiles.ListCompiledLibraries(profileId);

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            var host = new ScriptHost(
                _capture,
                window.Handle,
                window.X,
                window.Y,
                request.TemplateRoot,
                ct);

            var context = new ScriptContext();

            // Reset dispatcher state from any previous Run before the new engine boots
            // so stale registered actions / queued invocations never leak across runs.
            _dispatcher.Reset();

            UpdateState(new RunnerState(RunnerStatus.Running));
            _log.Info($"Run started against \"{window.Title}\" ({window.Width}x{window.Height}) — main: {request.MainName}, libraries available: {availableLibraries.Count}");

            _thread = new Thread(() => RunLoop(host, run, context, ct))
            {
                IsBackground = true,
                Name = "BrickBot.Runner",
            };
            _thread.Start();
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        lock (_lock)
        {
            if (_state.Status != RunnerStatus.Running) return;
            UpdateState(new RunnerState(RunnerStatus.Stopping));
            cts = _cts;
        }

        cts?.Cancel();
    }

    private void RunLoop(IScriptHost host, ScriptRunRequest run, ScriptContext context, CancellationToken ct)
    {
        try
        {
            _scriptEngine.Execute(run, host, context);
            _log.Info("Script completed.");
            UpdateState(new RunnerState(RunnerStatus.Idle));
        }
        catch (OperationCanceledException)
        {
            _log.Info("Run cancelled.");
            UpdateState(new RunnerState(RunnerStatus.Idle));
        }
        catch (OperationException op)
        {
            _logger.LogWarning(op, "Run aborted: {Code}", op.Code);
            _log.Error($"{op.Code}: {op.Message}");
            UpdateState(new RunnerState(RunnerStatus.Faulted, op.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled run failure");
            _log.Error(ex.Message);
            UpdateState(new RunnerState(RunnerStatus.Faulted, ex.Message));
        }
        finally
        {
            // Clear dispatcher state so the UI's Actions panel updates back to empty
            // even when a faulted run leaves stale registrations behind.
            _dispatcher.Reset();
        }
    }

    private void UpdateState(RunnerState newState)
    {
        lock (_lock) _state = newState;
        _ = _eventBus.EmitAsync(ModuleNames.RUNNER, RunnerEvents.STATUS_CHANGED, newState);
    }
}
