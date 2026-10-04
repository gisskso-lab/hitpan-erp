using System.Globalization;
using System.Text;
using HitPan.API.Services.LocalRollback;

namespace HitPan.API.Services.LocalSwap;

/// <summary>
/// 20260930작1 봉합 05ⓑ(설계 §14-1 · 계약 §2 봉합 합의 · PM 결재 S-2) — 판 이력 기록기.
/// API 가 뜰 때 한 번, 자기 판을 <c>{app}\rollback\versions-seen.txt</c> 에 한 줄(<c>{M.m.b}|{UTC o}</c>) 덧붙인다.
/// 읽는 쪽 = <see cref="RollbackMaterialFinder.TryReadPreviousVersion"/>(L) · 일꾼 S1(K2) — 「마지막 줄 = 지금 판」이면 바로 위 다른 판이 직전 설치 판.
/// </summary>
/// <remarks>
/// <para>🔴 <b>기동을 막지 않는다</b>(S-2 조건): 설치 루트 없음 · 윈도 아님 · 폴더 문지기(<see cref="ISwapFolderGuard.EnsureSafe"/>) 실패 ·
/// 읽기·쓰기 실패는 전부 로그만 남기고 끝난다. <see cref="StartAsync"/> 는 어떤 경우에도 던지지 않는다.</para>
/// <para>덧붙이기만(고치기·지우기 0). 마지막으로 읽히는 줄의 판이 지금 판과 같으면 쓰지 않는다(재시작마다 줄이 늘지 않게).
/// 모양이 틀린 줄은 읽는 쪽과 같은 규칙으로 건너뛴다. 줄바꿈 <c>\r\n</c> · UTF-8(BOM 없음) · ASCII 만.</para>
/// <para>자료(업무 데이터) 무접촉 — 생성자에 자료 연결·저장소 0.</para>
/// </remarks>
public sealed class InstalledVersionLedger : IHostedService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly ILocalSwapEnvironment _env;
    private readonly ISwapFolderGuard _guard;
    private readonly ILogger<InstalledVersionLedger> _logger;

    public InstalledVersionLedger(ILocalSwapEnvironment env, ISwapFolderGuard guard, ILogger<InstalledVersionLedger> logger)
    {
        _env = env;
        _guard = guard;
        _logger = logger;
    }

    /// <summary>기동 때 한 번 — <see cref="Record"/>. 예외는 여기서 끝난다(기동 계속).</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            Record();
        }
        catch (Exception ex)
        {
            // 기록 실패가 기동 실패로 번지면 반려(S-2) — 무엇이 나든 경고만 남기고 계속 뜬다.
            _logger.LogWarning(ex, "[VersionLedger] 판 이력을 적다가 예상 못 한 오류 — 기록 없이 계속 뜹니다.");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>판 이력 한 줄을 덧붙인다. 적었으면 true · 안 적었으면(같은 판 · 설치 루트 없음 · 문지기·쓰기 실패) false.</summary>
    public bool Record()
    {
        if (!_env.IsWindows) return false;
        var app = _env.AppRoot;
        if (app is null)
        {
            _logger.LogInformation("[VersionLedger] 설치 루트를 찾지 못해 판 이력을 적지 않습니다(개발 실행 등).");
            return false;
        }
        if (!RollbackMaterialFinder.TryParse(_env.CurrentVersion, out var current))
        {
            _logger.LogWarning("[VersionLedger] 지금 판({Version})이 M.m.b 로 읽히지 않아 판 이력을 적지 않습니다.", _env.CurrentVersion);
            return false;
        }

        var work = Path.Combine(app, LocalSwapLauncher.WorkFolderName);
        try
        {
            _guard.EnsureSafe(work);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "[VersionLedger] 작업 폴더가 안전하지 않아 판 이력을 적지 않습니다(병렬이슈 01) — 기동은 계속합니다.");
            return false;
        }

        var path = Path.Combine(work, LocalSwapLauncher.VersionsSeenFileName);
        try
        {
            string? existing = File.Exists(path) ? File.ReadAllText(path, Utf8NoBom) : null;
            if (existing is not null && LastVersion(existing) == current) return false;

            var line = RollbackMaterialFinder.Format(current) + "|"
                       + _env.UtcNow.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) + "\r\n";
            // 앞 줄이 줄바꿈 없이 끝났으면(쓰다 끊긴 줄) 새 줄로 시작한다 — 두 줄이 한 줄로 붙어 둘 다 못 읽히지 않게.
            if (!string.IsNullOrEmpty(existing) && !existing.EndsWith('\n')) line = "\r\n" + line;
            File.AppendAllText(path, line, Utf8NoBom);
            _logger.LogInformation("[VersionLedger] 판 이력에 {Version} 을 적었습니다.", RollbackMaterialFinder.Format(current));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "[VersionLedger] 판 이력 파일을 읽거나 쓰지 못했습니다 — 기동은 계속합니다: {Path}", path);
            return false;
        }
    }

    /// <summary>읽히는 마지막 줄의 판(읽는 쪽 <see cref="RollbackMaterialFinder.TryReadPreviousVersion"/> 과 같은 줄 규칙). 없으면 null.</summary>
    private static Version? LastVersion(string text)
    {
        Version? last = null;
        foreach (var raw in text.Split('\n'))
        {
            var cells = raw.Trim().Split('|');
            if (cells.Length != 2 || !RollbackMaterialFinder.TryParse(cells[0], out var v)) continue;
            last = v;
        }
        return last;
    }
}
