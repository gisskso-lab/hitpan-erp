using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// 계정 사용 한도가 찼을 때 던진다 (20261005작3 · 설계 §2).
/// </summary>
/// <remarks>
/// <see cref="InvalidOperationException"/> 을 잇는다 — 기존 <c>catch (InvalidOperationException)</c> 경로보다
/// <b>먼저</b> 잡아 409 <c>account_seat_full</c> 로 낸다(설계 §4).
/// </remarks>
public sealed class AccountSeatFullException : InvalidOperationException
{
    public int Active { get; }
    public int Limit { get; }

    public AccountSeatFullException(int active, int limit)
        : base($"계정 사용 한도({limit}개)가 찼습니다. 계정을 더 쓰려면 [구독계정추가]를 눌러 주세요.")
    {
        Active = active;
        Limit = limit;
    }
}

/// <summary>/users 머리 카드 3개와 엑셀 사전 판정이 쓰는 한 장 (잠금 없음).</summary>
public sealed record AccountSeatSnapshot(int Active, int BaseLimit, int Extra, int Limit, string Tier, bool UsedFloor);

/// <summary>
/// 🔴 <b>판정기 하나</b> — 계정이 생기거나 되살아나는 입구 5곳(Create · Bulk · Bootstrap 부모 · Update 0→1 · Resume)이
/// 전부 이 한 곳을 거친다 (20261005작3 · 설계 §2·§3).
/// </summary>
/// <remarks>
/// <para><b>자리</b> = 활성 계정(<c>is_active=1 AND is_deleted=0</c> · 대표 포함 · D-10). 「사용 안 함」·「계정폐기」는 안 센다.</para>
/// <para><b>한도</b> = 기본(<c>local_subscription.max_users</c> · 본사가 보낸 값) + 추가(<c>extra_accounts</c>).
/// 🔴 반증 F1(10/5 PM): 본사 웹훅이 옛 기본 3 을 실어 보내 5 를 덮는다 ⇒ 기본이 <see cref="DefaultBaseAccounts"/> 보다 작으면
/// <b>5 로 본다</b>(바닥값 · LogWarning). 저장된 값은 받은 그대로 둔다(덮기 막지 않음 — 본사 수정은 백오피스 트랙).</para>
/// <para><b>잠금 순서</b>(반증 P2 · F10 · F11): 트랜잭션 <b>첫 문장</b>은 <c>local_company</c> 행 <c>FOR UPDATE</c> 다.
/// ⚠️ PM 지시는 「tenants 행」이었으나 ERP 코드에는 <c>tenants</c> 에 INSERT 하는 길이 0곳이다(grep · 10/5) — 늘 있는 행이 아니다.
/// <c>local_company</c> 는 부모계정을 만들기 전에 bootstrap 이 반드시 넣는 행(<c>CreateParentAsync</c> 가 없으면 거절)이라
/// 계정이 생기는 모든 시점에 존재한다 ⇒ 행 없음 틈(F11)이 닫히고, 모든 입구가 같은 행을 먼저 잡아 교착(1213)이 안 생긴다.</para>
/// <para>🔴 REPEATABLE READ: 잠금 읽기는 read view 를 만들지 않는다 ⇒ 잠금 <b>뒤의</b> COUNT 가 첫 일반 읽기라 상대가 커밋한 행이 보인다.
/// 잠금 앞에 일반 SELECT 를 넣으면 F-6 이 되살아난다(G-A6).</para>
/// </remarks>
public static class AccountSeatGuard
{
    /// <summary>기본 제공 계정 수의 바닥값(베이직 최소 · 출하 DDL 기본값과 같은 수). 프로 8 은 본사가 보낸다.</summary>
    public const int DefaultBaseAccounts = 5;

    private sealed class SubRow
    {
        public int MaxUsers { get; set; }
        public int ExtraAccounts { get; set; }
        public string? Tier { get; set; }
    }

    /// <summary>
    /// 트랜잭션 안에서 부른다 — <b>그 트랜잭션의 첫 문장</b>이어야 한다. 차면 <see cref="AccountSeatFullException"/>.
    /// </summary>
    public static async Task EnsureSeatAsync(
        IDbConnection db, IDbTransaction tx, string tenantId, int adding, ILogger? logger, CancellationToken ct)
    {
        // ① 잠금 — 같은 회사의 계정 추가·되살리기를 줄 세운다(첫 문장).
        // ⬛ [봉합1 전 · 결과를 버렸다] await db.ExecuteScalarAsync<string?>(... "SELECT tenant_id FROM local_company ... FOR UPDATE" ...);
        // 🔴 10/5 봉합1 P2-05 — 행이 없으면 틈 잠금이라 두 트랜잭션이 서로 안 막는다(같이 COUNT · 같이 INSERT = 한도+1).
        //   행 없음이면 그 연결이 이름 잠금(AcquireAsync)을 쥐고 있어야만 진행한다. 아니면 거절(조용히 열지 않는다).
        var lockedRow = await db.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT tenant_id FROM local_company WHERE tenant_id = @TenantId FOR UPDATE",
            new { TenantId = tenantId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        if (lockedRow is null)
        {
            var mine = await db.ExecuteScalarAsync<long?>(new CommandDefinition(
                "SELECT IS_USED_LOCK(@Name) = CONNECTION_ID()",
                new { Name = SeatLockName(tenantId) }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            if (mine != 1)
            {
                logger?.LogWarning("[AccountSeat] local_company 행 없음 · 이름 잠금 없음 — 계정 추가를 거절한다 tenant={Tenant}", tenantId);
                throw new InvalidOperationException(SeatLockBusyMessage);
            }
        }

        var snap = await ReadAsync(db, tx, tenantId, ct).ConfigureAwait(false);
        if (snap.UsedFloor)
            logger?.LogWarning("[AccountSeat] 기본 계정 수가 바닥값보다 작거나 없다 — {Floor} 으로 본다 tenant={Tenant}",
                DefaultBaseAccounts, tenantId);

        if (snap.Active + adding > snap.Limit)
            throw new AccountSeatFullException(snap.Active, snap.Limit);
    }

    /// <summary>잠금 문구 — 이름 잠금을 못 얻었거나 잠금 없이 들어왔을 때(10/5 봉합1 P2-05).</summary>
    public const string SeatLockBusyMessage = "다른 계정 작업이 끝나기를 기다리다 멈췄습니다. 잠시 후 다시 해 주세요.";

    /// <summary>이름 잠금 대기 초(10/5 봉합1 P2-05). InnoDB 잠금 대기(50초)보다 짧게.</summary>
    public const int SeatLockTimeoutSeconds = 10;

    /// <summary>회사별 이름 잠금 이름 — 'hitpan_seat_' + tenant(48자 ≤ 64).</summary>
    public static string SeatLockName(string tenantId) => "hitpan_seat_" + tenantId;

    /// <summary>기본 + 추가 = 한도. int 를 넘으면 <see cref="int.MaxValue"/> 로 고정, 음수는 0(10/5 봉합1 P1-03 덤).</summary>
    public static int CombineLimit(int baseLimit, int extra)
    {
        var sum = (long)baseLimit + extra;
        if (sum < 0) return 0;
        return sum > int.MaxValue ? int.MaxValue : (int)sum;
    }

    /// <summary>
    /// 🔴 10/5 봉합1 P2-05 — 계정이 생기거나 되살아나는 트랜잭션의 <b>첫 문장</b>. 돌려받은 것을 <c>await using</c> 으로 쥔다.
    /// </summary>
    /// <remarks>
    /// <para>① <c>local_company</c> 행 <c>FOR UPDATE</c>(종전과 같다 · 커밋 때 풀린다).</para>
    /// <para>② 행이 없으면 틈 잠금끼리는 안 부딪힌다 ⇒ 이름 잠금(<c>GET_LOCK</c> · <see cref="SeatLockTimeoutSeconds"/>초)으로 대신 줄 세운다.
    /// 못 얻으면 거절. 이름 잠금은 트랜잭션이 아니라 연결에 붙으므로 커밋 뒤 <c>DisposeAsync</c> 가 <c>RELEASE_LOCK</c> 한다.</para>
    /// <para>잠금 읽기·<c>GET_LOCK</c> 은 read view 를 만들지 않는다 ⇒ 뒤의 COUNT 가 첫 일반 읽기다(G-A6 그대로).</para>
    /// </remarks>
    public static async Task<AccountSeatLock> AcquireAsync(
        IDbConnection db, IDbTransaction tx, string tenantId, ILogger? logger, CancellationToken ct)
    {
        var row = await db.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT tenant_id FROM local_company WHERE tenant_id = @TenantId FOR UPDATE",
            new { TenantId = tenantId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        if (row is not null) return new AccountSeatLock(db, tx, null, logger);

        logger?.LogWarning("[AccountSeat] local_company 행 없음 — 이름 잠금으로 대신 줄 세운다 tenant={Tenant}", tenantId);
        var name = SeatLockName(tenantId);
        var got = await db.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT GET_LOCK(@Name, @Timeout)",
            new { Name = name, Timeout = SeatLockTimeoutSeconds }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        if (got != 1)
        {
            logger?.LogWarning("[AccountSeat] 이름 잠금을 {Sec}초 안에 못 얻었다 — 거절 tenant={Tenant}", SeatLockTimeoutSeconds, tenantId);
            throw new InvalidOperationException(SeatLockBusyMessage);
        }
        return new AccountSeatLock(db, tx, name, logger);
    }

    /// <summary>잠금 없음 — 화면 카드·엑셀 사전 판정용. 서버 정본 판정은 <see cref="EnsureSeatAsync"/>.</summary>
    public static Task<AccountSeatSnapshot> GetSeatsAsync(IDbConnection db, string tenantId, CancellationToken ct) =>
        ReadAsync(db, null, tenantId, ct);

    /// <summary>
    /// 본사 보고 숫자 = <b>활성 자식계정 수 N</b>(D-17 · 설계 §10⑥). 총수(판정기) = 1 + N.
    /// 이름·아이디·사원 정보는 돌려주지 않는다(#18·#22).
    /// </summary>
    public static async Task<int> CountActiveChildrenAsync(IDbConnection db, string tenantId, CancellationToken ct)
    {
        var n = await db.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM users WHERE tenant_id = @TenantId AND is_parent = 0 AND is_active = 1 AND is_deleted = 0",
            new { TenantId = tenantId }, cancellationToken: ct)).ConfigureAwait(false);
        return (int)n;
    }

    private static async Task<AccountSeatSnapshot> ReadAsync(
        IDbConnection db, IDbTransaction? tx, string tenantId, CancellationToken ct)
    {
        var sub = await db.QueryFirstOrDefaultAsync<SubRow>(new CommandDefinition(
            "SELECT max_users AS MaxUsers, extra_accounts AS ExtraAccounts, subscription_tier AS Tier "
          + "FROM local_subscription WHERE tenant_id = @TenantId",
            new { TenantId = tenantId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        // ② 셈 — 활성만(대표 포함). account_type 을 쓰지 않는다(F-4).
        var active = await db.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM users WHERE tenant_id = @TenantId AND is_active = 1 AND is_deleted = 0",
            new { TenantId = tenantId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        var usedFloor = sub is null || sub.MaxUsers < DefaultBaseAccounts;
        var baseLimit = usedFloor ? DefaultBaseAccounts : sub!.MaxUsers;
        var extra = Math.Max(sub?.ExtraAccounts ?? 0, 0);
        // ⬛ [봉합1 전] return new AccountSeatSnapshot((int)active, baseLimit, extra, baseLimit + extra, ...
        //   🔴 P1-03 덤 — int 합이 넘치면 음수 한도 ⇒ 그 회사 계정 추가·되살리기 전부 409. CombineLimit 으로 고정.
        return new AccountSeatSnapshot((int)active, baseLimit, extra, CombineLimit(baseLimit, extra),
            string.IsNullOrWhiteSpace(sub?.Tier) ? "basic" : sub!.Tier!, usedFloor);
    }
}

/// <summary>
/// <see cref="AccountSeatGuard.AcquireAsync"/> 가 돌려주는 잠금 손잡이(10/5 봉합1 P2-05).
/// 행 잠금이면 할 일 없음(커밋이 푼다) · 이름 잠금이면 <c>RELEASE_LOCK</c>.
/// </summary>
public sealed class AccountSeatLock : IAsyncDisposable
{
    private readonly IDbConnection _db;
    private readonly IDbTransaction _tx;
    private readonly string? _name;
    private readonly ILogger? _logger;

    internal AccountSeatLock(IDbConnection db, IDbTransaction tx, string? name, ILogger? logger)
    {
        _db = db;
        _tx = tx;
        _name = name;
        _logger = logger;
    }

    /// <summary>이름 잠금으로 대신했는가(행 없음 갈래).</summary>
    public bool UsesNamedLock => _name is not null;

    public async ValueTask DisposeAsync()
    {
        if (_name is null) return;
        try
        {
            // 커밋·되돌림이 끝난 트랜잭션은 Connection 이 null — 그때는 트랜잭션 없이 보낸다.
            var activeTx = _tx.Connection is null ? null : _tx;
            await _db.ExecuteScalarAsync<long?>(new CommandDefinition(
                "SELECT RELEASE_LOCK(@Name)", new { Name = _name }, transaction: activeTx)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[AccountSeat] 이름 잠금 풀기 실패 — 연결이 닫힐 때 풀린다 name={Name}", _name);
        }
    }
}
