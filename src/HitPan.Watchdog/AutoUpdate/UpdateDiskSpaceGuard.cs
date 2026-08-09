namespace HitPan.Watchdog.AutoUpdate;

/// <summary>
/// 업데이트 적용 전 디스크 여유공간 검사 (작업지시서 20260809작2 P0-1 · 사장님 결재 2026-08-09).
///
/// ■ 무엇을 겪고서 생겼나 — 겪기 **전에** 만든다
///   2026-08-07 배포 종단 최종검증에서 **3팀이 독립으로 같은 곳을 지목**했다.
///   실측(2026-08-09): `grep -rniE "DriveInfo|AvailableFreeSpace|여유공간" src/` → **0건**.
///   업데이트가 디스크를 세 번 쓰는데(다운로드·백업·해제) 여유공간을 보는 코드가 하나도 없었다.
///
/// ■ 왜 P0 인가 — 안전망이 같이 무너진다
///   디스크가 차는 순간의 위험은 "업데이트 실패"가 아니다. **롤백도 디스크를 쓴다는 것**이다.
///   · 백업 단계에서 차면: RunPreUpdateBackupAsync 가 실패하고 기존 게이트가 업데이트를 차단한다.
///     이 경로는 이미 막혀 있다(UpdateOrchestrator §3 — 백업 실패 = 물리적 차단).
///   · 🔴 **해제 단계에서 차면**: 백업은 성공했는데 옛 버전은 지워졌고 새 버전은 안 풀렸다.
///     그리고 그 복구에 필요한 디스크가 없다. **가장 나쁜 자리에서 멈춘다.**
///   ⇒ 그래서 검사 지점은 **다운로드 전**이다. 받고 나서 알면 이미 패키지 크기만큼 썼다.
///
/// ■ 필요량을 어떻게 잡는가 (사장님 결재 — 안전계수)
///   manifest 에 `sizeBytes` 가 **이미 있다**(실측: 1.2.55 = 102,720,238 bytes).
///   새 통신 없이 계산할 수 있다. 필요량은 세 몫의 합이다:
///     ① 패키지 ZIP 다운로드      = sizeBytes
///     ② 압축 해제본             ≈ sizeBytes × <see cref="ExtractionFactor"/>
///     ③ 백업 + 롤백 여유(안전분) = <see cref="SafetyMarginBytes"/>
///   ②의 계수는 ZIP 압축률을 모르므로 보수적으로 잡는다. 실제보다 크게 잡아 막는 쪽이
///   작게 잡아 중간에 터지는 쪽보다 안전하다 — **여기서의 안전측은 "덜 시작"이다.**
///
/// ■ 판정 실패 시 어느 쪽으로 기우나 — 🔴 fail-**open** (중요)
///   드라이브 정보를 못 읽으면(권한·네트워크 드라이브·예외) **업데이트를 막지 않는다.**
///   · 근거: 헌법 #30(고객 PC 자가 회복). 이 검사는 **보조 안전장치**이지 업데이트의 전제가 아니다.
///     검사기 자체의 결함으로 전 고객 업데이트가 멈추는 것이 훨씬 나쁘다.
///   · 같은 이유로 UpdateCheckStampFile 도 fail-open 이다. 반대로 manifest **서명 검증**은
///     fail-closed 다(UpdateClient). **두 방향을 혼동하지 말 것.**
///   ⇒ 즉 이 검사는 **"확실히 부족할 때만" 막는다.** 모르면 진행시킨다.
///
/// ■ 여기서 하지 않는 것
///   · 디스크 정리·임시파일 삭제 — 워치독이 고객 디스크를 지우는 것은 별개 결재 사안이다.
///   · 백업 파일 회전 — 이미 별건으로 존재한다(7/29 디스크 봉합).
///   · 부족 시 재시도 스케줄 — 다음 확인 주기(기본 60분)에 자연히 다시 평가된다.
///
/// 헌법 정합: #1(추가만) / #15(침묵 금지 — 막을 때도 통과할 때도 남긴다) / #19 / #30 / #34.
/// </summary>
public sealed class UpdateDiskSpaceGuard
{
    private readonly ILogger<UpdateDiskSpaceGuard> _logger;

    /// <summary>
    /// 압축 해제본 몫의 계수. ZIP 압축률을 모르므로 보수적으로 잡는다.
    /// 2.0 = "풀면 원본의 2배까지 될 수 있다"고 가정. 실측 기준이 생기면 낮출 수 있으나,
    /// 낮추는 것은 <b>막는 힘을 약하게 하는 변경</b>이므로 근거 없이 건드리지 말 것.
    /// </summary>
    public const double ExtractionFactor = 2.0;

    /// <summary>
    /// 백업 + 롤백 + OS 여유를 위한 고정 안전분(5GB).
    /// 백업 크기는 고객사 DB 크기에 비례해 미리 알 수 없으므로 고정값으로 잡는다.
    /// 이 값이 0 이 되면 "패키지는 받았는데 백업할 자리가 없는" 상태를 못 막는다.
    /// </summary>
    public const long SafetyMarginBytes = 5L * 1024 * 1024 * 1024;

    public UpdateDiskSpaceGuard(ILogger<UpdateDiskSpaceGuard> logger) => _logger = logger;

    /// <summary>
    /// 이 업데이트를 시작해도 되는지 판정한다.
    /// </summary>
    /// <param name="packageSizeBytes">manifest 의 sizeBytes. 0 이하면 크기 미상으로 보고 패키지 몫을 0 으로 둔다.</param>
    /// <param name="targetPath">설치 대상 경로. null 이면 워치독 실행 위치 기준.</param>
    /// <returns>
    /// 진행해도 되면 true. <b>판정 불가일 때도 true</b>(fail-open — 위 §판정 실패 참조).
    /// 확실히 부족할 때만 false.
    /// </returns>
    public bool HasEnoughSpace(long packageSizeBytes, string? targetPath = null)
    {
        var required = CalculateRequiredBytes(packageSizeBytes);

        try
        {
            var path = targetPath ?? AppContext.BaseDirectory;
            var root = Path.GetPathRoot(Path.GetFullPath(path));

            if (string.IsNullOrWhiteSpace(root))
            {
                // 경로에서 드라이브를 못 뽑았다. 막지 않는다(fail-open).
                _logger.LogWarning("[Update] 디스크 검사 — 대상 경로에서 드라이브를 확인하지 못했다({Path}). " +
                                   "검사를 건너뛰고 진행한다(안전장치 미작동).", path);
                return true;
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                _logger.LogWarning("[Update] 디스크 검사 — 드라이브 {Root} 가 준비 상태가 아니다. " +
                                   "검사를 건너뛰고 진행한다(안전장치 미작동).", root);
                return true;
            }

            var free = drive.AvailableFreeSpace;
            if (free >= required)
            {
                // 통과할 때도 남긴다(헌법 #15). 부족해서 막힌 뒤에야 로그를 찾으면 이미 늦다.
                _logger.LogInformation("[Update] 디스크 검사 통과 — 여유 {Free}, 필요 {Required} (드라이브 {Root})",
                    Human(free), Human(required), root);
                return true;
            }

            // 🔴 확실히 부족하다. 여기서만 막는다.
            _logger.LogError("[Update] 🛑 디스크 여유공간 부족 — 업데이트를 시작하지 않는다. " +
                             "여유 {Free} < 필요 {Required} (드라이브 {Root}). " +
                             "부족한 채로 시작하면 옛 버전을 지운 뒤 멈추고, 롤백에 쓸 공간도 없다.",
                Human(free), Human(required), root);
            return false;
        }
        catch (Exception ex)
        {
            // fail-open. 검사기 결함으로 전 고객 업데이트가 멈추는 것이 더 나쁘다(헌법 #30).
            _logger.LogWarning(ex, "[Update] 디스크 검사 중 예외 — 검사를 건너뛰고 진행한다(안전장치 미작동). 필요 {Required}",
                Human(required));
            return true;
        }
    }

    /// <summary>필요 바이트 = 패키지 + 해제본 + 안전분. 크기 미상(0 이하)이면 안전분만 요구한다.</summary>
    public static long CalculateRequiredBytes(long packageSizeBytes)
    {
        if (packageSizeBytes <= 0)
            return SafetyMarginBytes;

        // double 경유 시 대형 패키지에서 오차가 날 수 있으나, 여기서는 보수적 상향이라 무해하다.
        var extraction = (long)(packageSizeBytes * ExtractionFactor);
        return packageSizeBytes + extraction + SafetyMarginBytes;
    }

    /// <summary>로그용 사람이 읽는 크기. CS 가 로그를 그대로 읽으므로 바이트 원시값을 쓰지 않는다.</summary>
    private static string Human(long bytes)
    {
        if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):F1}GB";
        if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):F1}MB";
        if (bytes >= 1L << 10) return $"{bytes / (double)(1L << 10):F1}KB";
        return $"{bytes}B";
    }
}
