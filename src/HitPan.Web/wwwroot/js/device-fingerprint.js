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
