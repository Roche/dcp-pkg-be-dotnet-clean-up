using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenDCP.Framework.Cleanup.Constants;
using OpenDCP.Framework.Cleanup.Interfaces;

namespace OpenDCP.Framework.Cleanup
{
    public abstract class BaseCleanup : BackgroundService
    {
        protected readonly IServiceProvider ServiceProvider;
        protected readonly ILogger<BaseCleanup> Logger;

        protected abstract string Module { get; }
        protected abstract int Version { get; }

        protected BaseCleanup(IServiceProvider serviceProvider, ILogger<BaseCleanup> logger)
        {
            ServiceProvider = serviceProvider;
            Logger = logger;
        }

        private readonly Dictionary<CleanupInterval, TimeSpan> _intervalTimes = new()
        {
            { CleanupInterval.Hourly, TimeSpan.FromHours(1) },
            { CleanupInterval.Daily, TimeSpan.FromDays(1) },
            { CleanupInterval.ThirtyDays, TimeSpan.FromDays(30) },
            { CleanupInterval.ThreeYears, TimeSpan.FromDays(365 * 3) },
            { CleanupInterval.TenYears, TimeSpan.FromDays(365 * 10) }
        };

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            Logger.LogWarning($"DCP {Module} Cleanup Start");

            await base.StartAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
        }
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await base.StopAsync(cancellationToken);

            Logger.LogWarning($"DCP {Module} Cleanup Stop");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            stoppingToken.ThrowIfCancellationRequested();

            using var scope = ServiceProvider.CreateScope();
            var registeredHooks = scope.ServiceProvider.GetServices<ICleanupHook>();

            var intervals = registeredHooks
                .Select(h => h.Interval)
                .ToList();

            if (!intervals.Any())
            {
                Logger.LogWarning($"{Module} No cleanup hooks registered.");
                return;
            }

            var runningTasks = new List<Task>();

            foreach (var interval in intervals)
            {
                if (_intervalTimes.TryGetValue(interval, out var timeSpan))
                {
                    runningTasks.Add(StartCleanupLoopForIntervalAsync(interval, timeSpan, stoppingToken));
                }
            }

            await Task.WhenAll(runningTasks);
        }

        private async Task StartCleanupLoopForIntervalAsync(CleanupInterval interval, TimeSpan period, CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(period);
            Logger.LogInformation($"{Module} Started periodic loop for {interval}.");

            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var executionScope = ServiceProvider.CreateScope();
                    var hooks = executionScope.ServiceProvider.GetServices<ICleanupHook>();

                    var matchingHooks = hooks.Where(h => h.Interval == interval);

                    foreach (var hook in matchingHooks)
                    {
                        Logger.LogInformation($"{Module} Running cleanup: {hook.GetType().Name}");
                        await hook.ExecuteCleanupAsync(stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"{Module} Error executing cleanup loop for interval {interval}: {ex.Message}");
                }
            }
        }
    }
}
