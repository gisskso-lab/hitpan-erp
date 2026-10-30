# 작업리뷰서 2026-10-30 — MySqlConnector 2.5.0 → 2.6.2 판 올림 · NU1902 억제 거둠

| 항목 | 내용 |
|---|---|
| 작업지시서 | [`docs/운영기록/20261030작_MySqlConnector판올림_작업지시서.md`](../../운영기록/20261030작_MySqlConnector판올림_작업지시서.md) (§0 · §2 · §3 · §5-1 · §6 · §8 · §10 · §PM) |
| 선행검증 | [`docs/검증/선행/20261030_선행검증_MySqlConnector_NU1902.md`](../선행/20261030_선행검증_MySqlConnector_NU1902.md) |
| 개발명세서 | [`docs/개발/erp/20261030작_MySqlConnector판올림_개발명세서.md`](../../개발/erp/20261030작_MySqlConnector판올림_개발명세서.md) |
| 대상 | 가지 `fix/20261030-MySqlConnector-판올림` · 바닥 `fa9b9687` · c1 `f2ee7f73` · c2 `c5e57046` · c3 `bfe2932c`(문서) |
| 구현자 | [3] 구현 갈래 (PM 지휘) |
| **검증자** | 검증팀장 데이비드 박 (SoD — 구현 참여 0 · [1-V] 작성자) |
| **판정** | 🟡 **조건부** — 로컬 측정 항목 전부 통과 · 반려 0 · 머지 전 필수 = CI 판독(DoD ④ · PM) + K-4·K-5 대표 결재 · 🆕 2.6.x 해석기 함정 1건(지금 노출 0 · 권고) |
| 실행 규칙 | 이 PC DB 시험 **0회** · 앱·시험 실행 0 · W 의 HEAD·작업 나무 무접촉 · c1·바닥 상태는 scratchpad 에 detach worktree 를 따로 만들어 쟀고 끝난 뒤 `git worktree remove` · 로그 = scratchpad `dp4-logs/`(레포 밖) |
| 도구 | SDK 10.0.204 · PowerShell(빌드 · Git Bash 는 `/p:` 를 경로로 바꾸므로 안 씀) · Git Bash(grep) · 정적 분석 스크립트 2개(파일 읽기만) |

## 1. DoD ①②③ 독립 재현

| # | 명령 | 결과 | 판정 |
|---|---|---|---|
| ① 판 갈림 | `git grep 'Include="MySqlConnector"'` (레포 전체 csproj·props·targets) + PowerShell `[xml]` 로 19개 csproj·props 를 읽어 **주석 밖 살아 있는** PackageReference 만 셈 | 살아 있는 줄 **8 · 전부 2.6.2** · 2.5.0 8줄은 전부 주석 안 · XML 해석 실패 0(주석 안 하이픈 두 개 연속 0 의 간접 증거) | 🟢 |
| ① 해석 결과 | 각 `obj/project.assets.json` 의 `MySqlConnector/x.y.z` | 9개 프로젝트(src 6 · tools 3 · `HitPan.Tests` 간접 포함) **전부 2.6.2** | 🟢 |
| ② 억제 | `[xml]` 로 살아 있는 `NoWarn`·`WarningsNotAsErrors`·`NuGetAudit*`·`TreatWarningsAsErrors` 요소 전수 | NU19xx 억제 **0**. 남은 것 = `HitPan.Web` NoWarn MUD0002·RZ10012(무접촉 대상) · TWAE=true 3곳(Watchdog · Watchdog.Tests · MdbMigrationBaselineRun) · 글자 grep 19줄(레포 전체)은 전부 주석 안 | 🟢 |
| ③ 대조군 restore | `dotnet restore src/HitPan.sln --force-evaluate` × 3 상태 | 바닥 `fa9b9687` **NU1902 3줄**(API · Backoffice.API · Infrastructure) → c1 **4줄**(+ **Application**) · 다른 NU19xx 0 → c2 **0** (NU1xxx · warning · error 줄 0) | 🟢 감사가 돌고 있고 Application 가림이 풀렸다가 판 올림으로 0 |
| ③ sln 빌드 | `dotnet build src/HitPan.sln -c Release /p:TreatWarningsAsErrors=true /warnaserror` (c2) | **0 Warning · 0 Error** (1분 39초) · NU1 줄 0 | 🟢 |
| ③ 밖 프로젝트 | 같은 옵션 · IntegrationTests · MdbMigrationBaselineRun · MigrationIdempotencyCheck | 각 **0/0** | 🟢 |
| ③ MigrationSmokeTest | 같은 옵션 | 오류 4 = **전부 CA1416**(`Program.cs` 23·32·37·45줄) · NU 0 | 별건(§2-1) |
| ③ `--vulnerable` | `dotnet list src/HitPan.sln package --vulnerable` | 13개 프로젝트 전부 「no vulnerable packages」 · MySqlConnector 0 | 🟢 |
| ③ `--include-transitive` | 같은 + `--include-transitive` (원천 api.nuget.org) | MySqlConnector **0** · 간접 High **4종 그대로**(SignalR.Common 8.0.11 · Caching.Memory 8.0.0 · Newtonsoft.Json 11.0.1 · System.Text.Json 8.0.0) ⇒ 취약점 자료가 살아 있는 상태의 0 | 🟢 (4종은 K-8 별건) |

- 증분 빌드 주의: c2 sln 빌드는 [3] 이 먼저 빌드한 W 위에서 돌았다. 꾸러미 판이 바뀌어 assets 가 갱신됐으므로 컴파일은 다시 돌았다고 보지만 `--no-incremental` 재측정은 안 했다(⚠️ · CI Build 잡이 깨끗한 나무로 다시 잰다).

## 2. [3] 이 말한 다른 점 4개

| # | [3] 주장 | 독립 확인 | 판정 |
|---|---|---|---|
| 2-1 | MigrationSmokeTest TWAE 시 CA1416 4건 · 바닥에서도 같다 | 바닥 `fa9b9687` 자기 설정 빌드 = **같은 4자리**(23·32·37·45) CA1416 + NU1902. 바닥 TWAE 빌드는 CA1416 까지 가지도 못하고 **Infrastructure NU1902 오류로 먼저 멈춤**. 이 차수 diff 에서 그 도구는 csproj 판 줄만 바뀜 | 🟢 별건 맞다. 덧붙임: 이 도구는 **판 올리기 전부터 TWAE 로는 깨져 있었다**(원인이 NU1902 → CA1416 으로 바뀌었을 뿐). csproj 에 TWAE 가 없어 자기 설정 빌드는 통과 · CI 미측정 |
| 2-2 | G1 78건 = 축자 문자열 머리 | 78줄 전부 `@"` 앞에 같은 줄 따옴표 **0**(= 코드 쪽 축자 문자열 시작) · 따옴표 매개변수 꼴 `@'x`·`` @`x `` **0** · C# 문자열 안 이스케이프 꼴(`@\"x` · `@""x`) **0** · .sql **0** | 🟢 지시서 「0」 은 매개변수 꼴 기준 숫자로 읽는 것이 맞다 |
| 2-3 | G3 `.sql` 92(지시서 91) | src **75**(전부 `Migrations/SQL`) + installer **17**(backoffice 16 + `hitpan_db_clean.sql`) = **92**. 바닥에서도 92 — 이 차수에서 `.sql` 변경 0 | 🟢 숫자 차이는 범위 차이(추정: 지시서는 installer/backoffice 16 만 셈 → 91). 어느 쪽이든 내용 무변경 |
| 2-4 | G5 3파일(지시서 2) | 3번째 `MdbReconciliationService.cs:1101` 은 **`OleDbCommand`(Access)** — MySqlConnector 아님 | 🟢 MySqlConnector 기준 2파일이 맞다. 나머지 2파일은 이름 중복 0 · `Parameters["@x"].Value =` 는 인덱서 **읽기 뒤 Value 대입**이라 #1632(인덱서 대입) 범위 밖 |

## 3. 2.6.0 해석 변화 × 우리 SQL — 독립 반증 (정적)

| G | 명령 | 숫자 | 판정 |
|---|---|---|---|
| G1 | §2-2 | 매개변수 꼴 0 | 무관 |
| G2 | `git grep -P -- '--\t'` .cs(src · 시험 제외) · .sql(src+installer · 레포 전체) | **0 · 0 · 0** | 무관 |
| G3 | `--\s*$` | .cs 28줄 = SQL 안 19 + C# 주석 9 · .sql 92파일 | 아래 R-1 |
| G4 | `Parameters\[[^]]+\]\s*=` | **0** | 무관 |
| G7 · G8 | 지시서 명령 | ⚠️ 이 리뷰에서 다시 안 돌림([3] 숫자 0 · 지시서 0 — 이 차수에서 .cs·.sql 변경 0 이라 바닥 숫자와 같다) | 기록 |

- **R-1 🆕 상류 해석기 원문 대조 — 「`--` + 줄바꿈(LF)」 은 다음 줄을 통째 주석으로 먹는다.** 상류 커밋 `00e8385f5`(#1624) 뒤 2.6.2 `SqlParser.cs` 는 `--` 다음 글자가 제어문자(0x00~0x20 · 0x7F)면 주석을 연다. 그런데 그 셋째 글자가 `\n` 이면 **그 `\n` 을 주석 시작 글자로 먹어서** 주석이 **다음 줄 끝 `\n`** 까지 이어진다. 2.5.0 은 셋째 글자가 공백일 때만 주석이었으므로 `--\n` 뒤 줄을 정상 해석했다. CRLF(`--\r\n`)는 `\r` 이 시작 글자가 되고 `\n` 에서 닫혀 안전하다.
  - 왜 중요한가: 먹힌 줄에 `@이름` 매개변수가 있으면 **치환되지 않고 그대로 서버로 간다** → 사용자 변수로 읽혀 NULL(조용한 오답). 레포 blob 은 `.cs` 가 LF(`git ls-files --eol` = `i/lf`) · `.gitattributes` 에 cs 규칙 없음 ⇒ **리눅스 체크아웃(CI · NCP 배포 빌드)은 LF**, 윈도우 개발PC 는 autocrlf 로 CRLF ⇒ **개발PC 에선 절대 안 보이고 CI·백오피스에서만 나는 꼴**.
  - 지금 노출: C# SQL 안 19줄 — **다음 줄이 전부 `-- ` 주석 줄**(CsRequestController 6 · EmployeeService 1 · PurchaseService 12, 하나씩 읽음) ⇒ 주석이 주석을 먹을 뿐 **매개변수 영향 0**.
  - .sql 146개(src·installer·db 전체) 를 2.5.0·2.6.2 상태 기계를 그대로 옮긴 정적 모사로 LF·CRLF 두 벌 비교: **매개변수 검출 차이 0 / 146 (LF·CRLF 모두)**. 문장 끝(`;`) 검출만 `installer/hitpan_db_clean.sql` 1파일에서 다름(LF 11곳 · CRLF 10곳 — 예: 1453줄 `DROP TABLE IF EXISTS print_common_settings;` 가 앞줄 `--` 에 먹힘).
- **R-2 매개변수 컬렉션(#1632)**: 인덱서 대입 0 · 직접 Add 는 2파일 이름 고유 · 나머지는 Dapper(이름당 1번) ⇒ 노출 0.
- **R-3 따옴표 매개변수(#1627·#1640)**: 매개변수 꼴 0 ⇒ 노출 0.
- 실행 증거는 **CI DB 게이트 · 백오피스 스키마 게이트(PM 판독)** 다. CI 는 우분투(LF)라 R-1 꼴이 있었다면 거기서 드러난다 — 이 축만큼은 CI 가 개발PC 보다 낫다. 단 게이트는 필터 목록만 돈다(지시서 §6-2 한계 그대로).

## 4. Pomelo 8.0.0 × 2.6.2 호환

| 근거 | 결과 |
|---|---|
| nuspec(로컬 NuGet 캐시) | Pomelo 8.0.0 의존 = `MySqlConnector` **2.3.5 이상(하한만)** · `EF Core Relational [8.0.0, 8.0.999]` ⇒ 2.6.2 허용 · 해석 결과 Pomelo 8.0.0 |
| 공개 API 비교 (2.5.0 ↔ 2.6.2 · lib/net48 · 반사 읽기 전용 로드) | 형식 67 → 69 · 공개 멤버 **삭제 0** · 추가 16(`SkipCertificateRevocationCheck` · `MySqlConnectorSemanticConventionsKinds` · `AddMySqlDataSource`/`AddKeyedMySqlDataSource` 가 `MySqlConnector` 이름공간으로 들어옴 · `WithSemanticConventionsKinds`) |
| 릴리스 노트 2.6.0~2.6.2 | 「breaking」 표시 0 · 이동 API 1(#1649 DI — Pomelo 미의존 · 우리 G8 0) · 동작 변화 = `EnableResultSetHeaderEvent` opt-in(우리 0) |
- ⇒ 바이너리 호환 근거 충분(삭제 0). 단 비교는 net48 판 · 우리가 쓰는 net8.0 판 표면은 ⚠️미비교(같은 원본에서 빌드되나 측정 아님). 실행 증거 = CI DB 게이트 EF 경로(§8-3 `MissingMethodException`·`TypeLoadException` 0 확인).

## 5. DB 게이트 비교 기준 런 (지시서 §6-2 「실행 수 = 직전 main 초록 런」)

| 후보 | 런 | SHA | 상태 | 쓰임 |
|---|---|---|---|---|
| **1순위** | **41004671856** | `fa9b9687`(= 이 가지 바닥) | DB 게이트 🟢 · 백오피스 스키마 게이트 🟢 · Build 🔴(NU1902) | 같은 나무 바로 위 — c2 런과 **DB 게이트·백오피스 게이트 실행 수**를 이것과 대조 |
| 2순위 | 40823856486 | `0b43a39e` (10/29) | 6잡 전부 🟢 | 「전 잡 초록」 이 꼭 필요하면 이것 |
- 실행 수 숫자(trx 개수 · `[SKIP]`)는 이 리뷰에서 안 셌다 — ⚠️미측정(PM 이 두 런 로그에서 직접 센다).

## 6. 지적

| 등급 | # | 내용 |
|---|---|---|
| 🔴 필수(머지 전) | M-1 | DoD ④ CI 판독: c1 런 restore NU1902 **4줄·Application 줄 실재** · c2 런 Build `NU190` 0 · `NU1900` 0 · `NU1605`/`NU1608` 0 · DB 게이트·백오피스 게이트 초록 + `[SKIP]` 0 + 실행 수 = 런 41004671856 · AppDbContext 쓰는 게이트 실행 이름 확인 · 런 수 ≤ 4 |
| 🔴 필수(머지 전) | M-2 | K-4·K-5 대표 결재(백오피스 운영 DB loopback 여부 · 자동 배포 런 승인/취소) — 지시서 §PM 그대로 · CI 는 127.0.0.1 이라 2.6.1 비번 전송 조건을 못 잰다 |
| 🔵 권고 | A-1 | R-1 함정을 기록으로 남길 것: 「SQL 안 줄 끝에 `--` 만 두지 않는다(뒤에 공백 하나라도) — 2.6.x 는 LF 에서 다음 줄을 먹는다」. 지금 노출 0 이나 새 SQL 이 `--` 줄 다음에 `@매개변수` 줄을 두면 **리눅스 빌드에서만 조용히 NULL**. 정적 검사 후보(별건 · 이 차수 밖) |
| 🔵 권고 | A-2 | `hitpan_db_clean.sql` 문장 끝 검출 차이(LF 11 · CRLF 10)가 닿는 C# 소비처(src 안 5곳 grep 적중)가 MySqlConnector 로 **나눠서/준비문으로** 보내는지 확인. 단일 비준비 명령이면 매개변수 차이 0 이라 무해로 본다(⚠️ 상류 코드상 문장 분할은 준비문 경로라는 이해 — 미대조) · DDL Smoke 는 mysql 클라이언트라 무관 |
| ⚪ 기록 | R-a | MigrationSmokeTest 는 바닥에서도 TWAE 로 깨져 있었다(NU1902 → 이제 CA1416). CI 미측정 · `.cs` 수정 필요라 이 차수 밖(별건) |
| ⚪ 기록 | R-b | 지시서 G3 91 · G5 2 · G1 0 은 범위·기준 차이로 설명됨(§2) — 숫자 오기 아님 |
| ⚪ 기록 | R-c | csproj 주석에 자격증명 모양 0 · 「NU1605」 등 하이픈 두 개 연속 0(XML 해석 성공) |

## 7. 미측정 (⚠️)
1. CI 런 전부(c1·c2 · DB 게이트 실행 수 · CodeQL·TruffleHog) — PM 판독.
2. c2 sln 빌드 `--no-incremental` 재측정.
3. G7·G8 이 리뷰 재실행(이 차수 .cs·.sql 변경 0 이라 바닥 숫자와 같다는 추론).
4. net8.0 판 공개 API 비교 · Pomelo 실행 호환(CI EF 경로).
5. `hitpan_db_clean.sql` C# 소비처 실행 경로(A-2) · R-1 정적 모사기는 상류 상태 기계를 옮긴 것이고 상류 원본으로 실행한 것이 아님.
6. 백오피스 운영 DB 위치(K-4) · tools 3곳 판 내림 NU1605 반사실(8곳 함께 올려 발생 0 — 5곳만 올린 경우는 안 쟀다).
