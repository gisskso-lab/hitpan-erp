using System.Reflection;

namespace HitPan.Watchdog;

/// <summary>
/// 워치독 버전 단일출처 (작업지시서 20260715작1 W4-0, 사장님 결재 2026-07-16).
///
/// ■ 왜 이 클래스가 생겼나
///   종전엔 워치독 버전이 세 곳에 각각 "1.0.0" 으로 손으로 적혀 있었다
///   (HealthProbe 진단 JSON / Worker 메타핑 / MetaPingPayload 기본값).
///   그런데 자동 업데이트는 "현재 설치 버전 vs manifest 버전" 비교로 동작하므로,
///   버전이 고정 문자열이면 ① 업데이트 판정이 무의미해지고
///   ② 본사가 보는 고객사 버전이 전부 사실과 달라진다(설치본은 1.2.xx 인데 1.0.0 으로 보고).
///   실제로 2026-07-16 조사에서 "인스톨러는 1.2.13 을 굽는데 메타핑은 1.0.0" 상태가 확인됐다.
///
/// ■ 단일출처 = 어셈블리 버전 (Directory.Build.props 의 HitPanVersion 에서 스탬핑)
///   Worker.GetCurrentVersion() 이 쓰던 정답 패턴을 공용으로 승격한 것이다.
///   업데이트 판정(UpdateClient)과 진단·메타핑이 반드시 같은 값을 봐야 하므로 출처가 하나여야 한다.
///
/// ■ 형식: Major.Minor.Build 3자리 — manifest version 과 동일 형식(예: "1.2.34").
///   AssemblyVersion 은 4자리(x.y.z.0)로 스탬핑되지만 Revision 은 버리고 3자리로 맞춘다.
///
/// 헌법 정합: #19(정합성·무결성) / #15(진단이 거짓말하면 추적 불가) / #34(정식 완성도).
/// </summary>
public static class VersionInfo
{
    /// <summary>
    /// 현재 워치독 설치 버전 ("Major.Minor.Build").
    /// 어셈블리에서 버전을 못 읽는 비정상 상황에서는 "0.0.0" 을 반환한다
    /// — 0.0.0 은 어떤 manifest 버전보다도 낮아, SemVer 비교에서 "새 버전 있음"으로 판정된다.
    ///   즉 판정 불능 시 업데이트를 막지 않는 쪽(fail-open)이 아니라, 사람이 이상을 알아채는 값이다.
    /// </summary>
    public static string Current
    {
        get
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var v = asm.GetName().Version;
            return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }
}
