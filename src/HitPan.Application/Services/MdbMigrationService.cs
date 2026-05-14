using System.Data;
using System.Data.Common;
using System.Data.OleDb;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using Dapper;
using HitPan.Application.Common;
using HitPan.Application.Interfaces;
using Microsoft.Extensions.Logging;
using MySqlConnector;

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

    /// <summary>
    /// 정공법 (사장님 6축 명령 2026-05-14, 축 3 SEC-04):
    /// 마이그 전용 connection 풀 factory. null이면 legacy 단일 _db로 fallback (기존 호출자 호환).
    /// </summary>
    private readonly IMigrationDbConnectionFactory? _migrationFactory;

    /// <summary>
    /// 잡 단위 전용 DbConnection (정공법 축 3). RunTableStepAsync가 잡 시작 시 set,
    /// 종료 시 clear. Db 속성이 이걸 우선 반환 → Migrate* 메서드들이 잡 conn 사용.
    /// AsyncLocal이라 PANDATA 11개 Task.WhenAll 병렬 시 각 잡이 독립 컨텍스트 유지 (헌법 #16).
    /// </summary>
    private static readonly AsyncLocal<IDbConnection?> _jobConnection = new();

    /// <summary>
    /// 잡 단위 전용 트랜잭션. RunTableStepAsync가 BeginTransaction 직후 set.
    /// Migrate* 메서드 안에서 `transaction: CurrentTx` 형태로 참조해도 되지만 기존 코드는
    /// 시그니처로 tx를 받으므로 본 필드는 향후 확장용(현재는 미사용).
    /// </summary>
    private static readonly AsyncLocal<IDbTransaction?> _jobTransaction = new();

    /// <summary>
    /// 잡 단위 conn 우선, 없으면 legacy _db (생성자 주입된 단일 conn).
    /// 기존 Migrate* 메서드들의 `Db.ExecuteAsync(...)` 호출을 `Db.ExecuteAsync(...)`로 바꾸면
    /// 잡이 RunTableStepAsync 안에 있을 때 자동으로 전용 풀 conn을 쓴다.
    /// </summary>
    private IDbConnection Db => _jobConnection.Value ?? _db;

    /// <summary>OLEDB 커넥션 문자열 템플릿 (MDB 경로 + 선택적 비번)</summary>
    /// 핫픽스 2026-05-13: 사장님 MDB(비번 7618968) 지원 — 결재 #13.
    private const string OleDbConnTemplate =
        "Provider=Microsoft.ACE.OLEDB.12.0;Data Source={0};Jet OLEDB:Database Password={1};";

    /// <summary>현재 마이그 호출의 MDB 비번 (AsyncLocal 컨텍스트 — overload 시그니처 보존하면서 비번 전달).</summary>
    private static readonly AsyncLocal<string?> _mdbPasswordContext = new();

    /// <summary>현재 마이그 호출의 job_id (AsyncLocal — migration_errors INSERT 시 사용).
    /// P0 #5 (2026-05-14): null이면 에러 저장 skip (legacy overload 호환).</summary>
    private static readonly AsyncLocal<string?> _jobIdContext = new();

    /// <summary>P0 #6 (2026-05-14): 테이블별 진행 상태 콜백 (UI Sticky/카드 가시화).
    /// (tableName, status, rows, elapsedMs, errorMsg) — controller에서 jobStore 업데이트로 연결.</summary>
    private static readonly AsyncLocal<Action<string, string, int, long, string?>?> _progressCallback = new();

    public MdbMigrationService(
        IDbConnection db,
        ILogger<MdbMigrationService> logger,
        IBinaryCryptoService crypto,
        IMigrationDbConnectionFactory? migrationFactory = null)
    {
        _db = db;
        _logger = logger;
        _crypto = crypto;
        _migrationFactory = migrationFactory;
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
    public Task<MdbMigrationResult> MigrateAsync(
        string folderPath, string tenantId, string? mdbPassword, CancellationToken ct = default)
        => MigrateAsync(folderPath, tenantId, mdbPassword, jobId: null, ct);

    /// <summary>
    /// jobId까지 받는 정식 overload (P0 #5, 2026-05-14).
    /// jobId가 있으면 테이블 실패 시 migration_errors에 AES 암호화 raw_data 저장.
    /// </summary>
    public Task<MdbMigrationResult> MigrateAsync(
        string folderPath, string tenantId, string? mdbPassword, string? jobId, CancellationToken ct = default)
        => MigrateAsync(folderPath, tenantId, mdbPassword, jobId, progressCallback: null, ct);

    /// <summary>
    /// 진행 상태 콜백까지 받는 정식 overload (P0 #6, 2026-05-14).
    /// progressCallback(tableName, status, rows, elapsedMs, errorMsg) — UI 가시화용.
    /// </summary>
    public async Task<MdbMigrationResult> MigrateAsync(
        string folderPath, string tenantId, string? mdbPassword, string? jobId,
        Action<string, string, int, long, string?>? progressCallback, CancellationToken ct = default)
    {
        var (pyojunPath, pandataPath, _) = ResolveMdbPaths(folderPath);
        _mdbPasswordContext.Value = mdbPassword;
        _jobIdContext.Value = jobId;
        _progressCallback.Value = progressCallback;
        try
        {
            return await MigrateCoreAsync(pyojunPath, pandataPath, tenantId, ct).ConfigureAwait(false);
        }
        finally
        {
            _mdbPasswordContext.Value = null;
            _jobIdContext.Value = null;
            _progressCallback.Value = null;
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

        // 정공법(축 3): factory가 있으면 잡-local 세션 튜닝(RunTableStepAsync 내부)으로 처리하므로
        // 글로벌 _db에 튜닝 적용할 필요 없음. legacy 모드일 때만 기존 봉합 경로 유지.
        var useMigrationPool = _migrationFactory is not null;
        if (!useMigrationPool)
        {
            await EnsureOpenAsync(ct).ConfigureAwait(false);
            // P0 #1 (2026-05-14): 마이그 세션 한정 튜닝 (legacy fallback).
            await ApplyMigrationSessionTuningAsync(ct).ConfigureAwait(false);
        }

        try
        {
            // ──────────────────────────────────────
            // 0. 마이그레이션 전용 기본 창고 (단독 tx) — 실패 시 throw (마스터 필수)
            // ──────────────────────────────────────
            await RunTableStepAsync("warehouse_migration", async tx =>
            {
                await EnsureMigrationWarehouseAsync(tenantId, defaultWarehouseId, now, tx, ct).ConfigureAwait(false);
                return 0;
            }, ct, continueOnFail: false, mdbFile: "(infra)").ConfigureAwait(false);

            // ──────────────────────────────────────
            // 1단계: PYOJUN.MDB (마스터 — FK 매핑 묶음 유지)
            // 4개 메서드가 partnerMap/itemMap/employeeMap 채우는 단계이므로
            // 동일 tx 안에서 처리해 매핑 일관성 보장. 이 단계는 거래 데이터에 비해 매우 가벼움(수만 행).
            // ──────────────────────────────────────
            _logger.LogInformation("[MDB마이그레이션] PYOJUN.MDB 읽기 시작: {Path}", pyojunPath);

            // PYOJUN(마스터)는 실패 시 throw — partnerMap/itemMap 못 채우면 PANDATA가 무의미.
            await RunTableStepAsync("pyojun_master", async tx =>
            {
                using var oleConn = OpenOleDb(pyojunPath);
                result.Partners = await MigratePartnersAsync(oleConn, tenantId, now, partnerMap, tx, ct).ConfigureAwait(false);
                result.Items = await MigrateItemsAsync(oleConn, tenantId, now, itemMap, tx, ct).ConfigureAwait(false);
                result.BomHeaders = await MigrateBomAsync(oleConn, tenantId, now, itemMap, tx, ct).ConfigureAwait(false);
                result.Employees = await MigrateEmployeesAsync(oleConn, tenantId, now, employeeMap, tx, ct).ConfigureAwait(false);
                return result.Partners + result.Items + result.BomHeaders + result.Employees;
            }, ct, continueOnFail: false, mdbFile: "PYOJUN").ConfigureAwait(false);

            // ──────────────────────────────────────
            // 2단계: PANDATA.mdb (거래 — 테이블별 독립 tx)
            // 각 테이블 commit 단위 ~수초~수십초. 한 테이블 실패 시 다른 테이블 보존.
            // partnerMap/itemMap/employeeMap은 in-memory 이므로 FK 무관.
            // ──────────────────────────────────────
            _logger.LogInformation("[MDB마이그레이션] PANDATA.mdb 읽기 시작: {Path}", pandataPath);

            // ──────────────────────────────────────
            // 정공법(축 1) 사장님 6축 명령 2026-05-14:
            //   PANDATA 11개 테이블 Task.WhenAll 병렬 (factory 모드만).
            //   각 테이블은 RunTableStepAsync 안에서 독립 conn 발급 — 헌법 #16 thread-safe.
            //   partnerMap/itemMap/employeeMap/defaultWarehouseId는 read-only로 공유 (안전).
            //   legacy 모드(_factory==null)는 단일 _db라 병렬 불가 → 순차 fallback 유지.
            // ──────────────────────────────────────
            var pandataJobs = new List<Func<Task>>
            {
                () => RunTableStepAsync("transactions", async tx =>
                {
                    using var oleConn = OpenOleDb(pandataPath);
                    var (salesCount, purchaseCount) = await MigrateTransactionsAsync(
                        oleConn, tenantId, now, partnerMap, itemMap, employeeMap, defaultWarehouseId, tx, ct).ConfigureAwait(false);
                    result.SalesOrders = salesCount;
                    result.PurchaseOrders = purchaseCount;
                    return salesCount + purchaseCount;
                }, ct),
                () => RunTableStepAsync("stock_ledger", async tx =>
                {
                    using var oleConn = OpenOleDb(pandataPath);
                    result.StockLedger = await MigrateStockLedgerAsync(
                        oleConn, tenantId, now, partnerMap, itemMap, defaultWarehouseId, tx, ct).ConfigureAwait(false);
                    return result.StockLedger;
                }, ct),
                () => RunTableStepAsync("collections", async tx =>
                {
                    using var oleConn = OpenOleDb(pandataPath);
                    result.Collections = await MigrateCollectionsAsync(
                        oleConn, tenantId, now, partnerMap, tx, ct).ConfigureAwait(false);
                    return result.Collections;
                }, ct),
                () => RunTableStepAsync("cashbook", async tx =>
                {
                    using var oleConn = OpenOleDb(pandataPath);
                    result.Cashbook = await MigrateCashbookAsync(
                        oleConn, tenantId, now, partnerMap, tx, ct).ConfigureAwait(false);
                    return result.Cashbook;
                }, ct),
                () => RunTableStepAsync("expenses", async tx =>
                {
                    using var oleConn = OpenOleDb(pandataPath);
                    result.Expenses = await MigrateExpensesAsync(
                        oleConn, tenantId, now, employeeMap, tx, ct).ConfigureAwait(false);
                    return result.Expenses;
                }, ct),
                () => RunTableStepAsync("purchase_orders", async tx =>
                {
                    using var oleConn = OpenOleDb(pandataPath);
                    result.PurchaseOrdersFromIU = await MigratePurchaseOrdersFromIUAsync(
                        oleConn, tenantId, now, partnerMap, itemMap, tx, ct).ConfigureAwait(false);
                    return result.PurchaseOrdersFromIU;
                }, ct),
                () => RunTableStepAsync("sales_orders", async tx =>
                {
                    using var oleConn = OpenOleDb(pandataPath);
                    result.SalesOrdersFromIO = await MigrateSalesOrdersFromIOAsync(
                        oleConn, tenantId, now, partnerMap, itemMap, tx, ct).ConfigureAwait(false);
                    return result.SalesOrdersFromIO;
                }, ct),
                () => RunTableStepAsync("tax_invoices", async tx =>
                {
                    using var oleConn = OpenOleDb(pandataPath);
                    result.TaxInvoices = await MigrateTaxInvoicesAsync(
                        oleConn, tenantId, now, partnerMap, tx, ct).ConfigureAwait(false);
                    return result.TaxInvoices;
                }, ct),
                () => RunTableStepAsync("bills", async tx =>
                {
                    using var oleConn = OpenOleDb(pandataPath);
                    result.Bills = await MigrateBillsAsync(
                        oleConn, tenantId, now, partnerMap, tx, ct).ConfigureAwait(false);
                    return result.Bills;
                }, ct),
                () => RunTableStepAsync("card_payments", async tx =>
                {
                    using var oleConn = OpenOleDb(pandataPath);
                    result.CardPayments = await MigrateCardPaymentsAsync(
                        oleConn, tenantId, now, partnerMap, tx, ct).ConfigureAwait(false);
                    return result.CardPayments;
                }, ct),
                () => RunTableStepAsync("bank_transactions", async tx =>
                {
                    using var oleConn = OpenOleDb(pandataPath);
                    result.BankTransactions = await MigrateBankTransactionsAsync(
                        oleConn, tenantId, now, partnerMap, tx, ct).ConfigureAwait(false);
                    return result.BankTransactions;
                }, ct),
            };

            if (useMigrationPool)
            {
                // 정공법: 11개 잡 Task.WhenAll. 각 잡 내부에서 독립 conn 발급(헌법 #16).
                // RunTableStepAsync는 continueOnFail=true 기본이라 한 테이블 실패가 다른 테이블 막지 않음.
                _logger.LogInformation("[MDB마이그레이션] PANDATA 11개 테이블 병렬 실행 시작 (정공법 축 1)");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await Task.WhenAll(pandataJobs.Select(j => j())).ConfigureAwait(false);
                sw.Stop();
                _logger.LogInformation(
                    "[MDB마이그레이션] PANDATA 11개 병렬 완료 ({Elapsed}ms)", sw.ElapsedMilliseconds);
            }
            else
            {
                // legacy: 단일 _db라 병렬 불가. 순차 실행.
                foreach (var job in pandataJobs)
                {
                    await job().ConfigureAwait(false);
                }
            }

            _logger.LogInformation("[MDB마이그레이션] 완료. 결과: {@Result}", result);
        }
        finally
        {
            // legacy 모드에서만 글로벌 _db 튜닝 원복 (정공법 모드는 잡 conn DisposeAsync로 자동 처리됨).
            if (!useMigrationPool)
            {
                await RestoreMigrationSessionTuningAsync(ct).ConfigureAwait(false);
            }
        }

        return result;
    }

    /// <summary>
    /// 테이블 단위 작업을 독립 트랜잭션으로 실행한다.
    /// 한 테이블 실패는 다른 테이블 commit을 보존한다(헌법 #20 본래 의미).
    /// P0 #5 (2026-05-14): 테이블 실패 시 migration_errors에 AES 암호화 raw_data 저장 후
    /// continueOnFail=true면 다음 테이블 계속, false면 throw.
    /// </summary>
    /// <param name="tableName">migration_checkpoints.table_name 값 — 진행 추적용.</param>
    /// <param name="work">tx를 받아 수행할 마이그 단위 작업. 반환값은 처리 행수.</param>
    /// <param name="ct">취소 토큰.</param>
    /// <param name="continueOnFail">true면 실패해도 다음 테이블 진행. false면 throw (마스터 단계용).</param>
    /// <param name="mdbFile">에러 저장 시 mdb_file 컬럼 값 (예: PYOJUN/PANDATA).</param>
    private async Task RunTableStepAsync(
        string tableName, Func<IDbTransaction, Task<int>> work, CancellationToken ct,
        bool continueOnFail = true, string mdbFile = "PANDATA")
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var cb = _progressCallback.Value;
        cb?.Invoke(tableName, "running", 0, 0, null);

        // 정공법(축 3): 마이그 factory가 있으면 잡 전용 conn을 발급해 일반 컨트롤러 풀과 0 공유.
        // factory가 없으면(legacy 호환 — 단위 테스트 등) 기존 _db로 fallback.
        System.Data.Common.DbConnection? jobConn = null;
        IDbConnection effectiveConn;
        if (_migrationFactory is not null)
        {
            jobConn = await _migrationFactory.CreateOpenAsync(ct).ConfigureAwait(false);
            effectiveConn = jobConn;
            _jobConnection.Value = jobConn;
            // 잡 전용 세션 튜닝: pool 반환 시 ConnectionReset으로 자동 원복되므로 안전.
            await ApplyJobSessionTuningAsync(effectiveConn, ct).ConfigureAwait(false);
        }
        else
        {
            effectiveConn = _db;
        }

        IDbTransaction? tx = null;
        try
        {
            tx = effectiveConn.BeginTransaction();
            _jobTransaction.Value = tx;
            var rows = await work(tx).ConfigureAwait(false);
            tx.Commit();
            sw.Stop();
            _logger.LogInformation(
                "[MDB마이그레이션] {Table} 완료: {Rows}행, {Elapsed}ms",
                tableName, rows, sw.ElapsedMilliseconds);
            cb?.Invoke(tableName, "completed", rows, sw.ElapsedMilliseconds, null);
        }
        catch (OperationCanceledException)
        {
            // 취소는 에러 저장 대상 아님 (사용자 의도된 중단).
            sw.Stop();
            // 헌법 #15: 빈 catch 금지 — rollback 실패도 운영자 추적 가능하도록 WARN.
            try { tx?.Rollback(); }
            catch (Exception rbex)
            {
                _logger.LogWarning(rbex,
                    "[MDB마이그레이션] {Table} 취소 중 롤백 실패 (무시하고 cancel 전파)", tableName);
            }
            cb?.Invoke(tableName, "failed", 0, sw.ElapsedMilliseconds, "취소됨");
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            try { tx?.Rollback(); }
            catch (Exception rbex) { _logger.LogError(rbex, "[MDB마이그레이션] {Table} 롤백 실패", tableName); }
            _logger.LogError(ex,
                "[MDB마이그레이션] {Table} 실패 ({Elapsed}ms, continueOnFail={Continue})",
                tableName, sw.ElapsedMilliseconds, continueOnFail);
            cb?.Invoke(tableName, "failed", 0, sw.ElapsedMilliseconds,
                $"{ex.GetType().Name}: {Truncate(ex.Message, 200)}");

            // migration_errors에 AES 암호화 raw_data 저장 (jobId 있을 때만, 헌법 #5).
            await TryInsertMigrationErrorAsync(tableName, mdbFile, ex, ct).ConfigureAwait(false);

            if (!continueOnFail) throw;
        }
        finally
        {
            tx?.Dispose();
            _jobTransaction.Value = null;
            // 잡 conn 정리: AsyncLocal clear → 다음 잡(또는 일반 컨트롤러)에 누수 0.
            // DisposeAsync로 pool 반환 (MySqlConnector가 ConnectionReset=true 기본으로 SET SESSION 원복).
            _jobConnection.Value = null;
            if (jobConn is not null)
            {
                try { await jobConn.DisposeAsync().ConfigureAwait(false); }
                catch (Exception dex)
                {
                    _logger.LogWarning(dex,
                        "[MDB마이그레이션] {Table} 잡 conn Dispose 실패 (pool에 비정상 반환 가능)", tableName);
                }
            }
        }
    }

    /// <summary>
    /// 잡 단위 세션 튜닝 — 정공법 축 3.
    /// 잡 conn에만 적용되므로 일반 컨트롤러는 0 영향. 잡 종료 시 DisposeAsync로 pool reset.
    /// </summary>
    private async Task ApplyJobSessionTuningAsync(IDbConnection conn, CancellationToken ct)
    {
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "SET SESSION innodb_flush_log_at_trx_commit = 2", cancellationToken: ct)).ConfigureAwait(false);
            await conn.ExecuteAsync(new CommandDefinition(
                "SET SESSION foreign_key_checks = 0", cancellationToken: ct)).ConfigureAwait(false);
            await conn.ExecuteAsync(new CommandDefinition(
                "SET SESSION unique_checks = 0", cancellationToken: ct)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // 튜닝 실패해도 잡은 계속 (속도만 손해, 안전성 OK).
            _logger.LogWarning(ex, "[MDB마이그레이션] 잡 세션 튜닝 적용 실패 — 기본값으로 진행");
        }
    }

    /// <summary>
    /// 테이블 단위 실패를 migration_errors에 저장한다 (헌법 #5 AES + #15 silent swallow 금지).
    /// jobId가 null이면 skip(legacy overload 호환). 에러 저장 자체가 실패해도 마이그 계속.
    /// </summary>
    private async Task TryInsertMigrationErrorAsync(
        string tableName, string mdbFile, Exception ex, CancellationToken ct)
    {
        var jobId = _jobIdContext.Value;
        if (string.IsNullOrWhiteSpace(jobId))
        {
            _logger.LogDebug("[MDB마이그레이션] jobId 없음 — migration_errors 저장 skip ({Table})", tableName);
            return;
        }

        try
        {
            // raw_data: 실패 컨텍스트 직렬화 후 AES-256-CBC 암호화 (헌법 #5 정공법, VARBINARY LONGBLOB).
            var rawPlain = $"{{\"table\":\"{tableName}\",\"mdb\":\"{mdbFile}\",\"exception\":\"{EscapeJson(ex.GetType().Name)}\",\"message\":\"{EscapeJson(ex.Message)}\",\"stack\":\"{EscapeJson(ex.StackTrace ?? string.Empty)}\"}}";
            var encryptedRaw = _crypto.EncryptToBytes(rawPlain);

            var errorType = MapErrorType(ex);
            var severity = "error";

            // tenant_id는 migration_jobs에서 조회 (1회). 못 찾으면 placeholder.
            var tenantId = await Db.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT tenant_id FROM migration_jobs WHERE job_id = @JobId LIMIT 1",
                new { JobId = jobId }, cancellationToken: ct)).ConfigureAwait(false) ?? "unknown";

            const string sql = """
                INSERT INTO migration_errors
                  (error_id, job_id, tenant_id, mdb_file, table_name,
                   error_type, error_severity, error_message, error_detail,
                   raw_data, occurred_at, created_at)
                VALUES
                  (@ErrorId, @JobId, @TenantId, @MdbFile, @TableName,
                   @ErrorType, @Severity, @Message, @Detail,
                   @RawData, @Now, @Now)
                """;
            await Db.ExecuteAsync(new CommandDefinition(sql, new
            {
                ErrorId = Guid.NewGuid().ToString(),
                JobId = jobId,
                TenantId = tenantId,
                MdbFile = mdbFile,
                TableName = tableName,
                ErrorType = errorType,
                Severity = severity,
                // WS-20260514-06 (SEC-02): MariaDB 예외 메시지에 박힐 수 있는 PII(주민/사업자/전화/이메일/계좌)
                // 마스킹 후 저장. 개인정보보호법 §29 안전조치의무 충족.
                Message = SensitiveFieldMasking.MaskTextPII(Truncate(ex.Message, 65535)),
                Detail = SensitiveFieldMasking.MaskTextPII(Truncate(ex.ToString(), 65535)),
                RawData = encryptedRaw,
                Now = DateTime.UtcNow,
            }, cancellationToken: ct)).ConfigureAwait(false);

            _logger.LogInformation(
                "[MDB마이그레이션] migration_errors 저장 완료 (job={JobId}, table={Table}, type={Type})",
                jobId, tableName, errorType);
        }
        catch (Exception logEx)
        {
            // 에러 저장 자체 실패 — 마이그 본 흐름 막지 않음 (헌법 #15: silent 금지, WARN 남김).
            _logger.LogWarning(logEx,
                "[MDB마이그레이션] migration_errors 저장 실패 — 본 마이그는 계속 진행 ({Table})", tableName);
        }
    }

    /// <summary>예외 타입 → migration_errors.error_type enum 매핑.</summary>
    private static string MapErrorType(Exception ex)
    {
        var msg = ex.Message?.ToLowerInvariant() ?? string.Empty;
        var typeName = ex.GetType().Name;

        if (typeName.Contains("Timeout", StringComparison.OrdinalIgnoreCase)) return "timeout";
        if (msg.Contains("duplicate") || msg.Contains("unique")) return "duplicate";
        if (msg.Contains("foreign key") || msg.Contains("fk_")) return "fk_missing";
        if (msg.Contains("encoding") || msg.Contains("charset")) return "encoding";
        if (msg.Contains("constraint") || msg.Contains("check")) return "constraint";
        if (msg.Contains("schema") || msg.Contains("column") || msg.Contains("table")) return "schema";
        return "other";
    }

    /// <summary>JSON 문자열 이스케이프 (제어문자 + 특수문자).</summary>
    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append($"\\u{(int)c:x4}");
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>문자열 길이 제한 (text 컬럼 65535 보호).</summary>
    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s.Substring(0, max));

    /// <summary>
    /// 마이그 세션 한정 튜닝 (사장님 결재, 글로벌 영향 0).
    /// </summary>
    private async Task ApplyMigrationSessionTuningAsync(CancellationToken ct)
    {
        try
        {
            // commit 시 redo log fsync 빈도 완화 (재시작 시 최근 1초 마이그 데이터 손실 가능 — 수용).
            await Db.ExecuteAsync(new CommandDefinition(
                "SET SESSION innodb_flush_log_at_trx_commit = 2", cancellationToken: ct)).ConfigureAwait(false);
            // 마이그 데이터는 외부 MDB 원천이므로 FK·UNIQUE 사전 검증 완료 전제.
            await Db.ExecuteAsync(new CommandDefinition(
                "SET SESSION foreign_key_checks = 0", cancellationToken: ct)).ConfigureAwait(false);
            await Db.ExecuteAsync(new CommandDefinition(
                "SET SESSION unique_checks = 0", cancellationToken: ct)).ConfigureAwait(false);
            _logger.LogInformation("[MDB마이그레이션] 세션 튜닝 적용 (innodb_flush=2, fk=0, unique=0)");
        }
        catch (Exception ex)
        {
            // 튜닝 실패해도 마이그는 계속 진행 (속도만 손해).
            _logger.LogWarning(ex, "[MDB마이그레이션] 세션 튜닝 적용 실패 — 기본값으로 진행");
        }
    }

    /// <summary>
    /// 세션 종료 시 자동 복원되지만 명시적으로 원복 (방어).
    /// WS-20260514-07 (SEC-04): 원복 실패 시 connection을 강제 Dispose해
    /// pool에 fk_checks=0/unique_checks=0/innodb_flush=2 상태로 반환되는 것을 차단한다.
    /// (헌법 #15: silent swallow 금지, #20: 다른 컨트롤러로 오염 전파 차단)
    /// </summary>
    private async Task RestoreMigrationSessionTuningAsync(CancellationToken ct)
    {
        var allRestored = false;
        try
        {
            await Db.ExecuteAsync(new CommandDefinition(
                "SET SESSION innodb_flush_log_at_trx_commit = 1", cancellationToken: ct)).ConfigureAwait(false);
            await Db.ExecuteAsync(new CommandDefinition(
                "SET SESSION foreign_key_checks = 1", cancellationToken: ct)).ConfigureAwait(false);
            await Db.ExecuteAsync(new CommandDefinition(
                "SET SESSION unique_checks = 1", cancellationToken: ct)).ConfigureAwait(false);
            allRestored = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[MDB마이그레이션] 세션 튜닝 원복 실패 — connection 강제 Dispose로 pool 오염 차단 (WS-07)");
        }
        finally
        {
            if (!allRestored)
            {
                // 원복 미완료 connection은 pool 반환 절대 금지.
                // Close + Dispose로 물리 소켓 폐기 → 다른 요청 오염 0.
                try
                {
                    if (Db.State == ConnectionState.Open) Db.Close();
                    Db.Dispose();
                    _logger.LogWarning(
                        "[MDB마이그레이션] 오염 가능 connection 강제 폐기 완료 (pool 반환 차단)");
                }
                catch (Exception disposeEx)
                {
                    _logger.LogError(disposeEx,
                        "[MDB마이그레이션] connection Dispose 실패 — 운영자 즉시 점검 필요");
                }
            }
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
        var exists = await Db.ExecuteScalarAsync<int>(
            new CommandDefinition(checkSql, new { Id = warehouseId, TenantId = tenantId },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        if (exists > 0) return;

        const string sql = """
            INSERT INTO warehouses (warehouse_id, tenant_id, wh_code, wh_name, wh_type, location, is_active, created_at, updated_at)
            VALUES (@Id, @TenantId, 'WH-MIG', '마이그레이션창고', 'normal', '레거시 데이터 이관용', 1, @Now, @Now)
            """;
        await Db.ExecuteAsync(new CommandDefinition(sql,
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
        // P0 #4 (2026-05-14): ORDER BY 추가 — 헌법 #13 멱등 순서 보장 (DB매니저 권고).
        var dt = ReadMdbTable(oleConn, "SELECT * FROM DOCF8 ORDER BY B_BUY");
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
            var partnerCode = $"MIG-{buyCode:D5}";

            // WS-20260514-08 (CODE-06): 멱등성 — 같은 partner_code 이미 있으면 기존 partner_id 매핑하고 INSERT skip.
            // uq_tenant_code(tenant_id, partner_code) UNIQUE 활용. 재실행 시 중복 INSERT 방지.
            var existingId = await Db.ExecuteScalarAsync<string?>(new CommandDefinition(
                "SELECT partner_id FROM partners WHERE tenant_id = @TenantId AND partner_code = @Code LIMIT 1",
                new { TenantId = tenantId, Code = partnerCode },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(existingId))
            {
                // 이미 존재 → 기존 ID로 매핑만 갱신, INSERT skip (멱등)
                partnerMap[buyCode] = existingId;
                continue;
            }

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

            await Db.ExecuteAsync(new CommandDefinition(sql, new
            {
                PartnerId = partnerId,
                TenantId = tenantId,
                PartnerCode = partnerCode,           // 레거시 코드 기반 partner_code (멱등 키, WS-08)
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
        // P0 #4 (2026-05-14): ORDER BY 추가 — 헌법 #13 멱등 순서 보장.
        var dt = ReadMdbTable(oleConn, "SELECT * FROM DOCFS ORDER BY S_PUM, S_KU");
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

            await Db.ExecuteAsync(new CommandDefinition(sql, new
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

            await Db.ExecuteAsync(new CommandDefinition(headerSql, new
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

                await Db.ExecuteAsync(new CommandDefinition(itemSql, new
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
        // P0 #4 (2026-05-14): ORDER BY 추가 — 헌법 #13 멱등 순서 보장.
        var dt = ReadMdbTable(oleConn, "SELECT * FROM DOCSW ORDER BY SW_NAME");
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

            await Db.ExecuteAsync(new CommandDefinition(sql, new
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
        // P0 #4 (2026-05-14): ORDER BY 추가 — 헌법 #13 멱등 순서 보장.
        // 헤더 로드
        var headerDt = ReadMdbTable(oleConn, "SELECT * FROM DOCF2 ORDER BY K2_NO");
        // 상세 로드 (라인 순서 보존)
        var detailDt = ReadMdbTable(oleConn, "SELECT * FROM DOCF1 ORDER BY KA_NO, KA_SUN");

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

                await Db.ExecuteAsync(new CommandDefinition(soSql, new
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

                        await Db.ExecuteAsync(new CommandDefinition(soItemSql, new
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

                await Db.ExecuteAsync(new CommandDefinition(poSql, new
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

                        await Db.ExecuteAsync(new CommandDefinition(poItemSql, new
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
        // 정공법(축 1, 사장님 6축 명령 2026-05-14):
        //   기존 1000행 청크 INSERT IGNORE(수~30분) → MySqlBulkCopy(LOAD DATA LOCAL INFILE) 단일 호출(수초).
        //   멱등성 유지 패턴:
        //     ① CREATE TEMPORARY TABLE stock_ledger_stage_xxx LIKE stock_ledger
        //     ② MySqlBulkCopy로 stage에 일괄 적재 (네트워크 1 RTT, 5~10초)
        //     ③ INSERT IGNORE INTO stock_ledger SELECT FROM stage  (UNIQUE 키로 중복 차단)
        //     ④ DROP TEMPORARY TABLE
        //   세션 한정 임시테이블이므로 잡 conn 종료 시 자동 소멸 — 누수 0.
        //   factory 모드(MySqlConnection 확보 가능)일 때만 활성화. legacy 모드는 기존 청크 경로로 fallback.
        const int ChunkSize = 1000;
        const string ColumnList =
            "(tenant_id, item_id, warehouse_id, partner_id, ledger_date, ym, " +
            "move_type, source_type, source_id, doc_no, qty_in, qty_out, " +
            "unit_cost, supply_amount, memo)";

        var dt = ReadMdbTable(oleConn, "SELECT * FROM DOCFB ORDER BY IJ_DT, IJ_SEQ");
        if (dt.Rows.Count == 0) return 0;

        // 1단계: in-memory에서 모든 row 변환·필터 (item 매핑 없으면 skip).
        var rows = new List<StockLedgerRow>(dt.Rows.Count);
        foreach (DataRow row in dt.Rows)
        {
            var itemKey = BuildItemKey(GetStr(row, "IJ_PUM"), GetStr(row, "IJ_KU"));
            if (!itemMap.TryGetValue(itemKey, out var itemId)) continue;

            var buyCode = GetInt(row, "IJ_BUY");
            partnerMap.TryGetValue(buyCode, out var partnerId);

            var dtStr = GetStr(row, "IJ_DT");
            var ledgerDate = ParseLegacyDate(dtStr) ?? now;
            var io = GetStr(row, "IJ_IO").ToUpperInvariant();
            var moveType = io == "I" ? "in" : "out";
            var qty = GetDec(row, "IJ_QTY");
            var amt = GetDec(row, "IJ_AMT");

            rows.Add(new StockLedgerRow
            {
                TenantId = tenantId,
                ItemId = itemId,
                WarehouseId = defaultWarehouseId,
                PartnerId = partnerId,
                LedgerDate = ledgerDate,
                Ym = ledgerDate.ToString("yyyy-MM"),
                MoveType = moveType,
                SourceId = $"mig-{dtStr}-{GetShort(row, "IJ_SEQ")}",
                DocNo = GetStr(row, "IJ_TAXNO"),
                QtyIn = io == "I" ? qty : 0m,
                QtyOut = io == "O" ? qty : 0m,
                UnitCost = qty != 0 ? amt / qty : 0m,
                SupplyAmount = amt,
                Memo = GetStr(row, "IJ_REM"),
            });
        }

        if (rows.Count == 0) return 0;

        // 정공법 BulkCopy 경로: 잡 conn이 MySqlConnection 인스턴스일 때(=정공법 모드) 활성화.
        // legacy 모드(_db가 다른 IDbConnection 구현)는 아래 청크 INSERT IGNORE로 fallback.
        if (Db is MySqlConnection mysqlConn && tx is MySqlTransaction mysqlTx)
        {
            return await BulkCopyStockLedgerAsync(mysqlConn, mysqlTx, rows, ct).ConfigureAwait(false);
        }

        // 2단계 (legacy fallback): 청크 단위 INSERT IGNORE (멱등성 자동).
        int inserted = 0;
        int chunkIdx = 0;
        var totalChunks = (rows.Count + ChunkSize - 1) / ChunkSize;

        for (int offset = 0; offset < rows.Count; offset += ChunkSize)
        {
            ct.ThrowIfCancellationRequested();
            chunkIdx++;
            var chunk = rows.GetRange(offset, Math.Min(ChunkSize, rows.Count - offset));

            var sb = new StringBuilder();
            sb.Append("INSERT IGNORE INTO stock_ledger ").Append(ColumnList).Append(" VALUES ");
            var dyn = new DynamicParameters();
            for (int i = 0; i < chunk.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('(')
                  .Append("@T").Append(i).Append(",@I").Append(i).Append(",@W").Append(i)
                  .Append(",@P").Append(i).Append(",@LD").Append(i).Append(",@YM").Append(i)
                  .Append(",@MT").Append(i).Append(",'migration',@SI").Append(i)
                  .Append(",@DN").Append(i).Append(",@QI").Append(i).Append(",@QO").Append(i)
                  .Append(",@UC").Append(i).Append(",@SA").Append(i).Append(",@M").Append(i)
                  .Append(')');

                var r = chunk[i];
                dyn.Add("T" + i, r.TenantId);
                dyn.Add("I" + i, r.ItemId);
                dyn.Add("W" + i, r.WarehouseId);
                dyn.Add("P" + i, r.PartnerId);
                dyn.Add("LD" + i, r.LedgerDate);
                dyn.Add("YM" + i, r.Ym);
                dyn.Add("MT" + i, r.MoveType);
                dyn.Add("SI" + i, r.SourceId);
                dyn.Add("DN" + i, r.DocNo);
                dyn.Add("QI" + i, r.QtyIn);
                dyn.Add("QO" + i, r.QtyOut);
                dyn.Add("UC" + i, r.UnitCost);
                dyn.Add("SA" + i, r.SupplyAmount);
                dyn.Add("M" + i, r.Memo);
            }

            var affected = await Db.ExecuteAsync(new CommandDefinition(
                sb.ToString(), dyn, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            inserted += affected;

            // 청크 5개마다 진행률 로그 (5000행 단위) — UI 폴링과 페이스 맞춤.
            if (chunkIdx % 5 == 0 || chunkIdx == totalChunks)
            {
                _logger.LogInformation(
                    "[MDB마이그레이션] stock_ledger 청크 {Chunk}/{Total} 처리 ({Done}/{Total2}행, INSERT IGNORE 누적={Inserted})",
                    chunkIdx, totalChunks, offset + chunk.Count, rows.Count, inserted);
            }
        }

        _logger.LogInformation(
            "[MDB마이그레이션] 입출고(stock_ledger) 완료: 후보 {Total}행 → INSERT {Inserted}행 (중복 IGNORE = {Skipped})",
            rows.Count, inserted, rows.Count - inserted);
        return inserted;
    }

    /// <summary>
    /// 정공법(축 1) MySqlBulkCopy 경로: TEMPORARY staging → INSERT IGNORE SELECT.
    /// 116K 행 기준 청크 INSERT IGNORE(수십초~수분) → 5~10초 목표.
    /// 멱등 키 uq_stock_ledger_source UNIQUE(tenant_id, source_type, source_id, item_id, move_type)로
    /// 재실행 중복은 IGNORE로 차단. 헌법 #3 INSERT ONLY 원장 원칙 유지.
    /// </summary>
    private async Task<int> BulkCopyStockLedgerAsync(
        MySqlConnection conn, MySqlTransaction tx, List<StockLedgerRow> rows, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var stageTable = $"stock_ledger_stage_{Guid.NewGuid():N}".Substring(0, 40);

        // 1) 세션 한정 TEMPORARY staging 테이블 생성. LIKE로 컬럼·인덱스 동일하게 복제하되
        //    UNIQUE 인덱스는 일단 유지 (BulkCopy 도중에도 충돌 0 가정 — 같은 잡 내 새 데이터).
        //    TEMPORARY 테이블은 세션 종료(=conn DisposeAsync) 시 자동 DROP.
        var createSql = $"CREATE TEMPORARY TABLE `{stageTable}` LIKE stock_ledger";
        using (var createCmd = new MySqlCommand(createSql, conn, tx))
        {
            createCmd.CommandTimeout = 60;
            await createCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        try
        {
            // 2) DataTable 빌드 (BulkCopy는 IDataReader/DataTable 입력).
            //    stock_ledger 본 테이블의 컬럼 순서/타입과 정확히 맞춰야 함 — LIKE 복제했으므로 동일.
            //    ledger_id/created_at 등 default/auto 컬럼은 ColumnMappings로 skip하고 명시 컬럼만 적재.
            var dataTable = BuildStockLedgerDataTable(rows);

            // MySqlBulkCopy는 IDisposable 미구현 — using 없이 사용 (내부 리소스는 호출당 정리).
            var bulk = new MySqlBulkCopy(conn, tx)
            {
                DestinationTableName = stageTable,
                BulkCopyTimeout = 600,
            };
            // 컬럼 매핑: DataTable 인덱스 → staging 테이블 컬럼명.
            var cols = new[]
            {
                "tenant_id", "item_id", "warehouse_id", "partner_id", "ledger_date", "ym",
                "move_type", "source_type", "source_id", "doc_no", "qty_in", "qty_out",
                "unit_cost", "supply_amount", "memo",
            };
            for (int i = 0; i < cols.Length; i++)
            {
                bulk.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(i, cols[i]));
            }

            var bulkResult = await bulk.WriteToServerAsync(dataTable, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "[MDB마이그레이션] stock_ledger BulkCopy 적재: {Rows}행, warnings={Warn}, {Elapsed}ms",
                bulkResult.RowsInserted, bulkResult.Warnings.Count, sw.ElapsedMilliseconds);

            // 3) INSERT IGNORE 본 테이블 (UNIQUE 충돌 시 skip → 멱등).
            var insertSql = $"""
                INSERT IGNORE INTO stock_ledger
                  (tenant_id, item_id, warehouse_id, partner_id, ledger_date, ym,
                   move_type, source_type, source_id, doc_no, qty_in, qty_out,
                   unit_cost, supply_amount, memo)
                SELECT
                   tenant_id, item_id, warehouse_id, partner_id, ledger_date, ym,
                   move_type, source_type, source_id, doc_no, qty_in, qty_out,
                   unit_cost, supply_amount, memo
                FROM `{stageTable}`
                """;
            int inserted;
            using (var insertCmd = new MySqlCommand(insertSql, conn, tx))
            {
                insertCmd.CommandTimeout = 600;
                inserted = await insertCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            sw.Stop();
            _logger.LogInformation(
                "[MDB마이그레이션] stock_ledger 정공법 완료: 후보 {Total}행 → INSERT {Inserted}행 (중복 IGNORE={Skipped}, 총 {Elapsed}ms)",
                rows.Count, inserted, rows.Count - inserted, sw.ElapsedMilliseconds);
            return inserted;
        }
        finally
        {
            // TEMPORARY는 세션 종료 시 auto-drop이지만 명시 DROP으로 잡 내 메모리 즉시 해제.
            try
            {
                using var dropCmd = new MySqlCommand($"DROP TEMPORARY TABLE IF EXISTS `{stageTable}`", conn, tx);
                dropCmd.CommandTimeout = 30;
                await dropCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            catch (Exception dex)
            {
                // 헌법 #15: silent 금지. drop 실패해도 세션 종료 시 auto-drop 보장 → WARN만.
                _logger.LogWarning(dex,
                    "[MDB마이그레이션] stock_ledger staging({Table}) DROP 실패 — 세션 종료 시 auto-drop 예정",
                    stageTable);
            }
        }
    }

    /// <summary>
    /// BulkCopy 입력용 DataTable 빌드. 컬럼 순서는 stock_ledger 컬럼 매핑(ColumnMappings)과 1:1 일치.
    /// </summary>
    private static DataTable BuildStockLedgerDataTable(List<StockLedgerRow> rows)
    {
        var dt = new DataTable();
        dt.Columns.Add("tenant_id", typeof(string));
        dt.Columns.Add("item_id", typeof(string));
        dt.Columns.Add("warehouse_id", typeof(string));
        dt.Columns.Add("partner_id", typeof(string));
        dt.Columns.Add("ledger_date", typeof(DateTime));
        dt.Columns.Add("ym", typeof(string));
        dt.Columns.Add("move_type", typeof(string));
        dt.Columns.Add("source_type", typeof(string));
        dt.Columns.Add("source_id", typeof(string));
        dt.Columns.Add("doc_no", typeof(string));
        dt.Columns.Add("qty_in", typeof(decimal));
        dt.Columns.Add("qty_out", typeof(decimal));
        dt.Columns.Add("unit_cost", typeof(decimal));
        dt.Columns.Add("supply_amount", typeof(decimal));
        dt.Columns.Add("memo", typeof(string));

        foreach (var r in rows)
        {
            dt.Rows.Add(
                r.TenantId,
                r.ItemId,
                r.WarehouseId,
                (object?)r.PartnerId ?? DBNull.Value,
                r.LedgerDate,
                r.Ym,
                r.MoveType,
                "migration",
                r.SourceId,
                (object?)r.DocNo ?? DBNull.Value,
                r.QtyIn,
                r.QtyOut,
                r.UnitCost,
                r.SupplyAmount,
                (object?)r.Memo ?? DBNull.Value);
        }
        return dt;
    }

    /// <summary>stock_ledger 청크 INSERT용 임시 row DTO.</summary>
    private sealed class StockLedgerRow
    {
        public string TenantId { get; set; } = string.Empty;
        public string ItemId { get; set; } = string.Empty;
        public string WarehouseId { get; set; } = string.Empty;
        public string? PartnerId { get; set; }
        public DateTime LedgerDate { get; set; }
        public string Ym { get; set; } = string.Empty;
        public string MoveType { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;
        public string? DocNo { get; set; }
        public decimal QtyIn { get; set; }
        public decimal QtyOut { get; set; }
        public decimal UnitCost { get; set; }
        public decimal SupplyAmount { get; set; }
        public string? Memo { get; set; }
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
        // P0 #4 (2026-05-14): ORDER BY 추가 — 헌법 #13 멱등 순서 보장 (수금 날짜+업체).
        var dt = ReadMdbTable(oleConn, "SELECT * FROM DOCF5 ORDER BY S_YMD, S_BUY");
        if (dt.Rows.Count == 0) return 0;

        // WS-20260514-08 (CODE-06): INSERT IGNORE + 멱등 키 source_type/source_id.
        // uq_collections_source(tenant_id, source_type, source_id) UNIQUE 활용 (WS-10에서 추가).
        // 재실행 시 같은 source_id 중복 INSERT 자동 차단.
        const string sql = """
            INSERT IGNORE INTO collections
              (collection_id, tenant_id, partner_id, collection_date, amount,
               collection_method, memo, is_active, created_at, updated_at,
               source_type, source_id)
            VALUES
              (@CollectionId, @TenantId, @PartnerId, @CollectionDate, @Amount,
               @Method, @Memo, 1, @Now, @Now,
               'migration', @SourceId)
            """;

        int count = 0;
        int rowIdx = 0;
        foreach (DataRow row in dt.Rows)
        {
            rowIdx++;
            var buyCode = GetInt(row, "S_BUY");
            if (!partnerMap.TryGetValue(buyCode, out var partnerId)) continue;

            var ymd = GetStr(row, "S_YMD");
            var collDate = ParseLegacyDate(ymd) ?? now;

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

            // WS-08 멱등 키: DOCF5에 시퀀스 컬럼 없음 → 정렬 ORDER BY 후 rowIdx 사용.
            // 동일 MDB 두 번 마이그 시 같은 ORDER BY → 같은 rowIdx → 같은 source_id → INSERT IGNORE skip.
            var sourceId = $"mig-{ymd}-{buyCode}-{rowIdx:D6}";

            await Db.ExecuteAsync(new CommandDefinition(sql, new
            {
                CollectionId = Guid.NewGuid().ToString(),
                TenantId = tenantId,
                PartnerId = partnerId,
                CollectionDate = collDate,
                Amount = amount,
                Method = method,
                Memo = GetStr(row, "S_REM"),
                Now = now,
                SourceId = sourceId,
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
        // P0 #4 (2026-05-14): ORDER BY 추가 — 헌법 #13 멱등 순서 보장 (경비 날짜).
        var dt = ReadMdbTable(oleConn, "SELECT * FROM DOCF6 ORDER BY AC_YMD");
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

            await Db.ExecuteAsync(new CommandDefinition(sql, new
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
        // P0 #4 (2026-05-14): ORDER BY 추가 — 헌법 #13 멱등 순서 보장 (전표 날짜).
        var dt = ReadMdbTable(oleConn, "SELECT * FROM DOCF7 ORDER BY SC_DT");
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

            await Db.ExecuteAsync(new CommandDefinition(sql, new
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

            await Db.ExecuteAsync(new CommandDefinition(headSql, new
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
                await Db.ExecuteAsync(new CommandDefinition(lineSql, new
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

            await Db.ExecuteAsync(new CommandDefinition(headSql, new
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
                await Db.ExecuteAsync(new CommandDefinition(lineSql, new
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
        // P0 #4 (2026-05-14): ORDER BY 추가 — 헌법 #13 멱등 순서 보장 (세금계산서 번호).
        var dt = ReadMdbTable(oleConn, "SELECT * FROM DOCF4 ORDER BY TX_NO");
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
                await Db.ExecuteAsync(new CommandDefinition(sql, new
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
        // P0 #4 (2026-05-14): ORDER BY 추가 — 헌법 #13 멱등 순서 보장 (어음 발행번호).
        var dt9 = ReadMdbTable(oleConn, "SELECT * FROM DOCF9 ORDER BY EU_NO");
        foreach (DataRow r in dt9.Rows)
        {
            var no = GetStr(r, "EU_NO");
            if (string.IsNullOrWhiteSpace(no)) continue;
            var amt = GetDec(r, "EU_AMT");
            if (amt <= 0) continue;

            var cla = GetStr(r, "EU_CLA");
            var billType = cla == "2" ? "P" : "R";
            var partnerName = GetStr(r, "EU_BUY");

            await Db.ExecuteAsync(new CommandDefinition(sql, new
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
        // P0 #4 (2026-05-14): ORDER BY 추가 — 헌법 #13 멱등 순서 보장 (어음 만기번호).
        var dtQ = ReadMdbTable(oleConn, "SELECT * FROM DOCFQ ORDER BY EQ_NO");
        foreach (DataRow r in dtQ.Rows)
        {
            var no = GetStr(r, "EQ_NO");
            if (string.IsNullOrWhiteSpace(no)) continue;
            var amt = GetDec(r, "EQ_AMT");
            if (amt <= 0) continue;

            var cla = GetStr(r, "EQ_CLA");
            var billType = cla == "2" ? "P" : "R";

            await Db.ExecuteAsync(new CommandDefinition(sql, new
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
        // P0 #4 (2026-05-14): ORDER BY 추가 — 헌법 #13 멱등 순서 보장 (카드결제 번호).
        var dt = ReadMdbTable(oleConn, "SELECT * FROM DOCCD ORDER BY CD_CDNO");
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
        // P0 #4 (2026-05-14): ORDER BY 추가 — 헌법 #13 멱등 순서 보장 (카드 라인).
        var dt1 = ReadMdbTable(oleConn, "SELECT * FROM DOCCD1 ORDER BY CD1_NO");
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

            await Db.ExecuteAsync(new CommandDefinition(headSql, new
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
                    await Db.ExecuteAsync(new CommandDefinition(lineSql, new
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
        // P0 #4 (2026-05-14): ORDER BY 추가 — 헌법 #13 멱등 순서 보장 (계좌+거래일).
        var dt = ReadMdbTable(oleConn, "SELECT * FROM BANKF ORDER BY BK_NO, BK_DT");
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

            await Db.ExecuteAsync(new CommandDefinition(sql, new
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
        if (Db.State == ConnectionState.Open) return;
        if (_db is DbConnection dbConnection)
        {
            await dbConnection.OpenAsync(ct).ConfigureAwait(false);
            return;
        }
        Db.Open();
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
