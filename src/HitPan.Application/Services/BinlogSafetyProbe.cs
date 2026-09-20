using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// 🔴 20260920작1 갈래 S1 — 서버의 바이너리 로그 설정을 보고 이월잔액 매칭 경로를 가른다.
/// <para>
/// 근거: 작업지시서 <c>docs/운영기록/20260920작1_수금등록_RC_안전경로_작업지시서.md</c> §4-2 ·
/// 설계 <c>docs/설계/erp/20260920_설계_수금등록_RC_안전경로.md</c> §2·§3 ·
/// 선행검증 <c>docs/검증/선행/20260920_선행검증서_수금등록_READCOMMITTED_서버설정의존_실측.md</c> F1·F5·F8.
/// </para>
/// <list type="bullet">
/// <item><c>log_bin=ON</c> <b>AND</b> <c>binlog_format=STATEMENT</c> 이면 READ COMMITTED 쓰기가 ERROR 1665 로 거절된다(F1) → RR 경로.</item>
/// <item>그 밖(로그 꺼짐 · MIXED · ROW)은 3판 그대로 RC 경로(F5).</item>
/// <item>조회 실패·권한거절·타임아웃 = <b>모르면 도는 쪽</b>(RR). 판정 실패가 등록을 막지 않는다.</item>
/// </list>
/// 판정 문장은 시스템 변수 조회 — 테이블·잠금 접근 0(F8). 프로세스 값 1개 · TTL 5분 · 1665 를 만나면 <see cref="Invalidate"/>.
/// </summary>
public interface IBinlogSafetyProbe
{
    /// <summary>이 서버에서 써야 할 매칭 모드. 캐시가 살아 있으면 DB 왕복 0.</summary>
    Task<LegacyMatchMode> GetModeAsync(IDbConnection db, ILogger? logger, CancellationToken ct);

    /// <summary>캐시를 버린다 — 실행 중 ERROR 1665 를 만났을 때(설계 §3 「즉시 무효화」).</summary>
    void Invalidate();
}

/// <inheritdoc cref="IBinlogSafetyProbe"/>
public sealed class BinlogSafetyProbe : IBinlogSafetyProbe
{
    /// <summary>
    /// 캐시 수명. <c>SET GLOBAL binlog_format</c> 은 재기동 없이도 바뀌므로 영구 캐시는 위험하고,
    /// 매번 조회는 등록마다 왕복이 는다. 5분 = 「고객이 설정을 고친 뒤 다시 저장」이 사람 시간 안에 반영되는 값(설계 §3 · PM 승인 A-5).
    /// </summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    private readonly TimeSpan _ttl;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private LegacyMatchMode? _cached;
    private DateTimeOffset _expiresAt;
    private int _queryCount;

    public BinlogSafetyProbe(TimeSpan? ttl = null, TimeProvider? clock = null)
    {
        _ttl = ttl ?? DefaultTtl;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>지금까지 서버에 실제로 물어본 횟수 — 캐시가 도는지 게이트가 잰다(G-RC9).</summary>
    public int QueryCount => Volatile.Read(ref _queryCount);

    /// <inheritdoc/>
    public async Task<LegacyMatchMode> GetModeAsync(IDbConnection db, ILogger? logger, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_cached is { } hit && _clock.GetUtcNow() < _expiresAt) return hit;
        }

        var mode = await ProbeAsync(db, logger, ct).ConfigureAwait(false);

        lock (_gate)
        {
            _cached = mode;
            _expiresAt = _clock.GetUtcNow() + _ttl;
        }
        return mode;
    }

    /// <inheritdoc/>
    public void Invalidate()
    {
        lock (_gate)
        {
            _cached = null;
            _expiresAt = default;
        }
    }

    private async Task<LegacyMatchMode> ProbeAsync(IDbConnection db, ILogger? logger, CancellationToken ct)
    {
        Interlocked.Increment(ref _queryCount);
        try
        {
            // CAST(… AS CHAR) — @@log_bin 은 서버·드라이버에 따라 0/1 · ON/OFF 로 돌아온다. 문자열로 고정해 두고 판정은 한 곳(Decide)에서만.
            var row = await db.QueryFirstOrDefaultAsync<Vars>(new CommandDefinition(
                "SELECT CAST(@@log_bin AS CHAR) AS LogBin, CAST(@@binlog_format AS CHAR) AS BinlogFormat",
                cancellationToken: ct)).ConfigureAwait(false);

            if (row is null)
            {
                logger?.LogWarning("[BinlogSafetyProbe] 서버 바이너리 로그 설정을 읽지 못했다(빈 결과) — 안전한 쪽(RepeatableReadLocking)으로 간다.");
                return LegacyMatchMode.RepeatableReadLocking;
            }

            var mode = Decide(row.LogBin, row.BinlogFormat);
            logger?.LogInformation("[BinlogSafetyProbe] log_bin={LogBin} binlog_format={BinlogFormat} → {Mode}", row.LogBin, row.BinlogFormat, mode);
            return mode;
        }
        catch (Exception ex)
        {
            // 판정 실패가 수금·지급 등록을 막으면 안 된다(#20) — 두 서버 모두에서 도는 RR 로 간다.
            logger?.LogWarning(ex, "[BinlogSafetyProbe] 서버 바이너리 로그 설정 조회 실패 — 안전한 쪽(RepeatableReadLocking)으로 간다.");
            return LegacyMatchMode.RepeatableReadLocking;
        }
    }

    /// <summary>
    /// 판정 한 곳(순수 함수 · DB 불필요 · G-RC7).
    /// 위험 = <c>log_bin</c> 켜짐 <b>AND</b> <c>binlog_format=STATEMENT</c> 뿐이다. 모르는 값은 전부 RR(도는 쪽).
    /// </summary>
    public static LegacyMatchMode Decide(string? logBin, string? binlogFormat)
    {
        var on = ParseLogBin(logBin);
        if (on is null) return LegacyMatchMode.RepeatableReadLocking;      // 모르는 값
        if (on == false) return LegacyMatchMode.ReadCommittedFresh;        // 로그를 안 쓰면 1665 조건이 성립하지 않는다(선행 §3)

        var fmt = binlogFormat?.Trim();
        if (string.IsNullOrEmpty(fmt)) return LegacyMatchMode.RepeatableReadLocking;
        if (string.Equals(fmt, "STATEMENT", StringComparison.OrdinalIgnoreCase)) return LegacyMatchMode.RepeatableReadLocking;
        if (string.Equals(fmt, "MIXED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fmt, "ROW", StringComparison.OrdinalIgnoreCase)) return LegacyMatchMode.ReadCommittedFresh;

        return LegacyMatchMode.RepeatableReadLocking;                      // 처음 보는 형식 = 모른다
    }

    private static bool? ParseLogBin(string? value)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (v is "0" || string.Equals(v, "OFF", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "FALSE", StringComparison.OrdinalIgnoreCase)) return false;
        if (v is "1" || string.Equals(v, "ON", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "TRUE", StringComparison.OrdinalIgnoreCase)) return true;
        return null;
    }

    private sealed class Vars
    {
        public string? LogBin { get; set; }
        public string? BinlogFormat { get; set; }
    }
}
