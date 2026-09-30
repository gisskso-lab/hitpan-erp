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
/// <item>🟢 20260930작1 봉합 05(설계 §14-1 · 계약 §2 봉합 합의) — ⓐ <c>prev</c> 가 있고 <c>replaced-by</c> ≠ 지금 판이면 ② 도 거부
///   ⓑ 판 이력 <c>versions-seen.txt</c> 로 직전 설치 판을 알면 ② 는 그 판의 zip 만(수동 업데이트·설치 EXE 로 한 판이 끼어도 두 판 뒤를 고르지 않는다).</item>
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

        var prevDir = System.IO.Path.Combine(work, "prev");
        var prev = FindPrev(prevDir, current);
        if (prev is not null) { reason = SwapReasons.Ok; return prev; }

        // 20260930작1 봉합 05 ⓐ — 묵은 prev 거부: prev 가 있는데 지금 판이 그걸 밀어낸 판이 아니면(= 그 뒤 다른 경로로 한 판 더 갔다)
        //   워치독 zip 가운데 「지금보다 낮은 것」도 바로 앞 판이라는 보장이 없다 ⇒ ② 도 거부(병렬이슈 05 · 계약 §2 봉합 합의).
        if (IsStalePrev(prevDir, current)) return null;

        // ⓑ 판 이력 — 직전 설치 판을 알면 ② 는 그 판의 zip 만. 모르면 ⓐ 만(봉합 전과 같음 · 1.3.48 비상 경로 보존).
        var knownPrevious = TryReadPreviousVersion(System.IO.Path.Combine(work, LocalSwapLauncher.VersionsSeenFileName), current, out var p)
            ? p : null;
        var zip = FindStagingZip(watchdogStagingDir, current, knownPrevious);
        if (zip is not null) { reason = SwapReasons.Ok; return zip; }
        return null;
    }

    /// <summary>
    /// 봉합 05 ⓐ — <c>rollback\prev</c> 폴더가 있고 <c>replaced-by.txt</c> 가 지금 판이 아니다(없거나 못 읽어도 같다 — 막는 쪽).
    /// </summary>
    public static bool IsStalePrev(string prevDir, Version current)
    {
        if (!Directory.Exists(prevDir)) return false;
        return !(TryReadVersion(System.IO.Path.Combine(prevDir, "replaced-by.txt"), out var by) && by == current);
    }

    /// <summary>
    /// 봉합 05 ⓑ — 판 이력(<c>versions-seen.txt</c> · 계약 §2)에서 직전 설치 판을 읽는다. 읽기만(기록기는 갈래 M).
    /// 읽힌 줄의 <b>마지막 줄 판 = 지금 판</b>일 때만, 그 위로 올라가며 처음 만나는 다른 판. 그 밖(파일 없음 · 마지막 ≠ 지금 · 한 줄뿐)은 false(모름).
    /// 모양이 틀린 줄은 건너뛴다. 파일을 못 읽으면 예외가 그대로 나간다(호출부가 거부·기록 — <c>IsChainBlocked</c> 와 같은 폭).
    /// </summary>
    public static bool TryReadPreviousVersion(string ledgerPath, Version current, out Version previous)
    {
        previous = new Version(0, 0, 0);
        if (!File.Exists(ledgerPath)) return false;
        var seen = new List<Version>();
        foreach (var line in File.ReadAllLines(ledgerPath))
        {
            var cells = line.Trim().Split('|');
            if (cells.Length != 2 || !TryParse(cells[0], out var v)) continue;
            seen.Add(v);
        }
        if (seen.Count < 2 || seen[^1] != current) return false;
        for (var i = seen.Count - 2; i >= 0; i--)
        {
            if (seen[i] == current) continue;
            previous = seen[i];
            return true;
        }
        return false;
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

    /// <param name="knownPrevious">봉합 05 ⓑ — 판 이력으로 안 직전 설치 판. null 이면 종전 규칙(지금보다 낮은 것 가운데 가장 큰 것).</param>
    private static RollbackMaterial? FindStagingZip(string stagingDir, Version current, Version? knownPrevious)
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
            if (knownPrevious is not null && v != knownPrevious) continue; // 봉합 05 ⓑ — 직전 판이 아닌 zip 은 후보 아님
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
