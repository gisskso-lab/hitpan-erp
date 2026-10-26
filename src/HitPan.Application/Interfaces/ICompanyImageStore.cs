namespace HitPan.Application.Interfaces;

/// <summary>
/// 🔴 회사 그림 <b>보관소</b> — 로고 · 직인 · 출력 머리 그림을 받아 둔다.
/// </summary>
/// <remarks>
/// 사장님 결재 2026-10-26 (결-H): <i>"응 이 차수에서 만들어"</i>
///
/// 🔴 <b>왜 필요한가</b> — 10/25 실측에서 드러난 것:
///   올리는 <b>칸은 두 화면에 실재</b>했고(양식정보설정 · 회사정보) DB 칸도 있었는데
///   (<c>local_company.logo_url</c>·<c>seal_url</c>·<c>header_url</c>)
///   <b>파일을 받는 통로가 0건</b>이었다. 단추를 누르면 파일 이름만 화면에 적고
///   <i>"(서버 업로드는 Phase 2)"</i> 를 띄웠고, 회사정보 쪽은 <b>「등록되었습니다」라고
///   말하면서 <c>null</c> 을 저장</b>했다 — 고객 화면에 거짓이 있었다.
///
/// 🔴 그리고 <b>워터마크의 재료가 로고</b>다(사장님 오더 10/25 ④).
///   보관소가 없으면 워터마크는 켜도 아무 일이 안 일어나는 칸이 된다.
///
/// 🔴 <b>양식지 보관소(<see cref="IFormBackgroundStore"/>)와 왜 따로 짓나</b> — 의도된 별건이다.
///   그 코드는 2026-10-24~25 에 반려 7건을 봉합한 자리이고 게이트가 그 코드를
///   <b>글자로</b> 재고 있다. 일반화하려고 손대면 봉합이 아니라 회귀가 된다(#1).
///   ⇒ 울타리 <b>모양은 그대로 옮기고</b> 파일은 새로 둔다. 작업지시서 §B-1 이 이 중복을 승인했다.
/// </remarks>
public interface ICompanyImageStore
{
    /// <summary>
    /// 그림을 보관소에 넣고 <b>작성기가 읽을 수 있는 경로</b>를 돌려준다.
    /// </summary>
    /// <param name="tenantId">🔴 JWT 클레임에서 온 값만 넣는다(#2).</param>
    /// <param name="kind">그림 종류 — <c>logo</c>·<c>seal</c>·<c>header</c>. 🔴 그 밖은 거절한다.</param>
    /// <param name="originalFileName">고객 파일 이름 — 🔴 <b>확장자만</b> 본다.</param>
    /// <param name="content">그림 바이트.</param>
    /// <returns>
    /// 성공하면 <c>(경로, null)</c>, 거절하면 <c>(null, 고객에게 보일 까닭)</c>.
    /// 🔴 <b>예외로 던지지 않는다</b> — 설정하던 손이 오류 화면으로 튕기면 안 된다.
    /// </returns>
    Task<(string? StoredPath, string? Rejection)> SaveAsync(
        string tenantId, string kind, string originalFileName, Stream content, CancellationToken ct = default);

    /// <summary>
    /// 보관소에서 그림 한 장을 지운다. 🔴 <b>이 테넌트 폴더 안</b>이 아니면 지우지 않는다.
    /// </summary>
    /// <returns>지웠으면 <c>null</c>, 못 지웠으면 까닭.</returns>
    string? Delete(string tenantId, string storedPath);
}
