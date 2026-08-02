// 히트판 전역 단축키 모듈
// Ctrl+K (또는 macOS ⌘+K) 입력 시 상단 챗봇 바를 연다.
// 입력창(Input/Textarea/ContentEditable)에서 충돌 없이 동작하도록 기본 동작만 막는다.
window.addEventListener('keydown', function (e) {
    var isCmdOrCtrl = e.ctrlKey || e.metaKey;
    if (!isCmdOrCtrl) return;
    if (e.key !== 'k' && e.key !== 'K') return;

    // 챗봇 바가 존재할 때만 개입한다 (로그인 전 페이지 등에서는 무시)
    var bar = document.querySelector('.hitpan-chatbot-input');
    if (!bar) return;

    e.preventDefault();
    bar.click();
});
