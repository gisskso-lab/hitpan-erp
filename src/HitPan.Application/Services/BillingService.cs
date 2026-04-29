using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Billing;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

/// <summary>
/// 구독결제 관리 서비스 (사장님 결재 2026-04-29).
/// PG 의존 동작은 IBillingProvider 구현체로 위임 (현재: ManualBillingProvider).
/// 토스 도입 시 TossBillingProvider 추가만으로 확장 가능 — 본 서비스 코드 수정 0.
/// </summary>
public sealed class BillingService : IBillingService
{
    private readonly IDbConnection _db;
    private readonly IEnumerable<IBillingProvider> _providers;

    public BillingService(IDbConnection db, IEnumerable<IBillingProvider> providers)
    {
        _db = db;
        _providers = providers;
    }

    private IBillingProvider ResolveProvider(string code)
    {
        var p = _providers.FirstOrDefault(x => x.ProviderCode == code);
        if (p is null)
        {
            throw new InvalidOperationException($"등록되지 않은 결제 provider: {code}");
        }
        return p;
    }

    // ─── 운영 설정 ───────────────────────────────────────
    public async Task<BillingSettingsDto> GetSettingsAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT head_office_bank    AS HeadOfficeBank,
                   head_office_account AS HeadOfficeAccount,
                   head_office_holder  AS HeadOfficeHolder,
                   auto_billing_day    AS AutoBillingDay,
                   grace_period_days   AS GracePeriodDays,
                   notify_email        AS NotifyEmail
            FROM billing_settings
            WHERE tenant_id = @TenantId
            """;

        var dto = await _db.QueryFirstOrDefaultAsync<BillingSettingsDto>(new CommandDefinition(
            sql, new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);

        return dto ?? new BillingSettingsDto();
    }

    public async Task UpdateSettingsAsync(string tenantId, UpdateBillingSettingsRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string upsert = """
            INSERT INTO billing_settings
              (setting_id, tenant_id, head_office_bank, head_office_account, head_office_holder,
               auto_billing_day, grace_period_days, notify_email)
            VALUES
              (UUID(), @TenantId, @HeadOfficeBank, @HeadOfficeAccount, @HeadOfficeHolder,
               @AutoBillingDay, @GracePeriodDays, @NotifyEmail)
            ON DUPLICATE KEY UPDATE
              head_office_bank    = VALUES(head_office_bank),
              head_office_account = VALUES(head_office_account),
              head_office_holder  = VALUES(head_office_holder),
              auto_billing_day    = VALUES(auto_billing_day),
              grace_period_days   = VALUES(grace_period_days),
              notify_email        = VALUES(notify_email),
              updated_at          = NOW(6)
            """;

        await _db.ExecuteAsync(new CommandDefinition(
            upsert,
            new
            {
                TenantId = tenantId,
                request.HeadOfficeBank,
                request.HeadOfficeAccount,
                request.HeadOfficeHolder,
                request.AutoBillingDay,
                request.GracePeriodDays,
                request.NotifyEmail
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    // ─── 결제수단 ────────────────────────────────────────
    public async Task<List<PaymentMethodDto>> GetPaymentMethodsAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT payment_method_id AS PaymentMethodId,
                   provider          AS Provider,
                   method_type       AS MethodType,
                   display_name      AS DisplayName,
                   card_brand        AS CardBrand,
                   card_last4        AS CardLast4,
                   card_owner_type   AS CardOwnerType,
                   is_default        AS IsDefault,
                   is_active         AS IsActive,
                   registered_at     AS RegisteredAt,
                   last_used_at      AS LastUsedAt
            FROM billing_payment_methods
            WHERE tenant_id = @TenantId
            ORDER BY is_default DESC, registered_at DESC
            """;

        var rows = await _db.QueryAsync<PaymentMethodDto>(new CommandDefinition(
            sql, new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToList();
    }

    public async Task<string> RegisterPaymentMethodAsync(string tenantId, RegisterPaymentMethodRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var provider = ResolveProvider(request.Provider);
        var customerKey = string.IsNullOrEmpty(request.CustomerKey)
            ? $"hp-{tenantId}"
            : request.CustomerKey;

        var pgResult = await provider.RegisterPaymentMethodAsync(
            tenantId, customerKey, request.TossAuthKey, ct).ConfigureAwait(false);

        if (!pgResult.Success)
        {
            throw new InvalidOperationException(
                $"결제수단 등록 실패: {pgResult.ErrorCode} {pgResult.ErrorMessage}");
        }

        var paymentMethodId = Guid.NewGuid().ToString();

        // 빌링키 평문은 받자마자 메모리에서만 다루고, DB에는 향후 ValueConverter로 암호화 저장 예정.
        // 현재 ManualProvider 는 빌링키 없음 → null 저장. 토스 붙는 시점에 암호화 hookup.
        byte[]? billingKeyEncrypted = pgResult.BillingKey is null
            ? null
            : System.Text.Encoding.UTF8.GetBytes(pgResult.BillingKey);
        // TODO(토스 도입 시): EncryptionConverter 로 교체. 지금은 평문 바이트로 임시 저장 (Manual 은 null).

        using var tx = (_db as DbConnection)?.BeginTransaction()
                       ?? throw new InvalidOperationException("DbConnection 트랜잭션 사용 불가");

        try
        {
            if (request.SetAsDefault)
            {
                const string clearDefault = """
                    UPDATE billing_payment_methods SET is_default = 0
                    WHERE tenant_id = @TenantId
                    """;
                await _db.ExecuteAsync(new CommandDefinition(
                    clearDefault, new { TenantId = tenantId },
                    transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            }

            const string insert = """
                INSERT INTO billing_payment_methods
                  (payment_method_id, tenant_id, provider, method_type, provider_billing_key,
                   customer_key, display_name, card_brand, card_last4, card_owner_type,
                   is_default, is_active, registered_at)
                VALUES
                  (@PaymentMethodId, @TenantId, @Provider, @MethodType, @BillingKey,
                   @CustomerKey, @DisplayName, @CardBrand, @CardLast4, @CardOwnerType,
                   @IsDefault, 1, NOW(6))
                """;

            await _db.ExecuteAsync(new CommandDefinition(
                insert,
                new
                {
                    PaymentMethodId = paymentMethodId,
                    TenantId = tenantId,
                    request.Provider,
                    request.MethodType,
                    BillingKey = billingKeyEncrypted,
                    CustomerKey = customerKey,
                    request.DisplayName,
                    CardBrand = pgResult.CardBrand ?? request.CardBrand,
                    CardLast4 = pgResult.CardLast4 ?? request.CardLast4,
                    CardOwnerType = pgResult.CardOwnerType ?? request.CardOwnerType,
                    IsDefault = request.SetAsDefault ? 1 : 0
                },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            tx.Commit();
            return paymentMethodId;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task SetDefaultPaymentMethodAsync(string tenantId, string paymentMethodId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        using var tx = (_db as DbConnection)?.BeginTransaction()
                       ?? throw new InvalidOperationException("DbConnection 트랜잭션 사용 불가");

        try
        {
            await _db.ExecuteAsync(new CommandDefinition(
                "UPDATE billing_payment_methods SET is_default = 0 WHERE tenant_id = @TenantId",
                new { TenantId = tenantId },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            await _db.ExecuteAsync(new CommandDefinition(
                """
                UPDATE billing_payment_methods SET is_default = 1, updated_at = NOW(6)
                WHERE tenant_id = @TenantId AND payment_method_id = @PaymentMethodId
                """,
                new { TenantId = tenantId, PaymentMethodId = paymentMethodId },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task DeletePaymentMethodAsync(string tenantId, string paymentMethodId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 인보이스 결제 이력에서 참조 중이면 비활성화로 대체 (감사 추적 보호).
        const string usageSql = """
            SELECT COUNT(*) FROM billing_invoices
            WHERE tenant_id = @TenantId AND payment_method_id = @PaymentMethodId
            """;
        var inUse = await _db.ExecuteScalarAsync<int>(new CommandDefinition(
            usageSql, new { TenantId = tenantId, PaymentMethodId = paymentMethodId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (inUse > 0)
        {
            await _db.ExecuteAsync(new CommandDefinition(
                """
                UPDATE billing_payment_methods
                SET is_active = 0, is_default = 0, updated_at = NOW(6)
                WHERE tenant_id = @TenantId AND payment_method_id = @PaymentMethodId
                """,
                new { TenantId = tenantId, PaymentMethodId = paymentMethodId },
                cancellationToken: ct)).ConfigureAwait(false);
            return;
        }

        await _db.ExecuteAsync(new CommandDefinition(
            "DELETE FROM billing_payment_methods WHERE tenant_id = @TenantId AND payment_method_id = @PaymentMethodId",
            new { TenantId = tenantId, PaymentMethodId = paymentMethodId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    // ─── 구독 ────────────────────────────────────────────
    public async Task<SubscriptionDto?> GetCurrentSubscriptionAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT subscription_id    AS SubscriptionId,
                   plan_code          AS PlanCode,
                   plan_name          AS PlanName,
                   monthly_amount     AS MonthlyAmount,
                   license_count      AS LicenseCount,
                   payment_method_id  AS PaymentMethodId,
                   started_at         AS StartedAt,
                   next_billing_date  AS NextBillingDate,
                   expires_at         AS ExpiresAt,
                   status             AS Status
            FROM billing_subscriptions
            WHERE tenant_id = @TenantId AND status = 'active'
            ORDER BY started_at DESC
            LIMIT 1
            """;

        return await _db.QueryFirstOrDefaultAsync<SubscriptionDto>(new CommandDefinition(
            sql, new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
    }

    // ─── 인보이스 ────────────────────────────────────────
    public async Task<List<InvoiceListDto>> GetInvoicesAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT invoice_id           AS InvoiceId,
                   invoice_no           AS InvoiceNo,
                   plan_name            AS PlanName,
                   billing_period_start AS BillingPeriodStart,
                   billing_period_end   AS BillingPeriodEnd,
                   total_amount         AS TotalAmount,
                   status               AS Status,
                   issued_at            AS IssuedAt,
                   paid_at              AS PaidAt,
                   provider             AS Provider,
                   tax_invoice_issued   AS TaxInvoiceIssued
            FROM billing_invoices
            WHERE tenant_id = @TenantId
            ORDER BY issued_at DESC
            """;

        var rows = await _db.QueryAsync<InvoiceListDto>(new CommandDefinition(
            sql, new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToList();
    }

    public async Task<InvoiceDetailDto?> GetInvoiceAsync(string tenantId, string invoiceId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT invoice_id           AS InvoiceId,
                   invoice_no           AS InvoiceNo,
                   subscription_id      AS SubscriptionId,
                   plan_code            AS PlanCode,
                   plan_name            AS PlanName,
                   billing_period_start AS BillingPeriodStart,
                   billing_period_end   AS BillingPeriodEnd,
                   amount               AS Amount,
                   vat                  AS Vat,
                   total_amount         AS TotalAmount,
                   status               AS Status,
                   issued_at            AS IssuedAt,
                   due_date             AS DueDate,
                   paid_at               AS PaidAt,
                   payment_method_id    AS PaymentMethodId,
                   provider             AS Provider,
                   provider_payment_key AS ProviderPaymentKey,
                   receipt_url          AS ReceiptUrl,
                   tax_invoice_issued   AS TaxInvoiceIssued,
                   tax_invoice_no       AS TaxInvoiceNo,
                   memo                 AS Memo
            FROM billing_invoices
            WHERE tenant_id = @TenantId AND invoice_id = @InvoiceId
            """;

        return await _db.QueryFirstOrDefaultAsync<InvoiceDetailDto>(new CommandDefinition(
            sql, new { TenantId = tenantId, InvoiceId = invoiceId }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<bool> PayInvoiceAsync(string tenantId, string invoiceId, PayInvoiceRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 인보이스 + 결제수단 조회.
        const string fetchSql = """
            SELECT i.invoice_id, i.total_amount, i.plan_name, i.status,
                   pm.provider, pm.customer_key
            FROM billing_invoices i
            LEFT JOIN billing_payment_methods pm
              ON pm.tenant_id = i.tenant_id AND pm.payment_method_id = @PaymentMethodId
            WHERE i.tenant_id = @TenantId AND i.invoice_id = @InvoiceId
            """;

        var row = await _db.QueryFirstOrDefaultAsync<(
            string InvoiceId, decimal TotalAmount, string PlanName, string Status,
            string? Provider, string? CustomerKey
        )?>(new CommandDefinition(
            fetchSql,
            new { TenantId = tenantId, InvoiceId = invoiceId, request.PaymentMethodId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (row is null) return false;
        if (row.Value.Status == "paid") return true;
        if (string.IsNullOrEmpty(row.Value.Provider)) return false;

        var provider = ResolveProvider(row.Value.Provider);
        var charge = await provider.ChargeAsync(
            tenantId, invoiceId, row.Value.TotalAmount,
            billingKeyPlain: null, // 토스 도입 시 DB에서 디코딩해 주입
            customerKey: row.Value.CustomerKey ?? $"hp-{tenantId}",
            orderName: row.Value.PlanName,
            ct).ConfigureAwait(false);

        // attempt 로그
        await InsertAttemptAsync(tenantId, invoiceId, request.PaymentMethodId, row.Value.Provider, charge, ct)
            .ConfigureAwait(false);

        if (charge.Success)
        {
            const string updateSql = """
                UPDATE billing_invoices
                SET status               = 'paid',
                    paid_at              = NOW(6),
                    payment_method_id    = @PaymentMethodId,
                    provider             = @Provider,
                    provider_payment_key = @PaymentKey,
                    receipt_url          = @ReceiptUrl,
                    memo                 = COALESCE(@Memo, memo),
                    updated_at           = NOW(6)
                WHERE tenant_id = @TenantId AND invoice_id = @InvoiceId
                """;

            await _db.ExecuteAsync(new CommandDefinition(
                updateSql,
                new
                {
                    TenantId = tenantId,
                    InvoiceId = invoiceId,
                    request.PaymentMethodId,
                    Provider = row.Value.Provider,
                    PaymentKey = charge.ProviderPaymentKey,
                    charge.ReceiptUrl,
                    request.Memo
                },
                cancellationToken: ct)).ConfigureAwait(false);
        }

        return charge.Success;
    }

    public async Task MarkInvoicePaidManuallyAsync(string tenantId, string invoiceId, MarkPaidRequest request, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            UPDATE billing_invoices
            SET status     = 'paid',
                paid_at    = COALESCE(@PaidAt, NOW(6)),
                provider   = COALESCE(provider, 'manual'),
                memo       = COALESCE(@Memo, memo),
                updated_at = NOW(6)
            WHERE tenant_id = @TenantId AND invoice_id = @InvoiceId
              AND status IN ('pending', 'failed')
            """;

        await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new { TenantId = tenantId, InvoiceId = invoiceId, request.PaidAt, request.Memo },
            cancellationToken: ct)).ConfigureAwait(false);

        // 감사 로그
        await InsertAttemptAsync(tenantId, invoiceId, null, "manual",
            new ChargeResult { Success = true, Status = "success" }, ct).ConfigureAwait(false);
    }

    private async Task InsertAttemptAsync(
        string tenantId, string invoiceId, string? paymentMethodId, string provider,
        ChargeResult result, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO billing_payment_attempts
              (attempt_id, tenant_id, invoice_id, payment_method_id, provider, status,
               error_code, error_message, provider_response_json)
            VALUES
              (UUID(), @TenantId, @InvoiceId, @PaymentMethodId, @Provider, @Status,
               @ErrorCode, @ErrorMessage, @RawResponse)
            """;

        await _db.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                TenantId = tenantId,
                InvoiceId = invoiceId,
                PaymentMethodId = paymentMethodId,
                Provider = provider,
                Status = result.Status,
                result.ErrorCode,
                result.ErrorMessage,
                result.RawResponse
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

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
}
