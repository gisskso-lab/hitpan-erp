# 조사서 06 — ERP 채팅(사내 메신저) · 전수조사 20261027

## §0 [카드]

| 칸 | 값 |
|---|---|
| 조사 범위 | **조사 26 / 분모 27 · 미조사 1** (§A 27행 · 미조사 행은 분모에 남김 · 지시서 §11 조건 4) |
| ⓐ 사장 확인 | **통과 4 / 27** (§A-1 팝업창 열기 · §A-8 대화 불러오기 · §A-10 보내기 · §A-12 읽음 — 전부 1.2.80) |
| ⓑ 코드리뷰 | **50.0** — ①⬜ 22행 평균 (조사된 26행 기준 · 미조사 1행 제외) |
| ⓒ 종합 | **57.7** — 조사된 26행 평균 (미조사 1행 제외) |
| 한 줄 결론 | 베타에서 가장 먼저 막히는 것 = **받은 파일을 못 내려받는다**(§A-16 · 맨 링크라 인증 머리가 안 실린다 → 401 추정). 그 다음 = **팝업창(주 경로)에는 실시간 수신 배관이 켜지지 않는다**(§A-11) |
| 기준판 · 조사자 · 읽은 파일 수 | `b9f34ccd` · hp-verifier ⑥(검증팀장 데이비드 박 관점) · **21개** (코드 17 · 문서 4) |
| 방식 | 코드·git·문서 읽기만 · 새 실측 0 · DB 조회 0 · 코드 수정 0 |

## §A [기능목록]

> ① 근거: 사장님 8/15 06:34 실측 코멘트 *「잘됨, 그러나 UI 쪽은 좀 더 손 봐야될듯」* — 인용 문서 `docs/운영기록/20260815_인수인계서_설치P0닫힘_메신저봉합_1.2.80게시_참조식.md` §② (판 = 1.2.80 · 봉합 커밋 `c35e0de0` #159). 같은 절에 「메시지 주고받기 성공, 읽음 표시 정상」.
> 「판 뒤 변경」: `git log c35e0de0..b9f34ccd` — ChatPage·ChatWindowPage·ChatNewRoomDialog·ChatDocPickerDialog·ChatButton·TopHeader·NotificationBell·NotificationClient·Web ChatService·API ChatController·Application ChatService·ChatFileStore **변경 0** · Sidebar 만 변경(8/24 메뉴 내림 #253 이후 최근 `347d7f70`).

| # | 기능 | ① 사장 실측 (근거 주소) | ② 구현 (파일:줄) | ③ 연결 (파일:줄 · 다음 칸까지 흐르나) | 점수 | 출처 |
|---|---|---|---|---|---|---|
| 1 | 상단바 메신저 버튼 → 팝업창 열기 | 🟢 1.2.80 (8/15 인계서 §②) · 판 뒤 변경 0 | 🟢 `TopHeader.razor:26,48-58` → `hitpan-chat-window.js` `open`(창 이름 고정 400×680) → `ChatWindowPage.razor:1-2,25-27` → `<ChatPage>` | 🟢 같은 컴포넌트를 얹는다 (`ChatWindowPage.razor:25-27`) | 100 | 🟢 |
| 2 | 팝업 차단 시 대체 길 | ⬜ | 🟡 `TopHeader.razor:49-55` 안내문만 · `/chat` 으로 보내는 대체 길은 `Sidebar.razor:476-495` 에 있으나 **호출자 0**(메뉴는 8/24 내림 `Sidebar.razor:209-215`) | 🔴 안내문이 「메뉴 → 메신저 로도 열 수 있습니다」라고 하는데 그 메뉴가 기준판에 없다 ⇒ 팝업 막힌 PC 는 주소를 손으로 치는 길뿐 | 10 | 🟢 |
| 3 | 내 방 목록(이름·인원·마지막 글·안 읽은 수) | ⬜ | 🟢 `ChatPage.razor:64-103` → Web `ChatService.cs:20-31` → `ChatController.cs:48-55` → `ChatService.cs`(App) `:58-108` | 🟢 `left_at IS NULL` 내 방만 · 1:1 이름은 조회 때 붙임(`:68-78`) | 80 | 🟢 |
| 4 | 1:1 방 만들기·다시 열기 | ⬜ | 🟢 `ChatNewRoomDialog.razor:21-25,167-171` → `ChatController.cs:61-77` → `ChatService.cs:119-246` (`direct_key` UNIQUE `DB-101_chat.sql:68`) | 🔴 기존 1:1 을 다시 열면 **나만** 되살린다(`ChatService.cs:189-199`). 상대가 나갔던 방이면 상대는 목록(`:100`)·알림(`:833`) 둘 다에서 빠진 채 남는다 ⇒ 보낸 글이 상대에게 안 간다. 결재 쪽 같은 함수는 **둘 다** 되살린다(`:891-898`) — 비대칭 | 10 | 🟢 |
| 5 | 부서 대화방 만들기 | ⬜ | 🟢 `ChatNewRoomDialog.razor:33-55,135-136` → `ChatService.cs:131-146`(부서 중 계정 있는 사원만) | 🟡 부서 목록 `GET api/departments` 가 클래스 `[Authorize(Policy="TenantAdminOnly")]`(`DepartmentController.cs:18`) + 메서드 `TenantOnly`(`:34`) — ASP.NET Core 는 두 정책을 **둘 다** 요구한다 ⇒ 자식계정은 403 · 부서 칸이 빈다(추정 · 실측 0) | 50 | 🟢 |
| 6 | 단체 대화방 만들기 | ⬜ | 🟢 `ChatService.cs:147-167,218-220` · 대화상자 `ChatNewRoomDialog.razor:24` | 🟢 만든 뒤 멤버 INSERT 트랜잭션(`:205-243`) | 80 | 🟢 |
| 7 | 대화 상대 후보(계정 있는 재직 사원만) | ⬜ | 🟢 `ChatController.cs:168-175` → `ChatService.cs:525-554`(`users` 실제 조인 · `is_active`) | 🟢 `ChatNewRoomDialog.razor:132` 가 받는다 | 80 | 🟢 |
| 8 | 대화 불러오기(최근 50) | 🟢 1.2.80 · 판 뒤 변경 0 | 🟢 `ChatPage.razor:361-402` → Web `ChatService.cs:73-79` → `ChatController.cs:93-114` → `ChatService.cs:291-360` | 🟢 열면 `MarkReadAsync`(`ChatPage.razor:398`) | 100 | 🟢 |
| 9 | 이전 대화 더 보기 | ⬜ | 🟡 API 만 — `before` 인자 `ChatController.cs:95` · `ChatService.cs:330` · Web 클라이언트는 `limit` 만 보낸다(`ChatService.cs`(Web) `:73-79`) · 화면 버튼 0 | 🟡 51번째 이전 글에 닿는 화면 길 없음 | 30 | 🟢 |
| 10 | 메시지 보내기(2000자) | 🟢 1.2.80 · 판 뒤 변경 0 | 🟢 `ChatPage.razor:243-250,410-428` → `ChatController.cs:117-141` → `ChatService.cs:367-426` | 🟢 저장 뒤 `PushToRoomAsync`(`:422-423`) | 100 | 🟢 |
| 11 | 실시간 수신 | ⬜ (진실원 `docs/베타배포_진실원.md` §6 표 7행 「채팅 실시간 수신 0회」) | 🟢 `ChatService.cs:822-859` → `SignalRNotificationService.cs:29-42` → `NotificationHub.cs:56-76`(JWT 로 그룹 배정) → `NotificationClient.cs:57-100` → `ChatPage.razor:291,304-354` | 🔴 허브를 켜는 곳은 `NotificationBell.razor:82` 하나 · 그 종은 `TopHeader.razor:20`(본 레이아웃)에만 있다. 팝업창은 `ChatWindowPage.razor:3` `EmptyLayout`(Body 만) = **별도 창·별도 앱 실행** ⇒ 팝업창 안 `ChatPage` 는 구독만 하고 연결은 아무도 안 연다 | 10 | 🟢 |
| 12 | 읽음 표시(1:1 「읽음」 · 단체 「n 읽음」) | 🟢 1.2.80 「읽음 표시 정상」 · 판 뒤 변경 0 | 🟢 `ChatService.cs:313-318`(보낸 사람 뺌) · `:489-500` · `ChatPage.razor:212-219,552-553` | 🟢 `last_read_at` 단일 칸 · ⚠️ 보낸 사람 화면은 다시 열 때만 갱신(읽음 푸시 없음 `:489-500`) | 100 | 🟢 |
| 13 | 상단바 안 읽은 수 배지 | ⬜ | 🟢 `ChatButton.razor:21-28,75-110` → `ChatController.cs:272-280` | 🟢 본 창은 종이 허브를 켜므로 알림 때 갱신 · 창 열 때 갱신(`TopHeader.razor:62`) | 80 | 🟢 |
| 14 | 알림종 → 대화방 이동 | ⬜ | 🟢 알림 `Link=/chat/{roomId}`(`ChatService.cs:849`) → `NotificationBell.razor:97-118` | 🟡 본 창 안 `/chat/{방}`(MainLayout)으로 간다 — 사장님 1.2.75 지적 「히트판 화면에서 메신저 동작」과 같은 모양(`Sidebar.razor:204-206` 주석) · 팝업 `/chat-window/{방}` 길(`ChatWindowPage.razor:2`)은 쓰이지 않는다 | 50 | 🟢 |
| 15 | 파일 보내기(20MB · 실행파일 차단 · 3중 한도) | ⬜ | 🟢 `ChatPage.razor:234-241,448-467` → Web `ChatService.cs:175-189` → `ChatController.cs:196-227` → `ChatService.cs:656-745` → `ChatFileStore.cs:63-119`(확장자 `:33-39` · 시그니처 `:44-49`) | 🟢 DB 실패 시 파일 뒷정리(`ChatService.cs:732-736`) | 80 | 🟢 |
| 16 | 파일 내려받기 | ⬜ | 🟢 `ChatController.cs:232-254` → `ChatService.cs:751-775`(방 참여 검사 `:768`) → `ChatFileStore.cs:121-128,148-169` | 🔴 화면은 맨 링크 `Href="api/chat/files/{id}" Target="_blank"`(`ChatPage.razor:205`) · API 인증은 JWT 머리 또는 `access_token`/`token` 쿼리뿐(`AuthExtensions.cs:70-81`) · 쿠키 인증 없음 · 같은 원점 서빙(`Program.cs:701,761`) ⇒ 토큰 없이 도착 → 401 (다른 화면은 `downloadFileFromBytes` 로 받는다 `storage.js:209`) | 10 | 🟢 |
| 17 | 그룹웨어 문서 연결(6종 고르기 → 보내기) | ⬜ | 🟢 `ChatDocPickerDialog.razor:28-33,113` → `ChatController.cs:180-188` → `ChatService.cs:560-639`(전부 본인 것만) · 보낼 때 서버가 제목 재조회 `:384-396,928-933` | 🟢 제목만 저장 · 없는 문서 차단 | 80 | 🟢 |
| 18 | 문서 링크 누르기(받는 쪽) | ⬜ | 🟢 `ChatPage.razor:189-196,505-525` | 🟡 문서가 아니라 **목록 화면**으로 간다(`:514-520`) · 받는 사람에게는 자기 목록(예 급여 `/hr/payroll`)이 열려 보낸 사람 문서에 못 닿는다 | 50 | 🟢 |
| 19 | 결재 안내 메시지(상신 → 결재자 · 승인/반려 → 신청자) | ⬜ | 🟢 `ApprovalController.cs:97,262-330` → `ChatService.cs:440-487`(「결재」 딱지 `:478` · `ChatPage.razor:537-549`) | 🟡 링크가 늘 `/approval/pending`(`ChatPage.razor:514`) — 신청자가 받은 승인·반려 결과도 결재 **대기함**으로 간다 | 50 | 🟢 |
| 20 | 방 나가기(옛 대화 보존) | ⬜ | 🟢 `ChatPage.razor:130-136,430-445` → `ChatController.cs:80-88` → `ChatService.cs:249-261` | 🟢 `left_at` 만 찍는다 | 80 | 🟢 |
| 21 | 내 메시지 숨기기 | ⬜ | 🟡 API `ChatController.cs:155-163` · `ChatService.cs:506-518` · Web 클라이언트 `ChatService.cs:124-128` · **화면 버튼 0**(ChatPage 에 호출 없음) | 🟡 주석은 「상대 화면에는 그대로」(`ChatController.cs:154`)인데 SQL 은 메시지 줄의 `deleted_at` 을 찍고 조회가 모두에게 거른다(`ChatService.cs:329`) ⇒ 말과 동작이 다르다 | 30 | 🟢 |
| 22 | 파일 사용량 조회 | ⬜ | 🟡 API `ChatController.cs:257-259` · `ChatService.cs:778-796` · Web `ChatService.cs:228-232` · 화면 호출 0 | 🟡 보여 줄 화면이 없다 | 30 | 🟢 |
| 23 | 파일 한도 값(회사별) | ⬜ | 🟡 읽기·첫 줄 만들기만 `ChatService.cs:984-1012` · 표 `DB-101_chat.sql:144-152` · 바꾸는 API·화면 0 | 🟡 기본값 20/500/5120MB 로만 돈다 | 30 | 🟢 |
| 24 | 첨부 저장 위치·보존 | ⬜ | 🟢 기본 `AppContext.BaseDirectory/chat-files`(`ChatFileStore.cs:55-60`) · 테넌트·월 폴더(`:101-118`) · 경로 이탈 차단(`:159-166`) | 🟡 업데이트 되돌리기 교체 때 옮겨 담기 `Rollback/local-swap.ps1:277-280`(읽기만) · **백업 포함 근거 0건**(`git grep chat-files` = DDL·local-swap·ChatFileStore 뿐 · 8/13 검증보고서 §4 R1 미해소) | 50 | 🟢 |
| 25 | 테넌트 격리 · 방 참여 관문 | ⬜ | 🟢 `employee_id` 는 JWT 에서만(`ChatController.cs:296-301`) · `tenant_id` 는 JWT → `TenantMiddleware.cs:100,122` → `ICurrentTenant` · 관문 `ChatService.cs:267-281`(읽기·보내기·파일 `:296,372,662,768`) · 허브 그룹은 서버가 정함(`NotificationHub.cs:50-51,56-76`) | 🟢 모든 SQL 에 `tenant_id` · 부모계정 우회 없음(`ChatController.cs:24-26`) | 80 | 🟢 |
| 26 | 계정 없는 사원 안내 | ⬜ | 🟢 403 + 안내문 `ChatController.cs:303-312` · 배지는 0(`:276`) | 🟡 Web 클라이언트가 실패를 `null` 로 삼켜(`ChatService.cs`(Web) `:26-31`) 화면에는 「대화 목록을 불러오지 못했습니다」만 뜬다(`ChatPage.razor:52-57`) — 안내문이 화면까지 안 간다 | 50 | 🟢 |
| 27 | 결재 알림과 메시지 이중 발송 방지 | ⬜ | 미조사 (`ApprovalTriggerHelper.cs:255` 안 읽음) | 미조사 | 미조사 | 🔴 |

## §B [베타작업]

| 항목 | 종류 | 근거 | 규모 | 선후 |
|---|---|---|---|---|
| B-1 받은 파일 내려받기 401 | 끊긴 흐름 | §A-16 · `ChatPage.razor:205` · `AuthExtensions.cs:70-81` | S | 사장님 2계정 실측(B-7)과 함께 확인 · 01b 그룹웨어 첨부 화면에 같은 모양이 있는지 01b 대조 |
| B-2 팝업창 실시간 수신 배관 미기동 | 끊긴 흐름 | §A-11 · `NotificationBell.razor:82` · `ChatWindowPage.razor:3` | S | B-7 실측 전에 먼저 — 고치지 않고 재면 「0회」가 「실패 1회」로 바뀔 뿐 |
| B-3 1:1 다시 열 때 상대 미복귀 | 끊긴 흐름 | §A-4 · `ChatService.cs:189-199` 대 `:891-898` | S | 없음 |
| B-4 팝업 차단 안내가 없는 메뉴를 가리킴 | 반쪽 | §A-2 · `TopHeader.razor:49-55` · `Sidebar.razor:209-215,476` | S | 사장님 8/24 「아이콘으로 동작제어」 결재 범위 안에서 |
| B-5 자식계정 부서 목록 403 추정 | 기록 없음 | §A-5 · `DepartmentController.cs:18,34` | S | 01a(설정 · 부서) 영역과 경계 — 실측 먼저 · 결재선 화면도 같은 목록을 쓰는지 01b |
| B-6 알림종 링크가 ERP 화면 안으로 연다 | 반쪽 | §A-14 · `ChatService.cs:849` | S | 사장님 1.2.75 지적과 같은 모양 — 판단은 사장님 |
| B-7 채팅 실시간 실측 0회 | 기록 없음 | 진실원 §6 표 7행 · 8/13 검증보고서 §4 R2·R3 | M | B-1·B-2·B-3 뒤 · 2계정(테넌트 test1234·test1 메모리 기준) · 터널 경로 |
| B-8 계정 없는 사원 안내문 미도달 | 반쪽 | §A-26 | S | 없음 |
| B-9 첨부가 백업에 안 들어감 | 막는 결재 | §A-24 · 8/13 검증보고서 §4 R1 | — | 🔴 첨부 저장 위치·설정 칸은 사장님 **보류**(10/1) — 제안·착수 없음 · 등재만 |

## §C [제안]

> 전부 🔵 · 채택은 사장님 · 첨부 저장 위치·설정 칸은 보류 사안이라 제안하지 않는다.

**C-1 수정보완**
- 🔵 ERP 매니저 「3분 안에 쓰나」: 팝업창(400px)에서 방 목록과 대화가 같은 높이로 세로로 쌓이는 배치(`ChatPage.razor:39-40,108-110` `xs=12` · `calc(100vh - 130px)` 두 개) — 좁은 창에서는 「목록 ↔ 대화」 한 칸씩 바꿔 보이기. 사장님 8/15 「UI 쪽은 좀 더 손 봐야될듯」의 후보(추정 · 화면 미실측).
- 🔵 ERP 매니저: 팝업이 막혔을 때 안내만 하지 말고 그 자리에서 화면 안 `/chat` 으로 바로 열기(종전 `Sidebar.razor:476-495` 와 같은 동작).
- 🔵 김삼성 상무 「실제 인사팀이 이렇게 일하나」: 받은 결재 결과·문서 링크가 목록이 아닌 **그 문서**로 열리기(§A-18·19) — 「클릭하시면 결재와 연결됩니다」 문구와 동작을 맞춘다.
- 🔵 김삼성 상무: 단체방은 만든 뒤 사람을 더할 길이 없다 · 부서방은 부서 이동·입사가 반영되지 않는다(`ChatService.cs:131-146` 만들 때 1회) — 신규 입사자 초대 방법.
- 🔵 CS팀장 강지원 「문의가 줄어드나」: 계정 없는 사원이 보는 문구를 서버 안내문 그대로(§A-26) — 「왜 안 되나요」 문의 자리.
- 🔵 개발팀: 허브 연결 토큰을 연결 때 한 번 잡아 쓴다(`NotificationClient.cs:66-83`) — 재연결 때 새 토큰을 읽고, 끊긴 뒤 다시 켜는 길(`_hub` 가 남아 `:59-62` 에서 되돌아감).
- 🔵 개발팀: 숨기기 주석(`ChatController.cs:154`)과 동작(`ChatService.cs:511-517`) 중 사장님 뜻 확인 후 하나로.

**C-2 추가기능** (오더·진실원에 없는 요구 — 제안만)
- 🔵 기술영업 「이게 팔리냐」: 대화 검색(코드 0 · 진실원 「검색·방 관리 없음」).
- 🔵 ERP 매니저: 이전 대화 더 보기 버튼(API 는 이미 있다 §A-9).
- 🔵 기술영업: 매출·매입 전표(견적·수주 등) 연결 — 지금 연결 대상은 그룹웨어 6종뿐(`ChatDocPickerDialog.razor:28-33`).

## §D [GPT]

| §A 행 | 파일:줄 (`b9f34ccd`) | 우리가 못 정한 이유 | GPT 에게 묻고 싶은 것 |
|---|---|---|---|
| A-16 파일 내려받기 | `src/HitPan.Web/Pages/HR/ChatPage.razor:205` · `src/HitPan.API/Extensions/AuthExtensions.cs:70-81` | 코드로는 401 이 확실해 보이나 실측 0 · 다른 미들웨어가 토큰을 붙이는지 전수 못 봄 | 「Blazor WASM 에서 JWT 를 localStorage 에 둘 때 `<a href>` 새 탭 요청이 이 API 의 `[Authorize]` 를 통과할 길이 있는가」 |
| A-11 실시간 수신 | `src/HitPan.Web/Services/NotificationClient.cs:57-100` · `Layout/NotificationBell.razor:82` · `Pages/HR/ChatWindowPage.razor:3` | `window.open` 창이 별도 WASM 실행이라는 전제 위의 판정 · 실측 0 | 「`EmptyLayout` 팝업창에서 `NotificationClient.StartAsync` 를 부르는 다른 경로가 있는가 · 없다면 팝업 안 실시간은 0 인가」 |
| A-5 부서방 | `src/HitPan.API/Controllers/DepartmentController.cs:18,34` | 1.2.74 에 「조회는 직원에게 연다」로 봉합 기록이 있는데 프레임워크 규칙상 두 정책이 AND | 「클래스 `[Authorize(Policy=A)]` + 메서드 `[Authorize(Policy=B)]` 에서 자식계정(B 만 충족)은 통과하는가」 |
| A-11 보조 · 허브 토큰 | `src/HitPan.Web/Services/NotificationClient.cs:66-83` · `src/HitPan.Application/Services/AuthService.cs:23`(8시간) | 터널 끊김 뒤 재연결 실패 가능성은 타이밍 문제라 코드만으로 확정 불가 | 「연결 시 잡은 토큰을 `AccessTokenProvider` 가 계속 돌려줄 때 만료 뒤 자동 재연결은 어떻게 되며 `Closed` 뒤 누가 다시 켜는가」 |
| A-21 숨기기 | `src/HitPan.API/Controllers/ChatController.cs:154` · `src/HitPan.Application/Services/ChatService.cs:329,511-517` | 주석·검증보고서(V6 「삭제는 숨김만」)·SQL 이 서로 다른 뜻 · 화면 버튼도 없어 사장님 의도 확인 불가 | 「이 구현은 『내 화면에서만 숨김』인가 『모두에게서 감춤』인가」 |

## §E [한계]

- **못 본 것 · 안 연 파일**: `Layout/ChatPopup.razor`(396줄 · 기준판에서 렌더하는 곳 0 — `<ChatPopup` 태그는 `TopHeader.razor:34` 주석뿐 · 옛 화면 안 채팅창 잔재로 보이나 내부 미독) · `ApprovalTriggerHelper.cs`(§A-27 미조사) · `IChatService.cs` · `Models/ChatModels.cs` · `DTOs/Chat/ChatDtos.cs` · 시험 4개(`ChatGuardTests` · `ChatWindowGuardTests` · `ChatDocumentColumnGuardTests` · `LocalSwapChatFilesGateTests` — 글자 게이트인지 동작 게이트인지 미확인) · 백업 서비스 본문(백업 포함 여부는 `git grep` 결과로만).
- **분모 출발점 정정**: 투입 카드의 「Sidebar `chat` 메뉴 2줄」은 기준판에 **Href 0줄** — 8/24 사장님 지시 「메신저 그룹웨어 메뉴에서 삭제, 아이콘으로 동작제어」로 내려갔다(`Sidebar.razor:209-215` 주석) · 남은 `OpenChatWindowAsync`(`:476`)는 호출자 0. 메뉴 행은 세지 않고 §A-2 에 반영.
- **① 판정 한계**: 8/15 사장님 「잘됨」은 팝업·주고받기·읽음만 직접 가리킨다. 방 목록·1:1 만들기도 그 실측에 쓰였을 수 있으나 원문에 없어 ⬜. 1.2.80 당시에도 팝업창에 허브가 안 켜졌다면(§A-11) 그때의 「주고받기 성공」은 실시간이 아니라 방을 다시 눌러 본 결과일 수 있다 — 추정.
- **7영역 밖(등재만)**: `ChatbotController`·`ChatbotBar` = CS 챗봇 · 백오피스 몫(메모리 10/8) — 06 분모 밖. 모바일 채팅 화면 = 07 몫(진실원 「모바일 채팅 0화면」). `DepartmentController` 정책 문제(§A-5)는 01a 설정 영역과 겹친다.
- **못 끝낸 행**: §A-27 1행.
- **순서 가정**: 지시서 §4 순서 + §11 결재 1(나쁜 쪽이 먼저 이긴다) — ①🟢 행은 판 뒤 변경 0 이면 100, ①⬜ 행은 ③🔴 를 ②🟡 보다 먼저 적용(§A-2 는 ②🟡·③🔴 → 10). ③🔴 는 「코드로 끊김 확인 · 실측 0」을 포함한다(§A-4·11·16).
