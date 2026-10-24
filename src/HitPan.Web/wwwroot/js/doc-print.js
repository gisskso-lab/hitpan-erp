// ─── 전표 인쇄 — 미리보기 틀을 인쇄한다 (20261024작2) ────────────────────
//
// 🔴 왜 storage.js 가 아니라 **따로 떼어 모듈로** 두나.
//   storage.js 는 index.html 의 <script src="...?v=">  로 실린다.
//   그런데 index.html 자체가 캐시되면 그 안에 적힌 꼬리표가 **옛 값으로 굳어**
//   아무리 꼬리표를 올려도 브라우저는 옛 js 를 쓴다 — 1.3.62·1.3.63 이 그렇게 샜다.
//   🔴 **꼬리표는 자식 파일을 갈아주지, 꼬리표를 적어둔 부모를 못 갈아준다.**
//   이 모듈은 **C# 쪽(.razor)이 경로로 직접 import** 한다. 그 경로는 DLL 안에 있고
//   DLL 은 index.html 과 다른 길로 갱신되므로, index.html 이 묵어도 이 파일은 새 판이 온다.
//
// 🔴 **돌려주는 것은 bool 이 아니라 「까닭」이다.**
//   종전에는 true/false 라 **어느 칸에서 막혔는지 영영 알 수 없었다.**
//   브라우저는 내가 못 재는 곳이다 ⇒ 다음 실측이 **칸 이름을 말해주게** 만든다.

/**
 * 보고 있는 그 틀(같은 blob)을 인쇄한다. 다시 그리지 않는다(합격선 ⑤).
 * @returns {string} ok-frame | ok-hidden | ok-window | blocked-popup | no-frame | failed-all
 */
export function printPreviewFrame(frame) {
    if (!frame) return 'no-frame';

    // ① 보고 있는 그 틀을 바로 — 같은 blob 출처면 이 길로 간다.
    try {
        frame.contentWindow.focus();
        frame.contentWindow.print();
        return 'ok-frame';
    } catch (e) {
        console.warn('[hitpan.print] ① 틀 직접 인쇄 실패', e);
    }

    // ② 같은 blob 을 숨은 틀에 올려서 — 뷰어 UI 없이 인쇄만.
    //   🔴 같은 주소다. **다시 그리지 않는다** — 종이가 같다는 뜻이다.
    try {
        const hidden = document.createElement('iframe');
        hidden.style.position = 'fixed';
        hidden.style.right = '0';
        hidden.style.bottom = '0';
        hidden.style.width = '0';
        hidden.style.height = '0';
        hidden.style.border = '0';
        hidden.src = frame.src;
        hidden.onload = () => {
            try {
                hidden.contentWindow.focus();
                hidden.contentWindow.print();
            } catch (e2) {
                console.warn('[hitpan.print] ② 숨은 틀 인쇄 실패', e2);
            }
            // 🔴 바로 지우면 인쇄창이 뜨기 전에 사라진다. 넉넉히 두고 치운다.
            setTimeout(() => { try { hidden.remove(); } catch { } }, 60000);
        };
        document.body.appendChild(hidden);
        return 'ok-hidden';
    } catch (e) {
        console.warn('[hitpan.print] ② 숨은 틀 만들기 실패', e);
    }

    // ③ 새 창 — 거기서 뜨는 인쇄창을 쓴다.
    try {
        const w = window.open(frame.src, '_blank');
        if (!w) return 'blocked-popup';
        w.addEventListener('load', () => {
            try { w.print(); } catch (e3) { console.warn('[hitpan.print] ③ 새 창 인쇄 실패', e3); }
        });
        return 'ok-window';
    } catch (e) {
        console.warn('[hitpan.print] ③ 새 창도 막혔다', e);
    }

    return 'failed-all';
}
