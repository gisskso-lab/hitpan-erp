# 개발명세서 2026-10-05 — 작1 §19 F (PR #449 CI 시험 75 실패 · 시험 장치 PSModulePath)

| 항목 | 내용 |
|---|---|
| 근거 작업지시서(PRD) | [`docs/운영기록/20260930작1_PC안롤백_작업지시서.md`](../../운영기록/20260930작1_PC안롤백_작업지시서.md) §19 (설계 = 19-1) · [1-V] [선행검증서](../../검증/선행/20261002_선행검증서_PR449_CI75실패_PSModulePath.md) |
| PM 결재 | ✅ 2026-10-05 (§19-5) · 사장님 오더 10/5 「응 이대로 진행해」 |
| 개발팀 지휘 | PM 브라운킴 |
| 구현 담당 | PM 메인 직접(규모 S · 토큰 매뉴얼 §2) |
| 커밋 | F-1·F-2 `9435f6da` (브랜치 `fix/20260930-local-rollback`) |

## 1. 무엇을 만들었나 (PRD 대비)

| PRD 요구 | 구현 | 상태 |
|---|---|---|
| F-1 19-1 ① — 5.1 을 띄우기 직전 `PSModulePath` 를 뺀다 | `src/HitPan.Tests/LocalSwap/LocalSwapWorkerRig.cs:120-123`(흉내 주입 1 + 주석 2 + 빼는 줄 1 · `Process.Start` 바로 앞) | ✅ |
| F-1 19-1 ③ — 시험 전용 속성 둘(덧붙임) | 같은 파일 `:31-35` `SimulatedParentPsModulePath` · `ControlKeepPsModulePath`(인스턴스 속성 · rig 는 `internal`) | ✅ |
| F-2 G-ENV1 + 대조군 | `src/HitPan.Tests/LocalSwap/LocalSwapOnStartGateTests.cs:115-173`(독 목록 `Pwsh7UtilityCmdlets :119` · 독 만들기 `:136` · 도우미 `RunPoisoned :147` · 🟢 `:157` · 대조 `:166`) | ✅ |
| F-3 이 명세서 + INDEX | 이 문서 · `docs/개발/erp/INDEX.md` 한 줄 | ✅ |

## 2. 변경 파일 목록

| 파일 | 변경 | 왜 |
|---|---|---|
| `src/HitPan.Tests/LocalSwap/LocalSwapWorkerRig.cs` | 추가 10 / 수정 0 | 제품과 같은 조건(작업 스케줄러는 깨끗한 환경) |
| `src/HitPan.Tests/LocalSwap/LocalSwapOnStartGateTests.cs` | 추가 59 / 수정 0 | G-ENV1 + 대조 |

- 기존 코드 **추가만**(`git diff 2e221b58 9435f6da` 삭제 줄 0). 제품(`src/HitPan.API/**` · `local-swap.ps1`) · `.github` · 워치독 · installer · Migrations · appsettings 변경 0. 기존 시험 기대값 변경 0.
- `LocalSwapOnStartGateTests` 는 `build-and-test.yml` GATES 에 이미 있다 → yml 변경 0.

## 3. 헌법 준수

| 조항 | 어떻게 지켰나 | 근거 |
|---|---|---|
| #1 | 덧붙임만 | §2 |
| #19 | 빌드 0/0 | §4 |
| #29 · #39 | 머신 설정·레지스트리·서비스·이 PC 히트판 실물 무접촉 · pwsh 는 설치 없이 세션 임시 폴더에 압축본만 | — |
| #42 | [1-V] → 작업지시서 §19 결재(`2e221b58`) 뒤 착수 | — |

## 4. 자체 확인 결과 (실측)

| 항목 | 명령 | 결과 |
|---|---|---|
| 빌드 (#19) | `dotnet build src/HitPan.sln --no-incremental` | **0 Warning · 0 Error** |
| G-ENV1 + 이웃 | `dotnet test … --filter LocalSwapOnStartGateTests`(DB `invalid.invalid`) | **10/10** |
| 대조군(게이트 파일 안) | 같은 실행 `Control_keeping_module_path_breaks_worker` | 통과 = 일꾼이 **broken** + 기록에 `Get-FileHash`(CI 75 의 모양을 실제로 봄) |
| 변이(Edit 도구) | rig `:123` 빼는 줄 삭제 → 같은 필터 | **FAIL 1/10** — G-ENV1 🟢 만 빨강 |
| 원복 | Edit 로 되돌림 → `git diff` | **0** · `build-brief` 0/0 |
| CI 조건 재현 회귀 | 부모 `PSModulePath` 앞에 pwsh 7.4.6 `Modules`(실물) → 전체 | **Get-FileHash 실패 0**(고치기 전 같은 조건 76) · 남은 14 는 아래 4-1 |
| Watchdog | `HitPan.Watchdog.Tests` | **167/167** |

### 4-1. 회귀 — 🔴 C: 여유 4.26GB 로 14건 `disk_low`
- 평상 조건 · CI 조건 둘 다 **2041/2055** — 실패 14 = 전부 `LocalSwapPrevFetchGateTests`(N4c G-NP1·4·5×6·6·8·9×4) · 메시지 `전제 — 세 번째 길이 서야 한다: disk_low` · 시험이 **실제 C: 여유**(`DriveInfo.AvailableFreeSpace` · `LocalRollbackService.cs:157` 등)를 잰다. 이번 변경(시험 장치 환경변수)과 무관 — 정리 뒤 재실행 결과는 4-2.
- C: 여유 = 10/2 12.1GB → 10/5 **4.26GB**. 이 세션 몫(Release 빌드 · 검증 worktree · pwsh 압축본) 외 대부분은 옛 worktree(`.claude/worktrees` 20여 개 · `C:\HitPanBuild\wt*` 6개).

### 4-2. 정리 뒤 재실행
- 이 세션이 만든 것만 정리(검증 worktree — 변경 0·push 됨·`bf894f4c` 로 합침 · `bin|obj\Release` · pwsh 압축본) → C: **4.28 → 5.21GB**.
- `LocalSwapPrevFetchGateTests` **47/47**(실패하던 14 포함) · 전체 회귀(DB `invalid.invalid`) **2055/2055** · Skipped 0(DB 증거로 쓰지 않는다) · 기준 2053 + G-ENV1 2.
- [4] D-1(설계 19-1 ③ 독 모양이 구현과 다름) → 작업지시서 19-1 ③ 아래 🔄 정정 줄 · D-2(이 명세서 미커밋) → 이 커밋.

## 5. 안 한 것 / 못 한 것 · 있었던 일
- 🔴 **첫 독이 가짜였다**: Get-FileHash 하나만 내보내는 psd1 로 만들었더니 대조군이 **success** — 일꾼이 Get-FileHash 전에 쓰는 다른 Utility 명령(Get-Date 등)이 정품 모듈을 불러 와 풀렸다(실측: 앞에 `Get-Date` 를 두면 SHA256 정상). pwsh 7.4.6 정본처럼 **내보내기 116개 + 없는 dll 참조**로 바꾸자 Get-Date·Add-Type 은 되고 Get-FileHash 만 실패 = 실물 PS7 폴더와 같은 동작. 대조군을 안 돌렸으면 「초록인데 아무것도 안 재는」 게이트가 될 뻔했다.
- [1-V] ⚠️ 「부모 pwsh 에서 PSModulePath 를 지워도 testhost 까지 실패 지속」은 여전히 원인 미실측 — 고친 자리가 `Process.Start` 바로 앞이라 판정엔 영향 없음.
- ⚠️ `BuildManifestGuardGateTests` G-R1: 10/2 CI 조건 재현에서 Get-FileHash 로 1회 실패 → 10/5 같은 조건(Debug·Release 둘 다) **재실행 4/4 통과**. 원인 미실측(같은 독 경로를 되풀이해 쓴 탓에 5.1 모듈 분석 캐시가 끼었을 가능성 ⚠️가설). CI 에선 pwsh 로 돌아 무관 · 이 오더 밖(§19-4) — 다음 차수 후보로 기록만.
- 고객 PC 에서 작업 스케줄러가 깨끗한 환경을 준다는 것(제품 무영향 근거)은 Windows 표준 동작 · 이 세션 실측 0.
