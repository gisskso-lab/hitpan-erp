using System.Text;
using Dapper;
using HitPan.Application.DTOs.Backoffice;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

public class ResellerService : IResellerService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordEncryptor _encryption;
    private readonly ILogger<ResellerService> _logger;

    public ResellerService(
        IUnitOfWork unitOfWork,
        IPasswordEncryptor encryption,
        ILogger<ResellerService> logger)
    {
        _unitOfWork = unitOfWork;
        _encryption = encryption;
        _logger = logger;
    }

    // ── 본사 대시보드 ────────────────────────────────────────────────────────

    public async Task<AdminDashboardResponse> GetAdminDashboardAsync(CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();
        var now = DateTime.UtcNow;
        var thisMonth = now.ToString("yyyy-MM");
        var monthStart = new DateTime(now.Year, now.Month, 1);

        var totalCustomers = await db.QueryFirstOrDefaultAsync<int>(
            "SELECT COUNT(*) FROM tenants WHERE status NOT IN ('expired')", null);

        var newThisMonth = await db.QueryFirstOrDefaultAsync<int>(
            "SELECT COUNT(*) FROM tenants WHERE created_at >= @Start", new { Start = monthStart });

        var monthlyBilling = await db.QueryFirstOrDefaultAsync<decimal>(
            "SELECT COALESCE(SUM(total_amount), 0) FROM billing_invoices WHERE billing_month = @Month AND status = 'paid'",
            new { Month = thisMonth });

        var unpaid = await db.QueryFirstOrDefaultAsync<int>(
            "SELECT COUNT(*) FROM billing_invoices WHERE billing_month = @Month AND status IN ('pending','failed')",
            new { Month = thisMonth });

        var activeResellers = await db.QueryFirstOrDefaultAsync<int>(
            "SELECT COUNT(*) FROM resellers WHERE status = 'active'", null);

        var pendingCommission = await db.QueryFirstOrDefaultAsync<decimal>(
            "SELECT COALESCE(SUM(payment_amount), 0) FROM commission_settlements WHERE status IN ('draft','approved')", null);

        var trend = (await db.QueryAsync<dynamic>(
            @"SELECT DATE_FORMAT(created_at, '%Y-%m') AS month, COUNT(*) AS new_count
              FROM tenants
              WHERE created_at >= DATE_SUB(@Now, INTERVAL 12 MONTH)
              GROUP BY month ORDER BY month",
            new { Now = now })).Select(r => new MonthlyCustomerTrend
        {
            Month = (string)r.month,
            NewCount = (int)r.new_count
        }).ToList();

        var topResellers = (await db.QueryAsync<dynamic>(
            @"SELECT r.reseller_name,
                     COUNT(t.tenant_id) AS customer_count,
                     COALESCE(SUM(bi.total_amount), 0) AS monthly_revenue
              FROM resellers r
              LEFT JOIN tenants t ON t.reseller_id = r.reseller_id
              LEFT JOIN billing_invoices bi ON bi.tenant_id = t.tenant_id AND bi.billing_month = @Month AND bi.status = 'paid'
              WHERE r.status = 'active'
              GROUP BY r.reseller_id, r.reseller_name
              ORDER BY monthly_revenue DESC
              LIMIT 5",
            new { Month = thisMonth })).Select(r => new ResellerPerformance
        {
            ResellerName = (string)r.reseller_name,
            CustomerCount = (int)r.customer_count,
            MonthlyRevenue = (decimal)r.monthly_revenue,
            MonthlyCommission = 0
        }).ToList();

        return new AdminDashboardResponse
        {
            TotalCustomers = totalCustomers,
            NewCustomersThisMonth = newThisMonth,
            MonthlyBillingTotal = monthlyBilling,
            UnpaidCustomers = unpaid,
            ActiveResellers = activeResellers,
            PendingCommissionTotal = pendingCommission,
            MonthlyCustomerTrend = trend,
            TopResellerPerformances = topResellers
        };
    }

    // ── 본사 고객사 관리 ─────────────────────────────────────────────────────

    public async Task<AdminTenantListResponse> GetAdminTenantsAsync(
        string? status, string? resellerId, string? planType, string? search,
        int page, int size, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();
        var offset = (page - 1) * size;

        var where = new List<string>();
        var param = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(status)) { where.Add("t.status = @Status"); param.Add("Status", status); }
        if (!string.IsNullOrWhiteSpace(resellerId)) { where.Add("t.reseller_id = @ResellerId"); param.Add("ResellerId", resellerId); }
        if (!string.IsNullOrWhiteSpace(search))
        {
            where.Add("(t.company_name LIKE @Search OR t.biz_no LIKE @Search)");
            param.Add("Search", $"%{search}%");
        }
        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        param.Add("Size", size);
        param.Add("Offset", offset);

        var countSql = $"SELECT COUNT(*) FROM tenants t {whereClause}";
        var totalCount = await db.QueryFirstOrDefaultAsync<int>(countSql, param);

        var sql = $@"
            SELECT t.tenant_id, t.tenant_code, t.company_name, t.biz_no,
                   r.reseller_name, t.status, t.trial_ends_at, t.created_at,
                   s.plan_type, s.next_billing_at,
                   (SELECT COUNT(*) FROM users u WHERE u.tenant_id = t.tenant_id AND u.is_active = 1) AS user_count
            FROM tenants t
            LEFT JOIN resellers r ON r.reseller_id = t.reseller_id
            LEFT JOIN billing_subscriptions s ON s.tenant_id = t.tenant_id AND s.status = 'active'
            {whereClause}
            ORDER BY t.created_at DESC
            LIMIT @Size OFFSET @Offset";

        var rows = await db.QueryAsync<dynamic>(sql, param);
        var items = rows.Select(r => new AdminTenantListItem
        {
            TenantId = (string)r.tenant_id,
            TenantCode = (string)r.tenant_code,
            CompanyName = (string)r.company_name,
            BizNo = (string)r.biz_no,
            ResellerName = r.reseller_name as string,
            Status = (string)r.status,
            PlanType = r.plan_type as string,
            UserCount = (int)(r.user_count ?? 0),
            TrialEndsAt = r.trial_ends_at as DateTime?,
            NextBillingAt = r.next_billing_at as DateTime?,
            CreatedAt = (DateTime)r.created_at
        }).ToList();

        return new AdminTenantListResponse
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            Size = size,
            TotalPages = (int)Math.Ceiling((double)totalCount / size)
        };
    }

    public async Task<AdminTenantDetail> GetAdminTenantAsync(string tenantId, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();

        var tenant = await db.QueryFirstOrDefaultAsync<dynamic>(
            @"SELECT t.*, r.reseller_name
              FROM tenants t LEFT JOIN resellers r ON r.reseller_id = t.reseller_id
              WHERE t.tenant_id = @TenantId",
            new { TenantId = tenantId })
            ?? throw new InvalidOperationException("고객사를 찾을 수 없습니다");

        var sub = await db.QueryFirstOrDefaultAsync<dynamic>(
            @"SELECT plan_type, base_users, extra_users, base_fee, extra_fee_per_user,
                     billing_cycle, started_at, next_billing_at, status
              FROM billing_subscriptions WHERE tenant_id = @TenantId AND status = 'active' LIMIT 1",
            new { TenantId = tenantId });

        var invoices = (await db.QueryAsync<dynamic>(
            @"SELECT invoice_id, billing_month, total_amount, status, paid_at
              FROM billing_invoices WHERE tenant_id = @TenantId
              ORDER BY billing_month DESC LIMIT 12",
            new { TenantId = tenantId })).Select(r => new AdminTenantInvoice
        {
            InvoiceId = (string)r.invoice_id,
            BillingMonth = (string)r.billing_month,
            TotalAmount = (int)r.total_amount,
            Status = (string)r.status,
            PaidAt = r.paid_at as DateTime?
        }).ToList();

        return new AdminTenantDetail
        {
            TenantId = (string)tenant.tenant_id,
            TenantCode = (string)tenant.tenant_code,
            CompanyName = (string)tenant.company_name,
            BizNo = (string)tenant.biz_no,
            CeoName = (string)tenant.ceo_name,
            Tel = tenant.tel as string,
            Address = tenant.address as string,
            ResellerId = tenant.reseller_id as string,
            ResellerName = tenant.reseller_name as string,
            Status = (string)tenant.status,
            TrialEndsAt = tenant.trial_ends_at as DateTime?,
            CreatedAt = (DateTime)tenant.created_at,
            Subscription = sub is null ? null : new AdminTenantSubscription
            {
                PlanType = sub.plan_type as string,
                BaseUsers = (int)sub.base_users,
                ExtraUsers = (int)sub.extra_users,
                BaseFee = (int)sub.base_fee,
                ExtraFeePerUser = (int)sub.extra_fee_per_user,
                BillingCycle = sub.billing_cycle as string,
                StartedAt = sub.started_at as DateTime?,
                NextBillingAt = sub.next_billing_at as DateTime?,
                Status = sub.status as string
            },
            Invoices = invoices
        };
    }

    public async Task UpdateTenantStatusAsync(string tenantId, UpdateTenantStatusRequest request, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();
        var affected = await db.ExecuteAsync(
            "UPDATE tenants SET status = @Status, updated_at = @Now WHERE tenant_id = @TenantId",
            new { Status = request.NewStatus, Now = DateTime.UtcNow, TenantId = tenantId });

        if (affected == 0)
            throw new InvalidOperationException("고객사를 찾을 수 없습니다");
    }

    public async Task<AdminCreateTenantResponse> AdminCreateTenantAsync(
        AdminCreateTenantRequest request, string adminId, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();

        var tenantId = Guid.NewGuid().ToString();
        var codeSeq = await db.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) + 1 FROM tenants");
        var tenantCode = $"T-{codeSeq:D3}";

        var tempPassword = GenerateTempPassword();
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword);

        using var tx = db.BeginTransaction();
        try
        {
            await db.ExecuteAsync(
                @"INSERT INTO tenants
                    (tenant_id, tenant_code, company_name, biz_no, ceo_name, tel, address,
                     reseller_id, status, trial_ends_at, db_host, db_name, license_key_hash,
                     reseller_tier, created_at, updated_at)
                  VALUES
                    (@TenantId, @TenantCode, @CompanyName, @BizNo, @CeoName, @Tel, @Address,
                     @ResellerId, @Status, @TrialEndsAt, '', '', '', 0, @Now, @Now)",
                new
                {
                    TenantId = tenantId,
                    TenantCode = tenantCode,
                    request.CompanyName,
                    request.BizNo,
                    request.CeoName,
                    request.Tel,
                    request.Address,
                    request.ResellerId,
                    Status = request.StartType == "trial" ? "trial" : "active",
                    TrialEndsAt = request.StartType == "trial"
                        ? (DateTime?)DateTime.UtcNow.AddDays(30) : null,
                    Now = DateTime.UtcNow
                }, tx);

            var planFees = GetPlanFees(request.PlanType);
            await db.ExecuteAsync(
                @"INSERT INTO billing_subscriptions
                    (subscription_id, tenant_id, plan_type, base_users, extra_users,
                     base_fee, extra_fee_per_user, billing_cycle,
                     started_at, next_billing_at, status, created_at, updated_at)
                  VALUES
                    (@Id, @TenantId, @PlanType, @BaseUsers, @ExtraUsers,
                     @BaseFee, @ExtraFeePerUser, @BillingCycle,
                     @StartedAt, @NextBillingAt, 'active', @Now, @Now)",
                new
                {
                    Id = Guid.NewGuid().ToString(),
                    TenantId = tenantId,
                    request.PlanType,
                    BaseUsers = planFees.baseUsers,
                    request.ExtraUsers,
                    BaseFee = planFees.baseFee,
                    ExtraFeePerUser = planFees.extraFeePerUser,
                    request.BillingCycle,
                    StartedAt = DateTime.UtcNow,
                    NextBillingAt = DateTime.UtcNow.AddMonths(1),
                    Now = DateTime.UtcNow
                }, tx);

            var userId = Guid.NewGuid().ToString();
            await db.ExecuteAsync(
                @"INSERT INTO users
                    (user_id, tenant_id, email, password_hash, user_name, role,
                     is_active, created_at, updated_at)
                  VALUES
                    (@UserId, @TenantId, @Email, @PasswordHash, '관리자',
                     'tenant_admin', 1, @Now, @Now)",
                new
                {
                    UserId = userId,
                    TenantId = tenantId,
                    Email = request.AdminEmail,
                    PasswordHash = passwordHash,
                    Now = DateTime.UtcNow
                }, tx);

            tx.Commit();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Backoffice] 고객사 등록 실패 — {CompanyName}", request.CompanyName);
            tx.Rollback();
            throw;
        }

        return new AdminCreateTenantResponse
        {
            TenantId = tenantId,
            TenantCode = tenantCode,
            TempPassword = tempPassword
        };
    }

    // ── 본사 대리점 관리 ─────────────────────────────────────────────────────

    public async Task<ResellerListResponse> GetResellersAsync(
        string? status, string? search, int page, int size, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();
        var offset = (page - 1) * size;
        var thisMonth = DateTime.UtcNow.ToString("yyyy-MM");

        var where = new List<string>();
        var param = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(status)) { where.Add("r.status = @Status"); param.Add("Status", status); }
        if (!string.IsNullOrWhiteSpace(search)) { where.Add("r.reseller_name LIKE @Search"); param.Add("Search", $"%{search}%"); }
        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        param.Add("Month", thisMonth);
        param.Add("Size", size);
        param.Add("Offset", offset);

        var totalCount = await db.QueryFirstOrDefaultAsync<int>(
            $"SELECT COUNT(*) FROM resellers r {whereClause}", param);

        var sql = $@"
            SELECT r.reseller_id, r.reseller_code, r.reseller_name, r.status, r.join_date,
                   COUNT(DISTINCT t.tenant_id) AS customer_count,
                   COALESCE(SUM(bi.total_amount), 0) AS monthly_revenue
            FROM resellers r
            LEFT JOIN tenants t ON t.reseller_id = r.reseller_id
            LEFT JOIN billing_invoices bi ON bi.tenant_id = t.tenant_id
                AND bi.billing_month = @Month AND bi.status = 'paid'
            {whereClause}
            GROUP BY r.reseller_id, r.reseller_code, r.reseller_name, r.status, r.join_date
            ORDER BY r.join_date DESC
            LIMIT @Size OFFSET @Offset";

        var rows = await db.QueryAsync<dynamic>(sql, param);
        var items = rows.Select(r => new ResellerListItem
        {
            ResellerId = (string)r.reseller_id,
            ResellerCode = (string)r.reseller_code,
            ResellerName = (string)r.reseller_name,
            Status = (string)r.status,
            CustomerCount = (int)r.customer_count,
            MonthlyRevenue = (decimal)r.monthly_revenue,
            MonthlyCommission = 0,
            JoinDate = (DateTime)r.join_date
        }).ToList();

        return new ResellerListResponse
        {
            Items = items, TotalCount = totalCount, Page = page, Size = size,
            TotalPages = (int)Math.Ceiling((double)totalCount / size)
        };
    }

    public async Task<ResellerDetail> GetResellerAsync(string resellerId, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();

        var r = await db.QueryFirstOrDefaultAsync<dynamic>(
            @"SELECT r.*,
                     COUNT(DISTINCT t.tenant_id) AS customer_count
              FROM resellers r
              LEFT JOIN tenants t ON t.reseller_id = r.reseller_id
              WHERE r.reseller_id = @Id
              GROUP BY r.reseller_id",
            new { Id = resellerId })
            ?? throw new InvalidOperationException("대리점을 찾을 수 없습니다");

        var bankMasked = MaskBankAccount(r.bank_account as string);

        return new ResellerDetail
        {
            ResellerId = (string)r.reseller_id,
            ResellerCode = (string)r.reseller_code,
            ResellerName = (string)r.reseller_name,
            BizNo = (string)r.biz_no,
            CeoName = (string)r.ceo_name,
            Tel = r.tel as string,
            Address = r.address as string,
            BankName = r.bank_name as string,
            BankAccountMasked = bankMasked,
            AccountHolder = r.account_holder as string,
            ContactPerson = r.contact_person as string,
            ContactPhone = r.contact_phone as string,
            ContactEmail = r.contact_email as string,
            JoinDate = (DateTime)r.join_date,
            Status = (string)r.status,
            CreatedAt = (DateTime)r.created_at,
            CustomerCount = (int)r.customer_count
        };
    }

    public async Task<string> GetResellerBankAccountAsync(string resellerId, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();
        var encrypted = await db.QueryFirstOrDefaultAsync<string>(
            "SELECT bank_account FROM resellers WHERE reseller_id = @Id", new { Id = resellerId })
            ?? throw new InvalidOperationException("대리점을 찾을 수 없습니다");

        if (string.IsNullOrWhiteSpace(encrypted))
            return "";

        // bank_account: Base64(AES-256 encrypted bytes)
        var cipherBytes = Convert.FromBase64String(encrypted);
        return _encryption.Decrypt(cipherBytes);
    }

    public async Task<CreateResellerResponse> CreateResellerAsync(
        CreateResellerRequest request, string createdBy, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();

        var seq = await db.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) + 1 FROM resellers");
        var code = $"RS-{seq:D3}";
        var id = Guid.NewGuid().ToString();

        var encryptedAccount = !string.IsNullOrWhiteSpace(request.BankAccount)
            ? Convert.ToBase64String(_encryption.Encrypt(request.BankAccount)) : null;

        await db.ExecuteAsync(
            @"INSERT INTO resellers
                (reseller_id, reseller_code, reseller_name, biz_no, ceo_name, tel, address,
                 bank_name, bank_account, account_holder,
                 contact_person, contact_phone, contact_email, join_date,
                 status, created_at, updated_at, created_by)
              VALUES
                (@Id, @Code, @Name, @BizNo, @CeoName, @Tel, @Address,
                 @BankName, @BankAccount, @AccountHolder,
                 @ContactPerson, @ContactPhone, @ContactEmail, @JoinDate,
                 'active', @Now, @Now, @CreatedBy)",
            new
            {
                Id = id, Code = code,
                Name = request.ResellerName, request.BizNo, request.CeoName,
                request.Tel, request.Address, request.BankName,
                BankAccount = encryptedAccount, request.AccountHolder,
                request.ContactPerson, request.ContactPhone, request.ContactEmail,
                JoinDate = request.JoinDate.Date,
                Now = DateTime.UtcNow, CreatedBy = createdBy
            });

        return new CreateResellerResponse { ResellerId = id, ResellerCode = code };
    }

    public async Task UpdateResellerAsync(string resellerId, UpdateResellerRequest request, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();
        var encryptedAccount = !string.IsNullOrWhiteSpace(request.BankAccount)
            ? Convert.ToBase64String(_encryption.Encrypt(request.BankAccount)) : null;

        await db.ExecuteAsync(
            @"UPDATE resellers SET
                reseller_name   = COALESCE(@Name, reseller_name),
                ceo_name        = COALESCE(@CeoName, ceo_name),
                tel             = COALESCE(@Tel, tel),
                address         = COALESCE(@Address, address),
                bank_name       = COALESCE(@BankName, bank_name),
                bank_account    = COALESCE(@BankAccount, bank_account),
                account_holder  = COALESCE(@AccountHolder, account_holder),
                contact_person  = COALESCE(@ContactPerson, contact_person),
                contact_phone   = COALESCE(@ContactPhone, contact_phone),
                contact_email   = COALESCE(@ContactEmail, contact_email),
                updated_at      = @Now
              WHERE reseller_id = @Id",
            new
            {
                Id = resellerId,
                Name = request.ResellerName, request.CeoName, request.Tel, request.Address,
                request.BankName, BankAccount = encryptedAccount, request.AccountHolder,
                request.ContactPerson, request.ContactPhone, request.ContactEmail,
                Now = DateTime.UtcNow
            });
    }

    public async Task UpdateResellerStatusAsync(
        string resellerId, UpdateResellerStatusRequest request, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();
        await db.ExecuteAsync(
            "UPDATE resellers SET status = @Status, updated_at = @Now WHERE reseller_id = @Id",
            new { Status = request.NewStatus, Now = DateTime.UtcNow, Id = resellerId });
    }

    // ── 대리점 계정 관리 ─────────────────────────────────────────────────────

    public async Task<List<ResellerAccountItem>> GetResellerAccountsAsync(
        string resellerId, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();
        var rows = await db.QueryAsync<dynamic>(
            @"SELECT account_id, account_name, email, role, is_active, last_login_at, created_at
              FROM reseller_accounts WHERE reseller_id = @ResellerId ORDER BY created_at",
            new { ResellerId = resellerId });

        return rows.Select(r => new ResellerAccountItem
        {
            AccountId = (string)r.account_id,
            AccountName = (string)r.account_name,
            Email = (string)r.email,
            Role = (string)r.role,
            IsActive = (bool)r.is_active,
            LastLoginAt = r.last_login_at as DateTime?,
            CreatedAt = (DateTime)r.created_at
        }).ToList();
    }

    public async Task<CreateResellerAccountResponse> CreateResellerAccountAsync(
        string resellerId, CreateResellerAccountRequest request, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();
        var id = Guid.NewGuid().ToString();
        var tempPassword = GenerateTempPassword();
        var hash = BCrypt.Net.BCrypt.HashPassword(tempPassword);

        await db.ExecuteAsync(
            @"INSERT INTO reseller_accounts
                (account_id, reseller_id, email, password_hash, account_name, role, phone, is_active, created_at, updated_at)
              VALUES
                (@Id, @ResellerId, @Email, @Hash, @Name, @Role, @Phone, 1, @Now, @Now)",
            new
            {
                Id = id, ResellerId = resellerId, request.Email,
                Hash = hash, Name = request.AccountName, request.Role, request.Phone,
                Now = DateTime.UtcNow
            });

        return new CreateResellerAccountResponse { AccountId = id, TempPassword = tempPassword };
    }

    public async Task ToggleResellerAccountAsync(string resellerId, string accountId, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();
        await db.ExecuteAsync(
            @"UPDATE reseller_accounts
              SET is_active = 1 - is_active, updated_at = @Now
              WHERE account_id = @AccountId AND reseller_id = @ResellerId",
            new { Now = DateTime.UtcNow, AccountId = accountId, ResellerId = resellerId });
    }

    // ── 수수료 정책 ──────────────────────────────────────────────────────────

    public async Task<List<CommissionPolicyItem>> GetCommissionPoliciesAsync(
        string resellerId, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();
        var rows = await db.QueryAsync<dynamic>(
            @"SELECT commission_id, plan_code, rate, effective_from, effective_to, is_active, created_at
              FROM reseller_commissions WHERE reseller_id = @ResellerId ORDER BY plan_code, effective_from DESC",
            new { ResellerId = resellerId });

        return rows.Select(r => new CommissionPolicyItem
        {
            CommissionId = (string)r.commission_id,
            PlanCode = (string)r.plan_code,
            Rate = (decimal)r.rate,
            EffectiveFrom = (DateTime)r.effective_from,
            EffectiveTo = r.effective_to as DateTime?,
            IsActive = (bool)r.is_active,
            CreatedAt = (DateTime)r.created_at
        }).ToList();
    }

    public async Task<string> CreateCommissionPolicyAsync(
        string resellerId, CreateCommissionPolicyRequest request, string createdBy, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();
        var id = Guid.NewGuid().ToString();

        using var tx = db.BeginTransaction();
        try
        {
            // 기존 유효 정책 마감 (effective_to = effectiveFrom - 1일)
            var closingDate = request.EffectiveFrom.Date.AddDays(-1);
            await db.ExecuteAsync(
                @"UPDATE reseller_commissions
                  SET effective_to = @ClosingDate, is_active = 0
                  WHERE reseller_id = @ResellerId AND plan_code = @PlanCode AND effective_to IS NULL",
                new { ClosingDate = closingDate, ResellerId = resellerId, request.PlanCode }, tx);

            await db.ExecuteAsync(
                @"INSERT INTO reseller_commissions
                    (commission_id, reseller_id, plan_code, rate, effective_from, effective_to, is_active, created_at, created_by)
                  VALUES
                    (@Id, @ResellerId, @PlanCode, @Rate, @EffectiveFrom, NULL, 1, @Now, @CreatedBy)",
                new
                {
                    Id = id, ResellerId = resellerId, request.PlanCode, request.Rate,
                    EffectiveFrom = request.EffectiveFrom.Date,
                    Now = DateTime.UtcNow, CreatedBy = createdBy
                }, tx);

            tx.Commit();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Backoffice] 수수료 정책 등록 실패 — {ResellerId}", resellerId);
            tx.Rollback();
            throw;
        }

        return id;
    }

    // ── 수수료 정산 ──────────────────────────────────────────────────────────

    public async Task<SettlementListResponse> GetSettlementsAsync(
        string? settlementMonth, string? status, string? resellerId,
        int page, int size, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();
        var offset = (page - 1) * size;

        var where = new List<string>();
        var param = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(settlementMonth)) { where.Add("cs.settlement_month = @Month"); param.Add("Month", settlementMonth); }
        if (!string.IsNullOrWhiteSpace(status)) { where.Add("cs.status = @Status"); param.Add("Status", status); }
        if (!string.IsNullOrWhiteSpace(resellerId)) { where.Add("cs.reseller_id = @ResellerId"); param.Add("ResellerId", resellerId); }
        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        param.Add("Size", size);
        param.Add("Offset", offset);

        var totalCount = await db.QueryFirstOrDefaultAsync<int>(
            $"SELECT COUNT(*) FROM commission_settlements cs {whereClause}", param);

        var rows = await db.QueryAsync<dynamic>(
            $@"SELECT cs.*, r.reseller_name
               FROM commission_settlements cs
               JOIN resellers r ON r.reseller_id = cs.reseller_id
               {whereClause}
               ORDER BY cs.settlement_month DESC, r.reseller_name
               LIMIT @Size OFFSET @Offset", param);

        var items = rows.Select(MapSettlementItem).ToList();

        var monthlyTotals = await db.QueryFirstOrDefaultAsync<dynamic>(
            $@"SELECT COALESCE(SUM(cs.total_revenue),0) AS rev, COALESCE(SUM(cs.total_commission),0) AS comm
               FROM commission_settlements cs {whereClause}", param);

        return new SettlementListResponse
        {
            Items = items, TotalCount = totalCount, Page = page, Size = size,
            TotalPages = (int)Math.Ceiling((double)totalCount / size),
            MonthlyTotalRevenue = (decimal)(monthlyTotals?.rev ?? 0),
            MonthlyTotalCommission = (decimal)(monthlyTotals?.comm ?? 0)
        };
    }

    public async Task<SettlementListItem> GetSettlementAsync(string settlementId, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();
        var row = await db.QueryFirstOrDefaultAsync<dynamic>(
            @"SELECT cs.*, r.reseller_name
              FROM commission_settlements cs
              JOIN resellers r ON r.reseller_id = cs.reseller_id
              WHERE cs.settlement_id = @Id", new { Id = settlementId })
            ?? throw new InvalidOperationException("정산 내역을 찾을 수 없습니다");

        return MapSettlementItem(row);
    }

    public async Task<GenerateSettlementResponse> GenerateSettlementsAsync(
        GenerateSettlementRequest request, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();

        var resellerWhere = !string.IsNullOrWhiteSpace(request.ResellerId)
            ? "AND r.reseller_id = @ResellerId" : "";

        var resellers = (await db.QueryAsync<dynamic>(
            $"SELECT reseller_id FROM resellers WHERE status = 'active' {resellerWhere}",
            new { request.ResellerId })).ToList();

        int generated = 0, skipped = 0;
        var reasons = new List<string>();

        foreach (var res in resellers)
        {
            string rid = (string)res.reseller_id;
            try
            {
                // 중복 체크
                var exists = await db.QueryFirstOrDefaultAsync<int>(
                    "SELECT COUNT(*) FROM commission_settlements WHERE reseller_id = @Rid AND settlement_month = @Month",
                    new { Rid = rid, Month = request.SettlementMonth });
                if (exists > 0) { skipped++; reasons.Add($"{rid}: 이미 정산 존재"); continue; }

                // 구독료 집계
                var revenue = await db.QueryFirstOrDefaultAsync<decimal>(
                    @"SELECT COALESCE(SUM(bi.total_amount), 0)
                      FROM billing_invoices bi
                      JOIN tenants t ON t.tenant_id = bi.tenant_id
                      WHERE t.reseller_id = @Rid AND bi.billing_month = @Month AND bi.status = 'paid'",
                    new { Rid = rid, Month = request.SettlementMonth });

                var customerCount = await db.QueryFirstOrDefaultAsync<int>(
                    @"SELECT COUNT(*) FROM tenants WHERE reseller_id = @Rid AND status = 'active'",
                    new { Rid = rid });

                // 유효 수수료율 (플랜 구분 없이 단순화: pro 기준, 추후 플랜별 계산 확장 가능)
                var rate = await db.QueryFirstOrDefaultAsync<decimal?>(
                    @"SELECT rate FROM reseller_commissions
                      WHERE reseller_id = @Rid AND is_active = 1
                        AND effective_from <= @Month
                        AND (effective_to IS NULL OR effective_to >= @Month)
                      ORDER BY effective_from DESC LIMIT 1",
                    new { Rid = rid, Month = request.SettlementMonth + "-01" }) ?? 0m;

                var commission = Math.Round(revenue * rate / 100m, 2);

                await db.ExecuteAsync(
                    @"INSERT INTO commission_settlements
                        (settlement_id, reseller_id, settlement_month, status,
                         active_customer_count, total_revenue, total_commission,
                         deduction_amount, payment_amount, created_at, updated_at)
                      VALUES
                        (@Id, @Rid, @Month, 'draft',
                         @CustomerCount, @Revenue, @Commission,
                         0, @Commission, @Now, @Now)",
                    new
                    {
                        Id = Guid.NewGuid().ToString(), Rid = rid,
                        Month = request.SettlementMonth,
                        CustomerCount = customerCount,
                        Revenue = revenue, Commission = commission,
                        Now = DateTime.UtcNow
                    });

                generated++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Settlement] 정산 생성 실패 — {ResellerId}", rid);
                skipped++;
                reasons.Add($"{rid}: 오류 발생");
            }
        }

        return new GenerateSettlementResponse
        {
            GeneratedCount = generated,
            SkippedCount = skipped,
            SkippedReasons = reasons
        };
    }

    public async Task ApproveSettlementAsync(
        string settlementId, string approvedBy, ApproveSettlementRequest request, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();
        var affected = await db.ExecuteAsync(
            @"UPDATE commission_settlements
              SET status = 'approved', approval_date = @Today, approved_by = @ApprovedBy,
                  memo = COALESCE(@Memo, memo), updated_at = @Now
              WHERE settlement_id = @Id AND status = 'draft'",
            new { Today = DateTime.UtcNow.Date, ApprovedBy = approvedBy, request.Memo, Now = DateTime.UtcNow, Id = settlementId });

        if (affected == 0)
            throw new InvalidOperationException("승인 가능한 정산이 아닙니다 (draft 상태가 아니거나 존재하지 않음)");
    }

    public async Task PaySettlementAsync(string settlementId, PaySettlementRequest request, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();
        var affected = await db.ExecuteAsync(
            @"UPDATE commission_settlements
              SET status = 'paid', payment_date = @PaymentDate,
                  memo = COALESCE(@Memo, memo), updated_at = @Now
              WHERE settlement_id = @Id AND status = 'approved'",
            new { PaymentDate = request.PaymentDate.Date, request.Memo, Now = DateTime.UtcNow, Id = settlementId });

        if (affected == 0)
            throw new InvalidOperationException("지급 처리 불가 (approved 상태가 아니거나 존재하지 않음)");
    }

    public async Task CancelSettlementAsync(string settlementId, CancelSettlementRequest request, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();
        await db.ExecuteAsync(
            @"UPDATE commission_settlements
              SET status = 'cancelled', memo = @Reason, updated_at = @Now
              WHERE settlement_id = @Id AND status IN ('draft','approved')",
            new { request.Reason, Now = DateTime.UtcNow, Id = settlementId });
    }

    // ── 대리점 — 내 고객사 ───────────────────────────────────────────────────

    public async Task<ResellerTenantListResponse> GetMyTenantsAsync(
        string resellerId, string? status, string? search,
        int page, int size, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();
        var offset = (page - 1) * size;

        var where = new List<string> { "t.reseller_id = @ResellerId" };
        var param = new DynamicParameters();
        param.Add("ResellerId", resellerId);
        if (!string.IsNullOrWhiteSpace(status)) { where.Add("t.status = @Status"); param.Add("Status", status); }
        if (!string.IsNullOrWhiteSpace(search)) { where.Add("t.company_name LIKE @Search"); param.Add("Search", $"%{search}%"); }
        var whereClause = "WHERE " + string.Join(" AND ", where);
        param.Add("Size", size);
        param.Add("Offset", offset);

        var totalCount = await db.QueryFirstOrDefaultAsync<int>(
            $"SELECT COUNT(*) FROM tenants t {whereClause}", param);

        var rows = await db.QueryAsync<dynamic>(
            $@"SELECT t.tenant_id, t.company_name, t.status, t.trial_ends_at,
                      s.plan_type, s.next_billing_at,
                      (SELECT COUNT(*) FROM users u WHERE u.tenant_id = t.tenant_id AND u.is_active = 1) AS user_count
               FROM tenants t
               LEFT JOIN billing_subscriptions s ON s.tenant_id = t.tenant_id AND s.status = 'active'
               {whereClause}
               ORDER BY t.created_at DESC
               LIMIT @Size OFFSET @Offset", param);

        var items = rows.Select(r => new ResellerTenantListItem
        {
            TenantId = (string)r.tenant_id,
            CompanyName = (string)r.company_name,
            Status = (string)r.status,
            PlanType = r.plan_type as string,
            UserCount = (int)(r.user_count ?? 0),
            NextBillingAt = r.next_billing_at as DateTime?,
            TrialEndsAt = r.trial_ends_at as DateTime?
        }).ToList();

        return new ResellerTenantListResponse
        {
            Items = items, TotalCount = totalCount, Page = page, Size = size,
            TotalPages = (int)Math.Ceiling((double)totalCount / size)
        };
    }

    public async Task<ResellerTenantDetail> GetMyTenantAsync(
        string resellerId, string tenantId, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();

        var tenant = await db.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT * FROM tenants WHERE tenant_id = @TenantId AND reseller_id = @ResellerId",
            new { TenantId = tenantId, ResellerId = resellerId })
            ?? throw new UnauthorizedAccessException("접근 권한이 없습니다");

        var sub = await db.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT * FROM billing_subscriptions WHERE tenant_id = @TenantId AND status = 'active' LIMIT 1",
            new { TenantId = tenantId });

        var invoices = (await db.QueryAsync<dynamic>(
            "SELECT invoice_id, billing_month, total_amount, status, paid_at FROM billing_invoices WHERE tenant_id = @TenantId ORDER BY billing_month DESC LIMIT 12",
            new { TenantId = tenantId })).Select(r => new AdminTenantInvoice
        {
            InvoiceId = (string)r.invoice_id,
            BillingMonth = (string)r.billing_month,
            TotalAmount = (int)r.total_amount,
            Status = (string)r.status,
            PaidAt = r.paid_at as DateTime?
        }).ToList();

        return new ResellerTenantDetail
        {
            TenantId = (string)tenant.tenant_id,
            CompanyName = (string)tenant.company_name,
            BizNo = (string)tenant.biz_no,
            CeoName = (string)tenant.ceo_name,
            Tel = tenant.tel as string,
            Address = tenant.address as string,
            Status = (string)tenant.status,
            TrialEndsAt = tenant.trial_ends_at as DateTime?,
            CreatedAt = (DateTime)tenant.created_at,
            Subscription = sub is null ? null : new AdminTenantSubscription
            {
                PlanType = sub.plan_type as string,
                BaseUsers = (int)sub.base_users,
                ExtraUsers = (int)sub.extra_users,
                BaseFee = (int)sub.base_fee,
                ExtraFeePerUser = (int)sub.extra_fee_per_user,
                BillingCycle = sub.billing_cycle as string,
                StartedAt = sub.started_at as DateTime?,
                NextBillingAt = sub.next_billing_at as DateTime?,
                Status = sub.status as string
            },
            Invoices = invoices
        };
    }

    // ── 대리점 — 수수료 ──────────────────────────────────────────────────────

    public async Task<List<CommissionPolicyItem>> GetMyCommissionPoliciesAsync(
        string resellerId, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();
        var rows = await db.QueryAsync<dynamic>(
            @"SELECT commission_id, plan_code, rate, effective_from, effective_to, is_active, created_at
              FROM reseller_commissions
              WHERE reseller_id = @ResellerId AND is_active = 1
              ORDER BY plan_code",
            new { ResellerId = resellerId });

        return rows.Select(r => new CommissionPolicyItem
        {
            CommissionId = (string)r.commission_id,
            PlanCode = (string)r.plan_code,
            Rate = (decimal)r.rate,
            EffectiveFrom = (DateTime)r.effective_from,
            EffectiveTo = r.effective_to as DateTime?,
            IsActive = (bool)r.is_active,
            CreatedAt = (DateTime)r.created_at
        }).ToList();
    }

    public async Task<SettlementListResponse> GetMySettlementsAsync(
        string resellerId, int page, int size, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();
        var offset = (page - 1) * size;

        var totalCount = await db.QueryFirstOrDefaultAsync<int>(
            "SELECT COUNT(*) FROM commission_settlements WHERE reseller_id = @ResellerId",
            new { ResellerId = resellerId });

        var rows = await db.QueryAsync<dynamic>(
            @"SELECT cs.*, r.reseller_name
              FROM commission_settlements cs
              JOIN resellers r ON r.reseller_id = cs.reseller_id
              WHERE cs.reseller_id = @ResellerId
              ORDER BY cs.settlement_month DESC
              LIMIT @Size OFFSET @Offset",
            new { ResellerId = resellerId, Size = size, Offset = offset });

        return new SettlementListResponse
        {
            Items = rows.Select(MapSettlementItem).ToList(),
            TotalCount = totalCount, Page = page, Size = size,
            TotalPages = (int)Math.Ceiling((double)totalCount / size)
        };
    }

    public async Task<ResellerDashboardResponse> GetResellerDashboardAsync(
        string resellerId, CancellationToken ct = default)
    {
        var db = _unitOfWork.GetDbConnection();
        var now = DateTime.UtcNow;
        var thisMonth = now.ToString("yyyy-MM");
        var lastMonth = now.AddMonths(-1).ToString("yyyy-MM");
        var monthStart = new DateTime(now.Year, now.Month, 1);

        var customerCount = await db.QueryFirstOrDefaultAsync<int>(
            "SELECT COUNT(*) FROM tenants WHERE reseller_id = @Rid", new { Rid = resellerId });

        var newThisMonth = await db.QueryFirstOrDefaultAsync<int>(
            "SELECT COUNT(*) FROM tenants WHERE reseller_id = @Rid AND created_at >= @Start",
            new { Rid = resellerId, Start = monthStart });

        var lastSettlement = await db.QueryFirstOrDefaultAsync<dynamic>(
            "SELECT total_commission, status FROM commission_settlements WHERE reseller_id = @Rid AND settlement_month = @Month LIMIT 1",
            new { Rid = resellerId, Month = lastMonth });

        var statusSummary = await db.QueryAsync<dynamic>(
            "SELECT status, COUNT(*) AS cnt FROM tenants WHERE reseller_id = @Rid GROUP BY status",
            new { Rid = resellerId });

        var active = 0; var trial = 0; var suspended = 0;
        foreach (var s in statusSummary)
        {
            if ((string)s.status == "active") active = (int)s.cnt;
            else if ((string)s.status == "trial") trial = (int)s.cnt;
            else if ((string)s.status == "suspended") suspended = (int)s.cnt;
        }

        // 이달 예상 수수료 (이달 paid 청구서 × 수수료율)
        var thisRevenue = await db.QueryFirstOrDefaultAsync<decimal>(
            @"SELECT COALESCE(SUM(bi.total_amount), 0)
              FROM billing_invoices bi JOIN tenants t ON t.tenant_id = bi.tenant_id
              WHERE t.reseller_id = @Rid AND bi.billing_month = @Month AND bi.status = 'paid'",
            new { Rid = resellerId, Month = thisMonth });

        var rate = await db.QueryFirstOrDefaultAsync<decimal?>(
            @"SELECT rate FROM reseller_commissions
              WHERE reseller_id = @Rid AND is_active = 1
              ORDER BY effective_from DESC LIMIT 1",
            new { Rid = resellerId }) ?? 0m;

        var estimatedCommission = Math.Round(thisRevenue * rate / 100m, 2);

        var trend = (await db.QueryAsync<dynamic>(
            @"SELECT cs.settlement_month AS month, cs.total_commission AS commission
              FROM commission_settlements cs
              WHERE cs.reseller_id = @Rid
              ORDER BY cs.settlement_month DESC LIMIT 6",
            new { Rid = resellerId }))
            .Select(r => new MonthlyCommissionTrend
            {
                Month = (string)r.month,
                Commission = (decimal)r.commission
            }).OrderBy(x => x.Month).ToList();

        return new ResellerDashboardResponse
        {
            CustomerCount = customerCount,
            NewCustomersThisMonth = newThisMonth,
            EstimatedCommission = estimatedCommission,
            LastMonthCommission = lastSettlement is null ? 0 : (decimal)lastSettlement.total_commission,
            LastMonthSettlementStatus = lastSettlement is null ? "" : (string)lastSettlement.status,
            CustomerStatusSummary = new CustomerStatusSummary { Active = active, Trial = trial, Suspended = suspended },
            MonthlyCommissionTrend = trend
        };
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────────────────

    private static SettlementListItem MapSettlementItem(dynamic r) => new()
    {
        SettlementId = (string)r.settlement_id,
        ResellerId = (string)r.reseller_id,
        ResellerName = (string)r.reseller_name,
        SettlementMonth = (string)r.settlement_month,
        ActiveCustomerCount = (int)r.active_customer_count,
        TotalRevenue = (decimal)r.total_revenue,
        TotalCommission = (decimal)r.total_commission,
        DeductionAmount = (decimal)r.deduction_amount,
        PaymentAmount = (decimal)r.payment_amount,
        Status = (string)r.status,
        PaymentDate = r.payment_date as DateTime?,
        ApprovalDate = r.approval_date as DateTime?
    };

    private static string? MaskBankAccount(string? encrypted)
    {
        if (string.IsNullOrWhiteSpace(encrypted)) return null;
        // 암호화된 값은 길어서 마스킹 처리 (복호화는 별도 엔드포인트에서)
        return "****-****";
    }

    private static string GenerateTempPassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
        var random = new Random();
        return new string(Enumerable.Range(0, 10).Select(_ => chars[random.Next(chars.Length)]).ToArray()) + "!1";
    }

    private static (int baseUsers, int baseFee, int extraFeePerUser) GetPlanFees(string planType) =>
        planType switch
        {
            "pro" => (5, 59000, 10000),
            "enterprise" => (10, 100000, 10000),
            _ => (3, 29000, 10000)
        };
}
