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

    /// 🔴 20261028작G1-1 (K35) — 알림을 눌러 **그 대화방**으로 채팅 창을 연다. (추가만 · 위 open 은 그대로)
    ///
    /// open 은 창이 이미 떠 있으면 focus 만 한다 — 알림을 누르면 그 방으로 **가야** 하므로 따로 둔다.
    ///   ① 창이 살아 있으면 그 창을 url 로 보낸다 → focus → true.
    ///      팝업 쪽 Blazor.navigateTo 가 있으면 그것(앱 재시작 없음 · .NET 8 WASM window.Blazor 공개 함수),
    ///      없거나(아직 부팅 전) 실패하면 location.assign(url).
    ///   ② 살아 있지 않으면 기존 open(url) 을 부른다 — 같은 크기·같은 창 이름 'hitpanChat'.
    ///      본 창을 새로고침해 _win 을 잃어도 같은 이름이라 브라우저가 떠 있는 팝업을 재사용해 그 주소로 보낸다.
    ///   팝업 차단·예외는 false — 화면이 안내하고 본 창으로 대체한다(조용한 실패 금지).
    openRoom: function (url) {
        try {
            if (this._win && !this._win.closed) {
                var moved = false;
                try {
                    var b = this._win.Blazor;
                    if (b && typeof b.navigateTo === 'function') {
                        b.navigateTo(url);
                        moved = true;
                    }
                } catch (navErr) {
                    console.error('[hitpanChat] 팝업 안 이동 실패 — 주소로 다시 엽니다', navErr);
                }
                if (!moved) this._win.location.assign(url);

                this._win.focus();
                return true;
            }

            return this.open(url);
        } catch (e) {
            console.error('[hitpanChat] 대화방 창을 열지 못했습니다', e);
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
