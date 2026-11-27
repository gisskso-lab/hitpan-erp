using Dapper;
using HitPan.API.Controllers;
using HitPan.Application.DTOs.DataReset;
using HitPan.Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

// MigrationController 는 [SupportedOSPlatform("windows")] 다 — 짝 파일과 같은 해제(#19).
#pragma warning disable CA1416

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>D-B1 — 운영 판에서 초기화 닫힘 · DB 쪽</b>(20261127작 W-S-01 갈래 B · 설계 §4 · CI db-gate 전용 · 이 PC 3306 = 실물이라 로컬 금지).
/// </summary>
/// <remarks>
/// <para>
/// 시드 회사(대표 1 · 업무 줄 있음)에서 <b>대표 본인 + 맞는 비번</b>으로 운영 구현(<see cref="DataResetBetaAvailability"/>)의
/// <c>ResetAllAsync</c> 를 불러도 거절 — 회사 범위 모든 표 행 수 그대로 · 거절 감사 +1 · 강제 백업 0.
/// 자료이관 덮어쓰기(:705) 길도 같은 서비스라 함께 막힘을 같은 자리에서 잰다.
/// </para>
/// <para>무력화: <c>ResetAllAsync</c> 첫 문장(00)과 판정 함수 2곳의 닫힘 줄을 빼면 대표가 통과해 업무 줄이 지워져 빨강.</para>
/// </remarks>
public sealed partial class DataResetParentOnlyGateDbTests
{
    /// <summary>운영 구현(닫힘) + 진짜 AuditService — 거절 감사 행을 DB 로 센다.</summary>
    private static DataResetService NewClosedServiceWithRealAudit(MySqlConnection db, CountingBackup backup, string userId) =>
        new(db, backup,
            new AuditService(db, new FixedTenant(TenantId, userId), NullLogger<AuditService>.Instance),
            NullLogger<DataResetService>.Instance, new DataResetBetaAvailability());

    /// <summary>회사 범위(tenant_id 칸이 있는 모든 표) 행 수 — 감사 표(audit_trail)는 거절 감사가 더해지므로 따로 센다.</summary>
    private static async Task<Dictionary<string, long>> TenantRowCountsAsync(MySqlConnection db, string tenantId)
    {
        var tables = (await db.QueryAsync<string>(
            """
            SELECT c.table_name
            FROM information_schema.columns c
            JOIN information_schema.tables t ON t.table_schema = c.table_schema AND t.table_name = c.table_name
            WHERE c.table_schema = DATABASE() AND c.column_name = 'tenant_id' AND t.table_type = 'BASE TABLE'
              AND c.table_name <> 'audit_trail'
            """)).ToList();
        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tables)
        {
            // 표 이름은 information_schema 에서 온 실제 이름(백틱 감쌈) — 시험 전용 임시 DB.
            counts[t] = await db.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM `{t}` WHERE tenant_id = @T", new { T = tenantId });
        }
        return counts;
    }

    [Fact(DisplayName = "D-B1 🔴 운영 구현(닫힘) — 대표 본인 · 맞는 비번 → 거절(closed · 닫힘 안내) · 회사 범위 모든 표 행 수 그대로 · 거절 감사 +1 · 강제 백업 0 · 덮어쓰기 이관 길도 400 · 판정 직접 = Closed")]
    public async Task DB1_운영판_닫힘_대표도_거절()
    {
        if (!ServerAvailable()) { DbGateEnvironment.SkipOrFail(nameof(DB1_운영판_닫힘_대표도_거절)); return; }
        SetUpFreshInstall();
        await using var db = await OpenAsync();
        var owner = await InsertUserAsync(db, TenantId, "owner", parent: true, admin: true, OwnerPw);
        await InsertBusinessRowAsync(db, TenantId, "db1");

        // 대조 — 열린 가짜면 같은 대표가 판정을 통과한다(닫힘이 깃발에서만 온다는 증거).
        Assert.Equal(DataResetGate.Allowed, await NewService(db, new CountingBackup()).GetResetGateAsync(TenantId, owner));

        var before = await TenantRowCountsAsync(db, TenantId);
        Assert.True(before.TryGetValue("partner_legacy_balances", out var biz) && biz == 1, "준비 실패 — 업무 줄이 안 들어갔다.");
        var deniedBefore = (await AuditReasonsAsync(db, TenantId, "data_reset_denied")).Count;

        var backup = new CountingBackup();
        var res = await NewClosedServiceWithRealAudit(db, backup, owner).ResetAllAsync(
            new DataResetRequest { Password = OwnerPw, ConfirmText = "초기화" }, TenantId, owner);

        Assert.False(res.Success, "🔴 닫힌 판에서 대표 본인이 초기화를 했다 — 베타 판 서버 닫음(K25 · §258)이 안 됐다.");
        Assert.Equal(DataResetGateCodes.Closed, res.DenyCode);
        Assert.Equal(DataResetService.ClosedMessage, res.Error);
        Assert.True(backup.Count == 0, $"🔴 닫힌 판에서 강제 백업이 {backup.Count}번 돌았다(닫힘은 백업 앞 첫 문장이어야 한다).");

        var after = await TenantRowCountsAsync(db, TenantId);
        foreach (var (table, n) in before)
        {
            Assert.True(after.TryGetValue(table, out var m) && m == n, $"🔴 닫힌 판에서 `{table}` 행 수가 {n} → {m} 로 바뀌었다.");
        }

        var denied = await AuditReasonsAsync(db, TenantId, "data_reset_denied");
        Assert.Equal(deniedBefore + 1, denied.Count);
        Assert.Contains(DataResetService.ClosedDenyReasonPrefix + DataResetGateCodes.Closed, denied);
        Assert.Empty(await AuditReasonsAsync(db, TenantId, "data_reset"));
        Assert.Empty(await AuditReasonsAsync(db, TenantId, "data_reset_failed"));

        // 판정 직접(둘째 겹) — 닫힘이면 DB 를 읽기 전에 Closed.
        Assert.Equal(DataResetGate.Closed, await NewClosedServiceWithRealAudit(db, new CountingBackup(), owner).GetResetGateAsync(TenantId, owner));

        // 자료이관 덮어쓰기 길(MigrationController :705) — 같은 서비스라 400 · 닫힘 안내 · 백업 0 · 줄 그대로(P7 꼴).
        var migBackup = new CountingBackup();
        var c = new MigrationController(
            migrationService: null!, logger: NullLogger<MigrationController>.Instance,
            jobStore: null!, scopeFactory: null!, progress: null!, reconciliation: null!,
            dataReset: NewClosedServiceWithRealAudit(db, migBackup, owner), provisioner: null!, db: null!);
        var http = new DefaultHttpContext();
        http.Items["TenantId"] = TenantId;
        http.Items["UserId"] = owner;
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;   // 메인 PC(자료 보관 컴퓨터)
        c.ControllerContext = new ControllerContext { HttpContext = http };

        var result = await c.StartMigrationJob(new MdbMigrationRequest
        {
            FolderPath = @"C:\HITWIN",
            Mode = "overwrite",
            ConfirmText = "덮어쓰기",
            Password = OwnerPw,
        }, default);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(DataResetService.ClosedMessage, bad.Value?.GetType().GetProperty("message")?.GetValue(bad.Value) as string);
        Assert.True(migBackup.Count == 0, $"🔴 덮어쓰기 길에서 닫힌 판인데 강제 백업이 {migBackup.Count}번 돌았다.");
        await using var check = await OpenAsync();
        Assert.Equal(1L, await BusinessRowsAsync(check, TenantId));
    }
}
