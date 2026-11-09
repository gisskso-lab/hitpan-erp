# [3-V] 병렬검증 — 백오피스 S4 F14 `CsStatusMenu`(9.3 MudMenu 상속) · 칸 감쌈 · 보안상무 안철수

## 카드
| 항목 | 내용 |
|---|---|
| 작성 | 보안상무 안철수 관점 · 2026-11-10 · 루틴 [3-V] · [3] 이 도는 중 |
| 대상 | 가지 `feat/20261107-백오피스S4닫기` · 머리 `1fab5e47` 위 미커밋 사본(02:01)으로 읽기 시작 → 검증 도중 [3] 커밋 **`1f451132`** · 커밋 blob 칸 `9c358a00` · .cs `9c47cc32` = 읽은 사본과 기계 대조 같음(재확인) — `CsStatusMenu.cs`(새 · 28줄) · `CsStatusCell.razor`(+32 −4) |
| 근거 | 작업지시서 [20261109작 F14](../../운영기록/20261109작_백오피스CS상태칸_F14_MudMenu상속_고침_작업지시서.md) §3 · §4 · §5 · §7 · §8 · §11 · [1-V F14](../선행/20261109_1V_F14_MudMenu초점_회로종료_근본원인.md) · 개발명세서 [20261109작 F14](../../개발/20261109작_백오피스CS상태칸_F14_MudMenu상속_고침_개발명세서.md)(빌드 · 범위 줄만) · MudBlazor 9.3 원문 사본 `e858549b…/scratchpad/mudsrc93/`(MudMenu.razor · .razor.cs · MudMenuItem.razor.cs) · 형식 계층 = NuGet 캐시 dll **메타데이터 표 직접 읽기**(아래 ②) |
| 안 한 것 | 코드 수정 0 · git 쓰기 0(이 문서 · INDEX 한 줄만) · 빌드 · `dotnet test` 0 · 서버 · 브라우저 0 · 측정 0(같은 PC 시각 민감 측정과 겹침) · 운영 · DB 접촉 0(#39) · 디컴파일 도구 설치 0 |
| **판정** | 🟡 **조건부** — 두 파일은 지시서 §4 · §5 코드 블록과 **기계 대조로 같다**. 거르는 범위(R6 · R8)는 형식 계층 원문으로 맞다. 다만 원문을 따라가면 **R1(↓)은 회로가 끊길 것으로 예측된다**(아래 ③). S-2d 가 빨강일 가능성이 높으니 §10 B(재설계 · 대표 재상신)를 미리 준비해 두라. 그 밖에 측정 칸 5개를 [4] 에 넘긴다(④). |

## ① 구현 ↔ 지시서 글자 대조
| 자리 | 방법 | 결과 |
|---|---|---|
| `CsStatusMenu.cs` | §4 ```csharp 블록 추출 ↔ 파일(CR 제거) `diff` | **같음**(차이 0) · 줄 끝 CRLF 통일 |
| 칸 5-9 메서드 | §5 ```csharp 블록 ↔ 칸 「F14 — 위 세 곳」~메서드 끝 `diff` | **같음**(차이 0) |
| 칸 5-1~5-8 | `git diff -- src` 읽기 | 태그 이름 2곳 · `@inject` 1 · 세 호출 자리 · 옛 줄 주석 모두 지시대로. 칸 줄 끝 CRLF 217/217(섞임 0) |
| §8 범위 `-` 줄 수 | `git diff -- src \| grep -c '^-[^-]'` | **4**(기대 5). 원인: 5-1 이 옛 L43 을 주석 **안에 그대로 둔** 꼴이라 git 은 그 줄을 지운 줄이 아니라 문맥 줄로 본다. 이것은 결함이 아니라 **게이트 기대값 오기**이고 개발명세서 L59 에도 🟡 로 적혀 있다 → [4] 는 「4 + 옛 L43 이 주석 안에 글자 그대로」 로 판정한다(V-3) |
| R9 범위 샘 | `grep -rn CsStatusMenu src` · `git status src` | 두 파일만(칸 3 · .cs 2) · DataGrid · ERP · App.razor · JS · CSS 0 → 🟢 |
| #23 금지어 | 두 파일 diff · .cs 전문에서 대소문자 무시 grep | 0 → 🟢 |

## ② 형식 계층 — 원문(메타데이터) 대조
- 방법: dll 의 ECMA-335 메타데이터 TypeDef · TypeRef · MethodDef 표를 Python 으로 직접 읽음(스크립트는 내 scratchpad · 레포 밖). **대조군**: 같은 스크립트로 `System.Private.CoreLib`(8.0.33)를 읽어 알려진 사실 `TaskCanceledException → OperationCanceledException` · `ObjectDisposedException → InvalidOperationException` 이 그대로 나오는 것을 확인했다 → 읽기 장치 유효.

| 형식 | 기반 | 출처 |
|---|---|---|
| `Microsoft.JSInterop.JSException` | `System.Exception` | `microsoft.aspnetcore.app.ref/8.0.27` ref · `microsoft.jsinterop/8.0.13` lib 둘 다 |
| `Microsoft.JSInterop.JSDisconnectedException`(sealed) | `System.Exception` — **JSException 하위 아님** | 같음 |
| `TaskCanceledException` | `OperationCanceledException` → `SystemException` | CoreLib 8.0.33 |
| `ObjectDisposedException` | `InvalidOperationException` → `SystemException` | 〃 |
| `MudBlazor.MudMenu`(9.3 net8.0) | `MudComponentBase` · unsealed · `OnAfterRenderAsync` protected virtual(final 아님) | `mudblazor/9.3.0/lib/net8.0` |
| `MudBlazor.MudComponentBase` | `ComponentBaseWithState` · **`get_Logger` protected 있음** · `OnAfterRender`(동기)만 덮음 · `OnAfterRenderAsync` 덮음 **없음** | 〃 |
| `MudBlazor.ComponentBaseWithState` | `ComponentBase` · `OnAfterRenderAsync` 덮음 없음 | 〃 |
| `ComponentBase` | `IHandleEvent.HandleEventAsync` · `IHandleAfterRender.OnAfterRenderAsync` = 명시 구현(private final) | aspnetcore.app.ref 8.0.27 |

→ 지시서 §11 미확인 두 개가 닫힌다. ⓐ 기반 클래스에 `Logger` 가 **있다** → 새 속성 이름을 `Logger` 로 했으면 CS0108 이 났다. `FocusSkipLog` 로 피한 것은 필요한 선택이었다. ⓑ `base.OnAfterRenderAsync` 사슬에서 JS 를 부르는 곳은 MudMenu L1020 · L1026 뿐이다(위 두 단은 `ComponentBase` 의 빈 Task). 따라서 `catch (JSException)` 이 삼키는 범위는 그 두 초점 호출로 **한정된다**.

## ③ 반례별 결론
| # | 결론 | 근거 · 남는 것 |
|---|---|---|
| **R1** ↓ | 🔴 **회로 종료 예측**(원문 추론 · 미측정) | L1026 실패 갈래는 L1025 `_focusedIndex = 0` 대입 **뒤**에 던지고, 그 직전 L1020 은 성공했으므로 초점은 **래퍼**에 있다. `_menuItems` = [버려진 k][살아 있는 n] 순서다(Clear 는 `CloseMenuAsync` L510 하나, 등록은 L1035 `Add` 로 뒤에 붙음). 이 상태에서 ↓ 를 누르면 래퍼 `@onkeydown="HandleKeyDownAsync"`(MudMenu.razor L112 · **Blazor 이벤트 길**) → L843 → L872 `_focusedIndex = 1` → L1046 → L1062 로 간다. k ≥ 2 이면(지시서 6-3: k = 2 또는 5) [1] 도 버려진 항목이라 **같은 JSException 이 이벤트 손잡이 안에서** 나고, 경계가 없어 회로가 끝난다. 키 가로채기(L1076~1081)는 ArrowDown/Up 에 `preventDown` 만 준다. `subscribeDown` 기본값은 메타데이터로 미확인이나, 구독되더라도 JS→.NET 호출 길이라 회로를 죽이지 않고, 래퍼 이벤트 길이 그대로 남는다. **결론: S-2d 가 빨강일 가능성이 높다 → §10 B 를 미리 준비.** |
| R2 Enter | 🟡 기록 · 서버가 막음 | 래퍼 Enter → L935 → `_menuItems[0]`(버려진 항목 · 처분된 컴포넌트) → `OnClickHandlerAsync`. 이 항목의 `Disabled` 와 람다 `() => OnPick.InvokeAsync(s)` 의 `s` 는 **옛 그림 값**이다(`OnPick` 은 `this` 로 읽혀 현재 행 콜백). 결과는 둘 중 하나다. 옛 상태가 [0] 과 같았으면 `GetDisabled` 가 참이라 아무것도 안 골라지고(G4 🟡), 아니면 옛 [0] 값이 올라간다. 서버 `CsAdminController` L338~ 가 `AllowedStatus` · 수기건 `AllowedManualStatus` · `from == to` 를 직접 검사하므로 **허용 밖 값은 저장되지 않는다** → 무결성 🟢 · 사용자 의도 어긋남만 남는다(S-2e) |
| R3 ↑ · 버리는 호출 | 🟡 조용한 실패 · 회로는 삶 | ⓐ 초점이 **단추**에 있을 때(L1020 실패 갈래 · `_focusedIndex = -1` 유지): 바깥 div `@onkeydown="TrackKeyboardInteraction"`(MudMenu.razor L13 · 동기 void)이 ↓/↑ 를 받아 `_ = InvokeAsync(() => FocusItemAsync(..))`(L1138 · L1143)를 부른다. [0] 이 버려진 항목이면 예외는 **버려진 Task 안에 갇힌다** → 회로 종료 0 · 기록 0(우리 catch 밖) · 초점은 단추 그대로. 그런데 `_focusedIndex` 는 이미 0/Count−1 이 되어 **다음 ↓ 는 아무 일도 안 한다**. 키보드 사용자는 목록에 못 들어가고 Tab · Esc 만 듣는다(R10 과 한 몸). ⓑ 초점이 **래퍼**에 있을 때 ↑: L872 에서 0 → Count−1(살아 있는 마지막)로 가 처음엔 안전하다. 그러나 ↑ 를 n 번 더 누르면 버려진 구역(k−1)에 닿아 R1 과 같은 회로 종료가 예측된다 → S-2d 에 ↑ 갈래 필요(V-1) |
| R4 진짜 초점 실패 삼킴 | 🟢 범위 한정 | ② ⓑ — 삼키는 곳은 L1020 · L1026 두 초점 호출뿐이다. 그 둘이 실패한 결과는 「초점이 안 옮겨짐」 한 가지라 다른 이유의 실패를 삼켜도 상태가 상하지 않는다. 대조: **정상 칸에서 상속 문구 0** 을 [4] 게이트로 고정(V-2) |
| R5 기록 넘침 | 🟢 기록(운영 접촉 0) | 백오피스 기록 설정 = 기본 콘솔 공급자(`Default: Information` → Warning 통과) · 코드상 파일 공급자 0 → 보관은 서버 측 수집기(운영 · 미확인)에 달림. 1건 = 문구 + JS · .NET 스택. 빈도 상한 = 목록이 열린 채 일어나는 그림 수(사용자 이벤트 비율 이하 · 그림이 그림을 부르는 고리 없음). 고침 전에는 같은 사건이 `Unhandled exception in circuit`(오류 수준 · 스택)으로 남았다 → 건당 기록량 같거나 적음. 문구에 고객 업무 데이터 0 · JS 오류 글은 클라이언트가 보낸 문자열이지만 이 노출면은 고침 전에도 같았다(#18 · #22 영향 0) |
| **R6** | 🟢 **확정**(메타데이터) | `JSDisconnectedException : Exception`(JSException 하위 아님) · `TaskCanceledException : OperationCanceledException`(JSException 무관) → `catch (JSException)` 은 끊긴 회로 · 시한 예외를 **잡지 않는다**. 지시서 §4 문장과 일치 |
| **R7** 태그 바꿈 | 🟢 정적 · 측정 1칸 권고 | 매개변수 · `CaptureUnmatchedValues`(`title`)는 상속 속성으로 바인딩되고, `@ref` 는 `MudMenu?` 필드로 위로 변환해 담긴다. 같은 이름공간이 `Components/_Imports.razor` L15 에 있어 태그가 풀린다(안 풀리면 RZ10012 경고로 0/0 이 깨짐 → 개발명세서는 두 구성 0/0 이라 적음 · 이 창에서 재빌드 안 함). 행동은 DOM 대조로 닫는다(V-4) |
| **R8** `when` 거름 | 🟢 **확정** | `ObjectDisposedException` → `InvalidOperationException` → `SystemException`: JSDisconnected · OCE 어느 쪽 하위도 아님 → 안 잡힘. `InvalidOperationException` · `JSException` · `ArgumentException` 도 안 잡힘. 단 주석 두 문장은 원문 미확인 · 과장 → V-5 |
| R9 범위 | 🟢 | ① 표 |
| R10 L1020 뒤 단추 | 🟡 (R3 ⓐ 로 구체화) | 「↓ 가 단추엔 손잡이 없음」 은 원문과 다르다. 바깥 div 의 `TrackKeyboardInteraction` 이 받는다. 다만 첫 ↓ 는 [0] 이 살아 있으면 들어가고, 버려졌으면 **조용히 실패한 뒤 이후 ↓ 가 먹통**이 된다 → V-1 |

### 별건 후보(범위 밖 · 기록만 · 이 고침과 무관)
- `src/HitPan.Backoffice/Program.cs` L21 `o.DetailedErrors = true`(2026-06-16 진단용) — 회로가 끝날 때 상세 예외 글을 브라우저로 보낸다. F14 는 회로 종료 횟수를 줄여 노출을 **줄이는** 쪽이다. 운영 설정 재검토는 별건 결재 대상.

## ④ [4] 에 넘길 측정 칸 제안(측정 전 고정 요청)
| # | 칸 | 무엇 · 기대 |
|---|---|---|
| **V-1** | S-2d 확장 | ⓐ 다시 연 뒤 초점 자리(래퍼 / 단추)와 **k(버려진 항목 수 2 · 5)** 를 표본마다 적고 k 별로 따로 센다. ⓑ ↓ 갈래와 별도로 **↑ 를 (n+k) 번** 누르는 갈래를 둔다. ⓒ 초점이 단추인 표본은 ↓ 2번 → 두 번째 ↓ 뒤 활성 요소를 기록한다(R3 ⓐ 먹통 확인). 기대(원문 추론): 래퍼 + k≥2 + ↓ → 회로 종료. 하나라도 나오면 G3 🔴 → §10 B |
| **V-2** | 정상 칸 상속 문구 0 | F12 k 48 · F13 정상 25 · reg 에서 「사라진 요소로 가는 초점 한 번 건너뜀」 = **0**(G5 의 「S-2 · C7 · F-H 에서 >0」 짝). 0 보다 크면 정상 길에서도 삼키는 것이다 → 분석 |
| **V-3** | §8 범위 기대값 정정 | `-` 줄 = **4** + 옛 L43 이 5-1 주석 안에 글자 그대로(`git diff` 문맥 줄) 확인 · `grep CsStatusMenu src` = 2파일 |
| **V-4** | DOM 동일 칸(R7) | 대조판 A2 ↔ F14 판, 상태 7꼴(5종 + 빈 값 「-」 T3 + 예전 기록 수기건)마다 칸 `outerHTML`(자동 id · `_bl_` 속성 제거)이 같을 것. `title` · `class bo-status-menu …` · `aria-expanded` 포함 |
| **V-5** | 주석 근거 확인(코드 행동 무관) | 칸 감쌈 주석 ① 「9.3 키 가로채기 해제가 같은 둘을 스스로 삼키는 꼴」: 사본 `CloseMenuAsync` L515~522 는 `catch (JSException)` 만 있고 `KeyInterceptorService` 는 사본에 없다 → **원문 미확인**. 해제(L517)가 끊김 · 시한 예외를 안에서 삼키지 않으면, 시한(60초) 갈래에서 L525 `SetValueAsync(false)` 를 건너뛴 채 감쌈이 삼킨다. 그러면 9.3 안 `_openState` 는 참으로 남고 항목은 비워진다(살아 있는 회로 · 극단 지연에서만). ② 「살아 있는 회로에선 이 둘이 나지 않아」 → OCE 는 살아 있는 회로에서도 JS 시한 초과로 날 수 있다. 처리: [3] 이 `KeyInterceptorService.cs` 원문 줄을 사본에 더해 확인하거나, 주석을 「원문 미확인」 으로 고친다(측정 불요) |

## ⑤ 정리
- 🔴 1(예측): R1 — 고침의 결함이 아니라 (나) 가 덮지 못하는 길이며 지시서 §3-3 · §10 B 에 이미 적혀 있다. 이 문서는 원문으로 그 위험이 **「가능」 이 아니라 「예측」** 임을 보태고, S-2d 를 k · 초점 자리 · ↑ 갈래로 쪼개 달라는 것이다.
- 🟡 4: R2(서버가 막음 · 의도 어긋남만) · R3/R10(조용한 실패 · 먹통) · V-3 게이트 기대값 오기 · V-5 주석 근거.
- 🟢 5: R4 · R5 · R6 · R8 · R9 (R7 정적 🟢 · V-4 로 닫음).
