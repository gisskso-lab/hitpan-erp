using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Sdk;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴 <b>20261019작B2 절 ④ — 종단 성적서 갱신 + 「검증팀 C-3」·「검증팀 C-8」 판정 게이트</b>
/// (<c>G-B2-4-1</c> · <c>G-B2-4-2</c> · 검출기 자기시험 2건)
///
/// <para><b>[이 절이 하는 것]</b> 「판정」이지 「시험 수행」이 아니다. 실패 주입(검증팀 C-3)의
/// 실제 수행은 백지 Sandbox 를 요구하므로 범위 밖이다(작지서 §8-2 위험 1 · PM 판정).
/// ⇒ <b>미채증은 미채증으로 적는다.</b> 등급을 올리지 않는다.</para>
///
/// <para><b>[왜 게이트가 필요한가]</b> 성적서 §6-2-1 이 적은 그대로 <c>C-1</c>~<c>C-3</c> 는
/// <b>3팀 동명이의</b>다(검증팀 / 설계팀 / 개발팀이 각자 다른 내용을 같은 이름으로 썼다).
/// 팀명 없이 인용하면 다음 사람이 <b>다른 팀 조건을 닫았다고 읽는다.</b>
/// ⇒ 이 게이트는 <b>갱신분 안에서</b> 팀명 없는 표기를 0건으로 고정한다.</para>
///
/// <para>🔴 <b>§9 교정 반영</b> — 작지서 §9-1: 종전 <c>G-B2-4-2</c> 는 검증팀 C-8 을 「부분」으로
/// 적게 했으나, 성적서 <c>:368</c>·<c>:558</c> 이 <b>「신규 조건이 필요하다 → C-8 신설」</b>·
/// <b>「🔴 신규 · 미착수」</b> 이므로 「부분」은 <b>수행 없는 등급 상향</b>이다. 폐기하고
/// <b>「신규·미착수」</b>로 문다. 검증팀 C-3 은 <b>「미채증」 유지</b>.</para>
///
/// <para>🔴 <b>음성 대조군</b> — 글자 단언이 아니라는 것을 이 파일 안에서 증명한다:
/// <c>G-B2-4-N1</c>·<c>G-B2-4-N2</c> 가 <b>검출기 자신</b>을 합성 표본으로 먹여 잰다.
/// 검출기가 가짜면(아무것도 못 잡으면) 그 두 건이 빨강이다.</para>
///
/// <para>근거: <c>docs/운영기록/20261019작B2_CS왕복_종단성적서_작P0봉합ABC_작업지시서.md</c>
/// §2-2 · §3 · <b>§9-1</b> · §9-4</para>
/// </summary>
public sealed class B2EndToEndReportVerdictGateTests
{
    /// <summary>성적서 안에서 갱신분을 여는 닻. 이 글자 사이만 센다 — 기존 줄은 안 건드린다(헌법 #1).</summary>
    private const string AnchorOpen = "<!-- G-B2-4 갱신분 시작 : 20261019작B2 절 ④ -->";

    private const string AnchorClose = "<!-- G-B2-4 갱신분 끝 : 20261019작B2 절 ④ -->";

    // ── 레포 뿌리 (선례 P0DeliveryPathGateTests.RepoRoot 와 같은 방식) ──────────
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !(Directory.Exists(Path.Combine(dir.FullName, "src"))
                                 && Directory.Exists(Path.Combine(dir.FullName, "docs"))))
            dir = dir.Parent;
        if (dir is null) throw new XunitException("레포 뿌리를 못 찾았다(src + docs 폴더 기준).");
        return dir.FullName;
    }

    private static string ReportPath() => Path.Combine(RepoRoot(), "docs", "검증",
        "20260807_프로젝트_시험성적서_배포종단_설치_워치독_업데이트.md");

    /// <summary>갱신분(닻 사이)만 떼어 온다. 닻이 없으면 아직 안 적은 것이다 — 조용한 통과 금지.</summary>
    private static string UpdatedBlock()
    {
        var path = ReportPath();
        Assert.True(File.Exists(path), "종단 성적서가 없다: " + path);
        var text = File.ReadAllText(path);

        var open = text.IndexOf(AnchorOpen, StringComparison.Ordinal);
        var close = text.IndexOf(AnchorClose, StringComparison.Ordinal);
        Assert.True(open >= 0,
            "🔴 성적서에 20261019작B2 ④ 갱신분이 없다 — 여는 닻을 못 찾았다:\n  " + AnchorOpen);
        Assert.True(close > open,
            "🔴 갱신분의 닫는 닻이 없거나 순서가 뒤집혔다:\n  " + AnchorClose);

        return text.Substring(open + AnchorOpen.Length, close - open - AnchorOpen.Length);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  검출기 — 팀명 없는 조건 표기를 찾는다
    //
    //  🔴 「C-3」 앞에 「검증팀」이 붙었는지만 본다. 이 검출기가 가짜면 G-B2-4-1 은
    //     영원히 초록이므로, 아래 G-B2-4-N1 이 합성 표본으로 검출기를 먼저 잰다.
    // ══════════════════════════════════════════════════════════════════════════
    internal static List<string> BareConditionMentions(string block)
    {
        var bad = new List<string>();
        foreach (var raw in block.Split('\n'))
        {
            foreach (Match m in Regex.Matches(raw, @"C-[38]\b"))
            {
                // 🔴 「앞 30자 안에 검증팀이 있나」로 재면 안 된다 — 2026-10-19 실측으로 이 검출기가
                //   먼저 빨강이 됐다. 같은 줄에 앞서 「검증팀 C-8」이 있으면 그 뒤의 맨 「C-3」까지
                //   팀명이 붙은 것으로 읽혔다. ⇒ **바로 직전 낱말**만 본다.
                var before = raw.Substring(0, m.Index)
                                .TrimEnd(' ', '\t', '*', '`', '「', '|', '(', '[');
                if (!before.EndsWith("검증팀", StringComparison.Ordinal))
                    bad.Add(raw.Trim());
            }
        }
        return bad;
    }

    /// <summary>표 한 줄에서 판정 칸을 꺼낸다. <c>| 검증팀 C-3 | … | 판정 |</c> 모양.</summary>
    private static string VerdictCellOf(string block, string condition)
    {
        foreach (var raw in block.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("|", StringComparison.Ordinal)) continue;

            var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToList();
            if (cells.Count < 2) continue;
            if (!cells[0].Contains(condition, StringComparison.Ordinal)) continue;

            return cells[cells.Count - 1];
        }
        throw new XunitException(
            $"🔴 갱신분에 「{condition}」 판정 줄이 없다 — 판정표가 기계가 읽을 모양이 아니다.");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  G-B2-4-1 — 갱신분은 팀명을 병기한다
    // ══════════════════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-B2-4-1 성적서 갱신분의 C-3·C-8 표기에 「검증팀」 팀명이 전건 병기된다")]
    public void G_B2_4_1_갱신분은_팀명을_병기한다()
    {
        var block = UpdatedBlock();

        // 조용한 통과 금지 — 갱신분에 조건 표기가 아예 없으면 닻만 심은 것이다.
        var total = Regex.Matches(block, @"C-[38]\b").Count;
        Assert.True(total >= 2,
            $"갱신분에서 조건 표기를 {total} 건만 찾았다 — 검증팀 C-3 · 검증팀 C-8 둘을 다 적어야 한다.");

        var bare = BareConditionMentions(block);
        Assert.True(bare.Count == 0,
            "🔴 팀명 없는 C-3·C-8 표기가 갱신분에 남았다 — 3팀 동명이의(성적서 §6-2-1)라서 "
            + "다음 사람이 다른 팀 조건을 닫았다고 읽는다:\n  " + string.Join("\n  ", bare));
    }

    [Fact(DisplayName = "G-B2-4-N1 팀명 검출기 자기시험 — 팀명 없는 표기를 실제로 잡는다 (음성 대조군)")]
    public void G_B2_4_N1_검출기가_가짜가_아니다()
    {
        // 🔴 합성 표본. 검출기가 아무것도 못 잡는 가짜라면 이 단언이 먼저 빨강이 된다.
        var 팀명없음 = "| C-3 | 실패 주입 | 미채증 |";
        Assert.Single(BareConditionMentions(팀명없음));

        var 팀명있음 = "| 검증팀 C-3 | 실패 주입 | 미채증 |";
        Assert.Empty(BareConditionMentions(팀명있음));

        // 한 줄에 둘이 섞여 있으면 팀명 없는 쪽만 잡힌다.
        var 섞임 = "검증팀 C-8 은 신규다. 그런데 C-3 는 아직이다.";
        Assert.Single(BareConditionMentions(섞임));
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  G-B2-4-2 — 판정 내용 (🔴 §9-1 교정판)
    //
    //    검증팀 C-3 = 「미채증」   (성적서 :553 그대로 유지)
    //    검증팀 C-8 = 「신규·미착수」(성적서 :368·:558 그대로 — 「부분」은 폐기)
    // ══════════════════════════════════════════════════════════════════════════
    [Fact(DisplayName = "G-B2-4-2 검증팀 C-3 = 미채증 · 검증팀 C-8 = 신규·미착수 로 적혔다")]
    public void G_B2_4_2_판정이_등급을_올리지_않는다()
    {
        var block = UpdatedBlock();

        var c3 = VerdictCellOf(block, "검증팀 C-3");
        Assert.Contains("미채증", c3);
        Assert.DoesNotContain("해소", c3);
        Assert.DoesNotContain("충족", c3);

        var c8 = VerdictCellOf(block, "검증팀 C-8");
        Assert.Contains("신규", c8);
        Assert.Contains("미착수", c8);
        Assert.DoesNotContain("해소", c8);
        Assert.DoesNotContain("충족", c8);
        // 🔴 §9-1 교정의 핵심 — 「부분」은 수행 없는 등급 상향이라 폐기됐다.
        Assert.DoesNotContain("부분", c8);
    }

    [Fact(DisplayName = "G-B2-4-N2 판정칸 해독기 자기시험 — 「해소」로 적으면 실제로 잡힌다 (음성 대조군)")]
    public void G_B2_4_N2_해독기가_가짜가_아니다()
    {
        // 🔴 합성 표본으로 해독기를 먼저 잰다. 판정 칸을 못 읽는 해독기라면
        //    G-B2-4-2 는 무엇을 적어도 초록이 된다.
        var 거짓상향 = "| 검증팀 C-3 | R-6 실패 주입 | 🟢 해소 |";
        Assert.Equal("🟢 해소", VerdictCellOf(거짓상향, "검증팀 C-3"));
        Assert.Contains("해소", VerdictCellOf(거짓상향, "검증팀 C-3"));

        var 올바름 = "| 검증팀 C-3 | R-6 실패 주입 | ⬜ 미채증 유지 |";
        Assert.DoesNotContain("해소", VerdictCellOf(올바름, "검증팀 C-3"));

        // 판정 줄이 없으면 조용히 통과하지 않고 터진다.
        Assert.Throws<XunitException>(() => VerdictCellOf("판정표가 없다", "검증팀 C-3"));
    }
}
