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
