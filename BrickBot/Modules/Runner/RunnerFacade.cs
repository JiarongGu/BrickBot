using BrickBot.Modules.Core.Ipc;
using BrickBot.Modules.Runner.Models;
using BrickBot.Modules.Runner.Services;
using Microsoft.Extensions.Logging;

namespace BrickBot.Modules.Runner;

public sealed class RunnerFacade : BaseFacade
{
    private readonly IRunnerService _service;
    private readonly PayloadHelper _payload;

    public RunnerFacade(IRunnerService service, PayloadHelper payload, ILogger<RunnerFacade> logger) : base(logger)
    {
        _service = service;
        _payload = payload;
    }

    protected override Task<object?> RouteMessageAsync(IpcRequest request)
    {
        return request.Type switch
        {
            "GET_STATE" => Task.FromResult<object?>(_service.State),
            "START" => Task.FromResult<object?>(Start(request)),
            "STOP" => Task.FromResult<object?>(Stop()),
            "LIST_ACTIONS" => Task.FromResult<object?>(new { actions = _service.ListActions() }),
            "INVOKE_ACTION" => Task.FromResult<object?>(InvokeAction(request)),
            _ => throw new InvalidOperationException($"Unknown RUNNER request type: {request.Type}"),
        };
    }

    private object InvokeAction(IpcRequest request)
    {
        var name = _payload.GetRequiredValue<string>(request.Payload, "name");
        _service.InvokeAction(name);
        return new { success = true };
    }

    private RunnerState Start(IpcRequest request)
    {
        var windowHandle = (nint)_payload.GetRequiredValue<long>(request.Payload, "windowHandle");
        var profileId = _payload.GetRequiredValue<string>(request.Payload, "profileId");
        var mainName = _payload.GetRequiredValue<string>(request.Payload, "mainName");
        var templateRoot = _payload.GetOptionalValue<string>(request.Payload, "templateRoot") ?? string.Empty;

        _service.Start(new RunRequest(windowHandle, profileId, mainName, templateRoot));
        return _service.State;
    }

    private RunnerState Stop()
    {
        _service.Stop();
        return _service.State;
    }
}
