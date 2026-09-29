// 🔴 W-3 (20260928작2 개정3 절S·U · 설계 §14-4·§14-6) — storage.js 를 **실제로** 가짜 저장소 위에 올려 부른다.
//   글자 검사가 아니다: 파일을 vm 에서 돌리고 window.hitpanStorage_* 를 호출해 저장소 상태를 잰다.
//   실행: node --test tests/node/   (CI build 잡 「W-3」 단계)
//   🔴 음성 대조군 — ① 비교 없이 지우면 「다름」 줄 FAIL ② 표식을 무시하면 「표식 있음」 줄 FAIL.
'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const SRC = fs.readFileSync(
    path.join(__dirname, '..', '..', 'src', 'HitPan.Web', 'wwwroot', 'js', 'storage.js'), 'utf8');

class FakeStorage {
    constructor() { this.m = new Map(); }
    getItem(k) { return this.m.has(k) ? this.m.get(k) : null; }
    setItem(k, v) { this.m.set(k, String(v)); }
    removeItem(k) { this.m.delete(k); }
}

function load() {
    const localStorage = new FakeStorage();
    const sessionStorage = new FakeStorage();
    const window = {};
    vm.runInNewContext(SRC, { window, localStorage, sessionStorage, navigator: {}, console });
    return { window, localStorage, sessionStorage };
}

const A = 'hitpan_access_token';
const R = 'hitpan_refresh_token';
const N = 'hitpan_user_name';
const MARK = 'hitpan_token_store_v2';

test('W-3 ① 같음 → 로그인 칸 전부(양쪽 저장소) 지움 · removed', () => {
    const { window, localStorage, sessionStorage } = load();
    localStorage.setItem(A, 'acc'); localStorage.setItem(R, 'ref1'); localStorage.setItem(N, 'name');
    sessionStorage.setItem(A, 'oldacc');
    assert.equal(window.hitpanStorage_removeAuthIfRefreshIs('ref1'), 'removed');
    for (const k of [A, R, N]) {
        assert.equal(localStorage.getItem(k), null, k);
        assert.equal(sessionStorage.getItem(k), null, k);
    }
});

test('W-3 ② 다름 → 안 지움 · rotated (남의 탭이 새 토큰을 썼다)', () => {
    const { window, localStorage } = load();
    localStorage.setItem(A, 'acc2'); localStorage.setItem(R, 'ref2'); localStorage.setItem(N, 'name');
    assert.equal(window.hitpanStorage_removeAuthIfRefreshIs('ref1'), 'rotated');
    assert.equal(localStorage.getItem(R), 'ref2');
    assert.equal(localStorage.getItem(A), 'acc2');
    assert.equal(localStorage.getItem(N), 'name');
});

test('W-3 ③ 저장소에 refresh 없음 → empty', () => {
    const { window, localStorage } = load();
    localStorage.setItem(A, 'acc');
    assert.equal(window.hitpanStorage_removeAuthIfRefreshIs('ref1'), 'empty');
    assert.equal(localStorage.getItem(R), null);
});

test('W-3 ④ 표식 없음 + localStorage 비었음 + sessionStorage 있음 → 두 칸 다 옮김 + 표식', () => {
    const { window, localStorage, sessionStorage } = load();
    sessionStorage.setItem(A, 'sacc'); sessionStorage.setItem(R, 'sref');
    assert.equal(window.hitpanStorage_get(A), 'sacc');
    assert.equal(window.hitpanStorage_get(R), 'sref');   // 첫 칸을 옮긴 뒤에도 둘째 칸이 막히지 않는다
    assert.equal(localStorage.getItem(A), 'sacc');
    assert.equal(localStorage.getItem(R), 'sref');
    assert.equal(sessionStorage.getItem(A), null);
    assert.equal(sessionStorage.getItem(R), null);
    assert.notEqual(localStorage.getItem(MARK), null);
});

test('W-3 ⑤ 표식 있음 → 안 옮김 + sessionStorage 토큰 칸 지움', () => {
    const { window, localStorage, sessionStorage } = load();
    localStorage.setItem(MARK, '1');
    sessionStorage.setItem(A, 'sacc'); sessionStorage.setItem(R, 'sref');
    assert.equal(window.hitpanStorage_get(R), null);
    assert.equal(localStorage.getItem(R), null);
    assert.equal(localStorage.getItem(A), null);
    assert.equal(sessionStorage.getItem(A), null);
    assert.equal(sessionStorage.getItem(R), null);
});

test('W-3 ⑥ remove 는 표식하지 않는다 · set(토큰 칸)은 표식한다', () => {
    const { window, localStorage } = load();
    window.hitpanStorage_remove(R);
    assert.equal(localStorage.getItem(MARK), null);
    window.hitpanStorage_set('theme', 'dark');
    assert.equal(localStorage.getItem(MARK), null);   // 토큰 칸이 아니면 표식 안 함
    window.hitpanStorage_set(R, 'ref');
    assert.notEqual(localStorage.getItem(MARK), null);
});
