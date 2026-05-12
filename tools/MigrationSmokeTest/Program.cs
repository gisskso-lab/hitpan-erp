// W2 D5 (2026-05-12) — 마이그 실측 스모크 테스트
// PYOJUN.MDB 3건 → MariaDB(임시 tenant) 마이그 → 검증 → tenant 삭제 (사장님 결재 B안)
// 실행 후 즉시 폐기 예정 (tools/ 일회용 스크립트)

using System.Data;
using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using HitPan.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using MySqlConnector;

const string testTenantId = "test-mig-20260512";
const string mdbFolder = @"C:\HITWINLAN10";
const string connString = "Server=localhost;Database=hitpan_erp;Uid=hitpan;Pwd=Hitpan2025!;SslMode=None;AllowUserVariables=true;";

Console.WriteLine("=== W2 D5 마이그 실측 스모크 테스트 ===");
Console.WriteLine($"테스트 tenant: {testTenantId}");
Console.WriteLine($"MDB 폴더: {mdbFolder}");
Console.WriteLine();

// DI 구성 (간소)
using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
var logger = loggerFactory.CreateLogger<MdbMigrationService>();

using IDbConnection db = new MySqlConnection(connString);
db.Open();

// 헌법 #5: ERP_ENCRYPTION_KEY 환경변수 = 운영 키 그대로 사용
var encryption = new EncryptionService();
IBinaryCryptoService crypto = new BinaryCryptoServiceAdapter(encryption);

var service = new MdbMigrationService(db, logger, crypto);

try
{
    Console.WriteLine("[1/3] PreviewAsync 호출 — MDB 테이블 건수 확인");
    var preview = await service.PreviewAsync(mdbFolder, testTenantId);
    foreach (var (table, count) in preview)
    {
        Console.WriteLine($"  {table,-20} : {count}건");
    }
    Console.WriteLine();

    Console.WriteLine("[2/3] MigrateAsync 호출 — 실제 마이그 (트랜잭션)");
    var result = await service.MigrateAsync(mdbFolder, testTenantId);
    Console.WriteLine($"  partners       : {result.Partners}건");
    Console.WriteLine($"  items          : {result.Items}건");
    Console.WriteLine($"  employees      : {result.Employees}건");
    Console.WriteLine($"  bomHeaders     : {result.BomHeaders}건");
    Console.WriteLine($"  salesOrders    : {result.SalesOrders}건");
    Console.WriteLine($"  purchaseOrders : {result.PurchaseOrders}건");
    Console.WriteLine($"  stockLedger    : {result.StockLedger}건");
    Console.WriteLine($"  taxInvoices    : {result.TaxInvoices}건");
    Console.WriteLine($"  TOTAL          : {result.Total}건");
    Console.WriteLine();

    Console.WriteLine("[3/3] 결과 검증 — partners 데이터 + AES 복호화");
    Console.WriteLine("(검증은 별도 mysql + PowerShell로 진행)");

    Console.WriteLine();
    Console.WriteLine("=== 마이그 성공 ===");
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine("=== 마이그 실패 ===");
    Console.WriteLine($"Exception: {ex.GetType().Name}");
    Console.WriteLine($"Message: {ex.Message}");
    if (ex.InnerException != null)
    {
        Console.WriteLine($"Inner: {ex.InnerException.Message}");
    }
    Console.WriteLine();
    Console.WriteLine(ex.StackTrace);
    return 1;
}
