using System.Globalization;

namespace HitPan.Watchdog.AutoUpdate;

/// <summary>
/// 업데이트 진행 표식 (작업지시서 20260715작1 W4-1, 사장님 결재 2026-07-16 / CTO 재심사 Q2).
///
/// ■ 무엇을 위한 표식인가
///   업데이트는 파일 교체를 위해 keepalive 예약작업을 잠시 꺼야 한다(안 그러면 1분마다 ERP 가 되살아나
///   파일을 잠근다). 그런데 워치독이 그 사이 죽으면 keepalive 가 꺼진 채로 남아 ERP 가 영영 안 뜬다
///   — 업데이트가 고객 ERP 를 영구 정지시키는 최악의 사고다.
///   그래서 워치독은 기동할 때마다 "keepalive 가 꺼져 있는데 업데이트 중이 아니면 즉시 켠다"를 점검한다.
///   이 표식이 그 "업데이트 중인가"의 판단 근거다.
///
/// ■ 왜 DB 가 아니라 파일인가 (CTO 적발)
///   DB 에 두면 워치독이 스왑 중 죽었을 때 표식이 그대로 남는다 → 자가점검이 영원히 "업데이트 중이구나"
///   하고 keepalive 를 안 켠다 → 3중 보장이 1중으로 무너진다. 파일 + TTL 이라야 스스로 만료된다.
///
/// ■ 왜 {app}\update.lock 인가 (CTO 처방)
///   %LOCALAPPDATA% 는 워치독이 SYSTEM 이라 C:\Windows\System32\config\systemprofile\... 로 숨는다
///   — CS 도 고객도 못 찾는다. {app} 루트는 db.conf 가 있는 곳이라 사람이 보고, 백신 예외·ACL 이 이미
///   설정돼 있다(헌법 #31).
///
/// ■ 왜 mtime 이 아니라 파일 내용의 UTC 인가 (CTO 처방)
///   시계 변경은 공격이 아니라 사고로 깨진다. NTP 동기화·수동 시간 변경으로 mtime 이 미래가 되면
///   TTL 이 영원히 만료되지 않아 자가점검이 무력화된다.
///   판정 규칙 = "만료됐거나, 말이 안 되면(미래 표식) 푼다" — 실패 시 기본값은 항상 '푼다'(fail-safe).
///
/// 헌법 정합: #15(침묵 금지) / #20(워크플로우 끊김 0) / #30(고객 PC 자가 회복) / #34(정식 완성도).
/// </summary>
public sealed class UpdateLockFile
{
    /// <summary>
    /// 표식 유효 기간. 이보다 오래된 표식은 "업데이트가 비정상 종료된 흔적"으로 보고 무시한다.
    /// 정상 업데이트(다운로드 완료 후 교체·재기동)는 수 분 내에 끝나므로 15분이면 충분히 넉넉하다.
    /// </summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    private readonly ILogger<UpdateLockFile> _logger;
    private readonly string _path;

    public UpdateLockFile(ILogger<UpdateLockFile> logger)
    {
        _logger = logger;
        _path = ResolvePath();
    }

    public string Path => _path;

    /// <summary>
    /// {app}\update.lock 경로를 정한다. DbConfReader 와 동일한 설치 구조 전제:
    ///   워치독 EXE = {app}\watchdog\ → 표식 = {app}\update.lock
    /// 상위 폴더를 못 쓰는 비정상 배치(테스트 등)에서는 실행 폴더로 폴백한다.
    /// </summary>
    private static string ResolvePath()
    {
        var baseDir = AppContext.BaseDirectory;
        var parent = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, ".."));
        var target = Directory.Exists(parent) ? parent : baseDir;
        return System.IO.Path.Combine(target, "update.lock");
    }

    /// <summary>업데이트 시작을 표시한다. 실패해도 예외를 던지지 않는다(표식은 보조 장치다).</summary>
    public void Acquire(string version)
    {
        try
        {
            var body = $"{DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)}|{version}";
            File.WriteAllText(_path, body);
            _logger.LogInformation("[Update/Lock] 업데이트 진행 표식 생성 ({V}) → {Path}", version, _path);
        }
        catch (Exception ex)
        {
            // 헌법 #15: 침묵 금지. 표식을 못 남기면 자가점검이 "업데이트 중"을 몰라 keepalive 를
            //   조기에 되살릴 수 있다(교체 중 파일 잠금 → 업데이트 실패). 사고는 아니지만 흔적은 남긴다.
            _logger.LogWarning(ex, "[Update/Lock] 표식 생성 실패 — 자가점검이 업데이트 중임을 모를 수 있습니다: {Path}", _path);
        }
    }

    /// <summary>업데이트 종료를 표시한다(성공·실패·롤백 무관 — 끝났으면 지운다).</summary>
    public void Release()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
                _logger.LogInformation("[Update/Lock] 업데이트 진행 표식 해제 → {Path}", _path);
            }
        }
        catch (Exception ex)
        {
            // 지우기 실패는 TTL 이 덮어준다(15분 후 자동 무효). 흔적만 남긴다.
            _logger.LogWarning(ex, "[Update/Lock] 표식 삭제 실패 — {Ttl}분 후 자동 만료됩니다: {Path}", Ttl.TotalMinutes, _path);
        }
    }

    /// <summary>
    /// 지금 업데이트가 진행 중이라고 볼 수 있는가.
    ///
    /// false 를 반환하는 경우(= 자가점검이 keepalive 를 되살려도 되는 상태):
    ///   · 표식 없음 — 정상
    ///   · 표식이 TTL 초과 — 업데이트가 비정상 종료된 흔적
    ///   · 표식 시각이 미래 — 시계가 바뀌었거나 파일이 손상됨("말이 안 되면 푼다")
    ///   · 표식을 읽을 수 없음 — 판정 불능 시 '푼다'(ERP 가 안 뜨는 것보다 낫다)
    /// </summary>
    public bool IsUpdateInProgress()
    {
        try
        {
            if (!File.Exists(_path)) return false;

            var raw = File.ReadAllText(_path);
            var stampText = raw.Split('|', 2)[0];
            if (!DateTime.TryParse(stampText, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var written))
            {
                _logger.LogWarning("[Update/Lock] 표식 내용을 해석할 수 없어 무시합니다(업데이트 중 아님으로 판정): '{Raw}'", raw);
                return false;
            }

            var now = DateTime.UtcNow;
            if (written > now)
            {
                // 시계 변경·파일 손상. 미래에 시작된 업데이트란 있을 수 없다.
                _logger.LogWarning("[Update/Lock] 표식 시각이 미래입니다({Written:o}, 현재 {Now:o}) — 손상으로 보고 무시합니다.", written, now);
                return false;
            }

            var age = now - written;
            if (age > Ttl)
            {
                _logger.LogWarning("[Update/Lock] 표식이 {Age:N0}분 전 것으로 유효기간({Ttl:N0}분)을 넘겼습니다 — 비정상 종료된 업데이트로 보고 무시합니다.",
                    age.TotalMinutes, Ttl.TotalMinutes);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            // fail-safe: 판정 불능 = '업데이트 중 아님'. 잘못 판정해도 keepalive 가 켜질 뿐이고,
            //   반대(영원히 안 켜짐)는 ERP 영구 정지다. 안전한 쪽으로 기운다.
            _logger.LogWarning(ex, "[Update/Lock] 표식 판정 실패 — 업데이트 중 아님으로 간주합니다: {Path}", _path);
            return false;
        }
    }
}
