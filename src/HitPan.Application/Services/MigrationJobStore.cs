using System.Collections.Concurrent;
using System.Data;
using Dapper;

namespace HitPan.Application.Services;

/// <summary>
/// 마이그레이션 백그라운드 잡 진행 상태 인메모리 저장소.
/// 2026-05-14 야간: Cloudflare 524(100초 한계) 회피 — POST는 즉시 JobId 반환, 진행률은 GET 폴링.
/// P0 #5 (2026-05-14 새벽): migration_jobs DB INSERT 추가 — migration_errors FK 충족 + 영구 기록.
/// 단일 서버 환경 전제 (베타). 클러스터 환경에서는 Redis로 교체 필요.
/// </summary>
public sealed class MigrationJobStore
{
    private readonly ConcurrentDictionary<string, MigrationJob> _jobs = new();
    private readonly IDbConnection _db;

    public MigrationJobStore(IDbConnection db)
    {
        _db = db;
    }

    /// <summary>잡 생성 + migration_jobs DB INSERT (FK 충족).</summary>
    public async Task<MigrationJob> CreateAsync(
        string tenantId, string initiatedBy, string sourceFolder, string? clientIp = null, string? userAgent = null)
    {
        var job = new MigrationJob
        {
            JobId = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            Status = "queued",
            StartedAt = DateTime.UtcNow,
        };
        _jobs[job.JobId] = job;

        // migration_jobs DB INSERT — migration_errors.job_id FK 충족.
        const string sql = """
            INSERT INTO migration_jobs
              (job_id, tenant_id, initiated_by, source_folder, status, started_at, client_ip, user_agent, created_at, updated_at)
            VALUES
              (@JobId, @TenantId, @InitiatedBy, @SourceFolder, 'pending', @Now, @ClientIp, @UserAgent, @Now, @Now)
            """;
        try
        {
            await _db.ExecuteAsync(new CommandDefinition(sql, new
            {
                job.JobId, TenantId = tenantId, InitiatedBy = initiatedBy,
                SourceFolder = sourceFolder, Now = job.StartedAt,
                ClientIp = clientIp, UserAgent = userAgent,
            })).ConfigureAwait(false);
        }
        catch
        {
            // DB INSERT 실패 시 in-memory도 제거 — FK 미충족 잡 잔재 차단.
            _jobs.TryRemove(job.JobId, out _);
            throw;
        }
        return job;
    }

    public MigrationJob? Get(string jobId)
        => _jobs.TryGetValue(jobId, out var job) ? job : null;

    public void Update(string jobId, Action<MigrationJob> update)
    {
        if (_jobs.TryGetValue(jobId, out var job))
            update(job);
    }

    /// <summary>잡 상태 DB 동기화 (status enum + 카운터). best-effort, 실패 시 in-memory 유지.</summary>
    public async Task SyncToDbAsync(string jobId, string status, DateTime? finishedAt = null)
    {
        try
        {
            const string sql = """
                UPDATE migration_jobs
                   SET status = @Status,
                       completed_at = CASE WHEN @Status IN ('completed','failed','canceled') THEN @Finished ELSE completed_at END,
                       updated_at = @Now
                 WHERE job_id = @JobId
                """;
            await _db.ExecuteAsync(new CommandDefinition(sql, new
            {
                JobId = jobId, Status = status, Finished = finishedAt, Now = DateTime.UtcNow,
            })).ConfigureAwait(false);
        }
        catch
        {
            // best-effort — 동기화 실패는 본 마이그 흐름 막지 않음.
        }
    }
}

public sealed class MigrationJob
{
    public string JobId { get; set; } = "";
    public string TenantId { get; set; } = "";
    /// <summary>queued | running | completed | failed</summary>
    public string Status { get; set; } = "queued";
    public string CurrentStep { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public MigrationJobResult? Result { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>P0 #6 (2026-05-14): 테이블별 진행 상태 — UI Sticky/카드 가시화용.
    /// key = table_name (snake_case), value = 진행 정보.</summary>
    public ConcurrentDictionary<string, MigrationTableProgress> TableProgress { get; } = new();
}

/// <summary>P0 #6 (2026-05-14): 테이블 단위 진행 상태 노출 DTO.</summary>
public sealed class MigrationTableProgress
{
    /// <summary>pending | running | completed | failed</summary>
    public string Status { get; set; } = "pending";
    public int Rows { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public long ElapsedMs { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class MigrationJobResult
{
    public int Partners { get; set; }
    public int Items { get; set; }
    public int BomHeaders { get; set; }
    public int Employees { get; set; }
    public int SalesOrders { get; set; }
    public int PurchaseOrders { get; set; }
    public int StockLedger { get; set; }
    public int Collections { get; set; }
    public int Cashbook { get; set; }
    public int Expenses { get; set; }
    public int PurchaseOrdersFromIU { get; set; }
    public int SalesOrdersFromIO { get; set; }
    public int TaxInvoices { get; set; }
    public int Bills { get; set; }
    public int CardPayments { get; set; }
    public int BankTransactions { get; set; }
}
