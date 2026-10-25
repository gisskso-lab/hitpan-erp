using HitPan.Contracts.Forms;

namespace HitPan.Application.Services;

/// <summary>
/// 🔴 양식지 그림의 <b>보관 자리와 받아 주는 범위</b> — 읽는 쪽과 쓰는 쪽이 <b>같은 값</b>을 쓴다.
/// </summary>
/// <remarks>
/// 🔴 <b>왜 따로 뺐나</b> — 2026-10-24 까지 이 값들은 <c>PdfRenderService</c> 안에만 있었다.
///   그때는 <b>읽기만</b> 했으니 한 군데면 됐다. 사장님 오더로 <b>업로드 통로</b>가 생기면서
///   읽는 쪽과 쓰는 쪽이 둘이 됐다. 두 군데에 같은 목록을 적어 두면 <b>언젠가 어긋난다</b>
///   — 한쪽만 `.webp` 를 받고 다른 쪽은 안 읽는 날, 고객은 「올라갔는데 안 나온다」를 본다.
///   그 사고를 애초에 못 나게 값을 <b>한 곳</b>에 둔다.
///
/// 🔴 <b>폴더를 왜 ProgramData 에 두나</b> — 고객 PC 안이다(#22·#30 · 본사로 안 간다).
///   `Program Files` 는 쓰기 권한이 막혀 있고, 사용자 폴더는 계정마다 달라
///   다른 직원이 로그인하면 양식지가 사라진 것처럼 보인다.
///
/// 🔴 <b>테넌트마다 갈라 둔다</b> — 한 PC 에 사업장이 둘일 수 있다(multi_business_per_pc).
///   섞이면 남의 양식지가 내 전표에 깔린다.
/// </remarks>
public static class FormBackgroundPaths
{
    /// <summary>양식지 그림을 두는 <b>유일한 뿌리 폴더</b>.</summary>
    public static string Root =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "HitPan", "form-backgrounds");

    /// <summary>
    /// 이 테넌트의 양식지 폴더. 🔴 <c>tenantId</c> 는 <b>JWT 클레임에서 온 값만</b> 넣는다(#2).
    /// </summary>
    public static string TenantRoot(string tenantId) => Path.Combine(Root, tenantId);

    /// <summary>
    /// 받아 주는 그림 형식. 🔴 이 밖은 <b>보관소에 들어가지도, 작성기로 가지도 않는다</b>.
    /// <c>.pdf</c>·<c>.svg</c>·<c>.exe</c> 따위는 여기서 끝난다.
    /// 🔴 [2026-10-25 [4] 반려 R-1 봉합] 목록을 <b>여기서 적지 않는다</b> —
    ///   화면(`HitPan.Web`)이 이 프로젝트를 참조하지 않아 razor 에 두 번째 벌이 생겼고,
    ///   게이트는 서버 두 자리만 대조해 **Web 벌이 어긋나도 초록**이었다.
    ///   ⇒ 값은 양쪽이 함께 보는 <see cref="FormBackgroundLimits"/> 에 있다.
    /// </summary>
    public static readonly HashSet<string> AllowedExts =
        new(FormBackgroundLimits.AllowedExts, StringComparer.OrdinalIgnoreCase);

    /// <summary>그림 한 장 크기 상한. 🔴 값은 <see cref="FormBackgroundLimits"/> 한 자리에 있다.</summary>
    public const long MaxBytes = FormBackgroundLimits.MaxBytes;
}
