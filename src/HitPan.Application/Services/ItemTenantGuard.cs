using System.Data;
using Dapper;

namespace HitPan.Application.Services;

/// <summary>
/// 🔴 W-G1-51 후1 갈래 2(작업지시서 20261127작 §4 · PM 결재 K2 · K3 (가)) — <b>전표 상세행의 품목은 그 회사 품목만 저장한다.</b>
/// </summary>
/// <remarks>
/// 🔴 <b>왜 생겼나</b> — <c>items</c> 는 <c>item_id</c> 단일 PK 이고 <c>(tenant_id, item_id)</c> 유일 키가 없다.
///   전표 저장 길은 요청 본문의 품목 id 를 회사 확인 없이 상세행에 넣었다 → 다른 회사 품목 id · 없는 품목 id 가
///   이 회사 전표 줄로 저장될 수 있었다(대표 §249 #1 「전표 저장 전부」).
/// 🔴 <b>한 번 묻고 수만 맞춘다</b> — <c>SELECT COUNT(*) FROM items WHERE tenant_id = @TenantId AND item_id IN @Ids</c> 한 문장
///   (Dapper 목록 펼침 · 헌법 #16 <c>Task.WhenAll</c> 0). PK 라 셈 = 서로 다른 행 수 → 셈 ≠ 요청 id 수면 거절.
/// 🔴 <b>대소문자 무시 중복 제거</b> — 표 콜레이션 <c>utf8mb4_unicode_ci</c> 가 대소문자 · 끝 공백을 같게 보므로
///   같은 품목이 「A1」 · 「a1」 로 두 번 와도 DB 는 한 행으로 센다. 셈과 맞추려고 <see cref="StringComparer.OrdinalIgnoreCase"/> 로 줄인다.
/// 🔴 <b>빈 id 는 뺀다</b> — 거래명세 「id 비면 이름으로 찾기」 길(이미 <c>WHERE tenant_id = @TenantId</c>)을 보존한다(손 0).
/// 🔴 <b>문구는 하나</b> — 다른 회사 품목인지 없는 품목인지 가르지 않는다(다른 회사 품목 id 가 있는지 알려 주는 단서 0).
/// 🔴 <c>is_active</c> 는 보지 않는다 — 쉬게 한 품목이 든 옛 전표 수정이 갑자기 막히면 흐름이 끊긴다(헌법 #20).
/// 🔴 테넌트 값은 부르는 쪽이 이미 가진 JWT 값만 넘긴다(헌법 #2 · 요청 본문 tenant 0) · 비면 조용히 통과하지 않고 거절한다.
/// 🔴 catch 0(헌법 #15) — 예외는 전역 미들웨어가 400 <c>{ error }</c> · 로그 <c>LogWarning</c> 로 받는다.
/// 🔴 반드시 <b>첫 쓰기 전</b>에 부른다 — 거절되면 아무 행도 바뀌지 않아야 한다.
/// </remarks>
public static class ItemTenantGuard
{
    /// <summary>회사 밖 · 없는 품목이 섞였을 때 사용자에게 보이는 문구(쉬운 말 · 단서 0).</summary>
    public const string NotThisTenantItemMessage =
        "이 회사에 등록되지 않은 품목이 들어 있어 저장할 수 없습니다. 품목을 다시 골라 주세요.";

    /// <summary>
    /// 요청한 품목 id 가 모두 <paramref name="tenantId"/> 회사의 품목인지 확인한다. 아니면 <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <param name="db">부르는 쪽 연결(같은 연결 · 직렬).</param>
    /// <param name="tenantId">JWT 에서 온 회사 id — 비면 거절한다.</param>
    /// <param name="itemIds">실제로 저장할 품목 id 목록. 빈 · 공백 id 는 이름 찾기 길이라 뺀다.</param>
    /// <param name="tx">부르는 쪽 트랜잭션이 열려 있으면 넘긴다(없으면 null).</param>
    /// <param name="ct">취소 토큰.</param>
    public static async Task EnsureItemsBelongToTenantAsync(
        IDbConnection db,
        string tenantId,
        IEnumerable<string?> itemIds,
        IDbTransaction? tx,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(itemIds);

        // ① 빈 · 공백 id 뺌 ② 앞뒤 공백 자름 · 대소문자 무시 중복 제거(콜레이션과 맞춤)
        var ids = itemIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // ③ 남은 수 0 이면 DB 안 감
        if (ids.Count == 0)
        {
            return;
        }

        // ④ 회사 값이 비면 거절 — 조용히 통과하지 않는다(헌법 #2)
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new InvalidOperationException(NotThisTenantItemMessage);
        }

        // ⑤ 한 번 묻는다
        var count = await db.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM items WHERE tenant_id = @TenantId AND item_id IN @Ids",
            new { TenantId = tenantId, Ids = ids },
            transaction: tx,
            cancellationToken: ct));

        // ⑥ 셈이 다르면 거절
        if (count != ids.Count)
        {
            throw new InvalidOperationException(NotThisTenantItemMessage);
        }
    }
}
