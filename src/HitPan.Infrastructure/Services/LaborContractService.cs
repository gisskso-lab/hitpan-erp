using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.ESign;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>
/// 전자근로계약서 서비스 구현.
/// 워크플로우: draft → sent → signed
/// - 생성: CreateAsync (draft)
/// - 발송: SendAsync (sent, sent_at=NOW)
/// - 서명첨부: AttachSignatureAsync (signed, signed_at=NOW, esign_id 연결)
/// </summary>
public sealed class LaborContractService : ILaborContractService
{
    private readonly IDbConnection _db;
    private readonly IAuditService _audit;

    public LaborContractService(IDbConnection db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<List<LaborContractDto>> GetListAsync(
        string tenantId,
        string? employeeId = null,
        string? status = null,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT
              contract_id       AS ContractId,
              employee_id       AS EmployeeId,
              employee_name     AS EmployeeName,
              contract_type     AS ContractType,
              start_date        AS StartDate,
              end_date          AS EndDate,
              work_place        AS WorkPlace,
              job_description   AS JobDescription,
              working_hours     AS WorkingHours,
              salary_amount     AS SalaryAmount,
              salary_type       AS SalaryType,
              pay_day           AS PayDay,
              social_insurance  AS SocialInsurance,
              annual_leave      AS AnnualLeave,
              extra_terms       AS ExtraTerms,
              status            AS Status,
              sent_at           AS SentAt,
              signed_at         AS SignedAt,
              esign_id          AS EsignId,
              created_at        AS CreatedAt
            FROM labor_contracts
            WHERE tenant_id = @TenantId
              AND (@EmployeeId IS NULL OR employee_id = @EmployeeId)
              AND (@Status IS NULL OR status = @Status)
            ORDER BY created_at DESC
            """;

        var rows = await _db.QueryAsync<LaborContractDto>(new CommandDefinition(
            sql,
            new { TenantId = tenantId, EmployeeId = employeeId, Status = status },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    public async Task<LaborContractDto?> GetAsync(string contractId, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT
              contract_id       AS ContractId,
              employee_id       AS EmployeeId,
              employee_name     AS EmployeeName,
              contract_type     AS ContractType,
              start_date        AS StartDate,
              end_date          AS EndDate,
              work_place        AS WorkPlace,
              job_description   AS JobDescription,
              working_hours     AS WorkingHours,
              salary_amount     AS SalaryAmount,
              salary_type       AS SalaryType,
              pay_day           AS PayDay,
              social_insurance  AS SocialInsurance,
              annual_leave      AS AnnualLeave,
              extra_terms       AS ExtraTerms,
              status            AS Status,
              sent_at           AS SentAt,
              signed_at         AS SignedAt,
              esign_id          AS EsignId,
              created_at        AS CreatedAt
            FROM labor_contracts
            WHERE contract_id = @ContractId AND tenant_id = @TenantId
            """;

        return await _db.QueryFirstOrDefaultAsync<LaborContractDto>(new CommandDefinition(
            sql,
            new { ContractId = contractId, TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<string> CreateAsync(
        CreateLaborContractRequest req,
        string tenantId,
        string userId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.EmployeeId)) throw new InvalidOperationException("직원 ID는 필수입니다.");
        if (string.IsNullOrWhiteSpace(req.EmployeeName)) throw new InvalidOperationException("직원 이름은 필수입니다.");
        if (req.StartDate == default) throw new InvalidOperationException("근로 시작일(start_date)은 필수입니다.");

        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var contractId = Guid.NewGuid().ToString();

        const string sql = """
            INSERT INTO labor_contracts (
              contract_id, tenant_id, employee_id, employee_name,
              contract_type, start_date, end_date,
              work_place, job_description, working_hours,
              salary_amount, salary_type, pay_day,
              social_insurance, annual_leave, extra_terms,
              status, created_at, created_by)
            VALUES (
              @ContractId, @TenantId, @EmployeeId, @EmployeeName,
              @ContractType, @StartDate, @EndDate,
              @WorkPlace, @JobDescription, @WorkingHours,
              @SalaryAmount, @SalaryType, @PayDay,
              @SocialInsurance, @AnnualLeave, @ExtraTerms,
              'draft', NOW(6), @CreatedBy)
            """;

        await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                ContractId = contractId,
                TenantId = tenantId,
                req.EmployeeId,
                req.EmployeeName,
                ContractType = string.IsNullOrWhiteSpace(req.ContractType) ? "regular" : req.ContractType,
                StartDate = req.StartDate.Date,
                EndDate = req.EndDate?.Date,
                req.WorkPlace,
                req.JobDescription,
                req.WorkingHours,
                req.SalaryAmount,
                req.SalaryType,
                req.PayDay,
                req.SocialInsurance,
                req.AnnualLeave,
                req.ExtraTerms,
                CreatedBy = userId
            },
            cancellationToken: ct)).ConfigureAwait(false);

        var afterJson = $"{{\"employee_id\":\"{req.EmployeeId}\",\"contract_type\":\"{req.ContractType}\",\"start_date\":\"{req.StartDate:yyyy-MM-dd}\"}}";
        await _audit.LogAsync("create", "labor_contract", contractId, afterJson: afterJson, ct: ct).ConfigureAwait(false);

        return contractId;
    }

    public async Task SendAsync(string contractId, string tenantId, string userId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // draft 상태에서만 sent 전환 허용 (이미 sent/signed는 재발송 금지)
        const string sql = """
            UPDATE labor_contracts
            SET status = 'sent',
                sent_at = NOW(6)
            WHERE contract_id = @ContractId
              AND tenant_id = @TenantId
              AND status = 'draft'
            """;

        var affected = await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new { ContractId = contractId, TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (affected == 0)
            throw new InvalidOperationException("발송 대상 계약서를 찾지 못했거나 이미 발송/서명된 상태입니다.");

        await _audit.LogAsync(
            "send",
            "labor_contract",
            contractId,
            afterJson: "{\"status\":\"sent\"}",
            ct: ct).ConfigureAwait(false);
    }

    public async Task AttachSignatureAsync(string contractId, string esignId, string tenantId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(esignId))
            throw new InvalidOperationException("서명 기록 ID(esign_id)는 필수입니다.");

        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 서명 기록이 동일 테넌트에 실제 존재하는지 확인 (격리 검증)
        var exists = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            SELECT COUNT(*) FROM esign_records
            WHERE esign_id = @EsignId AND tenant_id = @TenantId AND is_void = 0
            """,
            new { EsignId = esignId, TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (exists == 0)
            throw new InvalidOperationException("유효한 서명 기록을 찾을 수 없습니다.");

        const string sql = """
            UPDATE labor_contracts
            SET status = 'signed',
                signed_at = NOW(6),
                esign_id = @EsignId
            WHERE contract_id = @ContractId
              AND tenant_id = @TenantId
              AND status IN ('draft','sent')
            """;

        var affected = await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new { ContractId = contractId, TenantId = tenantId, EsignId = esignId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (affected == 0)
            throw new InvalidOperationException("서명 첨부 가능한 계약서를 찾지 못했습니다.");

        await _audit.LogAsync(
            "sign",
            "labor_contract",
            contractId,
            afterJson: $"{{\"status\":\"signed\",\"esign_id\":\"{esignId}\"}}",
            ct: ct).ConfigureAwait(false);
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open) return;
        if (_db is DbConnection dbConn)
        {
            await dbConn.OpenAsync(ct).ConfigureAwait(false);
            return;
        }
        _db.Open();
    }
}
