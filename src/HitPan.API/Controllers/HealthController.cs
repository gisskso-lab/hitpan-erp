using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace HitPan.API.Controllers;

/// <summary>
/// Health Check 엔드포인트 — DB 연결 상태·버전 보고.
///
/// ■ 접근 범위 (주석 정정 2026-07-16, 작1 W4-0)
///   종전 주석은 "익명 접근(공개). Grafana/Uptime Robot/AWS ELB 헬스 프로빙용" 이라 적혀 있었으나
///   실제 동작과 정반대다 — HealthIpWhitelistMiddleware(Program.cs, 인증 미들웨어보다 앞)가
///   루프백(127.0.0.1/::1)만 허용하므로 외부 Grafana/ELB 는 403 을 받는다.
///   [AllowAnonymous] 는 인증만 면제할 뿐 IP 화이트리스트를 통과시키지 않는다.
///   이 주석에 속아 "공개 엔드포인트"로 오판한 사례가 실제로 있었다(헌법 #15 — 흔적이 거짓말하면 안 된다).
///
/// ■ 주 소비자 = 워치독 (로컬 호출이라 화이트리스트 통과)
///   워치독의 업데이트 검증(작1 W4-5)이 아래 checks["version"] 을 읽어 "새 버전으로 교체됐나"를 판정한다.
///   ⚠️ 단 이 판정 경로는 단일 실패점이다 — HealthCheck:AllowedIps 설정 한 줄로 열리고,
///      UseForwardedHeaders 배선이 없어 프록시 경유 시 RemoteIpAddress 가 프록시 IP 가 되며,
///      터널 ingress 를 5257(API) 직결로 붙이는 배포 형태가 실재한다.
///      그래서 워치독은 이 응답만 믿지 않고 교체된 EXE 의 FileVersion 을 함께 확인한다(2중 판정).
/// </summary>
[ApiController]
[Route("health")]
[AllowAnonymous]
public sealed class HealthController : ControllerBase
{
    private readonly IDbConnection _db;

    public HealthController(IDbConnection db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var checks = new Dictionary<string, object>();
        var overall = "healthy";

        // DB 체크 — 간단한 SELECT 1
        try
        {
            if (_db.State != ConnectionState.Open)
            {
                if (_db is System.Data.Common.DbConnection c) await c.OpenAsync(ct);
                else _db.Open();
            }
            await Dapper.SqlMapper.ExecuteScalarAsync<int>(_db, "SELECT 1");
            checks["database"] = "ok";
        }
        catch (Exception ex)
        {
            checks["database"] = $"fail: {ex.Message}";
            overall = "unhealthy";
        }

        // 앱 메타
        // 봉합 (2026-07-16, 작1 W4-0 — 사장님 결재, CTO 적발 B-1): 종전 "1.0.0-beta" 리터럴.
        //   워치독의 업데이트 검증(W4-5)이 바로 이 값을 읽어 "신 버전으로 교체됐나"를 판정한다.
        //   리터럴이면 교체·재시작이 전부 성공해도 "신 버전 아님"으로 읽혀 정상 업데이트를 롤백하고
        //   무한 재시도한다 — 이 한 줄 때문에 자동 업데이트가 단 한 번도 성공할 수 없었다.
        checks["version"] = VersionInfo.Current;
        checks["environment"] = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown";
        checks["uptime_sec"] = (int)(DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds;

        var result = new { status = overall, timestamp = DateTime.UtcNow, checks };
        return overall == "healthy" ? Ok(result) : StatusCode(503, result);
    }
}
