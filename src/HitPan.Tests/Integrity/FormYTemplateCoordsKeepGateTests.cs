using System.Text.RegularExpressions;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 W-G1-51 갈래 B — Y 템플릿을 편집창에서 저장해도 <b>칸 좌표가 안 지워진다</b>(설계 §7-3 · YN5).
/// </summary>
/// <remarks>
/// <para>
/// 근거: 설계 <c>docs/설계/erp/20261118_WG1-51_인쇄작3_반려봉합_설계.md</c> §7-3 「Y 템플릿 편집」 ·
/// 구별 기준 <c>docs/설계/erp/20261119_WG1-51_갈래A_K4기본폼_구별기준.md</c> §4 갈래 B 조건 B-1 · B-2(PM 결재 🟢).
/// </para>
/// <para>
/// 🔴 무엇을 재나 — 편집창이 「불러온 좌표를 그대로 보내는」 판정이
///   ⑴ 서버 렌더 Y 판정(<c>PdfRenderService.cs</c> <c>isYTemplate</c> · <c>useCoords</c>)과 <b>같은 네 칸</b>을 보나(B-2)
///   ⑵ 그림 찍기 칸을 <b>불러온 저장값</b>(<c>Template.PrintBackground</c>)으로 보나(B-1)
///   ⑶ 빈 A4 저장 길이 그 판정 <b>밑에서만</b> 불러온 좌표를 보내나.
/// </para>
/// <para>
/// 🔴 이 게이트가 못 재는 것(「통과」로 적지 않는다): 브라우저에서 실제로 저장한 행의 좌표 글자가
///   그대로인지(YN5) · 미리보기에 칸 값이 나오는지(YN5-2) · K4 저장이 좌표를 비우는지(YN5-3)는
///   DB · 앱이 있어야 재는 축이라 갈래 D · Z(CI)가 잰다. 서버 ⑤(그림이 실제로 읽힘)는 화면이 잴 수 없다.
/// </para>
/// <para>🔴 기존 시험 파일은 한 줄도 안 고쳤다(#1 · 축⒧-1 개정 0). DB 를 쓰지 않는다.</para>
/// </remarks>
public class FormYTemplateCoordsKeepGateTests
{
    private static readonly string[] EditorPath =
        { "src", "HitPan.Web", "Components", "Common", "FormTemplateEditorDialog.razor" };

    private static readonly string[] RenderPath =
        { "src", "HitPan.Application", "Services", "PdfRenderService.cs" };

    /// <summary>
    /// Y 판정 네 칸 — (이름 · 서버 쪽 글자 · 편집창 쪽 글자). 편집창 쪽은 <b>불러온 값</b>(<c>Template.</c>)만 본다.
    /// </summary>
    private static readonly (string Name, string Server, string Editor)[] FourConditions =
    {
        ("① 빈 A4(plain · 서수 비교)",
            @"string\.Equals\(\s*template\?\.PaperMode\s*,\s*""plain""\s*,\s*StringComparison\.Ordinal\s*\)",
            @"string\.Equals\(\s*Template\.PaperMode\s*,\s*""plain""\s*,\s*StringComparison\.Ordinal\s*\)"),
        ("② 저장된 그림 찍기 = 켬",
            @"(?<![\w.])savedPrintBackground(?!\w)",
            @"(?<![\w.])Template\.PrintBackground(?!\w)"),
        ("③ 그림 경로 있음",
            @"!\s*string\.IsNullOrWhiteSpace\(\s*template\?\.BackgroundImageUrl\s*\)",
            @"!\s*string\.IsNullOrWhiteSpace\(\s*Template\.BackgroundImageUrl\s*\)"),
        ("④ 칸 좌표 1개 이상",
            @"\(\s*template\?\.FieldCoords\.Count\s*\?\?\s*0\s*\)\s*>\s*0",
            @"(?<![\w.])_coords\.Count\s*>\s*0"),
    };

    [Fact(DisplayName = "갈래B-1 🔴 편집창 Y 판정이 서버 렌더 Y 판정과 **같은 네 칸**을 본다 (구별 기준 §4 B-2)")]
    public void YTemplateJudgement_SameFourConditionsAsRenderer()
    {
        var render = ReadSource(RenderPath);
        var editor = ReadSource(EditorPath);

        var serverExpr = Statement(render, "var isYTemplate =") + "\n" + Statement(render, "var useCoords =");
        var editorExpr = Statement(editor, "_loadedYTemplate =");

        foreach (var (name, server, edit) in FourConditions)
        {
            Assert.True(Regex.IsMatch(serverExpr, server),
                $"서버 렌더 Y 판정에 {name} 칸이 없다 — 서버가 바뀌었으면 편집창 판정도 같이 바꿔야 한다(B-2).");
            Assert.True(Regex.IsMatch(editorExpr, edit),
                $"편집창 Y 판정에 {name} 칸이 없다 — 서버와 판정이 갈리면 화면이 지킨 좌표를 서버가 버리거나 그 반대가 된다.");
        }
    }

    [Fact(DisplayName = "갈래B-1 🔴 그림 찍기 칸은 **불러온 저장값**으로 판정한다 — 지금 칸 값이면 K4 가 Y 가 된다 (B-1)")]
    public void YTemplateJudgement_UsesLoadedSavedValueOnly()
    {
        var editor = ReadSource(EditorPath);
        Assert.True(LoadedJudgementIsSavedValueOnly(editor),
            "편집창 Y 판정이 지금 칸 값(_form.)을 읽는다 — K4(그림 찍기 0)에서 칸만 켜고 저장해도 좌표가 살아 Y 가 된다.");
    }

    [Fact(DisplayName = "갈래B-1 🔴 빈 A4 저장 길은 Y 판정 **밑에서만** 불러온 좌표를 글자 그대로 보낸다 (YN5 · YN5-3)")]
    public void PlainSavePath_SendsLoadedCoordsOnlyUnderYJudgement()
    {
        var editor = ReadSource(EditorPath);
        Assert.True(PlainPathGuarded(editor),
            "BuildCoordsJson 의 빈 A4 줄이 「KeepLoadedYCoords ? _loadedCoordsJson : null」 모양이 아니다 — "
            + "Y 템플릿 좌표가 지워지거나(YN5) K4 좌표가 살아난다(YN5-3).");
    }

    [Fact(DisplayName = "갈래B-1 음성대조군 🔴 심은 결함 셋 — 각각 빨개진다")]
    public void NegativeControls_PlantedDefectsTurnRed()
    {
        var editor = ReadSource(EditorPath);

        // ⑴ B-1 결함 — 그림 찍기를 지금 칸 값으로 판정
        var b1 = editor.Replace("&& Template.PrintBackground", "&& _form.PrintBackground", StringComparison.Ordinal);
        Assert.NotEqual(editor, b1);
        Assert.False(LoadedJudgementIsSavedValueOnly(b1), "B-1 결함을 심었는데 초록이다 — 이 게이트는 빨개질 수 없다.");
        Assert.DoesNotMatch(FourConditions[1].Editor, Statement(b1, "_loadedYTemplate ="));

        // ⑵ B-2 결함 — 그림 경로 칸을 뺌
        var b2 = editor.Replace("&& !string.IsNullOrWhiteSpace(Template.BackgroundImageUrl)", string.Empty, StringComparison.Ordinal);
        Assert.NotEqual(editor, b2);
        Assert.DoesNotMatch(FourConditions[2].Editor, Statement(b2, "_loadedYTemplate ="));

        // ⑶ 옛 판(main) 모양 — 빈 A4 면 무조건 null
        var old = Regex.Replace(editor,
            @"if \(_form\.PaperMode != ""preprint""\) return KeepLoadedYCoords \? _loadedCoordsJson : null;",
            @"if (_form.PaperMode != ""preprint"") return null;");
        Assert.NotEqual(editor, old);
        Assert.False(PlainPathGuarded(old), "옛 판 모양을 심었는데 초록이다 — YN5 를 못 잡는다.");

        // ⑷ 판정 없이 늘 보내는 모양 — K4 좌표가 살아난다
        var always = Regex.Replace(editor,
            @"return KeepLoadedYCoords \? _loadedCoordsJson : null;",
            "return _loadedCoordsJson;");
        Assert.NotEqual(editor, always);
        Assert.False(PlainPathGuarded(always), "판정 없이 늘 보내는 모양을 심었는데 초록이다 — YN5-3 을 못 잡는다.");
    }

    // ──────────────────────────────────────────────────────────────

    private static bool LoadedJudgementIsSavedValueOnly(string editor)
    {
        var expr = Statement(editor, "_loadedYTemplate =");
        return Regex.IsMatch(expr, FourConditions[1].Editor)
            && !expr.Contains("_form.", StringComparison.Ordinal);
    }

    private static bool PlainPathGuarded(string editor)
    {
        var body = MethodBody(editor, "private string? BuildCoordsJson()");
        var line = Regex.Match(body,
            @"if\s*\(\s*_form\.PaperMode\s*!=\s*""preprint""\s*\)\s*return\s+KeepLoadedYCoords\s*\?\s*_loadedCoordsJson\s*:\s*null\s*;");
        if (!line.Success) return false;

        // 판정 속성이 불러온 Y 판정을 안고 있어야 한다 — 빠지면 K4 좌표가 살아난다.
        var keep = Statement(editor, "=> _loadedYTemplate");
        return keep.Length > 0
            && Regex.IsMatch(keep, @"string\.Equals\(\s*_form\.PaperMode\s*,\s*""plain""\s*,\s*StringComparison\.Ordinal\s*\)")
            && Regex.IsMatch(keep, @"(?<![\w.])_form\.PrintBackground(?!\w)")
            && Regex.IsMatch(keep, @"!\s*string\.IsNullOrWhiteSpace\(\s*_form\.BackgroundImageUrl\s*\)");
    }

    /// <summary>
    /// 줄머리가 <paramref name="anchor"/> 로 시작하는 <b>코드 줄</b>부터 첫 <c>;</c> 까지 — 줄 끝 <c>//</c> 주석은 걷는다.
    /// 🔴 주석 줄(<c>//</c> · <c>*</c> · <c>@*</c>)은 닻으로 안 센다. 못 찾으면 빈 글자(⇒ 단언이 빨개진다).
    /// </summary>
    private static string Statement(string src, string anchor)
    {
        var lines = src.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var t = lines[i].TrimStart();
            if (!t.StartsWith(anchor, StringComparison.Ordinal)) continue;

            var sb = new System.Text.StringBuilder();
            for (var j = i; j < lines.Length; j++)
            {
                var code = CutLineComment(lines[j]);
                sb.Append(code).Append('\n');
                if (code.Contains(';', StringComparison.Ordinal)) return sb.ToString();
            }
            return string.Empty;
        }
        return string.Empty;
    }

    /// <summary>메서드 머리 줄부터 짝 맞는 닫는 중괄호까지 — 주석 줄은 걷는다.</summary>
    private static string MethodBody(string src, string header)
    {
        var lines = src.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var sb = new System.Text.StringBuilder();
        var depth = 0;
        var started = false;
        foreach (var raw in lines)
        {
            if (!started)
            {
                if (!raw.TrimStart().StartsWith(header, StringComparison.Ordinal)) continue;
                started = true;
            }

            var code = CutLineComment(raw);
            sb.Append(code).Append('\n');
            foreach (var c in code)
            {
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) return sb.ToString(); }
            }
        }
        return started ? sb.ToString() : string.Empty;
    }

    /// <summary>
    /// 줄 끝 <c>//</c> 주석을 걷는다 — 🔴 문자열 안의 <c>//</c> 는 안 자른다(글자를 한 자씩 걷는다).
    /// 주석으로만 된 줄은 빈 줄이 된다.
    /// </summary>
    private static string CutLineComment(string line)
    {
        var t = line.TrimStart();
        if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal)
            || t.StartsWith("@*", StringComparison.Ordinal) || t.StartsWith("/*", StringComparison.Ordinal))
            return string.Empty;

        var inStr = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inStr)
            {
                if (c == '\\') { i++; continue; }
                if (c == '"') inStr = false;
                continue;
            }
            if (c == '"') { inStr = true; continue; }
            if (c == '/' && i + 1 < line.Length && line[i + 1] == '/') return line[..i];
        }
        return line;
    }

    private static string ReadSource(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.True(dir is not null, "레포 루트를 찾아야 한다");
        var path = Path.Combine(new[] { dir! }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"파일이 있어야 한다: {Path.Combine(parts)}");
        return File.ReadAllText(path, System.Text.Encoding.UTF8);
    }
}
