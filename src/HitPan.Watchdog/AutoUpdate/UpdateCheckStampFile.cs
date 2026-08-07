using System.Globalization;

namespace HitPan.Watchdog.AutoUpdate;

/// <summary>
/// 마지막 업데이트 확인 시각 기록 (작업지시서 20260807작2 N-10, 4안 혼합 · 사장님 결재 7건 2026-08-07).
///
/// ■ 무엇을 겪고서 생겼나
///   2026-08-07 사장님 백지환경 실측에서 게시한 1.2.55 를 워치독이 스스로 발견하지 못했다.
///   사장님이 직접 `sc stop` / `sc start` 를 치신 뒤에야 잡혔다.
///   원인: 확인 게이트가 "오늘 이미 했나"(DateOnly, 인메모리)였다. 그날 한 번 평가하면
///   그 뒤 새 버전이 올라와도 다음날까지 조회하지 않았고, 재시작하면 그 기억이 통째로 날아가
///   "우연히" 풀렸다. 채증 이벤트 500건 중 업데이트 관련 0건 — 침묵하는 return 이라 흔적도 없었다.
///
/// ■ 그래서 이 파일이 하는 일 — 목적을 오해하지 말 것
///   이 파일의 목적은 **"게이트를 유지"** 하는 것이 아니라 **"재시작 폭주에 상한을 두는"** 것이다.
///   · 워치독은 sc failure(5초) · Guardian(5분)이 되살리는 구조다. 크래시 루프에 빠진 PC 는
///     기동마다 manifest 를 1회씩 두드리는데, 인메모리 기억만으로는 그 반복에 상한이 없다.
///   · 반대로 N시간이 지났으면 **재시작 여부와 무관하게 확인한다.** 여기 적힌 시각이
///     확인을 막는 근거가 되어서는 안 된다. 3안(영속화) 단독이 반려된 이유가 그것이다.
///
/// ■ 왜 DB 가 아니라 파일인가 (결재-2)
///   ① 업데이트 발견은 DB 생존과 무관해야 한다 — DB 가 죽었을 때야말로 업데이트가 필요할 수 있다.
///   ② 워치독의 DB 접근은 mariadb.exe 프로세스 기동 방식이라, 코드리뷰서 P1-3(비밀번호 명령줄 노출)의
///      노출면을 이 기능 때문에 더 늘릴 이유가 없다.
///
/// ■ 왜 {app}\update-check.stamp 인가
///   UpdateLockFile 의 {app}\update.lock 과 같은 관례를 따른다. %LOCALAPPDATA% 는 워치독이 SYSTEM 이라
///   C:\Windows\System32\config\systemprofile\... 로 숨어 CS 도 고객도 못 찾는다. {app} 루트는
///   db.conf 가 있는 곳이라 사람이 열어보고, 백신 예외·ACL 이 이미 잡혀 있다(헌법 #31).
///   부수 이득: CS 가 파일 하나 열어 "마지막으로 언제 확인했나"를 즉시 안다. 종전엔 프로세스 안에만 있었다.
///
/// ■ 판정 실패 시 어느 쪽으로 기우나 — fail-open (설계서 A-2)
///   읽기 실패 · 해석 불가 · 미래 시각은 전부 **"확인한 적 없음"** 으로 본다. 즉 즉시 확인한다.
///   안전측은 "덜 확인"이 아니라 "더 확인"이다. 파일이 깨져 영영 업데이트를 못 받는 쪽이 최악이다.
///   ※ manifest **서명 검증**은 정반대(fail-closed — UpdateClient). 두 방향을 혼동하지 말 것.
///   쓰기 실패도 업데이트를 막지 않는다. 로그만 남긴다(헌법 #15).
///
/// 헌법 정합: #1(추가만) / #15(침묵 금지) / #19 / #30(고객 PC 자가 회복 · 본사 의존 0) / #34(정식 완성도).
/// </summary>
public sealed class UpdateCheckStampFile
{
    private readonly ILogger<UpdateCheckStampFile> _logger;
    private readonly string _path;

    public UpdateCheckStampFile(ILogger<UpdateCheckStampFile> logger)
    {
        _logger = logger;
        _path = ResolvePath();
    }

    public string Path => _path;

    /// <summary>
    /// {app}\update-check.stamp 경로를 정한다. UpdateLockFile.ResolvePath 와 동일한 설치 구조 전제:
    ///   워치독 EXE = {app}\watchdog\ → 기록 = {app}\update-check.stamp
    /// 상위 폴더를 못 쓰는 비정상 배치(테스트 등)에서는 실행 폴더로 폴백한다.
    /// </summary>
    private static string ResolvePath()
    {
        var baseDir = AppContext.BaseDirectory;
        var parent = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, ".."));
        var target = Directory.Exists(parent) ? parent : baseDir;
        return System.IO.Path.Combine(target, "update-check.stamp");
    }

    /// <summary>
    /// 마지막으로 확인을 마친 시각(UTC). 없거나 읽을 수 없거나 말이 안 되면 null 을 돌려준다.
    /// null = "확인한 적 없음" = 즉시 확인해야 함(fail-open).
    /// </summary>
    public DateTime? ReadLastCheckedUtc()
    {
        try
        {
            if (!File.Exists(_path))
            {
                // 첫 기동·파일 삭제. 사고가 아니라 정상 상태 중 하나다 — 조용히 "확인한 적 없음".
                return null;
            }

            var raw = File.ReadAllText(_path).Trim();
            if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var stamp))
            {
                _logger.LogWarning("[Update/Stamp] 기록을 해석할 수 없어 '확인한 적 없음'으로 봅니다(즉시 재확인): '{Raw}' — {Path}", raw, _path);
                return null;
            }

            var utc = stamp.Kind == DateTimeKind.Utc ? stamp : stamp.ToUniversalTime();

            // 시계 변경·NTP 보정·파일 손상. 미래에 끝난 확인이란 있을 수 없다(설계서 A-3).
            //   여기서 걸러내지 않으면 그 미래 시각이 지날 때까지 업데이트가 영영 막힌다.
            if (utc > DateTime.UtcNow)
            {
                _logger.LogWarning("[Update/Stamp] 기록 시각이 미래입니다({Stamp:o}, 현재 {Now:o}) — 시계 변경·손상으로 보고 '확인한 적 없음'으로 처리합니다.",
                    utc, DateTime.UtcNow);
                return null;
            }

            return utc;
        }
        catch (Exception ex)
        {
            // 헌법 #15 + fail-open: 판정 불능이면 '확인한 적 없음'. 잘못 판정해도 manifest 1KB 를 한 번 더
            //   조회할 뿐이고, 반대(영영 안 확인)는 고객이 옛 버전에 고정되는 사고다.
            _logger.LogWarning(ex, "[Update/Stamp] 기록 읽기 실패 — '확인한 적 없음'으로 간주합니다(즉시 재확인): {Path}", _path);
            return null;
        }
    }

    /// <summary>
    /// 확인을 마친 시각을 기록한다.
    /// 🔴 실패해도 예외를 던지지 않는다 — 이 기록은 보조 장치이지 업데이트의 전제조건이 아니다.
    ///    쓰기가 막힌다고 업데이트를 멈추면, 디스크·권한 사고 하나가 고객을 옛 버전에 영구히 고정시킨다.
    /// </summary>
    public void WriteCheckedNow(DateTime utcNow)
    {
        try
        {
            File.WriteAllText(_path, utcNow.ToString("O", CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            // 헌법 #15: 침묵 금지. 못 남기면 재시작 폭주 상한만 사라진다(정상 PC 에는 무해).
            //   업데이트 자체는 인메모리 기억으로 계속 게이트되므로 진행에 지장 없다.
            _logger.LogWarning(ex, "[Update/Stamp] 기록 저장 실패 — 재시작 시 확인 간격 상한이 적용되지 않습니다(업데이트는 그대로 진행): {Path}", _path);
        }
    }
}
