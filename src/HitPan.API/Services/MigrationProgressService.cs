using System.Collections.Concurrent;
using HitPan.API.Hubs;
using HitPan.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace HitPan.API.Services;

/// <summary>
/// 마이그레이션 진행률 SignalR push 구현 (정공법 CODE-01, 2026-05-14).
///
/// Singleton 등록 — 모든 요청·백그라운드 잡이 동일 인스턴스 공유.
/// IDbConnection 의존 0 (DI captive 차단). IHubContext만 주입.
///
/// 헌법 준수:
///   - #15 빈 catch 금지: SignalR push 실패 시 LogWarning.
///   - #16 MySqlConnection 미사용 (in-memory + Hub만).
///   - #19 warnings 0.
///   - #25 안전하게: Hub Authorize → 인증된 클라만 수신.
/// </summary>
public sealed class MigrationProgressService : IMigrationProgressService
{
    private readonly IHubContext<MigrationProgressHub> _hub;
    private readonly ILogger<MigrationProgressService> _logger;

    // jobId → tableKey → snapshot
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, MigrationProgressSnapshot>> _store = new();

    public MigrationProgressService(IHubContext<MigrationProgressHub> hub, ILogger<MigrationProgressService> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task UpdateAsync(string jobId, string tableName, string status, int rows, long elapsedMs, string? errorMessage)
    {
        if (string.IsNullOrEmpty(jobId) || string.IsNullOrEmpty(tableName)) return;

        var jobTables = _store.GetOrAdd(jobId, _ => new ConcurrentDictionary<string, MigrationProgressSnapshot>());
        var snap = jobTables.GetOrAdd(tableName, _ => new MigrationProgressSnapshot());
        snap.Status = status;
        snap.Rows = rows;
        snap.ElapsedMs = elapsedMs;
        snap.ErrorMessage = errorMessage;

        try
        {
            await _hub.Clients.Group(jobId).SendAsync("ProgressUpdate", new
            {
                jobId,
                tableName,
                status,
                rows,
                elapsedMs,
                errorMessage,
            });
        }
        catch (Exception ex)
        {
            // 헌법 #15 빈 catch 금지 — push 실패해도 in-memory는 살아 폴링 fallback 가능.
            _logger.LogWarning(ex, "[MigrationProgressService] SignalR push 실패 jobId={JobId} table={Table}", jobId, tableName);
        }
    }

    public async Task CompleteJobAsync(string jobId, string finalStatus, string? errorMessage = null)
    {
        if (string.IsNullOrEmpty(jobId)) return;

        try
        {
            await _hub.Clients.Group(jobId).SendAsync("JobCompleted", new
            {
                jobId,
                status = finalStatus,
                errorMessage,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MigrationProgressService] JobCompleted push 실패 jobId={JobId}", jobId);
        }

        // 10분 후 정리 (재접속·디버깅 여유).
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMinutes(10));
            _store.TryRemove(jobId, out _);
        });
    }

    public IReadOnlyDictionary<string, MigrationProgressSnapshot> GetSnapshot(string jobId)
    {
        if (_store.TryGetValue(jobId, out var snap))
        {
            return snap;
        }
        return new Dictionary<string, MigrationProgressSnapshot>();
    }
}
