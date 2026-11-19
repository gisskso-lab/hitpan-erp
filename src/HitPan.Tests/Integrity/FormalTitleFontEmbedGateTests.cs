using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HitPan.Application.Services;
using Xunit;

namespace HitPan.Tests.Integrity;

/// <summary>
/// 🔴🔴 격식 제목 글꼴 반입 게이트 — W-G1-51 갈래 E⑵ 고침 3 (법무 1줄 ⑥ · 설계 P11 · PM 판단 §74).
/// </summary>
/// <remarks>
/// 🔴 <b>왜 있나</b> — 법무 조건: 원본 1개 · 손질 0 · SHA-256 고정 · 저장소 파일과 빌드 산출 내장 리소스 <b>둘 다</b> 같은 값.
///   바이트가 하나라도 다르면(줄바꿈 변환 · 다른 판 · 손질) 「수정본」 이 되어 OFL 조항 3 · 4 검토가 다시 필요하다 ⇒ 빨강.
/// 🔴 고정 값 = 개발명세서 §고침 3 고침3-1(notofonts/noto-cjk 태그 Serif2.003 · 커밋 9b0f1436e455d902de067a2501422e5dc71ad16b ·
///   Serif/SubsetOTF/KR/NotoSerifKR-Bold.otf). 판 올림 = 새 태그 · 새 커밋 · 새 해시로 법무 ①~⑥ 다시 대조한 뒤에만 이 값을 바꾼다.
/// 🔴 내장 리소스 이름은 <b>제품 상수</b>(<c>PdfRenderService.FormalTitleFontResourceName</c>)를 반사로 읽는다 —
///   시험이 이름을 따로 적으면 제품 배선이 바뀌어도 초록이 된다.
/// 🔴 api 출력 · 게시 폴더의 고지 파일(<c>licenses\NotoSerifKR-OFL.txt</c>) 실림은 [4] 실측 몫이다(법무 ㉱) — 여기서 단언하지 않는다.
/// </remarks>
public sealed class FormalTitleFontEmbedGateTests
{
    private const string PinnedSha256 = "263408609ef416d597718ea91055379867d3e7086fa32f049996995d4e715e55";

    private const string FontFileName = "NotoSerifKR-Bold.otf";
    private const string NoticeFileName = "NotoSerifKR-OFL.txt";

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.True(dir is not null && Directory.Exists(Path.Combine(dir, "src")),
            "레포 루트를 찾아야 한다");
        return dir!;
    }

    private static string FontDirFile(string fileName) =>
        Path.Combine(FindRepoRoot(), "src", "HitPan.Application", "Resources", "Fonts", fileName);

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string ProductResourceName()
    {
        var field = typeof(PdfRenderService).GetField(
            "FormalTitleFontResourceName", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.True(field is not null, "FormalTitleFontResourceName 이 없다 — 배선이 바뀌었다.");
        return (string)field!.GetRawConstantValue()!;
    }

    [Fact(DisplayName = "E⑵ 고침3 ① 🔴 저장소 글꼴 파일 SHA-256 = 고정 값(받을 때 1번 계산 · 손질 0)")]
    public void RepoFontFileMatchesPinnedSha256()
    {
        var path = FontDirFile(FontFileName);
        Assert.True(File.Exists(path), $"저장소에 글꼴 파일 {FontFileName} 이 없다.");

        Assert.Equal(PinnedSha256, Sha256Hex(File.ReadAllBytes(path)));
    }

    [Fact(DisplayName = "E⑵ 고침3 ② 🔴 빌드 산출 내장 리소스 바이트 SHA-256 = 고정 값 = 저장소 파일(다르면 빨강)")]
    public void EmbeddedResourceMatchesPinnedSha256AndRepoFile()
    {
        var name = ProductResourceName();
        using var stream = typeof(PdfRenderService).Assembly.GetManifestResourceStream(name);
        Assert.True(stream is not null,
            $"HitPan.Application 에 내장 리소스 {name} 가 없다 — csproj EmbeddedResource 줄 · LogicalName 을 확인.");

        using var buffer = new MemoryStream();
        stream!.CopyTo(buffer);
        var embedded = buffer.ToArray();
        var repo = File.ReadAllBytes(FontDirFile(FontFileName));

        Assert.Equal(PinnedSha256, Sha256Hex(embedded));
        Assert.True(embedded.AsSpan().SequenceEqual(repo),
            "내장 리소스와 저장소 글꼴 파일의 바이트가 다르다 — 원본 그대로가 아니다.");
    }

    [Fact(DisplayName = "E⑵ 고침3 ③ 🔴 저장소 글꼴 옆 고지 파일 — 저작권 원문(이름 기록 ID 0 · 7) · OFL 1.1 전문")]
    public void NoticeFileNextToFontCarriesCopyrightAndLicense()
    {
        var path = FontDirFile(NoticeFileName);
        Assert.True(File.Exists(path), $"저장소 글꼴 옆에 고지 파일 {NoticeFileName} 이 없다(법무 ③ (다)).");

        // 평문 UTF-8 — 깨진 바이트면 여기서 던진다(법무 ㉮).
        var text = File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));

        Assert.Contains("© 2017-2024 Adobe (http://www.adobe.com/).", text);
        Assert.Contains("Noto is a trademark of Google Inc.", text);
        Assert.Contains("SIL OPEN FONT LICENSE Version 1.1 - 26 February 2007", text);
        Assert.Contains("THE FONT SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND,", text);
    }
}
