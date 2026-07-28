// hitpan-signup.js — 약관 동의 postMessage 브리지 (사장님 결재 2026-06-01)
// legal/*.html 동의 버튼 → window.opener.postMessage → SignupPage.OnLegalAgree
// 정합: 정보주체 권리 보호 + 약관규제법 + 헌법 #15 (silent swallow 금지)
(function () {
    if (window.hitpanSignup) return;

    var dotNetRef = null;
    var handler = null;

    window.hitpanSignup = {
        bindAgree: function (ref) {
            dotNetRef = ref;
            if (handler) window.removeEventListener('message', handler);
            handler = function (e) {
                if (!e || !e.data || e.data.type !== 'hitpan-agree' || !e.data.key) return;
                try {
                    if (dotNetRef) dotNetRef.invokeMethodAsync('OnLegalAgree', e.data.key);
                } catch (err) {
                    console.warn('[hitpanSignup] OnLegalAgree invoke failed:', err);
                }
            };
            window.addEventListener('message', handler);
        },
        unbindAgree: function () {
            if (handler) {
                window.removeEventListener('message', handler);
                handler = null;
            }
            dotNetRef = null;
        }
    };
})();
