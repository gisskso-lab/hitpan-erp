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
              force_edit_require_password AS ForceEditRequirePassword,
              stock_eval_method AS StockEvalMethod,
              use_multi_warehouse AS UseMultiWarehouse,
              stock_shortage_alert AS StockShortageAlert,
              allow_minus_stock AS AllowMinusStock,
              price_input_type AS PriceInputType,
              auto_vat_adjust AS AutoVatAdjust,
              vat_round_type AS VatRoundType,
              price_a_rate AS PriceARate,
              price_b_rate AS PriceBRate,
              price_c_rate AS PriceCRate,
              price_d_rate AS PriceDRate,
              price_e_rate AS PriceERate,
              use_credit_limit AS UseCreditLimit,
              credit_limit_amount AS CreditLimitAmount,
              show_purchase_price AS ShowPurchasePrice,
              use_sales_by_employee AS UseSalesByEmployee,
              use_personal_info_protect AS UsePersonalInfoProtect,
              industry_type AS IndustryType
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
            ForceEditRequirePassword = ToBool(row.ForceEditRequirePassword),
            StockEvalMethod = row.StockEvalMethod,
            UseMultiWarehouse = ToBool(row.UseMultiWarehouse),
            StockShortageAlert = ToBool(row.StockShortageAlert),
            AllowMinusStock = ToBool(row.AllowMinusStock),
            PriceInputType = row.PriceInputType,
            AutoVatAdjust = ToBool(row.AutoVatAdjust),
            VatRoundType = row.VatRoundType,
            PriceARate = row.PriceARate,
            PriceBRate = row.PriceBRate,
            PriceCRate = row.PriceCRate,
            PriceDRate = row.PriceDRate,
            PriceERate = row.PriceERate,
            UseCreditLimit = ToBool(row.UseCreditLimit),
            CreditLimitAmount = row.CreditLimitAmount,
            ShowPurchasePrice = ToBool(row.ShowPurchasePrice),
            UseSalesByEmployee = ToBool(row.UseSalesByEmployee),
            UsePersonalInfoProtect = ToBool(row.UsePersonalInfoProtect),
            IndustryType = row.IndustryType
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
              force_edit_require_password,
              stock_eval_method,
              use_multi_warehouse,
              stock_shortage_alert,
              allow_minus_stock,
              price_input_type,
              auto_vat_adjust,
              vat_round_type,
              price_a_rate,
              price_b_rate,
              price_c_rate,
              price_d_rate,
              price_e_rate,
              use_credit_limit,
              credit_limit_amount,
              show_purchase_price,
              use_sales_by_employee,
              use_personal_info_protect,
              industry_type)
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
              @ForceEditRequirePassword,
              @StockEvalMethod,
              @UseMultiWarehouse,
              @StockShortageAlert,
              @AllowMinusStock,
              @PriceInputType,
              @AutoVatAdjust,
              @VatRoundType,
              @PriceARate,
              @PriceBRate,
              @PriceCRate,
              @PriceDRate,
              @PriceERate,
              @UseCreditLimit,
              @CreditLimitAmount,
              @ShowPurchasePrice,
              @UseSalesByEmployee,
              @UsePersonalInfoProtect,
              @IndustryType)
            ON DUPLICATE KEY UPDATE
              allow_force_price_input = @AllowForcePriceInput,
              allow_force_vat_input = @AllowForceVatInput,
              allow_zero_price = @AllowZeroPrice,
              allow_past_edit = @AllowPastEdit,
              past_edit_password_hash = @PastEditPasswordHash,
              allow_force_stock_adjust = @AllowForceStockAdjust,
              allow_credit_override = @AllowCreditOverride,
              price_deviation_limit = @PriceDeviationLimit,
              force_edit_require_password = @ForceEditRequirePassword,
              stock_eval_method = @StockEvalMethod,
              use_multi_warehouse = @UseMultiWarehouse,
              stock_shortage_alert = @StockShortageAlert,
              allow_minus_stock = @AllowMinusStock,
              price_input_type = @PriceInputType,
              auto_vat_adjust = @AutoVatAdjust,
              vat_round_type = @VatRoundType,
              price_a_rate = @PriceARate,
              price_b_rate = @PriceBRate,
              price_c_rate = @PriceCRate,
              price_d_rate = @PriceDRate,
              price_e_rate = @PriceERate,
              use_credit_limit = @UseCreditLimit,
              credit_limit_amount = @CreditLimitAmount,
              show_purchase_price = @ShowPurchasePrice,
              use_sales_by_employee = @UseSalesByEmployee,
              use_personal_info_protect = @UsePersonalInfoProtect,
              industry_type = @IndustryType
            """;

        var priceInputType = NormalizeEnum(dto.PriceInputType, new[] { "net", "inclusive" }, "net");
        var stockEvalMethod = NormalizeEnum(dto.StockEvalMethod, new[] { "moving_avg", "fifo", "lifo" }, "moving_avg");
        var vatRoundType = NormalizeEnum(dto.VatRoundType, new[] { "round", "floor", "ceil" }, "round");
        var industryType = NormalizeEnum(dto.IndustryType, new[] { "retail", "metal", "elec", "plastic", "wood", "food" }, "retail");
        var creditAmount = dto.CreditLimitAmount < 0 ? 0 : dto.CreditLimitAmount;

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
                ForceEditRequirePassword = dto.ForceEditRequirePassword ? 1 : 0,
                StockEvalMethod = stockEvalMethod,
                UseMultiWarehouse = dto.UseMultiWarehouse ? 1 : 0,
                StockShortageAlert = dto.StockShortageAlert ? 1 : 0,
                AllowMinusStock = dto.AllowMinusStock ? 1 : 0,
                PriceInputType = priceInputType,
                AutoVatAdjust = dto.AutoVatAdjust ? 1 : 0,
                VatRoundType = vatRoundType,
                PriceARate = dto.PriceARate,
                PriceBRate = dto.PriceBRate,
                PriceCRate = dto.PriceCRate,
                PriceDRate = dto.PriceDRate,
                PriceERate = dto.PriceERate,
                UseCreditLimit = dto.UseCreditLimit ? 1 : 0,
                CreditLimitAmount = creditAmount,
                ShowPurchasePrice = dto.ShowPurchasePrice ? 1 : 0,
                UseSalesByEmployee = dto.UseSalesByEmployee ? 1 : 0,
                UsePersonalInfoProtect = dto.UsePersonalInfoProtect ? 1 : 0,
                IndustryType = industryType
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    private static string NormalizeEnum(string? value, string[] allowed, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim().ToLowerInvariant();
        return Array.Exists(allowed, v => v == trimmed) ? trimmed : fallback;
    }

    /// <summary>
    /// local_company 테이블에 사업장 연락처·주소 등 표시용 정보를 저장한다.
    /// </summary>
    public async Task SaveCompanyAsync(UpdateTenantCompanyDto dto, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // NOT NULL 컬럼(상호·대표·사업자번호)은 빈 값이 오면 DB 기존 값을 유지한다.
        // is_locked_from_landing 도 함께 읽는다 — 잠금 시 이 3필드는 서버가 변경을 거부한다(아래).
        const string selectRequired = """
            SELECT
              company_name AS CompanyName,
              ceo_name AS CeoName,
              biz_no AS BizNo,
              is_locked_from_landing AS IsLockedFromLanding
            FROM local_company
            WHERE tenant_id = @TenantId
            """;

        var required = await _db.QueryFirstOrDefaultAsync<TenantCompanyRequiredRow>(
                new CommandDefinition(selectRequired, new { TenantId = tenantId }, cancellationToken: ct))
            .ConfigureAwait(false);

        if (required is null)
        {
            return;
        }

        // 🔴 랜딩 잠금 강제 (헌법 #35 · 작4 §2-6-2 봉합).
        //   종전에는 화면에서만 ReadOnly 로 막았고 서버엔 검증이 없었다.
        //   화면 잠금은 화면을 거치지 않는 요청을 못 막는다 — 서버가 최종 관문이다.
        //   잠긴 테넌트는 회사명·사업자번호·대표자명 3필드를 무조건 DB 기존 값으로 되돌린다.
        //   (변경이 필요하면 본사 고객지원으로 사업자등록증을 재등록한다.)
        var lockedFromLanding = required.IsLockedFromLanding;

        var companyName = lockedFromLanding || string.IsNullOrWhiteSpace(dto.CompanyName)
            ? required.CompanyName
            : dto.CompanyName.Trim();
        var ceoName = lockedFromLanding || string.IsNullOrWhiteSpace(dto.CeoName)
            ? required.CeoName
            : dto.CeoName.Trim();
        var bizNo = lockedFromLanding || string.IsNullOrWhiteSpace(dto.BizNo)
            ? required.BizNo
            : dto.BizNo.Trim();
        if (bizNo.Length > 12)
        {
            bizNo = bizNo[..12];
        }

        // 🔴 2026-08-09 봉합 (사장님 지적 · 작4 ⑤번) — 기본주소와 상세주소를 분리 저장한다.
        //   종전에는 두 값을 공백으로 합쳐 address 한 칸에 넣었는데, 조회 SQL 은 상세주소를 안 읽었다.
        //   그래서 새로고침하면 상세주소칸이 비었고, 다시 입력해 저장하면 중복 누적돼
        //   저장할 때마다 주소가 길어지다가 200자에서 잘렸다.
        //   DB-85 에서 address_detail 컬럼을 신설해 각자 제자리에 저장한다.
        var address = TruncateNullable(dto.Address, 200);
        var addressDetail = TruncateNullable(dto.AddressDetail, 200);

        var subsidiaryNo = dto.SubsidiaryNo?.Trim() ?? string.Empty;
        if (subsidiaryNo.Length > 4)
        {
            subsidiaryNo = subsidiaryNo[..4];
        }

        var corpNo = dto.CorpNo?.Trim();
        if (!string.IsNullOrEmpty(corpNo) && corpNo.Length > 13)
        {
            corpNo = corpNo[..13];
        }

        var tel = TruncateNullable(dto.Tel, 20);
        var fax = TruncateNullable(dto.Fax, 20);
        var email = TruncateNullable(dto.Email, 100);
        var homepage = TruncateNullable(dto.Homepage, 200);
        var zipCode = TruncateNullable(dto.ZipCode, 10);
        var bizType = TruncateNullable(dto.BizType, 50);
        var bizItem = TruncateNullable(dto.BizItem, 100);

        const string updateSql = """
            UPDATE local_company SET
              company_name = @CompanyName,
              ceo_name = @CeoName,
              biz_no = @BizNo,
              biz_type = @BizType,
              biz_item = @BizItem,
              tel = @Tel,
              fax = @Fax,
              email = @Email,
              homepage = @Homepage,
              zip_code = @ZipCode,
              address = @Address,
              address_detail = @AddressDetail,
              logo_url = @LogoUrl,
              seal_url = @SealUrl,
              header_url = @HeaderUrl,
              corp_no = @CorpNo,
              subsidiary_no = @SubsidiaryNo,
              updated_at = NOW(6)
            WHERE tenant_id = @TenantId
            """;

        await _db.ExecuteAsync(new CommandDefinition(
                updateSql,
                new
                {
                    TenantId = tenantId,
                    CompanyName = companyName,
                    CeoName = ceoName,
                    BizNo = bizNo,
                    BizType = bizType,
                    BizItem = bizItem,
                    Tel = tel,
                    Fax = fax,
                    Email = email,
                    Homepage = homepage,
                    ZipCode = zipCode,
                    Address = address,
                    AddressDetail = addressDetail,
                    LogoUrl = TruncateNullable(dto.LogoUrl, 200),
                    SealUrl = TruncateNullable(dto.SealUrl, 200),
                    HeaderUrl = TruncateNullable(dto.HeaderUrl, 200),
                    CorpNo = corpNo,
                    SubsidiaryNo = string.IsNullOrEmpty(subsidiaryNo) ? null : subsidiaryNo
                },
                cancellationToken: ct))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// local_company 테이블에서 사업장 기본 정보를 조회한다.
    /// </summary>
    public async Task<UpdateTenantCompanyDto?> GetCompanyAsync(string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        const string sql = """
            SELECT
              company_name AS CompanyName,
              ceo_name AS CeoName,
              biz_no AS BizNo,
              biz_type AS BizType,
              biz_item AS BizItem,
              tel AS Tel,
              fax AS Fax,
              email AS Email,
              homepage AS Homepage,
              zip_code AS ZipCode,
              address AS Address,
              address_detail AS AddressDetail,
              logo_url AS LogoUrl,
              seal_url AS SealUrl,
              header_url AS HeaderUrl,
              corp_no AS CorpNo,
              subsidiary_no AS SubsidiaryNo,
              is_locked_from_landing AS IsLockedFromLanding
            FROM local_company
            WHERE tenant_id = @TenantId
            """;
        return await _db.QueryFirstOrDefaultAsync<UpdateTenantCompanyDto>(
                new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// NULL 허용 문자열을 최대 길이로 잘라 local_company 컬럼 제약에 맞춘다.
    /// </summary>
    private static string? TruncateNullable(string? value, int maxLen)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var t = value.Trim();
        return t.Length <= maxLen ? t : t[..maxLen];
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
        ForceEditRequirePassword = true,
        StockEvalMethod = "moving_avg",
        UseMultiWarehouse = false,
        StockShortageAlert = true,
        AllowMinusStock = false,
        PriceInputType = "net",
        AutoVatAdjust = true,
        VatRoundType = "round",
        PriceARate = 1.00m,
        PriceBRate = 1.10m,
        PriceCRate = 1.20m,
        PriceDRate = 1.30m,
        PriceERate = 1.50m,
        UseCreditLimit = true,
        CreditLimitAmount = 1000000m,
        ShowPurchasePrice = false,
        UseSalesByEmployee = true,
        UsePersonalInfoProtect = true,
        IndustryType = "retail"
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

    private sealed class TenantCompanyRequiredRow
    {
        public string CompanyName { get; set; } = string.Empty;

        public string CeoName { get; set; } = string.Empty;

        public string BizNo { get; set; } = string.Empty;

        /// <summary>랜딩 가입 자동 반영 잠금. 참이면 위 3필드는 저장 시 변경을 거부한다(헌법 #35).</summary>
        public bool IsLockedFromLanding { get; set; }
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

        public string StockEvalMethod { get; set; } = "moving_avg";

        public long UseMultiWarehouse { get; set; }

        public long StockShortageAlert { get; set; } = 1;

        public long AllowMinusStock { get; set; }

        public string PriceInputType { get; set; } = "net";

        public long AutoVatAdjust { get; set; } = 1;

        public string VatRoundType { get; set; } = "round";

        public decimal PriceARate { get; set; } = 1.00m;

        public decimal PriceBRate { get; set; } = 1.10m;

        public decimal PriceCRate { get; set; } = 1.20m;

        public decimal PriceDRate { get; set; } = 1.30m;

        public decimal PriceERate { get; set; } = 1.50m;

        public long UseCreditLimit { get; set; } = 1;

        public decimal CreditLimitAmount { get; set; } = 1000000m;

        public long ShowPurchasePrice { get; set; }

        public long UseSalesByEmployee { get; set; } = 1;

        public long UsePersonalInfoProtect { get; set; } = 1;

        public string IndustryType { get; set; } = "retail";
    }
}
