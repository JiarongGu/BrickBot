using BrickBot.Modules.Core.Helpers;

namespace BrickBot.Modules.Core.Services;

/// <summary>
/// Pre-warms caches and runs initialization work during the splash screen, before the UI is interactive.
/// Modules contribute warmup tasks via <see cref="IEagerLoadingTask"/> registered in DI.
/// </summary>
public interface IEagerLoadingService
{
    Task EagerLoadAsync(IProgress<EagerLoadingProgress>? progress = null);
}

/// <summary>
/// A unit of eager-loading work contributed by a module. Order ascending. Higher = later.
/// </summary>
public interface IEagerLoadingTask
{
    /// <summary>Lower runs first. Use 100/200/300 to leave room for inserts.</summary>
    int Order { get; }

    /// <summary>Short label shown on the splash screen.</summary>
    string Label { get; }

    Task ExecuteAsync(CancellationToken cancellationToken = default);
}

public sealed class EagerLoadingProgress
{
    public string Operation { get; set; } = string.Empty;
    public int Percent { get; set; }
    public bool IsComplete { get; set; }
}

/// <summary>
/// Default service: runs all DI-registered <see cref="IEagerLoadingTask"/>s in order.
/// Failures are logged but never propagate (eager load is non-critical).
/// </summary>
public sealed class EagerLoadingService : IEagerLoadingService
{
    private readonly ILogHelper _logger;
    private readonly IEnumerable<IEagerLoadingTask> _tasks;

    public EagerLoadingService(ILogHelper logger, IEnumerable<IEagerLoadingTask> tasks)
    {
        _logger = logger;
        _tasks = tasks;
    }

    public async Task EagerLoadAsync(IProgress<EagerLoadingProgress>? progress = null)
    {
        _logger.Info("Starting eager loading...", "EagerLoading");

        var ordered = _tasks.OrderBy(t => t.Order).ToList();
        if (ordered.Count == 0)
        {
            progress?.Report(new EagerLoadingProgress { Operation = "Ready", Percent = 100, IsComplete = true });
            _logger.Info("No eager loading tasks registered", "EagerLoading");
            return;
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            var task = ordered[i];
            var percent = (int)((i + 1) * 100.0 / ordered.Count);

            progress?.Report(new EagerLoadingProgress { Operation = task.Label, Percent = percent });
            _logger.Verbose($"Eager task: {task.Label} ({percent}%)", "EagerLoading");

            try
            {
                await task.ExecuteAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Eager task '{task.Label}' failed (non-critical): {ex.Message}", "EagerLoading");
            }
        }

        progress?.Report(new EagerLoadingProgress { Operation = "Ready", Percent = 100, IsComplete = true });
        _logger.Info("Eager loading completed", "EagerLoading");
    }
}
