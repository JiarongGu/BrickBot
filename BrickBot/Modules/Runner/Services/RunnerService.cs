using BrickBot.Modules.Capture.Services;
using BrickBot.Modules.Core;
using BrickBot.Modules.Core.Events;
using BrickBot.Modules.Core.Exceptions;
using BrickBot.Modules.Input.Models;
using BrickBot.Modules.Input.Services;
using BrickBot.Modules.Profile.Services;
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
    private readonly IProfileService _profileService;
    private readonly IInputService _input;
    private readonly ILogger<RunnerService> _logger;

    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Thread? _thread;
    private ScriptHost? _activeHost;
    private RunnerState _state = new(RunnerStatus.Idle);

    public RunnerService(
        ICaptureService capture,
        IWindowFinder windowFinder,
        IScriptEngine scriptEngine,
        IScriptFileService scriptFiles,
        IScriptDispatcher dispatcher,
        IRunLog log,
        IProfileEventBus eventBus,
        IProfileService profileService,
        IInputService input,
        ILogger<RunnerService> logger)
    {
        _capture = capture;
        _windowFinder = windowFinder;
        _scriptEngine = scriptEngine;
        _scriptFiles = scriptFiles;
        _dispatcher = dispatcher;
        _log = log;
        _eventBus = eventBus;
        _profileService = profileService;
        _input = input;
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

            // Apply per-profile input delivery mode. Singleton input service so the mode
            // persists across calls — overwrite at every Start so switching profiles between
            // runs picks up the right mode without leaking the previous profile's setting.
            var inputMode = InputMode.SendInput;
            try
            {
                var cfg = _profileService.GetProfileConfigurationAsync(request.ProfileId).GetAwaiter().GetResult();
                inputMode = cfg?.Input?.Mode ?? InputMode.SendInput;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load input config for profile {Profile}; defaulting to SendInput", request.ProfileId);
            }
            _input.Mode = inputMode;
            _input.TargetWindow = window.Handle;

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
            var cts = _cts;
            var ct = cts.Token;

            var host = new ScriptHost(
                _capture,
                request.ProfileId,
                window.Handle,
                window.X,
                window.Y,
                request.TemplateRoot,
                cts,
                request.StopWhen);
            _activeHost = host;

            var context = new ScriptContext();

            // Reset dispatcher state from any previous Run before the new engine boots
            // so stale registered actions / queued invocations never leak across runs.
            _dispatcher.Reset();

            UpdateState(new RunnerState(RunnerStatus.Running));
            _log.Info($"Run started against \"{window.Title}\" ({window.Width}x{window.Height}) — main: {request.MainName}, libraries available: {availableLibraries.Count}, input: {inputMode}");
            if (request.StopWhen is { } sw)
            {
                _log.Info($"Stop conditions: " +
                    (sw.TimeoutMs.HasValue ? $"timeout={sw.TimeoutMs}ms " : "") +
                    (string.IsNullOrEmpty(sw.OnEvent) ? "" : $"onEvent='{sw.OnEvent}' ") +
                    (string.IsNullOrEmpty(sw.CtxKey) ? "" : $"ctx['{sw.CtxKey}'] {sw.CtxOp ?? "eq"} {sw.CtxValue}"));
            }

            // Timeout watchdog. Runs independently of the script thread so it trips even when the
            // script blocks on a long vision call (which never gets back to runForever to check).
            if (request.StopWhen?.TimeoutMs is int timeoutMs && timeoutMs > 0)
            {
                _ = Task.Delay(timeoutMs, ct).ContinueWith(t =>
                {
                    if (!t.IsCanceled) host.RequestStop(StopReason.Timeout, $"{timeoutMs}ms elapsed");
                }, TaskScheduler.Default);
            }

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
        ScriptHost? host;
        lock (_lock)
        {
            if (_state.Status != RunnerStatus.Running) return;
            UpdateState(new RunnerState(RunnerStatus.Stopping));
            host = _activeHost;
        }

        // RequestStop is the single shutdown path — it sets StoppedReason=User AND cancels the
        // CTS so blocked vision/wait calls wake up. Plain cts.Cancel() would race the JS-side
        // stop-reason write and lose the "user clicked stop" signal.
        host?.RequestStop(StopReason.User, "user");
    }

    private void RunLoop(ScriptHost host, ScriptRunRequest run, ScriptContext context, CancellationToken ct)
    {
        try
        {
            _scriptEngine.Execute(run, host, context);
            // Script returned naturally — main didn't call runForever or it exited cleanly.
            var reason = host.StoppedReason == StopReason.None ? StopReason.Completed : host.StoppedReason;
            _log.Info($"Script completed ({reason}).");
            UpdateState(new RunnerState(RunnerStatus.Idle, StoppedReason: reason, StoppedDetail: host.StoppedDetail));
        }
        catch (OperationCanceledException)
        {
            // Cancelled either by user, timeout watchdog, or script-side brickbot.stop() / event /
            // ctx predicate trigger. host.StoppedReason carries the precise reason — fall back to
            // User if nobody set one (defensive).
            var reason = host.StoppedReason == StopReason.None ? StopReason.User : host.StoppedReason;
            _log.Info($"Run stopped ({reason}{(host.StoppedDetail is null ? "" : $": {host.StoppedDetail}")}).");
            UpdateState(new RunnerState(RunnerStatus.Idle, StoppedReason: reason, StoppedDetail: host.StoppedDetail));
        }
        catch (OperationException op)
        {
            _logger.LogWarning(op, "Run aborted: {Code}", op.Code);
            _log.Error($"{op.Code}: {op.Message}");
            UpdateState(new RunnerState(RunnerStatus.Faulted, op.Message, StopReason.Faulted, op.Code));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled run failure");
            _log.Error(ex.Message);
            UpdateState(new RunnerState(RunnerStatus.Faulted, ex.Message, StopReason.Faulted, ex.Message));
        }
        finally
        {
            // Clear dispatcher state so the UI's Actions panel updates back to empty
            // even when a faulted run leaves stale registrations behind.
            _dispatcher.Reset();
            lock (_lock) _activeHost = null;
        }
    }

    private void UpdateState(RunnerState newState)
    {
        lock (_lock) _state = newState;
        _ = _eventBus.EmitAsync(ModuleNames.RUNNER, RunnerEvents.STATUS_CHANGED, newState);
    }
}
