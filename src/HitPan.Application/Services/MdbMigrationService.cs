using System.Data;
using System.Data.Common;
using System.Data.OleDb;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using Dapper;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// 레거시 히트판(VB6 + Access MDB) → 신규 히트판(MariaDB) 데이터 마이그레이션 서비스이다.
/// 3개의 MDB 파일(PYOJUN, PANDATA, POTHER)을 읽어 신규 스키마에 맞게 변환·INSERT한다.
/// Windows 전용: Microsoft.ACE.OLEDB.12.0 Provider 필요.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MdbMigrationService
{
    private readonly IDbConnection _db;
    private readonly ILogger<MdbMigrationService> _logger;

    /// <summary>OLEDB 커넥션 문자열 템플릿 (MDB 경로를 채워 넣는다)</summary>
    private const string OleDbConnTemplate =
        "Provider=Microsoft.ACE.OLEDB.12.0;Data Source={0};Jet OLEDB:Database Password=;";

    public MdbMigrationService(IDbConnection db, ILogger<MdbMigrationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ────────────────────────────────────────────────────────────────
    // 공개 메서드: 마이그레이션 실행
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// MDB 폴더 경로로 마이그레이션 (폴더 안에서 PYOJUN/PANDATA/POTHER 자동 탐색).
    /// </summary>
    public async Task<MdbMigrationResult> MigrateAsync(
        string folderPath, string tenantId, CancellationToken ct = default)
    {
        var (pyojunPath, pandataPath, _) = ResolveMdbPaths(folderPath);
        return await MigrateAsync(pyojunPath, pandataPath, tenantId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// MDB 폴더 내 테이블 건수 미리보기 (실제 import 없음).
    /// </summary>
    public Task<Dictionary<string, int>> PreviewAsync(
        string folderPath, string tenantId, CancellationToken ct = default)
    {
        var (pyojunPath, pandataPath, potherPath) = ResolveMdbPaths(folderPath);
        var result = new Dictionary<string, int>();

        // PYOJUN.MDB
        if (File.Exists(pyojunPath))
        {
            using var oleConn = OpenOleDb(pyojunPath);
            result["DOCF8 (업체마스터)"] = CountMdbTable(oleConn, "DOCF8");
            result["DOCFS (상품마스터)"] = CountMdbTable(oleConn, "DOCFS");
            result["DOCRT (BOM)"] = CountMdbTable(oleConn, "DOCRT");
            result["DOCSW (사원)"] = CountMdbTable(oleConn, "DOCSW");
            result["COSTNO (비용코드)"] = CountMdbTable(oleConn, "COSTNO");
        }

        // PANDATA.mdb
        if (File.Exists(pandataPath))
        {
            using var oleConn = OpenOleDb(pandataPath);
            result["DOCF2 (거래헤더)"] = CountMdbTable(oleConn, "DOCF2");
            result["DOCF1 (거래상세)"] = CountMdbTable(oleConn, "DOCF1");
            result["DOCFB (입출고)"] = CountMdbTable(oleConn, "DOCFB");
            result["DOCF4 (세금계산서)"] = CountMdbTable(oleConn, "DOCF4");
            result["DOCF5 (수금)"] = CountMdbTable(oleConn, "DOCF5");
            result["DOCF6 (경비)"] = CountMdbTable(oleConn, "DOCF6");
            result["DOCF7 (전표)"] = CountMdbTable(oleConn, "DOCF7");
            result["DOCFA (매입상세)"] = CountMdbTable(oleConn, "DOCFA");
            result["DOCFO (발주상세)"] = CountMdbTable(oleConn, "DOCFO");
        }

        // POTHER.mdb
        if (File.Exists(potherPath))
        {
            using var oleConn = OpenOleDb(potherPath);
            result["DOCNM (명함)"] = CountMdbTable(oleConn, "DOCNM");
            result["DOCAS (AS)"] = CountMdbTable(oleConn, "DOCAS");
            result["DELIVERY (배송)"] = CountMdbTable(oleConn, "DELIVERY");
            result["CALENDAR (달력)"] = CountMdbTable(oleConn, "CALENDAR");
        }

        return Task.FromResult(result);
    }

    /// <summary>
    /// 3개 MDB 파일을 읽어 지정 tenant_id로 MariaDB에 마이그레이션한다.
    /// </summary>
    private async Task<MdbMigrationResult> MigrateAsync(
        string pyojunPath,
        string pandataPath,
        string tenantId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pyojunPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(pandataPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var result = new MdbMigrationResult();
        var now = DateTime.UtcNow;

        // FK 매핑용 딕셔너리 (레거시 코드 → 신규 UUID)
        // 업체: buy_code(Int32) → partner_id(UUID)
        var partnerMap = new Dictionary<int, string>();
        // 상품: "품명|규격" → item_id(UUID)
        var itemMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // 사원: SW_NAME → employee_id(UUID)
        var employeeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // 기본 창고 ID (신규 시스템의 기본 창고)
        var defaultWarehouseId = "wh-migration";

        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // 전체 트랜잭션으로 감싸기
        IDbTransaction? tx = null;
        try
        {
            tx = _db.BeginTransaction();

            // ──────────────────────────────────────
            // 0. 마이그레이션 전용 기본 창고 생성
            // ──────────────────────────────────────
            await EnsureMigrationWarehouseAsync(tenantId, defaultWarehouseId, now, tx, ct).ConfigureAwait(false);

            // ──────────────────────────────────────
            // 1단계: PYOJUN.MDB (마스터 데이터)
            // ──────────────────────────────────────
            _logger.LogInformation("[MDB마이그레이션] PYOJUN.MDB 읽기 시작: {Path}", pyojunPath);

            using (var oleConn = OpenOleDb(pyojunPath))
            {
                // 1-1. 업체마스터 (DOCF8 → partners)
                result.Partners = await MigratePartnersAsync(oleConn, tenantId, now, partnerMap, tx, ct).ConfigureAwait(false);

                // 1-2. 상품마스터 (DOCFS → items)
                result.Items = await MigrateItemsAsync(oleConn, tenantId, now, itemMap, tx, ct).ConfigureAwait(false);

                // 1-3. BOM (DOCRT → bom_headers + bom_items)
                result.BomHeaders = await MigrateBomAsync(oleConn, tenantId, now, itemMap, tx, ct).ConfigureAwait(false);

                // 1-4. 사원 (DOCSW → employees)
                result.Employees = await MigrateEmployeesAsync(oleConn, tenantId, now, employeeMap, tx, ct).ConfigureAwait(false);
            }

            // ──────────────────────────────────────
            // 2단계: PANDATA.mdb (거래 데이터)
            // ──────────────────────────────────────
            _logger.LogInformation("[MDB마이그레이션] PANDATA.mdb 읽기 시작: {Path}", pandataPath);

            using (var oleConn = OpenOleDb(pandataPath))
            {
                // 2-1. 거래(판매/매입) 헤더·상세 (DOCF2/DOCF1)
                var (salesCount, purchaseCount) = await MigrateTransactionsAsync(
                    oleConn, tenantId, now, partnerMap, itemMap, employeeMap, defaultWarehouseId, tx, ct).ConfigureAwait(false);
                result.SalesOrders = salesCount;
                result.PurchaseOrders = purchaseCount;

                // 2-2. 매입매출 입출고 (DOCFB → stock_ledger)
                result.StockLedger = await MigrateStockLedgerAsync(
                    oleConn, tenantId, now, partnerMap, itemMap, defaultWarehouseId, tx, ct).ConfigureAwait(false);

                // 2-3. 수금 (DOCF5 → collections)
                result.Collections = await MigrateCollectionsAsync(
                    oleConn, tenantId, now, partnerMap, tx, ct).ConfigureAwait(false);

                // 2-4. 경비 (DOCF6 → cashbook)
                result.Cashbook = await MigrateCashbookAsync(
                    oleConn, tenantId, now, partnerMap, tx, ct).ConfigureAwait(false);

                // 2-5. 전표 (DOCF7 → expenses 또는 cashbook 보조)
                result.Expenses = await MigrateExpensesAsync(
                    oleConn, tenantId, now, employeeMap, tx, ct).ConfigureAwait(false);
            }

            tx.Commit();
            _logger.LogInformation("[MDB마이그레이션] 완료. 결과: {@Result}", result);
        }
        catch
        {
            tx?.Rollback();
            throw;
        }
        finally
        {
            tx?.Dispose();
        }

        return result;
    }

    // ────────────────────────────────────────────────────────────────
    // 마이그레이션 전용 기본 창고 생성
    // ────────────────────────────────────────────────────────────────

    /// <summary>마이그레이션 데이터가 들어갈 기본 창고가 없으면 생성한다.</summary>
    private async Task EnsureMigrationWarehouseAsync(
        string tenantId, string warehouseId, DateTime now, IDbTransaction tx, CancellationToken ct)
    {
        const string checkSql = "SELECT COUNT(*) FROM warehouses WHERE warehouse_id = @Id AND tenant_id = @TenantId";
        var exists = await _db.ExecuteScalarAsync<int>(
            new CommandDefinition(checkSql, new { Id = warehouseId, TenantId = tenantId },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        if (exists > 0) return;

        const string sql = """
            INSERT INTO warehouses (warehouse_id, tenant_id, wh_code, wh_name, wh_type, location, is_active, created_at, updated_at)
            VALUES (@Id, @TenantId, 'WH-MIG', '마이그레이션창고', 'normal', '레거시 데이터 이관용', 1, @Now, @Now)
            """;
        await _db.ExecuteAsync(new CommandDefinition(sql,
            new { Id = warehouseId, TenantId = tenantId, Now = now },
            transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
    }

    // ────────────────────────────────────────────────────────────────
    // 1-1. 업체마스터 마이그레이션 (DOCF8 → partners)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// DOCF8(업체마스터)를 읽어 partners 테이블에 INSERT한다.
    /// buy_code → partner_id 매핑을 partnerMap에 저장한다.
    /// </summary>
    private async Task<int> MigratePartnersAsync(
        OleDbConnection oleConn, string tenantId, DateTime now,
        Dictionary<int, string> partnerMap, IDbTransaction tx, CancellationToken ct)
    {
        var dt = ReadMdbTable(oleConn, "SELECT * FROM DOCF8");
        if (dt.Rows.Count == 0) return 0;

        const string sql = """
            INSERT INTO partners
              (partner_id, tenant_id, partner_code, partner_name, partner_type,
               biz_no, ceo_name, biz_type, biz_item,
               tel, fax, address, address_detail, zip_code,
               credit_limit, bank_name, bank_account, account_holder,
               manager_name, manager_tel, tax_type, memo,
               is_active, is_deleted, created_at, updated_at, price_grade, row_version)
            VALUES
              (@PartnerId, @TenantId, @PartnerCode, @PartnerName, @PartnerType,
               @BizNo, @CeoName, @BizType, @BizItem,
               @Tel, @Fax, @Address, @AddressDetail, @ZipCode,
               @CreditLimit, @BankName, @BankAccount, @AccountHolder,
               @ManagerName, @ManagerTel, @TaxType, @Memo,
               1, 0, @Now, @Now, @PriceGrade, 0)
            """;

        int count = 0;
        foreach (DataRow row in dt.Rows)
        {
            var buyCode = GetInt(row, "buy_code");
            var partnerId = Guid.NewGuid().ToString();

            // buy_code → partner_id 매핑 저장 (이후 거래 FK 참조용)
            partnerMap[buyCode] = partnerId;

            // buy_gu(구분): "1"=매입처, "2"=매출처, 그 외=양쪽
            var buyGu = GetStr(row, "buy_gu");
            var partnerType = buyGu switch
            {
                "1" => "supplier",
                "2" => "customer",
                _ => "both"
            };

            // buy_taxgubun(세금구분) 변환
            var taxGubun = GetStr(row, "buy_taxgubun");
            var taxType = taxGubun switch
            {
                "1" or "과세" => "taxable",
                "2" or "면세" => "exempt",
                "3" or "영세" => "zero_rate",
                _ => "taxable"
            };

            // buy_rem~rem6 비고 합치기
            var memoBuilder = new StringBuilder();
            for (int i = 0; i <= 6; i++)
            {
                var colName = i == 0 ? "buy_rem" : $"buy_rem{i}";
                var val = GetStr(row, colName);
                if (!string.IsNullOrWhiteSpace(val))
                {
                    if (memoBuilder.Length > 0) memoBuilder.Append(" | ");
                    memoBuilder.Append(val);
                }
            }

            await _db.ExecuteAsync(new CommandDefinition(sql, new
            {
                PartnerId = partnerId,
                TenantId = tenantId,
                PartnerCode = $"MIG-{buyCode:D5}",  // 레거시 코드 기반 partner_code 생성
                PartnerName = GetStr(row, "buy_name"),
                PartnerType = partnerType,
                BizNo = GetStr(row, "buy_taxno"),          // 사업자번호
                CeoName = GetStr(row, "buy_top"),           // 대표자
                BizType = GetStr(row, "buy_euptae"),        // 업태
                BizItem = GetStr(row, "buy_eupjong"),       // 업종
                Tel = GetStr(row, "buy_tel"),
                Fax = GetStr(row, "buy_fax"),
                Address = GetStr(row, "buy_addr"),
                AddressDetail = GetStr(row, "buy_addr1"),
                ZipCode = GetStr(row, "buy_postno"),
                CreditLimit = GetDec(row, "buy_yeasin"),    // 여신한도
                BankName = GetStr(row, "buy_bank"),
                BankAccount = GetStr(row, "buy_bankno"),
                AccountHolder = GetStr(row, "buy_bankname"),
                ManagerName = GetStr(row, "buy_damdang"),   // 담당자
                ManagerTel = GetStr(row, "buy_damdang1"),
                TaxType = taxType,
                Memo = memoBuilder.Length > 0 ? memoBuilder.ToString() : (string?)null,
                PriceGrade = "A",
                Now = now
            }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            count++;
        }

        _logger.LogInformation("[MDB마이그레이션] 업체 {Count}건 이관 완료", count);
        return count;
    }

    // ────────────────────────────────────────────────────────────────
    // 1-2. 상품마스터 마이그레이션 (DOCFS → items)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// DOCFS(상품마스터)를 읽어 items 테이블에 INSERT한다.
    /// "품명|규격" → item_id 매핑을 itemMap에 저장한다.
    /// </summary>
    private async Task<int> MigrateItemsAsync(
        OleDbConnection oleConn, string tenantId, DateTime now,
        Dictionary<string, string> itemMap, IDbTransaction tx, CancellationToken ct)
    {
        var dt = ReadMdbTable(oleConn, "SELECT * FROM DOCFS");
        if (dt.Rows.Count == 0) return 0;

        const string sql = """
            INSERT INTO items
              (item_id, tenant_id, item_code, item_name, item_type, unit, spec,
               purchase_price, sale_price, standard_price, cost_price, std_price,
               price_a, price_b, price_c, price_d, price_e,
               tax_type, barcode, item_group, memo,
               is_active, is_deleted, safety_stock, created_at, updated_at, row_version)
            VALUES
              (@ItemId, @TenantId, @ItemCode, @ItemName, 'product', @Unit, @Spec,
               @PurchasePrice, @SalePrice, @StandardPrice, @CostPrice, @StdPrice,
               @PriceA, @PriceB, @PriceC, @PriceD, @PriceE,
               @TaxType, @Barcode, @ItemGroup, @Memo,
               1, 0, 0, @Now, @Now, 0)
            """;

        int count = 0;
        int seq = 1;
        foreach (DataRow row in dt.Rows)
        {
            var pumName = GetStr(row, "S_PUM");   // 품명
            var spec = GetStr(row, "S_KU");        // 규격

            // 품명이 비어있으면 건너뜀
            if (string.IsNullOrWhiteSpace(pumName)) continue;

            var itemId = Guid.NewGuid().ToString();
            var itemKey = BuildItemKey(pumName, spec);

            // 동일 품명+규격 중복 시 첫 번째만 사용
            if (!itemMap.TryAdd(itemKey, itemId)) continue;

            // S_TAX(과세구분) 변환
            var sTax = GetStr(row, "S_TAX");
            var taxType = sTax switch
            {
                "1" or "과세" => "taxable",
                "2" or "면세" => "exempt",
                "3" or "영세" => "zero_rate",
                _ => "taxable"
            };

            var purchasePrice = GetDec(row, "S_IDAN");    // 매입단가
            var salePrice = GetDec(row, "S_PDAN");         // 판매단가
            var costPrice = GetDec(row, "S_JEK");          // 재고단가

            await _db.ExecuteAsync(new CommandDefinition(sql, new
            {
                ItemId = itemId,
                TenantId = tenantId,
                ItemCode = $"MIG-{seq:D5}",   // 자동 생성 item_code
                ItemName = pumName,
                Unit = string.IsNullOrWhiteSpace(GetStr(row, "S_DANW")) ? "EA" : GetStr(row, "S_DANW"),
                Spec = spec,
                PurchasePrice = purchasePrice,
                SalePrice = salePrice,
                StandardPrice = salePrice,
                CostPrice = costPrice,
                StdPrice = salePrice,
                PriceA = GetDec(row, "S_PDANA"),   // 판매단가A
                PriceB = GetDec(row, "S_PDANB"),   // 판매단가B
                PriceC = GetDec(row, "S_PDANC"),   // 판매단가C
                PriceD = GetDec(row, "S_PDAND"),   // 판매단가D
                PriceE = GetDec(row, "S_PDANE"),   // 판매단가E
                TaxType = taxType,
                Barcode = GetStr(row, "S_BARCODE"),
                ItemGroup = GetStr(row, "S_CCODE"),   // 분류코드 → item_group
                Memo = GetStr(row, "S_DESC"),          // 설명 → memo
                Now = now
            }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            count++;
            seq++;
        }

        _logger.LogInformation("[MDB마이그레이션] 상품 {Count}건 이관 완료", count);
        return count;
    }

    // ────────────────────────────────────────────────────────────────
    // 1-3. BOM 마이그레이션 (DOCRT → bom_headers + bom_items)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// DOCRT(BOM)를 읽어 bom_headers + bom_items 테이블에 INSERT한다.
    /// 완성품(RT_PUM+RT_KU) 기준으로 헤더를 그룹핑하고, 자재별로 상세를 생성한다.
    /// </summary>
    private async Task<int> MigrateBomAsync(
        OleDbConnection oleConn, string tenantId, DateTime now,
        Dictionary<string, string> itemMap, IDbTransaction tx, CancellationToken ct)
    {
        var dt = ReadMdbTable(oleConn, "SELECT * FROM DOCRT ORDER BY RT_PUM, RT_KU, RT_SUN");
        if (dt.Rows.Count == 0) return 0;

        const string headerSql = """
            INSERT INTO bom_headers (bom_id, tenant_id, product_item_id, bom_name, bom_version, is_default, is_active, memo, created_at, updated_at)
            VALUES (@BomId, @TenantId, @ProductItemId, @BomName, 1, 1, 1, '레거시 MDB에서 이관된 BOM', @Now, @Now)
            """;

        const string itemSql = """
            INSERT INTO bom_items (bom_item_id, bom_id, tenant_id, seq_no, material_item_id, qty, unit, loss_rate, memo)
            VALUES (@BomItemId, @BomId, @TenantId, @SeqNo, @MaterialItemId, @Qty, 'EA', @LossRate, @Memo)
            """;

        // 완성품 기준으로 그룹핑 (RT_PUM + RT_KU)
        var groups = new Dictionary<string, List<DataRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (DataRow row in dt.Rows)
        {
            var key = BuildItemKey(GetStr(row, "RT_PUM"), GetStr(row, "RT_KU"));
            if (!groups.ContainsKey(key)) groups[key] = new List<DataRow>();
            groups[key].Add(row);
        }

        int headerCount = 0;
        foreach (var (productKey, details) in groups)
        {
            // 완성품이 items에 없으면 BOM 생성 불가 → 건너뜀
            if (!itemMap.TryGetValue(productKey, out var productItemId))
            {
                _logger.LogWarning("[MDB마이그레이션] BOM 완성품 매핑 실패: {Key}", productKey);
                continue;
            }

            var bomId = Guid.NewGuid().ToString();
            var bomName = $"MIG-BOM-{GetStr(details[0], "RT_PUM")}";

            await _db.ExecuteAsync(new CommandDefinition(headerSql, new
            {
                BomId = bomId,
                TenantId = tenantId,
                ProductItemId = productItemId,
                BomName = bomName.Length > 100 ? bomName[..100] : bomName,
                Now = now
            }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            // 자재 상세 INSERT
            foreach (var detail in details)
            {
                var materialKey = BuildItemKey(GetStr(detail, "RT_RPUM"), GetStr(detail, "RT_RKU"));
                if (!itemMap.TryGetValue(materialKey, out var materialItemId))
                {
                    _logger.LogWarning("[MDB마이그레이션] BOM 자재 매핑 실패: {Key}", materialKey);
                    continue;
                }

                await _db.ExecuteAsync(new CommandDefinition(itemSql, new
                {
                    BomItemId = Guid.NewGuid().ToString(),
                    BomId = bomId,
                    TenantId = tenantId,
                    SeqNo = GetShort(detail, "RT_SUN"),
                    MaterialItemId = materialItemId,
                    Qty = GetDec(detail, "RT_UNIT"),       // 소요량
                    LossRate = GetDec(detail, "RT_ABS"),    // 로스율
                    Memo = GetStr(detail, "RT_GU")
                }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            }

            headerCount++;
        }

        _logger.LogInformation("[MDB마이그레이션] BOM {Count}건 이관 완료", headerCount);
        return headerCount;
    }

    // ────────────────────────────────────────────────────────────────
    // 1-4. 사원 마이그레이션 (DOCSW → employees)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// DOCSW(사원)를 읽어 employees 테이블에 INSERT한다.
    /// SW_NAME → employee_id 매핑을 employeeMap에 저장한다.
    /// </summary>
    private async Task<int> MigrateEmployeesAsync(
        OleDbConnection oleConn, string tenantId, DateTime now,
        Dictionary<string, string> employeeMap, IDbTransaction tx, CancellationToken ct)
    {
        var dt = ReadMdbTable(oleConn, "SELECT * FROM DOCSW");
        if (dt.Rows.Count == 0) return 0;

        const string sql = """
            INSERT INTO employees
              (employee_id, tenant_id, emp_no, emp_name, position, job_title, emp_type,
               join_date, phone, email, is_active, created_at, updated_at, role)
            VALUES
              (@EmployeeId, @TenantId, @EmpNo, @EmpName, @Position, @JobTitle, 'regular',
               @JoinDate, @Phone, NULL, 1, @Now, @Now, 'sales_user')
            """;

        int count = 0;
        int seq = 1;
        foreach (DataRow row in dt.Rows)
        {
            var name = GetStr(row, "SW_NAME");
            if (string.IsNullOrWhiteSpace(name)) continue;

            var employeeId = Guid.NewGuid().ToString();

            // 동일 이름 중복 시 첫 번째만 사용 (레거시는 이름 기반 참조)
            employeeMap.TryAdd(name, employeeId);

            var joinDate = ParseLegacyDate(GetStr(row, "SW_IBSAIL")) ?? now;

            await _db.ExecuteAsync(new CommandDefinition(sql, new
            {
                EmployeeId = employeeId,
                TenantId = tenantId,
                EmpNo = $"MIG-{seq:D4}",
                EmpName = name,
                Position = GetStr(row, "SW_JIKKUB"),       // 직급
                JobTitle = GetStr(row, "SW_JIKCHAK"),      // 직책
                JoinDate = joinDate,
                Phone = GetStr(row, "SW_HP"),               // 핸드폰 우선, 없으면 전화
                Now = now
            }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            count++;
            seq++;
        }

        _logger.LogInformation("[MDB마이그레이션] 사원 {Count}건 이관 완료", count);
        return count;
    }

    // ────────────────────────────────────────────────────────────────
    // 2-1. 거래 마이그레이션 (DOCF2 + DOCF1 → sales_orders/purchase_orders + items)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// DOCF2(거래헤더) + DOCF1(거래상세)를 읽어
    /// K2_GUBUN="S" → sales_orders + sales_order_items,
    /// K2_GUBUN="B" → purchase_orders + purchase_order_items 에 INSERT한다.
    /// </summary>
    private async Task<(int SalesCount, int PurchaseCount)> MigrateTransactionsAsync(
        OleDbConnection oleConn, string tenantId, DateTime now,
        Dictionary<int, string> partnerMap,
        Dictionary<string, string> itemMap,
        Dictionary<string, string> employeeMap,
        string defaultWarehouseId,
        IDbTransaction tx, CancellationToken ct)
    {
        // 헤더 로드
        var headerDt = ReadMdbTable(oleConn, "SELECT * FROM DOCF2");
        // 상세 로드
        var detailDt = ReadMdbTable(oleConn, "SELECT * FROM DOCF1");

        if (headerDt.Rows.Count == 0) return (0, 0);

        // 상세를 전표번호 기준으로 그룹핑
        var detailsByNo = new Dictionary<string, List<DataRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (DataRow row in detailDt.Rows)
        {
            var no = GetStr(row, "KA_NO");
            if (string.IsNullOrWhiteSpace(no)) continue;
            if (!detailsByNo.ContainsKey(no)) detailsByNo[no] = new List<DataRow>();
            detailsByNo[no].Add(row);
        }

        // 판매 주문 INSERT SQL
        const string soSql = """
            INSERT INTO sales_orders
              (order_id, tenant_id, order_no, partner_id, employee_id, order_date,
               status, total_amount, vat_amount, memo, created_at, updated_at, is_deleted)
            VALUES
              (@OrderId, @TenantId, @OrderNo, @PartnerId, @EmployeeId, @OrderDate,
               'draft', @TotalAmount, @VatAmount, @Memo, @Now, @Now, 0)
            """;
        const string soItemSql = """
            INSERT INTO sales_order_items
              (order_item_id, order_id, tenant_id, item_id, ordered_qty, delivered_qty,
               unit_price, supply_amount, vat_amount, item_status)
            VALUES
              (@ItemId, @OrderId, @TenantId, @ItemItemId, @Qty, 0,
               @UnitPrice, @SupplyAmount, @VatAmount, 'pending')
            """;

        // 매입 주문 INSERT SQL
        const string poSql = """
            INSERT INTO purchase_orders
              (po_id, tenant_id, po_no, partner_id, employee_id, po_date,
               status, total_amount, vat_amount, memo, created_at, updated_at, is_deleted)
            VALUES
              (@PoId, @TenantId, @PoNo, @PartnerId, @EmployeeId, @PoDate,
               'draft', @TotalAmount, @VatAmount, @Memo, @Now, @Now, 0)
            """;
        const string poItemSql = """
            INSERT INTO purchase_order_items
              (po_item_id, po_id, tenant_id, item_id, ordered_qty, received_qty,
               unit_price, supply_amount, vat_amount, warehouse_id, item_status)
            VALUES
              (@ItemId, @PoId, @TenantId, @ItemItemId, @Qty, 0,
               @UnitPrice, @SupplyAmount, @VatAmount, @WarehouseId, 'pending')
            """;

        int salesCount = 0, purchaseCount = 0;
        int soSeq = 1, poSeq = 1;

        foreach (DataRow header in headerDt.Rows)
        {
            var slipNo = GetStr(header, "K2_NO");          // 전표번호
            var gubun = GetStr(header, "K2_GUBUN");         // S=판매, B=매입
            var buyCode = GetInt(header, "K2_BUYC");        // 업체코드(Int32)
            var sawon = GetStr(header, "K2_SAWON");         // 담당사원
            var amt = GetDec(header, "K2_AMT");             // 공급가
            var vat = GetDec(header, "K2_VAT");             // 부가세
            var dtStr = GetStr(header, "K2_DT");            // 일자(YYYYMMDD)
            var orderDate = ParseLegacyDate(dtStr) ?? now;

            // 업체 매핑
            partnerMap.TryGetValue(buyCode, out var partnerId);
            if (string.IsNullOrEmpty(partnerId))
            {
                _logger.LogWarning("[MDB마이그레이션] 거래 업체코드 매핑 실패: {Code}, 전표: {SlipNo}", buyCode, slipNo);
                continue;
            }

            // 사원 매핑 (없으면 null)
            employeeMap.TryGetValue(sawon, out var employeeId);

            // 상세 행 가져오기
            detailsByNo.TryGetValue(slipNo, out var details);

            if (gubun.Equals("S", StringComparison.OrdinalIgnoreCase))
            {
                // ── 판매 ──
                var orderId = Guid.NewGuid().ToString();
                var orderNo = $"MIG-SO-{soSeq:D5}";

                await _db.ExecuteAsync(new CommandDefinition(soSql, new
                {
                    OrderId = orderId,
                    TenantId = tenantId,
                    OrderNo = orderNo,
                    PartnerId = partnerId,
                    EmployeeId = employeeId,
                    OrderDate = orderDate,
                    TotalAmount = amt,
                    VatAmount = vat,
                    Memo = $"레거시 전표번호: {slipNo}",
                    Now = now
                }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

                // 상세 INSERT
                if (details != null)
                {
                    foreach (var d in details)
                    {
                        var itemKey = BuildItemKey(GetStr(d, "KA_PUM"), GetStr(d, "KA_KU"));
                        itemMap.TryGetValue(itemKey, out var itemItemId);
                        if (string.IsNullOrEmpty(itemItemId)) continue;

                        await _db.ExecuteAsync(new CommandDefinition(soItemSql, new
                        {
                            ItemId = Guid.NewGuid().ToString(),
                            OrderId = orderId,
                            TenantId = tenantId,
                            ItemItemId = itemItemId,
                            Qty = GetDec(d, "KA_SU"),
                            UnitPrice = GetDec(d, "KA_DAN"),
                            SupplyAmount = GetDec(d, "KA_KUM"),
                            VatAmount = GetDec(d, "KA_VAT")
                        }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
                    }
                }

                salesCount++;
                soSeq++;
            }
            else if (gubun.Equals("B", StringComparison.OrdinalIgnoreCase))
            {
                // ── 매입 ──
                var poId = Guid.NewGuid().ToString();
                var poNo = $"MIG-PO-{poSeq:D5}";

                await _db.ExecuteAsync(new CommandDefinition(poSql, new
                {
                    PoId = poId,
                    TenantId = tenantId,
                    PoNo = poNo,
                    PartnerId = partnerId,
                    EmployeeId = employeeId,
                    PoDate = orderDate,
                    TotalAmount = amt,
                    VatAmount = vat,
                    Memo = $"레거시 전표번호: {slipNo}",
                    Now = now
                }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

                // 상세 INSERT
                if (details != null)
                {
                    foreach (var d in details)
                    {
                        var itemKey = BuildItemKey(GetStr(d, "KA_PUM"), GetStr(d, "KA_KU"));
                        itemMap.TryGetValue(itemKey, out var itemItemId);
                        if (string.IsNullOrEmpty(itemItemId)) continue;

                        await _db.ExecuteAsync(new CommandDefinition(poItemSql, new
                        {
                            ItemId = Guid.NewGuid().ToString(),
                            PoId = poId,
                            TenantId = tenantId,
                            ItemItemId = itemItemId,
                            Qty = GetDec(d, "KA_SU"),
                            UnitPrice = GetDec(d, "KA_DAN"),
                            SupplyAmount = GetDec(d, "KA_KUM"),
                            VatAmount = GetDec(d, "KA_VAT"),
                            WarehouseId = defaultWarehouseId
                        }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
                    }
                }

                purchaseCount++;
                poSeq++;
            }
        }

        _logger.LogInformation("[MDB마이그레이션] 판매 {Sales}건, 매입 {Purchase}건 이관 완료", salesCount, purchaseCount);
        return (salesCount, purchaseCount);
    }

    // ────────────────────────────────────────────────────────────────
    // 2-2. 매입매출 입출고 (DOCFB → stock_ledger)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// DOCFB(매입매출 입출고)를 읽어 stock_ledger 테이블에 INSERT한다.
    /// stock_ledger는 INSERT ONLY 원칙 — UPDATE/DELETE 절대 금지.
    /// </summary>
    private async Task<int> MigrateStockLedgerAsync(
        OleDbConnection oleConn, string tenantId, DateTime now,
        Dictionary<int, string> partnerMap,
        Dictionary<string, string> itemMap,
        string defaultWarehouseId,
        IDbTransaction tx, CancellationToken ct)
    {
        var dt = ReadMdbTable(oleConn, "SELECT * FROM DOCFB");
        if (dt.Rows.Count == 0) return 0;

        const string sql = """
            INSERT INTO stock_ledger
              (tenant_id, item_id, warehouse_id, partner_id, ledger_date, ym,
               move_type, source_type, source_id, doc_no, qty_in, qty_out,
               unit_cost, supply_amount, memo)
            VALUES
              (@TenantId, @ItemId, @WarehouseId, @PartnerId, @LedgerDate, @Ym,
               @MoveType, 'migration', @SourceId, @DocNo, @QtyIn, @QtyOut,
               @UnitCost, @SupplyAmount, @Memo)
            """;

        int count = 0;
        foreach (DataRow row in dt.Rows)
        {
            var itemKey = BuildItemKey(GetStr(row, "IJ_PUM"), GetStr(row, "IJ_KU"));
            if (!itemMap.TryGetValue(itemKey, out var itemId)) continue;

            var buyCode = GetInt(row, "IJ_BUY");
            partnerMap.TryGetValue(buyCode, out var partnerId);

            var dtStr = GetStr(row, "IJ_DT");
            var ledgerDate = ParseLegacyDate(dtStr) ?? now;
            var ym = ledgerDate.ToString("yyyy-MM");

            // IJ_IO: "I"=입고(in), "O"=출고(out)
            var io = GetStr(row, "IJ_IO").ToUpperInvariant();
            var moveType = io == "I" ? "in" : "out";
            var qty = GetDec(row, "IJ_QTY");
            var unitCost = qty != 0 ? GetDec(row, "IJ_AMT") / qty : 0m;

            // 창고: IJ_CHANG이 있으면 사용, 없으면 기본 창고
            var changStr = GetStr(row, "IJ_CHANG");
            var warehouseId = string.IsNullOrWhiteSpace(changStr) ? defaultWarehouseId : defaultWarehouseId;

            await _db.ExecuteAsync(new CommandDefinition(sql, new
            {
                TenantId = tenantId,
                ItemId = itemId,
                WarehouseId = warehouseId,
                PartnerId = partnerId,
                LedgerDate = ledgerDate,
                Ym = ym,
                MoveType = moveType,
                SourceId = $"mig-{GetStr(row, "IJ_DT")}-{GetShort(row, "IJ_SEQ")}",
                DocNo = GetStr(row, "IJ_TAXNO"),
                QtyIn = io == "I" ? qty : 0m,
                QtyOut = io == "O" ? qty : 0m,
                UnitCost = unitCost,
                SupplyAmount = GetDec(row, "IJ_AMT"),
                Memo = GetStr(row, "IJ_REM")
            }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            count++;
        }

        _logger.LogInformation("[MDB마이그레이션] 입출고(stock_ledger) {Count}건 이관 완료", count);
        return count;
    }

    // ────────────────────────────────────────────────────────────────
    // 2-3. 수금 마이그레이션 (DOCF5 → collections)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// DOCF5(수금)를 읽어 collections 테이블에 INSERT한다.
    /// </summary>
    private async Task<int> MigrateCollectionsAsync(
        OleDbConnection oleConn, string tenantId, DateTime now,
        Dictionary<int, string> partnerMap,
        IDbTransaction tx, CancellationToken ct)
    {
        var dt = ReadMdbTable(oleConn, "SELECT * FROM DOCF5");
        if (dt.Rows.Count == 0) return 0;

        const string sql = """
            INSERT INTO collections
              (collection_id, tenant_id, partner_id, collection_date, amount,
               collection_method, memo, is_active, created_at, updated_at)
            VALUES
              (@CollectionId, @TenantId, @PartnerId, @CollectionDate, @Amount,
               @Method, @Memo, 1, @Now, @Now)
            """;

        int count = 0;
        foreach (DataRow row in dt.Rows)
        {
            var buyCode = GetInt(row, "S_BUY");
            if (!partnerMap.TryGetValue(buyCode, out var partnerId)) continue;

            var collDate = ParseLegacyDate(GetStr(row, "S_YMD")) ?? now;

            // S_GU(구분)에 따라 수금방법 추정
            var gu = GetStr(row, "S_GU");
            var method = gu switch
            {
                "현금" or "1" => "cash",
                "카드" or "2" => "card",
                "어음" or "3" => "note",
                "수표" or "4" => "check",
                _ => "bank_transfer"
            };

            // S_SUK(수금금액)을 사용. 없으면 S_BAL(발생금액) 사용
            var amount = GetDec(row, "S_SUK");
            if (amount == 0) amount = GetDec(row, "S_BAL");

            await _db.ExecuteAsync(new CommandDefinition(sql, new
            {
                CollectionId = Guid.NewGuid().ToString(),
                TenantId = tenantId,
                PartnerId = partnerId,
                CollectionDate = collDate,
                Amount = amount,
                Method = method,
                Memo = GetStr(row, "S_REM"),
                Now = now
            }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            count++;
        }

        _logger.LogInformation("[MDB마이그레이션] 수금 {Count}건 이관 완료", count);
        return count;
    }

    // ────────────────────────────────────────────────────────────────
    // 2-4. 경비 마이그레이션 (DOCF6 → cashbook)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// DOCF6(경비)를 읽어 cashbook 테이블에 INSERT한다.
    /// </summary>
    private async Task<int> MigrateCashbookAsync(
        OleDbConnection oleConn, string tenantId, DateTime now,
        Dictionary<int, string> partnerMap,
        IDbTransaction tx, CancellationToken ct)
    {
        var dt = ReadMdbTable(oleConn, "SELECT * FROM DOCF6");
        if (dt.Rows.Count == 0) return 0;

        const string sql = """
            INSERT INTO cashbook
              (cashbook_id, tenant_id, tx_date, tx_type, category, partner_id,
               description, income_amount, expense_amount, balance,
               payment_method, memo, is_active, created_at)
            VALUES
              (@CashbookId, @TenantId, @TxDate, @TxType, '경비', @PartnerId,
               @Description, @IncomeAmount, @ExpenseAmount, 0,
               'cash', @Memo, 1, @Now)
            """;

        int count = 0;
        foreach (DataRow row in dt.Rows)
        {
            var txDate = ParseLegacyDate(GetStr(row, "AC_YMD")) ?? now;
            var amt = GetDec(row, "AC_AMT");

            var buyCode = GetInt(row, "AC_SBUY");
            partnerMap.TryGetValue(buyCode, out var partnerId);

            // AC_SGU(구분)에 따라 입출금 판단
            var gu = GetStr(row, "AC_SGU");
            var isExpense = true; // 기본적으로 경비(지출)로 처리

            // 적요(차/대) 합쳐서 description
            var jenCha = GetStr(row, "AC_JEN");   // 적요차
            var jekDae = GetStr(row, "AC_JEK");   // 적요대
            var description = $"{jenCha} {jekDae}".Trim();
            if (string.IsNullOrWhiteSpace(description)) description = "레거시 경비 이관";

            await _db.ExecuteAsync(new CommandDefinition(sql, new
            {
                CashbookId = Guid.NewGuid().ToString(),
                TenantId = tenantId,
                TxDate = txDate,
                TxType = isExpense ? "expense" : "income",
                PartnerId = partnerId,
                Description = description.Length > 200 ? description[..200] : description,
                IncomeAmount = isExpense ? 0m : amt,
                ExpenseAmount = isExpense ? amt : 0m,
                Memo = GetStr(row, "AC_cheri"),   // 처리 → memo
                Now = now
            }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            count++;
        }

        _logger.LogInformation("[MDB마이그레이션] 경비(cashbook) {Count}건 이관 완료", count);
        return count;
    }

    // ────────────────────────────────────────────────────────────────
    // 2-5. 전표 마이그레이션 (DOCF7 → expenses)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// DOCF7(전표)를 읽어 expenses 테이블에 INSERT한다.
    /// SC_CR(차변)이 양수면 지출, SC_DR(대변)이 양수면 수입 처리.
    /// </summary>
    private async Task<int> MigrateExpensesAsync(
        OleDbConnection oleConn, string tenantId, DateTime now,
        Dictionary<string, string> employeeMap,
        IDbTransaction tx, CancellationToken ct)
    {
        var dt = ReadMdbTable(oleConn, "SELECT * FROM DOCF7");
        if (dt.Rows.Count == 0) return 0;

        const string sql = """
            INSERT INTO expenses
              (expense_id, tenant_id, expense_date, employee_id, category, description,
               amount, vat_amount, payment_method, receipt_yn, approval_status,
               memo, is_active, created_at)
            VALUES
              (@ExpenseId, @TenantId, @ExpenseDate, @EmployeeId, @Category, @Description,
               @Amount, 0, 'cash', 0, 'approved',
               @Memo, 1, @Now)
            """;

        int count = 0;
        foreach (DataRow row in dt.Rows)
        {
            var expDate = ParseLegacyDate(GetStr(row, "SC_DT")) ?? now;
            var sawon = GetStr(row, "SC_SAWON");
            employeeMap.TryGetValue(sawon, out var employeeId);

            // 차변/대변 중 큰 쪽이 금액
            var cr = GetDec(row, "SC_CR");   // 차변
            var dr = GetDec(row, "SC_DR");   // 대변
            var amount = cr > 0 ? cr : dr;
            if (amount == 0) continue;

            var costCode = GetStr(row, "SC_KCODE");     // 비용코드
            var description = GetStr(row, "SC_JEK");    // 적요
            if (string.IsNullOrWhiteSpace(description)) description = "레거시 전표 이관";

            await _db.ExecuteAsync(new CommandDefinition(sql, new
            {
                ExpenseId = Guid.NewGuid().ToString(),
                TenantId = tenantId,
                ExpenseDate = expDate,
                EmployeeId = employeeId,
                Category = string.IsNullOrWhiteSpace(costCode) ? "기타" : costCode,
                Description = description.Length > 200 ? description[..200] : description,
                Amount = amount,
                Memo = GetStr(row, "SC_REM"),
                Now = now
            }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            count++;
        }

        _logger.LogInformation("[MDB마이그레이션] 전표(expenses) {Count}건 이관 완료", count);
        return count;
    }

    // ════════════════════════════════════════════════════════════════
    // 유틸리티 메서드
    // ════════════════════════════════════════════════════════════════

    /// <summary>MDB 폴더 경로에서 3개 파일 경로를 자동 탐색한다.</summary>
    private static (string Pyojun, string Pandata, string Pother) ResolveMdbPaths(string folderPath)
    {
        // 보안: Path Traversal 방지 — ".." 포함 경로 차단
        if (folderPath.Contains("..", StringComparison.Ordinal))
            throw new InvalidOperationException("경로에 '..'을 포함할 수 없습니다.");

        // 절대 경로만 허용 (상대 경로 차단)
        if (!Path.IsPathRooted(folderPath))
            throw new InvalidOperationException("절대 경로만 입력 가능합니다.");

        if (!Directory.Exists(folderPath))
            throw new FileNotFoundException($"폴더를 찾을 수 없습니다: {folderPath}");

        // 대소문자 무시하고 탐색
        var files = Directory.GetFiles(folderPath, "*.mdb", SearchOption.TopDirectoryOnly);

        string FindFile(string name) =>
            files.FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? Path.Combine(folderPath, name);

        return (FindFile("PYOJUN.MDB"), FindFile("PANDATA.mdb"), FindFile("POTHER.mdb"));
    }

    /// <summary>MDB 테이블 레코드 수를 카운트한다.</summary>
    private static int CountMdbTable(OleDbConnection conn, string tableName)
    {
        try
        {
            using var cmd = new OleDbCommand($"SELECT COUNT(*) FROM [{tableName}]", conn);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch
        {
            return 0; // 테이블 없으면 0
        }
    }

    /// <summary>MariaDB 커넥션이 닫혀있으면 비동기로 열어준다.</summary>
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

    /// <summary>OLEDB로 MDB 파일을 열어 OleDbConnection을 반환한다.</summary>
    private static OleDbConnection OpenOleDb(string mdbPath)
    {
        var connStr = string.Format(OleDbConnTemplate, mdbPath);
        var conn = new OleDbConnection(connStr);
        conn.Open();
        return conn;
    }

    /// <summary>MDB 테이블을 SELECT하여 DataTable로 반환한다. 한글 인코딩을 보장한다.</summary>
    private static DataTable ReadMdbTable(OleDbConnection conn, string sql)
    {
        using var cmd = new OleDbCommand(sql, conn);
        using var adapter = new OleDbDataAdapter(cmd);
        var dt = new DataTable();
        adapter.Fill(dt);
        return dt;
    }

    /// <summary>
    /// 레거시 날짜 문자열("YYYYMMDD" 또는 "YYYY-MM-DD" 등)을 DateTime으로 변환한다.
    /// 변환 실패 시 null 반환.
    /// </summary>
    private static DateTime? ParseLegacyDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return null;

        // 하이픈/슬래시 제거 → 순수 숫자 8자리로 통일
        var cleaned = dateStr.Replace("-", "").Replace("/", "").Replace(".", "").Trim();

        if (cleaned.Length >= 8 &&
            DateTime.TryParseExact(cleaned[..8], "yyyyMMdd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return dt;
        }

        // 일반 파싱 시도
        if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt2))
        {
            return dt2;
        }

        return null;
    }

    /// <summary>상품 매핑 키 생성: "품명|규격" (규격이 비어있으면 품명만)</summary>
    private static string BuildItemKey(string pumName, string spec)
    {
        return string.IsNullOrWhiteSpace(spec)
            ? pumName.Trim()
            : $"{pumName.Trim()}|{spec.Trim()}";
    }

    // ── DataRow 안전 읽기 헬퍼 ──

    /// <summary>DataRow에서 문자열을 안전하게 읽는다. 컬럼이 없거나 DBNull이면 빈 문자열을 반환한다.</summary>
    private static string GetStr(DataRow row, string col)
    {
        if (!row.Table.Columns.Contains(col)) return string.Empty;
        var val = row[col];
        return val == DBNull.Value ? string.Empty : Convert.ToString(val) ?? string.Empty;
    }

    /// <summary>DataRow에서 int를 안전하게 읽는다.</summary>
    private static int GetInt(DataRow row, string col)
    {
        if (!row.Table.Columns.Contains(col)) return 0;
        var val = row[col];
        if (val == DBNull.Value) return 0;
        return Convert.ToInt32(val);
    }

    /// <summary>DataRow에서 short를 안전하게 읽는다.</summary>
    private static short GetShort(DataRow row, string col)
    {
        if (!row.Table.Columns.Contains(col)) return 0;
        var val = row[col];
        if (val == DBNull.Value) return 0;
        return Convert.ToInt16(val);
    }

    /// <summary>DataRow에서 decimal을 안전하게 읽는다. 금액은 반드시 decimal 사용 (float/double 금지).</summary>
    private static decimal GetDec(DataRow row, string col)
    {
        if (!row.Table.Columns.Contains(col)) return 0m;
        var val = row[col];
        if (val == DBNull.Value) return 0m;
        return Convert.ToDecimal(val);
    }
}

// ════════════════════════════════════════════════════════════════
// 결과 DTO
// ════════════════════════════════════════════════════════════════

/// <summary>
/// MDB 마이그레이션 결과 DTO — 테이블별 이관 건수를 담는다.
/// </summary>
public sealed class MdbMigrationResult
{
    /// <summary>업체(partners) 이관 건수</summary>
    public int Partners { get; set; }

    /// <summary>상품(items) 이관 건수</summary>
    public int Items { get; set; }

    /// <summary>BOM 헤더(bom_headers) 이관 건수</summary>
    public int BomHeaders { get; set; }

    /// <summary>사원(employees) 이관 건수</summary>
    public int Employees { get; set; }

    /// <summary>판매(sales_orders) 이관 건수</summary>
    public int SalesOrders { get; set; }

    /// <summary>매입(purchase_orders) 이관 건수</summary>
    public int PurchaseOrders { get; set; }

    /// <summary>입출고(stock_ledger) 이관 건수</summary>
    public int StockLedger { get; set; }

    /// <summary>수금(collections) 이관 건수</summary>
    public int Collections { get; set; }

    /// <summary>경비(cashbook) 이관 건수</summary>
    public int Cashbook { get; set; }

    /// <summary>전표(expenses) 이관 건수</summary>
    public int Expenses { get; set; }

    /// <summary>전체 이관 건수 합계</summary>
    public int Total => Partners + Items + BomHeaders + Employees
                        + SalesOrders + PurchaseOrders + StockLedger
                        + Collections + Cashbook + Expenses;

    public override string ToString()
    {
        return $"업체:{Partners}, 상품:{Items}, BOM:{BomHeaders}, 사원:{Employees}, " +
               $"판매:{SalesOrders}, 매입:{PurchaseOrders}, 입출고:{StockLedger}, " +
               $"수금:{Collections}, 경비:{Cashbook}, 전표:{Expenses} [합계:{Total}]";
    }
}
