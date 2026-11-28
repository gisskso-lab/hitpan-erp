namespace HitPan.Web.Services;

/// <summary>
/// 🔴 W-G1-52 갈래 C 고침 1 — 대표 비밀번호 되찾기 <b>안내 글 · 주소 읽기 · 알림 1줄</b>(X1 · X2 · X6).
/// </summary>
/// <remarks>
/// <para>Blazor 의존 0 순수 C# — 시험 프로젝트가 이 파일을 링크해 <b>불러서</b> 잰다(ApiErrorText.cs 선례).</para>
/// <para>🔴 X1 — 안내 속 이름은 설치기(<c>installer/HitPan-Universal.iss</c>)와 실행 글 파일 이름에서 온다.
/// 시험 <c>ParentResetGateTests.X1_Guide_Names_Match_Installer_And_Command_File</c> 가 글자 대조 — 설치기 쪽 이름이 바뀌면 빨강.</para>
/// <para>고객 화면 글이다 — 개발 용어 0 · 「처음 보는 대표가 혼자 쓸 수 있냐」.</para>
/// </remarks>
public static class ParentResetGuideText
{
    /// <summary>시작 메뉴 묶음 이름 = <c>.iss</c> <c>DefaultGroupName</c>.</summary>
    public const string StartMenuGroup = "HitPan ERP";

    /// <summary>시작 메뉴 항목 이름 = <c>.iss</c> <c>[Icons]</c> <c>{group}\…</c>(새로 깐 PC 에만 있음).</summary>
    public const string ShortcutName = "대표 비밀번호 다시 정하기";

    /// <summary>바탕화면 아이콘 이름 = <c>.iss</c> <c>[Icons]</c> <c>{commondesktop}\…</c>(처음 깔 때 생김 · 업데이트로 받은 PC 에도 남아 있음).</summary>
    public const string DesktopIconName = "HitPan ERP";

    /// <summary>실행 글이 든 폴더 이름(<c>{app}\api</c>).</summary>
    public const string ApiFolderName = "api";

    /// <summary>실행 글 파일 이름(확장자 <c>.cmd</c> 는 윈도우 기본 설정에서 안 보임) = <c>src/HitPan.API/대표비밀번호다시정하기.cmd</c>.</summary>
    public const string CommandFileStem = "대표비밀번호다시정하기";

    /// <summary>보통 설치 폴더 = <c>.iss</c> <c>DefaultDirName={autopf}\HitPan</c> 의 64비트 윈도우 값.</summary>
    public const string UsualInstallFolder = @"C:\Program Files\HitPan";

    /// <summary>
    /// 안내 걸음(번호 없이) — 🔴 X1 ② 「왼쪽 아래」 같은 자리 말 없음(윈도우 11 은 작업 표시줄 가운데) · ① 묶음 이름 = 실제 값.
    /// </summary>
    public static readonly string[] Steps =
    {
        $"작업 표시줄의 [시작](윈도우 모양 단추)을 누르고 「{StartMenuGroup}」 → 「{ShortcutName}」 를 누릅니다. ([시작] 을 누른 뒤 「대표 비밀번호」 라고 쳐도 나옵니다.)",
        "윈도우가 「허용하시겠어요?」 하고 물으면 [예] 를 누릅니다.",
        "열린 화면에서 대표 아이디를 확인하고 새 비밀번호를 두 번 넣습니다.",
    };

    /// <summary>
    /// 🔴 X1 ③ — 시작 메뉴에 항목이 없을 때(업데이트로 받은 PC)의 대체 길 = 설계 R16 「히트판 폴더 안 실행 글을 두 번 누름」 + 폴더 찾는 쉬운 길.
    /// </summary>
    public static readonly string FallbackLine =
        $"시작 메뉴에 「{ShortcutName}」 가 없으면: 바탕화면 「{DesktopIconName}」 아이콘을 마우스 오른쪽 단추로 누르고 「파일 위치 열기」 → 「{ApiFolderName}」 폴더 → 「{CommandFileStem}」 를 두 번 누릅니다. (보통 {UsualInstallFolder}\\{ApiFolderName} 폴더입니다.)";

    /// <summary>번호 붙인 안내 걸음(화면이 그대로 보임).</summary>
    public static IEnumerable<string> NumberedSteps()
    {
        for (var i = 0; i < Steps.Length; i++) yield return $"{i + 1}. {Steps[i]}";
    }

    /// <summary>🔴 X2(설계 R16 ⑤) — 대표 다음 로그인 때 1줄(시각 = 이 PC 시각으로 바꾼 값).</summary>
    public static string NoticeLine(DateTime localTime) =>
        $"대표 비밀번호가 {localTime.Month}월 {localTime.Day}일 {localTime.Hour}시 {localTime.Minute:00}분에 메인PC 에서 다시 정해졌습니다.";

    /// <summary>주소 <c>#t=</c> 뒤의 표만 꺼낸다(다른 칸 무시 · 없으면 null).</summary>
    public static string? ReadToken(string uri)
    {
        var hash = uri.IndexOf('#');
        if (hash < 0) return null;
        foreach (var part in uri[(hash + 1)..].Split('&'))
        {
            if (part.StartsWith("t=", StringComparison.Ordinal)) return part[2..];
        }
        return null;
    }

    /// <summary>
    /// 🔴 X6([3-V] P3-2) — 표를 읽은 뒤 주소 칸 · 방문 기록에서 지울 때 넣을 주소(경로 + 물음표 뒤까지 · <c>#</c> 뒤 0).
    /// </summary>
    public static string PathWithoutFragment(string uri)
    {
        var hash = uri.IndexOf('#');
        var noFragment = hash < 0 ? uri : uri[..hash];
        return Uri.TryCreate(noFragment, UriKind.Absolute, out var u) ? u.PathAndQuery : noFragment;
    }
}
