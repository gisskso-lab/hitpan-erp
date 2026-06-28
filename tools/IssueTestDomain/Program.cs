// 통신무결성 test 서브도메인(e2e-comms) 발급 최소실행기 (작3 블록C, 2026-06-28)
//
// 목적: 정식 자동화 코드 CloudflareDomainService 를 그대로 호출해
//       e2e-comms.hitpan.kr DNS + 터널을 발급한다. "고객 가입 시 자동발급"과 동일 코드 경로 검증.
// 안전: 백오피스 DB·로그인은 안 거친다(#39 demo 무손상). 환경변수만 읽는다.
//
// 사용:
//   dotnet run -- --dry-run     # 준비점검만(IsConfigured 확인, 발급 안 함) — 읽기, #29 무관
//   dotnet run -- --issue       # 실제 발급(#29 쓰기 — 사장님 GO 후에만)
//   (별칭 기본값 e2e-comms, --alias <name> 으로 변경)

using HitPan.Backoffice.API.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var dryRun = !args.Contains("--issue");        // 기본은 안전(dry-run). --issue 명시해야 실발급
var aliasIdx = Array.IndexOf(args, "--alias");
var alias = (aliasIdx >= 0 && aliasIdx + 1 < args.Length) ? args[aliasIdx + 1] : "e2e-comms";

// CloudflareDomainService 가 요구하는 최소 DI: IConfiguration·IHttpClientFactory·ILogger
var services = new ServiceCollection();
services.AddHttpClient();
services.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information));
var config = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?> { ["Cloudflare:BaseDomain"] = "hitpan.kr" })
    .Build();
services.AddSingleton<IConfiguration>(config);
services.AddSingleton<ICloudflareDomainService, CloudflareDomainService>();
var sp = services.BuildServiceProvider();

var cf = sp.GetRequiredService<ICloudflareDomainService>();
var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("IssueTestDomain");

Console.WriteLine("====================================================");
Console.WriteLine($"  통신무결성 test 도메인 발급기  (alias={alias}.hitpan.kr)");
Console.WriteLine($"  모드: {(dryRun ? "DRY-RUN (준비점검만, 발급 안 함)" : "ISSUE (실제 발급 — #29)")}");
Console.WriteLine("====================================================");

// ── 준비점검: 환경변수(토큰·Zone·Account) 인식 여부 ──
Console.WriteLine($"[점검] IsConfigured = {cf.IsConfigured}");
if (!cf.IsConfigured)
{
    Console.WriteLine("[X] Cloudflare 환경변수 미인식. CLOUDFLARE_API_TOKEN·ZONE_ID·ACCOUNT_ID 확인 후 재시도.");
    return 1;
}
Console.WriteLine("[OK] 환경변수 3개 인식됨. 발급 코드 호출 준비 완료.");

if (dryRun)
{
    Console.WriteLine("\n[DRY-RUN] 여기까지가 준비점검(읽기). 실제 발급은 --issue 로 (사장님 GO 후).");
    return 0;
}

// ── 실제 발급 (#29 쓰기 — 사장님 GO 후에만 도달) ──
// test 발급은 실고객 tenant 가 아니므로 tenantId/tenantCode 자리에 식별용 더미를 넣는다(DB INSERT 없음).
var ct = CancellationToken.None;
try
{
    Console.WriteLine($"\n[1/3] DNS 발급: {alias}.hitpan.kr …");
    var dns = await cf.IssueAsync("e2e-comms-test", "e2ecomms", alias, ct);
    Console.WriteLine($"   → Domain={dns.Domain}  RecordId={dns.RecordId}");

    Console.WriteLine($"[2/3] 터널 발급: hitpan-e2ecomms …");
    var tunnel = await cf.IssueTunnelAsync("e2e-comms-test", "e2ecomms", ct);
    Console.WriteLine($"   → TunnelId={tunnel.TunnelId}  CnameTarget={tunnel.CnameTarget}");
    Console.WriteLine($"   → TunnelToken(앞8자)={tunnel.TunnelToken[..Math.Min(8, tunnel.TunnelToken.Length)]}… (전체는 마스킹)");

    Console.WriteLine($"[3/3] DNS→터널 매핑 + ingress …");
    await cf.UpdateDnsTunnelTargetAsync(dns.RecordId, dns.Domain, tunnel.TunnelId, ct);
    await cf.UpdateTunnelIngressAsync(tunnel.TunnelId, dns.Domain, "http://localhost:15234", ct);
    Console.WriteLine($"   → ingress: {dns.Domain} → http://localhost:15234");

    Console.WriteLine($"\n[완료] {alias}.hitpan.kr 자동발급 성공. 터널토큰은 test 슬롯 cloudflared 에 주입 필요.");
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine($"\n[발급 실패] {ex.GetType().Name}: {ex.Message}");
    log.LogError(ex, "도메인 발급 중 예외");
    return 2;
}
