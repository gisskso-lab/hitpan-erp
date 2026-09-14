using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dapper;

namespace HitPan.Application.Services;

/// <summary>
/// 🔴 20260915작1 갈래 E — 거래처 「이전 프로그램 최종잔액 맞춤」(<c>partner_legacy_balances</c> · DB-123).
/// <para>
/// 근거: 작업지시서 <c>docs/운영기록/20260915작1_자료이관_머리없는줄_분류봉합_작업지시서.md</c> §10-6(R-A) · §13(R-A2 (나)) · §14(병렬이슈39·40) ·
/// 설계 §17 · 개발명세서 <c>docs/개발/erp/20260915작1_갈래E_잔액맞춤_개발명세서.md</c>.
/// </para>
/// <list type="bullet">
/// <item>잔액 = F3(<see cref="LegacyMdbMapping.PartnerLegacyBalances"/>) — 레거시 거래처원장이 저장한 최종 잔액. 거래처당 <b>순잔액 한 칸</b>.</item>
/// <item>부호(PM 결정 · 병렬이슈40): <b>+ = 미수 칸 · − = 미지급 칸</b>. 판매·매입 양쪽 거래처도 순잔액 부호로 한 칸에만 들어간다.</item>
/// <item>옛 코드 여러 개 → 히트판 거래처 하나(매핑 없는 코드는 폴백 거래처 <c>LEGACY_UNKNOWN_PTNR</c>)면 <b>합산 1행</b> — UNIQUE (tenant_id, partner_id).</item>
/// <item>재실행 = UPSERT(같은 거래처 행을 새 값으로 바꾼다) → 이중 적재 0.</item>
/// </list>
/// <para>
/// 🔴 미수·미지급 식과의 약속(설계 §17 · 개발명세서 §3): F3 는 이관한 명세서·수금·지급 이력을 <b>이미 품은</b> 최종값이다.
/// 그래서 이 표에 행이 있는 회사는 미수·미지급 식에서 <c>source_type='migration'</c> 행을 빼고 이 표를 <b>한 번</b> 더한다
/// (<see cref="HasLegacyBalanceSql"/>). 사람이 입력한 명세서·수금·지급은 그대로 쌓인다.
/// </para>
/// </summary>
public static class MdbLegacyPartnerBalance
{
    /// <summary>폴백 거래처 코드 — <c>MdbMigrationService.EnsureLegacyFallbackPartnerAsync</c> 와 같은 값(varchar(20) 안 19자).</summary>
    public const string FallbackPartnerCode = "LEGACY_UNKNOWN_PTNR";

    /// <summary>멱등 키 머리 — <c>source_id = mig-legacybal-{partner_id}</c> (최대 50자 · 칼럼 varchar(80)).</summary>
    public const string SourceIdPrefix = "mig-legacybal-";

    /// <summary>
    /// 미수·미지급 식이 이관 행을 뺄지 가르는 조건 한 줄 — 이 회사에 이월잔액 행이 하나라도 있으면 참.
    /// 파라미터 이름은 <c>@TenantId</c> 고정(모든 호출 식이 같은 이름을 쓴다).
    /// </summary>
    public const string HasLegacyBalanceSql =
        "EXISTS (SELECT 1 FROM partner_legacy_balances plb_x WHERE plb_x.tenant_id = @TenantId)";

    /// <summary>
    /// 수금·지급 이관 뒤 호출. DOCF5 가 없거나 0행이면 아무것도 안 하고 0(R5 — 대사표 「거래처원장 표 없음」은 갈래 C 몫).
    /// 반환 = 이번에 적은(또는 새 값으로 바꾼) 거래처 행 수.
    /// <para>⚠️ 트랜잭션을 받지 않는다(계약 시그니처). 호출자는 이 연결에 열린 트랜잭션이 없을 때 부른다.
    /// 중간에 실패해도 UPSERT 라 다시 부르면 같은 결과가 된다.</para>
    /// </summary>
    public static async Task<int> ApplyAsync(
        IDbConnection db,
        string tenantId,
        DataTable? docf5,
        IReadOnlyDictionary<int, string> partnerMap,
        DateTime baseDate,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(partnerMap);
        if (string.IsNullOrWhiteSpace(tenantId)) throw new ArgumentException("tenantId 가 비었다.", nameof(tenantId));
        if (docf5 is null || docf5.Rows.Count == 0) return 0;

        var balances = LegacyMdbMapping.PartnerLegacyBalances(LegacyMdbMapping.ReadPartnerLedgerRows(docf5));
        if (balances.Count == 0) return 0;

        if (db.State != ConnectionState.Open)
        {
            if (db is DbConnection dc) await dc.OpenAsync(ct).ConfigureAwait(false);
            else db.Open();
        }

        string? fallbackPartnerId = null;
        var merged = new Dictionary<string, Acc>(StringComparer.Ordinal);
        foreach (var (code, amount) in balances.OrderBy(kv => kv.Key))
        {
            ct.ThrowIfCancellationRequested();
            string partnerId;
            if (partnerMap.TryGetValue(code, out var mapped) && !string.IsNullOrEmpty(mapped))
            {
                partnerId = mapped;
            }
            else
            {
                fallbackPartnerId ??= await EnsureFallbackPartnerAsync(db, tenantId, ct).ConfigureAwait(false);
                partnerId = fallbackPartnerId;
            }

            if (!merged.TryGetValue(partnerId, out var acc))
            {
                acc = new Acc();
                merged[partnerId] = acc;
            }
            acc.Amount += amount;
            acc.Codes.Add(code);
        }

        var date = baseDate.Date;
        var rows = merged.Select(kv => new
        {
            BalanceId = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            PartnerId = kv.Key,
            LegacyBuyCode = (long)kv.Value.Codes.Min(),
            BaseDate = date,
            BalanceAmount = kv.Value.Amount,
            SourceId = SourceIdPrefix + kv.Key,
            Hash = Sha256Hex(string.Join("|",
                tenantId, kv.Key, date.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                kv.Value.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                string.Join(",", kv.Value.Codes.OrderBy(c => c)))),
        }).ToList();

        // 헌법 #13: partner_legacy_balances 칼럼은 격리 DB(출하 DDL + DB-123) DESCRIBE 로 확인 — 개발명세서 §5.
        const string upsertSql = """
            INSERT INTO partner_legacy_balances
              (balance_id, tenant_id, partner_id, legacy_buy_code, base_date, balance_amount,
               source_type, source_id, migrated_source_hash)
            VALUES
              (@BalanceId, @TenantId, @PartnerId, @LegacyBuyCode, @BaseDate, @BalanceAmount,
               'migration', @SourceId, @Hash)
            ON DUPLICATE KEY UPDATE
              legacy_buy_code      = VALUES(legacy_buy_code),
              base_date            = VALUES(base_date),
              balance_amount       = VALUES(balance_amount),
              source_type          = VALUES(source_type),
              source_id            = VALUES(source_id),
              migrated_source_hash = VALUES(migrated_source_hash)
            """;

        var written = 0;
        const int chunk = 500;
        for (var i = 0; i < rows.Count; i += chunk)
        {
            ct.ThrowIfCancellationRequested();
            var part = rows.Skip(i).Take(chunk).ToList();
            await db.ExecuteAsync(new CommandDefinition(upsertSql, part, cancellationToken: ct)).ConfigureAwait(false);
            written += part.Count;
        }
        return written;
    }

    /// <summary>
    /// 폴백 거래처 확보 — 수금·명세서 이관이 매핑 실패 코드에 쓰는 거래처와 <b>같은 행</b>(partner_code 로 찾는다).
    /// 없으면 같은 모양으로 INSERT IGNORE 후 재조회(멱등 · <c>MdbMigrationService.cs:2071</c> 관용구).
    /// </summary>
    private static async Task<string> EnsureFallbackPartnerAsync(IDbConnection db, string tenantId, CancellationToken ct)
    {
        const string findSql = "SELECT partner_id FROM partners WHERE tenant_id = @TenantId AND partner_code = @Code LIMIT 1";
        var existing = await db.ExecuteScalarAsync<string?>(new CommandDefinition(
            findSql, new { TenantId = tenantId, Code = FallbackPartnerCode }, cancellationToken: ct)).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(existing)) return existing;

        var now = DateTime.Now;
        await db.ExecuteAsync(new CommandDefinition("""
            INSERT IGNORE INTO partners
              (partner_id, tenant_id, partner_code, partner_name, partner_type,
               is_active, is_deleted, created_at, updated_at, memo)
            VALUES
              (@Id, @TenantId, @Code, '레거시 미식별 거래처', 'customer',
               1, 0, @Now, @Now, '진범 #2 봉합 — K2_BUYC 매핑 실패 거래의 fallback 거래처')
            """,
            new { Id = Guid.NewGuid().ToString(), TenantId = tenantId, Code = FallbackPartnerCode, Now = now },
            cancellationToken: ct)).ConfigureAwait(false);

        var resolved = await db.ExecuteScalarAsync<string?>(new CommandDefinition(
            findSql, new { TenantId = tenantId, Code = FallbackPartnerCode }, cancellationToken: ct)).ConfigureAwait(false);
        return !string.IsNullOrEmpty(resolved)
            ? resolved
            : throw new InvalidOperationException("폴백 거래처(LEGACY_UNKNOWN_PTNR)를 만들지 못했다 — 이월잔액을 적을 거래처가 없다.");
    }

    private static string Sha256Hex(string s)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    private sealed class Acc
    {
        public decimal Amount;
        public List<int> Codes { get; } = new();
    }
}
