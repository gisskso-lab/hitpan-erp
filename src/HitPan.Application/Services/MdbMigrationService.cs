using System.Data;
using System.Data.Common;
using System.Data.OleDb;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using Dapper;
using HitPan.Application.Interfaces;
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
    private readonly IBinaryCryptoService _crypto;

    /// <summary>OLEDB 커넥션 문자열 템플릿 (MDB 경로 + 선택적 비번)</summary>
    /// 핫픽스 2026-05-13: 사장님 MDB(비번 7618968) 지원 — 결재 #13.
    private const string OleDbConnTemplate =
        "Provider=Microsoft.ACE.OLEDB.12.0;Data Source={0};Jet OLEDB:Database Password={1};";

    /// <summary>현재 마이그 호출의 MDB 비번 (AsyncLocal 컨텍스트 — overload 시그니처 보존하면서 비번 전달).</summary>
    private static readonly AsyncLocal<string?> _mdbPasswordContext = new();

    public MdbMigrationService(
        IDbConnection db,
        ILogger<MdbMigrationService> logger,
        IBinaryCryptoService crypto)
    {
        _db = db;
        _logger = logger;
        _crypto = crypto;
    }

    // ────────────────────────────────────────────────────────────────
    // 공개 메서드: 마이그레이션 실행
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// MDB 폴더 경로로 마이그레이션 (폴더 안에서 PYOJUN/PANDATA/POTHER 자동 탐색).
    /// </summary>
    public async Task<MdbMigrationResult> MigrateAsync(
        string folderPath, string tenantId, CancellationToken ct = default)
        => await MigrateAsync(folderPath, tenantId, mdbPassword: null, ct).ConfigureAwait(false);

    /// <summary>
    /// MDB 비번을 받는 overload (핫픽스 2026-05-13).
    /// 비번이 걸린 레거시 히트판 MDB(예: 7618968) 처리용.
    /// </summary>
    public async Task<MdbMigrationResult> MigrateAsync(
        string folderPath, string tenantId, string? mdbPassword, CancellationToken ct = default)
    {
        var (pyojunPath, pandataPath, _) = ResolveMdbPaths(folderPath);
        _mdbPasswordContext.Value = mdbPassword;
        try
        {
            return await MigrateCoreAsync(pyojunPath, pandataPath, tenantId, ct).ConfigureAwait(false);
        }
        finally
        {
            _mdbPasswordContext.Value = null;
        }
    }

    /// <summary>
    /// MDB 폴더 내 테이블 건수 미리보기 (실제 import 없음).
    /// </summary>
    public Task<Dictionary<string, int>> PreviewAsync(
        string folderPath, string tenantId, CancellationToken ct = default)
        => PreviewAsync(folderPath, tenantId, mdbPassword: null, ct);

    /// <summary>
    /// MDB 비번을 받는 Preview overload (핫픽스 2026-05-13).
    /// </summary>
    public Task<Dictionary<string, int>> PreviewAsync(
        string folderPath, string tenantId, string? mdbPassword, CancellationToken ct = default)
    {
        _mdbPasswordContext.Value = mdbPassword;
        return PreviewCoreAsync(folderPath, tenantId, ct);
    }

    private Task<Dictionary<string, int>> PreviewCoreAsync(
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
            result["DOCFA (매입발주)"] = CountMdbTable(oleConn, "DOCFA");
            result["DOCFO (매출주문)"] = CountMdbTable(oleConn, "DOCFO");
            result["DOCF9 (어음발행)"] = CountMdbTable(oleConn, "DOCF9");
            result["DOCFQ (어음만기)"] = CountMdbTable(oleConn, "DOCFQ");
            result["DOCCD (카드결제)"] = CountMdbTable(oleConn, "DOCCD");
            result["DOCCD1 (카드라인)"] = CountMdbTable(oleConn, "DOCCD1");
            result["BANKF (은행거래)"] = CountMdbTable(oleConn, "BANKF");
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
    /// P0 #1 (2026-05-14): 거대 단일 트랜잭션 → 테이블별 분리 tx + 체크포인트.
    /// 사장님 헌법 #20 본래 의미(워크플로우 끊김 0) 정공법 회복. 한 테이블 실패해도
    /// 다른 테이블 commit 보존 + 재실행 시 미완료 테이블만 재처리.
    /// </summary>
    private async Task<MdbMigrationResult> MigrateCoreAsync(
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
        var partnerMap = new Dictionary<int, string>();
        var itemMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var employeeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var defaultWarehouseId = "wh-migration";

        await EnsureOpenAsync(ct).ConfigureAwait(false);

        // P0 #1 (2026-05-14): 마이그 세션 한정 튜닝 — 사장님 결재 (innodb_flush 세션 한정)
        // 글로벌 영향 0, 마이그 잡 종료 시 자동 복원. 청크 commit 횟수↑ 대비 fsync 부하 완화.
        // foreign_key_checks=0 / unique_checks=0 은 마이그 데이터 무결성 사전 검증 완료 전제.
        await ApplyMigrationSessionTuningAsync(ct).ConfigureAwait(false);

        try
        {
            // ──────────────────────────────────────
            // 0. 마이그레이션 전용 기본 창고 (단독 tx)
            // ──────────────────────────────────────
            await RunTableStepAsync("warehouse_migration", async tx =>
            {
                await EnsureMigrationWarehouseAsync(tenantId, defaultWarehouseId, now, tx, ct).ConfigureAwait(false);
                return 0;
            }, ct).ConfigureAwait(false);

            // ──────────────────────────────────────
            // 1단계: PYOJUN.MDB (마스터 — FK 매핑 묶음 유지)
            // 4개 메서드가 partnerMap/itemMap/employeeMap 채우는 단계이므로
            // 동일 tx 안에서 처리해 매핑 일관성 보장. 이 단계는 거래 데이터에 비해 매우 가벼움(수만 행).
            // ──────────────────────────────────────
            _logger.LogInformation("[MDB마이그레이션] PYOJUN.MDB 읽기 시작: {Path}", pyojunPath);

            await RunTableStepAsync("pyojun_master", async tx =>
            {
                using var oleConn = OpenOleDb(pyojunPath);
                result.Partners = await MigratePartnersAsync(oleConn, tenantId, now, partnerMap, tx, ct).ConfigureAwait(false);
                result.Items = await MigrateItemsAsync(oleConn, tenantId, now, itemMap, tx, ct).ConfigureAwait(false);
                result.BomHeaders = await MigrateBomAsync(oleConn, tenantId, now, itemMap, tx, ct).ConfigureAwait(false);
                result.Employees = await MigrateEmployeesAsync(oleConn, tenantId, now, employeeMap, tx, ct).ConfigureAwait(false);
                return result.Partners + result.Items + result.BomHeaders + result.Employees;
            }, ct).ConfigureAwait(false);

            // ──────────────────────────────────────
            // 2단계: PANDATA.mdb (거래 — 테이블별 독립 tx)
            // 각 테이블 commit 단위 ~수초~수십초. 한 테이블 실패 시 다른 테이블 보존.
            // partnerMap/itemMap/employeeMap은 in-memory 이므로 FK 무관.
            // ──────────────────────────────────────
            _logger.LogInformation("[MDB마이그레이션] PANDATA.mdb 읽기 시작: {Path}", pandataPath);

            // 2-1. 거래(판매/매입) — DOCF2/DOCF1
            await RunTableStepAsync("transactions", async tx =>
            {
                using var oleConn = OpenOleDb(pandataPath);
                var (salesCount, purchaseCount) = await MigrateTransactionsAsync(
                    oleConn, tenantId, now, partnerMap, itemMap, employeeMap, defaultWarehouseId, tx, ct).ConfigureAwait(false);
                result.SalesOrders = salesCount;
                result.PurchaseOrders = purchaseCount;
                return salesCount + purchaseCount;
            }, ct).ConfigureAwait(false);

            // 2-2. stock_ledger — DOCFB (P0 #3에서 5K 청크로 추가 분리 예정)
            await RunTableStepAsync("stock_ledger", async tx =>
            {
                using var oleConn = OpenOleDb(pandataPath);
                result.StockLedger = await MigrateStockLedgerAsync(
                    oleConn, tenantId, now, partnerMap, itemMap, defaultWarehouseId, tx, ct).ConfigureAwait(false);
                return result.StockLedger;
            }, ct).ConfigureAwait(false);

            // 2-3. collections — DOCF5
            await RunTableStepAsync("collections", async tx =>
            {
                using var oleConn = OpenOleDb(pandataPath);
                result.Collections = await MigrateCollectionsAsync(
                    oleConn, tenantId, now, partnerMap, tx, ct).ConfigureAwait(false);
                return result.Collections;
            }, ct).ConfigureAwait(false);

            // 2-4. cashbook — DOCF6
            await RunTableStepAsync("cashbook", async tx =>
            {
                using var oleConn = OpenOleDb(pandataPath);
                result.Cashbook = await MigrateCashbookAsync(
                    oleConn, tenantId, now, partnerMap, tx, ct).ConfigureAwait(false);
                return result.Cashbook;
            }, ct).ConfigureAwait(false);

            // 2-5. expenses — DOCF7
            await RunTableStepAsync("expenses", async tx =>
            {
                using var oleConn = OpenOleDb(pandataPath);
                result.Expenses = await MigrateExpensesAsync(
                    oleConn, tenantId, now, employeeMap, tx, ct).ConfigureAwait(false);
                return result.Expenses;
            }, ct).ConfigureAwait(false);

            // 2-6. purchase_orders — DOCFA
            await RunTableStepAsync("purchase_orders", async tx =>
            {
                using var oleConn = OpenOleDb(pandataPath);
                result.PurchaseOrdersFromIU = await MigratePurchaseOrdersFromIUAsync(
                    oleConn, tenantId, now, partnerMap, itemMap, tx, ct).ConfigureAwait(false);
                return result.PurchaseOrdersFromIU;
            }, ct).ConfigureAwait(false);

            // 2-7. sales_orders — DOCFO
            await RunTableStepAsync("sales_orders", async tx =>
            {
                using var oleConn = OpenOleDb(pandataPath);
                result.SalesOrdersFromIO = await MigrateSalesOrdersFromIOAsync(
                    oleConn, tenantId, now, partnerMap, itemMap, tx, ct).ConfigureAwait(false);
                return result.SalesOrdersFromIO;
            }, ct).ConfigureAwait(false);

            // 2-8. tax_invoices — DOCF4 (4품목 행분해)
            await RunTableStepAsync("tax_invoices", async tx =>
            {
                using var oleConn = OpenOleDb(pandataPath);
                result.TaxInvoices = await MigrateTaxInvoicesAsync(
                    oleConn, tenantId, now, partnerMap, tx, ct).ConfigureAwait(false);
                return result.TaxInvoices;
            }, ct).ConfigureAwait(false);

            // 2-9. bills — DOCF9 + DOCFQ
            await RunTableStepAsync("bills", async tx =>
            {
                using var oleConn = OpenOleDb(pandataPath);
                result.Bills = await MigrateBillsAsync(
                    oleConn, tenantId, now, partnerMap, tx, ct).ConfigureAwait(false);
                return result.Bills;
            }, ct).ConfigureAwait(false);

            // 2-10. card_payments — DOCCD + DOCCD1
            await RunTableStepAsync("card_payments", async tx =>
            {
                using var oleConn = OpenOleDb(pandataPath);
                result.CardPayments = await MigrateCardPaymentsAsync(
                    oleConn, tenantId, now, partnerMap, tx, ct).ConfigureAwait(false);
                return result.CardPayments;
            }, ct).ConfigureAwait(false);

            // 2-11. bank_transactions — BANKF
            await RunTableStepAsync("bank_transactions", async tx =>
            {
                using var oleConn = OpenOleDb(pandataPath);
                result.BankTransactions = await MigrateBankTransactionsAsync(
                    oleConn, tenantId, now, partnerMap, tx, ct).ConfigureAwait(false);
                return result.BankTransactions;
            }, ct).ConfigureAwait(false);

            _logger.LogInformation("[MDB마이그레이션] 완료. 결과: {@Result}", result);
        }
        finally
        {
            // 마이그 세션 한정 튜닝 원복 (세션 종료 시 자동이지만 명시).
            await RestoreMigrationSessionTuningAsync(ct).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// 테이블 단위 작업을 독립 트랜잭션으로 실행한다.
    /// 한 테이블 실패는 다른 테이블 commit을 보존한다(헌법 #20 본래 의미).
    /// </summary>
    /// <param name="tableName">migration_checkpoints.table_name 값 — 진행 추적용.</param>
    /// <param name="work">tx를 받아 수행할 마이그 단위 작업. 반환값은 처리 행수.</param>
    private async Task RunTableStepAsync(
        string tableName, Func<IDbTransaction, Task<int>> work, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        IDbTransaction? tx = null;
        try
        {
            tx = _db.BeginTransaction();
            var rows = await work(tx).ConfigureAwait(false);
            tx.Commit();
            sw.Stop();
            _logger.LogInformation(
                "[MDB마이그레이션] {Table} 완료: {Rows}행, {Elapsed}ms",
                tableName, rows, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            try { tx?.Rollback(); }
            catch (Exception rbex) { _logger.LogError(rbex, "[MDB마이그레이션] {Table} 롤백 실패", tableName); }
            _logger.LogError(ex,
                "[MDB마이그레이션] {Table} 실패 — 이 테이블만 롤백, 다른 테이블 보존. ({Elapsed}ms)",
                tableName, sw.ElapsedMilliseconds);
            throw;
        }
        finally
        {
            tx?.Dispose();
        }
    }

    /// <summary>
    /// 마이그 세션 한정 튜닝 (사장님 결재, 글로벌 영향 0).
    /// </summary>
    private async Task ApplyMigrationSessionTuningAsync(CancellationToken ct)
    {
        try
        {
            // commit 시 redo log fsync 빈도 완화 (재시작 시 최근 1초 마이그 데이터 손실 가능 — 수용).
            await _db.ExecuteAsync(new CommandDefinition(
                "SET SESSION innodb_flush_log_at_trx_commit = 2", cancellationToken: ct)).ConfigureAwait(false);
            // 마이그 데이터는 외부 MDB 원천이므로 FK·UNIQUE 사전 검증 완료 전제.
            await _db.ExecuteAsync(new CommandDefinition(
                "SET SESSION foreign_key_checks = 0", cancellationToken: ct)).ConfigureAwait(false);
            await _db.ExecuteAsync(new CommandDefinition(
                "SET SESSION unique_checks = 0", cancellationToken: ct)).ConfigureAwait(false);
            _logger.LogInformation("[MDB마이그레이션] 세션 튜닝 적용 (innodb_flush=2, fk=0, unique=0)");
        }
        catch (Exception ex)
        {
            // 튜닝 실패해도 마이그는 계속 진행 (속도만 손해).
            _logger.LogWarning(ex, "[MDB마이그레이션] 세션 튜닝 적용 실패 — 기본값으로 진행");
        }
    }

    /// <summary>세션 종료 시 자동 복원되지만 명시적으로 원복 (방어).</summary>
    private async Task RestoreMigrationSessionTuningAsync(CancellationToken ct)
    {
        try
        {
            await _db.ExecuteAsync(new CommandDefinition(
                "SET SESSION innodb_flush_log_at_trx_commit = 1", cancellationToken: ct)).ConfigureAwait(false);
            await _db.ExecuteAsync(new CommandDefinition(
                "SET SESSION foreign_key_checks = 1", cancellationToken: ct)).ConfigureAwait(false);
            await _db.ExecuteAsync(new CommandDefinition(
                "SET SESSION unique_checks = 1", cancellationToken: ct)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[MDB마이그레이션] 세션 튜닝 원복 실패 — 세션 종료 시 자동 복원됨");
        }
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

        // W2 D3 (2026-05-12): partners 19개 컬럼 보강 INSERT (사장님 결재)
        // 신규 컬럼: card_commission_rate, classification_code, manager_department,
        //   price_grade_code, legacy_extra, discount_rate, keyman_birth/name/phone,
        //   margin_rate, sales_employee, trade_start_date, business_registration_date,
        //   tel_secondary, tax_classification, ceo_name_legacy, partner_type_legacy,
        //   ceo_resident_no_encrypted (VARBINARY AES-256, 결재 #4 정책)
        const string sql = """
            INSERT INTO partners
              (partner_id, tenant_id, partner_code, partner_name, partner_type,
               biz_no, ceo_name, biz_type, biz_item,
               tel, fax, address, address_detail, zip_code,
               credit_limit, bank_name, bank_account, account_holder,
               manager_name, manager_tel, tax_type, memo,
               is_active, is_deleted, created_at, updated_at, price_grade, row_version,
               card_commission_rate, classification_code, manager_department,
               price_grade_code, legacy_extra, discount_rate,
               keyman_birth, keyman_name, keyman_phone,
               margin_rate, sales_employee, trade_start_date,
               business_registration_date, tel_secondary, tax_classification,
               ceo_resident_no_encrypted)
            VALUES
              (@PartnerId, @TenantId, @PartnerCode, @PartnerName, @PartnerType,
               @BizNo, @CeoName, @BizType, @BizItem,
               @Tel, @Fax, @Address, @AddressDetail, @ZipCode,
               @CreditLimit, @BankName, @BankAccount, @AccountHolder,
               @ManagerName, @ManagerTel, @TaxType, @Memo,
               1, 0, @Now, @Now, @PriceGrade, 0,
               @CardCommissionRate, @ClassificationCode, @ManagerDepartment,
               @PriceGradeCode, @LegacyExtra, @DiscountRate,
               @KeymanBirth, @KeymanName, @KeymanPhone,
               @MarginRate, @SalesEmployee, @TradeStartDate,
               @BusinessRegistrationDate, @TelSecondary, @TaxClassification,
               @CeoResidentNoEncrypted)
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

            // W2 D3 (2026-05-12): 19개 보강 컬럼 - DOCF8 41컬럼 중 누락분 추가
            // buy_DOSCODE 옵션 H: 원본은 price_grade_code 보존, price_grade는 'A' 기본값 (A안 결재)
            var doscode = GetStr(row, "buy_DOSCODE")?.Trim();
            var topJumin = GetStr(row, "buy_topjumin")?.Trim();

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
                PriceGrade = "A",   // CHAR(1) 기본값, 옵션 H 후처리 시 결정
                Now = now,
                // 신규 19개 컬럼 (작9, 2026-05-12 결재)
                CardCommissionRate = GetDec(row, "buy_cardyul"),
                ClassificationCode = GetStr(row, "buy_ccode"),
                ManagerDepartment = GetStr(row, "buy_damdangbu"),
                PriceGradeCode = doscode,                  // 옵션 H 원본 보존
                LegacyExtra = GetStr(row, "buy_fil"),
                DiscountRate = GetDec(row, "buy_halyul"),
                KeymanBirth = GetStr(row, "buy_keybirth"),
                KeymanName = GetStr(row, "buy_keyname"),
                KeymanPhone = GetStr(row, "buy_keytel"),
                MarginRate = GetDec(row, "buy_mayul"),
                SalesEmployee = GetStr(row, "buy_sawon"),
                TradeStartDate = ParseDateOrNull(GetStr(row, "buy_startdt")),
                BusinessRegistrationDate = ParseDateOrNull(GetStr(row, "buy_taxdt")),
                TelSecondary = GetStr(row, "buy_tel1"),
                TaxClassification = taxGubun,
                // 형사영역 (헌법 #5, CRIMINAL_DOMAIN_POLICY.md): 부가가치세법 §32 처리 근거
                CeoResidentNoEncrypted = string.IsNullOrEmpty(topJumin) ? null : _crypto.EncryptToBytes(topJumin)
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

        // W2 D3 (2026-05-12): items 4개 보강 컬럼 추가 (safety_stock 기존, 신규 4개)
        // 작10: spec_detail, unit_secondary, reorder_point, supplier_default_id
        const string sql = """
            INSERT INTO items
              (item_id, tenant_id, item_code, item_name, item_type, unit, spec,
               purchase_price, sale_price, standard_price, cost_price, std_price,
               price_a, price_b, price_c, price_d, price_e,
               tax_type, barcode, item_group, memo,
               is_active, is_deleted, safety_stock, created_at, updated_at, row_version,
               spec_detail, unit_secondary, reorder_point, supplier_default_id)
            VALUES
              (@ItemId, @TenantId, @ItemCode, @ItemName, 'product', @Unit, @Spec,
               @PurchasePrice, @SalePrice, @StandardPrice, @CostPrice, @StdPrice,
               @PriceA, @PriceB, @PriceC, @PriceD, @PriceE,
               @TaxType, @Barcode, @ItemGroup, @Memo,
               1, 0, 0, @Now, @Now, 0,
               @SpecDetail, @UnitSecondary, @ReorderPoint, @SupplierDefaultId)
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
                Now = now,
                // 신규 4개 컬럼 (작10, 2026-05-12 결재). safety_stock은 기존 컬럼 유지(0).
                // S_SPEC·S_UNIT2·S_SAFE·S_REORD·S_VENDOR는 사장님 실 데이터 분포 확인 후 매핑 (W3).
                // 현재 빈 MDB 가정 → null·기본값으로 INSERT, 향후 베타 체험단 실 데이터로 보강.
                SpecDetail = GetStr(row, "S_SPEC"),
                UnitSecondary = GetStr(row, "S_UNIT2"),
                ReorderPoint = GetDec(row, "S_REORD"),
                SupplierDefaultId = (string?)null      // FK 매핑은 partners 마이그 완료 후 별도 후처리
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

        // W2 D3 (2026-05-12): employees 31개 보강 컬럼 (작11 결재, A안)
        // A. 기본 8 / B. 형사 5 (AES-256) / C. 직장 7 / D. 레거시잔액 10 / E. 해외 1
        // 형사영역(헌법 #5): SW_JUMIN·SW_PAY·SW_PAYoth → VARBINARY AES-256
        const string sql = """
            INSERT INTO employees
              (employee_id, tenant_id, emp_no, emp_name, position, job_title, emp_type,
               join_date, phone, email, is_active, created_at, updated_at, role,
               address, zip_code, birth_date, birth_calendar, birth_lunar_converted,
               home_phone, emergency_contact, memo,
               resident_no_encrypted, salary_encrypted, salary_type, salary_category, salary_extra_encrypted,
               department, marriage_status, business_type, is_resigned, resign_date, resign_reason, nationality,
               legacy_bal1, legacy_bal2, legacy_bal3, legacy_bal4, legacy_bal5,
               legacy_bal6, legacy_bal7, legacy_bal8, legacy_bal9, legacy_bal10,
               salary_country)
            VALUES
              (@EmployeeId, @TenantId, @EmpNo, @EmpName, @Position, @JobTitle, 'regular',
               @JoinDate, @Phone, NULL, 1, @Now, @Now, 'sales_user',
               @Address, @ZipCode, @BirthDate, @BirthCalendar, @BirthLunarConverted,
               @HomePhone, @EmergencyContact, @Memo,
               @ResidentNoEncrypted, @SalaryEncrypted, @SalaryType, @SalaryCategory, @SalaryExtraEncrypted,
               @Department, @MarriageStatus, @BusinessType, @IsResigned, @ResignDate, @ResignReason, @Nationality,
               @LegacyBal1, @LegacyBal2, @LegacyBal3, @LegacyBal4, @LegacyBal5,
               @LegacyBal6, @LegacyBal7, @LegacyBal8, @LegacyBal9, @LegacyBal10,
               @SalaryCountry)
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

            // 형사영역 평문 추출 (즉시 AES-256 암호화, 평문은 메서드 스코프 내에서만 존재)
            var residentNo = GetStr(row, "SW_JUMIN")?.Trim();
            var salary = GetInt(row, "SW_PAY");
            var salaryExtra = GetStr(row, "SW_PAYoth")?.Trim();

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
                Now = now,
                // A. 기본 정보 (8개)
                Address = GetStr(row, "SW_ADDR"),
                ZipCode = GetStr(row, "SW_POSTNO"),
                BirthDate = ParseLegacyDate(GetStr(row, "SW_BIRTH")),
                BirthCalendar = (byte)(GetInt(row, "SW_BIRTHgu") == 0 ? 1 : GetInt(row, "SW_BIRTHgu")),
                BirthLunarConverted = (byte)GetInt(row, "SW_BIRTHtel"),
                HomePhone = GetStr(row, "SW_TEL"),
                EmergencyContact = GetStr(row, "SW_TELem"),
                Memo = GetStr(row, "SW_REM"),
                // B. 형사 영역 (5개) — AES-256 + 동의 + 마스킹 + step-up + 감사로그 (CRIMINAL_DOMAIN_POLICY.md)
                ResidentNoEncrypted = string.IsNullOrEmpty(residentNo) ? null : _crypto.EncryptToBytes(residentNo),
                SalaryEncrypted = salary == 0 ? null : _crypto.EncryptToBytes(salary.ToString(CultureInfo.InvariantCulture)),
                SalaryType = (byte?)(GetInt(row, "SW_PAYgu") == 0 ? null : (byte?)GetInt(row, "SW_PAYgu")),
                SalaryCategory = (byte?)(GetInt(row, "SW_PAYeuy") == 0 ? null : (byte?)GetInt(row, "SW_PAYeuy")),
                SalaryExtraEncrypted = string.IsNullOrEmpty(salaryExtra) ? null : _crypto.EncryptToBytes(salaryExtra),
                // C. 직장 정보 (7개)
                Department = GetStr(row, "SW_BU"),
                MarriageStatus = GetStr(row, "SW_MARRY"),
                BusinessType = GetStr(row, "SW_WORK"),
                IsResigned = (byte)GetInt(row, "SW_OUT"),
                ResignDate = ParseLegacyDate(GetStr(row, "SW_OUTDT")),
                ResignReason = GetStr(row, "SW_OUTREM"),
                Nationality = GetStr(row, "SW_NATION"),
                // D. 레거시 잔액 (10개) — 원본 그대로 보존
                LegacyBal1 = GetStr(row, "SW_BAL1"),
                LegacyBal2 = GetStr(row, "SW_BAL2"),
                LegacyBal3 = GetStr(row, "SW_BAL3"),
                LegacyBal4 = GetStr(row, "SW_BAL4"),
                LegacyBal5 = GetStr(row, "SW_BAL5"),
                LegacyBal6 = GetStr(row, "SW_BAL6"),
                LegacyBal7 = GetStr(row, "SW_BAL7"),
                LegacyBal8 = GetStr(row, "SW_BAL8"),
                LegacyBal9 = GetStr(row, "SW_BAL9"),
                LegacyBal10 = GetStr(row, "SW_BAL10"),
                // E. 해외 (1개)
                SalaryCountry = (byte?)(GetInt(row, "SW_PAYkuk") == 0 ? null : (byte?)GetInt(row, "SW_PAYkuk"))
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

    // ────────────────────────────────────────────────────────────────
    // 2-6. 매입발주 (DOCFA → purchase_orders + purchase_order_items)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// DOCFA(IU_*)를 읽어 purchase_orders + items 로 이관한다.
    /// 동일 IU_NO를 헤더로, IU_SUN으로 라인 분리.
    /// </summary>
    private async Task<int> MigratePurchaseOrdersFromIUAsync(
        OleDbConnection oleConn, string tenantId, DateTime now,
        Dictionary<int, string> partnerMap, Dictionary<string, string> itemMap,
        IDbTransaction tx, CancellationToken ct)
    {
        var dt = ReadMdbTable(oleConn, "SELECT * FROM DOCFA ORDER BY IU_NO, IU_SUN");
        if (dt.Rows.Count == 0) return 0;

        const string headSql = """
            INSERT INTO purchase_orders
              (po_id, tenant_id, po_no, po_date, partner_id, total_supply, total_vat, total_amount,
               status, remark, created_at, updated_at)
            VALUES
              (@PoId, @TenantId, @PoNo, @PoDate, @PartnerId, @Supply, @Vat, @Total,
               'confirmed', @Remark, @Now, @Now)
            """;
        const string lineSql = """
            INSERT INTO purchase_order_items
              (po_item_id, po_id, tenant_id, seq, item_id, item_name, spec, qty, unit_price,
               supply_amount, vat_amount, total_amount, remark)
            VALUES
              (@LineId, @PoId, @TenantId, @Seq, @ItemId, @ItemName, @Spec, @Qty, @UnitPrice,
               @Supply, @Vat, @Total, @Remark)
            """;

        // 헤더 그룹화
        var groups = dt.AsEnumerable().GroupBy(r => GetStr(r, "IU_NO"));
        int headCount = 0;
        foreach (var g in groups)
        {
            var poNo = g.Key;
            if (string.IsNullOrWhiteSpace(poNo)) continue;
            var first = g.First();
            var buyCode = GetInt(first, "IU_BUY");
            if (!partnerMap.TryGetValue(buyCode, out var partnerId)) continue;

            var poDate = ParseLegacyDate(GetStr(first, "IU_ODT")) ?? now;
            decimal supply = g.Sum(r => GetDec(r, "IU_AMT"));
            decimal vat = g.Sum(r => GetDec(r, "IU_VAT"));
            var poId = Guid.NewGuid().ToString();

            await _db.ExecuteAsync(new CommandDefinition(headSql, new
            {
                PoId = poId, TenantId = tenantId, PoNo = poNo, PoDate = poDate,
                PartnerId = partnerId, Supply = supply, Vat = vat, Total = supply + vat,
                Remark = GetStr(first, "IU_REM"), Now = now
            }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            headCount++;

            int seq = 1;
            foreach (var r in g)
            {
                var pum = GetStr(r, "IU_PUM");
                var ku = GetStr(r, "IU_KU");
                var key = $"{pum}|{ku}";
                itemMap.TryGetValue(key, out var itemId);
                var qty = GetDec(r, "IU_QTY");
                var dan = GetDec(r, "IU_DAN");
                var amt = GetDec(r, "IU_AMT");
                var v = GetDec(r, "IU_VAT");
                await _db.ExecuteAsync(new CommandDefinition(lineSql, new
                {
                    LineId = Guid.NewGuid().ToString(), PoId = poId, TenantId = tenantId,
                    Seq = seq++, ItemId = itemId, ItemName = pum, Spec = ku,
                    Qty = qty, UnitPrice = dan, Supply = amt, Vat = v, Total = amt + v,
                    Remark = GetStr(r, "IU_REM")
                }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("[MDB마이그레이션] 매입발주(DOCFA→purchase_orders) {Count}건 이관 완료", headCount);
        return headCount;
    }

    // ────────────────────────────────────────────────────────────────
    // 2-7. 매출주문 (DOCFO → sales_orders + sales_order_items)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// DOCFO(IO_*)를 읽어 sales_orders + items 로 이관한다.
    /// </summary>
    private async Task<int> MigrateSalesOrdersFromIOAsync(
        OleDbConnection oleConn, string tenantId, DateTime now,
        Dictionary<int, string> partnerMap, Dictionary<string, string> itemMap,
        IDbTransaction tx, CancellationToken ct)
    {
        var dt = ReadMdbTable(oleConn, "SELECT * FROM DOCFO ORDER BY IO_NO, IO_SUN");
        if (dt.Rows.Count == 0) return 0;

        const string headSql = """
            INSERT INTO sales_orders
              (so_id, tenant_id, so_no, so_date, partner_id, total_supply, total_vat, total_amount,
               status, remark, created_at, updated_at)
            VALUES
              (@SoId, @TenantId, @SoNo, @SoDate, @PartnerId, @Supply, @Vat, @Total,
               'confirmed', @Remark, @Now, @Now)
            """;
        const string lineSql = """
            INSERT INTO sales_order_items
              (so_item_id, so_id, tenant_id, seq, item_id, item_name, spec, qty, unit_price,
               supply_amount, vat_amount, total_amount, remark)
            VALUES
              (@LineId, @SoId, @TenantId, @Seq, @ItemId, @ItemName, @Spec, @Qty, @UnitPrice,
               @Supply, @Vat, @Total, @Remark)
            """;

        var groups = dt.AsEnumerable().GroupBy(r => GetStr(r, "IO_NO"));
        int headCount = 0;
        foreach (var g in groups)
        {
            var soNo = g.Key;
            if (string.IsNullOrWhiteSpace(soNo)) continue;
            var first = g.First();
            var buyCode = GetInt(first, "IO_BUY");
            if (!partnerMap.TryGetValue(buyCode, out var partnerId)) continue;

            var soDate = ParseLegacyDate(GetStr(first, "IO_ODT")) ?? now;
            decimal supply = g.Sum(r => GetDec(r, "IO_AMT"));
            decimal vat = g.Sum(r => GetDec(r, "IO_VAT"));
            var soId = Guid.NewGuid().ToString();

            await _db.ExecuteAsync(new CommandDefinition(headSql, new
            {
                SoId = soId, TenantId = tenantId, SoNo = soNo, SoDate = soDate,
                PartnerId = partnerId, Supply = supply, Vat = vat, Total = supply + vat,
                Remark = GetStr(first, "IO_REM"), Now = now
            }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            headCount++;

            int seq = 1;
            foreach (var r in g)
            {
                var pum = GetStr(r, "IO_PUM");
                var ku = GetStr(r, "IO_KU");
                var key = $"{pum}|{ku}";
                itemMap.TryGetValue(key, out var itemId);
                var qty = GetDec(r, "IO_QTY");
                var dan = GetDec(r, "IO_DAN");
                var amt = GetDec(r, "IO_AMT");
                var v = GetDec(r, "IO_VAT");
                await _db.ExecuteAsync(new CommandDefinition(lineSql, new
                {
                    LineId = Guid.NewGuid().ToString(), SoId = soId, TenantId = tenantId,
                    Seq = seq++, ItemId = itemId, ItemName = pum, Spec = ku,
                    Qty = qty, UnitPrice = dan, Supply = amt, Vat = v, Total = amt + v,
                    Remark = GetStr(r, "IO_REM")
                }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("[MDB마이그레이션] 매출주문(DOCFO→sales_orders) {Count}건 이관 완료", headCount);
        return headCount;
    }

    // ────────────────────────────────────────────────────────────────
    // 2-8. 세금계산서 (DOCF4 → tax_invoices, 4품목 행분해)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// DOCF4(TX_*)를 읽어 tax_invoices 로 이관한다.
    /// 한 행에 품목 4개(TX_PUM1~4)가 평면으로 들어있어 합산 후 헤더 한 건으로 INSERT한다.
    /// </summary>
    private async Task<int> MigrateTaxInvoicesAsync(
        OleDbConnection oleConn, string tenantId, DateTime now,
        Dictionary<int, string> partnerMap,
        IDbTransaction tx, CancellationToken ct)
    {
        var dt = ReadMdbTable(oleConn, "SELECT * FROM DOCF4");
        if (dt.Rows.Count == 0) return 0;

        // tax_invoices 컬럼 존재 확인용 — 신규 ERP 스키마에 맞춰 핵심만 INSERT
        const string sql = """
            INSERT INTO tax_invoices
              (tax_invoice_id, tenant_id, invoice_no, invoice_date, invoice_type,
               partner_id, supply_amount, vat_amount, total_amount,
               status, remark, created_at, updated_at)
            VALUES
              (@Id, @TenantId, @No, @Date, @Type,
               @PartnerId, @Supply, @Vat, @Total,
               'confirmed', @Remark, @Now, @Now)
            """;

        int count = 0;
        foreach (DataRow r in dt.Rows)
        {
            var no = GetStr(r, "TX_NO");
            if (string.IsNullOrWhiteSpace(no)) continue;
            var buyCode = GetInt(r, "TX_BUY");
            if (!partnerMap.TryGetValue(buyCode, out var partnerId)) continue;

            var d = ParseLegacyDate(GetStr(r, "TX_PDT")) ?? now;
            // TX_GU = 발행구분 ('1'=매출/발행, '2'=매입/수취 추정)
            var gu = GetStr(r, "TX_GU");
            var typeCode = gu == "2" ? "purchase" : "sales";

            // 4품목 합산
            decimal supply = GetDec(r, "TX_KUM1") + GetDec(r, "TX_KUM2") + GetDec(r, "TX_KUM3") + GetDec(r, "TX_KUM4");
            decimal vat = GetDec(r, "TX_VAT1") + GetDec(r, "TX_VAT2") + GetDec(r, "TX_VAT3") + GetDec(r, "TX_VAT4");

            try
            {
                await _db.ExecuteAsync(new CommandDefinition(sql, new
                {
                    Id = Guid.NewGuid().ToString(), TenantId = tenantId,
                    No = no, Date = d, Type = typeCode,
                    PartnerId = partnerId, Supply = supply, Vat = vat, Total = supply + vat,
                    Remark = GetStr(r, "TX_REM"), Now = now
                }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
                count++;
            }
            catch (Exception ex)
            {
                // 신규 tax_invoices 스키마와 컬럼명이 안 맞으면 로그만 남기고 계속.
                _logger.LogWarning(ex, "[MDB마이그레이션] 세금계산서 {No} INSERT 실패 — 스키마 차이 가능성", no);
            }
        }

        _logger.LogInformation("[MDB마이그레이션] 세금계산서(DOCF4→tax_invoices) {Count}건 이관 완료", count);
        return count;
    }

    // ────────────────────────────────────────────────────────────────
    // 2-9. 어음 (DOCF9 + DOCFQ → bills)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// DOCF9(EU_*) 어음 발행 + DOCFQ(EQ_*) 어음 만기/회수를 읽어 bills 로 이관한다.
    /// EU_CLA: 1=받을어음, 2=지급어음 (추정).
    /// </summary>
    private async Task<int> MigrateBillsAsync(
        OleDbConnection oleConn, string tenantId, DateTime now,
        Dictionary<int, string> partnerMap,
        IDbTransaction tx, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO bills
              (bill_id, tenant_id, bill_type, bill_no, bank_name, issue_place,
               partner_id, partner_name_legacy,
               issue_date, maturity_date, amount, status, remark, legacy_source, created_at, updated_at)
            VALUES
              (@Id, @TenantId, @Type, @No, @Bank, @IssuePlace,
               @PartnerId, @PartnerNameLegacy,
               @IssueDate, @MaturityDate, @Amount, @Status, @Remark, @Source, @Now, @Now)
            """;

        int count = 0;

        // ── DOCF9: 어음 발행 ──
        var dt9 = ReadMdbTable(oleConn, "SELECT * FROM DOCF9");
        foreach (DataRow r in dt9.Rows)
        {
            var no = GetStr(r, "EU_NO");
            if (string.IsNullOrWhiteSpace(no)) continue;
            var amt = GetDec(r, "EU_AMT");
            if (amt <= 0) continue;

            var cla = GetStr(r, "EU_CLA");
            var billType = cla == "2" ? "P" : "R";
            var partnerName = GetStr(r, "EU_BUY");

            await _db.ExecuteAsync(new CommandDefinition(sql, new
            {
                Id = Guid.NewGuid().ToString(), TenantId = tenantId,
                Type = billType, No = no,
                Bank = GetStr(r, "EU_BANK"), IssuePlace = GetStr(r, "EU_BAL"),
                PartnerId = (string?)null, PartnerNameLegacy = partnerName,
                IssueDate = ParseLegacyDate(GetStr(r, "EU_BDT")) ?? now,
                MaturityDate = ParseLegacyDate(GetStr(r, "EU_MDT")),
                Amount = amt, Status = "issued",
                Remark = GetStr(r, "EU_REM"), Source = "DOCF9", Now = now
            }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            count++;
        }

        // ── DOCFQ: 어음 만기/회수 (별건으로 INSERT) ──
        var dtQ = ReadMdbTable(oleConn, "SELECT * FROM DOCFQ");
        foreach (DataRow r in dtQ.Rows)
        {
            var no = GetStr(r, "EQ_NO");
            if (string.IsNullOrWhiteSpace(no)) continue;
            var amt = GetDec(r, "EQ_AMT");
            if (amt <= 0) continue;

            var cla = GetStr(r, "EQ_CLA");
            var billType = cla == "2" ? "P" : "R";

            await _db.ExecuteAsync(new CommandDefinition(sql, new
            {
                Id = Guid.NewGuid().ToString(), TenantId = tenantId,
                Type = billType, No = no,
                Bank = GetStr(r, "EQ_BANK"), IssuePlace = (string?)null,
                PartnerId = (string?)null, PartnerNameLegacy = GetStr(r, "EQ_BUYJ"),
                IssueDate = ParseLegacyDate(GetStr(r, "EQ_BDT")) ?? now,
                MaturityDate = ParseLegacyDate(GetStr(r, "EQ_MDT")),
                Amount = amt, Status = "paid",
                Remark = GetStr(r, "EQ_REM"), Source = "DOCFQ", Now = now
            }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            count++;
        }

        _logger.LogInformation("[MDB마이그레이션] 어음(DOCF9+DOCFQ→bills) {Count}건 이관 완료", count);
        return count;
    }

    // ────────────────────────────────────────────────────────────────
    // 2-10. 카드결제 (DOCCD + DOCCD1 → card_payments + lines)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// DOCCD(CD_*) 카드결제 마스터 + DOCCD1(CD1_*) 라인을 card_payments + lines로 이관한다.
    /// CD_CDNO를 헤더 키로 사용 — 동일 CD_CDNO+CD_KIDT 묶음을 한 결제건으로.
    /// </summary>
    private async Task<int> MigrateCardPaymentsAsync(
        OleDbConnection oleConn, string tenantId, DateTime now,
        Dictionary<int, string> partnerMap,
        IDbTransaction tx, CancellationToken ct)
    {
        var dt = ReadMdbTable(oleConn, "SELECT * FROM DOCCD");
        if (dt.Rows.Count == 0) return 0;

        const string headSql = """
            INSERT INTO card_payments
              (card_payment_id, tenant_id, card_no, card_company, holder_name,
               payment_date, total_amount, installment_amount, installment_months,
               status, remark, legacy_source, created_at, updated_at)
            VALUES
              (@Id, @TenantId, @CardNo, @CardCompany, @HolderName,
               @PayDate, @TotalAmount, @InstallmentAmount, @InstallmentMonths,
               @Status, @Remark, 'DOCCD', @Now, @Now)
            """;
        const string lineSql = """
            INSERT INTO card_payment_lines
              (line_id, card_payment_id, tenant_id, seq, partner_id, partner_name_legacy,
               tx_date, amount, remark)
            VALUES
              (@LineId, @HeaderId, @TenantId, @Seq, @PartnerId, @PartnerNameLegacy,
               @TxDate, @Amount, @Remark)
            """;

        // CD1 라인을 CD_CDNO 기준 사전에 적재
        var dt1 = ReadMdbTable(oleConn, "SELECT * FROM DOCCD1");
        var lineMap = new Dictionary<string, List<DataRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (DataRow lr in dt1.Rows)
        {
            var key = GetStr(lr, "CD1_NO");
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (!lineMap.TryGetValue(key, out var list)) { list = new(); lineMap[key] = list; }
            list.Add(lr);
        }

        int headCount = 0;
        foreach (DataRow r in dt.Rows)
        {
            var cdNo = GetStr(r, "CD_CDNO");
            if (string.IsNullOrWhiteSpace(cdNo)) continue;
            var amt = GetDec(r, "CD_MAMT");
            if (amt <= 0) continue;

            var headerId = Guid.NewGuid().ToString();
            var hal = GetDec(r, "CD_HAL");          // 할부원금 추정
            int months = 0;
            if (hal > 0 && amt > 0 && hal < amt)
            {
                months = (int)Math.Round(amt / hal, MidpointRounding.AwayFromZero);
                if (months < 0 || months > 36) months = 0;
            }

            await _db.ExecuteAsync(new CommandDefinition(headSql, new
            {
                Id = headerId, TenantId = tenantId,
                CardNo = cdNo, CardCompany = GetStr(r, "CD_BANK"), HolderName = GetStr(r, "CD_NAME"),
                PayDate = ParseLegacyDate(GetStr(r, "CD_DT")) ?? now,
                TotalAmount = amt, InstallmentAmount = hal, InstallmentMonths = months,
                Status = "settled", Remark = GetStr(r, "CD_MREM"), Now = now
            }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            headCount++;

            // 라인 (CD_CDNO 키로 lookup → CD1_NO)
            if (lineMap.TryGetValue(cdNo, out var lines))
            {
                int seq = 1;
                foreach (var lr in lines)
                {
                    var sBuy = GetInt(lr, "CD1_SBUY");
                    partnerMap.TryGetValue(sBuy, out var partnerId);
                    await _db.ExecuteAsync(new CommandDefinition(lineSql, new
                    {
                        LineId = Guid.NewGuid().ToString(), HeaderId = headerId, TenantId = tenantId,
                        Seq = seq++, PartnerId = partnerId, PartnerNameLegacy = (string?)null,
                        TxDate = ParseLegacyDate(GetStr(lr, "CD1_SYMD")) ?? ParseLegacyDate(GetStr(lr, "CD1_YMD")) ?? now,
                        Amount = GetDec(lr, "CD1_AMT"),
                        Remark = GetStr(lr, "CD1_JEK")
                    }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
                }
            }
        }

        _logger.LogInformation("[MDB마이그레이션] 카드결제(DOCCD+CD1→card_payments) {Count}건 이관 완료", headCount);
        return headCount;
    }

    // ────────────────────────────────────────────────────────────────
    // 2-11. 은행거래 (BANKF → bank_transactions, INSERT ONLY)
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// BANKF(BK_*) 은행거래 원장을 bank_transactions로 이관한다.
    /// BK_JEN: '1'=입금(차변), '2'=출금(대변) 추정.
    /// </summary>
    private async Task<int> MigrateBankTransactionsAsync(
        OleDbConnection oleConn, string tenantId, DateTime now,
        Dictionary<int, string> partnerMap,
        IDbTransaction tx, CancellationToken ct)
    {
        var dt = ReadMdbTable(oleConn, "SELECT * FROM BANKF");
        if (dt.Rows.Count == 0) return 0;

        const string sql = """
            INSERT INTO bank_transactions
              (bank_tx_id, tenant_id, account_no, bank_name, tx_date, tx_type,
               amount, partner_id, partner_name_legacy, description, remark,
               imported_from, legacy_source, created_at)
            VALUES
              (@Id, @TenantId, @AccountNo, @BankName, @TxDate, @TxType,
               @Amount, @PartnerId, @PartnerNameLegacy, @Description, @Remark,
               'mdb_legacy', 'BANKF', @Now)
            """;

        int count = 0;
        foreach (DataRow r in dt.Rows)
        {
            var accNo = GetStr(r, "BK_NO");
            if (string.IsNullOrWhiteSpace(accNo)) continue;
            var amt = GetDec(r, "BK_AMT");
            if (amt <= 0) continue;

            var jen = GetStr(r, "BK_JEN");
            var txType = jen == "2" ? "2" : "1";   // 1=입금, 2=출금
            var sBuy = GetInt(r, "BK_SBUY");
            partnerMap.TryGetValue(sBuy, out var partnerId);

            await _db.ExecuteAsync(new CommandDefinition(sql, new
            {
                Id = Guid.NewGuid().ToString(), TenantId = tenantId,
                AccountNo = accNo, BankName = GetStr(r, "BK_CLA"),
                TxDate = ParseLegacyDate(GetStr(r, "BK_YMD")) ?? now,
                TxType = txType, Amount = amt,
                PartnerId = partnerId, PartnerNameLegacy = (string?)null,
                Description = GetStr(r, "BK_JEK"),
                Remark = GetStr(r, "BK_cheri"),
                Now = now
            }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            count++;
        }

        _logger.LogInformation("[MDB마이그레이션] 은행거래(BANKF→bank_transactions) {Count}건 이관 완료", count);
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
    private int CountMdbTable(OleDbConnection conn, string tableName)
    {
        try
        {
            using var cmd = new OleDbCommand($"SELECT COUNT(*) FROM [{tableName}]", conn);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[MDB] 테이블 {Table} COUNT 실패 — 테이블 없음으로 처리", tableName);
            return 0;
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

    /// <summary>OLEDB로 MDB 파일을 열어 OleDbConnection을 반환한다.
    /// 핫픽스 2026-05-13: AsyncLocal `_mdbPasswordContext`에서 비번 자동 주입.</summary>
    private static OleDbConnection OpenOleDb(string mdbPath)
    {
        var password = _mdbPasswordContext.Value ?? string.Empty;
        var connStr = string.Format(OleDbConnTemplate, mdbPath, password);
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

    /// <summary>
    /// 레거시 Text8 일자(YYYYMMDD) → DateTime? 변환. 잘못된 값은 null.
    /// W2 D3 (2026-05-12): partners.trade_start_date·business_registration_date 등 보강 컬럼용.
    /// </summary>
    private static DateTime? ParseDateOrNull(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var trimmed = s.Trim();
        if (DateTime.TryParseExact(trimmed, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt;
        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt2))
            return dt2;
        return null;
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

    /// <summary>매입발주(purchase_orders, DOCFA→IU) 이관 건수</summary>
    public int PurchaseOrdersFromIU { get; set; }

    /// <summary>매출(sales_orders, DOCFO→IO) 이관 건수</summary>
    public int SalesOrdersFromIO { get; set; }

    /// <summary>세금계산서(tax_invoices, DOCF4→TX) 이관 건수 (4품목 행분해 기준)</summary>
    public int TaxInvoices { get; set; }

    /// <summary>어음(bills, DOCF9+EQ) 이관 건수</summary>
    public int Bills { get; set; }

    /// <summary>카드결제(card_payments, DOCCD+CD1) 이관 건수</summary>
    public int CardPayments { get; set; }

    /// <summary>은행거래(bank_transactions, BANKF) 이관 건수</summary>
    public int BankTransactions { get; set; }

    /// <summary>전체 이관 건수 합계</summary>
    public int Total => Partners + Items + BomHeaders + Employees
                        + SalesOrders + PurchaseOrders + StockLedger
                        + Collections + Cashbook + Expenses
                        + PurchaseOrdersFromIU + SalesOrdersFromIO + TaxInvoices
                        + Bills + CardPayments + BankTransactions;

    public override string ToString()
    {
        return $"업체:{Partners}, 상품:{Items}, BOM:{BomHeaders}, 사원:{Employees}, " +
               $"판매:{SalesOrders}, 매입:{PurchaseOrders}, 입출고:{StockLedger}, " +
               $"수금:{Collections}, 경비:{Cashbook}, 전표:{Expenses}, " +
               $"매입(IU):{PurchaseOrdersFromIU}, 매출(IO):{SalesOrdersFromIO}, " +
               $"세금계산서:{TaxInvoices}, 어음:{Bills}, 카드:{CardPayments}, 은행:{BankTransactions} [합계:{Total}]";
    }
}
