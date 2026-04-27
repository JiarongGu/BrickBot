using BrickBot.Modules.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace BrickBot.Modules.Core.Ipc;

public abstract class BaseFacade
{
    protected readonly ILogger Logger;

    protected BaseFacade(ILogger logger)
    {
        Logger = logger;
    }

    public async Task<IpcResponse> HandleAsync(IpcRequest request)
    {
        try
        {
            var data = await RouteMessageAsync(request).ConfigureAwait(false);
            return new IpcResponse { Id = request.Id, Success = true, Data = data };
        }
        catch (OperationException op)
        {
            Logger.LogWarning(op, "Operation error in {Module}/{Type}: {Code}", request.Module, request.Type, op.Code);
            return new IpcResponse
            {
                Id = request.Id,
                Success = false,
                Error = op.Message,
                ErrorDetails = new ErrorDetails(op.Code, op.Parameters),
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unhandled error in {Module}/{Type}", request.Module, request.Type);
            return new IpcResponse { Id = request.Id, Success = false, Error = ex.Message };
        }
    }

    protected abstract Task<object?> RouteMessageAsync(IpcRequest request);
}
