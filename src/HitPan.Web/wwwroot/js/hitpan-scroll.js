// 히트판 화면 이동(스크롤) 모듈
//
// 왜 필요한가 (2026-08-24 작4 ② · 사장님 실측이 잡은 실패):
//   결재설정에서 「설정」 을 눌러도 **화면이 움직이지 않았다.**
//   편집 영역은 표 아래에 나타나는데 문서유형이 10줄이라 화면 밖이었다 —
//   눌러도 아무 일도 안 일어난 것처럼 보였다.
//   사장님: *"고객입장에서 «설정 아이콘 눌렀는데, 결재설정 어떻게 해요?» 라고 오해할수도"*
//
// 🔴 1차 시도가 실패한 이유 — MudBlazor `ScrollManager.ScrollToAsync` 는 **창(window)** 을 스크롤한다.
//   그런데 이 앱은 창이 스크롤되지 않는다. `MainLayout.razor:69` 의
//   `<main class="hitpan-content">` 가 `overflow:auto` 인 **별도 스크롤 컨테이너**이고,
//   페이지 내용은 전부 그 안에 있다(hitpan.css:70-74).
//   ⇒ 창은 이미 끝까지 있는 상태라 아무 일도 안 일어났다. **오류도 안 났다.**
//
// `scrollIntoView` 는 **스크롤 가능한 조상을 브라우저가 알아서 찾아** 올라간다.
// 레이아웃이 바뀌어도 따라간다 — 좌표를 우리가 계산하지 않는다.

window.hitpanScroll = {
    /**
     * 주어진 id 를 가진 요소가 보이도록 화면을 옮긴다.
     *
     * @param {string} elementId 대상 요소의 id (# 없이)
     * @returns {boolean} 실제로 옮겼으면 true, 대상을 못 찾았으면 false
     *
     * 🔴 반환값을 주는 이유: 호출부가 **"움직였나"** 를 알 수 있어야 한다.
     *    조용히 실패하면 이번 같은 사고를 또 못 잡는다.
     */
    toElement: function (elementId) {
        var el = document.getElementById(elementId);
        if (!el) {
            // 대상이 없다 = 아직 렌더 전이거나 id 가 바뀐 것. 호출부가 판단하게 알린다.
            return false;
        }
        el.scrollIntoView({ behavior: 'smooth', block: 'start' });
        return true;
    }
};
