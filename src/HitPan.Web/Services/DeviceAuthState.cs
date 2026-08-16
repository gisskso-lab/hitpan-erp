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
        // 🔴 20260816작2 — 승인 대기도 함께 푼다.
        //   대표가 [예] 를 눌러 통과하는 길과 인증번호를 넣어 통과하는 길이 **둘 다** 여기로 온다.
        //   한쪽만 풀면 통과했는데도 관문이 그대로 남는다.
        if (!_blocked && !_awaitingApproval) return;
        _blocked = false;
        _awaitingApproval = false;
        Changed?.Invoke();
    }

    // ══════════════════════════════════════════════════════════════
    // 🔴 승인 대기 — 로그인과 ERP 사이의 관문 (20260816작2 · 사장님 전결)
    // ══════════════════════════════════════════════════════════════

    private static bool _awaitingApproval;

    /// <summary>
    /// 이 기기가 <b>대표의 허락을 기다리는 중</b>인가.
    ///
    /// <para>
    /// 사장님 설계: 로그인 → <b>[디바이스 인증]</b> → 히트판 ERP.
    /// 로그인은 통과했지만(401 을 내지 않는다) 아직 업무 화면에 들어가면 안 되는 상태다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <see cref="IsBlocked"/> 와 다르다. 그쪽은 <i>"인증 번호를 받아 오세요"</i>(직원이 할 일이 있다),
    /// 이쪽은 <i>"대표님이 허락하면 바로 시작됩니다"</i>(직원은 기다리기만 하면 된다).
    /// 둘을 합치면 직원에게 <b>할 수 없는 일을 시키는 안내</b>가 뜬다.
    /// </para>
    ///
    /// <para>🔴 이 상태에서도 <b>슬롯은 아직 안 먹는다.</b> 대표가 [예] 를 누를 때 1개 는다.</para>
    /// </summary>
    public static bool IsAwaitingApproval => _awaitingApproval;

    /// 로그인 응답이 "이 기기는 승인 대기" 라고 알려줬다.
    public static void MarkAwaitingApproval()
    {
        if (_awaitingApproval) return;   // 이미 알고 있으면 화면을 흔들지 않는다
        _awaitingApproval = true;
        Changed?.Invoke();
    }
}
