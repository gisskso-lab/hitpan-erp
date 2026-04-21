// 히트판 디바이스 지문(fingerprint) 유틸.
// - 히트판은 디바이스 수 과금 — 기기 식별용 지문을 로그인 시 서버로 전달한다.
// - 서버는 같은 (tenant_id, fingerprint) 조합이면 기존 기기로 간주, 없으면 신규 등록.
window.hitpanDevice = {
    // 브라우저 환경값 기반 간이 해시 (SHA-256이 아니라 js 내장 문자열 해시).
    // 서버 쪽 fingerprint 컬럼은 SHA-256 문자열 폭에 맞춰놨지만, 길이 제약만 맞으면 됨.
    getFingerprint: function () {
        var parts = [
            navigator.userAgent,
            navigator.platform,
            screen.width + 'x' + screen.height,
            (Intl && Intl.DateTimeFormat) ? Intl.DateTimeFormat().resolvedOptions().timeZone : '',
            navigator.language || ''
        ];
        var s = parts.join('|');
        var hash = 0;
        for (var i = 0; i < s.length; i++) {
            hash = ((hash << 5) - hash) + s.charCodeAt(i);
            hash |= 0;
        }
        // 16진수 16자리 패딩
        var hex = Math.abs(hash).toString(16);
        while (hex.length < 16) hex = '0' + hex;
        return hex;
    },

    // UA 기반 디바이스 타입 간이 판별
    getDeviceType: function () {
        var ua = navigator.userAgent || '';
        if (/iPad|Tablet/i.test(ua)) return 'tablet';
        if (/Mobi|Android|iPhone|iPod/i.test(ua)) return 'mobile';
        return 'pc';
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
    }
};
