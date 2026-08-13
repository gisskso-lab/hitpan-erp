// 메신저 팝업창 — 작(2026-08-14)
//
// 🔴 사장님 지시 (2026-08-13~14):
//   "메신저를 별도 팝업창으로 구현하자고 했는데, 사장의 요구대로 안됨."
//   "pc카카오톡 사이즈로 팝업을 띄워 채팅운영을 할 것!!!"
//
// 종전 구현은 화면 안 오른쪽 아래에 붙는 <div> 였다. 말만 팝업이고 진짜 창이 아니라
// ERP 화면을 가렸다 — 사장님이 말씀하신 **일하면서 대화하는 멀티태스킹**이 안 됐다.
//
// ⇒ 진짜 브라우저 창으로 띄운다. PC 카카오톡 크기(약 400×680)에 맞췄다.
window.hitpanChat = {
    // 이미 열린 창을 기억한다. 버튼을 다시 누르면 새 창을 또 만들지 않고
    // 있던 창을 앞으로 가져온다 — 카카오톡과 같은 동작이다.
    _win: null,

    /// 채팅 창을 연다. 이미 열려 있으면 그 창에 초점을 준다.
    open: function (url) {
        try {
            // 열려 있고 안 닫혔으면 그걸 쓴다.
            if (this._win && !this._win.closed) {
                this._win.focus();
                return true;
            }

            // PC 카카오톡 크기. 화면 오른쪽에 붙여 띄운다 — 왼쪽에 ERP 를 두고 쓰는 자세다.
            var w = 400;
            var h = 680;
            var left = Math.max(0, (window.screen.availWidth || 1280) - w - 40);
            var top = Math.max(0, Math.floor(((window.screen.availHeight || 800) - h) / 2));

            var features = [
                'width=' + w,
                'height=' + h,
                'left=' + left,
                'top=' + top,
                'resizable=yes',      // 사람마다 화면이 다르다. 크기는 고객이 정한다
                'scrollbars=yes',
                'status=no',
                'toolbar=no',
                'menubar=no',
                'location=no'
            ].join(',');

            // 창 이름을 고정한다 — 같은 이름이면 브라우저가 새 창을 안 만들고 재사용한다.
            this._win = window.open(url, 'hitpanChat', features);

            // 팝업 차단기에 막히면 null 이 온다. 🔴 조용히 실패하면 사용자는
            // "눌렀는데 아무 일도 안 난다" 만 겪는다 — false 를 돌려 화면이 안내하게 한다.
            if (!this._win) return false;

            this._win.focus();
            return true;
        } catch (e) {
            console.error('[hitpanChat] 창을 열지 못했습니다', e);
            return false;
        }
    },

    /// 열려 있는 채팅 창을 닫는다.
    close: function () {
        try {
            if (this._win && !this._win.closed) this._win.close();
            this._win = null;
        } catch (e) {
            console.error('[hitpanChat] 창을 닫지 못했습니다', e);
        }
    }
};
