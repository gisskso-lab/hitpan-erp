(function () {
    if (window.hitpanSignup) return;

    var dotnetRef = null;
    var handler = null;

    function onMessage(e) {
        var data = e && e.data;
        if (!data || typeof data !== 'object') return;
        if (data.type !== 'hitpan-agree') return;
        var key = data.key;
        if (key !== 'service' && key !== 'privacy' && key !== 'outsourcing' && key !== 'marketing') return;
        if (dotnetRef) {
            try { dotnetRef.invokeMethodAsync('OnLegalAgree', key); } catch (err) { }
        }
    }

    window.hitpanSignup = {
        bindAgree: function (ref) {
            dotnetRef = ref;
            if (handler) { window.removeEventListener('message', handler); }
            handler = onMessage;
            window.addEventListener('message', handler, false);
        },
        unbindAgree: function () {
            if (handler) {
                window.removeEventListener('message', handler);
                handler = null;
            }
            dotnetRef = null;
        }
    };
})();
