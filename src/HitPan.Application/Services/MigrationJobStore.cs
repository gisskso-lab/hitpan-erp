using System.Collections.Concurrent;

namespace HitPan.Application.Services;

/// <summary>
/// 마이그레이션 백그라운드 잡 진행 상태 인메모리 저장소.
/// 2026-05-14 야간: Cloudflare 524(100초 한계) 회피 — POST는 즉시 JobId 반환, 진행률은 GET 폴링.
/// 단일 서버 환경 전제 (베타). 클러스터 환경에서는 Redis로 교체 필요.
/// </summary>
public sealed class MigrationJobStore
{
    private readonly ConcurrentDictionary<string, MigrationJob> _jobs = new();

    public MigrationJob Create(string tenantId)
    {
        var job = new MigrationJob
        {
            JobId = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            Status = "queued",
            StartedAt = DateTime.UtcNow
        };
        _jobs[job.JobId] = job;
        return job;
    }

    public MigrationJob? Get(string jobId)
        => _jobs.TryGetValue(jobId, out var job) ? job : null;

    public void Update(string jobId, Action<MigrationJob> update)
    {
        if (_jobs.TryGetValue(jobId, out var job))
            update(job);
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
