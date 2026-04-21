window.hitpanStorage = {
    set: (key, value) => localStorage.setItem(key, value),
    get: (key) => localStorage.getItem(key),
    remove: (key) => localStorage.removeItem(key)
};

window.hitpanStorage_set = (key, value) => window.hitpanStorage.set(key, value);
window.hitpanStorage_get = (key) => window.hitpanStorage.get(key);
window.hitpanStorage_remove = (key) => window.hitpanStorage.remove(key);

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
