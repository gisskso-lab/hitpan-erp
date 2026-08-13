using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Leave;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>
/// 연차 엔진 구현. 작(2026-08-13) 그룹웨어 단계5.
/// </summary>
/// <remarks>
/// 🔴 이 클래스에 <b>법정 숫자를 쓰지 않는다.</b> 15일·5인·15시간 전부
/// <c>labor_policy_settings</c> 에서 읽는다. 상수로 빼는 것도 금지다 —
/// 상수도 고치려면 재배포가 필요하고, 그러면 법이 바뀔 때마다 전 고객이 멈춘다.
/// (설계도 §0: <i>"지금 연차 15일이 코드에 박혀 있어서 못 바꾸는 것이 바로 그 실패다"</i>)
/// </remarks>
public sealed class AnnualLeaveService : IAnnualLeaveService
{
    private readonly IDbConnection _db;

    public AnnualLeaveService(IDbConnection db)
    {
        _db = db;
    }

    // ───────────────────────────────────────────────────────────────
    // ① 제안
    // ───────────────────────────────────────────────────────────────

    public async Task<List<AnnualLeaveSuggestionDto>> SuggestAsync(string tenantId, int grantYear,
        string? employeeId = null, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 그 해 기준으로 유효한 기준값을 읽는다.
        // 🔴 "지금" 이 아니라 "그 해 12/31" 기준이다 — 2024년 연차를 2026년 기준으로 계산하면 틀린다.
        var asOf = new DateTime(grantYear, 12, 31);
        var policies = await LoadPolicyMapAsync(tenantId, asOf, ct).ConfigureAwait(false);

        var employees = (await _db.QueryAsync<EmployeeRow>(new CommandDefinition(
            """
            SELECT
              e.employee_id  AS EmployeeId,
              e.emp_name     AS EmpName,
              e.join_date    AS JoinDate,
              e.emp_type     AS EmpType,
              e.weekly_hours AS WeeklyHours
            FROM employees e
            WHERE e.tenant_id = @TenantId
              AND e.is_active = 1
              AND (@EmployeeId IS NULL OR e.employee_id = @EmployeeId)
            ORDER BY e.emp_name
            """,
            new { TenantId = tenantId, EmployeeId = employeeId },
            cancellationToken: ct)).ConfigureAwait(false)).ToList();

        // 이미 부여된 건이 있으면 함께 보여준다 — 두 번 주지 않게.
        var existing = (await _db.QueryAsync<ExistingGrantRow>(new CommandDefinition(
            """
            SELECT employee_id AS EmployeeId, grant_type AS GrantType,
                   status AS Status, granted_days AS GrantedDays
            FROM annual_leave_grants
            WHERE tenant_id = @TenantId AND grant_year = @GrantYear
            """,
            new { TenantId = tenantId, GrantYear = grantYear },
            cancellationToken: ct)).ConfigureAwait(false)).ToList();

        // 상시근로자수 — 5인 미만이면 연차 부여 의무가 달라진다.
        // ⚠️ 값이 없으면(미정) 경고만 하고 계산은 그대로 한다 —
        //    모른다고 연차를 0으로 만들면 법정 미달이 된다.
        var workplace = await _db.QueryFirstOrDefaultAsync<WorkplaceRow>(new CommandDefinition(
            "SELECT regular_employee_count AS RegularEmployeeCount FROM local_company WHERE tenant_id = @TenantId",
            new { TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);

        var result = new List<AnnualLeaveSuggestionDto>();

        foreach (var emp in employees)
        {
            var suggestion = Calculate(emp, grantYear, policies, workplace);

            var hit = existing.FirstOrDefault(x =>
                x.EmployeeId == emp.EmployeeId && x.GrantType == suggestion.GrantType);

            if (hit is not null)
            {
                suggestion.ExistingStatus = hit.Status;
                suggestion.ExistingGrantedDays = hit.GrantedDays;
            }

            result.Add(suggestion);
        }

        return result;
    }

    /// <summary>
    /// 한 사람의 연차를 계산한다.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>여기가 자동 계산의 전부</b>이고, 여기서 나온 값은 <b>제안</b>일 뿐이다.
    /// 자동으로 판단할 수 없는 것은 계산을 멈추지 않고 <see cref="AnnualLeaveSuggestionDto.Warnings"/>
    /// 로 넘긴다 — 사람이 보고 정한다.
    /// </remarks>
    private static AnnualLeaveSuggestionDto Calculate(EmployeeRow emp, int grantYear,
        IReadOnlyDictionary<string, decimal> policies, WorkplaceRow? workplace)
    {
        // 그 해 말 기준 근속.
        var asOf = new DateTime(grantYear, 12, 31);
        var joinDate = emp.JoinDate.Date;
        var serviceDays = (asOf - joinDate).TotalDays;

        // 🔴 봉합 (2026-08-13, 20260813검2): 종전 `Math.Round(serviceDays / 365.25, 2)` 는
        //    **아직 1년이 안 된 사람을 1년 이상으로 판정**했다. 실측:
        //
        //      입사 2026-01-01 → 근속 364일 → 364/365.25 = 0.99658 → 반올림 **1.00**
        //      ⇒ 하루가 모자란데 "1년 이상" 갈래로 넘어간다.
        //
        //    숫자가 커지는 것으로 끝나지 않는다 — grant_type='annual' 로 저장되면
        //    uk_grant_emp_year_type(UNIQUE) 때문에 **담당자가 나중에 월차로 바꾸려 해도 막힌다.**
        //    수동으로 정할 자유를 코드가 뺏는 셈이라 반자동 원칙에 어긋난다.
        //
        // ⇒ 반올림하지 않고 **실제로 지난 햇수**를 센다(생일 세는 법과 같다).
        var completedYears = CompletedYears(joinDate, asOf);

        // 화면·근거문에는 소수까지 보여준다(사람이 "몇 년 몇 개월" 을 가늠해야 하므로).
        // 🔴 다만 **판정에는 쓰지 않는다** — 판정은 completedYears 로만 한다.
        var serviceYears = serviceDays < 0
            ? 0m
            : (decimal)Math.Floor(serviceDays / 365.25 * 100) / 100m;   // 내림 — 올려서 넘기지 않는다

        var dto = new AnnualLeaveSuggestionDto
        {
            EmployeeId = emp.EmployeeId,
            EmployeeName = emp.EmpName,
            GrantYear = grantYear,
            ServiceYears = serviceYears < 0 ? 0 : serviceYears
        };

        // ── 아직 입사 전 ──
        if (serviceDays < 0)
        {
            dto.SuggestedDays = 0;
            dto.GrantType = "annual";
            dto.CalcBasis = $"{grantYear}년 말 기준 아직 입사 전입니다(입사일 {joinDate:yyyy-MM-dd}).";
            return dto;
        }

        var baseDays = Policy(policies, "annual_leave_base_days");
        var monthlyDays = Policy(policies, "monthly_leave_days_under_1y");
        var extraPer = Policy(policies, "annual_leave_extra_per_years");
        var extraCycle = Policy(policies, "annual_leave_extra_cycle_years");
        var extraStart = Policy(policies, "annual_leave_extra_start_years");
        var maxDays = Policy(policies, "annual_leave_max_days");
        var shortTimeHours = Policy(policies, "short_time_weekly_hours");
        var smallThreshold = Policy(policies, "small_business_threshold");

        // ── 1년 미만: 한 달 개근에 하루 ──
        // ⚠️ 실제 개근 여부는 근태를 봐야 안다. 여기서는 '근무 개월 수' 로 최대치를 제안하고,
        //    결근이 있으면 사람이 줄인다(반자동).
        if (completedYears < 1)
        {
            var months = (int)Math.Floor(serviceDays / 30.4375);
            if (months < 0) months = 0;

            dto.GrantType = "monthly";
            dto.SuggestedDays = months * monthlyDays;
            dto.CalcBasis =
                $"1년 미만 근속({serviceYears}년). 근무 {months}개월 × {monthlyDays}일 = {dto.SuggestedDays}일. "
                + "개근한 달만 해당하므로 결근이 있으면 줄여야 합니다.";
            dto.Warnings.Add("한 달 개근 여부는 자동으로 판단하지 않습니다. 근태를 확인하고 확정하세요.");
        }
        else
        {
            // ── 1년 이상: 기본 + 가산 ──
            var extra = 0m;
            if (completedYears >= extraStart && extraCycle > 0)
            {
                // 🔴 봉합 (2026-08-13, 20260813검2): 종전에 `+ 1` 이 붙어 있어
                //    **설정표대로 안 나왔다.** 실측:
                //      가산시작 3년·주기 2년으로 설정했는데 근속 3년에 이미 +1일,
                //      5년에 +2일 — 시작하자마자 한 번 더해지고 있었다.
                //
                //    설정이 "3년부터 2년마다" 면 3년째는 기본값 그대로이고
                //    첫 가산은 그 다음 주기에 붙는 것이 설정을 읽는 상식이다.
                //
                // ⇒ `+1` 을 걷어낸다. 🔴 <b>기준값은 전부 설정표에서 온다</b> —
                //    여기에 법정 숫자를 적지 않는다(사장님: "법적검토를 따지지마",
                //    "고객사가 입력하는 값만 받으면 되"). 값이 다르면 설정에서 바꾼다.
                var cycles = Math.Floor((completedYears - extraStart) / extraCycle);
                if (cycles < 0) cycles = 0;
                extra = cycles * extraPer;
            }

            var total = baseDays + extra;
            var capped = maxDays > 0 && total > maxDays;
            if (capped) total = maxDays;

            dto.GrantType = "annual";
            dto.SuggestedDays = total;
            dto.CalcBasis =
                $"근속 {serviceYears}년. 기본 {baseDays}일"
                + (extra > 0 ? $" + 가산 {extra}일(근속 {extraStart}년부터 {extraCycle}년마다 {extraPer}일)" : "")
                + (capped ? $" → 상한 {maxDays}일 적용" : "")
                + $" = {total}일";
        }

        // ── 사람이 봐야 할 사정들 ──
        // 🔴 계산을 멈추지 않는다. 멈추면 아무 값도 안 나와서 오히려 못 준다.

        if (emp.WeeklyHours is null)
        {
            dto.Warnings.Add(
                $"주당 소정근로시간이 정해지지 않았습니다. 주 {shortTimeHours}시간 미만이면 연차가 달라집니다.");
        }
        else if (emp.WeeklyHours < shortTimeHours)
        {
            dto.Warnings.Add(
                $"주 {emp.WeeklyHours}시간으로 단시간 근로({shortTimeHours}시간 미만)입니다. "
                + "연차·주휴가 통상 근로자와 다르므로 확인이 필요합니다.");
        }

        if (workplace?.RegularEmployeeCount is null)
        {
            dto.Warnings.Add("상시근로자수가 정해지지 않았습니다. 회사 정보에서 먼저 확정하세요.");
        }
        else if (workplace.RegularEmployeeCount < smallThreshold)
        {
            dto.Warnings.Add(
                $"상시근로자 {workplace.RegularEmployeeCount}명으로 {smallThreshold}인 미만 사업장입니다. "
                + "연차 부여 의무가 달라지므로 확인이 필요합니다.");
        }

        // 고용형태에 따라 달라지는 자리 — 판정은 사람이 한다.
        if (emp.EmpType is "part" or "daily")
        {
            dto.Warnings.Add("단시간·일용 근로자는 근로 형태에 따라 연차 산정이 달라집니다.");
        }

        return dto;
    }

    // ───────────────────────────────────────────────────────────────
    // ②③ 수정 + 확정
    // ───────────────────────────────────────────────────────────────

    public async Task<string> ConfirmAsync(string tenantId, string confirmedBy,
        ConfirmAnnualLeaveRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        if (request.GrantedDays < 0)
        {
            throw new InvalidOperationException("연차 일수는 0보다 작을 수 없습니다.");
        }

        // 🔴 제안과 다르게 정했으면 이유를 받는다.
        //    이력이 없으면 "내 연차가 왜 이거냐" 에 답할 수 없다(사장님 반자동 원칙).
        var isAdjusted = request.GrantedDays != request.SuggestedDays;
        if (isAdjusted && string.IsNullOrWhiteSpace(request.AdjustReason))
        {
            throw new InvalidOperationException(
                "자동 계산과 다르게 정했습니다. 사유를 남겨야 확정할 수 있습니다.");
        }

        var empExists = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM employees WHERE tenant_id=@TenantId AND employee_id=@EmpId AND is_active=1",
            new { TenantId = tenantId, EmpId = request.EmployeeId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (empExists == 0)
        {
            throw new InvalidOperationException("연차를 부여할 수 없는 사원입니다(미존재 또는 퇴직).");
        }

        var grantId = Guid.NewGuid().ToString();

        // 🔴 부여 이력과 잔여 반영을 ★같은 트랜잭션★ 으로 묶는다.
        //    따로 하면 이력만 남고 잔여가 안 늘거나(직원이 못 씀), 그 반대가 된다.
        using var tx = _db.BeginTransaction();
        try
        {
            // 같은 해·같은 종류가 이미 확정돼 있으면 다시 주지 않는다(이중 부여 차단).
            var already = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(*) FROM annual_leave_grants
                WHERE tenant_id = @TenantId AND employee_id = @EmpId
                  AND grant_year = @Year AND grant_type = @Type
                  AND status = 'confirmed'
                """,
                new
                {
                    TenantId = tenantId, EmpId = request.EmployeeId,
                    Year = request.GrantYear, Type = request.GrantType
                },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            if (already > 0)
            {
                throw new InvalidOperationException(
                    $"{request.GrantYear}년 연차가 이미 확정돼 있습니다. 바꾸려면 조정 부여를 쓰세요.");
            }

            // 🔴 봉합 (2026-08-13, 20260813검2): UNIQUE(uk_grant_emp_year_type)는
            //    **상태를 안 본다.** 위 검사는 'confirmed' 만 세므로, 취소된 줄이 남아 있으면
            //    검사는 통과하고 INSERT 에서 1062 로 터진다 —
            //    컨트롤러는 InvalidOperationException 만 잡으므로 담당자는 **까닭 없는 500** 을 본다.
            //
            //    담당자가 취소한 뒤 다시 주는 것은 **정상 업무**다(사장님: 수동으로 간다).
            //    ⇒ 취소된 줄은 자리를 비켜 준다. 지우지 않고 종류에 표시를 남겨
            //      이력은 그대로 보존한다(헌법 #3 정신 — 있었던 일은 남는다).
            await _db.ExecuteAsync(new CommandDefinition(
                """
                UPDATE annual_leave_grants
                SET grant_type = CONCAT(grant_type, '-x', LEFT(REPLACE(UUID(), '-', ''), 6)),
                    updated_at = NOW(6)
                WHERE tenant_id = @TenantId AND employee_id = @EmpId
                  AND grant_year = @Year AND grant_type = @Type
                  AND status = 'cancelled'
                """,
                new
                {
                    TenantId = tenantId, EmpId = request.EmployeeId,
                    Year = request.GrantYear, Type = request.GrantType
                },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            await _db.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO annual_leave_grants
                  (grant_id, tenant_id, employee_id, grant_year, period_start, period_end,
                   suggested_days, granted_days, is_adjusted, adjust_reason,
                   grant_type, service_years, calc_basis,
                   status, confirmed_by, confirmed_at, created_by)
                VALUES
                  (@GrantId, @TenantId, @EmpId, @Year, @PeriodStart, @PeriodEnd,
                   @Suggested, @Granted, @IsAdjusted, @AdjustReason,
                   @Type, @ServiceYears, @CalcBasis,
                   'confirmed', @ConfirmedBy, NOW(6), @ConfirmedBy)
                """,
                new
                {
                    GrantId = grantId,
                    TenantId = tenantId,
                    EmpId = request.EmployeeId,
                    Year = request.GrantYear,
                    PeriodStart = request.PeriodStart == default
                        ? new DateTime(request.GrantYear, 1, 1) : request.PeriodStart.Date,
                    PeriodEnd = request.PeriodEnd == default
                        ? new DateTime(request.GrantYear, 12, 31) : request.PeriodEnd.Date,
                    Suggested = request.SuggestedDays,
                    Granted = request.GrantedDays,
                    IsAdjusted = isAdjusted ? 1 : 0,
                    AdjustReason = request.AdjustReason,
                    Type = request.GrantType,
                    ServiceYears = request.ServiceYears,
                    CalcBasis = request.CalcBasis,
                    ConfirmedBy = confirmedBy
                },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            // 🔴 잔여 반영 — employees 의 두 칸은 기존 화면·쿼리가 보는 '합계 캐시' 다.
            //    끊으면 연차 신청 화면이 잔여를 못 읽어 워크플로우가 깨진다(헌법 #20).
            //    ⚠️ 덮어쓰지 않고 더한다. 조정·이월 부여가 따로 들어올 수 있다.
            await _db.ExecuteAsync(new CommandDefinition(
                """
                UPDATE employees
                SET annual_leave_total = annual_leave_total + @Days,
                    updated_at = NOW(6)
                WHERE tenant_id = @TenantId AND employee_id = @EmpId
                """,
                new { Days = request.GrantedDays, TenantId = tenantId, EmpId = request.EmployeeId },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            tx.Commit();
            return grantId;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ───────────────────────────────────────────────────────────────
    // 이력 · 기준값
    // ───────────────────────────────────────────────────────────────

    public async Task<List<AnnualLeaveGrantDto>> GetGrantsAsync(string tenantId, int? grantYear,
        string? employeeId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var rows = await _db.QueryAsync<AnnualLeaveGrantDto>(new CommandDefinition(
            """
            SELECT
              g.grant_id      AS GrantId,
              g.employee_id   AS EmployeeId,
              e.emp_name      AS EmployeeName,
              g.grant_year    AS GrantYear,
              g.period_start  AS PeriodStart,
              g.period_end    AS PeriodEnd,
              g.suggested_days AS SuggestedDays,
              g.granted_days  AS GrantedDays,
              g.is_adjusted   AS IsAdjusted,
              g.adjust_reason AS AdjustReason,
              g.grant_type    AS GrantType,
              g.service_years AS ServiceYears,
              g.calc_basis    AS CalcBasis,
              g.status        AS Status,
              g.confirmed_by  AS ConfirmedBy,
              g.confirmed_at  AS ConfirmedAt,
              g.created_at    AS CreatedAt
            FROM annual_leave_grants g
            LEFT JOIN employees e
              ON e.employee_id = g.employee_id AND e.tenant_id = g.tenant_id
            WHERE g.tenant_id = @TenantId
              AND (@GrantYear IS NULL OR g.grant_year = @GrantYear)
              AND (@EmployeeId IS NULL OR g.employee_id = @EmployeeId)
            ORDER BY g.grant_year DESC, e.emp_name, g.created_at DESC
            """,
            new { TenantId = tenantId, GrantYear = grantYear, EmployeeId = employeeId },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    public async Task<List<LaborPolicyDto>> GetPoliciesAsync(string tenantId, DateTime? asOf = null,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var at = (asOf ?? DateTime.Today).Date;

        // 그 시점에 유효한 값만. 같은 열쇠가 여러 시행일로 있으면 가장 최근 것.
        var rows = await _db.QueryAsync<LaborPolicyDto>(new CommandDefinition(
            """
            SELECT
              p.policy_id      AS PolicyId,
              p.policy_key     AS PolicyKey,
              p.policy_value   AS PolicyValue,
              p.value_unit     AS ValueUnit,
              p.effective_from AS EffectiveFrom,
              p.effective_to   AS EffectiveTo,
              p.label          AS Label,
              p.description    AS Description,
              p.is_statutory   AS IsStatutory,
              p.updated_reason AS UpdatedReason,
              p.updated_at     AS UpdatedAt
            FROM labor_policy_settings p
            WHERE p.tenant_id = @TenantId
              AND p.effective_from <= @AsOf
              AND (p.effective_to IS NULL OR p.effective_to >= @AsOf)
              AND p.effective_from = (
                    SELECT MAX(q.effective_from) FROM labor_policy_settings q
                    WHERE q.tenant_id = p.tenant_id
                      AND q.policy_key = p.policy_key
                      AND q.effective_from <= @AsOf
              )
            ORDER BY p.policy_key
            """,
            new { TenantId = tenantId, AsOf = at },
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.ToList();
    }

    public async Task<string> SavePolicyAsync(string tenantId, string updatedBy,
        SaveLaborPolicyRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(request.PolicyKey))
        {
            throw new InvalidOperationException("기준값 항목을 선택해 주세요.");
        }

        // 원본 행에서 이름·단위·법정 여부를 가져온다(관리자가 그것까지 정하지 않는다).
        var seed = await _db.QueryFirstOrDefaultAsync<PolicySeedRow>(new CommandDefinition(
            """
            SELECT label AS Label, value_unit AS ValueUnit, description AS Description,
                   is_statutory AS IsStatutory
            FROM labor_policy_settings
            WHERE tenant_id = @TenantId AND policy_key = @Key
            ORDER BY effective_from DESC
            LIMIT 1
            """,
            new { TenantId = tenantId, Key = request.PolicyKey },
            cancellationToken: ct)).ConfigureAwait(false);

        if (seed is null)
        {
            throw new InvalidOperationException("알 수 없는 기준값 항목입니다.");
        }

        var effectiveFrom = request.EffectiveFrom == default
            ? DateTime.Today
            : request.EffectiveFrom.Date;

        // 🔴 같은 시행일이면 덮고, 다르면 새 행을 넣는다.
        //    다른 시행일의 옛 행은 그대로 둔다 — 과거 계산이 그 값을 봐야 한다.
        var policyId = Guid.NewGuid().ToString();

        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO labor_policy_settings
              (policy_id, tenant_id, policy_key, policy_value, value_unit, effective_from,
               label, description, is_statutory, updated_by, updated_reason)
            VALUES
              (@PolicyId, @TenantId, @Key, @Value, @Unit, @From,
               @Label, @Description, @IsStatutory, @UpdatedBy, @Reason)
            ON DUPLICATE KEY UPDATE
              policy_value   = @Value,
              updated_by     = @UpdatedBy,
              updated_reason = @Reason,
              updated_at     = NOW(6)
            """,
            new
            {
                PolicyId = policyId,
                TenantId = tenantId,
                Key = request.PolicyKey,
                Value = request.PolicyValue,
                Unit = seed.ValueUnit,
                From = effectiveFrom,
                seed.Label,
                seed.Description,
                IsStatutory = seed.IsStatutory ? 1 : 0,
                UpdatedBy = updatedBy,
                Reason = request.UpdatedReason
            },
            cancellationToken: ct)).ConfigureAwait(false);

        return policyId;
    }

    // ───────────────────────────────────────────────────────────────
    // 헬퍼
    // ───────────────────────────────────────────────────────────────

    /// <summary>그 시점에 유효한 기준값을 열쇠→값 으로 읽는다.</summary>
    private async Task<Dictionary<string, decimal>> LoadPolicyMapAsync(string tenantId, DateTime asOf,
        CancellationToken ct)
    {
        var list = await GetPoliciesAsync(tenantId, asOf, ct).ConfigureAwait(false);
        return list.ToDictionary(p => p.PolicyKey, p => p.PolicyValue, StringComparer.Ordinal);
    }

    /// <summary>
    /// 기준값을 꺼낸다. <b>없으면 0</b> — 임의의 숫자를 여기 쓰지 않는다.
    /// </summary>
    /// <remarks>
    /// 🔴 여기에 <c>?? 15m</c> 같은 기본값을 넣으면 그게 곧 하드코딩이다.
    /// 기준값이 없으면 0 이 나오고, 그러면 화면에서 "기준값이 없다" 가 드러난다.
    /// 조용히 그럴듯한 값이 나오는 것보다 <b>안 나오는 게 낫다</b>.
    /// </remarks>
    /// <summary>
    /// 입사일부터 기준일까지 <b>실제로 지난 햇수</b>. 작(2026-08-13 봉합, 20260813검2).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>나이를 세는 방법과 같다</b> — 생일이 지나야 한 살을 먹는다.
    /// 365.25 로 나눠 반올림하면 <b>364일 근무자가 1년으로 판정</b>되어
    /// 담당자가 보기에 앞뒤가 안 맞는 제안이 나온다. 게다가 <c>grant_type</c> 이
    /// 그렇게 저장되면 UNIQUE 때문에 <b>담당자가 나중에 바꾸지도 못한다.</b>
    /// <para>
    /// ⚠️ 이 함수는 <b>제안값을 만들 뿐</b>이다. 최종 일수는 담당자가 정한다
    /// (사장님: <i>"고객사 내부사정에 따라 판단할수 없거나 기준을 잡을수 없는건
    /// 담당자가 직접 부여하는 수동방식"</i>).
    /// </para>
    /// <para>
    /// 윤년도 자동으로 맞는다 — 날짜를 더해 비교하므로 2/29 문제를 따로 다루지 않아도 된다.
    /// </para>
    /// </remarks>
    private static decimal CompletedYears(DateTime joinDate, DateTime asOf)
    {
        if (asOf < joinDate) return 0m;

        var years = asOf.Year - joinDate.Year;

        // 아직 입사 기념일이 안 지났으면 한 해를 뺀다.
        if (asOf < joinDate.AddYears(years)) years--;

        return years < 0 ? 0m : years;
    }

    private static decimal Policy(IReadOnlyDictionary<string, decimal> policies, string key)
        => policies.TryGetValue(key, out var v) ? v : 0m;

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open) return;
        if (_db is DbConnection dbConnection)
        {
            await dbConnection.OpenAsync(ct).ConfigureAwait(false);
            return;
        }
        _db.Open();
    }

    private sealed class EmployeeRow
    {
        public string EmployeeId { get; set; } = string.Empty;
        public string EmpName { get; set; } = string.Empty;
        public DateTime JoinDate { get; set; }
        public string? EmpType { get; set; }
        public decimal? WeeklyHours { get; set; }
    }

    private sealed class ExistingGrantRow
    {
        public string EmployeeId { get; set; } = string.Empty;
        public string GrantType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public decimal GrantedDays { get; set; }
    }

    private sealed class WorkplaceRow
    {
        public int? RegularEmployeeCount { get; set; }
    }

    private sealed class PolicySeedRow
    {
        public string Label { get; set; } = string.Empty;
        public string ValueUnit { get; set; } = "day";
        public string? Description { get; set; }
        public bool IsStatutory { get; set; }
    }
}
