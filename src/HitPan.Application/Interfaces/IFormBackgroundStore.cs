namespace HitPan.Application.Interfaces;

/// <summary>
/// 🔴 양식지 그림 <b>보관소</b> — 고객이 쓰는 양식용지를 그림 파일로 받아 둔다.
/// </summary>
/// <remarks>
/// 사장님 2026-10-24: <i>"그밖에 이미지파일로 양식지 추가시, PDF편집기로 즉시 반영될 수 있도록 설계, 구현해"</i>
///
/// 🔴 <b>왜 필요한가</b> — 10/24 에 작성기가 양식지 그림을 종이에 깔 수 있게 됐지만,
///   <b>그림을 넣을 입구가 없었다.</b> 편집기에는 주소를 손으로 적는 글자 칸뿐이었고
///   바깥 주소는 거절되니(인쇄가 남의 서버에 묶이면 안 된다) 사실상 쓸 길이 0 이었다.
///   「고쳤다 ≠ 갔다」와 같은 모양이고, 이번에 끊긴 자리는 <b>입구</b>였다.
/// </remarks>
public interface IFormBackgroundStore
{
    /// <summary>
    /// 그림을 보관소에 넣고 <b>작성기가 읽을 수 있는 경로</b>를 돌려준다.
    /// </summary>
    /// <param name="tenantId">🔴 JWT 클레임에서 온 값만 넣는다(#2).</param>
    /// <param name="originalFileName">고객 파일 이름 — 🔴 <b>확장자만</b> 본다. 이름은 쓰지 않는다.</param>
    /// <param name="content">그림 바이트.</param>
    /// <returns>
    /// 성공하면 <c>(경로, null)</c>, 거절하면 <c>(null, 고객에게 보일 까닭)</c>.
    /// 🔴 <b>예외로 던지지 않는다</b> — 양식 맞추던 손이 오류 화면으로 튕기면 안 된다.
    /// </returns>
    Task<(string? StoredPath, string? Rejection)> SaveAsync(
        string tenantId, string originalFileName, Stream content, CancellationToken ct = default);

    /// <summary>
    /// 보관소에서 그림 한 장을 지운다. 🔴 <b>이 테넌트 폴더 안</b>이 아니면 지우지 않는다.
    /// </summary>
    /// <returns>지웠으면 <c>null</c>, 못 지웠으면 까닭.</returns>
    string? Delete(string tenantId, string storedPath);
}
