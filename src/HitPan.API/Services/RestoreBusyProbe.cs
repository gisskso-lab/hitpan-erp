using HitPan.API.Services.ManualUpdate;
using HitPan.Application.DTOs.Backup;
using HitPan.Application.Interfaces;

namespace HitPan.API.Services;

/// <summary>
/// 🔴 W-G1-52 갈래 A-2③ D13 — 업데이트 내려받기 · 교체가 도는 중이면 복원 걸음 ④ 앞에서 멈춤(<b>읽기만</b> · #43 업데이트 로직 손 0).
/// 수동 업데이트 일감(<see cref="ManualUpdateService.IsRunning"/>) · 워치독 자동 업데이트 잠금(<see cref="IAutoUpdateLockProbe"/>) 둘을 본다.
/// </summary>
public sealed class RestoreBusyProbe : IRestoreBusyProbe
{
    private readonly ManualUpdateService _manual;
    private readonly IAutoUpdateLockProbe _autoLock;
    private readonly ILogger<RestoreBusyProbe> _logger;

    public RestoreBusyProbe(ManualUpdateService manual, IAutoUpdateLockProbe autoLock, ILogger<RestoreBusyProbe> logger)
    {
        _manual = manual;
        _autoLock = autoLock;
        _logger = logger;
    }

    public string? BusyCode(string tenantId)
    {
        if (_manual.IsRunning) return SafeRestoreDenyCodes.UpdateRunning;
        try
        {
            return _autoLock.IsAutoUpdateInProgress() ? SafeRestoreDenyCodes.UpdateRunning : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 잠금 파일을 못 읽으면 「도는 중」 쪽으로(멈춤 쪽 · 복원은 다시 하면 됨)
            _logger.LogWarning(ex, "자동 업데이트 잠금을 읽지 못해 복원을 멈춤 tenantId={TenantId}", tenantId);
            return SafeRestoreDenyCodes.UpdateRunning;
        }
    }
}
