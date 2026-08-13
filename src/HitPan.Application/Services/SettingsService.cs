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

        // 🔴 2026-08-11 전수조사 봉합 — 화면이 보내는 값을 서버가 몰라서 **조용히 바뀌던** 자리.
        //
        //   [재고 평가방법] 화면은 `이동평균법·표준단가·최종매입가` 세 가지를 준다
        //     (Settings.razor). 그런데 서버 허용목록은 `moving_avg·fifo·lifo` 였다.
        //     ⇒ 고객이 **표준단가나 최종매입가를 고르면** 목록에 없으니 맨 끝 기본값(이동평균법)으로
        //       되돌아갔다. 저장은 성공했다고 뜨고 값만 바뀌었다. 고객은 자기가 고른 대로
        //       계산되는 줄 안다.
        //
        //   ⇒ 화면 쪽이 옳다. 화면 낱말은 **사장님이 정한 업무 용어**이고,
        //     fifo·lifo 는 개발자가 임의로 넣은 것이다. 서버를 화면에 맞춘다.
        //   ⚠️ fifo·lifo 도 계속 받아준다 — 이미 그 값으로 저장된 자료가 있으면
        //     지우지 않기 위해서다(우리가 만든 규칙 때문에 기존 값이 날아가면 안 된다).
        var stockEvalMethod = NormalizeEnum(dto.StockEvalMethod,
            new[] { "moving_avg", "standard", "last_purchase", "fifo", "lifo" }, "moving_avg");

        //   [끝전 반올림] 화면은 켜면 `round`, 끄면 `truncate` 를 보낸다.
        //     서버 목록에 `truncate` 가 없어서 **끄면 다시 켜졌다** — 토글이 안 먹었다.
        //     `truncate`(끝을 버린다)와 `floor`(내림)는 양수에서 결과가 같다.
        var vatRoundType = NormalizeEnum(dto.VatRoundType,
            new[] { "round", "truncate", "floor", "ceil" }, "round");
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
    public async Task<IReadOnlyList<string>> SaveCompanyAsync(UpdateTenantCompanyDto dto, string tenantId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // NOT NULL 컬럼(상호·대표·사업자번호)은 빈 값이 오면 DB 기존 값을 유지한다.
        // is_locked_from_landing 도 함께 읽는다 — 잠금 시 아래 8필드는 서버가 변경을 거부한다.
        //
        // 🔴 2026-08-10 확장 (사장님 결재 · 20260810작1):
        //   종전엔 상호·대표자·사업자번호 3필드만 되돌렸다. 그런데 사업자등록증에 적힌 값은
        //   업태·종목·주소·우편번호·법인등록번호까지 8개다. 나머지 5개는 잠금 밖이라
        //   정상 가입한 뒤 이 5개만 바꿔 다른 사업장인 것처럼 쓸 수 있었다(무단사용 경로).
        //   ⇒ 등록증에서 읽은 값 전부를 잠근다. 등록증에 없는 값(전화·팩스·홈페이지·이메일·
        //      상세주소·종사업장번호)은 대조할 원본이 없으므로 잠그지 않는다.
        const string selectRequired = """
            SELECT
              company_name AS CompanyName,
              ceo_name AS CeoName,
              biz_no AS BizNo,
              biz_type AS BizType,
              biz_item AS BizItem,
              zip_code AS ZipCode,
              address AS Address,
              corp_no AS CorpNo,
              is_locked_from_landing AS IsLockedFromLanding
            FROM local_company
            WHERE tenant_id = @TenantId
            """;

        var required = await _db.QueryFirstOrDefaultAsync<TenantCompanyRequiredRow>(
                new CommandDefinition(selectRequired, new { TenantId = tenantId }, cancellationToken: ct))
            .ConfigureAwait(false);

        if (required is null)
        {
            return Array.Empty<string>();
        }

        // 잠겨 있어 저장하지 않은 항목을 모아 호출자에게 돌려준다.
        // 조용히 무시하면 고객은 "고쳤는데 안 바뀐다" 는 상태에 빠지고 원인도 알 수 없다.
        var rejected = new List<string>();

        // 🔴 랜딩 잠금 강제 (헌법 #35 · 작4 §2-6-2 봉합).
        //   종전에는 화면에서만 ReadOnly 로 막았고 서버엔 검증이 없었다.
        //   화면 잠금은 화면을 거치지 않는 요청을 못 막는다 — 서버가 최종 관문이다.
        //   잠긴 테넌트는 사업자등록증 기재 항목을 무조건 DB 기존 값으로 되돌린다.
        //   (변경이 필요하면 [기본 사업장 정보 변경 요청] 으로 본사 승인을 받는다.)
        var lockedFromLanding = required.IsLockedFromLanding;

        // 🔴 2026-08-10 (사장님 결재 · 20260810작1 T7) — 잠금 대상을 3필드 → 8필드로 확장.
        //   사업자등록증에 적힌 값 전부를 잠근다. 종전엔 업태·종목·주소·우편번호·법인등록번호가
        //   잠금 밖이라, 정상 가입한 뒤 이 5개만 바꿔 다른 사업장처럼 쓸 수 있었다(무단사용 경로).
        //
        //   ⚠️ 단 "값이 이미 있을 때만" 잠근다.
        //   기존 값이 비어 있는 채로 잠그면 고객이 영원히 못 채우는 칸이 된다.
        //   실제로 지금 부트스트랩은 주소·업태·종목을 안 채워 보내는 경우가 있어(수집 자체가
        //   해시만 했다) 그 칸들이 빈 상태다 — 여기서 무조건 잠그면 화면이 잠긴 빈칸이 된다.
        //   ⇒ 백오피스 수집(T2)이 값을 채워 주기 전까지는 빈 칸을 고객이 채울 수 있게 둔다.
        //      값이 한 번 들어오면 그때부터 잠긴다.
        string? LockIfPresent(bool locked, string? current, string? incoming, string fieldLabel)
        {
            if (!locked || string.IsNullOrWhiteSpace(current))
            {
                return incoming;
            }

            // 실제로 다른 값을 보내온 경우에만 "거부" 로 기록한다.
            // 화면이 잠긴 값을 그대로 되돌려 보내는 것은 정상이므로 알릴 필요가 없다.
            if (!string.IsNullOrWhiteSpace(incoming)
                && !string.Equals(current.Trim(), incoming.Trim(), StringComparison.Ordinal))
            {
                rejected.Add(fieldLabel);
            }

            return current;
        }

        var companyName = LockIfPresent(lockedFromLanding, required.CompanyName, dto.CompanyName, "사용업체명")
            is { } cn && !string.IsNullOrWhiteSpace(cn) ? cn.Trim() : required.CompanyName;
        var ceoName = LockIfPresent(lockedFromLanding, required.CeoName, dto.CeoName, "대표자")
            is { } ceo && !string.IsNullOrWhiteSpace(ceo) ? ceo.Trim() : required.CeoName;
        var bizNo = LockIfPresent(lockedFromLanding, required.BizNo, dto.BizNo, "사업자번호")
            is { } bn && !string.IsNullOrWhiteSpace(bn) ? bn.Trim() : required.BizNo;
        if (bizNo.Length > 12)
        {
            bizNo = bizNo[..12];
        }

        // 🔴 2026-08-09 봉합 (사장님 지적 · 작4 ⑤번) — 기본주소와 상세주소를 분리 저장한다.
        //   종전에는 두 값을 공백으로 합쳐 address 한 칸에 넣었는데, 조회 SQL 은 상세주소를 안 읽었다.
        //   그래서 새로고침하면 상세주소칸이 비었고, 다시 입력해 저장하면 중복 누적돼
        //   저장할 때마다 주소가 길어지다가 200자에서 잘렸다.
        //   DB-85 에서 address_detail 컬럼을 신설해 각자 제자리에 저장한다.
        //   주소는 잠금 대상(등록증 소재지), 상세주소는 잠금 제외(층·호수는 등록증에 없을 수 있다).
        var address = TruncateNullable(LockIfPresent(lockedFromLanding, required.Address, dto.Address, "사업장 주소"), 200);
        var addressDetail = TruncateNullable(dto.AddressDetail, 200);

        var subsidiaryNo = dto.SubsidiaryNo?.Trim() ?? string.Empty;
        if (subsidiaryNo.Length > 4)
        {
            subsidiaryNo = subsidiaryNo[..4];
        }

        //   법인등록번호는 잠금 대상(등록증 기재).
        var corpNo = LockIfPresent(lockedFromLanding, required.CorpNo, dto.CorpNo, "법인등록번호")?.Trim();
        if (!string.IsNullOrEmpty(corpNo) && corpNo.Length > 13)
        {
            corpNo = corpNo[..13];
        }

        var tel = TruncateNullable(dto.Tel, 20);
        var fax = TruncateNullable(dto.Fax, 20);
        var email = TruncateNullable(dto.Email, 100);
        var homepage = TruncateNullable(dto.Homepage, 200);
        //   우편번호·업태·종목은 잠금 대상(등록증 기재). 전화·팩스·이메일·홈페이지는 제외 —
        //   등록증에 없는 값이라 대조할 원본이 없다(연락처를 고정하는 것이 목적이 아니다).
        var zipCode = TruncateNullable(LockIfPresent(lockedFromLanding, required.ZipCode, dto.ZipCode, "우편번호"), 10);
        var bizType = TruncateNullable(LockIfPresent(lockedFromLanding, required.BizType, dto.BizType, "업태"), 50);
        var bizItem = TruncateNullable(LockIfPresent(lockedFromLanding, required.BizItem, dto.BizItem, "업종"), 100);

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
              -- 작(2026-08-13) 단계4 토대 — 사업장 노무 정보.
              -- 🔴 tax_type 은 컬럼만 있고 저장 경로가 없어 늘 기본값이었다. 여기서 잇는다.
              tax_type = @TaxType,
              regular_employee_count = @RegularEmployeeCount,
              business_entity_type = @BusinessEntityType,
              employee_count_asof = @EmployeeCountAsOf,
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
                    SubsidiaryNo = string.IsNullOrEmpty(subsidiaryNo) ? null : subsidiaryNo,
                    // 작(2026-08-13) 단계4 — 사업장 노무 정보.
                    // 🔴 정해진 값만 받는다. 아무 문자열이나 들어가면 나중에 판정하는 쪽이
                    //    모르는 값을 만나 조용히 기본값으로 흘러간다(emp_type 이 겪은 그 병).
                    TaxType = NormalizeChoice(dto.TaxType, "taxable", "tax_free"),
                    BusinessEntityType = NormalizeChoice(dto.BusinessEntityType, "corporate", "individual"),
                    // 음수·비현실적인 값은 막는다. null 은 '미정' 이라 그대로 둔다.
                    RegularEmployeeCount = dto.RegularEmployeeCount is > 0 and <= 1_000_000
                        ? dto.RegularEmployeeCount
                        : null,
                    EmployeeCountAsOf = dto.EmployeeCountAsOf
                },
                cancellationToken: ct))
            .ConfigureAwait(false);

        // 거부 목록은 호출자(컨트롤러)가 응답과 로그로 알린다.
        // 이 서비스에는 로거가 주입되어 있지 않아 여기서 기록하지 않는다.
        return rejected;
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
              -- 작(2026-08-13) 단계4 토대 — 노무 판정에 쓰는 사업장 정보.
              -- 🔴 tax_type 은 컬럼이 있는데도 SELECT·UPDATE·화면 어디에도 없어
              --    값을 넣을 방법이 없었다(늘 기본값 'taxable'). 여기서 살린다.
              tax_type AS TaxType,
              regular_employee_count AS RegularEmployeeCount,
              business_entity_type AS BusinessEntityType,
              employee_count_asof AS EmployeeCountAsOf,
              is_locked_from_landing AS IsLockedFromLanding
            FROM local_company
            WHERE tenant_id = @TenantId
            """;
        return await _db.QueryFirstOrDefaultAsync<UpdateTenantCompanyDto>(
                new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: ct))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 정해진 선택지 중 하나인지 본다. 아니면 <c>null</c>(미정)로 돌린다.
    /// </summary>
    /// <remarks>
    /// 🔴 작(2026-08-13) 단계4. 아무 문자열이나 저장되면, 나중에 이 값으로 연차·보험을
    /// 판정하는 쪽이 모르는 값을 만나 <b>조용히 기본값으로 흘러간다</b>.
    /// <c>emp_type</c> 이 정확히 그 병을 갖고 있다 — 유령값이 들어와도
    /// <c>ParseEmpType</c> 이 말없이 <c>Regular</c> 로 바꿔 화면엔 정상으로 보인다.
    /// 여기서는 <b>모르는 값을 받지 않는다</b>. 미정은 미정으로 남긴다.
    /// </remarks>
    private static string? NormalizeChoice(string? value, params string[] allowed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var t = value.Trim().ToLowerInvariant();
        return Array.Exists(allowed, a => a == t) ? t : null;
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

        // 🔴 2026-08-10 (20260810작1 T7) — 잠금 대상 확장분.
        //   사업자등록증에 적힌 값이라 잠근다. 위 3개와 달리 NULL 허용이므로 string? 이다.
        public string? BizType { get; set; }

        public string? BizItem { get; set; }

        public string? ZipCode { get; set; }

        public string? Address { get; set; }

        public string? CorpNo { get; set; }

        /// <summary>
        /// 사업자등록증 자동 반영 잠금. 참이면 등록증 기재 8필드
        /// (상호·대표자·사업자번호·업태·종목·우편번호·주소·법인등록번호)는 저장 시 변경을 거부한다.
        /// 단 기존 값이 비어 있는 칸은 잠그지 않는다 — 영원히 못 채우는 칸이 되기 때문이다.
        /// 헌법 #35 · 무단사용 차단 2관문(20260810작1 §1-1).
        /// </summary>
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
