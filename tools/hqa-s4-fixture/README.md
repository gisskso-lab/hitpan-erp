# HQA S4 시험 앱 — 백오피스 CS 상태 칸 고침 전 · 고친 꼴

> **판: A′ (11/13)** — 고친 꼴 = 기능 가지 `feat/20261107-백오피스S4닫기` 머리 `16e88c01092d33eb643c6cac454703f00efa02ac`. 고침 전은 그대로 `b54732eb`.
> ⬛낡음: 11/7 판 — 고친 꼴 = `e456e4f6a0b2dc36f14ef53f64106d455f363ecb` (시험 앱 가지 머리 `8e7f3208` 까지).

## 1. 목적

- **HQA 관문 ① 직접 실측용**이다. 외부 검증팀이 **자기 환경에서 직접 띄워** 백오피스 「고객A/S 목록」의 상태 칸(`CsStatusCell`)을 고침 전과 고친 꼴로 나란히 잰다.
- **운영이 아니다.** 데이터는 전부 가짜이고, 인증 없음 · 서버 저장 0 · DB 접속 0 · 바깥 망 호출 0 이다.
- 제품 코드(`src/`)는 이 묶음에서 한 줄도 바뀌지 않았다. 제품 파일을 **원 커밋에서 글자 그대로 꺼내** 이 앱 안에 두었을 뿐이다(§8 해시 표). A′ 판(11/13)에서 고친 꼴은 세 파일(상태 칸 · 상태 칸 CSS · 새 메뉴 파일 `CsStatusMenu.cs`)이다.
  - ⬛낡음(11/7 판): 「제품의 두 파일을 원 커밋에서 글자 그대로 꺼내」
- 측정 스크립트는 넣지 않았다 — HQA 가 자기 스크립트로 잰다.

> 🔴 **이 앱 통과 = 전체 백오피스 통합 실측 아님.** 이 앱은 상태 칸과 그 칸을 품은 목록 한 구획만 옮겼다. 사이드바 · 머리줄 · 인증 · 실제 서버 응답 · 실제 데이터 양과 모양이 들어간 통합 화면의 동작은 이 앱으로 증명되지 않는다.

## 2. 요구

| 항목 | 판 |
|---|---|
| .NET SDK | 8.x (대상 틀 `net8.0`) |
| Node.js | 18 이상 (지연 프록시 `proxy/delay-proxy.js` · Playwright 를 쓸 때) |
| Playwright 브라우저 | HQA 측정 스크립트가 쓰는 판 (`npx playwright install chromium` 등) |
| NuGet | 첫 빌드 때 `MudBlazor 9.3.0` 을 내려받는다 |

## 3. 기동

레포 루트에서:

```bash
# 1) 빌드 (경고도 오류로 — 0 경고 0 오류여야 정상)
dotnet build tools/hqa-s4-fixture/HqaS4Fixture.csproj -warnaserror

# 2) 띄우기 — 환경은 반드시 Development
ASPNETCORE_ENVIRONMENT=Development dotnet run --project tools/hqa-s4-fixture/HqaS4Fixture.csproj --urls http://127.0.0.1:5799
```

PowerShell 이면:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project tools/hqa-s4-fixture/HqaS4Fixture.csproj --urls http://127.0.0.1:5799
```

- `dotnet run` 은 `Properties/launchSettings.json` 을 읽어 환경을 `Development` 로 둔다. `--no-launch-profile` 을 주거나 빌드 산출물(`HqaS4Fixture.dll`)을 직접 띄울 때는 위처럼 환경 변수를 꼭 준다.
- 🔴 **환경이 Production 이면 MudBlazor 정적 파일(`_content/MudBlazor/MudBlazor.min.css` · `MudBlazor.min.js`)이 404 난다** — 목록 단추가 열리지 않고 모양이 깨진다(이 사고가 실제로 있었다). 앱은 Development 가 아니면 기동 기록에 경고 한 줄을 남긴다.
- 기동이 되면 기록에 `Now listening on: http://127.0.0.1:5799` 와 `Hosting environment: Development` 가 찍힌다.
- 끌 때는 그 창에서 Ctrl+C.

## 4. 지연 프록시 (선택)

브라우저 ↔ 앱 사이에 방향마다 지연을 넣어 느린 회선을 흉내 낸다(웹소켓 포함 TCP 바이트를 그대로 넘긴다 · Node 내장 모듈만 씀).

```bash
node tools/hqa-s4-fixture/proxy/delay-proxy.js <듣는포트> <방향당지연ms> [대상포트=5799] [대상호스트=127.0.0.1]

# 예: 방향당 150ms(왕복 약 300ms 추가) → 브라우저는 http://127.0.0.1:5800/after 로 연다
node tools/hqa-s4-fixture/proxy/delay-proxy.js 5800 150
```

127.0.0.1 에만 듣는다.

**고침(HQA D-S4-1 · 11/7)** — 첫 판은 데이터를 지연 뒤에 쓰면서 상대가 닫으면(`close`) 반대쪽을 바로 끊어(`destroy`) 지연 대기열에 남은 바이트를 잃었다. 지금 판은 방향마다 대기열 하나(FIFO)에 데이터 · EOF · 오류 닫기를 같은 지연으로 넣는다 — 양쪽 `allowHalfOpen` · EOF 는 앞선 데이터가 다 나간 뒤 반대쪽 `end()` · `close` 에서 즉시 끊지 않음 · 오류는 대기열 뒤에서 `end()` 후 1초 안에 안 닫히면 끊음 · 대상 연결 실패면 지연 뒤 클라이언트를 닫음. 옛 줄은 파일 안에 주석으로 남겼다. 쓰는 법 · 듣는 주소는 그대로다.

대조 시험(옛 판 · 새 판 × 방향당 지연 0 · 150 · 300ms):

| 경우 | 옛 판 | 새 판 |
|---|---|---|
| (a) 서버가 17바이트 보내고 바로 끝냄 | 0/3 — 세 지연 모두 0바이트 | 3/3 — 17바이트 |
| (b) 서버가 1MB 보내고 끝냄 | 0/3 — 0바이트 | 3/3 — 1,048,576바이트 · SHA-256 일치 |
| (c) 클라이언트가 요청 뒤 반쪽 닫음 → 서버가 EOF 받고 응답 | 0/3 — 서버가 요청을 못 받고 응답 0 | 3/3 — 요청 1,000바이트 · 응답 262,209바이트 전부 |
| (d) 대상 포트 닫힘 | 3/3 — 즉시 닫힘 | 3/3 — 지연 + 30ms 안에 닫힘(한도 지연 + 1초) |
| (e) 한 연결에서 왕복 100회 | 3/3 — 순서 보존 | 3/3 — 순서 보존 |

## 5. 화면

| 주소 | 내용 |
|---|---|
| `/` | 두 화면으로 가는 단추 둘 |
| `/before` | **고침 전** 상태 칸 — `b54732eb` 의 `CsStatusCell.razor` + `backoffice-dashboard.css` (운영 판과 같은 글자) |
| `/after` | **고친 꼴(A′)** 상태 칸 — `16e88c01` 의 같은 두 파일 + 그 칸이 쓰는 메뉴 `CsStatusMenu.cs` |
| ~~`/after`~~ ⬛낡음(11/7 판) | 고친 꼴 상태 칸 — `e456e4f6` 의 같은 두 파일 |

두 화면은 같은 목록 · 같은 가짜 데이터 · 같은 공통 CSS 를 쓰고, **상태 칸 컴포넌트(고친 꼴은 그 칸이 쓰는 `CsStatusMenu` 포함)와 `backoffice-dashboard.css` 만 다르다.** (A′ 판의 `backoffice-dashboard.css` 는 `e456e4f6` 과 같은 블롭이다 — 두 화면 CSS 차이는 11/7 판과 같다.) CSS 는 `App.razor` 가 주소로 고르므로 판을 바꿀 때는 **주소를 새로 연다**(화면 안 「고친 꼴로 / 고침 전으로」 단추도 강제 새로고침으로 옮기고 쿼리를 그대로 넘긴다).

화면 꼴(백오피스 `AdminCsListPage.razor` 목록 구획을 옮김):

- 왼쪽 `MudItem md=7` — `MudDataGrid`(Hover · Dense · SortMode.Single · 쪽 나눔) · 칸 = 고객사 · 분류 · 접수 경로 · 응대유형 · **상태** · 접수 · 답
- 오른쪽 `MudItem md=5` — 상세 자리. **행을 누르면 `#detail` 이 「상세 열림」으로 바뀐다**(`data-open="1"` · `data-row="<번호>"`). 상태 칸을 눌렀는데 이 값이 바뀌면 행 클릭으로 샌 것이다.
- 상태를 고르면 그 줄 값이 화면 안에서만 바뀌고 기록이 쌓인다(서버 · DB 0).

재기 편하게 둔 자리:

| 선택자 | 뜻 |
|---|---|
| `#fixture-root[data-variant]` | `before` / `after` |
| `link[data-fixture-css]` | 실린 상태 칸 CSS 판 (`before` / `after`) |
| `[data-row="<번호>"]` | 상태 칸을 감싼 div · `data-status-kind` = `normal` · `legacy` · `blank` · `empty` · `null` |
| `#detail` | 상세 열림 표시 (`data-open` 0/1 · `data-row`) |
| `#rowclick-count` · `#rowclicks` | 행 클릭 횟수 · 줄 번호 기록(쉼표) |
| `#pick-count` · `#picks` | 상태 고름 횟수 · `줄번호:값` 기록(쉼표) |
| `#before` | 목록 앞 단추(키보드 탭 출발점) |
| `#blank` | 목록 아래 빈 자리(바깥 누름용) |

## 6. 쿼리

| 쿼리 | 기본 | 범위 | 뜻 |
|---|---|---|---|
| `rows` | 24 | 1~2000 | 가짜 줄 수 |
| `pager` | 1 | 0/1 | 쪽 나눔. 1 = 백오피스처럼 `MudDataGridPager` 켬 · 0 = 끔(한 쪽에 모든 줄) |
| `rpp` | 10 | 1~2000 | 쪽당 줄 수 (MudDataGrid 9.3 기본 10) · `pager=0` 이면 무시 |
| `special` | 1 | 0/1 | 특수 줄(아래 표) 넣기 |
| `md` | 7 | 1~12 | 목록 칸 너비(MudItem md) · 상세 칸 = 12 - md |
| `padl` | 16 | 0~2000 | 바깥 왼쪽 여백(px) — 목록을 오른쪽으로 밀어 목록 펼침 방향 바뀜을 잴 때 |

예: `/after?rows=200&pager=0` · `/before?rows=5&special=0` · `/after?md=12&padl=900`

## 7. 가짜 데이터

- 난수 없음 — 같은 `rows` · `special` 이면 늘 같은 줄(시각만 오늘 기준). 고객사 이름은 `가짜고객사001` 꼴.
- 3줄에 1줄(번호 % 3 == 2)은 **손으로 적은 건**(접수 경로 「전화」 · 상태 목록 2종 = 접수 · 완료), 나머지는 **고객사 쪽지**(`erp_message` · 상태 목록 5종 = 접수 · 처리중 · 보류 · 고객답대기 · 완료).
- 쪽지 건 상태는 5종을 돌아가며, 전화 건은 접수 · 완료를 번갈아 둔다.
- 4줄마다 「안 읽음」(ReadAt 없음) · 6줄마다 별점 「좋아요」 · 10줄마다(6의 배수 제외) 「아쉬워요」.
- 목록을 열면 지금 값 줄은 「… (지금)」으로 보이고 고를 수 없다.

`special=1`(기본)일 때 특수 줄:

| 줄 | 접수 경로 | 상태 값 | `data-status-kind` | 보는 것 |
|---|---|---|---|---|
| 2 | 전화 | `처리중` | `legacy` | 목록 밖 옛 값 → 목록 맨 위 「처리중 (예전 기록 — 그대로 둡니다)」(누를 수 없음) |
| 3 | 쪽지 | `"   "`(공백 셋) | `blank` | 공백뿐인 상태 값 |
| 4 | 쪽지 | `""`(빈 값) | `empty` | 빈 상태 값 |
| 6 | 쪽지 | `null` | `null` | 값 없음 |
| 8 | 전화 | `"  "`(공백 둘) | `blank` | 공백뿐 + 전화 목록 밖 값 → 「예전 기록」 줄도 붙는다 |

## 8. 원본 파일 해시

**A′ 판(11/13)**: 고친 꼴 행의 원 커밋이 `16e88c01` 로 바뀌었다. 11/7 판 값은 표 아래쪽 ⬛낡음 행으로 남겼다.

이 앱 안 원본 파일은 아래 커밋의 같은 경로에서 `git show <SHA>:<경로>` 로 꺼낸 바이트 그대로다(`.gitattributes` 의 `* -text` 로 줄끝 바꿈도 막았다). `git hash-object <파일>` 로 대조한다.

| 이 앱 안 경로 | 원 경로 | 원 커밋 | git hash-object |
|---|---|---|---|
| `Components/Before/CsStatusCell.razor` | `src/HitPan.Backoffice/Components/Pages/Platform/Components/CsStatusCell.razor` | `b54732ebc767c9d1fafe57c7e8b9630a2d7d4265` (고침 전 · 운영 판과 같은 글자) | `1eef106a401e900283a4af7997f68585cdf9271d` |
| `wwwroot/css/before/backoffice-dashboard.css` | `src/HitPan.Backoffice/wwwroot/css/backoffice-dashboard.css` | `b54732ebc767c9d1fafe57c7e8b9630a2d7d4265` | `3f938234e1f776d1e917cd9f286ea72823da4034` |
| `Components/After/CsStatusCell.razor` | `src/HitPan.Backoffice/Components/Pages/Platform/Components/CsStatusCell.razor` | `16e88c01092d33eb643c6cac454703f00efa02ac` (고친 꼴 A′ · 기능 가지 머리) | `9c358a00d2320e2308ff56f2cec1030a0f582261` |
| `wwwroot/css/after/backoffice-dashboard.css` | `src/HitPan.Backoffice/wwwroot/css/backoffice-dashboard.css` | `16e88c01092d33eb643c6cac454703f00efa02ac` (`e456e4f6` 와 같은 블롭 — 11/7 판과 무변) | `0e922200fc29f057aff2d9b4b436c815c177bdc3` |
| `Components/After/CsStatusMenu.cs` (A′ 새 파일) | `src/HitPan.Backoffice/Components/Pages/Platform/Components/CsStatusMenu.cs` | `16e88c01092d33eb643c6cac454703f00efa02ac` | `1aa0fc6aebc21c6c9e72a40bb4f77c18acad507a` |
| `Vocab/CsVocab.cs` | `src/HitPan.Backoffice/Components/Pages/Platform/Components/CsVocab.cs` | `16e88c01…` (`b54732eb` · `e456e4f6` 와 같은 블롭 — 세 판 사이 무변) | `0ec65e70ef69b451458018cbdb749a98402c0cab` |
| ⬛낡음(11/7 판) `Components/After/CsStatusCell.razor` | 위와 같음 | `e456e4f6a0b2dc36f14ef53f64106d455f363ecb` (고친 꼴 · 그때 가지 코드 머리) | `644e27ebfb594c90cf4e9b755a7b9a0d2bb9f138` |
| ⬛낡음(11/7 판) `wwwroot/css/after/backoffice-dashboard.css` | 위와 같음 | `e456e4f6a0b2dc36f14ef53f64106d455f363ecb` | `0e922200fc29f057aff2d9b4b436c815c177bdc3` |
| ⬛낡음(11/7 판) `Vocab/CsVocab.cs` | 위와 같음 | `e456e4f6…` (`b54732eb` 와 같은 블롭 — 두 판 사이 무변) | `0ec65e70ef69b451458018cbdb749a98402c0cab` |
| `wwwroot/css/common/backoffice-v2.css` | `src/HitPan.Backoffice/wwwroot/css/backoffice-v2.css` | `e456e4f6…` (`b54732eb` 와 같은 블롭) | `9b7635789c9d8a8bf8e170d98b7f8d50f434005d` |
| `wwwroot/css/common/app.css` | `src/HitPan.Backoffice/wwwroot/app.css` | `e456e4f6…` | `73a69d6f6829d98de0cfe14f8f9179e7bc318232` |
| `wwwroot/css/common/bootstrap.min.css` | `src/HitPan.Backoffice/wwwroot/lib/bootstrap/dist/css/bootstrap.min.css` | `e456e4f6…` | `39934146ffdc3af1e17b83657576b2d21f3cbecd` |

- 두 `backoffice-dashboard.css` 의 머리 주석(2~3줄)에는 레포 금지어 목록에 든 낱말이 있다. 제품 원본의 글자이고(main 의 같은 파일에도 같은 줄), 해시 대조를 지키려고 바꾸지 않았다 — 동작과 무관한 주석이다. 제품 쪽 주석 정리는 S4 머지 뒤 별건으로 다룬다(지금 고치면 고친 꼴 해시가 바뀐다).

대조 예:

```bash
git hash-object tools/hqa-s4-fixture/Components/After/CsStatusCell.razor
git rev-parse 16e88c01092d33eb643c6cac454703f00efa02ac:src/HitPan.Backoffice/Components/Pages/Platform/Components/CsStatusCell.razor
git hash-object tools/hqa-s4-fixture/Components/After/CsStatusMenu.cs
git rev-parse 16e88c01092d33eb643c6cac454703f00efa02ac:src/HitPan.Backoffice/Components/Pages/Platform/Components/CsStatusMenu.cs
```

⬛낡음(11/7 판) 대조 예: `git rev-parse e456e4f6a0b2dc36f14ef53f64106d455f363ecb:src/HitPan.Backoffice/Components/Pages/Platform/Components/CsStatusCell.razor`

### 컴파일되게 한 감싸기 (원본 파일 밖에서만)

| 감싸기 | 왜 |
|---|---|
| 고침 전 · 고친 꼴을 `Components/Before` · `Components/After` 두 폴더에 나눠 둠 | 같은 이름 `CsStatusCell` 두 개가 겹치지 않게 — 이름공간이 `HqaS4Fixture.Components.Before` / `.After` 가 된다(백오피스에서는 `HitPan.Backoffice.Components.Pages.Platform.Components`). 각 폴더의 `_Imports.razor` 는 설명 주석뿐이다. |
| A′ 새 파일 `CsStatusMenu.cs` 는 `Components/After/` 에 두되 **namespace 까지 글자 그대로**(`HitPan.Backoffice.Components.Pages.Platform.Components`) 둠 | `.cs` 파일은 폴더 이름공간을 받지 않고 파일 안 namespace 를 쓴다. 상위 `Components/_Imports.razor` 가 그 이름공간을 이미 `@using` 으로 끌어오므로 고친 꼴 `CsStatusCell.razor` 의 `<CsStatusMenu>` 가 그대로 찾는다. 고침 전 칸은 이 메뉴를 쓰지 않는다. `Components/After/_Imports.razor` 는 설명 주석뿐이다(A′ 판으로 갱신 · 옛 첫 줄은 ⬛낡음으로 남김). |
| `CsVocab.cs` 는 **대역이 아니라 레포 실제 파일**을 원래 이름공간(`HitPan.Backoffice.Components.Pages.Platform.Components`) 그대로 둠 | 상위 `Components/_Imports.razor` 가 그 이름공간을 `@using` 으로 끌어온다(백오피스 `_Imports.razor` 와 같은 줄). 백오피스 `_Imports.razor` 의 별칭 `Color` · `Size` · `Align` 도 같게 둠. |
| 화면이 `Status="@(context.Item.Status!)"` 로 넘김 | 가짜 줄에 `null` 상태를 넣기 위해 줄 모델의 `Status` 를 `string?` 로 두었다. 상태 칸 매개변수는 `string` 이라 null 허용 경고를 끄는 `!` 만 붙였다(런타임 값은 그대로 null 이 넘어간다). |

## 9. 운영과 다른 점

| 항목 | 운영 백오피스 | 이 앱 |
|---|---|---|
| 데이터 | 실제 서버 응답(최대 300건 끊어 받음) | 가짜 · 쿼리로 줄 수 조절 · 서버 호출 0 |
| 상태 고르기 | 페이지가 서버에 저장 후 목록 갱신 | 화면 안 값만 바꿈 · 기록만 쌓음(서버 · DB 0) |
| 인증 | 로그인 · 권한 | 없음 |
| 레이아웃 | `MainLayout`(사이드바 · 머리줄 · 상태 막대) | 없음 — 목록 구획만 |
| 목록 칸 | 분류 칸 아래 모양 꼬리표 줄 · 빈 목록 안내 카드 · 불러오는 중 표시 | 분류 낱말만 · 빈 목록 · 불러오는 중 없음 |
| 상세 칸 | 상세 패널(답 · 진행 기록 등) | 「상세 열림」 글 한 줄 + 기록 |
| `<head>` | Google Material Icons 글꼴 · 범위 CSS 묶음(`HitPan.Backoffice.styles.css`) · 파비콘 | 셋 다 안 실음(바깥 망 의존 0 · 상태 칸에는 범위 CSS 없음) |
| 공급자 | `MudThemeProvider` · `MudPopoverProvider` · `MudDialogProvider` · `MudSnackbarProvider` | 같음 |
| 렌더 | Razor 컴포넌트 · `Routes @rendermode="InteractiveServer"` · **미리 그리기 켬**(기본값) | 같음 |
| 꾸러미 | net8.0 · MudBlazor 9.3.0 | 같음 |

**미리 그리기(prerender)는 운영과 같이 켜져 있다.** 첫 응답 HTML 은 서버가 정적으로 그린 것이고, 회로(웹소켓)가 붙은 뒤에야 단추가 움직인다. 붙기 전 누름은 반응하지 않는다 — 측정할 때 회로 연결을 기다리는지 HQA 스크립트에서 정한다.

## 10. 파일

```
tools/hqa-s4-fixture/
  README.md                      이 문서
  HqaS4Fixture.csproj            net8.0 · MudBlazor 9.3.0 (레포 솔루션에 안 넣음 · CI 대상 아님)
  Program.cs                     InteractiveServer · Development 아니면 경고
  Properties/launchSettings.json dotnet run 때 환경 Development
  .gitattributes                 * -text (원본 바이트 그대로)
  Components/App.razor           CSS 순서 = 백오피스 App.razor · 상태 칸 CSS 를 주소로 고름
  Components/Routes.razor        공급자 넷 + 라우터(레이아웃 없음)
  Components/_Imports.razor
  Components/Pages/Home.razor    /
  Components/Pages/FixturePage.razor  /before · /after
  Components/Pages/FixtureData.cs     가짜 데이터
  Components/Before/             고침 전 원본 CsStatusCell.razor + 설명용 _Imports.razor
  Components/After/              고친 꼴(A′) 원본 CsStatusCell.razor · CsStatusMenu.cs + 설명용 _Imports.razor
  Vocab/CsVocab.cs               레포 실제 파일(원본)
  wwwroot/css/common/            공통 CSS 원본 셋
  wwwroot/css/before|after/      상태 칸 CSS 원본 두 판
  proxy/delay-proxy.js           지연 프록시
```
