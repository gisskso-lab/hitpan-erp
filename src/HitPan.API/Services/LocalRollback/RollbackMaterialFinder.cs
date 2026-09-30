using System.IO.Compression;
using HitPan.API.Services.LocalSwap;

namespace HitPan.API.Services.LocalRollback;

/// <summary>되돌리기 재료 하나 — 종류·경로·그 판.</summary>
public sealed record RollbackMaterial(string Kind, string Path, string Version);

/// <summary>
/// 「바로 이전 한 판」 재료 판정 — 설계 §3·§13-1 · 개정 결재(워치독 무변경 ⇒ 재료 ①·② 둘뿐) · 병렬이슈 02.
/// 읽기만 한다(파일 존재·작은 글자 파일·zip 목차). 자료(업무 데이터)에는 닿지 않는다.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>연쇄 차단 — <c>{app}\rollback\rolled-back.txt</c> 첫 칸 = 지금 판이면 거부(되돌린 판에서 또 되돌리기 금지 · 다음 업데이트 성공이 푼다).</item>
/// <item>① <c>{app}\rollback\prev</c> — <c>replaced-by.txt</c> = 지금 판일 때만(그 사이 자동 업데이트가 한 번 더 갔으면 바로 앞 판이 아니다).</item>
/// <item>② 워치독 <c>staging\hitpan-{V}.zip</c> — V = 지금 판보다 작은 것 가운데 <b>가장 큰 판 하나</b>.
///   ⚠️ 이력 대조(설계 §3-3 「V 성공 → 현재 성공」)는 자료 읽기라 이 모듈이 하지 않는다 — 대신 <b>지금 판의 zip 도 있어야</b>
///   (= 지금 판이 워치독 자동 업데이트로 들어왔다는 파일 증거) 받는다. 설치 EXE 로 깐 판은 zip 이 없어 거부된다(설계 §3-3 과 같은 결과).</item>
/// </list>
/// </remarks>
public static class RollbackMaterialFinder
{
    public const string ChainMarkFileName = "rolled-back.txt";

    /// <summary>재료 ① 이동에 드는 여유(같은 볼륨 이름 바꾸기라 거의 0 — 로그·표식 몫).</summary>
    public const long PrevHeadroomBytes = 512L * 1024 * 1024;

    /// <summary>
    /// 재료를 찾는다. 없으면 null 과 사유 코드(<see cref="SwapReasons"/>).
    /// </summary>
    public static RollbackMaterial? Find(string appRoot, string watchdogStagingDir, string currentVersion, out string reason)
    {
        reason = SwapReasons.NoPreviousVersion;
        if (!TryParse(currentVersion, out var current)) { reason = SwapReasons.RequestInvalid; return null; }

        var work = System.IO.Path.Combine(appRoot, LocalSwapLauncher.WorkFolderName);
        if (IsChainBlocked(System.IO.Path.Combine(work, ChainMarkFileName), current))
        {
            reason = SwapReasons.RollbackChainBlocked;
            return null;
        }

        var prev = FindPrev(System.IO.Path.Combine(work, "prev"), current);
        if (prev is not null) { reason = SwapReasons.Ok; return prev; }

        var zip = FindStagingZip(watchdogStagingDir, current);
        if (zip is not null) { reason = SwapReasons.Ok; return zip; }
        return null;
    }

    /// <summary>
    /// 설계 §1 P-e — 저장 공간. ① = 여유 512MB · ② = zip 풀린 크기 × 2 + 512MB(⚠️ 기준값 PM 결재 · 설계 §11).
    /// </summary>
    public static long RequiredFreeBytes(RollbackMaterial material)
    {
        if (material.Kind != SwapMaterialKinds.StagingZip) return PrevHeadroomBytes;
        long total = 0;
        using (var archive = ZipFile.OpenRead(material.Path))
        {
            foreach (var e in archive.Entries) total += e.Length;
        }
        return total * 2 + PrevHeadroomBytes;
    }

    private static bool IsChainBlocked(string markPath, Version current)
    {
        if (!File.Exists(markPath)) return false;
        var first = File.ReadAllText(markPath).Trim().Split('|')[0];
        // 못 읽는 표식은 막지 않는다 — 판이 다르면 이미 다음 업데이트가 지나간 것이다
        return TryParse(first, out var v) && v == current;
    }

    private static RollbackMaterial? FindPrev(string prev, Version current)
    {
        if (!Directory.Exists(prev)) return null;
        foreach (var part in new[] { "api", "web", "watchdog" })
            if (!Directory.Exists(System.IO.Path.Combine(prev, part))) return null;
        if (!File.Exists(System.IO.Path.Combine(prev, "sha256.txt"))) return null;
        if (!TryReadVersion(System.IO.Path.Combine(prev, "version.txt"), out var v)) return null;
        if (!TryReadVersion(System.IO.Path.Combine(prev, "replaced-by.txt"), out var by)) return null;
        if (by != current || v >= current) return null;
        return new RollbackMaterial(SwapMaterialKinds.Prev, prev, Format(v));
    }

    private static RollbackMaterial? FindStagingZip(string stagingDir, Version current)
    {
        if (!Directory.Exists(stagingDir)) return null;
        Version? best = null;
        string? bestPath = null;
        var currentZipSeen = false;
        foreach (var file in Directory.EnumerateFiles(stagingDir, "hitpan-*.zip"))
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(file);
            if (!TryParse(name["hitpan-".Length..], out var v)) continue;
            if (v == current) { currentZipSeen = true; continue; }
            if (v < current && (best is null || v > best)) { best = v; bestPath = file; }
        }
        if (best is null || bestPath is null || !currentZipSeen) return null;
        return new RollbackMaterial(SwapMaterialKinds.StagingZip, bestPath, Format(best));
    }

    private static bool TryReadVersion(string path, out Version v)
    {
        v = new Version(0, 0, 0);
        return File.Exists(path) && TryParse(File.ReadAllText(path).Trim(), out v);
    }

    /// <summary><c>M.m.b</c> 세 칸만 본다(FileVersion 네 번째 칸 무시 — 워치독 비교와 같은 폭).</summary>
    public static bool TryParse(string? text, out Version v)
    {
        v = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var p = text.Trim().Split('.');
        if (p.Length < 3) return false;
        if (!int.TryParse(p[0], out var a) || !int.TryParse(p[1], out var b) || !int.TryParse(p[2], out var c)) return false;
        if (a < 0 || b < 0 || c < 0) return false;
        v = new Version(a, b, c);
        return true;
    }

    public static string Format(Version v) => $"{v.Major}.{v.Minor}.{v.Build}";
}
