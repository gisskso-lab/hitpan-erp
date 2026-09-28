// ⬛ [낡은 머리 주석 · 20260928작2 절H 이전]
//   "민감 토큰(access/refresh)은 sessionStorage — 탭 종료 시 자동 소거, localStorage 대비 XSS 노출 창 짧음.
//    비민감 UI 상태(테마, 필터, 목록 너비 등)는 localStorage 유지.
//    (근본 방어인 HttpOnly 쿠키 전환은 P1 작지서로 별도 처리.)"
//
// 🔴 사장님 9/28 K-6 (가) — 창을 닫으면 자기 접속에 막히던 P0 봉합(20260928작2 절H · 설계 §13-1 · PI-1).
//   [무엇이 났나] PC 1대 차단을 켜자, 창(탭)을 닫는 순간 sessionStorage 의 토큰이 사라지는데 서버의 접속은
//     살아 있어서, 같은 PC 에서 다시 열면 **자기 접속 때문에** 「다른 PC에서 사용 중입니다」(409)로 막혔다.
//   [지금] access·refresh 를 **localStorage** 에 둔다 — 창·브라우저를 다시 열어도, 새 탭에서도 로그인이 이어진다.
//     같은 브라우저의 탭들은 **한 로그인을 나눠 쓴다**(한 탭 로그아웃 = 전 탭 로그아웃).
//   ⚠️ 대가(사장님 수용): 공용 PC 에서 로그아웃 없이 닫으면 다음 사람이 앞 사람 계정으로 열린다(상한 = refresh 7일).
//     로그인 화면의 퇴근 안내 한 줄(절M)이 이것을 알린다. HttpOnly 쿠키는 보안 별건 트랙이다(선행 = API CORS 좁히기).
//   비민감 UI 상태(테마, 필터, 목록 너비 등)는 종전대로 localStorage.
const SESSION_KEYS = new Set(['access_token', 'refresh_token']);

const hitpanStorage = {
    set: (key, value) => {
        // ⬛ [낡은 줄] `const store = SESSION_KEYS.has(key) ? sessionStorage : localStorage;`
        localStorage.setItem(key, value);
        if (SESSION_KEYS.has(key)) {
            // 옛 자리에 남은 값이 있으면 치운다 — 두 곳에 다른 값이 있으면 어느 것이 진짜인지 갈린다.
            sessionStorage.removeItem(key);
        }
    },
    get: (key) => {
        // ⬛ [낡은 줄] `const store = SESSION_KEYS.has(key) ? sessionStorage : localStorage; return store.getItem(key);`
        const v = localStorage.getItem(key);
        if (v !== null || !SESSION_KEYS.has(key)) return v;

        // 🔴 옮겨 오기 — 업데이트 순간 열려 있던 탭은 토큰을 sessionStorage 에 들고 있다.
        //   localStorage 에 없고 sessionStorage 에 있으면 옮기고 sessionStorage 에서 지운다(그 탭이 튕기지 않는다).
        const old = sessionStorage.getItem(key);
        if (old !== null) {
            localStorage.setItem(key, old);
            sessionStorage.removeItem(key);
        }
        return old;
    },
    remove: (key) => {
        // 마이그레이션: 같은 키가 구 localStorage에 남아있을 수 있으므로 둘 다 제거.
        sessionStorage.removeItem(key);
        localStorage.removeItem(key);
    }
};

// Blazor IJSRuntime에서만 호출되도록 전역 노출 최소화.
window.hitpanStorage_set = (key, value) => hitpanStorage.set(key, value);
window.hitpanStorage_get = (key) => hitpanStorage.get(key);
window.hitpanStorage_remove = (key) => hitpanStorage.remove(key);

// ══════════════════════════════════════════════════════════════════
// 🔴 탭 사이 갱신 잠금 (20260928작2 절I · 설계 §13-2 ②).
//
//   토큰을 탭끼리 나눠 쓰게 되자(절H), 두 탭이 **같은 refresh** 로 동시에 갱신을 불렀다.
//   서버는 두 번째를 거절(단일사용)하고, 화면은 그것을 「로그인 끝」으로 읽어 토큰을 지웠다.
//   ⇒ 브라우저 전체에서 갱신을 한 번에 하나로 줄 세운다(navigator.locks — 탭이 닫히면 브라우저가 스스로 푼다).
//
//   hitpanLock_acquire(name, waitMs) → 잡은 번호(1 이상) · 0 = 잠금 없이 진행(지원 안 함·기다림 초과·오류)
//   hitpanLock_release(id)           → 그 번호의 잠금을 푼다
//   ⚠️ 0 이어도 C# 쪽 탭 안 잠금(RefreshGate.Lock)은 그대로 있다. 막히는 쪽으로 실패하지 않는다.
// ══════════════════════════════════════════════════════════════════
const _hitpanLockReleases = new Map();
let _hitpanLockSeq = 0;

window.hitpanLock_acquire = (name, waitMs) => {
    if (!navigator.locks || typeof navigator.locks.request !== 'function') return Promise.resolve(0);

    const id = ++_hitpanLockSeq;
    return new Promise((resolveAcquired) => {
        let settled = false;
        const controller = (typeof AbortController === 'function') ? new AbortController() : null;
        const timer = setTimeout(() => {
            if (settled) return;
            settled = true;
            if (controller) controller.abort();
            console.warn('[hitpanLock] 갱신 잠금 기다림 초과 — 잠금 없이 진행합니다', name);
            resolveAcquired(0);
        }, waitMs || 15000);

        const options = controller ? { signal: controller.signal } : {};
        navigator.locks.request(name, options, () => new Promise((release) => {
            if (settled) { release(); return; }   // 이미 포기했다 — 잡자마자 놓는다
            settled = true;
            clearTimeout(timer);
            _hitpanLockReleases.set(id, release);
            resolveAcquired(id);
        })).catch((e) => {
            if (settled) return;                  // 기다림 초과로 끊은 것(AbortError) — 위에서 이미 알렸다
            settled = true;
            clearTimeout(timer);
            console.warn('[hitpanLock] 갱신 잠금 실패 — 잠금 없이 진행합니다', e);
            resolveAcquired(0);
        });
    });
};

window.hitpanLock_release = (id) => {
    const release = _hitpanLockReleases.get(id);
    if (!release) return;
    _hitpanLockReleases.delete(id);
    release();
};

// ══════════════════════════════════════════════════════════════════
// 🔴 팝업창(메신저)에 로그인 상태를 물려준다. 작(2026-08-14).
//
//   ⬛ [20260928작2 절H] 토큰이 localStorage 로 옮겨 가 **팝업도 같은 저장소를 본다** ⇒ 이 함수가 물려줄 일은
//     사실상 없다(sessionStorage 에 토큰이 없다). 옛 판(업데이트 전)에 열린 부모 창을 위해 **남긴다** — 지우지 않는다.
//     아래 「왜 localStorage 로 안 옮기나」 서술은 K-6 (가) 이전의 판단이다.
//
//   ■ 무엇을 겪고서
//     사장님 지적: "히트판메신저 팝업은 여전히 안됨" — 창은 뜨는데 **빈 화면**이었다.
//     진범: 토큰이 sessionStorage 인데 **sessionStorage 는 창마다 따로다.**
//     window.open 으로 연 새 창은 자기만의 **빈** sessionStorage 를 갖는다 ⇒
//     팝업이 토큰을 못 찾아 [Authorize] 에 걸려 아무것도 안 그렸다.
//
//   ■ 왜 localStorage 로 안 옮기나
//     그러면 한 줄로 끝나지만 **보안이 나빠진다** — 이 파일 첫 줄이 밝힌 대로
//     민감 토큰을 sessionStorage 에 둔 것은 XSS 노출 창을 줄이려는 판단이다.
//     팝업 하나 때문에 그 판단을 뒤집지 않는다.
//
//   ■ 그래서 부모 창에서 **물려받는다**
//     window.open 으로 연 창은 opener 로 부모를 가리킨다. **같은 출처**라
//     토큰이 브라우저 밖으로 나가지 않는다 — 저장 정책은 그대로다.
//     ⚠️ 다른 출처면 opener 접근 자체가 예외라 try 로 감싼다(그 경우 로그인 화면으로 간다).
// ══════════════════════════════════════════════════════════════════
window.hitpanStorage_inheritFromOpener = () => {
    try {
        // 부모가 없으면(직접 주소를 친 경우) 물려받을 것도 없다.
        if (!window.opener || window.opener.closed) return false;

        var inherited = false;
        SESSION_KEYS.forEach(key => {
            // 이미 있으면 덮지 않는다 — 이 창에서 따로 로그인했을 수 있다.
            if (sessionStorage.getItem(key)) return;

            var v = window.opener.sessionStorage.getItem(key);
            if (v) {
                sessionStorage.setItem(key, v);
                inherited = true;
            }
        });

        return inherited;
    } catch (e) {
        // 다른 출처면 여기로 온다. 막힌 게 정상이고, 팝업은 로그인 화면을 띄운다.
        console.warn('[hitpanStorage] 부모 창에서 로그인 상태를 물려받지 못했습니다', e);
        return false;
    }
};

// 바이트 배열(Base64) → 브라우저 파일 다운로드
window.downloadFileFromBytes = (fileName, contentType, base64) => {
    const bin = atob(base64);
    const len = bin.length;
    const bytes = new Uint8Array(len);
    for (let i = 0; i < len; i++) bytes[i] = bin.charCodeAt(i);
    const blob = new Blob([bytes], { type: contentType });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};

// ── 자료 인쇄 (사장님 지시 2026-08-12) ───────────────────────────
// 🔴 종전에는 그냥 window.print() 였다. 종이에 **표만** 나와서
//    무슨 자료인지·어느 기간인지 적혀 있지 않았다.
//    거래처나 세무사에게 넘기면 받는 사람이 알 수가 없다(우리 화면을 모르니까).
// ⇒ 인쇄 직전에만 머리말을 붙이고, 끝나면 반드시 지운다.
//    지우지 않으면 화면에 남아 다음 인쇄 때 두 번 찍힌다.
window.hitpanPrintGrid = (title, subtitle) => {
    const ID = 'hitpan-print-header';
    document.getElementById(ID)?.remove();   // 앞선 인쇄가 남긴 것이 있으면 먼저 치운다

    const box = document.createElement('div');
    box.id = ID;

    const t = document.createElement('div');
    t.className = 'hp-print-title';
    t.textContent = title || '자료';        // textContent — 제목에 든 <> 가 태그로 해석되지 않게
    box.appendChild(t);

    if (subtitle) {
        const s = document.createElement('div');
        s.className = 'hp-print-sub';
        s.textContent = subtitle;
        box.appendChild(s);
    }

    const stamp = document.createElement('div');
    stamp.className = 'hp-print-stamp';
    const n = new Date();
    const p = (v) => String(v).padStart(2, '0');
    stamp.textContent = `출력: ${n.getFullYear()}-${p(n.getMonth() + 1)}-${p(n.getDate())} `
        + `${p(n.getHours())}:${p(n.getMinutes())}`;
    box.appendChild(stamp);

    document.body.insertBefore(box, document.body.firstChild);

    const cleanup = () => document.getElementById(ID)?.remove();
    // 인쇄창을 닫은 뒤 치운다. 브라우저마다 시점이 달라 두 갈래를 다 건다.
    window.addEventListener('afterprint', cleanup, { once: true });

    try {
        window.print();
    } finally {
        // afterprint 를 안 주는 브라우저가 있어 보험을 둔다 —
        //   안 지우면 화면에 머리말이 남는다.
        setTimeout(cleanup, 1000);
    }
};
