/*
 * 히트판 ERP — 키보드 네비게이션 v2.0 (사장님 헌법 2026-04-29 재정의)
 *
 *   "값 변경 안 한 칸은 1번 엔터로 통과,
 *    값 넣은 칸은 1번 엔터=확정 / 2번 엔터=다음 칸. 그리고 방향키로도 이동."
 *
 * 동작 규칙:
 *   [Enter — 폼 입력]
 *     · dirty 아님 (값 미변경)        → 1번 Enter = 다음 칸
 *     · dirty (값 변경 후 미확정)     → 1번 Enter = 확정(blur+change 디스패치, 포커스 유지, dirty 해제)
 *                                      → 2번 Enter = 다음 칸
 *     · Shift+Enter                   → 이전 칸 (dirty 무시)
 *     · 마지막 칸 + dirty 아님         → submit 자동 클릭
 *
 *   [방향키 — 폼 입력]
 *     · ↓                             → 다음 칸
 *     · ↑                             → 이전 칸
 *     · → (캐럿이 텍스트 끝일 때만)   → 다음 칸
 *     · ← (캐럿이 텍스트 시작일 때만) → 이전 칸
 *     · 숫자 input 의 ↑↓ spin 동작은 칸 이동으로 통일 (ERP에서 spin 거의 미사용)
 *
 *   [그리드 (.mud-table 등)]
 *     · Enter 2단계 동일 적용
 *     · ↑/↓                           → 같은 컬럼 위/아래 행 (기존 동작 유지)
 *
 * 예외 (Enter/방향키 본래 동작 유지):
 *   - <textarea>      줄바꿈/캐럿 이동
 *   - <button>, <a>   클릭/이동
 *   - data-keyboard-nav="off"
 *   - role="combobox" + aria-expanded="true" (드롭다운 항목 선택)
 *   - .mud-popover-open 떠 있을 때 (MudSelect/MudAutocomplete 열림)
 *
 * 기존 코드 영향 0건 — 글로벌 keydown 리스너 1개 + dirty 추적 listener 1개.
 * 백엔드/DB/워크플로우 영향 0건.
 */

(function () {
    'use strict';

    const FOCUSABLE = [
        'input:not([type="hidden"]):not([type="button"]):not([type="submit"]):not([type="reset"]):not([disabled]):not([readonly])',
        'select:not([disabled])',
        '[contenteditable="true"]',
        '[tabindex]:not([tabindex="-1"]):not(button):not(a):not(textarea)'
    ].join(', ');

    // dirty 상태를 element 자체에 저장 (WeakMap 으로 GC 안전)
    const dirtyMap = new WeakMap();
    const baselineMap = new WeakMap();

    function getValue(el) {
        if (el.type === 'checkbox' || el.type === 'radio') return el.checked ? '1' : '0';
        return el.value != null ? el.value : '';
    }

    function isDirty(el) {
        if (!baselineMap.has(el)) return false;
        return getValue(el) !== baselineMap.get(el);
    }

    function captureBaseline(el) {
        baselineMap.set(el, getValue(el));
        dirtyMap.set(el, false);
    }

    function commitValue(el) {
        // 1번 엔터 = 확정. blur 안 시키고 값만 굳힌다 (포커스 유지가 사장님 요구).
        // change 이벤트 디스패치 → Blazor 바인딩이 갱신되도록.
        try { el.dispatchEvent(new Event('change', { bubbles: true })); } catch (_) {}
        try { el.dispatchEvent(new Event('input', { bubbles: true })); } catch (_) {}
        baselineMap.set(el, getValue(el));
        dirtyMap.set(el, false);

        // 시각 피드백: 살짝 깜빡 (CSS 클래스 토글)
        el.classList.add('hp-kb-committed');
        setTimeout(() => el.classList.remove('hp-kb-committed'), 180);
    }

    function isEnterNativeContext(target) {
        if (!target || !target.tagName) return false;
        const tag = target.tagName.toUpperCase();
        if (tag === 'TEXTAREA') return true;
        if (tag === 'BUTTON') return true;
        if (tag === 'A') return true;
        if (target.isContentEditable) return true;
        if (target.dataset && target.dataset.keyboardNav === 'off') return true;
        if (target.getAttribute('role') === 'combobox' &&
            target.getAttribute('aria-expanded') === 'true') return true;
        if (document.querySelector('.mud-popover-open')) return true;
        return false;
    }

    function isInsideGrid(target) {
        return target.closest && target.closest('.mud-table, .mud-data-grid, [data-grid-cell]');
    }

    function getFocusables(from) {
        const container = from.closest('form, .mud-paper, .mud-dialog-content, .hp-form-scope') || document.body;
        const all = Array.from(container.querySelectorAll(FOCUSABLE));
        return all.filter(el => {
            if (el.offsetParent === null) return false;
            const rect = el.getBoundingClientRect();
            return rect.width > 0 && rect.height > 0;
        });
    }

    function moveFocus(current, direction) {
        const list = getFocusables(current);
        const idx = list.indexOf(current);
        if (idx < 0) return false;

        const next = direction === 'next' ? idx + 1 : idx - 1;
        if (next < 0 || next >= list.length) {
            if (direction === 'next') {
                const submit = current.closest('form')?.querySelector(
                    'button[type="submit"], button.hp-submit, .mud-button-filled-primary');
                if (submit && !submit.disabled) {
                    submit.click();
                    return true;
                }
            }
            return false;
        }

        const target = list[next];
        target.focus();
        if (target.select && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA')) {
            try { target.select(); } catch (_) {}
        }
        // 새 칸 진입 시 baseline 캡처 (dirty 추적 시작점)
        captureBaseline(target);
        return true;
    }

    // 캐럿 위치 판단
    function caretAtStart(el) {
        if (el.selectionStart == null) return true;
        return el.selectionStart === 0 && el.selectionEnd === 0;
    }
    function caretAtEnd(el) {
        if (el.selectionStart == null) return true;
        const len = (el.value || '').length;
        return el.selectionStart === len && el.selectionEnd === len;
    }

    // 그리드 셀 네비게이션
    function handleGridKey(e, target) {
        const cell = target.closest('td, .mud-table-cell, [data-grid-cell]');
        if (!cell) return false;
        const row = cell.closest('tr, .mud-table-row');
        if (!row) return false;

        if (e.key === 'Enter') {
            // Enter 2단계: dirty 면 1번=확정, 2번=이동
            if (isDirty(target)) {
                commitValue(target);
                return true;
            }
            const cells = Array.from(row.querySelectorAll(FOCUSABLE)).filter(el => el.offsetParent !== null);
            const idx = cells.indexOf(target);
            if (idx >= 0 && idx < cells.length - 1) {
                cells[idx + 1].focus();
                if (cells[idx + 1].select) try { cells[idx + 1].select(); } catch (_) {}
                captureBaseline(cells[idx + 1]);
                return true;
            }
            const next = row.nextElementSibling;
            if (next) {
                const nextInputs = Array.from(next.querySelectorAll(FOCUSABLE)).filter(el => el.offsetParent !== null);
                if (nextInputs.length > 0) {
                    nextInputs[0].focus();
                    if (nextInputs[0].select) try { nextInputs[0].select(); } catch (_) {}
                    captureBaseline(nextInputs[0]);
                    return true;
                }
            }
            return false;
        }

        if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
            const sibRow = e.key === 'ArrowDown' ? row.nextElementSibling : row.previousElementSibling;
            if (!sibRow) return false;
            const cellIndex = Array.from(row.children).indexOf(cell);
            const sibCell = sibRow.children[cellIndex];
            if (sibCell) {
                const input = sibCell.querySelector(FOCUSABLE);
                if (input) {
                    input.focus();
                    if (input.select) try { input.select(); } catch (_) {}
                    captureBaseline(input);
                    return true;
                }
            }
        }

        return false;
    }

    // 포커스 들어올 때 baseline 캡처 (사용자 입력 시작점 = 깨끗한 상태)
    document.addEventListener('focusin', function (e) {
        const t = e.target;
        if (!t || !t.tagName) return;
        const tag = t.tagName.toUpperCase();
        if (tag !== 'INPUT' && tag !== 'SELECT' && tag !== 'TEXTAREA') return;
        captureBaseline(t);
    }, true);

    document.addEventListener('keydown', function (e) {
        if (window._hitpanKbNavDisabled) return;
        if (!['Enter', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(e.key)) return;

        const target = e.target;
        if (!target || !target.tagName) return;

        // 그리드 안이면 그리드 핸들러 우선 (Enter / ↑↓ 만 처리)
        if (isInsideGrid(target)) {
            if (e.key === 'Enter' || e.key === 'ArrowDown' || e.key === 'ArrowUp') {
                if (handleGridKey(e, target)) {
                    e.preventDefault();
                }
                return;
            }
            // 그리드 내 ←→ 는 본래 동작
            return;
        }

        const tag = target.tagName.toUpperCase();
        if (tag !== 'INPUT' && tag !== 'SELECT') return;

        // Enter 처리
        if (e.key === 'Enter') {
            if (isEnterNativeContext(target)) return;

            // Shift+Enter → 무조건 이전 칸 (확정 무시)
            if (e.shiftKey) {
                if (moveFocus(target, 'prev')) e.preventDefault();
                return;
            }

            // dirty 면 1번 엔터 = 확정 (포커스 유지)
            if (isDirty(target)) {
                commitValue(target);
                e.preventDefault();
                return;
            }

            // dirty 아니면 다음 칸
            if (moveFocus(target, 'next')) e.preventDefault();
            return;
        }

        // 방향키 처리
        if (e.key === 'ArrowDown') {
            // select 의 ↓는 옵션 이동이라 본래 동작 유지
            if (tag === 'SELECT') return;
            if (moveFocus(target, 'next')) e.preventDefault();
            return;
        }

        if (e.key === 'ArrowUp') {
            if (tag === 'SELECT') return;
            if (moveFocus(target, 'prev')) e.preventDefault();
            return;
        }

        if (e.key === 'ArrowRight') {
            if (tag !== 'INPUT') return;
            // 캐럿이 끝일 때만 다음 칸 (그렇지 않으면 텍스트 내 캐럿 이동)
            if (!caretAtEnd(target)) return;
            if (moveFocus(target, 'next')) e.preventDefault();
            return;
        }

        if (e.key === 'ArrowLeft') {
            if (tag !== 'INPUT') return;
            if (!caretAtStart(target)) return;
            if (moveFocus(target, 'prev')) e.preventDefault();
            return;
        }
    }, /* useCapture */ false);

    window.hitpanKbNav = {
        version: '2.0.0',
        disable: function () {
            window._hitpanKbNavDisabled = true;
            console.log('[hitpan-keyboard-nav] disabled');
        },
        enable: function () {
            window._hitpanKbNavDisabled = false;
            console.log('[hitpan-keyboard-nav] enabled');
        },
        // 디버깅: 현재 포커스 요소의 dirty 상태 확인
        debugDirty: function () {
            const el = document.activeElement;
            console.log('[hitpan-keyboard-nav] activeElement:', el,
                        'baseline:', baselineMap.get(el),
                        'current:', el ? getValue(el) : null,
                        'dirty:', el ? isDirty(el) : null);
        }
    };

    console.log('[hitpan-keyboard-nav] v2.0 loaded — Enter 2단계 + 방향키 이동');
})();
