// 히트판 디바이스 지문(fingerprint) 유틸.
// - 히트판은 디바이스 수 과금 — 기기 식별용 지문을 로그인 시 서버로 전달한다.
// - 서버는 같은 (tenant_id, fingerprint) 조합이면 기존 기기로 간주, 없으면 신규 등록.
window.hitpanDevice = {
    // ── 기기 특성 기반 안정 해시 (작1 2차봉합 2026-07-02, 사장님 결재) ──
    //   [진범] 종전 getFingerprint 는 localStorage 에 crypto.randomUUID() 난수를 심어 재사용했다.
    //   localStorage 는 origin(scheme+host+port)별로 격리되므로, 같은 PC라도 접속 주소가
    //   localhost ↔ 127.0.0.1 ↔ {id}.hitpan.kr 로 달라지면 저장소가 분리돼 매번 새 난수 = 새 기기.
    //   (호스트 실측 확정: localhost=01a7075d… ≠ 127.0.0.1=40ba83fe…)
    //   [봉합] 지문의 씨앗을 난수가 아니라 "브라우저·기기 환경 특성 해시"로 둔다. 환경 특성은
    //   origin·저장소와 무관하므로 주소가 달라도 같은 PC면 같은 씨앗이 나온다(origin 격리 극복).
    //   localStorage 캐시는 있으면 우선 재사용(성능·안정), 없으면 환경 해시로 복원 → 캐시 삭제·
    //   시크릿 모드·주소 변경에도 같은 지문. (검증팀 경고: 환경해시 단독은 동일기종 충돌 →
    //   캐시 병행으로 완화. NOT NULL 방어: 어떤 경우에도 빈 문자열 반환 금지, 최후 폴백 보유.)
    //   ⚠️ 지문 산식을 바꾸면 기존 등록 기기(옛 난수)와 안 맞아 재인식될 수 있다 → 마이그레이션은
    //   서버측 유예·재매핑으로 처리(작1 §5-3). 프론트는 안정 지문 생성까지만 책임진다.

    // 32bit FNV-1a 문자열 해시 (외부 의존 없이 동기 계산; 서버 fingerprint 컬럼 길이 안에 들어감)
    _hash: function (str) {
        var h = 0x811c9dc5;
        for (var i = 0; i < str.length; i++) {
            h ^= str.charCodeAt(i);
            h = (h + ((h << 1) + (h << 4) + (h << 7) + (h << 8) + (h << 24))) >>> 0;
        }
        return ('00000000' + h.toString(16)).slice(-8);
    },

    // origin·저장소와 무관한 기기 환경 특성만 모아 씨앗을 만든다.
    _envSeed: function () {
        try {
            var nav = navigator || {};
            var scr = screen || {};
            var tz = '';
            try { tz = Intl.DateTimeFormat().resolvedOptions().timeZone || ''; } catch (e) {}
            var parts = [
                nav.platform || '',
                nav.language || '',
                (nav.languages || []).join(','),
                nav.hardwareConcurrency || '',
                nav.maxTouchPoints || '',
                nav.deviceMemory || '',
                scr.width + 'x' + scr.height,
                scr.colorDepth || '',
                (scr.availWidth + 'x' + scr.availHeight),
                tz
            ];
            // UA 는 브라우저 업데이트로 흔들리므로 "브라우저 계열"만 거칠게(세부 버전 제외)
            var ua = (nav.userAgent || '').replace(/[0-9]+\.[0-9.]+/g, '');
            parts.push(ua);
            return parts.join('|');
        } catch (e) {
            return 'env-unknown';
        }
    },

    getFingerprint: function () {
        // 1) 캐시가 살아있으면 그대로 (같은 origin 재접속 = 성능·안정)
        try {
            var cached = localStorage.getItem('hitpan_fp_id');
            if (cached) return cached;
        } catch (e) { /* private 모드 등 — 아래 환경 해시로 진행 */ }

        // 2) 캐시 없음 → 환경 특성 해시로 복원 (origin·저장소 무관하게 같은 PC면 같은 값)
        var fp = '';
        try {
            fp = 'HFPv2-' + this._hash(this._envSeed());
        } catch (e) { fp = ''; }

        // 3) NOT NULL 방어 — 환경 해시조차 실패하면 최후 폴백(빈 문자열 절대 반환 안 함)
        if (!fp || fp === 'HFPv2-') {
            fp = 'HFPv2-' + this._hash('' + (Date.now ? Date.now() : 0) + Math.random());
        }

        // 4) 캐시에 저장(가능하면). 실패해도 다음 접속에 환경 해시로 같은 값 복원되므로 무해.
        try { localStorage.setItem('hitpan_fp_id', fp); } catch (e) { /* noop */ }
        return fp;
    },

    /// 스스로를 Mac 이라고 신고하지만 실은 손으로 만지는 기기(아이패드)인가.
    ///
    /// 🔴 2026-08-11 20260811작2 봉합 (사장님 실측 P0-1 · **과금이 걸린 자리**).
    ///   애플은 아이패드가 "데스크톱 화면을 달라"고 요청할 때 스스로를 **Mac 으로 신고**하게 만들었다.
    ///   그래서 기기가 보내주는 소개 문구(UA)만 읽으면 아이패드와 Mac 이 구분되지 않는다.
    ///   구분할 수 있는 유일한 실마리가 **손가락 터치를 몇 개까지 받는가**다.
    ///   Mac 은 터치가 없고(0), 아이패드는 여러 개를 받는다.
    ///
    ///   ⚠️ 히트판은 **기기 수로 요금을 매긴다.** 이 판정이 틀리면 아이패드가 컴퓨터 칸을 깎아
    ///     고객이 쓰지도 않은 자리에 돈을 낸다. 그래서 이름 짓기(getDeviceName)와
    ///     종류 가르기(getDeviceType) 가 **반드시 같은 답**을 쓰도록 이 한 곳에만 둔다.
    ///     두 곳에 같은 판별을 적으면 한쪽만 고쳐지는 사고가 난다.
    _isTouchMac: function (ua) {
        try {
            return /Macintosh/i.test(ua || navigator.userAgent || '')
                && (navigator.maxTouchPoints || 0) > 1;
        } catch (e) { return false; }
    },

    // 기기 이름 — 고객이 목록에서 "어느 컴퓨터인지" 알아보기 위한 값.
    //
    // 🔴 2026-08-10 [4] D-4 봉합 (검증팀장 데이비드 박 적발 · P0).
    //   종전엔 로그인 요청에 DeviceName 을 **아무도 보내지 않았다.** LoginRequest.DeviceName 은
    //   정의만 있고 채우는 쪽이 0개였다. 그 결과 기기 목록이 전부 "(이름없음)" 으로 보였고,
    //   메인PC 표식도 이름 없이 "자료 보관 컴퓨터" 단독으로만 만들어졌다.
    //
    //   ⚠️ 브라우저는 **PC 의 실제 컴퓨터 이름을 알 수 없다**(OS 정보 접근이 막혀 있다).
    //     그래서 알 수 있는 것(OS 계열 + 브라우저 계열)으로 사람이 알아볼 이름을 만든다.
    //     예: "Windows · Chrome" — 완벽하진 않지만 "(이름없음)" 보다 훨씬 낫고,
    //     고객이 나중에 목록에서 직접 이름을 바꿀 수 있게 하는 것은 별건이다.
    //
    //   고객이 읽는 값이라 개발용어를 쓰지 않는다(헌법 — 고객 노출 영역 개발용어 금지).
    getDeviceName: function () {
        try {
            var ua = navigator.userAgent || '';

            var os = '알 수 없는 기기';
            // 🔴 순서 주의 (20260811작2 · 사장님 실측 P0-2):
            //   아이패드가 스스로를 Mac 이라고 신고하기 때문에, Mac 을 먼저 물어보면
            //   아이패드가 전부 Mac 으로 확정된다. 사장님 화면에 "Mac · Safari" 로
            //   보인 것이 이 자리다. 그래서 **위장한 아이패드부터 걸러낸다.**
            //   ⚠️ 아이폰·구형 아이패드는 소개 문구에 "like Mac OS X" 라는 말이 들어 있다.
            //     그래서 Mac 을 먼저 물어보면 **아이폰까지 Mac 으로 확정된다.**
            //     아래처럼 애플 손기기(아이폰·아이패드)를 **Mac 보다 먼저** 가려야 한다.
            // 🔴 순서는 getDeviceType 과 **똑같이** 간다 (검증팀 C-3, 2026-08-11).
            //   두 함수가 다른 순서를 쓰면 **종류와 이름이 서로 어긋난다.**
            //   실제로 안드로이드 기기가 종류는 휴대기기인데 이름은 'Windows' 로 나왔다
            //   (소개 문구에 'Windows Phone' 이 들어간 기기 · Windows 검사가 위에 있었다).
            //   ⇒ 휴대기기를 **먼저** 가리고, 그 다음에 컴퓨터를 가린다.
            if (hitpanDevice._isTouchMac(ua)) os = 'iPhone/iPad';
            else if (/iPhone|iPad|iPod/i.test(ua)) os = 'iPhone/iPad';
            else if (/Android/i.test(ua)) os = 'Android';
            else if (/Windows/i.test(ua)) os = 'Windows';
            else if (/Macintosh|Mac OS/i.test(ua)) os = 'Mac';
            else if (/CrOS/i.test(ua)) os = '크롬북';
            else if (/X11|Wayland|Ubuntu|Fedora|Debian|FreeBSD|OpenBSD|Linux/i.test(ua)) os = 'Linux';

            // 순서 주의: Edge·Whale 은 UA 에 Chrome 을 포함하므로 먼저 걸러야 한다.
            var browser = '';
            if (/Edg\//i.test(ua)) browser = 'Edge';
            else if (/Whale/i.test(ua)) browser = '웨일';
            else if (/OPR\/|Opera/i.test(ua)) browser = 'Opera';
            else if (/Chrome/i.test(ua)) browser = 'Chrome';
            else if (/Firefox/i.test(ua)) browser = 'Firefox';
            else if (/Safari/i.test(ua)) browser = 'Safari';

            return browser ? (os + ' · ' + browser) : os;
        } catch (e) {
            return '알 수 없는 기기';
        }
    },

    // 기기 종류 가르기 — 🔴 **요금이 걸린 판정이다.**
    //
    // 🔴 2026-08-11 20260811작2 (사장님 판정 기준 확정):
    //   *"태블릿도 모바일로 잡으면 됨 — **운영체제가 안드로이드이거나 iOS이기 때문에**"*
    //
    //   *"**윈도우, 맥OS, 리눅스 등의 PC기반 운영체제가 아닌 것은 모두 모바일**"*
    //
    //   [무엇으로 가르나] **컴퓨터 운영체제인지 하나만 묻는다.**
    //     Windows · Mac · 리눅스  ⇒ 컴퓨터(pc)
    //     그 밖 **전부**          ⇒ 휴대기기(mobile)
    //
    //   🔴 [브라우저로 판단하지 않는다] 사장님 못박음:
    //     *"웹브라우저로 판단하면 안 돼. **윈도우PC에서 사파리를 돌릴 수도 있잖아**"*
    //     사파리를 쓴다고 애플 기기가 아니고, 크롬을 쓴다고 컴퓨터가 아니다.
    //     브라우저는 **사람이 고른 것**이고 기기 종류와 아무 상관이 없다.
    //     ⇒ 아래 판정에 브라우저 이름이 **한 글자도 없다.** 운영체제만 본다.
    //     (브라우저는 이름 짓기에만 쓴다 — "Windows · Safari" 처럼 고객이 어느 컴퓨터인지
    //      알아보라고 붙이는 꼬리표일 뿐, 요금 칸과 무관하다)
    //
    //   ⚠️ 사장님이 예로 드신 스마트TV 는 **따로 다루지 않는다.** TV 로 히트판을 볼 일이
    //     있다면 *"PC 에 TV 를 연결해서"* 보는 것이고, 그때 브라우저가 보는 것은 그 PC 의
    //     Windows 다 — 이미 컴퓨터로 잡힌다. TV 브라우저가 직접 올 일은 없다.
    //     설령 온다 해도 아래 3) 에서 저절로 휴대기기가 된다. **목록을 만들 필요가 없다.**
    //
    //   🔴 [묻는 방향이 중요하다] 휴대기기 목록을 만들어 놓고 "여기 있으면 모바일" 로
    //     물으면, 목록에 없는 **처음 보는 기기가 전부 컴퓨터로 떨어진다.**
    //     컴퓨터 칸이 더 비싸므로 그건 고객이 손해 보는 방향이다.
    //     반대로 물으면 — 컴퓨터 운영체제는 셋뿐이고 새로 생기지 않는다 —
    //     새 기기가 나와도 저절로 휴대기기 칸으로 간다. **모르는 것은 싼 칸으로.**
    //
    //   [왜 운영체제인가] 화면 크기·터치 여부로 가르면 경계가 끝없이 흔들린다 —
    //     터치 되는 노트북, 화면 큰 태블릿, 데스크톱 화면을 요청한 아이패드.
    //     실제로 종전 판정은 **안드로이드 태블릿**(소개 문구에 'Mobi' 도 'Tablet' 도 없다)과
    //     **Windows 태블릿PC**(문구에 'Tablet' 이 들어간다)에서 둘 다 틀렸다.
    //     운영체제는 흔들리지 않는다.
    //
    //   [칸은 둘뿐이다] 종전엔 'tablet' 을 따로 돌려줬으나, 요금 계산이 이미
    //     휴대기기와 태블릿을 **같은 칸에 합산**하고 있었다. 셋으로 나눠 부를 이유가 없다.
    //     (서버도 tablet 을 받으면 mobile 로 흡수한다 — TenantDeviceService.NormalizeDeviceType)
    //
    //   ⚠️ 아이패드가 어려운 이유: 스스로를 **Mac 이라고 신고**한다. 그래서 문구만 읽으면
    //     책상 위 Mac 과 구분되지 않는다. 손가락 터치를 받는지가 유일한 실마리다(_isTouchMac).
    //   ⚠️ 반대 방향도 똑같이 사고다 — **터치 없는 진짜 Mac 은 'pc' 여야 한다.**
    //   🔴 [예외가 나도 절대 던지지 않는다] 검증팀 C-1 (2026-08-11).
    //     이 함수만 감싸는 곳이 없었다. 그런데 이걸 부르는 쪽(AuthService)은 지문·종류·이름
    //     **세 가지를 한 묶음으로 감싸** 두어서, 여기서 예외가 한 번 나면 **종류와 이름이
    //     함께 날아간다.** 그러면 서버가 종류를 'pc' 로 채우고 이름은 비어버린다 —
    //     **아이패드가 컴퓨터 칸을 먹는다.** 이번에 없애려던 바로 그 증상이다.
    //     ⇒ 무슨 일이 있어도 값 하나는 돌려준다. 폴백은 'mobile' 이다(싼 칸 · §판정 기준).
    getDeviceType: function () {
      try {
        var ua = navigator.userAgent || '';

        // 🔴 1) 컴퓨터 운영체제와 **헷갈리는 휴대기기부터** 걷어낸다. 순서가 전부다.
        //
        //    ⚠️ 아이폰·아이패드는 소개 문구에 **"like Mac OS X"** 라는 말을 달고 온다.
        //      그래서 2) 의 Mac 검사가 **아이폰을 책상 위 Mac 으로 판정한다.**
        //      (실제로 이 순서를 놓쳐 아이폰이 컴퓨터로 잡히는 일이 한 번 있었다)
        //    ⚠️ 안드로이드도 문구에 'Linux' 가 들어간다 — 리눅스 위에서 돌기 때문이다.
        //      역시 리눅스 컴퓨터로 오인되지 않게 여기서 먼저 걷어낸다.
        if (hitpanDevice._isTouchMac(ua)) return 'mobile';   // Mac 으로 위장한 아이패드
        if (/iPhone|iPad|iPod/i.test(ua)) return 'mobile';
        if (/Android/i.test(ua)) return 'mobile';

        // 2) 컴퓨터 운영체제인가 — Windows · Mac · 리눅스, 이 셋뿐이다.
        if (/Windows NT|Win64|Win32/i.test(ua)) return 'pc';
        if (/Macintosh|Mac OS X/i.test(ua)) return 'pc';

        //    ⚠️ 리눅스는 조건을 좁게 잡는다. 'Linux' 라는 낱말 하나만 보면 안 된다 —
        //      리눅스 위에서 도는 기기가 많아서 그것들이 컴퓨터로 새어든다.
        //      **책상 위 리눅스 컴퓨터**는 창 시스템(X11·Wayland)이나 배포판 이름을 달고 온다.
        if (/X11|Wayland|CrOS|Ubuntu|Fedora|Debian|FreeBSD|OpenBSD/i.test(ua)) return 'pc';

        // 3) 컴퓨터 운영체제가 아니면 **전부 휴대기기** (사장님 판정).
        //    아이폰·아이패드·스마트TV, 그리고 아직 세상에 없는 무엇이든 여기로 온다.
        return 'mobile';
      } catch (e) {
        // 판정에 실패하면 **싼 칸**으로 보낸다. 비싼 칸을 잘못 깎지 않는 쪽이다.
        return 'mobile';
      }
    },

    // localStorage 기반 device_id 보관 (서버가 돌려준 값)
    getDeviceId: function () {
        try { return localStorage.getItem('hitpan_device_id'); }
        catch (e) { return null; }
    },
    setDeviceId: function (id) {
        try { if (id) localStorage.setItem('hitpan_device_id', id); }
        catch (e) { /* private 모드 등 */ }
    },
    clearDeviceId: function () {
        try { localStorage.removeItem('hitpan_device_id'); }
        catch (e) { /* noop */ }
    },

    // ── 모바일 홈화면 추가 (20260811작1 (D), 사장님 오더 2026-08-11) ──
    //   "Y 터치시 모바일 홈화면에 히트판ERP 아이콘 생성"
    //   아이폰과 안드로이드가 완전히 다르게 동작하므로 갈라서 다룬다.

    /// 아이폰·아이패드인가.
    ///   아이패드는 iPadOS 13+ 부터 UA 가 맥과 같아진다 — 그래서 터치 지원 여부를 함께 본다.
    ///   이 판정이 틀리면 아이폰 사용자에게 안드로이드 설명이 보인다(더 헷갈린다).
    isIos: function () {
        try {
            var ua = navigator.userAgent || '';
            if (/iPhone|iPod/i.test(ua)) return true;
            if (/iPad/i.test(ua)) return true;
            // iPadOS 13+ : "Macintosh" 로 위장하지만 터치가 된다
            //   🔴 20260811작2 — 같은 판별을 여기 또 적지 않는다. 두 곳에 적으면
            //     한쪽만 고쳐진다. 판별의 근거는 _isTouchMac 한 곳에만 둔다.
            if (hitpanDevice._isTouchMac(ua)) return true;
            return false;
        } catch (e) { return false; }
    },

    /// 브라우저가 "홈 화면에 추가" 버튼을 줄 수 있는가(안드로이드 크롬 계열).
    ///   아이폰은 이 이벤트를 아예 안 준다 — 애플이 자동 추가를 막아놨다.
    canInstallPrompt: function () {
        return !!window.__hitpanInstallPrompt;
    },

    /// 저장해 둔 설치 프롬프트를 띄운다.
    promptInstall: function () {
        try {
            var p = window.__hitpanInstallPrompt;
            if (!p) return;
            p.prompt();
            window.__hitpanInstallPrompt = null;   // 한 번 쓰면 다시 못 쓴다
        } catch (e) { /* noop */ }
    }
};

// 안드로이드 크롬은 설치 가능 시점에 이 이벤트를 준다. 잡아 뒀다가 사용자가
// [홈 화면에 추가] 를 누를 때 띄운다 — 브라우저가 알아서 띄우는 배너는 놓치기 쉽다.
window.addEventListener('beforeinstallprompt', function (e) {
    e.preventDefault();
    window.__hitpanInstallPrompt = e;
});
