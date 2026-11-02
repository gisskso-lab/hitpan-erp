using Dapper;
using HitPan.Backoffice.API.Controllers;
using MySqlConnector;

namespace HitPan.Backoffice.API.Services;

/// <summary>
/// 🔴 20261102작 — 고객사 화면 「ERP 설치」 칸의 판정 한 벌 (설계 <c>docs/설계/백오피스/20261102_설치유무표시_설계문서.md</c> §2 · §3).
///
/// <para><b>왜 둘째 문장인가</b>: 옛 칸(<c>is_locked_from_landing</c> · <c>bootstrap_at</c>)은 쓰는 코드가 0곳이라
/// 설치에 성공해도 전 고객이 「미설치」로 보였다. 이미 쌓이는 재료를 읽어 판정하되, 재료 표 · 칸이 없는 DB
/// (로컬 · 시험 · 복원)에서 기존 목록까지 500 이 되면 안 된다 ⇒ 기존 SELECT 는 그대로 두고 따로 읽는다.</para>
///
/// <para>🔴 <b>재료를 한 문장으로 다 읽는다.</b> 표마다 나누어 보호하면 한 표만 없을 때
/// 그 표 몫의 고객이 「미설치」로 보인다(반쪽 재료 판정 = 거짓 표시). 성공하면 전부 판정 · 실패하면 전부 판정 없음.</para>
///
/// <para>🔴 실패를 「미설치」로 떨어뜨리지 않는다. <c>InstallState = null</c> 로 두면 화면이 「확인 불가」로 읽는다.</para>
///
/// <para>🔴 <b><c>tenant_update_state</c> 는 「완료」 재료가 아니다</b>(지시서 §8-1). 업데이트 보고는 ERP 실행 증거가 아니고,
/// 실패 보고 · 로그인 불가 설치도 행을 만든다. 다만 설치 흔적이므로 「설치 시작」 재료로는 인정한다
/// — 보고가 있는 고객을 「미설치」로 보이면 틀린 표시다.</para>
/// </summary>
public static class TenantInstallStateReader
{
    public const string Installed = "installed";
    public const string Started = "started";
    public const string None = "none";

    // 5초 — 커진 표가 목록을 잡고 늘어지지 않게 한다. 넘으면 드라이버 취소 대기까지 약 7초 안에 끝난다(G-6ⓓ · G-8).
    private const int CommandTimeoutSeconds = 5;

    // EXISTS 만 쓴다 — 재료 표 둘 다 1고객 N행이고 tenant_update_state 는 tenant_id NULL 행도 있어 JOIN 하면 행이 불어난다.
    // 🔴 이 문자열 안에는 SQL 주석을 쓰지 않는다. 설명은 여기 C# 주석으로.
    public const string ReadSql = @"
        SELECT CAST(t.tenant_id AS CHAR) AS TenantId,
               t.owner_collected_at AS LastErpStartAt,
               CASE WHEN EXISTS (SELECT 1 FROM tenant_update_state u
                                  WHERE u.tenant_id = t.tenant_id) THEN 1 ELSE 0 END AS HasUpdateReport,
               CASE WHEN EXISTS (SELECT 1 FROM serial_verify_attempts a
                                  WHERE a.tenant_id = t.tenant_id
                                    AND a.result = 'installer-bootstrap') THEN 1 ELSE 0 END AS HasSerialPass
        FROM tenants t
        WHERE t.tenant_id IN @Ids";

    /// <summary>판정식 — 설계 §2 · 지시서 §8-1. 완료 = ERP 기동 기록 있음뿐 · 설치 시작 = 시리얼 통과 또는 업데이트 보고. 옛 칸은 판정에 쓰지 않는다.</summary>
    public static string Decide(DateTime? lastErpStartAt, long hasUpdateReport, long hasSerialPass)
    {
        if (lastErpStartAt is not null) return Installed;
        return hasSerialPass != 0 || hasUpdateReport != 0 ? Started : None;
    }

    /// <summary>
    /// 이미 읽은 행에 판정을 채운다. <paramref name="rows"/> 의 키는 첫 문장이 돌려준 값이다(클라이언트 값이 아니다).
    /// #16 — 같은 연결에서 순차 <c>await</c>. 실패해도 던지지 않는다(취소만 예외).
    /// </summary>
    public static async Task FillAsync(MySqlConnection db, IReadOnlyList<TenantsAdminController.TenantRow> rows,
                                       ILogger logger, CancellationToken ct)
    {
        if (rows.Count == 0) return;

        try
        {
            var facts = await db.QueryAsync<InstallFact>(new CommandDefinition(
                ReadSql,
                new { Ids = rows.Select(r => r.TenantId).ToList() },
                commandTimeout: CommandTimeoutSeconds,
                cancellationToken: ct));

            var byId = new Dictionary<string, InstallFact>(StringComparer.Ordinal);
            foreach (var f in facts) byId[f.TenantId] = f;

            foreach (var row in rows)
            {
                // 두 문장 사이에 사라진 행 — 판정 없음(「확인 불가」)으로 둔다.
                if (!byId.TryGetValue(row.TenantId, out var f)) continue;
                row.InstallState = Decide(f.LastErpStartAt, f.HasUpdateReport, f.HasSerialPass);
                row.LastErpStartAt = f.LastErpStartAt;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // #15 · 고객 키 · 회사명은 로그에 넣지 않는다.
            logger.LogWarning(ex, "[TenantsAdmin] ERP 설치 판정 실패 — 이 칸만 확인 불가로 둔다(대상 {Count}행)", rows.Count);
        }
    }

    private sealed class InstallFact
    {
        public string TenantId { get; set; } = "";
        public DateTime? LastErpStartAt { get; set; }
        // 결과형은 판마다 다르다 — long 으로 받는다(MySQL 8 실물은 게이트가 잰다).
        public long HasUpdateReport { get; set; }
        public long HasSerialPass { get; set; }
    }
}
