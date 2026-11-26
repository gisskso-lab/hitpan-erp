using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HitPan.Application.Services;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

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
///   → 2026-11-25 F-2 부터 G2 ~ G4 가 출시 꼴 게시 폴더에서 단언(W-G1-51 갈래 F-2 · 결재 §133 · 아래 ④ ~ ⑦).
/// 🔴 F-2(④ ~ ⑦) — ④ 저장소 고지 = LF 원본(늘 돎) · ⑤ ~ ⑦ 출시 꼴(win-x64 · framework-dependent · 단일 파일) 게시 폴더 실림.
///   ⑤ ~ ⑦ 은 <c>HITPAN_REQUIRE_SHIPFORM=1</c> 전용 단계(build-and-test.yml)에서만 잰다 — 그 단계에서 폴더가 없거나 비면 스킵이 아니라 빨강.
///   그 밖(전체 시험 · 로컬)에서는 <c>shipform-optout.log</c> 표식만 남기고 돌아간다(전용 단계가 표식 생김을 빨강으로 본다).
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

    // ───────────────────────── F-2 (W-G1-51 갈래 F-2 · 2026-11-25 · 고침 1 §15-2 · PM 결재 §140 · §141 · §142) ─────────────────────────
    // 🔴 메시지에는 전체 경로를 찍지 않는다(S-6 · N-6) — {DIR}(게시 폴더) · 저장소 루트 · 꾸러미 폴더 기준 상대 경로 · 파일 이름만.

    /// <summary>고지 원본(LF) 고정 값 — 저장소 블롭 5,682 B 의 SHA-256(결재 §133 ② · CRLF 판 5,803 B 는 다른 값).</summary>
    private const string PinnedNoticeSha256 = "20c156a223d1ab552adc88c575e1d71d5179f32b3e7190db78f931ee01aedf55";
    private const long PinnedNoticeLength = 5682;

    /// <summary>
    /// 단일 파일 번들 표식(32바이트 · 16진) — .NET 호스트가 단일 파일 번들 머리 위치를 찾는 고정 바이트.
    /// 출처: dotnet/runtime corehost 의 번들 표식 자리 표시 바이트(bundle_marker 의 header_offset 자리 · SDK HostModel 이
    /// 같은 바이트를 찾아 앞 8바이트에 머리 위치를 써 넣는다 · 링크 고정 0).
    /// 출처 주석엔 「.net core bundle 의 SHA-256」 이라 적혀 있으나 <b>계산으로 안 나온다</b>(2026-11-25 실측 · 개발명세서 §1) ⇒ 상수로 적고,
    /// 오타는 <see cref="AssertMarkerMatchesSdkAppHost"/> 의 자기 대조(같은 publish 의 중간 산출 apphost)가 동작으로 막는다.
    /// 🔴 표식을 못 찾으면 빨강. 머리 위치를 끝에서 추정 · 글자로 찾기 · 낱개 dll 로 대체 같은 <b>우회 금지</b> —
    ///   이 검사를 느슨하게 고치면 [3-V] 재검(작업지시서 고침 1 §15-2 ① · K8).
    /// </summary>
    private const string BundleMarker = "8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae";

    /// <summary>단일 파일 번들 머리 판 — 개발명세서에 이 PC 실측으로 적은 판(6.0). 다르면 읽는 법을 다시 재야 하므로 빨강.</summary>
    private const uint MeasuredBundleMajor = 6;
    private const uint MeasuredBundleMinor = 0;

    /// <summary>결재된 QuestPDF 판 — 네이티브 6 목록 · 고지 결재의 짝(K9 · Q7). 판이 바뀌면 G4 빨강 = 목록 · 고지 다시 결재.</summary>
    private const string ApprovedQuestPdfVersion = "2026.2.4";

    private const string ShipformExeName = "HitPan.API.exe";
    private const string BundledApplicationDll = "HitPan.Application.dll";
    private const string SelfContainedProbe = "System.Private.CoreLib.dll";

    /// <summary>같은 출시 꼴 publish 의 중간 산출 apphost — 표식 상수 자기 대조 자리(저장소 루트 기준 · K8).</summary>
    private static readonly string[] AppHostRelative = { "src", "HitPan.API", "obj", "Release", "net8.0", "win-x64", "apphost.exe" };

    /// <summary>QuestPDF 네이티브 6(1V F-4 · 결재 §133 ③). 원본 폴더 이름 집합이 이와 다르면 「판 바뀜 · 목록 다시 결재」.</summary>
    private static readonly string[] ExpectedNativeSix =
    {
        "qpdf.dll", "zlib1.dll", "QuestPdfSkia.dll", "libgcc_s_seh-1.dll", "libstdc++-6.dll", "libwinpthread-1.dll",
    };

    private static readonly Regex PlainVersion = new(@"^\d+(\.\d+){1,3}$", RegexOptions.CultureInvariant);

    private readonly ITestOutputHelper _output;

    public FormalTitleFontEmbedGateTests(ITestOutputHelper output) => _output = output;

    private static string Short(string hex) => hex.Length > 12 ? hex[..12] : hex;

    /// <summary>
    /// ⑤ ~ ⑦ 공통 관문. REQUIRE=1 이면 폴더가 있고 비어 있지 않아야 한다(아니면 빨강 · 스킵 아님).
    /// REQUIRE≠1 이면 표식만 남기고 null — 선례 InstallerBootstrapMessageGateTests G_T6_5 의 iscc-optout.log 꼴.
    /// </summary>
    private static string? ShipformDirOrOptOut(string gate)
    {
        bool required = Environment.GetEnvironmentVariable("HITPAN_REQUIRE_SHIPFORM") == "1";
        if (!required)
        {
            string optout = Path.Combine(AppContext.BaseDirectory, "shipform-optout.log");
            File.AppendAllText(optout,
                $"{DateTime.Now:s} {gate} G-F2 미계측(의도) — HITPAN_REQUIRE_SHIPFORM≠1. 출시 꼴 전용 단계가 잰다.\n");
            Console.Error.WriteLine($"[{gate} G-F2 미계측(의도)] 전용 단계(HITPAN_REQUIRE_SHIPFORM=1)에서만 잰다.");
            return null;
        }

        string? dir = Environment.GetEnvironmentVariable("HITPAN_SHIPFORM_DIR");
        if (string.IsNullOrWhiteSpace(dir))
            throw new XunitException("HITPAN_REQUIRE_SHIPFORM=1 인데 HITPAN_SHIPFORM_DIR 가 비었다 — 이 게이트는 스킵하지 않는다.");
        if (!Directory.Exists(dir))
            throw new XunitException("HITPAN_REQUIRE_SHIPFORM=1 인데 HITPAN_SHIPFORM_DIR 폴더가 없다 — 이 게이트는 스킵하지 않는다.");
        if (!Directory.EnumerateFileSystemEntries(dir).Any())
            throw new XunitException("HITPAN_REQUIRE_SHIPFORM=1 인데 HITPAN_SHIPFORM_DIR 폴더가 비었다 — 출시 꼴 publish 가 안 됐다.");
        return dir;
    }

    private sealed record BundleEntry(long Offset, long Size, long CompressedSize, byte Type, string RelativePath);

    private sealed record BundleInfo(uint Major, uint Minor, long HeaderOffset, int MarkerIndex, IReadOnlyList<BundleEntry> Entries);

    private static List<int> FindAll(byte[] haystack, byte[] needle)
    {
        var hits = new List<int>();
        int from = 0;
        while (from <= haystack.Length - needle.Length)
        {
            int idx = haystack.AsSpan(from).IndexOf(needle);
            if (idx < 0) break;
            hits.Add(from + idx);
            from += idx + 1;
        }
        return hits;
    }

    /// <summary>
    /// 표식 상수 자기 대조(K8) — 같은 출시 꼴 publish 의 중간 산출 apphost 안에서 상수가 정확히 1번 · 바로 앞 8바이트(머리 위치) = 0.
    /// 아니면 상수 오타 또는 SDK 호스트 판 바뀜 ⇒ 빨강. 파일 없음도 빨강(이 도우미는 REQUIRE=1 길에서만 불린다).
    /// </summary>
    private static void AssertMarkerMatchesSdkAppHost(byte[] marker)
    {
        var apphost = Path.Combine(new[] { FindRepoRoot() }.Concat(AppHostRelative).ToArray());
        var rel = string.Join('/', AppHostRelative);
        if (!File.Exists(apphost))
            throw new XunitException($"표식 자기 대조용 중간 산출 {rel} 이 없다 — 출시 꼴 publish(-r win-x64)가 먼저 돌아야 한다.");
        var bytes = File.ReadAllBytes(apphost);
        var hits = FindAll(bytes, marker);
        long header = hits.Count == 1 && hits[0] >= 8 ? BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(hits[0] - 8, 8)) : -1;
        if (hits.Count != 1 || header != 0)
            throw new XunitException(
                $"표식 상수 ≠ 이 SDK 호스트 — {rel} 안 적중 {hits.Count} 개 · 앞 8바이트 {header}(정확히 1 · 0 이어야 함). 판 바뀜 · 설계 다시.");
    }

    /// <summary>
    /// 단일 파일 번들 목록 읽기(읽기만 · 실행 · 적재 0). 표식 = <see cref="BundleMarker"/>(상수 · 자기 대조 뒤) ·
    /// 표식 바로 앞 8바이트 = 머리 위치 · 머리 = 주 · 부 판 · 파일 수 · 번들 ID · deps/runtimeconfig 위치 · 크기 · 깃발 ·
    /// 항목 = 위치 · 크기 · 압축 크기(판 6 부터) · 종류 · 상대 경로. 개발명세서 「번들 판 실측」 에 이 PC 실측을 적었다.
    /// </summary>
    private static BundleInfo ReadBundle(byte[] exe)
    {
        byte[] marker = Convert.FromHexString(BundleMarker);
        AssertMarkerMatchesSdkAppHost(marker);

        var hits = FindAll(exe, marker);
        if (hits.Count != 1)
            throw new XunitException(
                $"{{DIR}}\\{ShipformExeName} 의 번들 표식이 {hits.Count} 개다(정확히 1 이어야 함) — 단일 파일 출시 꼴이 아니거나 표식이 겹친다.");
        int markerIndex = hits[0];
        if (markerIndex < 8)
            throw new XunitException($"{{DIR}}\\{ShipformExeName} 번들 표식 앞에 머리 위치 8바이트가 없다(표식 위치 {markerIndex}).");

        long headerOffset = BinaryPrimitives.ReadInt64LittleEndian(exe.AsSpan(markerIndex - 8, 8));
        if (headerOffset <= markerIndex || headerOffset >= exe.Length)
            throw new XunitException(
                $"{{DIR}}\\{ShipformExeName} 번들 머리 위치 = {headerOffset}(표식 {markerIndex} · 길이 {exe.Length}) — 단일 파일 번들이 아니다(출시 꼴 = PublishSingleFile=true).");

        using var ms = new MemoryStream(exe, writable: false) { Position = headerOffset };
        using var r = new BinaryReader(ms, Encoding.UTF8);
        uint major = r.ReadUInt32();
        uint minor = r.ReadUInt32();
        if (major != MeasuredBundleMajor || minor != MeasuredBundleMinor)
            throw new XunitException(
                $"번들 판 {major}.{minor} ≠ 실측 기록 판 {MeasuredBundleMajor}.{MeasuredBundleMinor} — 읽는 법을 다시 재고 개발명세서를 고친 뒤에만 이 값을 바꾼다.");
        int count = r.ReadInt32();
        if (count <= 0 || count > 100_000)
            throw new XunitException($"번들 파일 수 {count} — 머리를 잘못 읽었다.");
        r.ReadString(); // 번들 ID
        r.ReadInt64(); r.ReadInt64(); // deps.json 위치 · 크기
        r.ReadInt64(); r.ReadInt64(); // runtimeconfig.json 위치 · 크기
        r.ReadUInt64(); // 깃발

        var entries = new List<BundleEntry>(count);
        for (int i = 0; i < count; i++)
        {
            long offset = r.ReadInt64();
            long size = r.ReadInt64();
            long compressed = r.ReadInt64();
            byte type = r.ReadByte();
            string path = r.ReadString();
            if (offset < 0 || size < 0 || offset + size > exe.Length)
                throw new XunitException($"번들 항목 {path} 의 위치 · 크기({offset} · {size})가 exe 밖이다 — 머리를 잘못 읽었다.");
            if (compressed != 0)
                throw new XunitException(
                    $"번들 항목 {path} 이 압축돼 있다(압축 크기 {compressed}) — 출시 꼴이 바뀌었다(EnableCompressionInSingleFile).");
            entries.Add(new BundleEntry(offset, size, compressed, type, path));
        }

        return new BundleInfo(major, minor, headerOffset, markerIndex, entries);
    }

    /// <summary>
    /// 번들 안 HitPan.Application.dll 항목 하나(정확히 1 · 루트 · 비압축)를 찾아 바이트를 꺼낸다.
    /// 의존형 단언(K10): 번들에 System.Private.CoreLib.dll 이 있으면 자기 포함 꼴 ⇒ 빨강(출시 꼴에 맞춰 느슨하게 고치지 말 것 · §142 ③).
    /// </summary>
    private static (BundleEntry Entry, byte[] Bytes) ExtractBundledApplicationDll(byte[] exe)
    {
        var bundle = ReadBundle(exe);
        if (bundle.Entries.Any(e => string.Equals(Path.GetFileName(e.RelativePath.Replace('\\', '/')), SelfContainedProbe,
                StringComparison.OrdinalIgnoreCase)))
            throw new XunitException(
                $"번들 안에 {SelfContainedProbe} 가 있다 — 자기 포함 꼴 · 출시 꼴(framework-dependent) 아님(결재 §133 ④ · K10).");

        var sameName = bundle.Entries
            .Where(e => string.Equals(Path.GetFileName(e.RelativePath.Replace('\\', '/')), BundledApplicationDll,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (sameName.Count != 1)
            throw new XunitException(
                $"번들 안 {BundledApplicationDll} 항목이 {sameName.Count} 개다(정확히 1 이어야 함) — " +
                $"[{string.Join(", ", sameName.Select(e => e.RelativePath))}]");
        var entry = sameName[0];
        if (!string.Equals(entry.RelativePath, BundledApplicationDll, StringComparison.Ordinal))
            throw new XunitException($"번들 안 항목 경로가 「{entry.RelativePath}」 다 — 루트의 {BundledApplicationDll} 이어야 한다.");
        return (entry, exe.AsSpan((int)entry.Offset, (int)entry.Size).ToArray());
    }

    /// <summary>PE 매니페스트 리소스 중 이름이 같은 내장 리소스 하나를 꺼낸다(읽기만). 반환 = 바이트 · dll 안 바이트 위치.</summary>
    private static (byte[] Bytes, long OffsetInDll) ReadEmbeddedResource(byte[] dll, string name)
    {
        using var pe = new PEReader(new MemoryStream(dll, writable: false));
        if (!pe.HasMetadata)
            throw new XunitException($"번들에서 꺼낸 {BundledApplicationDll} 에 메타데이터가 없다 — 엉뚱한 항목이다.");
        var md = pe.GetMetadataReader();
        var matches = md.ManifestResources
            .Select(h => md.GetManifestResource(h))
            .Where(res => md.GetString(res.Name) == name)
            .ToList();
        if (matches.Count != 1)
            throw new XunitException(
                $"번들 안 {BundledApplicationDll} 에 내장 리소스 {name} 가 {matches.Count} 개다(정확히 1 이어야 함) — csproj EmbeddedResource 줄 · LogicalName 확인.");
        var res = matches[0];
        if (!res.Implementation.IsNil)
            throw new XunitException($"리소스 {name} 가 내장이 아니다(다른 파일 · 어셈블리 참조).");

        var corHeader = pe.PEHeaders.CorHeader
            ?? throw new XunitException($"{BundledApplicationDll} 에 CLR 머리가 없다.");
        var dir = corHeader.ResourcesDirectory;
        if (!pe.PEHeaders.TryGetDirectoryOffset(dir, out int dirFileOffset))
            throw new XunitException($"{BundledApplicationDll} 의 리소스 디렉터리 위치를 못 찾았다.");

        long start = dirFileOffset + res.Offset;
        if (start + 4 > dll.Length)
            throw new XunitException($"리소스 {name} 위치가 dll 밖이다.");
        int length = BinaryPrimitives.ReadInt32LittleEndian(dll.AsSpan((int)start, 4));
        if (length < 0 || start + 4 + length > dll.Length)
            throw new XunitException($"리소스 {name} 길이 {length} 가 dll 밖이다.");
        return (dll.AsSpan((int)start + 4, length).ToArray(), start + 4);
    }

    [Fact(DisplayName = "F-2 ④ 저장소 고지 = LF 원본 (G-F2-G1) — 길이 5682 · SHA-256 고정(.gitattributes eol=lf)")]
    public void F2_G1_RepoNoticeIsPinnedLfOriginal()
    {
        var path = FontDirFile(NoticeFileName);
        Assert.True(File.Exists(path), $"저장소에 고지 파일 {NoticeFileName} 이 없다.");
        var bytes = File.ReadAllBytes(path);
        var sha = Sha256Hex(bytes);
        if (bytes.LongLength != PinnedNoticeLength || sha != PinnedNoticeSha256)
            throw new XunitException(
                $"저장소 고지 {NoticeFileName} 가 LF 원본이 아니다 — 기대 {PinnedNoticeLength} B · {Short(PinnedNoticeSha256)} / " +
                $"실측 {bytes.LongLength} B · {Short(sha)} (CRLF 체크아웃이면 .gitattributes 규칙 · 손질이면 원본 복구).");
    }

    [Fact(DisplayName = "F-2 ⑤ 출시 꼴 고지 실림 (G-F2-G2) — licenses\\NotoSerifKR-OFL.txt = 고정 = 저장소")]
    public void F2_G2_ShipformNoticeShipped()
    {
        var dir = ShipformDirOrOptOut("G2");
        if (dir is null) return;

        var shipped = Path.Combine(dir, "licenses", NoticeFileName);
        if (!File.Exists(shipped))
            throw new XunitException($"출시 꼴 게시 폴더에 {{DIR}}\\licenses\\{NoticeFileName} 이 없다 — HitPan.API.csproj 고지 None 줄 확인(법무 ㉱).");
        var bytes = File.ReadAllBytes(shipped);
        var sha = Sha256Hex(bytes);
        if (bytes.LongLength != PinnedNoticeLength || sha != PinnedNoticeSha256)
            throw new XunitException(
                $"출시 꼴 고지가 LF 원본이 아니다 — 기대 {PinnedNoticeLength} B · {Short(PinnedNoticeSha256)} / 실측 {bytes.LongLength} B · {Short(sha)}.");
        var repo = File.ReadAllBytes(FontDirFile(NoticeFileName));
        if (!bytes.AsSpan().SequenceEqual(repo))
            throw new XunitException(
                $"출시 꼴 고지와 저장소 고지의 바이트가 다르다 — 게시 {bytes.LongLength} B · {Short(sha)} / 저장소 {repo.LongLength} B · {Short(Sha256Hex(repo))}.");
    }

    [Fact(DisplayName = "F-2 ⑥ 출시 꼴 번들 dll 글꼴 (G-F2-G3) — HitPan.API.exe 번들 안 HitPan.Application.dll 내장 리소스 = 고정 = 저장소")]
    public void F2_G3_ShipformBundledDllCarriesFont()
    {
        var dir = ShipformDirOrOptOut("G3");
        if (dir is null) return;

        if (File.Exists(Path.Combine(dir, BundledApplicationDll)))
            throw new XunitException(
                $"게시 폴더 맨 위에 낱개 {{DIR}}\\{BundledApplicationDll} 이 있다 — 단일 파일 출시 꼴이 아니다(K10).");

        var exePath = Path.Combine(dir, ShipformExeName);
        if (!File.Exists(exePath))
            throw new XunitException($"출시 꼴 게시 폴더에 {{DIR}}\\{ShipformExeName} 이 없다.");
        var exe = File.ReadAllBytes(exePath);

        var (entry, dll) = ExtractBundledApplicationDll(exe);
        var name = ProductResourceName();
        var (font, offsetInDll) = ReadEmbeddedResource(dll, name);
        var sha = Sha256Hex(font);
        long fontStartInExe = entry.Offset + offsetInDll;

        _output.WriteLine(
            $"[G-F2-G3] 번들 안 {entry.RelativePath} · exe 위치 {entry.Offset} · 크기 {entry.Size} · 종류 {entry.Type} · dll SHA {Sha256Hex(dll)}");
        _output.WriteLine(
            $"[G-F2-G3] 리소스 {name} · exe 바이트 {fontStartInExe} ~ {fontStartInExe + font.Length - 1} · 길이 {font.Length} · SHA {sha}");

        if (sha != PinnedSha256)
            throw new XunitException(
                $"번들 안 글꼴 리소스 SHA 가 고정 값과 다르다 — 기대 {Short(PinnedSha256)} / 실측 {Short(sha)} · 길이 {font.Length}.");
        var repo = File.ReadAllBytes(FontDirFile(FontFileName));
        if (!font.AsSpan().SequenceEqual(repo))
            throw new XunitException("번들 안 글꼴 리소스와 저장소 글꼴 파일의 바이트가 다르다 — 원본 그대로가 아니다.");
    }

    /// <summary>csproj 의 QuestPDF PackageReference Version(글자 판) 하나 — 없거나 · 여럿이거나 · 글자 판이 아니면 빨강(null 예외 0).</summary>
    private static string ReadCsprojQuestPdfVersion(params string[] relative)
    {
        var rel = string.Join('/', relative);
        var path = Path.Combine(new[] { FindRepoRoot() }.Concat(relative).ToArray());
        if (!File.Exists(path))
            throw new XunitException($"{rel} 이 없다.");
        var refs = XDocument.Load(path).Descendants()
            .Where(e => e.Name.LocalName == "PackageReference" && (string?)e.Attribute("Include") == "QuestPDF")
            .ToList();
        if (refs.Count != 1)
            throw new XunitException($"{rel} 의 QuestPDF PackageReference 가 {refs.Count} 개다 — 정확히 1 이어야 한다.");
        var version = ((string?)refs[0].Attribute("Version"))?.Trim();
        if (version is null || !PlainVersion.IsMatch(version))
            throw new XunitException(
                $"{rel} 의 QuestPDF Version 이 글자 판이 아니다(값: {version ?? "(없음)"} · 속성 · 범위 금지) — QuestPDF 판 바뀜 · 네이티브 목록 · 고지 다시 결재.");
        return version;
    }

    /// <summary>
    /// 실제 복원 결과(src/HitPan.API/obj/project.assets.json)에서 QuestPDF 판 · 원본 폴더를 읽는다(K11 · 환경값 추정 0).
    /// 반환 = (판, 원본 native 폴더 전체 경로 — 메시지에 찍지 않는다 · N-6).
    /// </summary>
    private static (string Version, string NativeDir) ReadAssetsQuestPdf()
    {
        const string rel = "src/HitPan.API/obj/project.assets.json";
        var path = Path.Combine(FindRepoRoot(), "src", "HitPan.API", "obj", "project.assets.json");
        if (!File.Exists(path))
            throw new XunitException($"{rel} 이 없다 — 복원(dotnet restore)이 먼저 돌아야 한다.");
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = doc.RootElement;
        if (!root.TryGetProperty("libraries", out var libs) || libs.ValueKind != JsonValueKind.Object)
            throw new XunitException($"{rel} 에 libraries 가 없다.");
        var quest = libs.EnumerateObject().Where(p => p.Name.StartsWith("QuestPDF/", StringComparison.Ordinal)).ToList();
        if (quest.Count != 1)
            throw new XunitException($"{rel} libraries 의 QuestPDF/ 키가 {quest.Count} 개다 — 정확히 1 이어야 한다.");
        var version = quest[0].Name["QuestPDF/".Length..];
        if (!quest[0].Value.TryGetProperty("path", out var libPathEl) || libPathEl.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(libPathEl.GetString()))
            throw new XunitException($"{rel} 의 QuestPDF/{version} 에 path 가 없다.");
        if (!root.TryGetProperty("packageFolders", out var folders) || folders.ValueKind != JsonValueKind.Object)
            throw new XunitException($"{rel} 에 packageFolders 가 없다.");
        var first = folders.EnumerateObject().Select(p => p.Name).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(first))
            throw new XunitException($"{rel} packageFolders 가 비었다.");
        var nativeDir = Path.Combine(first, libPathEl.GetString()!, "runtimes", "win-x64", "native");
        return (version, nativeDir);
    }

    [Fact(DisplayName = "F-2 ⑦ 출시 꼴 네이티브 6 = nuget 원본 (G-F2-G4) — QuestPDF runtimes/win-x64/native")]
    public void F2_G4_ShipformNativeSixMatchNugetOriginals()
    {
        var dir = ShipformDirOrOptOut("G4");
        if (dir is null) return;

        var (assetsVersion, originDir) = ReadAssetsQuestPdf();
        var apiVersion = ReadCsprojQuestPdfVersion("src", "HitPan.API", "HitPan.API.csproj");
        var appVersion = ReadCsprojQuestPdfVersion("src", "HitPan.Application", "HitPan.Application.csproj");
        if (assetsVersion != ApprovedQuestPdfVersion || apiVersion != ApprovedQuestPdfVersion || appVersion != ApprovedQuestPdfVersion)
            throw new XunitException(
                $"QuestPDF 판 바뀜 — 네이티브 목록 · 고지 다시 결재. 결재 판 {ApprovedQuestPdfVersion} / assets {assetsVersion} · " +
                $"HitPan.API.csproj {apiVersion} · HitPan.Application.csproj {appVersion}.");

        // 원본 폴더 표시는 꾸러미 폴더 기준 상대 경로만(N-6 · packageFolders 는 절대 경로).
        var originRel = $"{{꾸러미 폴더}}/questpdf/{assetsVersion}/runtimes/win-x64/native";
        if (!Directory.Exists(originDir))
            throw new XunitException($"nuget 원본 폴더가 없다 — {originRel} (복원 안 됨 · 판 바뀜).");

        var originNames = Directory.GetFiles(originDir, "*.dll").Select(Path.GetFileName).OfType<string>()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        var expected = ExpectedNativeSix.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        if (!originNames.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase))
            throw new XunitException(
                $"QuestPDF {assetsVersion} 원본 네이티브 목록이 6 이름과 다르다 — 판 바뀜 · 목록 다시 결재. 원본: [{string.Join(", ", originNames)}]");

        var problems = new List<string>();
        foreach (var n in ExpectedNativeSix)
        {
            var shipped = Path.Combine(dir, n);
            var origin = File.ReadAllBytes(Path.Combine(originDir, n));
            var originSha = Sha256Hex(origin);
            if (!File.Exists(shipped))
            {
                problems.Add($"{n}: 게시 폴더({{DIR}})에 없음");
                continue;
            }
            var bytes = File.ReadAllBytes(shipped);
            var sha = Sha256Hex(bytes);
            if (sha != originSha)
                problems.Add($"{n}: 기대 {origin.LongLength} B · {Short(originSha)} / 실측 {bytes.LongLength} B · {Short(sha)}");
            else
                _output.WriteLine($"[G-F2-G4] {n} · {bytes.LongLength} B · {sha}");
        }

        if (problems.Count > 0)
            throw new XunitException("출시 꼴 네이티브 6 이 nuget 원본과 다르다 — " + string.Join(" | ", problems));
    }
}
