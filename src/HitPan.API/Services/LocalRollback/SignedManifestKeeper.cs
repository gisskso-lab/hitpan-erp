using System.Text;
using HitPan.API.Services.LocalSwap;

namespace HitPan.API.Services.LocalRollback;

/// <summary>
/// 20260930작1 1.3.50 확대 갈래 N1 — 서명 확인된 업데이트 안내 파일(manifest) 저장본을
/// <c>{app}\rollback\manifests\{M.m.b}.json</c> 에 남기고 꺼내는 파일 담당(설계 §19-1 조각 A).
///
/// ■ 무엇을 모르나 — 워치독 형식(<c>UpdateManifest</c>)을 모른다. 글자로만 받고 글자로만 돌려준다.
///   직렬화·서명 재검증은 <see cref="ManualUpdate.WatchdogUpdateCoreAdapter"/> 안에서만 한다(어댑터 머리말 「워치독 형식은 이 파일 밖으로 나가지 않는다」).
///   서명을 새로 만들지 않는다 — 사본 보관일 뿐이고, 진위는 쓸 때 기존 검증기로 다시 본다(폴더 안 파일이 바뀌어도 위조 불가).
///
/// ■ 어디에 — 작1 작업 폴더(<see cref="LocalSwapLauncher.WorkFolderName"/>) 아래 <see cref="FolderName"/>.
///   쓰기 전 기존 문지기 <see cref="ISwapFolderGuard.EnsureSafe"/>(판 이력 기록기 <see cref="InstalledVersionLedger"/> 와 같은 입구).
///   🔴 워치독 폴더(staging) 쓰기 0.
///
/// ■ 보관 규칙(숫자 상수 0 · §9 「바로 이전 한 판」 몫만)
///   지금 판 이상 전부 + 지금 판 아래는 하나만 — 판 이력이 아는 직전 설치 판(<see cref="RollbackMaterialFinder.TryReadPreviousVersion"/> ·
///   되돌리기 세 번째 길이 같은 함수로 판을 정한다). 이력으로 못 정하면 저장본 중 지금 판 아래 가장 높은 것 하나. 나머지는 지운다.
///
/// ■ 실패 — 이 클래스의 쓰기는 예외를 던진다(문지기 거부·디스크). 부르는 쪽(어댑터)이 경고 한 줄로 받고 확인 결과는 그대로 둔다(#15 · #30).
///   지난 저장본 지우기 실패만 여기서 파일마다 경고 한 줄로 끝낸다(남김 자체는 이미 끝났다).
/// </summary>
public sealed class SignedManifestKeeper
{
    /// <summary>작업 폴더 아래 저장본 폴더 이름.</summary>
    public const string FolderName = "manifests";

    private const string Extension = ".json";
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly ILocalSwapEnvironment _env;
    private readonly ISwapFolderGuard _guard;
    private readonly ILogger<SignedManifestKeeper> _logger;

    public SignedManifestKeeper(ILocalSwapEnvironment env, ISwapFolderGuard guard, ILogger<SignedManifestKeeper> logger)
    {
        _env = env;
        _guard = guard;
        _logger = logger;
    }

    /// <summary><c>{appRoot}\rollback\manifests</c>. 설치 루트를 모르면 null.</summary>
    public static string? ManifestsDir(string? appRoot)
        => appRoot is null ? null : Path.Combine(appRoot, LocalSwapLauncher.WorkFolderName, FolderName);

    /// <summary>
    /// 판 <paramref name="version"/> 의 저장본을 쓴다(임시 파일 → 이름 바꾸기) · 쓰고 나면 보관 규칙으로 지난 것을 지운다.
    /// 반환 = 실제로 썼는가(윈도우 아님·설치 루트 없음·판 형식 불량 = false). 문지기 거부·쓰기 실패 = 예외(부르는 쪽이 받는다).
    /// </summary>
    public bool Keep(string version, string manifestJson)
    {
        if (!_env.IsWindows) return false;
        var dir = ManifestsDir(_env.AppRoot);
        if (dir is null)
        {
            _logger.LogDebug("[ManifestKeeper] 설치 루트를 찾지 못해 안내 파일을 남기지 않습니다(개발 실행 등).");
            return false;
        }
        if (!RollbackMaterialFinder.TryParse(version, out var v))
        {
            _logger.LogWarning("[ManifestKeeper] 안내 파일의 판({Version})이 M.m.b 로 읽히지 않아 남기지 않습니다.", version);
            return false;
        }

        // 판 이력 기록기와 같은 입구 — 작업 폴더 · 그 아래 저장본 폴더 둘 다(재분석 지점·넓은 권한 줄이면 던진다).
        _guard.EnsureSafe(Path.Combine(_env.AppRoot!, LocalSwapLauncher.WorkFolderName));
        _guard.EnsureSafe(dir);
        // 실제 문지기는 폴더를 만들어 두지만, 그 일을 문지기에 기대지 않는다(문지기 통과 뒤라 재분석 지점을 거쳐 만들 틈 0 · 있으면 아무 일 없음).
        Directory.CreateDirectory(dir);

        // 파일 이름은 받은 글자가 아니라 정규화한 판으로만 만든다(경로 조각이 끼어들 틈 0).
        var path = Path.Combine(dir, RollbackMaterialFinder.Format(v) + Extension);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, manifestJson, Utf8NoBom);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            DeleteQuietly(tmp, "임시 파일");
        }
        _logger.LogInformation("[ManifestKeeper] {Version} 판 안내 파일을 남겼습니다.", RollbackMaterialFinder.Format(v));

        Prune(dir);
        return true;
    }

    /// <summary>
    /// 판 <paramref name="version"/> 의 저장본 글자. 없음·윈도우 아님·설치 루트 없음·판 형식 불량 = null.
    /// 읽기 실패(IO·권한) = 경고 한 줄 + null. 진위는 이 글자를 받은 쪽이 기존 서명 검증기로 본다.
    /// </summary>
    public string? Read(string version)
    {
        if (!_env.IsWindows) return null;
        var dir = ManifestsDir(_env.AppRoot);
        if (dir is null || !RollbackMaterialFinder.TryParse(version, out var v)) return null;

        var path = Path.Combine(dir, RollbackMaterialFinder.Format(v) + Extension);
        try
        {
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "[ManifestKeeper] 안내 파일을 읽지 못했습니다: {Path}", path);
            return null;
        }
    }

    /// <summary>보관 규칙(머리말) — 지금 판 이상 전부 + 아래 하나. 지금 판을 못 읽으면 아무것도 지우지 않는다.</summary>
    private void Prune(string dir)
    {
        if (!RollbackMaterialFinder.TryParse(_env.CurrentVersion, out var current))
        {
            _logger.LogWarning("[ManifestKeeper] 지금 판({Version})이 M.m.b 로 읽히지 않아 지난 안내 파일을 정리하지 않습니다.", _env.CurrentVersion);
            return;
        }

        var stored = new List<(Version Version, string Path)>();
        foreach (var file in Directory.GetFiles(dir, "*" + Extension))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            // 정규화한 판 이름 그대로인 파일만 다룬다(다른 이름은 손대지 않는다).
            if (!RollbackMaterialFinder.TryParse(name, out var sv) || RollbackMaterialFinder.Format(sv) != name) continue;
            stored.Add((sv, file));
        }

        var below = stored.Where(s => s.Version < current).ToList();
        if (below.Count == 0) return;

        Version keep;
        if (TryPreviousFromLedger(current, out var prev))
            keep = prev;
        else
            keep = below.Max(s => s.Version)!;

        foreach (var s in below)
        {
            if (s.Version == keep) continue;
            DeleteQuietly(s.Path, "지난 안내 파일");
        }
    }

    private bool TryPreviousFromLedger(Version current, out Version previous)
    {
        previous = new Version(0, 0, 0);
        var app = _env.AppRoot;
        if (app is null) return false;
        var ledger = Path.Combine(app, LocalSwapLauncher.WorkFolderName, LocalSwapLauncher.VersionsSeenFileName);
        try
        {
            return RollbackMaterialFinder.TryReadPreviousVersion(ledger, current, out previous);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "[ManifestKeeper] 판 이력을 읽지 못해 저장본 중 가장 높은 이전 판을 남깁니다: {Path}", ledger);
            return false;
        }
    }

    private void DeleteQuietly(string path, string what)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "[ManifestKeeper] {What}을 지우지 못했습니다: {Path}", what, path);
        }
    }
}
