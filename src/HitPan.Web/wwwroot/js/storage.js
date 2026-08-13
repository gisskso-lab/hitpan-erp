// 민감 토큰(access/refresh)은 sessionStorage — 탭 종료 시 자동 소거, localStorage 대비 XSS 노출 창 짧음.
// 비민감 UI 상태(테마, 필터, 목록 너비 등)는 localStorage 유지.
// (근본 방어인 HttpOnly 쿠키 전환은 P1 작지서로 별도 처리.)
const SESSION_KEYS = new Set(['access_token', 'refresh_token']);

const hitpanStorage = {
    set: (key, value) => {
        const store = SESSION_KEYS.has(key) ? sessionStorage : localStorage;
        store.setItem(key, value);
    },
    get: (key) => {
        const store = SESSION_KEYS.has(key) ? sessionStorage : localStorage;
        return store.getItem(key);
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
// 🔴 팝업창(메신저)에 로그인 상태를 물려준다. 작(2026-08-14).
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
