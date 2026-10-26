using HitPan.Contracts.Forms;

namespace HitPan.Application.Services;

/// <summary>
/// 🔴 회사 그림(로고 · 직인 · 출력 머리)의 <b>보관 자리</b> — 읽는 쪽과 쓰는 쪽이 <b>같은 값</b>을 쓴다.
/// </summary>
/// <remarks>
/// 🔴 <b>왜 양식지 폴더와 갈라 두나</b> — 쓰임이 다르고 지워지는 때가 다르다.
///   양식지 그림은 <b>양식 하나</b>에 묶여 그 양식을 지우면 같이 쓸모가 없어진다.
///   로고·직인은 <b>회사 한 벌</b>이고 모든 양식이 함께 본다.
///   한 폴더에 섞으면 「양식 정리」가 로고를 지우는 날이 온다.
///
/// 🔴 <b>폴더를 ProgramData 에 두는 까닭</b>은 양식지와 같다 — 고객 PC 안이다
///   (#22·#30 · 본사로 아무것도 안 보낸다). <c>Program Files</c> 는 쓰기가 막혀 있고,
///   사용자 폴더는 계정마다 달라 다른 직원이 로그인하면 사라진 것처럼 보인다.
///
/// 🔴 <b>테넌트마다 갈라 둔다</b> — 한 PC 에 사업장이 둘일 수 있다(multi_business_per_pc).
///   섞이면 남의 로고가 내 전표에 찍힌다.
/// </remarks>
public static class CompanyImagePaths
{
    /// <summary>회사 그림을 두는 <b>유일한 뿌리 폴더</b>.</summary>
    public static string Root =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "HitPan", "company-images");

    /// <summary>
    /// 이 테넌트의 회사 그림 폴더. 🔴 <c>tenantId</c> 는 <b>JWT 클레임에서 온 값만</b> 넣는다(#2).
    /// </summary>
    public static string TenantRoot(string tenantId) => Path.Combine(Root, tenantId);

    /// <summary>받아 주는 그림 형식 — 값은 <see cref="CompanyImageLimits"/> 한 자리에 있다.</summary>
    public static readonly HashSet<string> AllowedExts =
        new(CompanyImageLimits.AllowedExts, StringComparer.OrdinalIgnoreCase);

    /// <summary>그림 한 장 크기 상한 — 값은 <see cref="CompanyImageLimits"/> 한 자리에 있다.</summary>
    public const long MaxBytes = CompanyImageLimits.MaxBytes;

    /// <summary>
    /// 그림 종류 — 🔴 <b>파일 이름 앞머리</b>로 쓴다. 고객 파일 이름은 쓰지 않는다.
    /// </summary>
    public static readonly string[] Kinds = { "logo", "seal", "header" };

    /// <summary>이 종류를 받아 주나 — 🔴 바깥에서 온 글자를 폴더·파일 이름에 그냥 쓰지 않는다.</summary>
    public static bool IsKnownKind(string? kind) =>
        kind is not null && Array.Exists(Kinds, k => string.Equals(k, kind, StringComparison.Ordinal));
}
