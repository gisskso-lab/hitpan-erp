# 선행검증서 2026-10-02 — PR #449 CI 시험 75건 실패 원인 실측

> 근거 = [10/2 인계1](../../운영기록/20261002인계1_1350_NCP되돌리기_CTO조건부_사장님승인_CI빨강_인수인계서.md) §3 #1 · §9 · 이 문서는 [1-V] 선행검증이다 — **설계하지 않는다.** 원인·재료·제약만 적는다.

## §0 카드

| 항목 | 내용 |
|---|---|
| 검증자 | 검증팀장 데이비드 박 관점 · 코드 수정 0 · 운영 접촉 0(#39) · 이 PC 실물은 `/health` 읽기만 |
| 대상 | `fix/20260930-local-rollback` `94234dcc`(= `e12c56e4` + 문서 2커밋 · 코드 diff 0) · CI run `36982051337`(PR #449 head `94234dcc`) · 앞 run `36980365343`(head `4380b55e`) |
| 판정 | 🟢 **원인 하나로 확정 — 제품 결함 아님, 시험 장치의 환경 상속.** CI 의 `dotnet test` 는 pwsh 7 셸에서 돈다 → 시험 장치(`LocalSwapWorkerRig`)가 띄우는 Windows PowerShell 5.1 이 **pwsh 7 의 `PSModulePath` 를 그대로 물려받는다** → 5.1 이 `Get-FileHash` 를 못 찾는다 → 일꾼 `unexpected` → `broken/revert_failed` |
| ⛔ 막는 것 | 없음. `.github` 무접촉으로 고칠 길이 있다(§3) |
| 표기 | 🟢 실측 · ⚠️ 추론/미실측 · 🔴 설계가 반드시 알 것 |

## §1 실측

| # | 무엇 | 결과 |
|---|---|---|
| M-1 | CI 로그의 일꾼 오류 문구 집계(run `36982051337`) | `unexpected:` **106줄 전부** `The term 'Get-FileHash' is not recognized` — 다른 종류 0 🟢 |
| M-2 | 로컬 `dotnet test -c Release --filter LocalSwapOnStartGateTests`(부모 셸 = Windows PowerShell 5.1) | **8/8 통과** — 인계서 가설 「Release 구성 차이」 🔴 **기각** 🟢 |
| M-3 | 같은 시험을 pwsh 7.4.6(설치 없이 압축본을 임시 폴더에)에서 실행 | **7 실패 / 1 통과 — CI 와 같은 7건**(I-API6 8건 중 7) 🟢 |
| M-4 | 5.1 부모에서 변수 하나씩 바꿔 G-PV1 1건 실행 | X0 그대로 → **통과** · X1 `PSModulePath` 앞에 pwsh 7 `Modules` → **`Get-FileHash` 실패** · X2 `PATH` 앞에 pwsh 7 폴더 → **통과** ⇒ 변수는 `PSModulePath` 🟢 |
| M-5 | pwsh 7 → `cmd` → powershell.exe 5.1 로 `Get-FileHash` 직접 호출 | 물려받으면 **실패** · `PSModulePath` 지우면 **SHA256 정상** 🟢 (pwsh 가 powershell.exe 를 **직접** 부를 때는 pwsh 가 경로를 고쳐 줘서 안 난다 — 사이에 dotnet·cmd 가 끼면 안 고쳐진다) |
| M-6 | 전체 회귀 2053 을 X1 조건(5.1 부모 + pwsh 7 `Modules`)으로 실행 → 실패 이름 집합을 CI 와 대조 | 로컬 **76** · CI **75** · **CI 에만 있는 실패 0** · 로컬에만 1건 = `G-R1 진짜 build-manifest.ps1`(같은 `Get-FileHash` 오류 — CI 에선 PATH 에 `pwsh.exe` 가 있어 pwsh 로 돌기 때문에 통과 · `BuildManifestGuardGateTests.cs:65-71`) 🟢 |

⇒ **75건 전부가 이 한 원인으로 설명된다.** 9/30 부터 있던 시험이 CI 에서만 깨진 이유 = 이 브랜치가 CI 를 처음 탔기 때문(인계서 ⚠️가설 「처음부터 CI 환경에서 안 되던 것」 🟢 확인).

### ⚠️ 설명 못 한 것 1건 (판정은 안 바뀐다)
- pwsh 7 스크립트 안에서 `Remove-Item Env:PSModulePath` 뒤 `dotnet test` 를 불러도 G-PV1 은 **여전히 실패**했다(빌드 노드 재사용 끄고 2회). 같은 pwsh 에서 `cmd` 로 부르면 지워진 게 확인된다(M-5). dotnet/vstest 가 띄우는 testhost 까지 어떻게 전달되는지는 **미실측**. ⇒ **고칠 자리는 부모 셸이 아니라 powershell.exe 를 띄우는 바로 그 줄이어야 한다**(§3 A).

## §2 제품 영향 — 없다 (근거)

| # | 판정 | 근거 |
|---|---|---|
| P-1 | 🟢 | 제품은 일꾼을 **직접 안 띄운다** — `LocalSwapLauncher.cs:353-361` `schtasks /Create … /RU SYSTEM` → `/Run`. 일꾼 환경은 API 프로세스가 아니라 **작업 스케줄러가 SYSTEM 용으로 새로 만든다** ⚠️(Windows 표준 동작 · 이 세션에서 고객 PC 실측 0) |
| P-2 | 🟢 | 이 PC 머신 `PSModulePath` = `Program Files\WindowsPowerShell\Modules` · `system32\WindowsPowerShell\v1.0\Modules` · SQL Server 모듈 — **pwsh 7 경로 없음** |
| P-3 | 🟢 | CI 다른 잡 영향 없음 — Watchdog 167/167 · DDL Smoke · 마이그 번호 · 권한메뉴 · secrets 초록 |

## §3 고칠 자리 후보 (설계가 고른다 — 여기선 재료만)

| 안 | 자리 | 장 | 단 |
|---|---|---|---|
| **A** | 시험 장치 `LocalSwapWorkerRig.cs:104-113` 의 `ProcessStartInfo` 에서 `PSModulePath` 를 빼고 띄운다 | 제품 무접촉 · `.github` 무접촉 · **제품과 같은 조건**(작업 스케줄러도 깨끗한 환경) · 어느 셸에서 돌려도 같은 결과 | 시험 파일 1곳 |
| B | CI 시험 단계 셸을 바꾼다 | — | 🔴 `.github` = 설치·운영 워크플로 — 인계서 §9 「그때 여쭌다」 대상 · 다른 시험에도 번짐 |
| C | 일꾼 `local-swap.ps1` 의 `Get-FileHash` 4곳(`:606 :758 :766 :1125`)을 .NET 해시로 | 셸 무관 | 🔴 제품 코드 변경 · 착수 제약(이전 기능 무접촉)·[4] 재검증 범위 커짐 · 제품엔 결함이 없다(§2) |

🔴 설계가 알 것
1. A 를 고르면 **대조군**이 필요하다 — 고친 줄을 빼면 X1 조건에서 다시 `Get-FileHash` 실패가 나는지(M-4 X1 이 그 재료).
2. 같은 모양이 `BuildManifestGuardGateTests` 에도 있다(pwsh 가 PATH 에 없고 pwsh 경로를 물려받은 경우). CI 에선 안 깨진다 — 이번 범위에 넣을지는 설계 판단.
3. 시험 75건의 **내용(단언)은 무죄**다. 로컬 5.1 에서 2053/2053 이 이미 그 단언을 통과했다 — 고친 뒤 CI 에서 같은 75건이 초록이 되는지만 보면 된다.

## §4 곁에서 본 것 (이 오더 밖 · 기록만)
- **DB 게이트**가 문서만 바뀐 `94234dcc` 에서 빨강(앞 run 초록) — `BackupCredentialGateTests` 6건 `MySqlException: Too many connections`. 코드 diff 0 ⇒ CI DB 연결 수 한도 깜빡임 ⚠️(원인 미실측 · 9/22 「CI 재현적 초록 아님」 전례와 같은 모양). 75건 문제와 별개.
- CodeQL 은 이번 run 에서 아직 대기 — 인계서 §3 #2 그대로.

## §5 실측 재료 위치
- pwsh 7.4.6 압축본·로그 = 세션 임시 폴더(레포 밖 · 설치 0 · 레지스트리 0)
- CI 로그: `gh run view 36982051337 --log-failed`
