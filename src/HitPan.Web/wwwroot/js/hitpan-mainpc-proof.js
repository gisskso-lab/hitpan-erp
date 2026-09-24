// =============================================================
// 메인PC 「왕복 증명」 — 브라우저가 맡은 한 걸음
//   20260922작2 절B · 사장님 전결 2026-09-22
//   설계: docs/설계/erp/20260922_설계_메인PC물리식별_왕복증명.md
// =============================================================
//
// [이 파일이 하는 일 — 딱 하나]
//   서버에서 받은 표(challenge)를 들고 **자기 컴퓨터 안의 히트판**을 두드린다.
//
//   메인PC 면    → 그 컴퓨터 안에 히트판 본체가 있으니 답이 온다.
//   클라이언트면 → 자기 컴퓨터엔 히트판이 없으니 아무 답도 없다.
//
//   히트판 API 는 루프백 전용으로 열려 있어 **남의 메인PC 를 두드릴 방법이 없다.**
//   그래서 이 한 걸음이 곧 물리적 증거가 된다.
//
// [왜 이게 필요한가]
//   고객은 도메인으로 접속한다. 터널(cloudflared)이 그 PC 안에서 히트판을 다시 부르므로
//   서버가 보는 소켓 주소는 **외부 접속이든 메인PC 든 항상 127.0.0.1** 이다.
//   같은 사무실이면 공인 IP 도 같다. ⇒ **IP 로는 영영 못 가린다.**
//
// 🔴 실패는 오류가 아니다 — 클라이언트 PC 에서는 실패가 **정상**이다.
//   그러므로 콘솔에 아무것도 남기지 않는다. 고객 화면에 흔적을 만들지 않는다.
//   (사장님: "실제 사용하는 고객은 인증키가 도는지 안도는지 모르게")
//
// 🔴 로그인 토큰을 싣지 않는다. 표 하나면 된다 —
//   표는 서버가 발급했고, 1회용이며, 60초만 살고, 최종 판정은 서버가 세션까지 대조해 내린다.
// =============================================================

(function () {
    'use strict';

    // 히트판 본체가 자기 컴퓨터에서 듣고 있는 자리.
    //   ⚠️ 두 주소를 다 시도한다 — 브라우저·OS 설정에 따라 한쪽만 닿는 경우가 있다.
    //     (실측 2026-09-22: 도메인 페이지에서 둘 다 HTTP 200)
    var LOCAL_ORIGINS = ['http://127.0.0.1:5257', 'http://localhost:5257'];

    // 🔴 20260925작1 절A — 1500 에서 늘렸다. **종전 주석의 근거가 사실이 아니었다.**
    //
    //   [종전에 적혀 있던 말] *"길게 잡으면 히트판이 없는 컴퓨터에서 로그인이 그만큼 느려진다"*
    //   [실측 2026-09-25]  그런 일은 없다. 히트판이 없는 자리는 **연결 자체가 거부되어 1ms 에 끝난다.**
    //     빈 포트로 브라우저에서 쏴 봤다 → `실패 @1ms` · `실패 @0ms`.
    //     ⇒ 시간제한을 늘려서 느려지는 경우는 **존재하지 않는다.**
    //
    //   [그럼 무엇을 기다리는가] 브라우저가 띄우는 **"이 사이트가 당신 컴퓨터 안을 봐도 됩니까"** 팝업이다.
    //     Chrome·Edge 가 https 페이지에서 로컬(127.0.0.1)로 나가는 요청에 사용자 **권한**을 묻기 시작했다
    //     (프로필에 `loopback_network` 로 저장된다). 허용 전까지 요청은 **응답 없이 매달린다.**
    //     1500ms 는 그 팝업을 사람이 읽고 누르기에 턱없이 짧았고, 그래서
    //     **고객이 [허용] 을 눌러도 그 왕복은 이미 죽어 있었다** — 새로고침해야만 열렸다.
    //
    //   [걸리는 시간 실측]  히트판 없음 1ms · 권한 거부됨 1~3ms · 권한 있음 5~42ms · **팝업 대기 = 매달림**
    //     ⇒ 이 시간제한에 실제로 걸리는 경우는 **마지막 하나뿐이다.**
    //
    //   🔴 **무한정 기다리지는 않는다.** 없애면 사람이 팝업을 무시했을 때 회전자가 영원히 돈다
    //     (실측에서 30초 넘게 매달리는 것을 관측했다). 두 주소를 차례로 두드리므로 최악 20초이고,
    //     그 최악조차 **메인PC 에서 사람이 20초간 안 누른** 경우뿐이다 — 그때는 자료관리에서 한 번 더 돈다
    //     (`MainPcOnly.razor` · 20260925작1 절C).
    var TIMEOUT_MS = 10000;

    function askOnce(origin, challenge) {
        // AbortController 로 직접 끊는다 — fetch 는 스스로 시간제한을 두지 않는다.
        var ac = new AbortController();
        var timer = setTimeout(function () { ac.abort(); }, TIMEOUT_MS);

        return fetch(origin + '/api/devices/mainpc-proof', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ challenge: challenge }),
            signal: ac.signal,
            // 🔴 자격증명을 싣지 않는다. 이 길은 표만으로 지나간다.
            credentials: 'omit',
            cache: 'no-store'
        }).then(function (res) {
            if (!res.ok) return false;
            return res.json().then(function (body) { return !!(body && body.confirmed); });
        }).catch(function () {
            // 🔴 조용히 삼킨다. 클라이언트 PC 에서는 여기로 오는 것이 정상이다.
            return false;
        }).finally(function () {
            clearTimeout(timer);
        });
    }

    // 🔴 출입증은 sessionStorage 에 둔다 — localStorage 가 아니다.
    //   창을 닫으면 사라진다. 이 값은 **짧게 살아야** 안전하고, 디스크에 오래 남을 이유가 없다.
    //   ⚠️ 사생활 보호 모드·저장 차단 환경에서는 접근 자체가 예외를 던진다.
    //     그때도 화면은 멀쩡히 돌아야 하므로 전부 try 로 감싼다(값이 없으면 왕복을 다시 돌면 그만이다).
    var PASS_KEY = 'hitpan.mainpc.pass';

    function readPass() {
        try { return sessionStorage.getItem(PASS_KEY) || null; } catch (e) { return null; }
    }

    function writePass(v) {
        try {
            if (v) sessionStorage.setItem(PASS_KEY, v);
            else sessionStorage.removeItem(PASS_KEY);
        } catch (e) { /* 저장을 못 해도 동작은 계속된다 — 다음 요청에서 왕복을 다시 돈다 */ }
    }

    window.hitpanMainPc = {
        /** 자료관리 요청에 함께 보낼 출입증. 없으면 null. */
        getPass: function () { return readPass(); },

        /** 왕복을 통과해 받은 출입증을 보관한다. null 을 주면 지운다. */
        setPass: function (v) { writePass(v); return true; },

        /**
         * 표를 들고 자기 컴퓨터 안의 히트판을 두드린다.
         * @param {string} challenge 서버가 발급한 표
         * @returns {Promise<boolean>} 두드려서 답을 받았는가
         */
        probe: function (challenge) {
            if (!challenge) return Promise.resolve(false);

            // 한 주소가 막히면 다른 주소로 한 번 더 — 사장님 결재 D-5 "한 번 재시도".
            //   ⚠️ 동시에 쏘지 않고 차례로 한다. 히트판이 없는 컴퓨터에서 둘 다 실패할 때
            //     불필요한 요청이 겹쳐 보이는 것을 피한다.
            return askOnce(LOCAL_ORIGINS[0], challenge).then(function (ok) {
                if (ok) return true;
                return askOnce(LOCAL_ORIGINS[1], challenge);
            });
        }
    };
})();
