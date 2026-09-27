namespace HitPan.Application.Common;

/// <summary>
/// 🔴 <b>기기 종류 판정 — 이 파일이 유일한 판정 자리다</b> (20260927작2 절A · 설계 §2-2).
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>새로 만든 규칙이 아니다.</b> 아래 세 함수는 2026-08-18(20260818작2 · [3-V] V-05)에 결재돼
/// 이미 돌고 있던 판정이다. <c>TenantDeviceService</c>(Infrastructure) 안에 있던 <b>같은 함수를
/// 본문·주석 그대로 여기로 옮겼다.</b>
/// </para>
/// <para>
/// 🔴 <b>왜 옮겼나</b> — 로그인 판정이 필요한 <c>AuthService</c> 는 Application 에 있고 참조 방향이
/// <c>Infrastructure → Application</c> 이라(<c>HitPan.Infrastructure.csproj:5</c>) 위를 부를 수 없다.
/// 그렇다고 <b>복사하면 판정 자리가 둘이 된다</b> — 이 레포는 폴백 2곳(D-9)으로 이미 당했고,
/// 한쪽만 고쳐지는 사고가 바로 이 트랙이 존재하는 이유다.
/// ⇒ 아래(Application)로 내리고 <b>위(Infrastructure)가 이 함수를 부른다.</b>
/// <c>TenantDeviceService</c> 의 세 메서드는 시그니처만 남은 <b>위임 한 줄</b>이다(#1 — 지우지 않고 가리킨다).
/// </para>
/// <para>
/// 🔴 <c>public static</c> 인 이유 — <c>InternalsVisibleTo</c> 는 Infrastructure 에만 있다
/// (<c>HitPan.Infrastructure.csproj:13</c>). Application 에서 <c>internal</c> 로 두면
/// <b>게이트가 값을 대조할 수 없다</b>(글자 검사만 남아 F-2 재발).
/// </para>
/// </remarks>
public static class DeviceTypeResolver
{
    /// 기기 종류를 저장 가능한 값으로 정리한다.
    ///
    /// 🔴 2026-08-11 20260811작2 (사장님 판정 기준 확정):
    ///   *"태블릿도 모바일로 잡으면 됨 — **운영체제가 안드로이드이거나 iOS이기 때문에**"*
    ///   *"**태블릿, 폰, 스마트TV = 모바일** · 운영체제로 구분하면 됨"*
    ///   *"**윈도우, 맥OS, 리눅스 등의 PC기반 운영체제가 아닌 것은 모두 모바일**"*
    ///
    ///   [왜 운영체제로 가르나] 화면 크기나 터치 여부로 가르면 경계가 계속 흔들린다.
    ///     터치 되는 노트북, 화면 큰 태블릿, 데스크톱 화면을 요청한 아이패드…
    ///     끝이 없다. 그러나 **운영체제는 흔들리지 않는다.**
    ///     Windows·Mac·리눅스는 책상에 두고 쓰는 컴퓨터의 운영체제이고, 나머지는 아니다.
    ///
    ///   ⇒ 그래서 칸도 **둘뿐**이다: 휴대기기(mobile) · 컴퓨터(pc).
    ///     'tablet' 은 받아주되 **모바일로 흡수**한다 — 요금 계산이 이미 둘을 같은 칸에
    ///     합산하고 있었으므로(아래 mobileUsed), 굳이 셋으로 나눠 부를 이유가 없다.
    ///
    ///   🔴 [모르는 값은 휴대기기로 본다] 컴퓨터 칸이 더 비싸다. 판정이 애매할 때
    ///     컴퓨터로 세면 **고객이 쓰지도 않은 자리에 돈을 낸다.** 반대로 세면 우리가 조금
    ///     손해 볼 뿐이다. ⇒ 애매한 것은 **고객에게 유리한 쪽**으로 보낸다.
    ///     (클라이언트도 같은 방향으로 판정한다 — device-fingerprint.js getDeviceType)
    ///
    ///   ⚠️ null 을 돌려주는 경우: 클라이언트가 종류를 **안 보냈을 때**.
    ///     갱신 경로에서 COALESCE 로 받아 **기존 값을 지우지 않게** 하기 위함이다.
    ///
    ///   🔴 2026-08-15 20260815작3 P1 (I-6) — **이 자리 주석이 낡았다. 정정한다.**
    ///     [옛 문장] *"신규 등록 경로는 호출부에서 ?? "pc" 로 받는다"*
    ///     [지금] 신규 등록 경로는 **`?? "mobile"`** 로 받는다. 폴백을 싼 칸으로 돌렸다.
    ///     컴퓨터 칸이 더 비싸므로, 종류를 모를 때 컴퓨터로 세면 고객이 쓰지도 않은 자리에
    ///     돈을 낸다. ⇒ 값이 아예 없든 모르는 값이든 **똑같이 휴대기기**로 간다.
    /// <remarks>
    /// 🔴 2026-09-27 20260927작2 절A — <c>internal</c> 에서 <c>public</c> 으로 열었다.
    ///   자리가 Application 으로 내려오면서 <c>InternalsVisibleTo</c> 가 닿지 않기 때문이다
    ///   (이유는 클래스 주석 참조). 판정 내용은 **한 글자도 바뀌지 않았다.**
    /// </remarks>
    public static string? NormalizeDeviceType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var t = raw.Trim().ToLowerInvariant();

        // 🔴 2026-08-18 20260818작2 (2-6) — **`tablet` 을 더 이상 뭉개지 않는다.**
        //
        //   [옛 코드] `if (t is "tablet") return "mobile";` — 저장까지 mobile 로 덮었다.
        //   [지금] `tablet` 은 `tablet` 으로 남는다.
        //
        //   🔴 **과금은 한 글자도 안 바뀐다** (사장님 결재 3 — *"테블렛,모바일 같이 씀"*).
        //     칸을 세는 자리는 MobileUsedFrom 하나뿐이고, 그 함수가 `mobile` 과 `tablet` 을
        //     **원래부터 함께 세고 있었다.** ⇒ 저장만 갈라지고 칸 수는 그대로 둘이다.
        //
        //   ⚠️ 이 값을 **셋째 칸으로 착각하지 마라.** 가격표는 2칸이다(작업지시서 §8).
        //     저장을 가르는 이유는 하나뿐 — 나중에 *"태블릿은 따로 받자"* 하실 때
        //     **과거 자료가 있어야** 갈 수 있기 때문이다.
        if (t is "tablet") return "tablet";
        if (t is "mobile" or "pc") return t;

        // 모르는 값은 휴대기기로 본다 — 비싼 칸을 잘못 깎지 않는 쪽
        return "mobile";
    }

    /// <summary>
    /// 🔴 <b>기기 종류를 서버가 정한다</b> — 클라이언트 신고값을 <b>그대로 믿지 않는다</b> (20260818작2 · [3-V] V-05).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>진짜 구멍은 승격이 아니라 최초 등록이었다.</b>
    /// 2-1 은 <c>mobile → pc</c> 승격에 한도를 다시 보게 만든다. 그런데 <b>공격자는 승격할 이유가 없다</b> —
    /// 처음부터 <c>mobile</c> 이라고 신고하고 <b>안 바꾸면 그만</b>이다. 컴퓨터 한도가 0이어도
    /// 컴퓨터가 휴대기기 칸으로 무제한 들어온다.
    /// ⇒ <b>2-1 만 하면 게이트는 초록이고 구멍은 그대로다.</b> 그것을 막으려고 이 함수가 있다.
    /// </para>
    /// <para>
    /// [무엇을 하나] 클라이언트가 <c>DeviceType</c> 을 신고하지만, 그 값은 브라우저 안의 스크립트가
    /// 만든 것이라 <b>사람이 손으로 바꿔 보낼 수 있다.</b> 반면 <c>User-Agent</c> 는 브라우저가
    /// 스스로 붙여 서버가 <b>직접</b> 읽는 값이다(<c>Request.Headers.UserAgent</c>).
    /// ⇒ 둘이 <b>어긋나면 서버 판정을 쓴다.</b>
    /// </para>
    /// <para>
    /// ⚠️ <b>완벽을 약속하지 않는다.</b> User-Agent 도 마음만 먹으면 바꿀 수 있다.
    /// 이 함수가 하는 일은 <b>"신고값을 그대로 믿지 않는 것"</b> 하나다.
    /// <para>
    /// 🔴 <b>한계 정정 (2026-09-27 20260927작2 2차 · [3-V] V-B1 실측)</b> —
    /// ⬛ [낡은 줄] <i>"화면 조작만으로 칸을 고르던 것을 <b>헤더까지 함께 위조해야</b>만 되게 바꾼다"</i>
    /// 는 <b>정확하지 않다.</b> 위조도, 둘을 함께 맞추는 일도 필요하지 않다.
    /// </para>
    /// <para>
    /// 🔴 <b>「서버가 이긴다」는 User-Agent 가 말을 할 때만이다. UA 를 침묵시키면 신고값이 이긴다.</b>
    /// <c>User-Agent</c> 를 <b>생략하거나 공백</b>으로 두면 <c>JudgeTypeFromUserAgent</c> 가 <c>null</c> 을
    /// 돌려주고(이 파일 아래 <c>IsNullOrWhiteSpace</c>), <c>ResolveDeviceType</c> 은 <b>신고값을 그대로 쓴다</b>
    /// (신고값마저 없으면 <c>?? "mobile"</c> — 싼 칸). <b>Mac 계열 UA</b> 는 8/10 아이패드 사고 재발 방지를 위해
    /// <b>의도적으로 판정을 포기</b>하므로 같은 길로 빠져나간다. UA 에 <c>iPhone</c>·<c>Android</c> 문자열이
    /// 섞여 있으면(부분문자열 검사) 휴대기기로 간다.
    /// </para>
    /// <para>
    /// ⇒ 이 함수가 실제로 닫은 것은 <b>"UA 가 PC 라고 말하는데 신고값만 mobile 인" 경로</b> 하나다.
    /// 🔴 남는 것을 막는 것은 <b>다음 차수의 장비넘버(<c>hardware_id</c>)</b> 몫이고 여기서 약속하지 않는다.
    /// 이것을 <i>"위조를 막았다"</i> 거나 <i>"고쳤다"</i> 고 적으면 <b>거짓봉합</b>이다.
    /// </para>
    /// <para>
    /// 🔴 <b>판정 순서는 클라이언트(<c>device-fingerprint.js getDeviceType</c>)와 똑같이 간다.</b>
    /// 두 곳이 다른 순서를 쓰면 <b>정상 기기가 어긋남으로 잡혀</b> 멀쩡한 고객의 칸이 바뀐다.
    /// 특히 <b>휴대기기를 먼저 걷어낸다</b> — 아이폰·아이패드는 <c>like Mac OS X</c> 를,
    /// 안드로이드는 <c>Linux</c> 를 달고 오기 때문이다.
    /// </para>
    /// <para>
    /// ⚠️ <b>어긋남이 없으면 신고값을 존중한다.</b> User-Agent 로 못 가리는 것(태블릿 세부 구분 등)까지
    /// 서버가 뭉개면, 클라이언트가 애써 판정한 정보를 잃는다.
    /// </para>
    /// <para>
    /// 🔴 2026-09-27 20260927작2 절A — <b>로그인(축 B)도 이 함수를 부른다.</b>
    /// 종전 <c>AuthService.NormalizeDeviceKind</c> 는 <c>request.DeviceType</c> <b>신고값만</b> 보고
    /// 판정해(자진신고 한 칸) 화면 조작만으로 축 B 차단을 빠져나갈 수 있었다.
    /// ⇒ 그 함수를 없애고 이 자리로 모았다. <b>판정 자리는 하나다.</b>
    /// </para>
    /// </remarks>
    /// <param name="claimed">클라이언트가 신고한 종류. 안 보냈으면 <c>null</c>.</param>
    /// <param name="userAgent">서버가 직접 읽은 <c>User-Agent</c>. 없으면 <c>null</c>.</param>
    public static string ResolveDeviceType(string? claimed, string? userAgent)
    {
        var normalized = NormalizeDeviceType(claimed);
        var judged = JudgeTypeFromUserAgent(userAgent);

        // User-Agent 가 없거나 못 가리면 서버가 할 말이 없다 — 종전 규칙 그대로.
        //   🔴 그래도 `?? "mobile"` 로 끝낸다(8/16 CR2-2 종결 — 싼 칸이 고객에게 유리하다).
        if (judged is null) return normalized ?? "mobile";

        // 신고를 안 했으면 서버 판정을 쓴다.
        if (normalized is null) return judged;

        // 🔴 **어긋나면 서버가 이긴다.** 여기가 V-05 의 본체다.
        //   컴퓨터로 보이는데 휴대기기라고 신고했다 ⇒ 컴퓨터로 센다(요금이 새지 않는다).
        //   휴대기기로 보이는데 컴퓨터라고 신고했다 ⇒ 휴대기기로 센다(고객이 덜 낸다).
        var normalizedIsPc = normalized == "pc";
        var judgedIsPc = judged == "pc";
        if (normalizedIsPc != judgedIsPc) return judged;

        // 어긋나지 않는다 — 신고값을 존중한다(tablet 같은 세부 구분을 잃지 않기 위해서다).
        return normalized;
    }

    /// <summary>
    /// 🔴 <b>서버가 <c>User-Agent</c> 만 보고 종류를 가른다</b> (20260818작2 · [3-V] V-05).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>순서가 전부다.</b> <c>device-fingerprint.js</c> 의 <c>getDeviceType</c> 과
    /// <b>같은 순서</b>를 쓴다 — 두 곳이 갈리면 정상 기기가 어긋남으로 잡힌다.
    /// </para>
    /// <para>
    /// ⚠️ <b>못 가리면 <c>null</c> 을 돌려준다.</b> 억지로 하나를 고르면
    /// <b>모르는 것을 아는 척</b>하는 것이고, 그 값이 고객의 칸을 바꾼다.
    /// 모르면 신고값을 존중하는 쪽이 맞다.
    /// </para>
    /// <para>
    /// ⚠️ <b>서버는 손가락 터치를 알 수 없다.</b> 클라이언트는 <c>maxTouchPoints</c> 로
    /// Mac 으로 위장한 아이패드를 잡아내지만(<c>_isTouchMac</c>), 그 값은 헤더에 없다.
    /// ⇒ 서버 눈에 아이패드는 <b>Mac(pc)</b> 으로 보인다. 그래서 클라이언트가 <c>mobile</c> 이라
    /// 신고하면 <b>어긋남으로 잡혀 pc 로 뒤집힐 위험</b>이 있다 —
    /// 🔴 그것이 정확히 <b>2026-08-10 사고</b>(아이패드가 컴퓨터 칸을 먹던 일)의 재발이다.
    /// ⇒ <b>Mac 계열은 판정하지 않고 <c>null</c> 로 비켜선다.</b> 아이패드는 클라이언트 말을 믿는다.
    /// </para>
    /// </remarks>
    public static string? JudgeTypeFromUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return null;

        var ua = userAgent;

        // 1) 컴퓨터 운영체제와 헷갈리는 휴대기기부터 걷어낸다 — 클라이언트와 같은 순서다.
        //    ⚠️ 아이폰·아이패드는 "like Mac OS X" 를, 안드로이드는 "Linux" 를 달고 온다.
        if (Contains(ua, "iPhone") || Contains(ua, "iPad") || Contains(ua, "iPod")) return "mobile";
        if (Contains(ua, "Android")) return "mobile";

        // 2) 컴퓨터 운영체제인가 — Windows 와 책상 위 리눅스만 확신한다.
        if (Contains(ua, "Windows NT") || Contains(ua, "Win64") || Contains(ua, "Win32")) return "pc";
        if (Contains(ua, "X11") || Contains(ua, "Wayland") || Contains(ua, "CrOS")
            || Contains(ua, "Ubuntu") || Contains(ua, "Fedora")
            || Contains(ua, "FreeBSD") || Contains(ua, "OpenBSD")) return "pc";

        // 🔴 3) Mac 계열은 **판정하지 않는다.** 위 remarks 참조 —
        //    서버는 터치 여부를 못 봐서 아이패드와 책상 위 Mac 을 가를 수 없다.
        //    억지로 pc 라 하면 2026-08-10 사고가 재발한다.
        //
        //    ⚠️ 그 대가를 정확히 적는다: **Mac 을 사칭하면 이 검사를 빠져나간다.**
        //      막는 것은 다음 차수의 장비넘버(hardware_id) 몫이고, 여기서 약속하지 않는다.
        return null;
    }

    /// <summary>
    /// <c>user_sessions.device_kind</c> 에 넣을 수 있는 두 값으로만 접는다 (20260927작2 절A).
    /// </summary>
    /// <remarks>
    /// 🔴 <b>판정이 아니다.</b> 판정은 위 <c>ResolveDeviceType</c> 한 곳에서 이미 끝났다.
    /// 이 함수는 <b>저장 칸의 모양</b>에 맞추는 일만 한다.
    /// <para>
    /// 🔴 <b>왜 필요한가 (실측 근거)</b> — <c>DESCRIBE user_sessions</c>:
    /// <c>device_kind enum('pc','mobile') NOT NULL DEFAULT 'mobile'</c> —
    /// <b>칸이 둘뿐이다.</b> 그런데 <c>ResolveDeviceType</c> 은 <c>tablet</c> 을 돌려줄 수 있다
    /// (신고값이 <c>tablet</c> 이고 User-Agent 도 휴대기기로 보일 때 — 어긋남이 아니므로 신고값을 존중한다).
    /// 그 값을 그대로 넣으면 INSERT 가 실패해 세션 행이 안 남는다.
    /// </para>
    /// <para>
    /// ⚠️ <b>과금은 이 함수와 무관하다.</b> 칸을 세는 자리(<c>MobileUsedFrom</c>)는
    /// <c>mobile</c> 과 <c>tablet</c> 을 원래부터 함께 세고, <c>tenant_devices</c> 에는
    /// <c>tablet</c> 이 그대로 남는다. 접는 것은 <b>세션 표 한 곳</b>뿐이다.
    /// </para>
    /// <para>
    /// 🔴 축 B(같은 계정 PC 동시 로그인)는 <b>컴퓨터냐 아니냐</b>만 묻는다 —
    /// 모바일은 9/25 결재로 FREE 이고 PC 와 동시 접속이 허용된다.
    /// ⇒ <c>pc</c> 가 아닌 것은 전부 휴대기기 칸이다(8/11 결재 *"PC기반 운영체제가 아닌 것은 모두 모바일"*).
    /// </para>
    /// </remarks>
    public static string ToSessionDeviceKind(string? resolvedType)
        => string.Equals(resolvedType, "pc", StringComparison.Ordinal) ? "pc" : "mobile";

    private static bool Contains(string haystack, string needle)
        => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
