using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;

namespace HitPan.Application.Services;

/// <summary>
/// 🔴 20260920작1 갈래 S1 — 이월잔액 매칭 트랜잭션의 경로. <b>호출자가 선언</b>하고, 이 값 하나가
/// 격리수준(<see cref="LegacyBalanceMatching.IsolationFor"/>)과 잠금식(R 재계산 · 전표 SUM)을 함께 고른다.
/// 「RR 로 열고 일반 읽기로 판정」하는 조합은 코드로 표현할 수 없다(설계 §5-2 — 3판이 밟은 사고를 구조로 막는다).
/// </summary>
public enum LegacyMatchMode
{
    /// <summary>READ COMMITTED + 일반 읽기 — 3판 그대로. 안전한 서버(로그 꺼짐 · MIXED · ROW)에서 쓴다.</summary>
    ReadCommittedFresh = 0,

    /// <summary>REPEATABLE READ + 잠금 읽기 — <c>binlog_format=STATEMENT</c> 서버에서 쓴다. 정확성 계약은 RC 와 같고, 대가는 지연·재시도다.</summary>
    RepeatableReadLocking = 1
}

/// <summary>
/// 🔴 20260915작1 개정 3판 갈래 R1 — 「이전 프로그램 이월잔액」 매칭 공용 식(L0 · M · R).
/// <para>
/// 근거: 작업지시서 <c>docs/운영기록/20260915작1_자료이관_머리없는줄_분류봉합_작업지시서.md</c> §15-2·§15-3 ·
/// 설계 <c>docs/설계/erp/20260915_설계_자료이관_머리없는줄_분류봉합.md</c> §20·§21·§22 ·
/// 개발명세서 <c>docs/개발/erp/20260917작1_갈래R1_공용식_수금지급서버_개발명세서.md</c>.
/// </para>
/// <list type="bullet">
/// <item>L0 = 이관 이월잔액(미수 = + 잔액 · 미지급 = − 잔액 절대값).</item>
/// <item>M = 이관 뒤 이월 쪽에 맞춘 돈 — 수금 <c>ref_doc_type='legacy_balance'</c>(ref = 거래처) · 지급 <c>payment_type='legacy_balance'</c>(ref = 거래처)
///   + (갈래 E 호환) 사람이 이관 명세서·매입에 붙인 수금·지급 · 이관 매입의 확정 반품.</item>
/// <item>R = L0 − M. <b>클램프 없음</b>(R&lt;0 은 대사표 경고 · 숨김 금지).</item>
/// <item>🔴 이관 수금·지급 줄(<c>source_type='migration'</c>)은 <b>M 계산에서만</b> 빠진다. 목록 조회에서는 절대 빼지 않는다(사장님 원칙).</item>
/// <item>키 = <c>partner_id</c>(balance_id 아님 — 이월잔액 행 재삽입에도 M 이 유지된다 · G8-g).</item>
/// </list>
/// 모든 SQL 파라미터 이름은 <c>@TenantId</c> 고정.
/// </summary>
public static class LegacyBalanceMatching
{
    /// <summary><c>collections.ref_doc_type</c> · <c>payments.payment_type</c> 에 들어가는 값(14자 · 두 칼럼 모두 varchar(20) 이상).</summary>
    public const string RefType = "legacy_balance";

    /// <summary>화면·문구에 쓰는 이름.</summary>
    public const string Label = "이전 프로그램 이월잔액";

    /// <summary>
    /// S2(사장님 결재 대기 · 작지 §15-9) — 이월 기준일까지의 날짜로 입력한 수금·지급은 이월잔액에 맞추지 못하게 한다.
    /// 결재 결과로 <b>이 상수 한 곳만</b> 바뀐다. 조건 판정은 <see cref="IsBlockedByBaseDate"/> 한 곳.
    /// </summary>
    public const bool RejectBeforeBaseDate = true;

    // ── 고객 문구 (한 곳) ──
    internal const string MsgOverRemaining =
        "이전 프로그램 이월잔액이 {0:N0}원 남았습니다. 그보다 큰 금액은 남은 금액까지만 이월잔액으로 처리하고, 나머지는 거래명세서에 맞춰 주세요.";
    internal const string MsgBeforeBaseDate =
        "이전 프로그램 기준일({0:yyyy-MM-dd})까지의 {1}은 이미 이월잔액에 들어 있어 맞출 수 없습니다. 그 뒤에 {2} 돈이면 {2} 날짜로 입력해 주세요.";
    internal const string MsgWrongPartner =
        "이전 프로그램 이월잔액은 같은 거래처에만 맞출 수 있습니다.";
    internal const string MsgNoReceivable =
        "이 거래처에는 이전 프로그램에서 넘어온 받을 돈(이월 미수금)이 없습니다.";
    internal const string MsgNoPayable =
        "이 거래처에는 이전 프로그램에서 넘어온 줄 돈(이월 미지급금)이 없습니다.";
    internal const string MsgAmountNotPositive =
        "금액은 0원보다 커야 합니다.";

    /// <summary>
    /// 미수 파생표 — 열: <c>partner_id, base_date, legacy_amount(L0), matched_amount(M), remaining_amount(R)</c>.
    /// 이 회사의 이월잔액 행 전부(부호 반대 거래처는 L0=0).
    /// </summary>
    public const string ReceivableRemainingSql = """
        SELECT plb.partner_id AS partner_id,
               plb.base_date AS base_date,
               GREATEST(plb.balance_amount, 0) AS legacy_amount,
               IFNULL(lm.amt, 0) + IFNULL(em.amt, 0) AS matched_amount,
               GREATEST(plb.balance_amount, 0) - IFNULL(lm.amt, 0) - IFNULL(em.amt, 0) AS remaining_amount
          FROM partner_legacy_balances plb
          LEFT JOIN (
            SELECT lc.partner_id, SUM(lc.amount) AS amt
              FROM collections lc
             WHERE lc.tenant_id = @TenantId AND lc.is_active = 1
               AND COALESCE(lc.source_type, '') <> 'migration'
               AND lc.ref_doc_type = 'legacy_balance' AND lc.ref_doc_id = lc.partner_id
             GROUP BY lc.partner_id
          ) lm ON lm.partner_id = plb.partner_id
          LEFT JOIN (
            SELECT sd.partner_id, SUM(ec.amount) AS amt
              FROM collections ec
              JOIN sales_deliveries sd ON sd.delivery_id = ec.ref_doc_id AND sd.tenant_id = ec.tenant_id
             WHERE ec.tenant_id = @TenantId AND ec.is_active = 1 AND ec.ref_doc_type = 'sales_delivery'
               AND COALESCE(ec.source_type, '') <> 'migration'
               AND COALESCE(sd.source_type, '') = 'migration'
             GROUP BY sd.partner_id
          ) em ON em.partner_id = plb.partner_id
         WHERE plb.tenant_id = @TenantId
        """;

    /// <summary>미지급 파생표 — 열은 <see cref="ReceivableRemainingSql"/> 과 같다.</summary>
    public const string PayableRemainingSql = """
        SELECT plb.partner_id AS partner_id,
               plb.base_date AS base_date,
               GREATEST(-plb.balance_amount, 0) AS legacy_amount,
               IFNULL(lm.amt, 0) + IFNULL(em.amt, 0) + IFNULL(rm.amt, 0) AS matched_amount,
               GREATEST(-plb.balance_amount, 0) - IFNULL(lm.amt, 0) - IFNULL(em.amt, 0) - IFNULL(rm.amt, 0) AS remaining_amount
          FROM partner_legacy_balances plb
          LEFT JOIN (
            SELECT lp.partner_id, SUM(lp.amount) AS amt
              FROM payments lp
             WHERE lp.tenant_id = @TenantId AND lp.is_active = 1
               AND COALESCE(lp.source_type, '') <> 'migration'
               AND lp.payment_type = 'legacy_balance' AND lp.ref_order_id = lp.partner_id
             GROUP BY lp.partner_id
          ) lm ON lm.partner_id = plb.partner_id
          LEFT JOIN (
            SELECT pr.partner_id, SUM(ep.amount) AS amt
              FROM payments ep
              JOIN purchase_receipts pr ON pr.receipt_id = ep.ref_order_id AND pr.tenant_id = ep.tenant_id
             WHERE ep.tenant_id = @TenantId AND ep.is_active = 1 AND ep.payment_type = 'purchase'
               AND COALESCE(ep.source_type, '') <> 'migration'
               AND COALESCE(pr.source_type, '') = 'migration'
             GROUP BY pr.partner_id
          ) em ON em.partner_id = plb.partner_id
          LEFT JOIN (
            SELECT pr.partner_id, SUM(rti.supply_amount + rti.vat_amount) AS amt
              FROM purchase_returns rt
              JOIN purchase_return_items rti ON rti.return_id = rt.return_id AND rti.tenant_id = rt.tenant_id
              JOIN purchase_receipts pr ON pr.receipt_id = rt.receipt_id AND pr.tenant_id = rt.tenant_id
             WHERE rt.tenant_id = @TenantId AND rt.is_deleted = 0 AND rt.status = 'confirmed'
               AND COALESCE(pr.source_type, '') = 'migration'
             GROUP BY pr.partner_id
          ) rm ON rm.partner_id = plb.partner_id
         WHERE plb.tenant_id = @TenantId
        """;

    /// <summary>
    /// 「명세서 쪽」 수금 조건(<paramref name="alias"/> = collections 별칭).
    /// 활성 · <c>ref_doc_type='sales_delivery'</c> · 이월잔액 회사의 이관 수금 제외(종전 규칙) · M 에 들어간 사람 수금(이월잔액 거래처의 이관 명세서에 붙은 것) 제외.
    /// 이월잔액 행이 없는 회사는 종전 식과 같은 값(G8-k).
    /// </summary>
    public static string CollectionDocSideWhere(string alias) => $"""
        ({alias}.is_active = 1 AND {alias}.ref_doc_type = 'sales_delivery'
          AND NOT (COALESCE({alias}.source_type, '') = 'migration' AND {MdbLegacyPartnerBalance.HasLegacyBalanceSql})
          AND NOT (COALESCE({alias}.source_type, '') <> 'migration' AND EXISTS (
                SELECT 1 FROM sales_deliveries sd_lb
                  JOIN partner_legacy_balances plb_lb ON plb_lb.tenant_id = sd_lb.tenant_id AND plb_lb.partner_id = sd_lb.partner_id
                 WHERE sd_lb.tenant_id = {alias}.tenant_id AND sd_lb.delivery_id = {alias}.ref_doc_id
                   AND COALESCE(sd_lb.source_type, '') = 'migration')))
        """;

    /// <summary>「명세서 쪽」 지급 조건(<paramref name="alias"/> = payments 별칭). 수금과 대칭 · <c>payment_type='purchase'</c>.</summary>
    public static string PaymentDocSideWhere(string alias) => $"""
        ({alias}.is_active = 1 AND {alias}.payment_type = 'purchase'
          AND NOT (COALESCE({alias}.source_type, '') = 'migration' AND {MdbLegacyPartnerBalance.HasLegacyBalanceSql})
          AND NOT (COALESCE({alias}.source_type, '') <> 'migration' AND EXISTS (
                SELECT 1 FROM purchase_receipts pr_lb
                  JOIN partner_legacy_balances plb_lb ON plb_lb.tenant_id = pr_lb.tenant_id AND plb_lb.partner_id = pr_lb.partner_id
                 WHERE pr_lb.tenant_id = {alias}.tenant_id AND pr_lb.receipt_id = {alias}.ref_order_id
                   AND COALESCE(pr_lb.source_type, '') = 'migration')))
        """;

    /// <summary>「명세서 쪽」 매입반품 조건(<paramref name="alias"/> = purchase_returns 별칭). 확정 · 미삭제 · M 에 들어간 반품(이월잔액 거래처의 이관 매입 반품) 제외.</summary>
    public static string PurchaseReturnDocSideWhere(string alias) => $"""
        ({alias}.is_deleted = 0 AND {alias}.status = 'confirmed'
          AND NOT EXISTS (
                SELECT 1 FROM purchase_receipts pr_lb
                  JOIN partner_legacy_balances plb_lb ON plb_lb.tenant_id = pr_lb.tenant_id AND plb_lb.partner_id = pr_lb.partner_id
                 WHERE pr_lb.tenant_id = {alias}.tenant_id AND pr_lb.receipt_id = {alias}.receipt_id
                   AND COALESCE(pr_lb.source_type, '') = 'migration'))
        """;

    /// <summary>거래처 하나의 이월잔액 매칭 상태.</summary>
    public sealed record Remaining(string PartnerId, DateTime BaseDate, decimal LegacyAmount, decimal MatchedAmount, decimal RemainingAmount);

    /// <summary>
    /// 이 회사의 이월잔액 거래처 목록(L0&gt;0 또는 M≠0 인 거래처). R&lt;0 도 그대로 돌려준다(클램프 없음).
    /// 화면 한 줄(R&gt;0)은 호출자가 거른다.
    /// </summary>
    public static async Task<IReadOnlyList<Remaining>> ListAsync(IDbConnection db, IDbTransaction? tx, string tenantId, bool receivable, CancellationToken ct)
    {
        var sql = $"""
            SELECT x.partner_id AS PartnerId, x.base_date AS BaseDate, x.legacy_amount AS LegacyAmount,
                   x.matched_amount AS MatchedAmount, x.remaining_amount AS RemainingAmount
              FROM ({(receivable ? ReceivableRemainingSql : PayableRemainingSql)}) x
             WHERE x.legacy_amount > 0 OR x.matched_amount <> 0
             ORDER BY x.partner_id
            """;
        var rows = await db.QueryAsync<Row>(new CommandDefinition(
            sql, new { TenantId = tenantId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        return rows.Select(r => r.ToRemaining()).ToList();
    }

    /// <summary>
    /// 거래처 하나의 이월잔액 행을 <c>FOR UPDATE</c> 로 잠그고 R 을 다시 계산한다. 행이 없으면 null.
    /// 같은 거래처의 동시 이월 매칭은 이 잠금에서 줄을 선다.
    /// </summary>
    /// <inheritdoc cref="GetForUpdateAsync(IDbConnection, IDbTransaction, LegacyMatchMode, string, string, bool, CancellationToken)"/>
    /// <remarks>
    /// 🔴 20260920작1 S1 — 모드를 안 넘기는 <b>기존 호출자 호환</b> 오버로드(RC 경로 고정 · 헌법 #1 기존 시그니처 유지).
    /// 새 코드는 모드를 선언하는 쪽을 쓴다. 동작은 3판과 한 글자도 다르지 않다.
    /// </remarks>
    public static Task<Remaining?> GetForUpdateAsync(IDbConnection db, IDbTransaction tx, string tenantId, string partnerId, bool receivable, CancellationToken ct)
        => GetForUpdateAsync(db, tx, LegacyMatchMode.ReadCommittedFresh, tenantId, partnerId, receivable, ct);

    public static async Task<Remaining?> GetForUpdateAsync(IDbConnection db, IDbTransaction tx, LegacyMatchMode mode,
        string tenantId, string partnerId, bool receivable, CancellationToken ct)
    {
        // 🔴 20260915작1 3판 R1b (병렬이슈44 · PM 후속 2) — R 재계산은 문장마다 최신 커밋을 보는 READ COMMITTED 트랜잭션에서만.
        //   REPEATABLE READ 면 같은 트랜잭션의 앞선 일반 읽기 스냅숏으로 옛 R 을 판정한다 → 호출자 실수를 조용히 넘기지 않고 막는다.
        // 🔴 20260920작1 S1 (설계 §5) — 격리수준을 느슨하게 푸는 게 아니라 **모드를 선언받는다.**
        //   RR 모드는 잠금 읽기(아래 꼬리절)와 한 몸이므로 「RR 로 열고 일반 읽기로 판정」은 코드로 표현할 수 없다.
        //   Serializable·ReadUncommitted·Unspecified 는 어느 모드로도 통과 못 한다.
        var expected = IsolationFor(mode);
        if (tx.IsolationLevel != expected)
            throw new NotSupportedException($"[LegacyBalanceMatching] 이월잔액 매칭 트랜잭션은 {mode} 모드에서 {expected} 로 열어야 한다(현재 {tx.IsolationLevel}).");

        // 🔴 20260920작1 S1b ㉮ (설계 2판 §11-4) — 잠금 읽기는 STATEMENT 서버에서도 **최신**이다(0단계 실측).
        //   L0·base_date 를 여기서 같이 받아 둔다 — 문장이 늘지도, 잠금이 늘지도 않는다.
        var locked = await db.QueryFirstOrDefaultAsync<LockedBalance>(new CommandDefinition(
            "SELECT balance_id AS BalanceId, base_date AS BaseDate, balance_amount AS BalanceAmount FROM partner_legacy_balances WHERE tenant_id = @TenantId AND partner_id = @PartnerId FOR UPDATE",
            new { TenantId = tenantId, PartnerId = partnerId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        if (locked is null) return null;

        // 🔴 20260920작1 S1b ㉯ (설계 2판 §11 · 처방 (나)) — RR 경로는 **파생표를 쓰지 않는다.**
        //   파생표는 트랜잭션 스냅숏으로 먼저 만들어져 바깥 꼬리 잠금절이 안쪽 기본 테이블 행에 닿지 않는다(S1 §3-2 실측:
        //   같은 순간 R 85,000 / 기대 59,000). 실테이블 직접 읽기는 조인이 있어도 최신이다(S1b §8-1 실측).
        //   RC 경로는 아래 그대로 — 문장마다 최신을 보므로 애초에 문제가 없다(설계 §11-5).
        if (mode == LegacyMatchMode.RepeatableReadLocking)
            return await RecomputeRemainingLockedAsync(db, tx, tenantId, partnerId, receivable, locked, ct).ConfigureAwait(false);

        // 공용 식 본문은 한 글자도 안 건드린다(11곳 소비자 계약 · 설계 §4-2).
        var sql = $"""
            SELECT x.partner_id AS PartnerId, x.base_date AS BaseDate, x.legacy_amount AS LegacyAmount,
                   x.matched_amount AS MatchedAmount, x.remaining_amount AS RemainingAmount
              FROM ({(receivable ? ReceivableRemainingSql : PayableRemainingSql)}) x
             WHERE x.partner_id = @PartnerId
            """;
        var row = await db.QueryFirstOrDefaultAsync<Row>(new CommandDefinition(
            sql, new { TenantId = tenantId, PartnerId = partnerId }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        return row?.ToRemaining();
    }

    /// <summary>
    /// 🔴 20260920작1 S1 — 모드가 고르는 잠금 꼬리절(설계 §4-4 · D1).
    /// RC 모드는 문장마다 최신 커밋을 보므로 빈 문자열, RR 모드는 <c>LOCK IN SHARE MODE</c> 로 최신 커밋을 읽는다.
    /// STATEMENT 서버에서도 공유 잠금 읽기는 거절되지 않는다(선행 F2 · S1 §3 실측).
    /// <para>
    /// 🔴 S1b ㉱ — <b>남긴다. 쓰는 곳은 전표 남은금액(D1) 뿐이다</b>(<c>CollectionService</c>). 파생표 꼬리에는 더 이상 안 붙인다 —
    /// 파생표 바깥의 잠금절은 옛 스냅숏을 돌려주기 때문이다(S1 §3-2). RR 의 R 재계산은 <see cref="RecomputeRemainingLockedAsync"/> 가 맡는다.
    /// </para>
    /// </summary>
    public static string LockTailFor(LegacyMatchMode mode)
        => mode == LegacyMatchMode.RepeatableReadLocking ? "\n LOCK IN SHARE MODE" : string.Empty;

    // ────────────────────────────────────────────────────────────────────────────────
    // 🔴 20260920작1 S1b ㉰ — RR 전용 재계산 SQL (설계 2판 §11-4)
    //   · 실테이블 직접 읽기 · 거래처 1건 필터 · 꼬리 LOCK IN SHARE MODE(이 모드에서만 쓰이므로 상수에 박는다).
    //   · 술어·별칭(lc·ec·lp·ep·rt/rti/pr)은 공용 식에서 **그대로** 옮겼다. 두 식이 갈라지지 않게 묶는 장치는
    //     G-RC10(값 동등성 7사례) · G-RC11(공용 식 SHA-256 앵커) — 갈래 S2 가 만든다(설계 §11-3).
    //   · ❌ 파생표·`UNION ALL`·스칼라 하위질의로 합치지 않는다(안 서는 것으로 실측된 모양 · S1 §3-2).
    //     같은 이유로 #16 의 `UNION ALL` 권장은 여기서 적용 예외다 — 한 연결·순차이며 `Task.WhenAll` 이 아니다.
    // ────────────────────────────────────────────────────────────────────────────────

    /// <summary>미수 M ①: 이월잔액에 직접 맞춘 수금(공용 식의 <c>lm</c> 하위질의).</summary>
    public const string ReceivableMatchedLegacyLockedSql = """
        SELECT COALESCE(SUM(lc.amount), 0)
          FROM collections lc
         WHERE lc.tenant_id = @TenantId AND lc.partner_id = @PartnerId AND lc.is_active = 1
           AND COALESCE(lc.source_type, '') <> 'migration'
           AND lc.ref_doc_type = 'legacy_balance' AND lc.ref_doc_id = lc.partner_id
         LOCK IN SHARE MODE
        """;

    /// <summary>미수 M ②: 이관 명세서에 붙은 수금(공용 식의 <c>em</c> 하위질의 · 거래처는 <c>sd.partner_id</c>).</summary>
    public const string ReceivableMatchedDocLockedSql = """
        SELECT COALESCE(SUM(ec.amount), 0)
          FROM collections ec
          JOIN sales_deliveries sd ON sd.delivery_id = ec.ref_doc_id AND sd.tenant_id = ec.tenant_id
         WHERE ec.tenant_id = @TenantId AND sd.partner_id = @PartnerId
           AND ec.is_active = 1 AND ec.ref_doc_type = 'sales_delivery'
           AND COALESCE(ec.source_type, '') <> 'migration'
           AND COALESCE(sd.source_type, '') = 'migration'
         LOCK IN SHARE MODE
        """;

    /// <summary>미지급 M ①: 이월잔액에 직접 맞춘 지급(공용 식의 <c>lm</c> 하위질의).</summary>
    public const string PayableMatchedLegacyLockedSql = """
        SELECT COALESCE(SUM(lp.amount), 0)
          FROM payments lp
         WHERE lp.tenant_id = @TenantId AND lp.partner_id = @PartnerId AND lp.is_active = 1
           AND COALESCE(lp.source_type, '') <> 'migration'
           AND lp.payment_type = 'legacy_balance' AND lp.ref_order_id = lp.partner_id
         LOCK IN SHARE MODE
        """;

    /// <summary>미지급 M ②: 이관 매입에 붙은 지급(공용 식의 <c>em</c> 하위질의 · 거래처는 <c>pr.partner_id</c>).</summary>
    public const string PayableMatchedDocLockedSql = """
        SELECT COALESCE(SUM(ep.amount), 0)
          FROM payments ep
          JOIN purchase_receipts pr ON pr.receipt_id = ep.ref_order_id AND pr.tenant_id = ep.tenant_id
         WHERE ep.tenant_id = @TenantId AND pr.partner_id = @PartnerId
           AND ep.is_active = 1 AND ep.payment_type = 'purchase'
           AND COALESCE(ep.source_type, '') <> 'migration'
           AND COALESCE(pr.source_type, '') = 'migration'
         LOCK IN SHARE MODE
        """;

    /// <summary>미지급 M ③: 이관 매입의 확정 반품(공용 식의 <c>rm</c> 하위질의 · 미확정은 제외).</summary>
    public const string PayableMatchedReturnLockedSql = """
        SELECT COALESCE(SUM(rti.supply_amount + rti.vat_amount), 0)
          FROM purchase_returns rt
          JOIN purchase_return_items rti ON rti.return_id = rt.return_id AND rti.tenant_id = rt.tenant_id
          JOIN purchase_receipts pr ON pr.receipt_id = rt.receipt_id AND pr.tenant_id = rt.tenant_id
         WHERE rt.tenant_id = @TenantId AND pr.partner_id = @PartnerId
           AND rt.is_deleted = 0 AND rt.status = 'confirmed'
           AND COALESCE(pr.source_type, '') = 'migration'
         LOCK IN SHARE MODE
        """;

    /// <summary>
    /// 🔴 20260920작1 S1b ㉯㉰ — RR 경로의 R 재계산. <b>이 모드에서만</b> 불린다(호출자는 <see cref="GetForUpdateAsync"/> 하나).
    /// <list type="bullet">
    /// <item>L0·<c>base_date</c> 는 이미 잠근 행에서 받은 값이다(㉮) — 다시 읽지 않는다.</item>
    /// <item>M 은 실테이블 직접 문장(미수 2 · 미지급 3)을 <b>한 연결에서 순차로</b> 읽는다(#16 — <c>Task.WhenAll</c> 아님 · 연결 새로 안 만든다).</item>
    /// <item>R = <c>GREATEST(L0,0) - ΣM</c> 을 C# <c>decimal</c> 로(#4). <b>클램프 없음</b> — 음수 R 은 그대로 돌려준다(공용 식과 같다).</item>
    /// </list>
    /// </summary>
    private static async Task<Remaining> RecomputeRemainingLockedAsync(IDbConnection db, IDbTransaction tx,
        string tenantId, string partnerId, bool receivable, LockedBalance locked, CancellationToken ct)
    {
        var args = new { TenantId = tenantId, PartnerId = partnerId };

        decimal matched;
        if (receivable)
        {
            var lm = await SumLockedAsync(db, tx, ReceivableMatchedLegacyLockedSql, args, ct).ConfigureAwait(false);
            var em = await SumLockedAsync(db, tx, ReceivableMatchedDocLockedSql, args, ct).ConfigureAwait(false);
            matched = lm + em;
        }
        else
        {
            var lm = await SumLockedAsync(db, tx, PayableMatchedLegacyLockedSql, args, ct).ConfigureAwait(false);
            var em = await SumLockedAsync(db, tx, PayableMatchedDocLockedSql, args, ct).ConfigureAwait(false);
            var rm = await SumLockedAsync(db, tx, PayableMatchedReturnLockedSql, args, ct).ConfigureAwait(false);
            matched = lm + em + rm;
        }

        // 공용 식과 같은 산술: 미수 = GREATEST(balance_amount, 0) · 미지급 = GREATEST(-balance_amount, 0).
        var legacy = Math.Max(receivable ? locked.BalanceAmount : -locked.BalanceAmount, 0m);
        return new Remaining(partnerId, locked.BaseDate, legacy, matched, legacy - matched);
    }

    private static async Task<decimal> SumLockedAsync(IDbConnection db, IDbTransaction tx, string sql, object args, CancellationToken ct)
        => await db.ExecuteScalarAsync<decimal>(new CommandDefinition(
            sql, args, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

    /// <summary>
    /// 🔴 20260920작1 S1 — 열려 있는 트랜잭션이 곧 모드다(<see cref="IsolationFor"/> 의 역). 매칭 트랜잭션이 아닌 격리수준이면 <see cref="NotSupportedException"/>.
    /// 재시도 껍질이 모드를 다시 판정해도 본문이 실제 트랜잭션과 어긋날 수 없게 한다.
    /// </summary>
    public static LegacyMatchMode ModeOf(IDbTransaction tx) => tx.IsolationLevel switch
    {
        MatchIsolation => LegacyMatchMode.ReadCommittedFresh,
        MatchIsolationFallback => LegacyMatchMode.RepeatableReadLocking,
        _ => throw new NotSupportedException($"[LegacyBalanceMatching] 이월잔액 매칭 트랜잭션은 {MatchIsolation} 또는 {MatchIsolationFallback} 로 열어야 한다(현재 {tx.IsolationLevel}).")
    };

    /// <summary>
    /// 병렬이슈44 PM 후속 2 — 이월잔액 매칭 트랜잭션 격리 수준. 거래처 단위 직렬화는 <c>partner_legacy_balances</c> 행 <c>FOR UPDATE</c> 가 맡고,
    /// R 재계산은 문장마다 최신 커밋을 본다(파생표 <c>LOCK IN SHARE MODE</c> 방식은 다른 거래처 동시 매칭에서 교착 실측 → 폐기 · G8-w).
    /// ⚠️ 바이너리 로그를 <c>binlog_format=STATEMENT</c> 로 쓰는 서버는 RC 쓰기가 거절된다(MariaDB 11.4 기본값 MIXED).
    /// </summary>
    public const IsolationLevel MatchIsolation = IsolationLevel.ReadCommitted;

    /// <summary>
    /// 🔴 20260920작1 갈래 S1 — <c>binlog_format=STATEMENT</c> 서버에서 쓰는 격리수준(설계 §5-3).
    /// RC 가 ERROR 1665 로 거절되는 서버에서도 등록이 막히지 않게 한다(#20). 정확성은 잠금 읽기가 지킨다.
    /// </summary>
    public const IsolationLevel MatchIsolationFallback = IsolationLevel.RepeatableRead;

    /// <summary>모드 ↔ 격리수준. 이 사상(寫像) 한 곳만 안다 — 호출자도 가드도 여기를 본다.</summary>
    public static IsolationLevel IsolationFor(LegacyMatchMode mode) => mode switch
    {
        LegacyMatchMode.ReadCommittedFresh => MatchIsolation,
        LegacyMatchMode.RepeatableReadLocking => MatchIsolationFallback,
        _ => throw new NotSupportedException($"[LegacyBalanceMatching] 모르는 매칭 모드({mode})다.")
    };

    /// <summary>
    /// 이월잔액 매칭 등록 검사 — 호출자 트랜잭션 안 · INSERT 앞에서 부른다.
    /// ① ref = 거래처 ② 금액 &gt; 0 ③ 이월잔액 행 있음 + 방향 맞음 ④ (S2) 기준일 검사 ⑤ 금액 ≤ R(잠금 뒤 재계산).
    /// 실패 = <see cref="InvalidOperationException"/>(고객 문구 · <c>GlobalExceptionMiddleware</c> 가 400).
    /// </summary>
    /// <inheritdoc cref="EnsureMatchAllowedAsync(IDbConnection, IDbTransaction, LegacyMatchMode, string, string, string?, decimal, DateTime, bool, ILogger?, CancellationToken)"/>
    /// <remarks>
    /// 🔴 20260920작1 S1 — 모드를 안 넘기는 <b>기존 호출자 호환</b> 오버로드(RC 경로 고정 · 헌법 #1). 동작은 3판 그대로.
    /// </remarks>
    public static Task EnsureMatchAllowedAsync(IDbConnection db, IDbTransaction tx, string tenantId, string partnerId,
        string? refId, decimal amount, DateTime date, bool receivable, ILogger? logger, CancellationToken ct)
        => EnsureMatchAllowedAsync(db, tx, LegacyMatchMode.ReadCommittedFresh, tenantId, partnerId, refId, amount, date, receivable, logger, ct);

    public static async Task EnsureMatchAllowedAsync(IDbConnection db, IDbTransaction tx, LegacyMatchMode mode, string tenantId, string partnerId,
        string? refId, decimal amount, DateTime date, bool receivable, ILogger? logger, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(partnerId) || !string.Equals(refId, partnerId, StringComparison.Ordinal))
            Reject(logger, tenantId, partnerId, MsgWrongPartner);

        if (amount <= 0m)
            Reject(logger, tenantId, partnerId, MsgAmountNotPositive);

        var row = await GetForUpdateAsync(db, tx, mode, tenantId, partnerId, receivable, ct).ConfigureAwait(false);
        if (row is null || row.LegacyAmount <= 0m)
            Reject(logger, tenantId, partnerId, receivable ? MsgNoReceivable : MsgNoPayable);

        if (IsBlockedByBaseDate(date, row.BaseDate))
            Reject(logger, tenantId, partnerId, string.Format(System.Globalization.CultureInfo.GetCultureInfo("ko-KR"), MsgBeforeBaseDate, row.BaseDate, receivable ? "수금" : "지급", receivable ? "받은" : "준"));

        if (amount > row.RemainingAmount)
            Reject(logger, tenantId, partnerId,
                string.Format(System.Globalization.CultureInfo.GetCultureInfo("ko-KR"), MsgOverRemaining, Math.Max(row.RemainingAmount, 0m)));
    }

    /// <summary>
    /// S2 조건 한 곳. 이월 기준일은 레거시 최종잔액이 끝난 날이다 — 그날까지의 돈은 이미 잔액에 들어 있으므로 기준일 당일도 막는다.
    /// </summary>
    internal static bool IsBlockedByBaseDate(DateTime date, DateTime baseDate)
        => RejectBeforeBaseDate && date.Date <= baseDate.Date;

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Reject(ILogger? logger, string tenantId, string partnerId, string message)
    {
        logger?.LogInformation("[LegacyBalanceMatching] 이월잔액 매칭 거절 tenant={TenantId} partner={PartnerId}: {Message}", tenantId, partnerId, message);
        throw new InvalidOperationException(message);
    }

    /// <summary>🔴 S1b ㉮ — <c>FOR UPDATE</c> 로 잠근 이월잔액 행에서 같이 받아 오는 값(잠금 읽기 = 최신).</summary>
    private sealed class LockedBalance
    {
        public string BalanceId { get; set; } = string.Empty;
        public DateTime BaseDate { get; set; }
        public decimal BalanceAmount { get; set; }
    }

    private sealed class Row
    {
        public string PartnerId { get; set; } = string.Empty;
        public DateTime BaseDate { get; set; }
        public decimal LegacyAmount { get; set; }
        public decimal MatchedAmount { get; set; }
        public decimal RemainingAmount { get; set; }

        public Remaining ToRemaining() => new(PartnerId, BaseDate, LegacyAmount, MatchedAmount, RemainingAmount);
    }
}
