using System.Data;
using System.Data.Common;
using Dapper;
using HitPan.Application.DTOs.Settings;
using HitPan.Application.Interfaces;

namespace HitPan.Application.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly IDbConnection _db;

    public SettingsService(IDbConnection db)
    {
        _db = db;
    }

    public async Task<TenantSettingsDto> GetAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        const string sql = """
            SELECT
              tenant_id AS TenantId,
              allow_force_price_input AS AllowForcePriceInput,
              allow_force_vat_input AS AllowForceVatInput,
              allow_zero_price AS AllowZeroPrice,
              allow_past_edit AS AllowPastEdit,
              past_edit_password_hash AS PastEditPasswordHash,
              allow_force_stock_adjust AS AllowForceStockAdjust,
              allow_credit_override AS AllowCreditOverride,
              price_deviation_limit AS PriceDeviationLimit,
              force_edit_require_password AS ForceEditRequirePassword
            FROM tenant_settings
            WHERE tenant_id = @TenantId
            """;

        var row = await _db.QueryFirstOrDefaultAsync<TenantSettingsRow>(
            new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct))
            .ConfigureAwait(false);

        if (row is null)
        {
            return DefaultSettings(tenantId);
        }

        return new TenantSettingsDto
        {
            TenantId = row.TenantId,
            AllowForcePriceInput = ToBool(row.AllowForcePriceInput),
            AllowForceVatInput = ToBool(row.AllowForceVatInput),
            AllowZeroPrice = ToBool(row.AllowZeroPrice),
            AllowPastEdit = ToBool(row.AllowPastEdit),
            HasPastEditPassword = !string.IsNullOrEmpty(row.PastEditPasswordHash),
            AllowForceStockAdjust = ToBool(row.AllowForceStockAdjust),
            AllowCreditOverride = ToBool(row.AllowCreditOverride),
            PriceDeviationLimit = (int)Math.Clamp(row.PriceDeviationLimit, 0, int.MaxValue),
            ForceEditRequirePassword = ToBool(row.ForceEditRequirePassword)
        };
    }

    public async Task SaveAsync(UpdateTenantSettingsDto dto, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var limit = Math.Clamp(dto.PriceDeviationLimit, 0, 1000);

        var existingHash = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            """
            SELECT past_edit_password_hash
            FROM tenant_settings
            WHERE tenant_id = @TenantId
            """,
            new { TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);

        string? passwordHash = existingHash;
        if (dto.PastEditPassword is not null)
        {
            passwordHash = dto.PastEditPassword.Length == 0
                ? null
                : BCrypt.Net.BCrypt.HashPassword(dto.PastEditPassword);
        }

        const string upsert = """
            INSERT INTO tenant_settings (
              tenant_id,
              allow_force_price_input,
              allow_force_vat_input,
              allow_zero_price,
              allow_past_edit,
              past_edit_password_hash,
              allow_force_stock_adjust,
              allow_credit_override,
              price_deviation_limit,
              force_edit_require_password)
            VALUES (
              @TenantId,
              @AllowForcePriceInput,
              @AllowForceVatInput,
              @AllowZeroPrice,
              @AllowPastEdit,
              @PastEditPasswordHash,
              @AllowForceStockAdjust,
              @AllowCreditOverride,
              @PriceDeviationLimit,
              @ForceEditRequirePassword)
            ON DUPLICATE KEY UPDATE
              allow_force_price_input = @AllowForcePriceInput,
              allow_force_vat_input = @AllowForceVatInput,
              allow_zero_price = @AllowZeroPrice,
              allow_past_edit = @AllowPastEdit,
              past_edit_password_hash = @PastEditPasswordHash,
              allow_force_stock_adjust = @AllowForceStockAdjust,
              allow_credit_override = @AllowCreditOverride,
              price_deviation_limit = @PriceDeviationLimit,
              force_edit_require_password = @ForceEditRequirePassword
            """;

        await _db.ExecuteAsync(new CommandDefinition(
            upsert,
            new
            {
                TenantId = tenantId,
                AllowForcePriceInput = dto.AllowForcePriceInput ? 1 : 0,
                AllowForceVatInput = dto.AllowForceVatInput ? 1 : 0,
                AllowZeroPrice = dto.AllowZeroPrice ? 1 : 0,
                AllowPastEdit = dto.AllowPastEdit ? 1 : 0,
                PastEditPasswordHash = passwordHash,
                AllowForceStockAdjust = dto.AllowForceStockAdjust ? 1 : 0,
                AllowCreditOverride = dto.AllowCreditOverride ? 1 : 0,
                PriceDeviationLimit = limit,
                ForceEditRequirePassword = dto.ForceEditRequirePassword ? 1 : 0
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<UnitPriceValidationDto> ValidateUnitPriceAsync(
        string tenantId,
        decimal unitPrice,
        decimal referencePrice,
        CancellationToken ct = default)
    {
        var settings = await GetAsync(tenantId, ct).ConfigureAwait(false);
        var limit = Math.Clamp(settings.PriceDeviationLimit, 0, 1000);

        if (referencePrice == 0m)
        {
            if (unitPrice == 0m)
            {
                return new UnitPriceValidationDto
                {
                    Ok = true,
                    AppliedDeviationLimit = limit,
                    DeviationPercent = 0m
                };
            }

            if (!settings.AllowZeroPrice)
            {
                return new UnitPriceValidationDto
                {
                    Ok = false,
                    Message = "기준가가 0일 때 단가 입력은 허용되지 않습니다.",
                    AppliedDeviationLimit = limit
                };
            }

            return new UnitPriceValidationDto
            {
                Ok = true,
                AppliedDeviationLimit = limit,
                Message = "기준가 0 — 편차 검증 생략"
            };
        }

        var deviation = Math.Abs(unitPrice - referencePrice) / Math.Abs(referencePrice) * 100m;
        var ok = IsUnitPriceWithinDeviation(unitPrice, referencePrice, limit);

        return new UnitPriceValidationDto
        {
            Ok = ok,
            DeviationPercent = decimal.Round(deviation, 4, MidpointRounding.AwayFromZero),
            AppliedDeviationLimit = limit,
            Message = ok
                ? null
                : $"단가가 기준가 대비 허용 범위({limit}%)를 벗어났습니다."
        };
    }

    public bool IsUnitPriceWithinDeviation(
        decimal unitPrice,
        decimal referencePrice,
        int priceDeviationLimitPercent)
    {
        var limit = Math.Clamp(priceDeviationLimitPercent, 0, 1000);
        if (referencePrice == 0m)
        {
            return unitPrice == 0m;
        }

        var deviationPct = Math.Abs(unitPrice - referencePrice) / Math.Abs(referencePrice) * 100m;
        return deviationPct <= limit;
    }

    public async Task LogForceEditAsync(
        string tenantId,
        string userId,
        string tableName,
        string recordId,
        string fieldName,
        string? beforeValue,
        string? afterValue,
        string? reason,
        string? ip,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        await _db.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO force_edit_logs (
              log_id, tenant_id, user_id,
              table_name, record_id,
              field_name,
              before_value, after_value,
              reason, ip_address, created_at)
            VALUES (
              UUID(), @TenantId, @UserId,
              @TableName, @RecordId,
              @FieldName,
              @BeforeValue, @AfterValue,
              @Reason, @Ip, NOW(6))
            """,
            new
            {
                TenantId = tenantId,
                UserId = userId,
                TableName = tableName,
                RecordId = recordId,
                FieldName = fieldName,
                BeforeValue = beforeValue,
                AfterValue = afterValue,
                Reason = reason,
                Ip = ip
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<bool> VerifyForceEditPasswordAsync(
        string tenantId,
        string inputPassword,
        CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        var hash = await _db.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            """
            SELECT past_edit_password_hash
            FROM tenant_settings
            WHERE tenant_id = @TenantId
            """,
            new { TenantId = tenantId },
            cancellationToken: ct)).ConfigureAwait(false);

        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(inputPassword))
        {
            return false;
        }

        return BCrypt.Net.BCrypt.Verify(inputPassword, hash);
    }

    private static TenantSettingsDto DefaultSettings(string tenantId) => new()
    {
        TenantId = tenantId,
        AllowForcePriceInput = true,
        AllowForceVatInput = false,
        AllowZeroPrice = false,
        AllowPastEdit = false,
        HasPastEditPassword = false,
        AllowForceStockAdjust = true,
        AllowCreditOverride = false,
        PriceDeviationLimit = 50,
        ForceEditRequirePassword = true
    };

    private static bool ToBool(long v) => v != 0;

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_db.State == ConnectionState.Open)
        {
            return;
        }

        if (_db is DbConnection dbConnection)
        {
            await dbConnection.OpenAsync(ct).ConfigureAwait(false);
            return;
        }

        _db.Open();
    }

    private sealed class TenantSettingsRow
    {
        public string TenantId { get; set; } = string.Empty;

        public long AllowForcePriceInput { get; set; }

        public long AllowForceVatInput { get; set; }

        public long AllowZeroPrice { get; set; }

        public long AllowPastEdit { get; set; }

        public string? PastEditPasswordHash { get; set; }

        public long AllowForceStockAdjust { get; set; }

        public long AllowCreditOverride { get; set; }

        public long PriceDeviationLimit { get; set; }

        public long ForceEditRequirePassword { get; set; }
    }
}
