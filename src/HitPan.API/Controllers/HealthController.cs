using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace HitPan.API.Controllers;

/// <summary>
/// Health Check 엔드포인트 — Grafana/Uptime Robot/AWS ELB 헬스 프로빙용.
/// 익명 접근 (공개). DB 연결 상태 포함.
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
        checks["version"] = "1.0.0-beta";
        checks["environment"] = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown";
        checks["uptime_sec"] = (int)(DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds;

        var result = new { status = overall, timestamp = DateTime.UtcNow, checks };
        return overall == "healthy" ? Ok(result) : StatusCode(503, result);
    }
}
