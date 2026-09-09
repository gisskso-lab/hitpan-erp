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
    // CODE-01 즉시 봉합 (2026-05-14 18:50): IDbConnection이 Scoped라서 store도 Scoped 등록 필요.
    // 그러나 jobId 진행 상태는 모든 요청이 공유해야 하므로 dictionary는 static 유지.
    // 검증팀 1차 의견("CODE-01은 다음 사이클")이 옳았으나, WS-10에서 IDbConnection 주입하면서
    // ASP.NET Core DI validation이 즉시 부팅 차단 → 19:00 참관 30분 전 봉합.
    private static readonly ConcurrentDictionary<string, MigrationJob> _jobs = new();
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
        catch (Exception)
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

    /// <summary>
    /// 작22 (2026-09-09) A3: DB 에서 읽어 온 잡을 in-memory 에 되살린다 — API 재시작 뒤 <c>continue</c> 가 같은 jobId 로 이어갈 때.
    /// 이미 메모리에 있으면 그것을 돌려준다(진행 중 상태를 덮지 않는다).
    /// </summary>
    public MigrationJob Restore(MigrationJob job) => _jobs.GetOrAdd(job.JobId, job);

    /// <summary>
    /// 작22 (2026-09-09) A3: 잡 1건을 <b>DB 기준</b>으로 읽는다(<c>_jobs</c> 메모리 아님 — API 재시작 대비).
    /// tenant 대조(헌법 #2)는 부르는 쪽이 한다.
    /// </summary>
    public async Task<MigrationJobDbRow?> GetFromDbAsync(string jobId)
    {
        const string sql = """
            SELECT job_id AS JobId, tenant_id AS TenantId, status AS Status, source_folder AS SourceFolder,
                   started_at AS StartedAt, paused_at AS PausedAt, completed_at AS CompletedAt, created_at AS CreatedAt
              FROM migration_jobs
             WHERE job_id = @JobId
             LIMIT 1
            """;
        return await _db.QueryFirstOrDefaultAsync<MigrationJobDbRow>(new CommandDefinition(sql, new { JobId = jobId })).ConfigureAwait(false);
    }

    /// <summary>
    /// 작22 (2026-09-09) A3: 테넌트의 최신 잡 1건(<c>created_at DESC</c> · idx_tenant_created). 화면 진입 시 재접속·「이어서 가져오기」 판단용.
    /// </summary>
    public async Task<MigrationJobDbRow?> GetLatestByTenantAsync(string tenantId)
    {
        const string sql = """
            SELECT job_id AS JobId, tenant_id AS TenantId, status AS Status, source_folder AS SourceFolder,
                   started_at AS StartedAt, paused_at AS PausedAt, completed_at AS CompletedAt, created_at AS CreatedAt
              FROM migration_jobs
             WHERE tenant_id = @TenantId
             ORDER BY created_at DESC
             LIMIT 1
            """;
        return await _db.QueryFirstOrDefaultAsync<MigrationJobDbRow>(new CommandDefinition(sql, new { TenantId = tenantId })).ConfigureAwait(false);
    }

    /// <summary>
    /// 작22 (2026-09-09) A3: 잡의 표별 체크포인트(<c>migration_checkpoints</c> · DESCRIBE hitpan_e2e 2026-09-09:
    /// status enum(pending,running,done,failed,skipped) · processed_count int unsigned → SIGNED 로 캐스팅해 long 으로 받는다).
    /// </summary>
    public async Task<IReadOnlyList<MigrationCheckpointRow>> GetCheckpointsAsync(string jobId)
    {
        const string sql = """
            SELECT table_name AS TableName, mdb_file AS MdbFile, status AS Status,
                   CAST(processed_count AS SIGNED) AS ProcessedCount,
                   started_at AS StartedAt, completed_at AS CompletedAt, last_error AS LastError
              FROM migration_checkpoints
             WHERE job_id = @JobId
             ORDER BY table_order, table_name
            """;
        var rows = await _db.QueryAsync<MigrationCheckpointRow>(new CommandDefinition(sql, new { JobId = jobId })).ConfigureAwait(false);
        return rows.ToList();
    }

    /// <summary>잡 상태 DB 동기화 (status enum + 카운터). best-effort, 실패 시 in-memory 유지.
    /// 작22 (2026-09-09) A3: <c>paused</c> 는 <c>paused_at</c> 을 찍는다(migration_jobs.status enum 에 paused 실재 · DESCRIBE 2026-09-09).</summary>
    public async Task SyncToDbAsync(string jobId, string status, DateTime? finishedAt = null)
    {
        try
        {
            const string sql = """
                UPDATE migration_jobs
                   SET status = @Status,
                       completed_at = CASE WHEN @Status IN ('completed','failed','canceled') THEN @Finished ELSE completed_at END,
                       paused_at = CASE WHEN @Status = 'paused' THEN @Now ELSE paused_at END,
                       updated_at = @Now
                 WHERE job_id = @JobId
                """;
            await _db.ExecuteAsync(new CommandDefinition(sql, new
            {
                JobId = jobId, Status = status, Finished = finishedAt, Now = DateTime.UtcNow,
            })).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // best-effort — 동기화 실패는 본 마이그 흐름 막지 않음. 헌법 #15 정합 로그.
            Console.Error.WriteLine($"[MigrationJobStore] sync failed (best-effort): {ex.Message}");
        }
    }
}

/// <summary>작22 (2026-09-09) A3: <c>migration_jobs</c> 한 행 — DB 기준 잡 조회 결과(메모리 잡과 별개).</summary>
public sealed class MigrationJobDbRow
{
    public string JobId { get; set; } = "";
    public string TenantId { get; set; } = "";
    /// <summary>pending | preview | running | paused | completed | failed | canceled (DB enum)</summary>
    public string Status { get; set; } = "";
    public string SourceFolder { get; set; } = "";
    public DateTime? StartedAt { get; set; }
    public DateTime? PausedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>작22 (2026-09-09) A3: <c>migration_checkpoints</c> 한 행(표 단위 진행).</summary>
public sealed class MigrationCheckpointRow
{
    public string TableName { get; set; } = "";
    public string MdbFile { get; set; } = "";
    /// <summary>pending | running | done | failed | skipped</summary>
    public string Status { get; set; } = "";
    public long ProcessedCount { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? LastError { get; set; }
}

public sealed class MigrationJob
{
    public string JobId { get; set; } = "";
    public string TenantId { get; set; } = "";
    /// <summary>queued | running | paused | completed | failed — 작22 (2026-09-09): paused = 1단계만 끝남</summary>
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
    /// <summary>작21 (2026-09-04) A9: 지급(payments) 이관 건수. 컨트롤러 매핑(갈래 B)에서 result.Payments 를 채운다.</summary>
    public int Payments { get; set; }
    /// <summary>작22 (2026-09-09) D: 일일보고서(hr_reports, DOCME (사원,날짜) 묶음) 이관 건수. 컨트롤러 매핑에서 result.DailyReports 를 채운다.</summary>
    public int DailyReports { get; set; }
    public int Cashbook { get; set; }
    public int Expenses { get; set; }
    public int PurchaseOrdersFromIU { get; set; }
    public int SalesOrdersFromIO { get; set; }
    public int TaxInvoices { get; set; }
    public int Bills { get; set; }
    public int CardPayments { get; set; }
    public int BankTransactions { get; set; }
}
