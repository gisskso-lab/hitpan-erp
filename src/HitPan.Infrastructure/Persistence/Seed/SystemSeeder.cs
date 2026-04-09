using Microsoft.Extensions.Logging;

namespace HitPan.Infrastructure.Persistence.Seed;

public sealed class SystemSeeder
{
    private readonly CommonCodeSeeder _commonCodeSeeder;
    private readonly ILogger<SystemSeeder> _logger;

    public SystemSeeder(CommonCodeSeeder commonCodeSeeder, ILogger<SystemSeeder> logger)
    {
        _commonCodeSeeder = commonCodeSeeder;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        try
        {
            await _commonCodeSeeder.SeedAsync(ct);
            _logger.LogInformation("System seed completed.");
        }
        catch (Exception ex)
        {
            // Seeder failure should not block application startup.
            _logger.LogError(ex, "System seed failed. Application will continue to start.");
        }
    }
}
