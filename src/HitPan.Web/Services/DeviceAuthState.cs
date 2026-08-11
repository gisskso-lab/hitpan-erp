namespace HitPan.Web.Services;

/// 이 기기가 인증됐는지를 화면에 알리는 자리 (20260811작3 (A)).
///
/// 사장님 원문 (2026-08-11):
///   *"인증키가 없어 인증할 수 없는 슬롯은 로그인만 되고 화면은 비활성화 그리고
///     비활성화된 화면에 '인증된 기기가 아닙니다. 히트판 관리자에게 문의하세요.' 안내"*
///
/// 왜 이런 자리가 필요한가
///   막혔다는 사실을 아는 것은 **요청을 보낸 쪽**(HitPanApiAuthHandler)이고,
///   그것을 보여줘야 하는 것은 **화면**이다. 둘은 서로를 모른다.
///   여기가 그 사이를 잇는다 — 요청 쪽이 표시를 남기면 화면이 그것을 보고 안내를 띄운다.
///
/// ⚠️ 기기 대수를 세는 일과는 무관하다. 대수는 기기 목록의 줄 수로 이미 센다.
public static class DeviceAuthState
{
    private static bool _blocked;

    /// 막혔다는 사실이 새로 확인됐을 때 알린다.
    public static event Action? Changed;

    /// 지금 이 기기가 막혀 있나.
    public static bool IsBlocked => _blocked;

    /// 서버가 "인증된 기기가 아니다" 라고 답했다.
    public static void MarkBlocked()
    {
        if (_blocked) return;          // 이미 알고 있으면 화면을 흔들지 않는다
        _blocked = true;
        Changed?.Invoke();
    }

    /// 인증 번호를 넣어 통과했다.
    public static void MarkAllowed()
    {
        if (!_blocked) return;
        _blocked = false;
        Changed?.Invoke();
    }
}
