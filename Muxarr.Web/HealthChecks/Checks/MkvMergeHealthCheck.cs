using Microsoft.Extensions.Diagnostics.HealthChecks;
using Muxarr.Core.Utilities;

namespace Muxarr.Web.HealthChecks.Checks;

public class MkvMergeHealthCheck(ILogger<MkvMergeHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await ProcessExecutor.ExecuteProcessAsync("mkvmerge", "--version", TimeSpan.FromSeconds(5));

            if (result.ExitCode == 0)
            {
                return HealthCheckResult.Healthy();
            }

            return HealthCheckResult.Unhealthy($"mkvmerge exited with code {result.ExitCode}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Health check failed for mkvmerge");
            return HealthCheckResult.Unhealthy("mkvmerge is not available", ex);
        }
    }
}
