namespace HitPan.Application.Services;

/// <summary>
/// 🔴 <b>찾아보기 경로 규칙 — 작22 (2026-09-09) B1 · 설계 별지 §2</b>
///
/// <para>
/// 9/4 사장님 오더 7 <i>"편리해야함. 폴더경로의 주소를 붙여넣기도 할수있게, 파일경로를 직접 찾을 수 있게."</i><br/>
/// 브라우저는 PC 경로를 못 읽으므로 서버가 폴더를 대신 열어 보여준다. 서버가 파일시스템을 여는 자리라
/// <b>어디를 열 수 있는지</b> 를 여기서 순수 함수로 정한다 — <c>ResolveMdbPaths</c> 의 규칙(<c>..</c> 차단 · 절대경로만)을 그대로 잇고
/// UNC 와 시스템 폴더를 더한다.
/// </para>
///
/// <para>
/// 🔴 <b>왜 순수 static 인가</b> — 게이트(W17)가 <c>..</c> · <c>\\srv\share</c> · 상대경로 · <c>C:\Windows</c> 를 값으로 넣어 본다.
/// 파일시스템을 읽지 않는다 — 실재 여부·권한은 컨트롤러가 본다(접근거부는 LogDebug 후 그 폴더만 제외 · 헌법 #15).
/// </para>
/// </summary>
public static class MdbFolderBrowsePolicy
{
    /// <summary>목록에서 빼는 시스템 폴더 이름(대소문자 무시). 드라이브 루트 바로 아래 기준.</summary>
    public static readonly IReadOnlyList<string> ExcludedFolderNames = new[]
    {
        "Windows",
        "Program Files",
        "Program Files (x86)",
        "$Recycle.Bin",
        "System Volume Information",
    };

    /// <summary>★ 표시 조건 — 이 세 파일이 다 있어야 레거시 히트판 자료 폴더다(대소문자 무시).</summary>
    public static readonly IReadOnlyList<string> RequiredMdbFiles = new[]
    {
        "PYOJUN.MDB",
        "PANDATA.mdb",
        "POTHER.mdb",
    };

    /// <summary>
    /// 열어도 되는 경로인가. 거부 사유는 <paramref name="error"/> 에 화면 문구(개발용어 0)로.
    /// </summary>
    public static bool TryValidatePath(string? path, out string? error)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "폴더 경로를 입력해 주세요.";
            return false;
        }

        var p = path.Trim();

        // UNC(\\서버\공유) — 다른 컴퓨터의 자료는 이 컴퓨터로 복사한 뒤 고른다(업로드는 Q2 로 접힘 · 별지 §2 안내 문구).
        if (p.StartsWith(@"\\", StringComparison.Ordinal) || p.StartsWith("//", StringComparison.Ordinal))
        {
            error = "네트워크 경로(\\\\서버)는 열 수 없습니다. 다른 컴퓨터의 자료는 그 컴퓨터의 HITWIN 폴더를 이 컴퓨터로 복사한 뒤 골라 주세요.";
            return false;
        }

        // Path Traversal — ResolveMdbPaths 와 같은 규칙.
        if (p.Contains("..", StringComparison.Ordinal))
        {
            error = "경로에 '..' 은 쓸 수 없습니다.";
            return false;
        }

        if (p.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            error = "경로에 쓸 수 없는 글자가 있습니다.";
            return false;
        }

        // 드라이브 문자 + ':' + 구분자로 시작하는 완전한 경로만 — 상대경로(HITWIN · .\HITWIN)와 드라이브 상대경로(C:HITWIN)는 현재 디렉터리에
        // 따라 다른 곳을 가리키므로 거부한다. Path.IsPathRooted 는 C:HITWIN 도 true 라 쓰지 않는다.
        if (p.Length < 3 || !char.IsAsciiLetter(p[0]) || p[1] != ':' || (p[2] != '\\' && p[2] != '/'))
        {
            error = @"C:\HITWIN 처럼 드라이브부터 시작하는 전체 경로를 입력해 주세요.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>폴더 이름 하나가 시스템 폴더인가.</summary>
    public static bool IsExcludedFolderName(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return false;
        var name = folderName.Trim();
        return ExcludedFolderNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 전체 경로가 시스템 폴더(또는 그 아래)인가 — 루트 바로 아래 첫 마디로 판정한다(<c>C:\Windows\System32</c> 도 제외).
    /// </summary>
    public static bool IsExcludedPath(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return false;
        var p = fullPath.Trim().Replace('/', '\\');

        // 루트(C:\) 를 걷어낸 뒤 첫 마디만 본다.
        var afterRoot = p.Length >= 3 && p[1] == ':' && p[2] == '\\' ? p[3..] : p;
        if (afterRoot.Length == 0) return false;
        var firstSep = afterRoot.IndexOf('\\');
        var first = firstSep < 0 ? afterRoot : afterRoot[..firstSep];
        return IsExcludedFolderName(first);
    }

    /// <summary>폴더 안 파일 이름 목록에 MDB 세 개가 다 있는가(대소문자 무시).</summary>
    public static bool HasAllMdbFiles(IEnumerable<string> fileNames)
    {
        var set = new HashSet<string>(fileNames.Select(f => Path.GetFileName(f)), StringComparer.OrdinalIgnoreCase);
        return RequiredMdbFiles.All(set.Contains);
    }
}
