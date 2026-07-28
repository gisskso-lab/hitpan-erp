using System.Reflection;

namespace HitPan.API;

/// <summary>
/// ERP(API) 버전 단일출처 (작업지시서 20260715작1 W4-0, 사장님 결재 2026-07-16).
///
/// ■ 왜 이 클래스가 생겼나
///   종전엔 HealthController 가 "1.0.0-beta" 를 손으로 적어 응답했다.
///   그런데 워치독의 업데이트 검증(W4-5)이 바로 그 /health 응답의 version 을 읽어
///   "새 버전으로 교체됐나"를 판정한다. 리터럴이면 파일 교체·재시작이 전부 성공해도
///   판정에서 "1.0.0-beta"(= 신 버전 아님)를 읽어 정상 업데이트를 실패로 오판하고 롤백한다.
///   즉 이 하드코딩 하나 때문에 자동 업데이트가 단 한 번도 성공할 수 없었다(CTO 적발 B-1).
///
/// ■ 단일출처 = 어셈블리 버전 (Directory.Build.props 의 HitPanVersion 에서 스탬핑)
///   AuthController.GetCurrentErpVersion() 이 쓰던 정답 패턴을 공용으로 승격한 것이다.
///   워치독 HitPan.Watchdog.VersionInfo 와 동일 형식(Major.Minor.Build)을 맞춘다 —
///   두 프로세스가 같은 형식으로 말해야 manifest 버전과 대조가 성립한다.
///
/// 헌법 정합: #19(정합성·무결성) / #15(흔적이 거짓말하면 추적 불가) / #34(정식 완성도).
/// </summary>
public static class VersionInfo
{
    /// <summary>
    /// 현재 ERP 설치 버전 ("Major.Minor.Build", 예: "1.2.34").
    ///
    /// ■ 왜 GetEntryAssembly() 가 아니라 typeof(VersionInfo).Assembly 인가 (2026-07-16, 검증팀 F-3 적발)
    ///   GetEntryAssembly() 는 "지금 프로세스를 시작시킨 어셈블리"를 반환한다. 워치독 쪽에서 이 방식이
    ///   테스트 호스트(testhost 15.0.0)를 읽어 검증을 무력화한 사례가 실측으로 확인됐다.
    ///   API 는 schtasks 가 HitPan.API.exe 를 직접 실행하므로 현재는 문제가 없으나, 단일파일 publish·
    ///   호스트 변경 같은 환경 변화에 답이 흔들리면 안 되는 값이다(이 값으로 업데이트 성공을 판정한다).
    ///   "이 코드가 담긴 어셈블리"를 직접 지목해 환경과 무관하게 같은 답을 준다.
    ///
    /// ■ 못 읽으면 "0.0.0" — 사람이 이상을 즉시 알아채는 값이다.
    /// </summary>
    public static string Current
    {
        get
        {
            var v = typeof(VersionInfo).Assembly.GetName().Version;
            return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }
}
