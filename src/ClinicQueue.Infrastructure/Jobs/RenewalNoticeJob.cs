using ClinicQueue.Application.Platform.Subscriptions;
using Microsoft.Extensions.Logging;

namespace ClinicQueue.Infrastructure.Jobs;

/// <summary>
/// Daily job: expire past-due subscriptions and notify clinic owners 7 days before renewal (FR-102, FR-103).
/// </summary>
public sealed class RenewalNoticeJob
{
    private readonly ISubscriptionLifecycleService _lifecycle;
    private readonly ILogger<RenewalNoticeJob> _logger;

    public RenewalNoticeJob(ISubscriptionLifecycleService lifecycle, ILogger<RenewalNoticeJob> logger)
    {
        _lifecycle = lifecycle;
        _logger = logger;
    }

    public async Task ProcessAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Running subscription renewal notice / expiry sweep");
        await _lifecycle.ProcessRenewalsAsync(cancellationToken);
    }
}
