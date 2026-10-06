using System.Collections.Concurrent;
using Dapper;
using HitPan.Backoffice.API.Security;
using MySqlConnector;

namespace HitPan.Backoffice.API.Services;

/// <summary>
/// 🔴 ⓞ <b>라이브 계정 판독</b> — 요청당 1회 · <b>캐시 없음</b> (20261007작10 ①사이클 갈래 ㄱ · 설계 §3-3).
///
/// <para><b>왜 캐시가 없나.</b> TTL 을 두는 순간 「즉시 차단」이 그 TTL 만큼 거짓이 된다(설계 §3-3-나 결정).
/// 이 레포에는 같은 구멍이 <b>이미 적발되고 못 봉합된 채</b> 있다 — 작3·작4 병렬검증 P1-01
/// 「계정을 끈 뒤에도 8시간 토큰이 계속 통과한다」. 백오피스에 같은 구멍을 새로 파지 않는 것이
/// 이 클래스의 존재 이유다. ⇒ <b>성능을 이유로 여기에 <c>IMemoryCache</c> 를 끼우지 마라.</b>
/// 게이트 G-8ㄷ 가 「DB 를 바꾼 직후 첫 요청」이 403 인지를 10회 반복해서 잰다 — 캐시를 넣으면 빨간불이다.
/// 운영에서 실제로 느리다고 <b>측정되면</b> 그때 별건으로 올린다(선반영 금지 · 헌법 #33).</para>
///
/// <para><b>왜 왕복 1회인가.</b> 헌법 #16 — <c>MySqlConnection</c> + <c>Task.WhenAll</c> 금지.
/// 세 표를 <c>UNION ALL</c> 로 묶어 <b>연결 1개 · 왕복 1회</b>로 읽는다. 세 조회 모두 PK 점 조회
/// (<c>platform_admins.admin_id</c> · <c>bo_users.user_id</c> · <c>reseller_accounts.account_id</c> — 실측 DESCRIBE 2026-10-07).
/// 설계 실측: 중위 ≈1.2ms.</para>
///
/// <para>🔴 <b><c>reseller_accounts</c> 갈래는 조건부</b>(설계 §3-3-다). 로컬 상설 <c>hitpan_backoffice</c> 에는
/// 그 표가 <b>없다</b>(실측 2026-10-07 — <c>ERROR 1146</c> · 출하 DDL 보다 낡음). 표가 없으면 그 갈래를 뺀 쿼리를 쓴다.
/// 판별은 <b>연결문자열당 1회</b>만 하고 그 뒤로는 안 묻는다 — 요청당 판별 금지(설계값).
/// ⚠️ 이 1회 판별은 <b>표의 모양</b>을 기억하는 것이고 <b>계정의 상태</b>를 기억하는 것이 아니다 —
/// 위 「캐시 금지」와 충돌하지 않는다(계정 상태는 매 요청 새로 읽는다).</para>
/// </summary>
public interface IBoLiveAccountReader
{
    /// <summary>토큰 <c>sub</c> 로 세 표를 한 왕복에 읽어 <b>지금</b>의 계정 상태를 돌려준다.</summary>
    Task<BoLiveAccount> ReadAsync(string sub, CancellationToken ct = default);
}

/// <summary>ⓞ 판독 결과 한 줄(표별 원본).</summary>
public sealed class BoLiveAccountRow
{
    /// <summary>어느 표에서 왔나 — <c>platform_admins</c> · <c>bo_users</c> · <c>reseller_accounts</c>.</summary>
    public string Src { get; set; } = "";
    public string? Role { get; set; }
    public string? ResellerId { get; set; }
    public int IsActive { get; set; }
}

/// <summary>
/// ⓞ 판독의 <b>종합 판정</b>. 여러 표에 같은 사람이 있을 수 있는 2단 전환 중을 전제로(설계 §3-3-다),
/// 역할이 다르면 <b>좁은 쪽</b>을 쓰고 비활성이 하나라도 있으면 비활성으로 본다(fail-closed).
/// </summary>
public sealed class BoLiveAccount
{
    /// <summary>세 표 어디에도 없다 = 계정 삭제·이관 미완 ⇒ 호출자가 403 으로 끊는다.</summary>
    public bool Found { get; init; }

    /// <summary>한 줄이라도 <c>is_active=0</c> 이면 false.</summary>
    public bool IsActive { get; init; }

    /// <summary>좁은 쪽으로 접은 4값 역할. 모르는 값만 있었으면 null(fail-closed).</summary>
    public string? EffectiveRole { get; init; }

    /// <summary>DB 가 지금 말하는 소속 대리점. 본사 계정이면 null.</summary>
    public string? ResellerId { get; init; }

    /// <summary>판독에 쓰인 원본 줄(진단·로그용).</summary>
    public IReadOnlyList<BoLiveAccountRow> Rows { get; init; } = Array.Empty<BoLiveAccountRow>();

    public static BoLiveAccount NotFound { get; } = new() { Found = false, IsActive = false };
}

public sealed class BoLiveAccountReader : IBoLiveAccountReader
{
    private readonly IConfiguration _config;
    private readonly ILogger<BoLiveAccountReader> _logger;

    // 연결문자열당 1회 판별(요청당 판별 금지 · 설계 §3-3-다). 표의 "모양"만 기억한다.
    private static readonly ConcurrentDictionary<string, bool> ResellerAccountsPresent = new();

    public BoLiveAccountReader(IConfiguration config, ILogger<BoLiveAccountReader> logger)
    {
        _config = config;
        _logger = logger;
    }

    // ⓞ 쿼리 — 왕복 1회(#16). CAST(... AS CHAR) 은 char(36) 키를 GuidFormat 설정과 무관하게 문자열로 받기 위함
    // (기존 BackofficeAuthController.cs:43·96 과 같은 관례).
    private const string SqlWithReseller = @"
        SELECT 'platform_admins' AS Src, pa.role AS Role,
               CAST(NULL AS CHAR(36)) AS ResellerId, pa.is_active AS IsActive
          FROM platform_admins pa WHERE pa.admin_id = @Sub
        UNION ALL
        SELECT 'bo_users', bu.role,
               CAST(bu.reseller_id AS CHAR(36)), bu.is_active
          FROM bo_users bu WHERE bu.user_id = @Sub
        UNION ALL
        SELECT 'reseller_accounts', ra.role,
               CAST(ra.reseller_id AS CHAR(36)), ra.is_active
          FROM reseller_accounts ra WHERE ra.account_id = @Sub";

    private const string SqlWithoutReseller = @"
        SELECT 'platform_admins' AS Src, pa.role AS Role,
               CAST(NULL AS CHAR(36)) AS ResellerId, pa.is_active AS IsActive
          FROM platform_admins pa WHERE pa.admin_id = @Sub
        UNION ALL
        SELECT 'bo_users', bu.role,
               CAST(bu.reseller_id AS CHAR(36)), bu.is_active
          FROM bo_users bu WHERE bu.user_id = @Sub";

    public async Task<BoLiveAccount> ReadAsync(string sub, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sub)) return BoLiveAccount.NotFound;

        var cs = ConnString();
        await using var db = new MySqlConnection(cs);
        await db.OpenAsync(ct);

        var hasReseller = await ResellerAccountsPresentAsync(db, cs, ct);
        var sql = hasReseller ? SqlWithReseller : SqlWithoutReseller;

        var rows = (await db.QueryAsync<BoLiveAccountRow>(
            new CommandDefinition(sql, new { Sub = sub }, cancellationToken: ct))).ToList();

        return Fold(rows);
    }

    /// <summary>
    /// 여러 표의 줄을 <b>좁은 쪽 우선</b>으로 접는다(설계 §3-3-다).
    /// 테스트가 직접 부를 수 있게 <c>internal static</c> — 판정식을 시험이 베껴 쓰지 않게 하려는 것이다.
    /// </summary>
    internal static BoLiveAccount Fold(IReadOnlyList<BoLiveAccountRow> rows)
    {
        if (rows.Count == 0) return BoLiveAccount.NotFound;

        // 한 줄이라도 꺼져 있으면 꺼진 것으로 본다 — fail-closed.
        var active = rows.All(r => r.IsActive == 1);

        string? narrowest = null;
        foreach (var r in rows)
        {
            var n = BoRoles.Normalize(r.Role);
            if (n is null) continue;                        // 모르는 값은 넓은 쪽으로 추측하지 않는다
            if (narrowest is null) { narrowest = n; continue; }
            // 갈래가 다르면 비교가 아니다 — 소속 변경은 호출자가 403 으로 끊는다.
            // 여기서는 등급만 좁은 쪽으로 접는다(같은 갈래일 때만 의미가 있다).
            if (BoRoles.Rank(n) < BoRoles.Rank(narrowest)) narrowest = n;
        }

        // 소속: 대리점 줄(reseller_id 가 있는 줄)이 하나라도 있으면 그 값. 여러 개면 설계상 같아야 하고,
        // 다르면 호출자가 토큰과 대조해 403 으로 끊는다(여기서 섞어 고르지 않는다 — 첫 값을 그대로 넘긴다).
        var rid = rows.Select(r => r.ResellerId)
                      .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        return new BoLiveAccount
        {
            Found = true,
            IsActive = active,
            EffectiveRole = narrowest,
            ResellerId = string.IsNullOrWhiteSpace(rid) ? null : rid,
            Rows = rows,
        };
    }

    private async Task<bool> ResellerAccountsPresentAsync(MySqlConnection db, string cs, CancellationToken ct)
    {
        if (ResellerAccountsPresent.TryGetValue(cs, out var known)) return known;

        var n = await db.ExecuteScalarAsync<int>(new CommandDefinition(
            @"SELECT COUNT(*) FROM information_schema.tables
               WHERE table_schema = DATABASE() AND table_name = 'reseller_accounts'",
            cancellationToken: ct));
        var present = n > 0;
        ResellerAccountsPresent[cs] = present;
        if (!present)
        {
            // #15 — 조용히 넘기지 않는다. 이 DB 는 출하 DDL 보다 낡았다는 사실이 로그에 남아야 한다.
            _logger.LogWarning(
                "[BoLiveAccount] reseller_accounts 표가 없다 — 그 갈래를 뺀 쿼리로 판독한다. "
              + "대리점 계정은 이 DB 에서 로그인할 수 없다(출하 DDL installer/backoffice/00_backoffice_core.sql:173 참조).");
        }
        return present;
    }

    /// <summary>표 모양 판별 기억을 비운다 — <b>시험용</b>(격리 DB 를 다시 깔면 모양이 바뀐다).</summary>
    internal static void ForgetSchemaProbe() => ResellerAccountsPresent.Clear();

    private string ConnString() =>
        _config.GetConnectionString("BackofficeDb")
        ?? _config.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:BackofficeDb 미설정 — 라이브 계정 판독 불가");
}
