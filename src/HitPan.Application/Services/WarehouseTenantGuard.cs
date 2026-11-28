using System.Data;
using Dapper;

namespace HitPan.Application.Services;

/// <summary>
/// 🔴 W-X-11 후7 · W-X-12 후9(작업지시서 20261129작 §2 · 설계 §2 · PM 결재 K2 · K3 · K5 (가)) —
/// <b>저장 입구는 그 회사 창고만, 그리고 쓰는 중인(활성) 창고만 받는다.</b>
/// </summary>
/// <remarks>
/// 🔴 <b>왜 생겼나</b> — <c>warehouses</c> 는 <c>warehouse_id</c> 단일 PK 이고 원장 · 재고 · 전표 줄 쪽에 외래 키가 없다.
///   재고 조정 · 이관 · 매입 · 판매 · 반품 · 배송 묶음 저장 길은 요청 본문의 창고 id 를 회사 확인 없이 저장했다 →
///   다른 회사 창고 id · 없는 창고 id 가 INSERT ONLY 원장(헌법 #3)에 영구히 남을 수 있었다(대표 §265 #2).
/// 🔴 <b>한 번 묻고 수만 맞춘다</b> — <c>SELECT COUNT(*) FROM warehouses WHERE tenant_id = @TenantId AND (…) AND warehouse_id IN @Ids</c>
///   한 문장(Dapper 목록 펼침 · 헌법 #16 <c>Task.WhenAll</c> 0). PK 라 셈 = 서로 다른 행 수 → 셈 ≠ 요청 id 수면 거절.
/// 🔴 <b>빈 · 공백만인 id 는 뺀다</b> — 매입 · 판매 · 반품의 「빈 칸 = 서버가 기본창고 채움」 길을 보존한다(설계 §2-2 ②).
/// 🔴 <b>앞뒤 공백이 붙은 id 는 거절한다(자르지 않음 · K3 (가))</b> — 서비스는 원래 값을 저장하므로 자른 값으로 검사하면
///   앞 공백 id 가 통과해 유령 행이 된다(F-8). 끝 공백도 거절해 C# 셈과 콜레이션(PAD SPACE) 셈의 어긋남을 없앤다.
/// 🔴 <b>대소문자 무시 중복 제거</b> — 표 콜레이션 <c>utf8mb4_unicode_ci</c>(출하 DDL <c>warehouses</c>)와 셈을 맞춘다.
/// 🔴 <b>문구는 하나</b> — 다른 회사 창고 · 없는 창고 · 쉬게 한 창고를 가르지 않는다(다른 회사 창고 id 유무 단서 0 · K5).
/// 🔴 <b>잠금 0</b> — <c>FOR UPDATE</c> · 공유 잠금 없음(<see cref="ItemTenantGuard"/> 와 같음 · 설계 §2-4).
/// 🔴 <b>트랜잭션이 열린 연결이면 반드시 <c>tx</c> 를 넘긴다</b>(후4 교훈 — 안 넘기면 MySqlConnector 「active transaction」 예외).
/// 🔴 테넌트 값은 부르는 쪽이 이미 가진 JWT 값만 넘긴다(헌법 #2 · 요청 본문 tenant 0) · 비면 조용히 통과하지 않고 거절한다.
/// 🔴 catch 0(헌법 #15) — 예외는 전역 미들웨어가 400 <c>{ error }</c> · 로그 <c>LogWarning</c> 로 받는다.
/// 🔴 반드시 <b>첫 쓰기 전</b>에 부른다 — 거절되면 아무 행도 바뀌지 않아야 한다.
/// 🔴 공개 메서드 둘은 이름으로 길을 가른다(설계 §2-6 · CTO 전결 K4 C1 · C2) — 부르는 자리에 <c>true</c>/<c>false</c> 글자가 없다.
///   선택 인자 0(반사 게이트 보호) · SQL 한 벌(안쪽 <see cref="EnsureCoreAsync"/>).
/// 🔴 칸 대조(헌법 #13) = 출하 DDL <c>installer/hitpan_db_clean.sql</c> <c>warehouses</c> —
///   <c>warehouse_id varchar(36) PK</c> · <c>tenant_id varchar(36) NOT NULL</c> · <c>is_active tinyint(1) NOT NULL</c>. 새 칸 · 표 · 인덱스 0.
/// </remarks>
public static class WarehouseTenantGuard
{
    /// <summary>회사 밖 · 없는 · 쉬게 한 창고가 섞였을 때 사용자에게 보이는 문구(쉬운 말 · 단서 0 · K5).</summary>
    public const string NotUsableWarehouseMessage =
        "사용할 수 없는 창고가 들어 있어 저장할 수 없습니다. 창고를 다시 골라 주세요.";

    /// <summary>
    /// 요청한 창고 id 가 모두 <paramref name="tenantId"/> 회사의 <b>활성</b>(<c>is_active = 1</c>) 창고인지 확인한다.
    /// 아니면 <see cref="InvalidOperationException"/>(<see cref="NotUsableWarehouseMessage"/>).
    /// </summary>
    /// <param name="db">부르는 쪽 연결(같은 연결 · 직렬).</param>
    /// <param name="tenantId">JWT 에서 온 회사 id — 비면 거절한다.</param>
    /// <param name="warehouseIds">저장할 창고 id 목록. 빈 · 공백만인 id 는 서버 기본창고 길이라 뺀다 · 앞뒤 공백 id 는 거절.</param>
    /// <param name="tx">부르는 쪽 트랜잭션이 열려 있으면 반드시 넘긴다(없으면 null).</param>
    /// <param name="ct">취소 토큰.</param>
    public static Task EnsureWarehousesUsableAsync(
        IDbConnection db,
        string tenantId,
        IEnumerable<string?> warehouseIds,
        IDbTransaction? tx,
        CancellationToken ct)
        => EnsureCoreAsync(db, tenantId, warehouseIds, requireActive: true, tx, ct);

    /// <summary>
    /// 🔴 요청한 창고 id 가 모두 <paramref name="tenantId"/> 회사의 창고인지만 확인한다(활성은 보지 않음).
    /// 🔴 <b>발주→매입 전환 안쪽 한 곳 전용</b>(CTO 전결 K4 (나) · 설계 §2-6 · §4-2 W5c) — 전환에는 창고 고를 칸 · 발주 고칠 길 ·
    ///   쉬게 한 창고 되살릴 길이 없어 활성까지 보면 매입 흐름이 끊긴다(헌법 #20). 다른 저장 입구는 쓰지 않는다.
    /// </summary>
    /// <param name="db">부르는 쪽 연결(같은 연결 · 직렬).</param>
    /// <param name="tenantId">JWT 에서 온 회사 id — 비면 거절한다.</param>
    /// <param name="warehouseIds">저장할 창고 id 목록. 빈 · 공백만인 id 는 뺀다 · 앞뒤 공백 id 는 거절.</param>
    /// <param name="tx">부르는 쪽 트랜잭션이 열려 있으면 반드시 넘긴다(없으면 null).</param>
    /// <param name="ct">취소 토큰.</param>
    public static Task EnsureWarehousesBelongToTenantAsync(
        IDbConnection db,
        string tenantId,
        IEnumerable<string?> warehouseIds,
        IDbTransaction? tx,
        CancellationToken ct)
        => EnsureCoreAsync(db, tenantId, warehouseIds, requireActive: false, tx, ct);

    /// <summary>두 공개 메서드가 함께 지나는 안쪽 함수 — 갈라지는 것은 <c>@RequireActive</c> 값 하나뿐(설계 §2-6).</summary>
    private static async Task EnsureCoreAsync(
        IDbConnection db,
        string tenantId,
        IEnumerable<string?> warehouseIds,
        bool requireActive,
        IDbTransaction? tx,
        CancellationToken ct)
    {
        // ① 인자 확인
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(warehouseIds);

        // ② null · 빈 · 공백만인 id 뺌(서버 기본창고 채움 길 보존)
        var nonBlank = warehouseIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList();

        // ③ 앞뒤 공백이 붙은 id 는 거절 — 자르지 않는다(저장 값 = 검사 값 · K3 (가))
        if (nonBlank.Any(id => id != id.Trim()))
        {
            throw new InvalidOperationException(NotUsableWarehouseMessage);
        }

        // ④ 대소문자 무시 중복 제거(콜레이션 utf8mb4_unicode_ci 와 같은 셈)
        var ids = nonBlank
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // ⑤ 남은 수 0 이면 DB 안 감
        if (ids.Count == 0)
        {
            return;
        }

        // ⑥ 회사 값이 비면 거절 — 조용히 통과하지 않는다(헌법 #2)
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new InvalidOperationException(NotUsableWarehouseMessage);
        }

        // ⑦ 한 번 묻는다 — SQL 한 벌 · 잠금 0
        var count = await db.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM warehouses WHERE tenant_id = @TenantId AND (@RequireActive = 0 OR is_active = 1) AND warehouse_id IN @Ids",
            new { TenantId = tenantId, RequireActive = requireActive ? 1 : 0, Ids = ids },
            transaction: tx,
            cancellationToken: ct));

        // ⑧ 셈이 다르면 거절
        if (count != ids.Count)
        {
            throw new InvalidOperationException(NotUsableWarehouseMessage);
        }
    }
}
