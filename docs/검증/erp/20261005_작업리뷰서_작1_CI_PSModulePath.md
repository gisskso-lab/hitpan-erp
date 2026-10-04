# 작업리뷰서 2026-10-05 — 작1 §19 CI PSModulePath 봉합 (G-ENV1)

| 항목 | 내용 |
|---|---|
| 작업지시서(PRD) | `docs/운영기록/20260930작1_PC안롤백_작업지시서.md` §19 (19-1~19-5) |
| 선행검증서 | `docs/검증/선행/20261002_선행검증서_PR449_CI75실패_PSModulePath.md` §0·§1·§3 |
| 개발명세서 | ❌ 이 커밋에 없음 — 작지 19-2 F-3 `docs/개발/erp/20261005작1_CI_PSModulePath_개발명세서_F.md` 미존재(아래 D-2) |
| 대상 | `fix/20260930-local-rollback` `9435f6da` (부모 `2e221b58`) · 시험 파일 2개 · +69줄 · 삭제 0 |
| 구현자 | PM 브라운킴 (메인 직접) |
| **검증자** | [3-V] 보안상무 안철수 관점 + [4] 검증팀장 데이비드 박 관점 — `hp-verifier` 1 (격리 worktree · 구현 문맥 밖) |
| **판정** | ⚠️**조건부 통과** — 코드 결함 0 · 대조 성립 · CI 조건 재현에서 PSModulePath 원인 실패 0(G-R1 1건은 오더 밖). 조건 = D-2 명세서 F + 작지 19-3 ⑤ CI 75→0 실측 |

## 1. PRD 대비 검증

| PRD 요구 (작지 §19) | 실제 확인 | 판정 |
|---|---|---|
| 19-1 ① `Process.Start` 직전 `psi.Environment.Remove("PSModulePath")` | `LocalSwapWorkerRig.cs:123` · 주석 2줄 + 코드 1줄 · `Run` 두 겹(:100 → :103) 모두 이 한 몸을 지난다 | ✅ |
| 19-1 ③ 속성 둘 덧붙임 · 넣는 줄이 빼는 줄 **앞** | `:31-35` 인스턴스 속성 2개 · `:120`(넣기) → `:123`(빼기) 순서 | ✅ |
| 19-1 ③ 독 = psd1 하나 · `CmdletsToExport Get-FileHash` | 🟡 **구현은 116개 내보내기**(pwsh 7.4.6 정본 모양) — 설계와 다름. 근거는 코드 주석(사실 2: 하나만 내보내면 정품이 불려 와 풀림)뿐, 작지 미개정(D-1) | 🟡 |
| 19-1 ③ 테스트 프로세스 환경변수 무접촉 | `PoisonedParentPsModulePath` 는 testhost `PSModulePath` 를 **읽기만** · 쓰기는 `psi.Environment`(자식 전용) | ✅ |
| 19-2 F-2 기존 Fact 수정 0 · 끝에 덧붙임 | `git diff 2e221b58..9435f6da` = `+` 줄만(삭제 0) | ✅ |
| 19-3 ① 빌드 0/0 | §2 | ✅ |
| 19-3 ③ CI 조건 재현 회귀 → 0 (G-R1 제외) | §3 A-3 — 15 실패 = G-R1 1 + **환경(디스크) 14** · PSModulePath 원인 0 | ✅(단서) |
| 19-3 ④ 대조 ①② | §3 A-1·A-2 | ✅ |
| 19-3 ⑤ CI 75 → 0 | ⬜ 이 검증 범위 밖(push 후 PR #449 run 미실측) | ⬜ |
| 19-3 ⑥ 개발명세서 F + INDEX | ❌ 이 커밋에 없음 | ❌ |
| 19-4 금지 — 제품·`.github`·워치독·installer·Migrations·appsettings 0 | `--stat` = `src/HitPan.Tests/LocalSwap/` 2파일뿐 | ✅ |

## 2. 3관문

| 관문 | 결과 | 근거 (실제 출력) |
|---|---|---|
| 빌드 errors 0 + warnings 0 (#19) | ✅ | `dotnet build src/HitPan.sln --no-incremental` → `0 Warning(s) 0 Error(s)` (2:38) |
| ddl-smoke (#36) | — | DDL 변경 0 |
| 독립 반증 | ✅ | §3 — 7건 |

## 3. 독립 반증 — 실측 표

| # | 명령·조건 | 결과 | 판정 |
|---|---|---|---|
| A-0 | 기준 — `test-brief.ps1 -Filter LocalSwapOnStartGateTests` (DB `invalid.invalid` · 부모 PSModulePath 정상) | **10/10 통과** (사실 4 재현) | ✅ |
| A-1 | 🔴 대조 ② — `:123` 빼는 줄을 **Edit 로 지운** 사본 · 같은 필터 | **9/10 · G-ENV1 🟢 FAIL**(2s) · 대조군은 통과 ⇒ 게이트가 봉합 줄의 유무를 가른다 | ✅ |
| A-1b | A-1 사본 그대로 + 부모 PSModulePath 맨 앞에 독 폴더(사실 2 모양 · scratchpad 에 직접 생성 · 116개 확인) · `HitPan.Tests.LocalSwap` 네임스페이스 전체 | **90/333 실패** — 그중 일꾼 `unexpected: 'Get-FileHash' 용어가 …`(한국어 로캘 문구) 다수 · 14건은 아래 E-1(디스크) ⇒ 독 원인 **76 = CI 75 + G-ENV1 🟢 1** · 부모 → dotnet → testhost → rig 로 독이 **실제로 전달**됨 확인(선행 §1 ⚠️ 경로와 반대 방향은 성립) | ✅ |
| A-1c | Edit 로 원복 → `git diff` | **0줄** · `git status` 깨끗 | ✅ |
| A-2 | 독이 5.1 에 서는지 직접 — 독 PSModulePath 로 5.1 에서 `Get-Date` · `Get-FileHash` / 변수 지우고 `Get-FileHash` | `2026` 출력 후 **`The term 'Get-FileHash' is not recognized`** / 지우면 **`SHA256`** — 사실 2 재현 | ✅ |
| A-3 | 🔴 CI 조건 재현 — 봉합 상태 + 부모 PSModulePath 앞 독 폴더 · `HITPAN_DB_HOST=invalid.invalid` · 전체 회귀(trx) | **2040/2055 · 실패 15** = `BuildManifestGuardGateTests` G-R1 1(Get-FileHash · 오더 밖 · 기대대로 남음) + `LocalSwapPrevFetchGateTests` **14(전제 `disk_low`)** · LocalSwap 의 Get-FileHash 실패 **0** | ✅ |
| A-3b | A-3 의 14건이 독 탓인가 — 독 **없이** `-Filter LocalSwapPrevFetchGateTests` | **같은 14 실패 / 47** · `C:` 여유 **4.29 GB** ⇒ 독 무관 · 이 PC 디스크 환경 의존(E-1) | ✅ 독 무관 확정 |

**우회 경로 점검 (정적)**
- rig 의 `Process.Start` 는 `:124` 한 곳 · `Run(script, failAt)` 은 3인자 `Run` 으로 위임 ⇒ 빼는 줄을 건너는 호출 경로 0. LocalSwap 시험 중 rig 밖에서 프로세스를 띄우는 곳 0(`LocalSwapPipelineGateTests:175` 는 정규식 글자).
- 속성 누출: 두 속성은 **인스턴스** 속성 · 기본값 null/false · 설정하는 곳은 `RunPoisoned` 의 `using var rig` 하나뿐 · 공유 rig(static 필드·ClassFixture) 0 ⇒ 다른 시험으로 새지 않는다.
- `ProcessStartInfo.Environment` 는 Windows 에서 대소문자 무시 사전 ⇒ `PSMODULEPATH` 같은 철자 변형도 같이 빠진다.
- CI 「작1 게이트」 단계는 클래스별 `n -eq 0` 만 본다(`build-and-test.yml:71-75`) ⇒ Fact 2개 추가로 단계가 깨지지 않음 · yml 변경 0 이 맞다.
- 대조군의 구별력: `Assert.True(State == Broken, "독이 안 섰다…")` + `Contains("Get-FileHash")` — 독이 안 서면 success 로 FAIL. 문구가 아니라 명령 이름 토큰을 보므로 한국어 로캘에서도 성립(A-1b 에서 한국어 문구 확인).

## 4. 코드리뷰 발견

| # | 등급 | 위치 | 내용 | 재현 | 봉합 |
|---|---|---|---|---|---|
| D-1 | P3 | 작지 §19 19-1 ③ ↔ `LocalSwapOnStartGateTests.cs:119-145` | 독 재료가 결재된 설계(`CmdletsToExport Get-FileHash` 하나)와 다르게 116개로 구현됨. 이유(사실 2 — 하나짜리는 대조군이 success)는 코드 주석에만 있고 결재 문서는 옛 모양 그대로 ⇒ 다음 사람이 작지대로 「고치면」 대조군이 죽는다 | 정적 대조 | ⬜ 작지 19-1 ③ 정정 또는 명세서 F 에 기록 |
| D-2 | P3 | `docs/개발/erp/` | 19-2 F-3 개발명세서 F + INDEX 가 `9435f6da` 에 없음(완료 기준 ⑥ 미충족) | `ls docs/개발/erp | grep 20261005` → 0 | ⬜ |
| D-3 | P3 ⚠️ | `LocalSwapWorkerRig.cs:121-123` 주석 | 「제품은 SYSTEM 이 깨끗한 환경으로 띄운다 ⇒ 같은 조건」은 근사다. SYSTEM 작업도 **머신 수준** `PSModulePath`(레지스트리 Environment)는 받는다. 시험은 변수를 통째로 지우므로, 고객 PC 머신 PSModulePath 에 `Microsoft.PowerShell.Utility` 그림자가 있는 경우(제품 쪽 실패)는 이 시험이 못 잡는다. 사실 3 ⚠️(고객 PC 실측 0) 그대로 | 미실측(예약작업 조작 금지) | ⬜ 기록만 · 별건 후보 |
| D-4 | P3 | 사실 2 | pwsh 7.4.6 `Utility` 폴더 「psd1 하나 · dll 없음 · 116개」는 재료(압축본)가 남아 있지 않아 독립 재측정 못 함. 단 독의 **효과**는 A-1b·A-2 로 확인 ⇒ 판정 영향 없음 | — | — |
| E-1 | P2 (오더 밖) | `LocalSwapPrevFetchGateTests` (N4c G-NP1·4·5·6·8·9) | 실제 `C:` 여유 공간(`DriveInfo.AvailableFreeSpace` · 필요량 = 풀린 크기×2 + 512MB)에 의존 — 여유 4.29GB PC 에서 **14건 `disk_low` 실패**(독 유무 무관). 시험이 밀폐돼 있지 않다 ⇒ 「회귀 2055 통과」(19-3 ②)는 디스크 여유가 있는 PC 에서만 성립 · CI 러너는 통과(여유 큼) | A-3b | ⬜ 별건 — PM 판단(1.3.50 확대 [4] 리뷰서 쪽 소관) |

P0·P1 = **0** ⇒ 병렬이슈 별도 파일 없음.

## 5. 거짓봉합 점검

- [x] 봉합 전 증상 재현 — A-1b(봉합 줄 없음 + 독 → Get-FileHash 76) → A-3(봉합 + 독 → LocalSwap Get-FileHash 0)
- [x] 회귀 — A-3 전체 2055 중 봉합 원인 실패 0 (15건 = G-R1 1 · 디스크 14, 둘 다 봉합 무관 · A-3b 로 분리)
- [x] 문서만 고친 것 아님 — 코드 `:123` 이 있어야 G-ENV1 이 초록(A-1)
- [ ] 개발명세서 §5(안 한 것) — 명세서 자체 없음(D-2)

## 6. [3-V] 보안상무 안철수 관점

| 축 | 확인 | 판정 |
|---|---|---|
| 시험 전용 속성이 제품에 들어가나 | rig = `internal sealed` · `src/HitPan.Tests` 안 · 제품 csproj 중 Tests 를 참조하는 곳 0(`HitPan.Infrastructure` 의 `InternalsVisibleTo HitPan.Tests` 는 반대 방향) · installer·workflow 에 Tests 어셈블리 실어 보내는 줄 0 | ✅ |
| #2 tenant_id · #5 암호화 · #18·#22 본사 전송 | 무관 — DB·네트워크·테넌트 접촉 0 | ✅ |
| #23 외부 노출 금지어 | `git diff` `+` 줄에 AI·Claude·Cursor 0 | ✅ |
| 시험이 머신을 더럽히나 | 독 psd1 은 rig 임시 Root 아래에만 쓰고 `Dispose` 가 Root 를 지운다(`:443-447` · 실패 시 로그 — 빈 catch 아님 #15) · 프로세스·머신 환경변수 쓰기 0 · 레지스트리·예약작업 0 | ✅ |
| 봉합이 제품 위험을 가리나 | 시험 경로만 바뀜. 단 D-3 — 머신 수준 그림자 모듈 시나리오는 이제 어떤 시험도 안 잰다(전에도 안 쟀다 · 새 구멍 아님) | ⚠️ 기록 |
| 헌법 #1·#15·#19 | 삭제 0(덧붙임만) · 빈 catch 추가 0 · 0/0 | ✅ |

이 검증이 한 조작: worktree 안 `LocalSwapWorkerRig.cs` 한 줄 Edit 삭제 → Edit 원복(`git diff` 0). 독 폴더는 세션 scratchpad. 머신 설정·서비스·예약작업·`C:\Program Files\HitPan`·localhost:5257 무접촉 · pwsh 미설치.

## 7. 판정 근거

- ⚠️**조건부 통과** — 3관문 전부 · P0·P1 0 · 대조 2종 성립(봉합 줄 제거 시 G-ENV1 FAIL · 대조군이 독을 실제로 세움) · CI 조건 재현에서 PSModulePath 원인 실패 0.
- **통과 조건**: ① D-2 개발명세서 F(+INDEX) — D-1 독 모양 변경 사유를 거기 적을 것 ② 작지 19-3 ⑤ push 후 PR #449 `Build (errors 0 + warnings 0)` 시험 실패 75 → 0 실측.
- 기록만(판정 무관): D-3·D-4 · E-1(디스크 의존 14건 — 별건으로 PM 판단).
