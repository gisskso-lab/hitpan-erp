# #729 MySqlConnector 2.5.0 → 2.6.2 — HQA 1/3 요청 (a)·(b) 정적 회신

> 작성 = [4] 검증팀장 데이비드 박(정적 검토) · 공개 = PM 브라운킴 10/30 · 대상 = HQA #729 [4-X] 1/3 회신([원문](20261030_외부지적서_MySqlConnector_1차_GPT원문.md)) 요청 (a)·(b) · (c) 는 설계 안만(실행 = 대표 결재 사안)
> 스캐너·원자료 = 같은 폴더 [`20261030_x729scan/`](20261030_x729scan/) (실행 = `py -3 -I <스캐너> <6b15caf9 체크아웃 폴더> …`) · scan.json(원자료 109KB)은 싣지 않음 — 스캐너로 다시 만든다


- 고정 SHA: `6b15caf92ea124ddc8e06d664560c83ba2484d59` (작업 폴더 HEAD 일치 확인)
- 방법: 읽기만 함. `git ls-tree`/`git show <SHA>:경로` 로 저장소 원문(blob)을 읽었고, `git grep`, 그리고 파이썬 정적 렉서(아래 §3-2)를 썼다. **DB 접속 0 · 앱/빌드 실행 0 · 네트워크 0 · 코드 변경 0**
- 상태: 🟡 **부분 완료** — 턴 한도로 멈췄다. 끝내지 못한 칸은 ⚠️미측정·미완료로 적었다.

## ⓪ 보고서 머리 (검사 범위 · 중단 상태)

> **실행 증거 없음 — 정적 검토만.** 드라이버·DB·앱을 한 번도 실행하지 않았다. 중단된 검사는 완료나 통과로 세지 않는다.

**① 고정 SHA** — `6b15caf92ea124ddc8e06d664560c83ba2484d59`. `git rev-parse HEAD` 로 일치를 확인했고, 모든 원문은 `git show <SHA>:<경로>` 로 읽었다.

**② 검사 완료 목록 (대상 수 대비)**

| 검사 | 완료 / 전체 |
|---|---|
| `src/HitPan.API/Migrations/SQL/DB-*.sql` 꼴별 집계 + 세 모형 × LF·CRLF 렉서 대조 | **113 / 113** |
| `installer/backoffice/*.sql` 같은 검사 | **16 / 16** |
| 「주석을 여는 `--` 가 줄 끝」 다음 줄 분류 (위 두 묶음) | **586곳 / 586곳** (129 / 129 파일) |
| C# SQL 「`--` 줄끝」 (시험 프로젝트 제외 `src`·`tools` 의 `.cs` 전체를 grep) | **19 / 19곳**, 다음 줄 확인 |
| C# SQL 표본 — 리터럴 + 매개변수 혼재 | 16곳. 표본이며 전수가 아님 |
| C# SQL 꼴 grep (리터럴 안 `@` · `SET @`/`:=` · `@@` · `ESCAPE` · `/*` · `#` · `` @` ``) | 7꼴 / 7꼴 (위와 같은 범위) |
| MySqlConnector 직접 참조 csproj | 8 / 8 (전체 csproj 18) |
| `installer/hitpan_db_clean.sql` 소비자 grep | 저장소 전체(문서 제외) 1회. 판독 6곳 중 1곳(`build-manifest.ps1:120`)은 미판독 |
| `DELIMITER` 지시문이 든 `.sql` | 저장소 전체 → 2파일 |
| 줄끝 원문 (`git ls-files --eol`) | 113 + 16 + 1 = **130 / 130** |

**③ 미검사 목록 (완료로 세지 않음)**

1. C# SQL 안의 `?` 사용 — **0 / 전수 미착수**
2. 백오피스 컨트롤러·서비스 SQL 리터럴 표본 — **0 / 약 38 연결 지점**
3. ERP 별도 연결 7곳(`OutboxPollerWorker:81` · `IdempotencyCleanupService:36` · `IntegrityCheckService:49` · `MainPcRegistrationService:129` · `AuditLogMiddleware:43` · `CompanyBootstrapProvisioner:60·212·607`)의 연결 문자열 출처 — **0 / 7**
4. `tools/IssueTestDomain` 직접 SQL 여부 · `installer/updates/build-manifest.ps1:120` 용도
5. 상류 MySqlConnector 2.5.0/2.6.2 해석기 원문 재판독 · DLL 실행 (모형 규칙의 검증)
6. 윈도우 게시 산출물 SQL 의 실제 줄끝 (산출물 바이트)
7. CI 런에서 G-W13-5 결과 줄 재판독
8. 백오피스 운영 연결 문자열의 `AllowUserVariables` (저장소 밖 · 운영 접근 금지 사안)
9. `91_seed_admin.sql:29` 치환값의 글자 구성

**④ 중단 지점과 재개 방법**

- **중단 지점:** §3 C# SQL 표본 단계. 「리터럴 안 `@`」 grep 다음 순서인 **C# SQL 안 `?` 판별**에서 멈췄다. 그 뒤 미검사 2 → 3 → 4 순서로 이어 간다.
- **재개 명령** (작업 폴더 = 위 SHA 의 worktree):
  - **미검사 1 (`?`):** `git grep -n -P '"[^"]*\?[^"]*"|^\s+[^/"]*\?(?![?.\[])' -- 'src/*.cs' 'tools/*.cs' ':!src/HitPan.Tests' ':!src/HitPan.IntegrationTests'`
    - 결과에서 C# 삼항·null 연산자(`??`·`?.`·`? :`)를 손으로 거르고, SQL 문자열 안만 남긴다.
    - 시작 파일은 `src/HitPan.Application/Services/` 사전순이다.
  - **미검사 2:** `git grep -n 'new MySqlConnection(cs)' -- src/HitPan.Backoffice.API` 로 나온 파일마다 SQL 상수를 열어 리터럴 안 `@`·`?`·`\` 와 `--` 줄끝을 본다. 시작 파일은 `Controllers/BackofficeAuthController.cs` 다.
  - **미검사 3:** 각 줄 위의 `connStr` 대입을 거꾸로 따라간다(`git grep -n 'connStr\s*=' -- <파일>`).
  - **SQL 렉서 재실행 (원자료 재생성):**
    - `py -3 -I docs/검증/외부/20261030_x729scan/scan_sql.py <worktree> 6b15caf9… docs/검증/외부/20261030_x729scan/scan.json`
    - `py -3 -I docs/검증/외부/20261030_x729scan/summ.py scan.json summ.txt`
    - `py -3 -I docs/검증/외부/20261030_x729scan/eol_dash.py <worktree> 6b15caf9… eol.txt src/HitPan.API/Migrations/SQL installer/backoffice`
- **이번에 쓴 주요 명령:**
  - `git ls-files --eol`
  - `git grep -n -P -- '--\s*$'` (C# SQL 줄끝 `--`)
  - `git grep -n 'new MySqlConnection(\|UseMySql(\|MySqlBulkCopy'`
  - `git grep -n 'hitpan_db_clean'` · `git grep -n 'hitpan_db\.sql'`
  - `git grep -l -i -E '^\s*DELIMITER\s' -- '*.sql'`
  - `git grep -n -i 'AllowUserVariables'`
  - 위 파이썬 스캐너 3개

**⑤ 실행 증거 없음 — 정적 검토만.**

## 0. 요약

1. **MySqlConnector 를 거치는 `.sql` 파일 묶음은 둘이다.** ERP `src/HitPan.API/Migrations/SQL/DB-*.sql`(113개)는 `MigrationRunner` 가 보내고, 백오피스 `installer/backoffice/*.sql`(16개)는 `SchemaMigrator` 가 보낸다. `installer/hitpan_db_clean.sql` 이 MySqlConnector 로 가는 경로는 **찾은 범위에서 0** 이다. 이 파일은 CLI 입력 리다이렉션과 파일 복사로만 쓰인다.
2. **예외가 하나 있다. `DELIMITER` 가 든 마이그 `DB-16` 은 MySqlConnector 로 간다.** 경로는 `MigrationRunner` 다. 다만 판 올림과는 무관한 기존 사실이다. 신규 설치에서는 이미 적용된 것으로 시드되고, CI 게이트도 「MySqlConnector 거부」로 고정해 둔 목록에 넣어 두었다(§1-3).
3. **(b) 결과 — 정적 검사상 현재 영향 발견 0건 · DB 실행 증거 없음.**
   - 129개 파일(부록 A 전수 목록) × LF·CRLF 두 줄끝에 세 가지 정적 해석 모형(서버 근사 / 2.5 규칙 / 2.6 규칙)을 돌렸다.
   - 코드 영역에서 본 `?`·`@이름`·`DELIMITER`·`;` 의 **위치 차이는 0** 이다. 실행 결과는 미측정이다.
   - 이것은 모형 비교 결과일 뿐, 드라이버 판 사이의 해석이 같다는 실행 입증이 아니다.
4. **2.6.x 의 새 꼴 「`--` 바로 뒤 LF → 다음 줄까지 주석」 은 605곳에 있다.** SQL 파일에 586곳, C# SQL 에 19곳이다(탐지 조건은 §2-2).
   - **현재 영향:** 정적 검사로는 0 이다. 605곳 모두 다음 줄이 주석 줄이기 때문이다.
   - **향후 위험:** 이 꼴을 막는 장치가 없다. 두 사실은 따로 본다(§5 한계 ①·②).
5. **LF/CRLF 에는 공백이 있다.** CI 의 실행 게이트(우분투)는 LF 로 돈다. 반면 고객에게 나가는 ERP 산출물은 윈도우 러너에서 빌드되므로 CRLF 일 가능성이 크다(⚠️ 산출물 바이트는 미측정). 실행 대조군은 (c) 로 남는다.

## 1. (a) MySqlConnector 를 실제로 거치는 SQL 과 실행기

### 1-1. 실행기 표

| 종류 | 실행기 (파일:줄) | 보내는 SQL | 매개변수 | 해석 영향 연결 옵션 (파일:줄) |
|---|---|---|---|---|
| ERP 스키마 마이그 | `src/HitPan.Application/Services/MigrationRunner.cs:64` 이 파일을 찾고 · `:113` UTF-8 로 읽고 · `:131` 체크섬을 내고 · `:135~137` Dapper `ExecuteAsync` 로 **파일 통째 1회** 보냄 | `DB-*.sql` 113개 (폴더 위치 `:111~120`) | **0개** (`CommandDefinition` 에 매개변수 없음) | `src/HitPan.Infrastructure/Persistence/MigrationDbConnectionFactory.cs:57~59` `AllowUserVariables=true` · `AllowLoadLocalInfile=true` · `DefaultCommandTimeout=600` |
| ERP 마이그 — 도구 | `tools/MigrationIdempotencyCheck/Program.cs:49~51` · `tools/MdbMigrationBaselineRun/Program.cs:70~74` (같은 러너·같은 팩토리) | 같음 (`MdbMigrationBaselineRun.csproj:44~45` 가 링크로 복사) | 0 | 같음. 별도 연결 `tools/MdbMigrationBaselineRun/Program.cs:103` `AllowUserVariables=true` |
| ERP 마이그 — CI 게이트 | `src/HitPan.Tests/Integrity/ErpMigrationApplyGateTests.cs:600~603` 실물 러너 · G-W13-5 `:305~392` 가 **전 파일을 한 건씩 두 바퀴** 돌림 | 저장소 원문 `DB-*.sql` | 0 | `:745~756` `AllowUserVariables=true` (러너와 같은 성질) |
| ERP Dapper/ADO (업무 SQL) | `src/HitPan.Infrastructure/Extensions/InfrastructureExtensions.cs:65` `IDbConnection` → 서비스 계층 원문 SQL | C# 문자열 | `@이름` (Dapper) | `InfrastructureExtensions.cs:55` `AllowUserVariables=true` 포함 |
| ERP 별도 연결 | `src/HitPan.API/BackgroundServices/CsOutboxSenderWorker.cs:386~387` · `src/HitPan.API/Controllers/WebhookInboundController.cs:268` | C# 문자열 | `@이름` | 각 줄에 `AllowUserVariables=true` |
| ERP 별도 연결 (옵션 미판독) | `OutboxPollerWorker.cs:81` · `IdempotencyCleanupService.cs:36` · `IntegrityCheckService.cs:49` · `MainPcRegistrationService.cs:129` · `AuditLogMiddleware.cs:43` · `CompanyBootstrapProvisioner.cs:60·212·607` | C# 문자열 | `@이름` | ⚠️미측정 — 연결 문자열을 어디서 받는지 안 따라감 |
| ERP 대량 적재 | `src/HitPan.Application/Services/MdbMigrationService.cs:2790` 외 `MySqlBulkCopy` 11곳 | LOAD DATA (드라이버가 생성) | — | `MigrationDbConnectionFactory.cs:58` |
| EF Core (Pomelo 8.0.0) | `InfrastructureExtensions.cs:61` `UseMySql` · `src/HitPan.Infrastructure/Persistence/AppDbContextFactory.cs:25` | EF 생성 SQL | EF 매개변수 | Pomelo 가 쓰는 MySqlConnector = 직접 참조 2.6.2 (`HitPan.Infrastructure.csproj:31~32`). `TagWith` 0 · `FromSqlRaw/Interpolated` 0 · `ExecuteSqlRaw/Interpolated` 0 · EF `Database.Migrate` 0 (검색 결과는 전부 `MdbMigrationService.MigrateAsync`) |
| 백오피스 스키마 | `src/HitPan.Backoffice.API/Services/SchemaMigrator.cs:58` 연결 · `:108` 읽기 · `:109` 해시 · MariaDB 면 `:126~130` **파일 통째 1개 `MySqlCommand`**, MySQL 이면 `:210~225` 자체 분리기(`SplitStatements`)로 **문장별 `MySqlCommand`** | `installer/backoffice/*.sql` 16개 (`HitPan.Backoffice.API.csproj:45~48` 로 산출물에 복사) | 0 (기록 INSERT `:138~145` 만 `@F`·`@H`) | `src/HitPan.Backoffice.API/Program.cs:163` 설정 `BackofficeDb`. 저장소 기본값 `appsettings.json:13` 에는 `AllowUserVariables=true` 가 있다. **운영 값은 저장소에 없음 → ⚠️미측정** |
| 백오피스 업무 SQL | 컨트롤러·서비스 `new MySqlConnection(cs)` 약 38곳 (예: `BackofficeAuthController.cs:153` · `TenantsAdminController.cs:356` · `WebhookDispatcher.cs:68`) | C# 문자열 | `@이름` | 운영 연결 값 ⚠️미측정 |
| 도구 (마이그 아님) | `tools/MigrationSmokeTest/Program.cs:14·25` | `MdbMigrationService` 를 거치는 SQL 만 (`.sql` 파일 0) | `@이름` | `:14` `AllowUserVariables=true` |
| 도구 | `tools/IssueTestDomain` | `Backoffice.API` 프로젝트 참조만 있음 | — | ⚠️미측정 — 직접 SQL 이 있는지 안 읽음 |

- **CI 실행 여부.** `build-and-test.yml` 에서 위 도구 3개를 부르는 곳은 0 이다. CI 의 실행 증거는 `db-gate` 잡의 G-W13 게이트다.

### 1-2. `installer/hitpan_db_clean.sql` 의 소비자 — MySqlConnector 경로 0 (찾은 범위)

| 소비자 | 방식 |
|---|---|
| `.github/workflows/build-and-test.yml:354` | `mysql` CLI 에 `< installer/hitpan_db_clean.sql` 로 넘김 |
| `scripts/ddl-smoke-test.sh:23` | CLI 적재 |
| `.github/workflows/build-installer.yml:159` · `deploy-update.yml:504` | 설치 묶음 `hitpan_db.sql` 로 **파일 복사만** |
| `installer/HitPan-Universal.iss:2306` · `installer/HitPan.iss:263` | 설치 때 `"!MYSQL!" ... < 파일` — **mysql.exe 표준입력** (`HitPan-Universal.iss:286~291` 이 mysql.exe 경로를 찾음) |
| `installer/updates/build-manifest.ps1:120` | 경로만 참조. 용도는 ⚠️미판독 |
| `src/HitPan.Watchdog.Tests/UpdateConsentRuleCTests.cs:966·983` | 글자 비교로 읽기만 함 |

- C# 안의 `DELIMITER` 0 · `MySqlScript` 0. 워치독(`src/HitPan.Watchdog`)은 MySqlConnector 를 참조하지 않는다(csproj 의 PackageReference 판독).
- `DELIMITER` 지시문이 든 `.sql` 은 **레포에 2개** 뿐이다. `installer/hitpan_db_clean.sql`(CLI 경로)과 **`DB-16_phase5_certificates_etax_audit.sql:135·147`** 이다.

### 1-3. ⚠️ `DB-16` — `DELIMITER` 가 MySqlConnector 로 가는 유일한 경로 (판 올림과 무관한 기존 사실)

| 구분 | `DB-16_phase5_certificates_etax_audit.sql` (`DELIMITER` `:135·147`) | `installer/hitpan_db_clean.sql` (신규 설치 DDL · `DELIMITER` 6개) |
|---|---|---|
| 산출물에 실림 | `src/HitPan.API/HitPan.API.csproj:95` 가 `Migrations/SQL/DB-*.sql` 을 출력 폴더로 복사 | `build-installer.yml:159` · `deploy-update.yml:504` 가 설치 묶음 `hitpan_db.sql` 로 복사 |
| 찾기·읽기 | `MigrationRunner.cs:55`(`ResolveMigrationsDirectory` `:111~120`) → `:64` `DB-*.sql` 나열 → `:104` 적용 기록 있으면 건너뜀 → `:113` `File.ReadAllTextAsync` | 설치기 배치 `HitPan-Universal.iss:2306` · `HitPan.iss:263` |
| 실행기 | `MigrationRunner.cs:135~137` Dapper `ExecuteAsync` → **MySqlConnector** (연결 `MigrationDbConnectionFactory.cs:65`) | **mysql.exe 표준입력** (`< 파일`) · CI 는 `build-and-test.yml:354` · `scripts/ddl-smoke-test.sh:23` 도 CLI |
| 실제로 보내지는 조건 | `schema_migrations` 에 `DB-16` 성공 기록이 없을 때. 신규 설치는 `hitpan_db_clean.sql:3655~3658` 부근 시드 `('DB-16','clean-ddl',1)` 로 건너뜀. 호출자는 API 기동 시 러너(호출 줄은 ⚠️ 미판독) · 도구 2개 · CI 게이트 | 설치 때 기존 운영 데이터 0 일 때만 (`HitPan-Universal.iss:2306` 분기) |
| 2.6.2 와의 관계 | 드라이버 경로이므로 대상. 게이트(`ErpMigrationApplyGateTests.cs:527`)에는 「거부」 로 고정 | 드라이버를 거치지 않음 → 대상 아님 |

- 신규 설치는 출하 DDL 시드(`installer/hitpan_db_clean.sql:3655~3658` 부근 `('DB-16','clean-ddl',1)`)로 「이미 적용」 처리된다.
- CI 게이트 `ErpMigrationApplyGateTests.cs:527` 는 「MySqlConnector 가 DELIMITER 를 거부」 하는 것으로 `KnownNotReapplyable` 에 고정해 두었다. `:378~391` 이 목록이 **늘어도 빨강 · 줄어도 빨강** 으로 양쪽을 다 물기 때문에, 2.6.2 에서 거동이 바뀌었다면 이 게이트가 드러냈어야 한다. 해당 런의 이 시험 결과 줄은 ⚠️ 이번에 재판독하지 않았다.

## 2. (b) `DB-*.sql` 전수 · 백오피스 `.sql` 전수 — 꼴별 표

대상은 ERP `DB-*.sql` **113개 · 7,568줄**과 백오피스 `.sql` **16개 · 1,712줄**이다. 저장소 blob 의 CR 은 **0 / 129** 로, 전부 LF 다.

| 꼴 | ERP DB-*.sql | 백오피스 | 비고 · 대표 파일:줄 |
|---|---|---|---|
| 줄 끝 `--` (원문 글자) | 555줄 / 75파일 | 69줄 / 16파일 | 대부분 `-- -----…--` 처럼 이미 열린 주석 안에 있다 |
| **주석을 여는 첫 `--` 가 줄 끝**(= 2.6 이 LF 에서 다음 줄을 먹는 꼴) | **586곳 / 129파일 합계** | (합계에 포함) | **다음 줄 586/586 이 주석 줄** · 코드 줄 0 · 빈 줄 0 (탐지·판별 규칙 §2-2) |
| `--` + 탭 | 0 | 0 | |
| 코드 안 `--` + 공백 아닌 글자 | 0 | 0 | |
| `#` (원문 글자) | 164줄 / 66파일 | 48줄 / 15파일 | 렉서 판정상 전부 주석 영역. 세 모형이 같다 |
| `/*` | 4줄 — `DB-129:34` · `DB-39:13` · `DB-60:11` · `DB-84:11` | 1줄 — `31_…tenant_fk.sql:13` | **5줄 모두 `-- ` 주석 안의 경로 글자**(`installer/backoffice/*.sql` 등) |
| 버전 조건 주석 `/*!` | 0 | 0 | |
| `?` (원문 글자) | 8줄 / 7파일 — `DB-105:85` · `DB-116:13` · `DB-126:6` · `DB-127:49·135` · `DB-131:27` · `DB-86:20` · `DB-89:5` | 1줄 — `20_backoffice_telemetry.sql:7` | **9줄 전부 `--` 주석 안.** 코드 영역 `?` = **0** |
| `@이름` (원문 줄) | 138줄 / 22파일 | 3줄 — `31_…:70` · `45_…:33` · `91_seed_admin.sql:29` | 백오피스 3줄은 주석 또는 문자열(이메일) 안. 코드 영역 사용자 변수 토큰은 ERP **168개 / 19파일**, 백오피스 **0** |
| 따옴표 붙은 `@'x'`·`` @`x` ``·`@"x"` | 0 | 0 | 2.6 「인용된 매개변수 끝」 수정의 직접 대상 꼴이 없다 |
| `@@` 시스템 변수 | 0 | 0 | |
| `PREPARE/EXECUTE/DEALLOCATE` | 72줄 / 18파일 (예: `DB-147:48` · `DB-148:55·70·100·115·130·145` · `DB-149:40`) | 0 | 동적 SQL 관용구 |
| 문자열 안 `''` 겹따옴표 | 37줄 / 15파일 (예: `DB-88:54~55` · `DB-148:53`) | 0 | 동적 SQL 문자열 안의 COMMENT 리터럴 |
| 문자열 안 `\'` | 0 | 0 | |
| `DELIMITER` | 1파일 2줄 (`DB-16:135·147`) | 0 | §1-3 |
| 문장 수 (코드 `;`) | 539 · 2문장 이상 파일 70 | 73 · 2문장 이상 파일 12 | ERP 는 파일 통째로 보냄. 백오피스는 MariaDB 면 통째, MySQL 이면 자체 분리 |
| 치환 자리표시 | — | `91_seed_admin.sql:29` `${…}` (`SchemaMigrator.TrySubstituteEnv`) | 치환값의 글자 구성은 ⚠️미측정 |

### 2-1. 위험 판정 (2.6.x 변화별) — 판정은 모두 「정적 검사 결과」 이며 DB 실행 증거가 없다

| 2.6.x 변화 | 걸릴 수 있는 꼴 | 우리 SQL 의 노출 | 판정 |
|---|---|---|---|
| 주석 시작 문자 처리 (`--` 뒤 공백 아닌 제어문자도 주석) | `--` + 탭 · `--` + 줄바꿈 | 탭 0. 줄바꿈은 586곳(SQL) + 19곳(C#)이지만 다음 줄이 **전부 주석** | 정적 검사상 현재 영향 발견 0건 · DB 실행 증거 없음. 별도로 **향후 변경을 막는 장치 없음** |
| 인용된 매개변수 끝 | `@'x'`·`` @`x` `` | 0 | 해당 꼴 0건 (정적) |
| 매개변수 재작성 (이름 검사 강화) | 매개변수 컬렉션 직접 조작 · 같은 이름 중복 | `.sql` 경로는 매개변수가 0 (러너 `:135`, 백오피스 `:126`) | `.sql` 경로는 해당 없음 (정적). C# 업무 SQL 은 표본만 봄 — 미완료 |
| 매개변수 0 인데 클라이언트가 `?` 를 코드로 보는 경우 | 코드 영역 `?` | ERP·백오피스 모두 **0** (세 모형·두 줄끝 모두) | 정적 모형 3종 비교에서 위치 차이 0 · 실행 미측정 |
| `@이름` 을 사용자 변수로 통과시킴 | `AllowUserVariables` 필요 | ERP 168 토큰 → `MigrationDbConnectionFactory.cs:58` 에 있음. 백오피스 0 | 설정 있음 (정적). 실제 통과 거동은 실행 미측정 |

### 2-2. 605곳 탐지 조건 · 「다음 줄이 주석」 판별 근거

- **기준:** 고정 SHA `6b15caf9…` 의 blob 원문을 `git show <SHA>:<경로>` 로 읽었다. CRLF 는 LF 로 정규화한 뒤 판정했다.
- **`.sql` 586곳** — 스캐너 `docs/검증/외부/20261030_x729scan/eol_dash.py`. 인자는 `<worktree> 6b15caf92ea124ddc8e06d664560c83ba2484d59 eol.txt src/HitPan.API/Migrations/SQL installer/backoffice` 이고, 대상은 `Migrations/SQL/DB-*.sql` + `installer/backoffice/*.sql` = 129파일이다.
  - **탐지 규칙 (원문 요약):**
    1. 각 줄을 왼쪽부터 훑는다. 따옴표(`'` `"` `` ` ``) 안은 건너뛴다(`\` 이스케이프 처리 · 백틱 제외).
    2. 따옴표 밖에서 `#` 이 먼저 나오면 그 줄은 해당 없음이다.
    3. 따옴표 밖 **첫 `--`** 를 찾는다. 그 뒤 글자가 줄끝이거나 코드값 ≤ 0x20 이면 「주석을 여는 `--`」 다.
    4. 그 `--` 가 **줄의 마지막 두 글자**이면 1곳으로 센다.
  - 즉 `-- 설명 ----` 처럼 이미 `-- ` 로 열린 줄 끝의 대시는 세지 않는다. 그것이 원문 글자 555+69=624줄과 586곳의 차이다.
  - **다음 줄 판별 규칙:** 다음 줄을 앞뒤 공백 제거한 뒤 분류한다.
    - `--` 또는 `#` 로 시작 → 「주석」
    - 빈 문자열 → 「빈 줄」
    - 파일 끝 → 「EOF」
    - 그 밖 → 「코드」
  - 결과: `{주석: 586}` · 코드 0 · 빈 줄 0 · EOF 0.
  - **예시:** `DB-147_form_templates_tear_line.sql:7` `--` → `:8` `-- 🔴 왜 필요한가` · `DB-147…:13` → `:14` · `DB-88_tenant_devices_auth_key.sql:10` → `:11` `-- 무엇을 바꾸나` · `DB-88…:13` → `:14` · `44_backoffice_pricing_plan_columns.sql:4` → `:5` · `00_backoffice_core.sql:5` → `:6` `-- 헌법 정합:`
- **C# 19곳** — 명령은 `git grep -n -P -- '--\s*$' -- 'src/*.cs' 'tools/*.cs' ':!src/HitPan.Tests' ':!src/HitPan.IntegrationTests'` 이다.
  - 결과에서 C# 한 줄 주석(`^\s*//`)을 `grep -v -E ':[0-9]+:\s*//'` 로 뺐다. 남은 줄은 모두 SQL 문자열 안에서 **줄 전체가 `--`** 인 줄이다.
  - **다음 줄 판별:** `sed -n "$((n+1))p"` 로 한 줄씩 읽어 앞 공백 제거 후 `--` 로 시작하는지 눈으로 확인했다(19/19). 줄 목록은 §3-1 에 있다.
  - **예시:** `PurchaseService.cs:615` → `:616` `-- 🔴🔴 20260827작6 …` · `EmployeeService.cs:84` → `:85` `--   🔴 \`is_deleted=0\` …` · `CsRequestController.cs:360` → `:361` `-- 사고: …`
  - **한계:** 문자열을 `"\n"` 이스케이프로 이어 붙인 경우는 별도 grep 으로 봤다(`--\\n` 0건 · `--"` 0건). 문자열 보간·런타임 조립 SQL 은 범위 밖이다.

- **근거 수준.** 위 판정은 정적 렉서 모형으로 낸 것이다. 2.5/2.6 규칙은 공식 변경 이력과 상류 해석기 원문을 바탕으로 옮긴 모형이며, **이번 회신 작성 중에 상류 원문을 다시 읽거나 DLL 을 실행해 확인하지는 않았다(⚠️)**.

## 3. C# 안 SQL 문자열 표본 (위험 꼴 위주)

### 3-1. `--` + 줄끝 (2.6 이 다음 줄을 먹는 꼴) — 전수 19곳 (시험 프로젝트 제외 · C# `//` 주석 제외)

- `src/HitPan.API/Controllers/CsRequestController.cs:360·365·370·385·388·393`
- `src/HitPan.Application/Services/EmployeeService.cs:84`
- `src/HitPan.Application/Services/PurchaseService.cs:536·591·595·600·615·620·626·652·656·889·895·899`

19곳 모두 **다음 줄이 `--` 주석 줄**이어서, 먹혀도 주석이 주석을 먹을 뿐이다. 먹히는 줄 안에 `@매개변수` 는 0 이다. 예를 들어 `PurchaseService.cs:621` 은 `'canceled'` 따옴표가 든 주석 줄인데, 세 모형 모두 주석으로 본다. 백오피스 API 에서는 이 꼴이 0 이다.

### 3-2. 매개변수와 문자열 리터럴이 같은 문장에 있는 표본 (16곳)

`CsOutboxSenderWorker.cs:139·348` · `ApprovalService.cs:791·835·933·958` · `ApprovalTriggerHelper.cs:174` · `BomService.cs:882·955` · `PurchaseService.cs:1361·1546` · `ItemService.cs:65~66` · `PartnerService.cs:203~204` · `QuotationService.cs:63` · `ReportService.cs:62·267~268` · `LeaveBalanceHelper.cs:63`

리터럴 안에 `@`·`?`·`\` 가 든 것은 0 이다(`'%'`, `'(Reverse IN)'` 같은 괄호·한글만 있음). 리터럴 경계가 단순하다.

### 3-3. 그 밖의 꼴

| 꼴 | 건수 |
|---|---|
| 리터럴 안에 붙은 `@` (`'a@b'` 꼴) | **0** |
| `SET @`·`:=` 사용자 변수 | **0** |
| `@@` | 0 |
| `ESCAPE '…'` | 0 |
| SQL 안 `/*` | 0 (6건은 전부 C# `/* */` 주석) |
| SQL 안 `#` | 0 |
| `` @` `` | 0 |

- `src/HitPan.Application/Services/LegacyUnpostedDocumentService.cs:48~66` 은 raw 문자열에 `"\n"` 을 이어 붙인다. 이 이어 붙임은 **체크아웃과 무관하게 LF** 다. 조각 끝 `--"` 0건 · `--\n` 이스케이프 0건으로 노출은 0 이다.
- ⚠️미측정: C# SQL 안의 `?` 사용(C# 삼항·null 연산자와 섞여 grep 으로 가르지 못함) · 백오피스 컨트롤러 SQL 의 리터럴 표본.

## 4. LF/CRLF

| 항목 | 사실 (파일:줄) |
|---|---|
| 저장소 원문 | `git ls-files --eol` 결과 `DB-*.sql` 113/113 · 백오피스 16/16 · 출하 DDL 모두 `i/lf` |
| `.gitattributes` | `*.sh text eol=lf` 만 있다(`.gitattributes:13~` 부근). `.sql`·`.cs` 규칙은 **없다** ⇒ 체크아웃 줄끝은 각 기계의 `core.autocrlf` 를 따른다 |
| 이 개발 PC | system `core.autocrlf=true` ⇒ 작업 폴더 `w/crlf` |
| 산출물 실림 | ERP `src/HitPan.API/HitPan.API.csproj:95` `None Update … CopyToOutputDirectory`, 백오피스 `HitPan.Backoffice.API.csproj:45~48` `Content … Copy`. **임베디드 리소스 0 · 바이트 그대로 복사** ⇒ 체크아웃 줄끝이 실행 SQL 에 그대로 간다. 러너는 `File.ReadAllTextAsync` 로 읽으며 줄끝 정규화 0 (`MigrationRunner.cs:113` · `SchemaMigrator.cs:108`) |
| ERP 게시 빌드 | `deploy-update.yml:122` · `build-installer.yml:49` 가 `windows-latest` 다. 워크플로 안 `autocrlf` 설정은 0 ⇒ **러너 기본값을 따름 → CRLF 가능성 높음. ⚠️ 산출물 바이트 미측정** |
| CI 실행 게이트 | `build-and-test.yml:290` `db-gate` 가 `ubuntu-latest` ⇒ **LF 로만 실행**. 윈도우 빌드 잡 `:246~` 은 SQL 파일 집합과 비어 있지 않음만 확인하고 실행하지는 않는다 |
| 백오피스 배포 | `deploy-ncp.yml:44·86` `ubuntu-latest` ⇒ LF |
| 줄끝에 따라 바뀌는 다른 값 (참고 · 판 올림과 무관) | `MigrationRunner.cs:131` 체크섬이 줄끝 포함 원문으로 계산된다(비교 용도는 ⚠️ 미판독). `SchemaMigrator.cs:109~115` 는 해시가 다르면 **재적용**한다 ⇒ 빌드 기계의 줄끝이 바뀌면 백오피스 `.sql` 전부가 재적용 경로를 탈 수 있다 |

- **결론.** 2.6 의 `--`+줄끝 꼴은 **LF 에서만** 다음 줄을 먹는다. CRLF 에서는 `\r` 이 시작 글자가 되고 `\n` 에서 닫힌다.
  - ERP `.sql` 은 고객에게 CRLF 일 공산이 크다. 그런데 CI 는 LF(더 위험한 쪽)로 돌았다.
  - 백오피스는 배포와 CI 가 둘 다 LF 다.
  - 정적 모형 3종 비교에서는 두 줄끝 모두 위치 차이가 0 이다(실행 미측정). **두 판 × 두 줄끝 실행 대조는 없다.**

## 5. 한계 (⚠️미측정 · 미완료)

① **605곳 — 현재 영향:** 정적 검사상 0건이다(다음 줄이 전부 주석). DB 실행 증거는 없다.

② **605곳 — 향후 변경을 막는 장치 없음:** 누가 주석 줄 대신 코드 줄을 `--` 다음에 두면 2.6.x · LF 경로에서 그 줄이 클라이언트 해석상 주석이 된다. 예를 들어 `@매개변수` 가 치환되지 않는다. 이 꼴을 잡는 게이트·분석기는 저장소에서 찾지 못했다(찾은 범위 기준 · 전수 탐색은 아님).

나머지 한계:

1. 2.5.0/2.6.2 해석 규칙은 모형이다. 상류 원문 재판독과 DLL 실행을 하지 않았다(이 PC NuGet 캐시에 두 판 DLL 이 있으나 읽기만 함).
2. 렉서는 서버 규칙의 근사다. 동적 SQL(`PREPARE` 문자열)의 **2차 해석**은 서버만 하므로 드라이버 무관으로 보았다.
3. 백오피스 운영 연결 문자열의 `AllowUserVariables` 는 저장소에 없다.
4. ERP 별도 연결 7곳의 연결 문자열 출처를 따라가지 않았다.
5. C# SQL 의 `?` 와 백오피스 컨트롤러 SQL 리터럴은 표본조차 미완료다.
6. `tools/IssueTestDomain` · `build-manifest.ps1:120` 의 용도를 판독하지 않았다.
7. 윈도우 게시 산출물의 실제 줄끝은 미측정이다.
8. G-W13-5 의 해당 CI 런 결과 줄은 재판독하지 않았다.


## 6. (c) 다음 단계 — 두 안 비교 (어느 쪽도 이번에 실행하지 않음 · 선택은 대표 결재)

> **㉠ 정적 검증과 ㉡ 실행 대조는 서로 대체하지 않는다.** ㉠ 을 다 끝내도 **「실행 호환성 = 미측정」** 으로 남는다. ㉡ 만 해도 정적으로 본 꼴 목록(어디가 위험한가)은 생기지 않는다.

### 6-1. 비교표

| 항목 | ㉠ 남은 정적 검증 마치기 | ㉡ 격리 CI 실행 대조 (2.5.0 ↔ 2.6.2 × LF ↔ CRLF) |
|---|---|---|
| 대상 | §⓪-③ 미검사 1~4 · 6 · 7. 8 은 운영 접근 금지라 제외 | §6-2 의 「같은 SQL 집합」 |
| 방법 | 저장소 읽기만 한다. C# `?` 와 백오피스 SQL 리터럴은 grep 한 뒤 사람이 판독한다. 연결 출처 7곳을 역추적한다. 게시 산출물 줄끝은 기존 게시 zip 의 바이트를 확인한다(운영 무접촉 · 내려받기는 결재 필요) | **0단계(DB 없음):** 두 판 DLL 에 SQL 집합 × LF·CRLF 를 넣고, 서버로 보낼 최종 바이트를 비교한다. **1단계:** 수동 실행 워크플로 1개를 새로 두고(기존 잡 무접촉) 행렬 4칸을 돈다. 설계 고정 항목은 §6-2 |
| 예상 시간·런 수 | **추정** — 사람 판독 반나절 안팎 · CI 런 0 | **추정** — 0단계 수 분 · 런 1회. 1단계 행렬 1런(잡 4개 = `db-gate` 1회분 × 4)이고, 백오피스 MySQL 8 칸을 더하면 잡 +2. 재시도 포함 런 1~2회. 잡당 시간은 미추정 |
| 끝낸 뒤 남는 한계 | **실행 호환성 = 미측정 그대로.** 해석기 규칙은 모형이다. 「매개변수 0 + 코드 영역 `?`」 의 실제 거동과 리터럴 변질 여부는 판정할 수 없다 | 실행 환경은 CI 의 MariaDB 11.4 · 우분투 하나다. 고객 PC(윈도우 · 실제 설치 판)와 운영 백오피스(MySQL 8 운영 설정 · 운영 연결 옵션)는 대표하지 못한다. 업무 SQL 은 기존 시험이 부르는 문장만 덮는다. 0단계는 공개 API 로 바이트를 뽑을 수 있는지가 미확인이다 |
| 선행 조건 | 없음 | 대표 결재 · 워크플로 추가 PR(작업지시서 경로) · CI 런 한도 확인 |

### 6-2. ㉡ 설계 — 판·줄끝 말고도 4칸 모두 같게 고정할 것

| 고정 항목 | 내용 |
|---|---|
| 바꾸는 축 (2개만) | MySqlConnector **2.5.0 / 2.6.2** (8개 csproj 같은 판 · 해석 결과 `project.assets.json` 으로 확인) × SQL 줄끝 **LF / CRLF** (체크아웃 뒤 변환 · 변환 뒤 CR 수 확인) |
| 같은 SQL 집합 | 고정 SHA `6b15caf9…` 의 `src/HitPan.API/Migrations/SQL/DB-*.sql` **113개**(목록 = `git ls-tree` 결과를 런 산출물로 남김) + `installer/backoffice/*.sql` **16개** + 음성 대조군 SQL 1개. 4칸 모두 같은 SHA · 같은 목록 해시 |
| 같은 실행기 | ERP 는 `MigrationRunner.ApplyPendingAsync` 를 G-W13-5 루프(`ErpMigrationApplyGateTests.cs:305~392`)로 — 한 건씩 기록을 지우고 두 바퀴. 연결 성질 = `MigrationDbConnectionFactory.cs:57~59` 와 같음(`AllowUserVariables=true`). 백오피스는 `SchemaMigrator.ApplyAsync`(`:49`). 업무 SQL 은 기존 DB 게이트 시험 목록을 그대로 쓴다 |
| 같은 DB 초기 상태 | MariaDB **11.4** 서비스 컨테이너(같은 이미지 다이제스트 · 같은 `sql_mode`·문자셋). 칸마다 **빈 DB 를 새로 만들고** `installer/hitpan_db_clean.sql`(같은 SHA)을 CLI 로 적재한 뒤, 전건을 「적용됨」 으로 앉힌다(G-W13-5 선행 절차). 백오피스는 빈 DB 에서 시작한다. MySQL 8 칸도 이미지 판을 고정한다 |
| PASS (모두 같아야) | 4칸이 다음 넷 모두 같다: ① 실패 마이그 집합(= `KnownNotReapplyable` 10건 · 사유 첫 줄 종류) ② 적용 건수 ③ 스키마 모양(표·칸 형·널 + **COLUMN_DEFAULT · COLUMN_COMMENT** · 인덱스) ④ 백오피스 적용 파일 수와 `_schema_migrations` 행. 0단계는 129파일 × 2줄끝에서 2.5.0 과 2.6.2 가 보낼 바이트가 같아야 한다 |
| FAIL (하나라도) | 어느 칸에서 실패 집합이 늘거나 줄었다 · 오류 사유가 바뀌었다(예: 1064 문법 · 「Parameter … must be defined」 · 위치 매개변수 오류) · 적용 건수가 다르다 · 모양 한 줄이라도 다르다 · 0단계 바이트가 1바이트라도 다르다. 칸 사이 차이는 원인 파일:줄을 적기 전까지 FAIL 로 남긴다 |
| 음성 대조군 (장치 생존 확인) | `--`+줄끝 바로 다음 줄에 `@P` 매개변수를 둔 문장 1개를 매개변수를 넣어 실행한다. **2.6.2 × LF 칸에서만** 결과가 달라야 한다(매개변수 미치환 → 다른 값 또는 오류). 나머지 3칸은 같아야 한다. 4칸이 모두 같게 나오면 **장치가 죽은 것**이고, 그 런 전체를 무효(미측정)로 본다. 대조군은 제품 SQL 집합 밖의 시험 전용 파일로 둔다 |
| 대조군 ① DB-16 그대로 | `DB-16_phase5_certificates_etax_audit.sql`(같은 SHA · 무수정)을 4칸 모두에서 G-W13-5 와 같은 방식으로 한 건만 실행한다. **기대: 4칸 모두 같은 거부**(같은 예외 종류 · 같은 메시지 첫 줄 · 적용 0). 칸마다 거부 여부나 사유가 다르면 FAIL — 판 또는 줄끝이 `DELIMITER` 판정에 영향을 준다는 뜻이다 |
| 대조군 ② 줄 끝 `--` 다음 줄에 실제 SQL (합성) | 시험 전용 문장, 예: `SELECT @P AS a --` ⏎ `, @P AS b;`. 매개변수 `P=7` 로 실행한다. 서버 기준 정답은 `a=7, b=7`. **기대: 2.6.2 × LF 칸에서만 `b` 가 다르다**(NULL 등 · 두 번째 `@P` 미치환). 나머지 3칸은 `7, 7`. 이렇게 나와야 장치가 살아 있다. 4칸이 모두 같으면 장치가 죽은 것이고 런 전체를 무효(미측정)로 본다. `.sql` 경로용 변형(매개변수 0 · `--` 다음 줄에 `?` 를 둔 문장)도 함께 두어 2.6.2·LF 칸 거동을 기록한다 |
| 산출물 | 칸별 결과표(JSON) · SQL 목록 해시 · 드라이버 판 · CR 수 · DB 이미지 다이제스트. 대조는 표 4장을 기계로 비교한다 |

## 부록 A. 정적 검사 대상 129파일 전수 목록 (고정 SHA `6b15caf92ea124ddc8e06d664560c83ba2484d59`)

- **목록 생성 명령:** `git ls-tree -r --name-only <SHA> -- src/HitPan.API/Migrations/SQL | grep '/DB-[^/]*\.sql$'` + `git ls-tree -r --name-only <SHA> -- installer/backoffice | grep '\.sql$'`
- **셈:** ERP `DB-*.sql` **113** + 백오피스 `.sql` **16** = **129**. 원자료 파일은 `docs/검증/외부/20261030_x729scan/files_129.txt`(129줄)이다.
- **스캐너와 인자** (모두 `py -3 -I` · 읽기 전용 · DB 접속 0):
  - `scan_sql.py <worktree> 6b15caf92ea124ddc8e06d664560c83ba2484d59 scan.json` — 꼴별 집계와 세 모형 × LF·CRLF 렉서. 대상 목록은 스크립트 안 `SETS` 로 위 두 경로를 고정했다
  - `summ.py scan.json summ.txt` — 집계·차이 출력
  - `eol_dash.py <worktree> 6b15caf9… eol.txt src/HitPan.API/Migrations/SQL installer/backoffice` — 605곳 중 586곳 탐지·다음 줄 분류

```
src/HitPan.API/Migrations/SQL/DB-02_3tier_schema.sql
src/HitPan.API/Migrations/SQL/DB-03_test_accounts.sql
src/HitPan.API/Migrations/SQL/DB-04_week1_day1_security_performance.sql
src/HitPan.API/Migrations/SQL/DB-05_mariadb114_generated_columns.sql
src/HitPan.API/Migrations/SQL/DB-06_week1_day2_indexes_softdelete.sql
src/HitPan.API/Migrations/SQL/DB-07_fix_column_comments_utf8.sql
src/HitPan.API/Migrations/SQL/DB-08_week1_day3_users_tenants_master.sql
src/HitPan.API/Migrations/SQL/DB-08b_week1_day3_tenants_company_columns.sql
src/HitPan.API/Migrations/SQL/DB-09_tenants_rebuild_utf8_comments.sql
src/HitPan.API/Migrations/SQL/DB-100_payroll.sql
src/HitPan.API/Migrations/SQL/DB-101_chat.sql
src/HitPan.API/Migrations/SQL/DB-102_absence_type_default.sql
src/HitPan.API/Migrations/SQL/DB-103_tenant_devices_hardware_id.sql
src/HitPan.API/Migrations/SQL/DB-104_device_slot_policy_settings.sql
src/HitPan.API/Migrations/SQL/DB-105_fax_settings_history_partner_geo.sql
src/HitPan.API/Migrations/SQL/DB-106_resignation_letters.sql
src/HitPan.API/Migrations/SQL/DB-107_stock_alert_ghost_cleanup.sql
src/HitPan.API/Migrations/SQL/DB-108_sales_return_items_is_loss.sql
src/HitPan.API/Migrations/SQL/DB-109_purchase_returns_created_by.sql
src/HitPan.API/Migrations/SQL/DB-10_safety_force_edit_logs.sql
src/HitPan.API/Migrations/SQL/DB-110_warehouse_address.sql
src/HitPan.API/Migrations/SQL/DB-111_chart_of_accounts_expand.sql
src/HitPan.API/Migrations/SQL/DB-112_chart_of_accounts_local_tenant_fix.sql
src/HitPan.API/Migrations/SQL/DB-113_sales_chain_dup_guard.sql
src/HitPan.API/Migrations/SQL/DB-114_sales_orders_quotation_link.sql
src/HitPan.API/Migrations/SQL/DB-115_sales_return_journal_source_type.sql
src/HitPan.API/Migrations/SQL/DB-116_sales_return_chain_backfill.sql
src/HitPan.API/Migrations/SQL/DB-117_stock_ledger_warehouse_unique.sql
src/HitPan.API/Migrations/SQL/DB-118_payments_source.sql
src/HitPan.API/Migrations/SQL/DB-119_hr_reports_source.sql
src/HitPan.API/Migrations/SQL/DB-11_week2_day1_partners_master.sql
src/HitPan.API/Migrations/SQL/DB-120_main_pc_single_row_guard.sql
src/HitPan.API/Migrations/SQL/DB-123_legacy_unposted_documents.sql
src/HitPan.API/Migrations/SQL/DB-126_tenant_devices_mainpc_sealed_key.sql
src/HitPan.API/Migrations/SQL/DB-127_user_sessions_device_kind.sql
src/HitPan.API/Migrations/SQL/DB-128_tenant_settings_single_pc_default_off.sql
src/HitPan.API/Migrations/SQL/DB-129_tenant_settings_session_validity.sql
src/HitPan.API/Migrations/SQL/DB-12_week2_day2_items_master.sql
src/HitPan.API/Migrations/SQL/DB-130_refresh_tokens_session_id.sql
src/HitPan.API/Migrations/SQL/DB-131_tenant_settings_one_pc_login.sql
src/HitPan.API/Migrations/SQL/DB-135_local_update_attempts.sql
src/HitPan.API/Migrations/SQL/DB-136_account_seat_defaults.sql
src/HitPan.API/Migrations/SQL/DB-137_employees_login_id.sql
src/HitPan.API/Migrations/SQL/DB-138_ai_export_consents.sql
src/HitPan.API/Migrations/SQL/DB-139_cs_requests.sql
src/HitPan.API/Migrations/SQL/DB-13_week2_day3_bom_auto_order.sql
src/HitPan.API/Migrations/SQL/DB-140_cs_outbox.sql
src/HitPan.API/Migrations/SQL/DB-141_cs_replies.sql
src/HitPan.API/Migrations/SQL/DB-142_cs_forbidden_rejects.sql
src/HitPan.API/Migrations/SQL/DB-143_cs_rating_outbox.sql
src/HitPan.API/Migrations/SQL/DB-144_cs_requests_origin.sql
src/HitPan.API/Migrations/SQL/DB-145_cs_requests_hq_status.sql
src/HitPan.API/Migrations/SQL/DB-146_cs_requests_purpose.sql
src/HitPan.API/Migrations/SQL/DB-147_form_templates_tear_line.sql
src/HitPan.API/Migrations/SQL/DB-148_form_watermark_print_background.sql
src/HitPan.API/Migrations/SQL/DB-149_show_watermark_three_values.sql
src/HitPan.API/Migrations/SQL/DB-14_week2_day4_permissions_company_enhance.sql
src/HitPan.API/Migrations/SQL/DB-15_phase4_approval_collection.sql
src/HitPan.API/Migrations/SQL/DB-16_phase5_certificates_etax_audit.sql
src/HitPan.API/Migrations/SQL/DB-18_idempotency_and_summary_sources.sql
src/HitPan.API/Migrations/SQL/DB-19_tax_invoices_layer1.sql
src/HitPan.API/Migrations/SQL/DB-20_sales_deliveries_tax_invoice_id.sql
src/HitPan.API/Migrations/SQL/DB-21_items_auto_receive_on_order.sql
src/HitPan.API/Migrations/SQL/DB-22_positions_and_approval_lines.sql
src/HitPan.API/Migrations/SQL/DB-23_subscription_billing.sql
src/HitPan.API/Migrations/SQL/DB-24_backup_settings_history.sql
src/HitPan.API/Migrations/SQL/DB-25_bills_cards_bank.sql
src/HitPan.API/Migrations/SQL/DB-26_email_smtp_history.sql
src/HitPan.API/Migrations/SQL/DB-27_tenant_ai_subscription_columns.sql
src/HitPan.API/Migrations/SQL/DB-28_tenant_settings_missing_columns.sql
src/HitPan.API/Migrations/SQL/DB-29_approval_lines_schema_fix.sql
src/HitPan.API/Migrations/SQL/DB-30_approval_doc_lines.sql
src/HitPan.API/Migrations/SQL/DB-31_cleanup_orphan_draft_receipts.sql
src/HitPan.API/Migrations/SQL/DB-32_seed_chart_of_accounts.sql
src/HitPan.API/Migrations/SQL/DB-33_sales_orders_enum_expand.sql
src/HitPan.API/Migrations/SQL/DB-34_sales_orders_is_auto.sql
src/HitPan.API/Migrations/SQL/DB-35_journal_lines_unique.sql
src/HitPan.API/Migrations/SQL/DB-36_purchase_orders_is_auto.sql
src/HitPan.API/Migrations/SQL/DB-37_stock_ledger_unique.sql
src/HitPan.API/Migrations/SQL/DB-38_force_innodb.sql
src/HitPan.API/Migrations/SQL/DB-39_backoffice_schema.sql
src/HitPan.API/Migrations/SQL/DB-50_unified_calendar.sql
src/HitPan.API/Migrations/SQL/DB-60_backoffice_v1.sql
src/HitPan.API/Migrations/SQL/DB-70_watchdog_pings.sql
src/HitPan.API/Migrations/SQL/DB-71_licenses_token_hash.sql
src/HitPan.API/Migrations/SQL/DB-72_terms_consents.sql
src/HitPan.API/Migrations/SQL/DB-73_item_specs.sql
src/HitPan.API/Migrations/SQL/DB-74_form_templates.sql
src/HitPan.API/Migrations/SQL/DB-75_purchase_returns_reason.sql
src/HitPan.API/Migrations/SQL/DB-76_item_specs_seed.sql
src/HitPan.API/Migrations/SQL/DB-77_tenant_employees_snapshot.sql
src/HitPan.API/Migrations/SQL/DB-78_tenant_devices_snapshot.sql
src/HitPan.API/Migrations/SQL/DB-79_sync_tokens.sql
src/HitPan.API/Migrations/SQL/DB-80_reseller_applications.sql
src/HitPan.API/Migrations/SQL/DB-81_leave_requests.sql
src/HitPan.API/Migrations/SQL/DB-82_watchdog_update_consents.sql
src/HitPan.API/Migrations/SQL/DB-83_local_update_status.sql
src/HitPan.API/Migrations/SQL/DB-84_schema_migrations.sql
src/HitPan.API/Migrations/SQL/DB-85_local_company_address_detail_and_images.sql
src/HitPan.API/Migrations/SQL/DB-86_tenant_devices_is_main_pc.sql
src/HitPan.API/Migrations/SQL/DB-87_device_register_tokens.sql
src/HitPan.API/Migrations/SQL/DB-88_tenant_devices_auth_key.sql
src/HitPan.API/Migrations/SQL/DB-89_main_pc_db_guard.sql
src/HitPan.API/Migrations/SQL/DB-90_form_templates_print_options.sql
src/HitPan.API/Migrations/SQL/DB-91_ai_provider_3way.sql
src/HitPan.API/Migrations/SQL/DB-92_hr_reports.sql
src/HitPan.API/Migrations/SQL/DB-93_seed_positions_all_tenants.sql
src/HitPan.API/Migrations/SQL/DB-94_employee_weekly_hours.sql
src/HitPan.API/Migrations/SQL/DB-95_workplace_labor_settings.sql
src/HitPan.API/Migrations/SQL/DB-96_labor_policy_settings.sql
src/HitPan.API/Migrations/SQL/DB-97_annual_leave_grants.sql
src/HitPan.API/Migrations/SQL/DB-98_employee_leave_of_absence.sql
src/HitPan.API/Migrations/SQL/DB-99_employee_work_status.sql
installer/backoffice/00_backoffice_core.sql
installer/backoffice/10_backoffice_payment.sql
installer/backoffice/11_backoffice_payment_z3_approved_at.sql
installer/backoffice/20_backoffice_telemetry.sql
installer/backoffice/30_backoffice_tenants_extra_accounts.sql
installer/backoffice/31_backoffice_z2_landing_signups_tenant_fk.sql
installer/backoffice/32_backoffice_s2_owner.sql
installer/backoffice/40_backoffice_cs.sql
installer/backoffice/41_backoffice_cs_kb.sql
installer/backoffice/42_backoffice_cs_stages.sql
installer/backoffice/43_backoffice_promotion_legacy_align.sql
installer/backoffice/44_backoffice_pricing_plan_columns.sql
installer/backoffice/45_backoffice_cs_outbound_retention.sql
installer/backoffice/46_backoffice_email_send_history.sql
installer/backoffice/90_seed_permissions.sql
installer/backoffice/91_seed_admin.sql
```
