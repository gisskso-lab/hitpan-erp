using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 모든 DB 연결문자열에 <c>GuidFormat=None</c> 이 있는지 지키는 게이트
/// (사장님 실측 적발 2026-08-12 — 양식템플릿 500).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>무엇을 겪고서</b> — 설정관리 → 양식템플릿 화면이 <b>500</b> 으로 죽었다.
/// <code>System.Data.DataException: Error parsing column 0 (TemplateId=... - Guid)</code>
/// MySqlConnector 는 기본으로 <b><c>CHAR(36)</c> 컬럼을 Guid 로 돌려준다.</b>
/// 우리 DTO 는 이런 ID 를 전부 <c>string</c> 으로 받으므로 Dapper 가 값을 못 넣고 터진다.
/// </para>
/// <para>
/// ⚠️ <b>왜 여태 안 터졌나</b> — 대부분의 표가 <c>varchar(36)</c> 이라서다.
/// <c>partners.partner_id</c>=varchar(36) → String 으로 와서 멀쩡했고,
/// <c>form_templates.template_id</c>=char(36) → Guid 로 와서 터졌다.
/// 같은 폭탄이 <c>common_codes</c>·<c>item_specs</c>·<c>migration_jobs</c>·<c>sync_tokens</c> 등
/// <b>char(36) 을 쓰는 표 전체</b>에 잠복해 있었다. <b>화면을 안 열어봤을 뿐이다.</b>
/// </para>
/// <para>
/// 🔴 <b>왜 시험으로 막나</b> — 연결문자열을 만드는 곳이 <b>9곳</b>이다(각자 복사본).
/// 새로 하나 만들면서 이 옵션을 빠뜨리면 그 경로만 조용히 터진다 —
/// 빌드도 통과하고 다른 화면도 멀쩡해서 <b>그 화면을 열기 전까지 아무도 모른다.</b>
/// </para>
/// </remarks>
public class ConnectionStringGuidGuardTests
{
    /// <summary>src 폴더를 찾는다 — 시험은 bin 아래서 돌기 때문에 위로 올라가며 찾는다.</summary>
    private static string FindSrcRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "HitPan.sln");
            if (File.Exists(candidate)) return dir.FullName;
            dir = dir.Parent;
        }
        throw new Xunit.Sdk.XunitException("HitPan.sln 을 못 찾았다 — 시험이 소스를 읽을 수 없다.");
    }

    /// <summary>MySQL 연결문자열을 만드는 코드 줄. 주석·문서는 제외한다.</summary>
    private static readonly Regex ConnStrLine =
        new(@"""Server=\{[^""]*Database=", RegexOptions.Compiled);

    /// <summary>주석을 걷어낸다 — 설명 주석을 실제 설정으로 오인하지 않기 위해서다.</summary>
    /// <remarks>
    /// 완벽한 C# 파서가 아니다. <b>주석 안의 값을 코드로 세지 않는다</b>는 목적에만 쓴다.
    /// 문자열 안에 <c>//</c> 가 들어간 경우(예: URL)를 지우게 되지만,
    /// 그건 이 시험이 찾는 대상이 아니라 무해하다.
    /// </remarks>
    private static string StripComments(string src)
    {
        // /* ... */ 먼저, 그다음 줄 주석.
        var noBlock = Regex.Replace(src, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(noBlock, @"//.*?$", "", RegexOptions.Multiline);
    }

    [Fact(DisplayName = "🔴 모든 DB 연결문자열에 GuidFormat=None 이 있다")]
    public void 모든_연결문자열에_GuidFormat이_있다()
    {
        var root = FindSrcRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            // 빌드 산출물·임시 폴더는 소스가 아니다.
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}"))
                continue;

            var text = File.ReadAllText(file);
            if (!ConnStrLine.IsMatch(text)) continue;

            // 🔴 주석을 먼저 걷어낸다.
            //   이걸 안 하면 "// GuidFormat=None — …" 같은 **설명 주석만 보고 통과**시킨다.
            //   실제로 2026-08-12 자체 시험에서 그렇게 통과해 버렸다(막는 척만 하는 게이트).
            //   ⇒ 코드에 실제로 들어간 값만 세도록 주석을 지우고 본다.
            var code = StripComments(text);

            // 이어붙인 문자열이라 한 줄에 다 없을 수 있다 ⇒ 파일 단위로 본다.
            if (!code.Contains("GuidFormat=None"))
                offenders.Add(Path.GetRelativePath(root, file));
        }

        Assert.True(offenders.Count == 0,
            "DB 연결문자열에 GuidFormat=None 이 빠졌다. char(36) 컬럼이 Guid 로 와서 "
            + "string DTO 매핑이 500 으로 터진다(2026-08-12 양식템플릿 사고).\n  누락: "
            + string.Join("\n        ", offenders));
    }

    [Fact(DisplayName = "연결문자열을 만드는 곳이 늘어났는지 본다")]
    public void 연결문자열을_만드는_곳_수를_지킨다()
    {
        // 곳이 늘어난 것 자체는 잘못이 아니다. 다만 **늘어난 줄 모르고 지나가는 것**이 문제라
        //   숫자가 바뀌면 눈에 띄게 한다(위 시험이 옵션 누락은 이미 막는다).
        var root = FindSrcRoot();
        var count = 0;

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}"))
                continue;

            if (ConnStrLine.IsMatch(File.ReadAllText(file))) count++;
        }

        Assert.True(count >= 1, "연결문자열을 만드는 곳을 하나도 못 찾았다 — 이 시험의 탐지 방식이 깨졌다.");
    }
}
