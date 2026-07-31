# 설계서 — 6-1 `taskkill` hang 봉합 (7/30 회귀 진범)

| 항목 | 내용 |
|---|---|
| 문서 | **설계문서** — `docs/설계/설치배포/` |
| 작성 | 설계팀장 존 · PM 브라운킴 |
| 지시 | 사장님 — *"원인불명이란 없다. 인과관계란 반드시 존재하는 법. 찾아!!"* (2026-07-31) |
| 대상 | `installer/HitPan-Universal.iss:1902` · `:2291` |
| 선행 | [7/21작1 설치hang 작업지시서](../../개발/설치배포/20260721작1_설치hang_최초설치차단P0_봉합_작업지시서.md) **축 B 미이행분** |
| 상태 | 🟢 진실원 |

---

## 1. 왜 이 설계가 필요한가 — 인과 사슬 (실측 확정)

### 1-1. 진범은 이미 7/21 에 실측 채증돼 있었다

`20260721작1` 작업지시서 §진범:

```
taskkill 1828 @23:58:11  → 25분 hang
(죽인 후) taskkill 6644 @00:30:11 → 또 hang
설치 본체 HitPan-ERP-Setup-1.2.33 (3372) = 살아서 자식 taskkill 반환 대기
```

> *"`taskkill /F /IM` — 대상 없으면 즉시 반환해야 정상인데 **Windows Sandbox 에서 무한 대기**
> (taskkill /IM 의 프로세스 이미지 열거가 Sandbox 제약 계층에서 응답 못 받음 —
> 실측 중 `Get-CimInstance Win32_Process` 도 '액세스 거부' 나온 것과 동일 계층)."*

`Exec(..., ewWaitUntilTerminated)` = **무한 대기**. taskkill 이 안 끝나면 설치 전체가 영영 멈춘다.

**같은 문서가 `sc stop`·`sc delete` 를 무죄로 확정했다** (`sc query`=1060 → 즉시 반환).

### 1-2. 봉합이 3곳 중 1곳만 됐다 — 이것이 회귀의 실체

| 위치 | 가드 | 상태 |
|---|---|---|
| `:1450-1452` (`StopRunningComponentsForReinstall`) | ✅ `IsPreviouslyInstalled()` | 7/21 축 A 봉합 |
| **`:1902` (6-1)** | ❌ **없음** | 🔴 **7/30 사고 현장** |
| `:2291` (`DeinitializeSetup`) | ❌ 없음 | 🟡 잠복 |

7/21 커밋 `f06615b` 메시지가 스스로 갭을 명시했다:

> *"축 B(재설치 경로에서 taskkill /IM 자체의 hang 방어)는 다음 작지서 P1 로 분리"*

**축 B 는 오늘까지 미이행이다.** 그 사이 작10(`b793664`)이 `ExecLogged` 로 감싸기만 했을 뿐
**hang 방어는 0건**이다.

### 1-3. 로그 침묵이 이 지점을 지목한다

7/30 실측본(`e55a1e9`) 6-1 구간 — **로그 0줄**:

```pascal
Exec('/C sc stop cloudflared & timeout /t 3 /nobreak & sc delete cloudflared & timeout /t 2 /nobreak',
     '', SW_HIDE, ewWaitUntilTerminated, ResultCode);   // 로그 없음
Exec('/C taskkill /F /IM cloudflared.exe',
     '', SW_HIDE, ewWaitUntilTerminated, ResultCode);   // 로그 없음 · 무한 대기
```

설치 로그 마지막 줄 = `[FixupWatchdog] 정정 완료`(`:1797`).
그 다음 `Log()` 호출은 6-1-2 분기(`:1917`)까지 **없다.**

⇒ **`:1797` ~ `:1917` 사이에서 멈췄다.** 그 구간의 실행 요소는
registry.json PowerShell(`:1865`) · 6-1 sc 체인 · **6-1 taskkill** 셋뿐이다.

### 1-4. "전제가 전부 정상인데 결과만 실패"의 정체

설계서(20260730)가 든 전제 8개는 **전부 6-1 이전 단계의 산물**이다:

| 전제 | 산출 단계 |
|---|---|
| cloudflared.exe 54MB | 파일 복사 |
| 토큰 유효 · db.conf 기록 | 부트스트랩·5단계 |
| 부트스트랩 success | NCP 통신 |

**6-2 는 시작도 안 했으므로 그 어떤 전제도 반증이 되지 못한다.**
PM 이 9번 잘못 짚은 이유가 이것이다 — 전부 6-2 이후를 의심했는데 사건은 6-1 에서 끝나 있었다.

### 1-5. 사장님 "6-1 정상 동작" 실측이 왜 반증이 아닌가

20260730 설계서 §1-2 는 *"6-1 좀비 제거 ✅ 정상 — 사장님 수동 실행 실측"* 으로 6-1 을 무죄 처리했다.

**이것이 결정적 오판이다.**

| | 실행 컨텍스트 |
|---|---|
| 사장님 수동 실행 | **관리자 명령창(대화형 콘솔)** |
| 설치기 | `Exec(..., SW_HIDE, ...)` **비대화형 자식 프로세스** |

7/21 hang 은 **후자에서만** 재현됐다(`Get-CimInstance Win32_Process` 액세스 거부와 동일 계층).
**다른 실행 컨텍스트의 성공으로 hang 을 반증할 수 없다.**

---

## 2. 🔴 풀스택 매니저 권고를 그대로 쓰면 안 된다 (PM 반증)

권고: *"`:1450` 에 검증된 `IsPreviouslyInstalled()` 가드를 `:1902` 에도 적용"*

**이 가드는 6-1 에서 작동하지 않는다.** 실측:

```pascal
function IsPreviouslyInstalled(): Boolean;
begin
  Result := FileExists(ExpandConstant('{app}\db.conf'))         // ← 이것
         or FileExists(ExpandConstant('{app}\hitpan-keys.conf'));
end;
```

```
:1747  BootstrapContent.SaveToFile('{app}\db.conf')   ← db.conf 가 여기서 생성된다
:1902  6-1 taskkill                                   ← 그 뒤다
```

⇒ **백지 설치인데도 6단계 시점엔 `db.conf` 가 이미 존재**한다.
⇒ `IsPreviouslyInstalled()` = **항상 `true`** → 가드가 무력 → taskkill 그대로 실행.

> **봉합한 척만 하는 셈이다.** 검증팀이 이걸 놓치면 "봉합 완료"로 기록되고
> 다음 실측에서 또 같은 자리에서 멈춘다. 오늘 하루에만 이런 계열의 P0 가 3건 나왔다.

---

## 3. 목표

| # | 목표 | 판정 |
|---|---|---|
| **G-1** | 6-1 이 Sandbox 에서 hang 하지 않는다 | 설치가 6-2 로 진행 |
| **G-2** | 좀비 프로세스 정리 기능을 잃지 않는다 | 재설치 시 잔존 프로세스 정리됨 |
| **G-3** | hang 이 나도 설치가 영구 정지하지 않는다 | 시간 상한 존재 |
| **G-4** | 실패해도 침묵하지 않는다 | 로그에 종료코드·사유 |

---

## 4. 설계 — 3중 방어

### 4-1. [1차] `/IM`(이미지명 열거) 대신 `/FI`(필터) 사용 — 근본 봉합

hang 의 원인은 **프로세스 이미지 열거**다(7/21 실측: `Win32_Process` 액세스 거부와 동일 계층).

```
종전: taskkill /F /IM cloudflared.exe          ← 전체 프로세스 이미지 열거
봉합: taskkill /F /FI "IMAGENAME eq cloudflared.exe"
```

⚠️ **미검증**: `/FI` 도 내부적으로 열거를 쓸 가능성이 있다. 이 파일에 선례 0건이다.
⇒ **단독 채택하지 않는다.** 아래 4-2·4-3 과 함께 3중으로 간다
   (작10 교훈: *"선례 없는 기법을 '검증된 패턴'이라 부른 것이 P0 를 놓친 원인"*).

### 4-2. [2차] 시간 상한 — hang 해도 설치가 멈추지 않는다

```
cmd /C "start /B /WAIT timeout /t 15 /nobreak >nul & taskkill ..."   ← 부적합(중첩 복잡)
```

Inno Setup 의 `Exec` 는 타임아웃 인자가 없다. 대신 **비동기 실행**을 쓴다:

```pascal
Exec('{cmd}', '/C start "" /MIN taskkill /F /IM cloudflared.exe', '', SW_HIDE, ewNoWait, ResultCode);
Sleep(5000);   // 정리 시간만 주고 결과를 기다리지 않는다
```

`ewNoWait` = **반환을 기다리지 않는다.** taskkill 이 hang 해도 설치는 계속 간다.

⚠️ 대가: 종료코드를 못 받는다(G-4 손실). ⇒ 4-3 이 이를 보상한다.

### 4-3. [3차] 실행 조건 축소 — 백지에선 아예 안 돈다

**6-1 taskkill 의 존재 의의를 재검토한다.**

```pascal
:1897  ExecLogged('6-1-scstop',   'sc stop cloudflared');    ← 서비스 정지(프로세스 종료 포함)
:1899  ExecLogged('6-1-scdelete', 'sc delete cloudflared');  ← 등록 해제
:1902  ExecLogged('6-1-taskkill', 'taskkill /F /IM ...');    ← 그 뒤 잔존 프로세스 대비
```

**백지 신규설치에는 죽일 cloudflared 프로세스가 존재하지 않는다.**
`sc stop` 종료코드가 **1060(서비스 없음)** 이면 프로세스도 없다는 확증이다.

```pascal
if ScStopCode <> 1060 then      // 서비스가 있었다 = 프로세스 잔존 가능
  (taskkill 실행)
else
  Log('서비스 미등록 — 잔존 프로세스 없음, taskkill 건너뜀');
```

⇒ **백지에선 taskkill 자체가 안 돈다.** hang 원천 차단.
⇒ 재설치 경로에선 여전히 돈다(G-2 보존).

### 4-4. 채택 — 4-3 + 4-2 (4-1 은 보류)

| 안 | 채택 | 이유 |
|---|---|---|
| 4-1 `/FI` | ❌ **보류** | 선례 0건. 검증 없이 "봉합됐다"고 하면 작10 재발 |
| 4-2 `ewNoWait` | ✅ | hang 해도 설치 진행. 안전망 |
| 4-3 조건부 실행 | ✅ **주력** | 백지에서 원천 차단. `sc stop` 종료코드는 이미 `ExecLogged` 가 반환 |

**4-3 이 주력이고 4-2 가 안전망이다.** 재설치 경로에서 taskkill 이 돌 때도 hang 하지 않는다.

### 4-5. `:2291`(DeinitializeSetup) 도 함께

같은 무가드 taskkill 이다. 설치 **종료 시점**이라 hang 하면 설치 마법사가 안 닫힌다.
동일하게 `ewNoWait` 로 전환한다.

---

## 5. 위험 분석

| # | 위험 | 평가 | 대응 |
|---|---|---|---|
| R-1 | `ewNoWait` 로 taskkill 이 안 끝난 채 6-2 진입 → 파일 잠금 | 중 | `Sleep(2000)` 유지. 백지에선 4-3 이 실행 자체를 막음 |
| R-2 | `sc stop` 이 1060 이 아닌 다른 코드를 낼 수 있다 | 중 | `<> 1060` 이 아니라 **`= 1060` 일 때만 건너뛴다**(안전측) |
| R-3 | 종료코드 상실로 관측성 후퇴 (작10 역행) | **높** | 건너뛸 때도 **반드시 로그 1줄**. 침묵 금지(헌법 #15) |
| R-4 | `ewNoWait` 가 이 파일에 선례 있는가 | 중 | **확인 필요** — 없으면 `ewWaitUntilTerminated` 유지하고 4-3 만 |
| R-5 | 진범이 taskkill 이 아니라 `timeout` 이었을 가능성 | 중 | 작10 `ExecLogged` 가 이미 `timeout` 을 `Sleep` 으로 대체함(봉합됨) |

> **R-3 이 최대 위험이다.** 오늘 작10 이 *"관측성을 넣는다며 관측성을 파괴"* 했다.
> 같은 실수를 반복하지 않는다 — **건너뛰는 것도 반드시 기록한다.**

---

## 6. 검증 기준 (실측 전 확정)

| # | 기준 | 방법 | 통과 조건 |
|---|---|---|---|
| **V-1** | ISCC 컴파일 | CI | `Successful compile` · 신규 경고 0 |
| **V-2** | `ewNoWait` 선례 | grep | 있으면 채택 / 없으면 4-3 만 |
| **V-3** | 백지에서 taskkill 미실행 | 설치 로그 | `[6-1-taskkill] 건너뜀` 로그 존재 |
| **V-4** | 6-2 도달 | 설치 로그 | `[6-2-serviceinstall]` 줄 존재 |
| **V-5** | 관측성 무손실 | 로그 | 건너뛴 사유가 로그에 남음 |
| **V-6** | 재설치 경로 무손상 | 코드 리뷰 | 서비스 존재 시 taskkill 여전히 실행 |

> ⚠️ **V-3 이 이 봉합의 직접 증거다.** 6-1 을 통과했다는 것만으로는 부족하다 —
> **건너뛰었다는 로그**가 있어야 4-3 이 작동한 것이다.

---

**설계** 설계팀장 존 · PM 브라운킴 · 2026-07-31
**근거** [7/21작1 축 B 미이행분](../../개발/설치배포/20260721작1_설치hang_최초설치차단P0_봉합_작업지시서.md) · 풀스택 매니저 인과 규명
**PM 반증** §2 — 권고된 `IsPreviouslyInstalled()` 가드는 6-1 에서 무력(db.conf 선행 생성)
**다음** [3] 구현 → [3-V] 병렬검증 → [4] 검증
