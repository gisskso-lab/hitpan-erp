// ════════════════════════════════════════════════════════════════
// 히트판 챗봇 패널 드래그 이동
// - 헤더(.hitpan-chatbot-panel-header) 잡고 드래그
// - 화면 밖으로 나가지 않도록 경계 제한
// - 닫혔다가 다시 열리면 이동 위치 유지
// ════════════════════════════════════════════════════════════════
(function () {
    let savedLeft = null;
    let savedTop = null;
    let dragState = null;

    function getPanel() {
        return document.querySelector('.hitpan-chatbot-panel');
    }

    function getHeader(panel) {
        return panel.querySelector('.hitpan-chatbot-panel-header');
    }

    function onMouseDown(e) {
        // 닫기 버튼 등 인터랙션 요소는 드래그 제외
        if (e.target.closest('button') || e.target.closest('.mud-icon-button')) return;

        const panel = getPanel();
        if (!panel) return;

        const rect = panel.getBoundingClientRect();
        dragState = {
            panel: panel,
            offsetX: e.clientX - rect.left,
            offsetY: e.clientY - rect.top,
            width: rect.width,
            height: rect.height
        };

        // 고정 좌표로 전환 (최초 드래그 시)
        panel.style.left = rect.left + 'px';
        panel.style.top = rect.top + 'px';
        panel.style.right = 'auto';
        panel.style.bottom = 'auto';

        document.body.style.userSelect = 'none';
        e.preventDefault();
    }

    function onMouseMove(e) {
        if (!dragState) return;

        let newLeft = e.clientX - dragState.offsetX;
        let newTop = e.clientY - dragState.offsetY;

        // 화면 경계 제한 (상하좌우 최소 20px 여백)
        const maxLeft = window.innerWidth - dragState.width - 20;
        const maxTop = window.innerHeight - dragState.height - 20;
        newLeft = Math.max(20, Math.min(newLeft, maxLeft));
        newTop = Math.max(20, Math.min(newTop, maxTop));

        dragState.panel.style.left = newLeft + 'px';
        dragState.panel.style.top = newTop + 'px';

        savedLeft = newLeft;
        savedTop = newTop;
    }

    function onMouseUp() {
        if (dragState) {
            dragState = null;
            document.body.style.userSelect = '';
        }
    }

    // 패널이 새로 렌더링될 때 드래그 바인딩 + 저장된 위치 복원
    const observer = new MutationObserver(function (mutations) {
        for (const m of mutations) {
            for (const node of m.addedNodes) {
                if (node.nodeType !== 1) continue;
                const panel = node.classList?.contains('hitpan-chatbot-panel')
                    ? node
                    : node.querySelector?.('.hitpan-chatbot-panel');
                if (!panel) continue;

                const header = getHeader(panel);
                if (!header) continue;

                // 드래그 핸들 스타일
                header.style.cursor = 'move';
                header.title = '드래그하여 이동';
                header.addEventListener('mousedown', onMouseDown);

                // 저장된 위치 복원
                if (savedLeft !== null && savedTop !== null) {
                    panel.style.left = savedLeft + 'px';
                    panel.style.top = savedTop + 'px';
                    panel.style.right = 'auto';
                    panel.style.bottom = 'auto';
                }
            }
        }
    });

    observer.observe(document.body, { childList: true, subtree: true });
    document.addEventListener('mousemove', onMouseMove);
    document.addEventListener('mouseup', onMouseUp);
})();
