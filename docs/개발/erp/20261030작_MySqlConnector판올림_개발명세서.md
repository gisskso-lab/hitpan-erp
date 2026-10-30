# 개발명세서 2026-10-30 — MySqlConnector 2.5.0 → 2.6.2 판 올림 · 꺼 둔 NU1902 경고 되살림

| 항목 | 내용 |
|---|---|
| 근거 작업지시서 | [`docs/운영기록/20261030작_MySqlConnector판올림_작업지시서.md`](../../운영기록/20261030작_MySqlConnector판올림_작업지시서.md) (§3 · §5-1 · §6-1 · §8 · §10 · §PM) |
| PM 결재 | ✅ 2026-10-30 브라운킴 (K-1 = 2.6.2 · K-2 = 8곳 · K-3 = CI · 커밋은 로컬까지) |
| 개발팀 지휘 | PM 브라운킴 |
| 구현 담당 | [3] 구현 담당(hp-implementer 1갈래) |
| 기준 나무 | origin/main `fa9b9687` · 가지 `fix/20261030-MySqlConnector-판올림` |
| 커밋 | c1 `f2ee7f73` (§3-2 대조군) · c2 `c5e57046` (§3-1 판 올림) · c3 = 이 문서(문서만) · 🔴 push 0 (PM 이 한다) |
| 로그 | scratchpad `my3-logs/` (c1-restore · c2-restore · c2-build-* · c2-vuln* · base-build-smoke-plain) — 레포 밖 |

## 1. 무엇을 만들었나 (작업지시서 대비)

| 요구 | 구현 | 상태 |
|---|---|---|
| §3-2 `Application.csproj:47` NoWarn NU1902 거둠 · 종전 줄 주석 보존 · 사유 한 덩어리 | `src/HitPan.Application/HitPan.Application.csproj:54~59`(c2 뒤 줄 번호) · MailKit 사유 주석(`:48~53`) 무접촉 | ✅ c1 |
| §3-1 판 줄 8곳 2.6.2 · 각 자리 위 주석(차수명·결재·종전 줄·「8곳 같아야 한다」) · 정확한 판 하나 | 아래 §2 표 8파일 | ✅ c2 |
| §3-3 XML 주석 안 하이픈 두 개 연속 0 | 8파일 주석 전수 검사 결과 0 (§4) | ✅ |
| §3-3 c1·c2 분리 · `-F` 메시지 · 파일 이름으로만 add | c1 = csproj 1 + 작업지시서 1 · c2 = csproj 8 · 직후 `git log -1 --format="%s"` 확인 | ✅ |
| §6-1 ⓒⓓ 로컬 양성 대조·restore | §4 | ✅ |
| §5-1 G1~G4·G7·G8 (+G5·G9) [3] 재실행 | §4-2 | ✅ |

## 2. 변경 파일 목록

| 파일 | 변경 | 커밋 |
|---|---|---|
| `src/HitPan.Application/HitPan.Application.csproj` | NoWarn 줄 → 주석(+7/−1) · 판 줄(+8/−1) | c1 · c2 |
| `src/HitPan.API/HitPan.API.csproj:33~40` | 판 줄 +8/−1 | c2 |
| `src/HitPan.Backoffice.API/HitPan.Backoffice.API.csproj:21~28` | 판 줄 +8/−1 | c2 |
| `src/HitPan.Infrastructure/HitPan.Infrastructure.csproj:24~31` | 판 줄 +8/−1 | c2 |
| `src/HitPan.IntegrationTests/HitPan.IntegrationTests.csproj:14~21` | 판 줄 +8/−1 | c2 |
| `tools/MdbMigrationBaselineRun/MdbMigrationBaselineRun.csproj:30~37` | 판 줄 +8/−1 | c2 |
| `tools/MigrationIdempotencyCheck/MigrationIdempotencyCheck.csproj:24~31` | 판 줄 +8/−1 | c2 |
| `tools/MigrationSmokeTest/MigrationSmokeTest.csproj:18~25` | 판 줄 +8/−1 | c2 |
| `docs/운영기록/20261030작_MySqlConnector판올림_작업지시서.md` | 신규(설계팀장 작성분 그대로) | c1 |
| `docs/개발/erp/20261030작_MySqlConnector판올림_개발명세서.md` · `docs/개발/erp/INDEX.md` 한 줄 | 신규 · +1줄 | c3 |

- 줄 끝: 8파일 모두 원래 CRLF · 변경 뒤 CRLF 만(LF 단독 0) · BOM 있던 2파일(Application·Infrastructure) BOM 유지. `git diff --numstat` 파일당 +8/−1 (통째 바뀜 0).
- 덮어쓰기(#1): 종전 판 줄·종전 NoWarn 줄 모두 주석 안에 원문 그대로 남김. — ✅
- DDL·`.cs`·워크플로·`appsettings*`(#21) 변경 0 — ✅ 해당없음

## 3. 헌법 준수

| 조항 | 어떻게 지켰나 | 근거 |
|---|---|---|
| #1 | 종전 줄 주석 보존 | §2 |
| #19 | sln Release TWAE+warnaserror 0/0 (§4) | `my3-logs/c2-build-sln.log` |
| #21 | appsettings 무접촉 | c1·c2 파일 목록 |
| #39 · 지시 | 이 PC DB 시험·앱 실행·`dotnet test` 0 — restore·build·list package 만 | — |
| 억제 금지(§7) | 새 NoWarn·WarningsNotAsErrors·NuGetAudit* 0 (DoD ② grep 결과가 전부 주석 안) | §4 |
| 자격증명 모양 | 주석·커밋 메시지·이 문서에 0 | — |

## 4. 자체 확인 결과 (실측 · SDK 10.0.204 · W)

### 4-1 빌드·감사

| 항목 | 명령 | 결과 |
|---|---|---|
| c1 restore (§6-1 ⓓ 앞) | `dotnet restore src/HitPan.sln` force-evaluate | 종료 0 · **NU1902 4줄**(API · Application · Backoffice.API · Infrastructure) · 다른 NU19xx 0 — 기대 4 와 같음. Application 가림이 풀린 것이 로컬로 보임 |
| c2 restore (§6-1 ⓓ) | 같은 명령 | 종료 0 · **NU19xx 0 · NU1605/NU1608 0** · warning/error 줄 0 |
| sln 빌드 | `dotnet build src/HitPan.sln -c Release /p:TreatWarningsAsErrors=true /warnaserror` (PowerShell 로 실행 · Git Bash 는 `/p:` 를 경로로 바꿔 무효) | **0 Warning · 0 Error** (1분 45초) |
| IntegrationTests 빌드만 | 같은 옵션 | **0/0** |
| tools MdbMigrationBaselineRun 빌드만 | 같은 옵션 | **0/0** |
| tools MigrationIdempotencyCheck 빌드만 | 같은 옵션 | **0/0** |
| tools MigrationSmokeTest 빌드만 | 같은 옵션 | ⚠️ **오류 4 — 전부 CA1416**(`Program.cs:37` 등 · `MdbMigrationService.PreviewAsync` windows 전용 호출) · NU 0 |
| ↳ 같은 도구, 자기 설정대로(TWAE 없음 · 이 csproj 는 TWAE 미설정) | `dotnet build … -c Release` | 0 Error · 경고 4(CA1416) · NU 0 |
| ↳ 대조: 기준 나무 `fa9b9687`(판 올리기 전) | 같은 명령 | 0 Error · 경고 = CA1416 4 + NU1902 — **CA1416 은 판 올리기 전부터 있던 것**, 이 차수 무관 |
| c2 로그 전체 NU1xxx | `c2-*.log` grep `NU1\d\d\d` | **0** — tools 판 내림(NU1605) 예측(§2-2)은 8곳 함께 올려 발생 0 |
| §6-1 ⓒ 직접 | `dotnet list src/HitPan.sln package --vulnerable` | 취약 꾸러미 0 (13개 프로젝트 전부 「no vulnerable packages」) |
| §6-1 ⓒ 간접 | `… --vulnerable --include-transitive` | MySqlConnector **0** · 간접 High **4종 그대로**: Microsoft.AspNetCore.SignalR.Common 8.0.11 · Microsoft.Extensions.Caching.Memory 8.0.0 · Newtonsoft.Json 11.0.1 · System.Text.Json 8.0.0 (1-V ⑤ 와 같은 4종 · 판 올림으로 바뀐 것 없음) ⇒ 취약점 자료가 살아 있는 상태의 0 |

### 4-2 §5-1 grep 재실행 (c2 뒤 · `src` · `HitPan.Tests`·`HitPan.IntegrationTests`·bin·obj 제외)

| G | 결과 | 판정 |
|---|---|---|
| G1 | 지시서 명령 그대로면 .cs **78** — 전부 C# 축자 문자열 머리 `@"SELECT`·`@"INSERT`·`@"UPDATE` 등(문자 뒤 1글자 분포 C13·D1·G1·I17·S24·U21·s1). 따옴표 매개변수 꼴(`@'이름'`·`` @`이름` ``) **0** · .sql(`src`+`installer`) **0** · C# 안 이스케이프 꼴(`@\"이름`·`@""이름`) **0** | 무관 (지시서 「0」 은 축자 문자열 머리를 뺀 숫자로 읽힘 — [4] 확인 요청) |
| G2 | 하이픈2+탭 (.cs · .sql · `src`+`installer`) **0** | 무관 |
| G3 | .cs 28줄 = **SQL 안 19줄**(CsRequestController 6 · EmployeeService 1 · PurchaseService 12 · 전부 줄에 하이픈 두 개만, 뒤 글자 0) + C# 주석 구분선 9줄(`// ----`) · .sql(`src`+`installer`) **92파일** | 지시서 19줄과 같음. .sql 은 지시서 91 대비 +1 — 범위(`installer` 포함 여부) 차이 추정 · ⚠️미대조 |
| G4 | **0** | 무관 |
| G5 | **13줄 · 3파일** — `MdbMigrationService.cs` · `SchemaMigrator.cs` · 🆕`MdbReconciliationService.cs` | 지시서 「2파일」 대비 1파일 더 — 이름 중복 여부 읽지 않음 · [4] 확인 요청 |
| G7 | 모든 .sql `binary(32\|36)` **0** | 무관 |
| G8 | **0** | 무관 |
| G9 | `RETURNING` 0 · `PrepareAsync` 1 · `DateTime.MinValue` 2 — `CsOutboxSenderWorker.cs:38`(메모리 타이머 `_lastPullUtc` · 비교·대입 3곳뿐) · `WatchdogUpdateCoreAdapter.cs:340`(`UpdateManifest` 객체 → `DownloadAsync`) — **둘 다 SQL 매개변수로 안 감** | 무관 |

### 4-3 DoD ①② (§10)

- ① `grep -rn 'Include="MySqlConnector"' --include=*.csproj src tools` — 판 줄(들여쓰기 4칸, 주석 밖) **8줄 전부 2.6.2** · 2.5.0 8줄은 전부 주석 안(종전 줄 보존).
- ② `grep -rn "NU1902\|NuGetAudit\|WarningsNotAsErrors" --include=*.csproj --include=*.props src tools` — 12줄 전부 주석 안(Application `:50` 은 MailKit 옛 사유 주석) · 레포 루트 `Directory.Build.props` 에도 NU19·NuGetAudit·WarningsNotAsErrors·NoWarn **0**.

## 5. 안 한 것 / 못 한 것

| 항목 | 왜 | 후속 |
|---|---|---|
| push · 초안 PR · CI 런(§6-1 ⓐⓑ · DoD ④) | §PM 「[3] 은 로컬 커밋까지만」 | PM 이 c1 → 런 판독 → c2 → 런 판독 (런 상한 4) |
| `MigrationSmokeTest` 의 TWAE 0/0 | CA1416 4건이 판 올리기 전부터 있음 · 고치려면 `.cs` 수정 = §7 금지·§8-3 멈춤 자리 · CI 는 tools 를 안 잼 | 별건 후보로 기록만 (PM 판단) |
| `dotnet test`·앱 실행·DB 접속 | 이 PC 로컬 3306 = 실물 — 지시로 금지 | DB 게이트·백오피스 스키마 게이트 = CI |
| §5-2 백오피스 운영 DB 위치(K-4) | [3] 범위 밖 · #29·#39 대표 결재 사항 | 머지 상신 때 |
| Pomelo 8.0.0 × 2.6.2 실행 호환 | 컴파일만 확인(실행 0) | CI DB 게이트 EF 경로 |
| G5 신규 1파일(`MdbReconciliationService.cs`) 이름 중복 여부 · G3 .sql 91↔92 차이 원인 | 숫자만 다시 셌고 읽지 않음 | [4] |
| 1-V ③④ 상태 갱신 · 작업리뷰서 | [4]·PM 몫 | — |
| 간접 High 4종 | §7 범위 밖(K-8 기록만) | 별 차수 [1-V] |

## 6. [3-V] 병렬검증 지적 사항

| # | 지적 | 봉합 | 이슈 문서 |
|---|---|---|---|
| — | 이 문서 작성 시점까지 받은 지적 없음 | — | — |

## 7. 검증팀에 요청

- 특히 봐줬으면 하는 곳: ① c1 CI 런에서 restore NU1902 **4줄 · Application 줄 실재**(로컬은 4줄) ② c2 CI 런 Build 잡 `NU190` 0 · `NU1900` 0 · `NU1605`/`NU1608` 0 ③ DB 게이트·백오피스 스키마 게이트 실행 수가 직전 main 초록 런과 같은지(줄어든 초록 금지) ④ §4-2 G1 「78」 해석 · G5 3파일째.
- 회귀 우려: 2.6.0 SQL 해석 강화(§5-1)가 정적 grep 밖 동적 SQL 에서 걸릴 가능성 · 2.6.1 비번 전송 조건(§5-2)은 CI(127.0.0.1)가 못 잼 · tools 3곳은 CI 미측정(로컬 빌드만).
