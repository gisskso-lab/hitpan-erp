// ============================================================================
// 고리4 P1 멱등 검증 도구 (작5, 사장님 결재 2026-06-30) — Windows Sandbox M4 전용
// ----------------------------------------------------------------------------
// 방금 배선한 MigrationRunner(IMigrationRunner, Program.cs:75)를 빈 DB에서 직접
// 호출해 다음을 실측한다:
//   [1회차] 미적용 DB-*.sql 을 번호순·순차 적용 → 신규 적용 N건
//   [2회차] 동일 호출 → 신규 적용 0건 + 전부 멱등 skip (★ 멱등 핵심)
//
// 운영(demo 3306 / hitpan_erp 129만행) 무접촉:
//   - DB_NAME 이 운영 이름(hitpan_erp 등)이면 즉시 ABORT (slot빌더 R3 정신, 헌법 #39).
//   - 샌드박스 안 빈 DB(예: hitpan_m4)만 가리키게 환경변수로 강제.
//
// 헌법: #1 MigrationRunner 무수정(직접 인스턴스화만) / #15 실패는 로그+코드 반환 /
//       #32 부풀림 금지(멱등 깨지면 정직히 FAIL) / #39 검증=테스트환경.
// ============================================================================

using HitPan.Application.Interfaces;
using HitPan.Application.Services;
using HitPan.Infrastructure.Configuration;
using HitPan.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

// ── [0] 운영 보호 가드 (가장 먼저 — 잘못된 DB면 한 줄도 안 돌고 멈춤) ────────────
// 운영으로 의심되는 DB 이름 화이트리스트(blacklist). 샌드박스 빈 DB만 통과.
var dbName = TenantConfigReader.Get("DB_NAME") ?? "";
var bannedNames = new[] { "hitpan_erp", "hitpan_backoffice", "demo" };
if (bannedNames.Any(b => string.Equals(b, dbName, StringComparison.OrdinalIgnoreCase)))
{
    Console.Error.WriteLine(
        $"[ABORT] DB_NAME='{dbName}' 은 운영/금지 DB 입니다. 멱등 검증은 샌드박스 빈 DB 에서만 " +
        "수행합니다(헌법 #39). DB_NAME 을 테스트 DB(예: hitpan_m4)로 설정하고 다시 실행하십시오.");
    return 2;
}
if (string.IsNullOrWhiteSpace(dbName))
{
    Console.Error.WriteLine(
        "[ABORT] DB_NAME 환경변수가 비어 있습니다. 샌드박스 셋업이 테스트 DB 이름을 주입했는지 확인하십시오.");
    return 2;
}

Console.WriteLine("=== 고리4 P1 멱등 검증 (MigrationIdempotencyCheck) ===");
Console.WriteLine($"대상 DB : {dbName}  (host={TenantConfigReader.Get("DB_HOST") ?? "localhost"}, port={TenantConfigReader.Get("DB_PORT") ?? "3306"})");
Console.WriteLine("MigrationRunner 를 2회 호출 → 1회차 적용 N건 / 2회차 신규 0건(멱등) 을 검증합니다.");
Console.WriteLine();

using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));

// MigrationRunner 는 IMigrationDbConnectionFactory + ILogger 만 의존 → DI 없이 직접 구성(헌법 #1 무수정).
var factory = new MigrationDbConnectionFactory(
    loggerFactory.CreateLogger<MigrationDbConnectionFactory>());
var runner = new MigrationRunner(factory, loggerFactory.CreateLogger<MigrationRunner>());

try
{
    // ── [1회차] ─────────────────────────────────────────────────────────────
    Console.WriteLine("[1회차] ApplyPendingAsync 호출 ...");
    var r1 = await runner.ApplyPendingAsync(appVersion: "m4-idempotency-1");
    Console.WriteLine($"  성공={r1.Success}  신규적용={r1.AppliedMigrationIds.Count}건  멱등skip={r1.SkippedCount}건");
    if (!r1.Success)
    {
        Console.Error.WriteLine($"[FAIL] 1회차 실패: {r1.FailedMigrationId} — {r1.FailureMessage}");
        return 1;
    }
    Console.WriteLine();

    // ── [2회차] ★ 멱등 핵심 — 신규 적용이 0건이어야 한다 ──────────────────────
    Console.WriteLine("[2회차] ApplyPendingAsync 재호출 (멱등 확인) ...");
    var r2 = await runner.ApplyPendingAsync(appVersion: "m4-idempotency-2");
    Console.WriteLine($"  성공={r2.Success}  신규적용={r2.AppliedMigrationIds.Count}건  멱등skip={r2.SkippedCount}건");
    if (!r2.Success)
    {
        Console.Error.WriteLine($"[FAIL] 2회차 실패: {r2.FailedMigrationId} — {r2.FailureMessage}");
        return 1;
    }
    Console.WriteLine();

    // ── [판정] ─────────────────────────────────────────────────────────────
    if (r2.AppliedMigrationIds.Count != 0)
    {
        Console.Error.WriteLine(
            $"[FAIL] 멱등 깨짐 — 2회차에서 {r2.AppliedMigrationIds.Count}건이 또 적용됐습니다: " +
            $"{string.Join(", ", r2.AppliedMigrationIds)}. schema_migrations 추적이 동작하지 않습니다.");
        return 1;
    }

    Console.WriteLine("=== PASS — 멱등 검증 통과 ===");
    Console.WriteLine($"  1회차 신규 {r1.AppliedMigrationIds.Count}건 적용 → 2회차 신규 0건(전부 skip {r2.SkippedCount}건).");
    Console.WriteLine("  MigrationRunner(고리4 P1)는 빈 DB에서 멱등으로 동작합니다.");
    return 0;
}
catch (Exception ex)
{
    // 헌법 #15: silent swallow 금지 — 무엇이 어디서 깨졌는지 명확히.
    Console.Error.WriteLine();
    Console.Error.WriteLine("=== 예외 — 멱등 검증 중단 ===");
    Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
    if (ex.InnerException is not null)
        Console.Error.WriteLine($"Inner: {ex.InnerException.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    return 1;
}
