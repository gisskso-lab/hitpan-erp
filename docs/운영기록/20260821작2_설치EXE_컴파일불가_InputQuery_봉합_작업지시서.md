# 20260821작2 — 설치 EXE 컴파일 불가 봉합 (InputQuery) 작업지시서

> 작성: 설계팀장 존 | 결재: PM(브라운킴) → CTO 래리 → 사장님
> 사장님 지시 (2026-08-21): *"봉합해"* · *"가장 중요한거야"*
> 등급: **P0** — 게시 경로 전면 차단
> 브랜치: `fix/20260821-iss-inputquery`

---

## 0. 무엇이 났나

**1.2.96 게시가 EXE 굽는 단계에서 죽었다.**

```
Error on line 1876 in installer/HitPan-Universal.iss: Column 12:
Unknown identifier 'InputQuery'
Compile aborted.
```

`InputQuery` 는 **델파이 VCL 함수**다. Inno Setup 의 Pascal Script 에는 **존재하지 않는다.**
공식 지원 함수 목록에 없다 — 있는 것은 `MsgBox` · `TaskDialogMsgBox` · `GetOpenFileName` ·
`GetSaveFileName` · `BrowseForFolder` · `ExitSetupMsgBox` · `SelectDisk` 뿐이다.

⇒ 없는 함수를 부르고 있어 **컴파일이 아예 안 된다.**

### 파괴력 — 1.2.96 만의 문제가 아니다

| 막힌 것 | 내용 |
|---|---|
| **모든 버전 게시** | EXE 를 못 구우니 어떤 버전도 배포 불가 |
| **두 번째 사업자 설치** | 어제(8/20) P0 인데, 그 EXE 를 못 만든다 |
| **신규 고객 설치** | 새 EXE 가 안 나온다 |

---

## 1. 언제 들어왔나 — 어제 봉합이 심었다

```
310f8d49  fix(설치): 기존 DB 있는 PC에서 설치가 죽던 것 봉합 (20260820작5) (#199)
```

R-2 봉합(기존 MariaDB root 비번을 사용자에게 묻기)을 넣으면서 `InputQuery` 를 썼다.
**설계는 옳았다** — 20260425작3:102 에 *"root 실패 → 사용자에게 root 비번 요청"* 이 처음부터 있었다.
구현에 **존재하지 않는 함수**를 쓴 것이 문제다.

### 🔴 왜 어제는 안 걸렸나 (이게 진짜 교훈이다)

| 검사 | 하는 일 | 이 결함을 잡나 |
|---|---|---|
| PR CI `Installer Inno Setup Lint` | **파싱만** | ❌ 못 잡음 |
| 실제 게시 (ISCC 컴파일) | `[Code]` 섹션 **컴파일** | ✅ 잡음 |

1.2.95 는 **브랜치 커밋에서** 구웠고, PR 검사는 문법만 훑는다.
⇒ **PR 9개 검사 전부 초록인데 게시에서 터졌다.**

> 어제 인수인계서 §6 PM 실패 4건에 **"`InputQuery` 확인 없이 사용"** 이 이미 적혀 있었다.
> **적어만 두고 고치지 않아서** 오늘 터졌다. 기록은 봉합이 아니다.

---

## 2. 실행 시점 판정 — 마법사 페이지는 쓸 수 없다

1876행은 `CurStepChanged(CurStep: TSetupStep)` **안**이다 (1726행에서 시작).
= **설치 실행 중(ssInstall)**. 마법사 페이지는 이미 다 지나갔다.

기존 `TInputQueryWizardPage`(`SerialKeyPage`·`ParentAccountPage`)는
`InitializeWizard` 에서 **미리 만들어** 마법사 흐름에 끼우는 물건이라 여기선 못 쓴다.
(마법사 *페이지*는 `TForm` 이 아니라 `ShowModal` 자체가 없다)

⇒ **런타임에 띄우는 모달 대화상자**가 필요하다.

---

## 3. 설계 — `CreateCustomForm` + `TPasswordEdit`

공식 문서가 보증하는 유일한 방법이다. `CreateCustomForm` 은 진짜 `TSetupForm`(=`TForm` 계열)을
돌려주므로 `ShowModal` 이 있다.

```
AskPassword(제목, 안내문, var 비번) : Boolean
  · TNewStaticText  안내문 (여러 줄)
  · TPasswordEdit   비번 입력 (Password=True → 가려짐)
  · TNewButton × 2  확인(mrOk) / 취소(mrCancel)
```

### 🔴 함께 봉합하는 것 1 — 무인설치 무한대기

**조사에서 새로 드러난 것이다.** 모달 대화상자는 `/SILENT`·`/VERYSILENT` 에서
**답할 사람이 없는데 영원히 기다린다.** `/SUPPRESSMSGBOXES` 도 Code 함수 대화상자는 못 막는다.

컴파일 오류보다 나쁘다 — 실패가 아니라 **멈춤**이라 아무도 모른다.

⇒ `WizardSilent` 로 막는다. 무인설치면 **묻지 않고 즉시 실패**로 떨어뜨린다.
   기존 `DB-ROOT-AUTH` 오류 `MsgBox` 도 같은 결함이 있어 함께 막는다.

> ⚠️ 현재 무인설치 경로는 **레포에 없다**(고객은 우클릭 실행). 지금 당장 나는 사고는 아니다.
> 그래도 막는 이유: 한 줄이면 되고, 멈춘 설치는 고객이 스스로 못 빠져나온다.

### 🔴 함께 봉합하는 것 2 — CI Inno 버전 미고정

```
choco install innosetup -y --no-progress     ← build-installer.yml:169 · deploy-update.yml:577
```

버전이 안 박혀 있다. 그런데 **Inno 6.6.0 에서 `CreateCustomForm` 시그니처가 바뀌었다**
(`FlipSizeAndCenterIfNeeded`→`FlipAndCenterIfNeeded`, `SizeAndCenterOnShow`→`CenterOnShow`,
`KeepSizeX` 읽기전용). 최신은 6.7.3.

⇒ **우리가 커밋 하나 안 해도 남이 우리 빌드를 깰 수 있는 구조다.**
   6.6.0 개명이 그게 실제로 일어난다는 증거다.

**대응 2중**:
1. 코드를 `{$IF VER >= 6060000}` 로 **양쪽 버전 다 컴파일되게** 쓴다
2. CI 버전을 **고정**한다

---

## 4. 지시

| ID | 내용 | 파일 |
|---|---|---|
| F1 | `AskPassword()` 헬퍼 신설 (`CreateCustomForm`+`TPasswordEdit`) | `HitPan-Universal.iss` |
| F2 | 1876행 `InputQuery` → `AskPassword` 교체 | `HitPan-Universal.iss` |
| F3 | `WizardSilent` 가드 — 무인설치 무한대기 차단 | `HitPan-Universal.iss` |
| F4 | `DB-ROOT-AUTH` 오류 MsgBox 도 무인 가드 | `HitPan-Universal.iss` |
| F5 | CI Inno 버전 고정 (2곳) | `build-installer.yml` · `deploy-update.yml` |

**손대지 않는 것**: R-2 봉합의 **로직**(3회 재시도·성공 시 `MariaRootPw` 교체·실패 시 `DB-ROOT-AUTH` 중단)은
어제 설계 그대로 둔다. **호출하는 함수만 바꾼다** (§#1 덮어쓰기 금지).

---

## 5. 완료 판정 — 🔴 글자검사 금지

이 결함이 **글자검사를 통과했기 때문에** 오늘 터졌다. 같은 실수를 반복하지 않는다.

| # | 판정 | 방법 |
|---|---|---|
| 1 | **ISCC 실제 컴파일 통과** | 로컬 ISCC 로 컴파일. *"고쳤다"가 아니라 "컴파일된다"* |
| 2 | **봉합 빼면 FAIL 재현** | `AskPassword` 를 `InputQuery` 로 되돌리면 다시 컴파일 실패 |
| 3 | 게시 워크플로 통과 | CI ISCC 스텝 초록 |
| 4 | 무인 가드 동작 | `WizardSilent` 분기가 실제로 존재 |

🔴 **2번이 핵심**: 봉합을 빼서 실패를 재현하지 못하면, 통과한 이유가 봉합인지 다른 것인지 모른다.

---

## 6. 남은 위험 (이번 범위 밖 — 명시)

- ⚠️ **설치 종단 실측은 이번 범위 아님.** 컴파일이 되는 것과 대화상자가 실제로 뜨고
  비번을 받아 설치가 완주하는 것은 다르다. **백지 PC 실측이 진짜 게이트다.**
- ⚠️ **두 번째 사업자 설치**(어제 P0)는 이 EXE 가 나와야 시작할 수 있다.
