using BrickBot.Modules.Capture.Services;
using BrickBot.Modules.Core;
using BrickBot.Modules.Core.Events;
using BrickBot.Modules.Core.Exceptions;
using BrickBot.Modules.Runner.Models;
using BrickBot.Modules.Script.Services;
using Microsoft.Extensions.Logging;

namespace BrickBot.Modules.Runner.Services;

public sealed class RunnerService : IRunnerService
{
    private readonly ICaptureService _capture;
    private readonly IWindowFinder _windowFinder;
    private readonly IScriptEngine _scriptEngine;
    private readonly IScriptFileService _scriptFiles;
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
        IRunLog log,
        IProfileEventBus eventBus,
        ILogger<RunnerService> logger)
    {
        _capture = capture;
        _windowFinder = windowFinder;
        _scriptEngine = scriptEngine;
        _scriptFiles = scriptFiles;
        _log = log;
        _eventBus = eventBus;
        _logger = logger;
    }

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

            // Resolve scripts BEFORE we mark the run as started so a missing main fails fast.
            var libraries = _scriptFiles.LoadLibraries(request.ProfileId);
            var mainSource = _scriptFiles.LoadMain(request.ProfileId, request.MainName);
            var run = new ScriptRunRequest(mainSource, libraries);

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

            UpdateState(new RunnerState(RunnerStatus.Running));
            _log.Info($"Run started against \"{window.Title}\" ({window.Width}x{window.Height}) — main: {request.MainName}, libraries: {libraries.Count}");

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
    }

    private void UpdateState(RunnerState newState)
    {
        lock (_lock) _state = newState;
        _ = _eventBus.EmitAsync(ModuleNames.RUNNER, RunnerEvents.STATUS_CHANGED, newState);
    }
}
