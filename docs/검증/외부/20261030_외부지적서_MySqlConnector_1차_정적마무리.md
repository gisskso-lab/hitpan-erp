# #729 MySqlConnector 2.5.0 → 2.6.2 — 정적 회신 남은 미검사 마무리 (㉠)

> 작성 = [4] 검증팀장 데이비드 박(정적 검토) · 공개 = PM 브라운킴 10/30 · 대표 결재 (c) ① 의 ㉠ · 앞 회신 = [(a)(b) 정적 회신](20261030_외부지적서_MySqlConnector_1차_정적회신_ab.md)
> 스캐너·목록 = [`20261030_x729scan/fin/`](20261030_x729scan/fin/) · 원자료 lits.json(6MB)·CI 잡 로그(575KB)는 싣지 않음 — 스캐너·`gh run view 41015242496 --log` 로 다시 만든다


> 작성 = [4] 검증팀장 데이비드 박 · 2026-10-30 · 대표 결재 ㉠ 범위(미검사 1~4 · 6 · 7) · 8(운영 연결)은 제외
> 앞선 정적 회신 §⓪-③ 의 미검사 목록을 이어서 끝낸 것이다.

- **고정 SHA:** `6b15caf92ea124ddc8e06d664560c83ba2484d59` (읽기 전용 작업 폴더 `git rev-parse HEAD` 일치 · `git status` 깨끗)
- **완료 / 전체:** **6 / 6** (미검사 1 · 2 · 3 · 4 · 6 · 7). 항목마다 남는 한계는 따로 적었다.
- **미검사 (남은 것):** ㉠ 범위 안에는 없음. 범위 밖으로 남은 것 = 5(상류 해석기 원문·DLL 실행) · 8(운영 연결 문자열) · 9(`91_seed_admin.sql:29` 치환값).
- 🔴 **실행 증거 없음 — 정적 검토만.** 드라이버·DB·앱·빌드를 한 번도 실행하지 않았다. DB 접속 0 · 운영 접근 0 · 코드 변경 0 · 패키지 내려받기 0.
  - 예외 하나: 미검사 7 은 **이미 끝난 CI 런의 기록을 읽기만** 했다(`gh run view --log`). 새 런 0.
- 판정은 모두 「정적 검사상 …」 이다. 드라이버 판 사이의 해석이 같다는 실행 입증이 아니다.

## 방법 — C# 문자열 리터럴 추출기 (미검사 1·2 공통)

- 앞선 회신은 `git grep` 으로 C# SQL 안 `?` 를 가르지 못했다(C# 삼항·null 연산자와 섞임 · 후보 줄 7,213).
- 그래서 C# 렉서를 새로 썼다. 주석을 건너뛰고, 문자열 리터럴(일반 · `@"…"` · `"""…"""` · `$"…"` 보간)만 뽑는다. 보간 구멍 `{…}` 안은 C# 코드로 따로 렉싱한다.
- 뽑은 리터럴마다 SQL 영역을 다시 나눈다. 코드 / `'…'`·`"…"`·`` `…` `` 인용 / `--`·`/*`·`#` 주석이다.
- 대상: 시험 프로젝트 3개(`HitPan.Tests`·`HitPan.IntegrationTests`·`HitPan.Watchdog.Tests`)를 뺀 `src/*.cs`·`tools/*.cs` **756파일 · 리터럴 22,193개** (SQL 꼴 1,663개).
- 도구 위치 (레포 밖 · 이 세션 작업 폴더): `docs/검증/외부/20261030_x729scan/fin/cs_lits.py` · `an_q.py` · `agg.py` · `bo2.py` · `bo3.py` · `chk19.py`
  - 실행 꼴: `py -3 -I cs_lits.py <작업폴더> files_cs.txt lits.json`
- **대조군 (렉서가 제대로 가르는지):**
  - ① 앞선 회신의 C# `--`+줄끝 19곳을 **같은 19곳**(CsRequestController 6 · EmployeeService 1 · PurchaseService 12)으로 다시 찾았다.
  - ② 인용 안 `?` 를 잡았다 — 아래 `tools/MdbMigrationBaselineRun/Program.cs:192` 의 `'?'`.
  - ③ 인용 안 `\` 를 잡았다 — `WatchdogHandoffBootstrap.cs:385` 의 PowerShell 문자열. SQL 이 아닌 오탐이며 손으로 걸렀다.
  - ④ 코드 영역 `?` 를 잡았다 — `HitPan.Web` 의 URL `?limit=`.

## 항목별 결과

### 미검사 1 — C# SQL 안의 `?` · **완료**

| | |
|---|---|
| 대상 수 | `?` 가 든 리터럴 **137개** — Web 100 · 그 밖 37 |
| 완료 수 | 137 / 137 |

**SQL 꼴 리터럴 안 `?` — 9개**

- `HitPan.Web` 7개는 HTTP 경로의 `?` 다(`BackupService.cs:40` 등). Web 은 MySqlConnector 를 참조하지 않는다. `HitPan.Web.csproj` 의 프로젝트 참조는 Contracts 하나뿐이다.
- `src/HitPan.Application/Services/PurchaseService.cs:535` 는 SQL **`--` 주석 안**의 `?` 다(한글 문장 끝 물음표).
- `tools/MdbMigrationBaselineRun/Program.cs:192` 는 SQL **작은따옴표 인용 안** `'?'` 다(`ELSE '?' END`).
  - 같은 문장에 `@T` 매개변수가 함께 있다.
  - 이 도구는 CI 에서 불리지 않는다(앞선 회신 §1-1).

**SQL 꼴이 아닌 나머지 (Web 밖 35개를 하나씩 봄)**

- 정규식 · URL 쿼리 · UNC 경로 · 한글 안내 문구다. SQL 조각은 0 이다.

**결과**

- 정적 검사상 **ERP·백오피스·도구의 C# SQL 코드 영역 `?` 는 0건**이다.
- 인용 안 `?` 는 1건(도구 · CI 밖), 주석 안 `?` 는 1건이다.

**남는 한계**

- `.razor`·리소스·설정 파일에서 오는 SQL 은 대상이 아니다. C# 리터럴만 봤다.
- SQL 꼴 판별은 키워드로 한다. 다만 `?` 판정은 SQL 꼴 여부와 상관없이 137개 전부를 봤다.
- 인용 안 `'?'` 를 2.5/2.6 이 매개변수로 보지 않는다는 것은 모형 판단이며 실행으로 확인하지 않았다.

### 미검사 2 — 백오피스 컨트롤러·서비스 SQL 리터럴 · **완료 (표본 → 전수로 넓힘)**

| | |
|---|---|
| 대상 수 | `src/HitPan.Backoffice.API` 의 `new MySqlConnection(` **41곳 · 37파일**. 그 파일들의 SQL 꼴 리터럴 **277개 · 38파일**(38번째 `CredentialsStatusController.cs` 는 SQL 이 아닌 오탐) + SQL 꼴로 안 잡힌 조각 리터럴 21개(`' AND status = @Status'` 꼴) |
| 완료 수 | 연결이 있는 37파일 모두 SQL 리터럴 집합에 들어 있다(대조 0 누락). 277 + 21 / 277 + 21 |

**결과 — 정적 검사상 0건인 것**

- 인용 안 `@`·`?`·`\`·`--` **0건**
- 코드 영역 `?`·`#`·`/*` **0건**
- `--`+줄끝 **0건**. `--`+공백 59곳은 두 판 모두 주석이다.
- 따옴표가 안 닫힌 리터럴 0건

**보간 구멍이 든 SQL 리터럴 9개는 상수만 끼운다** (사용자 입력이 없다)

- `CsAdminController.cs:167·216·252` 는 `hasPurge ? "k.purged_at" : "NULL"` 꼴이다.
- `CsAdminController.cs:777` · `OwnerApprovalController.cs:50` · `PromotionController.cs:69` · `ResellerPortalController.cs:101·150` · `WebhookAdminController.cs:40` 는 `{where}` 를 끼운다. `where` 는 위 조각 리터럴(상수)만 이어 붙인 것이다.

**참고 — 같은 추출기로 ERP 프로젝트도 다시 셈** (SQL 꼴 리터럴 1,488개)

- 인용 안 특수문자 0 · 코드 `?` 0 · 코드 `#` 0 이다.
- `#` 표시 3건은 PowerShell 문자열 2건과 로그 문구 1건이다. 모두 SQL 이 아니다.
- 앞선 회신 §3-3 「리터럴 안 `@` 0」 과 같다.

**남는 한계**

- 백오피스 **운영 연결 문자열의 `AllowUserVariables`** 는 저장소 밖이다(미검사 8 · 범위 제외).
- `SchemaMigrator` 의 MySQL 쪽 자체 분리기(`SplitStatements`)의 동작은 실행으로 확인하지 않았다.
- `@이름` ↔ 매개변수 객체 속성의 1:1 대조는 하지 않았다(판 올림과 무관한 축).

### 미검사 3 — ERP 별도 연결의 연결 문자열 출처 · **완료**

| | |
|---|---|
| 대상 수 | 6파일 · 연결 줄 8개. 앞선 회신은 「7곳」 이라 적었으나 줄 수로는 8 이다 |
| 완료 수 | 8 / 8 |

| 연결 줄 | 연결 문자열 출처 (파일:줄) | `AllowUserVariables` | 그 파일 SQL 의 `@` |
|---|---|---|---|
| `src/HitPan.API/BackgroundServices/OutboxPollerWorker.cs:81` | 설정 `Outbox:BackofficeConnectionString` (`:50`). 비면 **폴러 비활성 후 return** (`:51~55`). 저장소의 `appsettings.json`·`appsettings.Production.json` 에는 이 키가 **없다** | 값이 저장소 밖 → ⚠️미측정 | `@OutboxId`·`@LastError` — 둘 다 매개변수 객체에 있음 |
| `src/HitPan.API/HostedServices/IdempotencyCleanupService.cs:36` | `BuildConnectionString()` `:61~75` — `TenantConfigReader`(db.conf) 로 조립 | **없음** | 0 |
| `src/HitPan.API/HostedServices/IntegrityCheckService.cs:49` | `BuildConnectionString()` `:142~154` — db.conf | **없음** | `@TenantId`·`@EntityType`·`@EntityId`·`@Reason`·`@Ym` — 매개변수 객체에 있음 |
| `src/HitPan.API/HostedServices/MainPcRegistrationService.cs:129` | `BuildConnectionString()` `:338~351` — db.conf | **없음** | `@Id`·`@TenantId`·`@Name`·`@Fp` — 매개변수 객체에 있음 |
| `src/HitPan.API/Middleware/AuditLogMiddleware.cs:43` | `:15~16` `GetConnectionString("DefaultConnection")` → 없으면 `BuildConnectionStringFromEnv()` `:85~95`(db.conf). 저장소 appsettings 에 `DefaultConnection` **없음** ⇒ 저장소 기준으로는 db.conf 경로 | **없음** | 8개 — `:55~70` 매개변수 객체와 1:1 일치 |
| `src/HitPan.API/Services/CompanyBootstrapProvisioner.cs:60·212·607` | `ResolveConnectionString()` `:31~44` — `DefaultConnection` → 없으면 db.conf | **없음** | 45종 — 매개변수 대조는 일부만 봄 |

- 비교: 주 연결 `src/HitPan.Infrastructure/Extensions/InfrastructureExtensions.cs:55` 과 마이그 연결 `MigrationDbConnectionFactory.cs:57~59` 에는 `AllowUserVariables=true` 가 있다.
- **결과:** 정적 검사상 6파일 모두 `SET @`·`:=` 사용자 변수가 0 이고(앞선 회신 §3-3), `@` 토큰은 Dapper 매개변수 꼴이다. 그래서 `AllowUserVariables` 가 없어도 판 올림과 이어지는 노출은 발견되지 않았다.
- **남는 한계**
  - `CompanyBootstrapProvisioner` 의 매개변수 45종 전체 1:1 대조는 미완료다.
  - `Outbox:BackofficeConnectionString`·`DefaultConnection` 이 운영에서 환경변수 등으로 주어지는지는 저장소 밖이다.

### 미검사 4 — `tools/IssueTestDomain` 직접 SQL · `build-manifest.ps1:120` 용도 · **완료**

| | |
|---|---|
| 대상 수 | 2 |
| 완료 수 | 2 / 2 |

- **`tools/IssueTestDomain/Program.cs`**
  - DI 에 넣는 것은 `AddHttpClient` 와 `CloudflareDomainService` 뿐이다(`:22~32`). 호출하는 것도 Cloudflare API 4개뿐이다(`:61·65·70·71`).
  - `:56` 주석에 「DB INSERT 없음」 이라고 적혀 있다.
  - `CloudflareDomainService.cs` 안의 `MySqlConnection` 0 이다. csproj 에는 `Backoffice.API` 프로젝트 참조만 있다.
  - ⇒ 정적 검사상 **MySqlConnector 를 거치는 SQL 0**.
- **`installer/updates/build-manifest.ps1:120`**
  - `:120~122` 가 `hitpan_db_clean.sql` 을 `Get-Content -Raw` 로 **글자로 읽는다**. `:125~` 에서 정규식으로 `CREATE TABLE` 컬럼 집합을 뽑는다.
  - 용도는 워치독 상태 표 3개의 스키마 변경을 막는 **빌드타임 게이트**다(`:108~118` 설명).
  - ⇒ DB 로 보내지 않는다. **MySqlConnector 경로 0**.
  - 앞선 회신 §1-2 「출하 DDL 의 MySqlConnector 경로 0」 이 이로써 판독 6곳 중 6곳으로 닫힌다.

### 미검사 6 — 윈도우 게시 산출물 SQL 의 줄끝 (레포 안 근거만) · **완료 (결론 = 레포로는 확정 불가)**

| | |
|---|---|
| 대상 수 | 근거 5종: `.gitattributes` · 게시 워크플로 2개의 러너·체크아웃 · 레포 전체의 줄끝 설정·변환 명령 · 산출물 복사 방식 · 러너의 체크섬 쓰임 |
| 완료 수 | 5 / 5 |

| 근거 | 사실 (파일:줄) |
|---|---|
| `.gitattributes` | 규칙 3줄뿐이다. `*.sh text eol=lf` 와 Web appsettings 2개의 `linguist-generated` 다. `.sql` 규칙 **없음** |
| 게시 러너 | `.github/workflows/deploy-update.yml:122` · `build-installer.yml:49` 모두 `windows-latest` |
| 체크아웃 | `deploy-update.yml:145~149` `actions/checkout@v7` (`fetch-depth: 0` 만) · `build-installer.yml:62` (옵션 없음). 줄끝 옵션 0 |
| 줄끝 설정·변환 | `.github`·`scripts`·`installer`·API csproj 에서 `autocrlf`·`core.eol`·`dos2unix`·`unix2dos` **0건** |
| 산출물 복사 | `src/HitPan.API/HitPan.API.csproj:95` `None Update="Migrations\SQL\DB-*.sql" CopyToOutputDirectory` — 바이트 그대로 복사한다 |
| 산출물 SQL 검사 | `deploy-update.yml:397~457` 는 게시물 안 `DB-*.sql` 의 **집합·건수**만 대조한다. 바이트·줄끝은 대조하지 않는다 |
| 체크섬 | `MigrationRunner.cs:130` 의 SHA-256 은 `schema_migrations` 에 **기록만** 한다(`:139·152` → `:210~220`). 비교에 쓰는 곳 0 ⇒ 줄끝이 달라도 재적용 판단에는 영향이 없다 |

- **결과:** 정적 검사상 **게시 산출물 SQL 의 줄끝은 윈도우 러너의 git 기본 설정이 정한다.** 그 설정은 저장소 밖이므로 레포 근거로는 LF·CRLF 를 **확정할 수 없다.**
  - 레포 안에는 줄끝을 고정하는 장치가 0 이다.
  - 앞선 회신의 「CRLF 가능성 높음」 은 그대로 **추정**으로 둔다.
- **남는 한계:** 실제 산출물 바이트는 미측정이다(내려받기 금지 범위). ㉡ 격리 실행 대조의 CRLF 축이 이 공백을 대신 메운다.

### 미검사 7 — CI 런의 G-W13-5 결과 줄 재판독 · **완료 (기록 읽기만)**

| | |
|---|---|
| 대상 수 | 고정 SHA 의 `Build & Test` 런 1개 → 잡 `DB 게이트 (Integrity — 실제 DB 실행)` 로그 2,649줄 |
| 완료 수 | 1 / 1 |

**런 머리 (원문)**

- 런 `41015242496`(pull_request) · `headSha` = `6b15caf92ea124ddc8e06d664560c83ba2484d59` · 결론 success · 잡 6개 모두 success
- 체크아웃 줄: `HEAD is now at 505a172 Merge 6b15caf9… into 5401bbec…`
  - 런은 PR 병합 나무에서 돌았다.
  - `git merge-base 6b15caf9 5401bbec` = `5401bbec` 이다. 고정 SHA 가 이미 그 main 끝 위에 있으므로 병합 나무 = 고정 SHA 나무다(정적 추론).

**결과 줄 (원문)**

- `Passed G-W13-5 🔴 재적용에서 깨지는 마이그가 **늘지 않는다** — 새 마이그는 한 건도 못 들어온다 [1 s]`
- G-W13-1~6 모두 Passed. 같은 잡 끝에 `SKIP 없음 — 게이트가 실제로 돌았다.` · `Total tests: 813`

**이 초록이 말하는 것 (시험 원문 `src/HitPan.Tests/Integrity/ErpMigrationApplyGateTests.cs:302~392`)**

- 디스크의 전 마이그를 한 건씩 지우고 실물 러너로 최대 두 번 돌린다.
- 그 뒤 세 가지를 단언한다.
  - 깨진 집합 = 고정 목록 10건(`:523~` · DB-02·09·**16**·20·27·34·35·36·37·38)
  - 늘어도 빨강 · 줄어도 빨강
  - 실제 적용 건수 ≥ 전체 − 10 (`:353~357` · 빈 폴더로 생기는 거짓 초록 차단)
- ⇒ 2.6.2 · LF(우분투) 에서 **깨지는 마이그의 집합이 2.5.0 때 고정한 것과 같다.** DB-16 은 여전히 「깨짐」 쪽에 있다.

**남는 한계**

- ① 실패 사유는 실패할 때만 찍힌다. 그래서 DB-16 이 2.6.2 에서도 **「DELIMITER 거부」 라는 같은 사유로** 깨졌는지는 로그로 확인할 수 없다.
- ② 이 로그에 MySqlConnector 복원 판 번호 줄은 없다. 시험이 2.6.2 로 돌았다는 것은 csproj(`HitPan.API.csproj:40` · `HitPan.Application.csproj:21`)에서 한 추론이다.
- ③ LF 한 줄끝만 돌았다.
- ④ 아래 새 위험 후보 R-2.

## 새 위험 후보 (판 올림과 직접 이어지는 것은 R-2 · 나머지는 참고)

- **R-2 (판 올림과 직접 관련)** — G-W13-5 가 무는 것은 **「에러가 났나」** 뿐이다.
  - 2.6 의 `--`+LF 꼴이 다음 줄의 **실제 SQL 을 주석으로 먹어도** 그 문장은 조용히 빠질 뿐 에러가 나지 않는다. 그래도 시험은 초록이다.
  - G-W13-2(모양 = 출하 DDL)는 가장 새 마이그 하나에만 걸린다.
  - ⇒ 현 CI 초록은 「조용한 문장 누락」 의 부재를 입증하지 못한다.
  - 정적으로는 현재 605곳 모두 다음 줄이 주석이다(앞선 회신). 실행 대조는 ㉡ 의 합성 대조군 몫이다.
- **R-1 (참고 · 판 올림과 무관)** — ERP 별도 연결 6파일 · 8줄은 `AllowUserVariables` 없이 db.conf 로 조립한다. 주 연결·마이그 연결과 옵션이 다르다.
  - 현재는 사용자 변수 0 이라 노출 없음(정적).
  - 다만 누가 이 경로의 SQL 에 `SET @x` 를 넣으면 이 경로에서만 실패한다.
- **R-3 (참고)** — `.sql` 의 줄끝을 고정하는 `.gitattributes` 규칙과 게시 단계 바이트 검사가 없다.
  - 고객 산출물 줄끝이 러너 기본값에 달려 있고, CI 실행 게이트(LF)와 같다는 보장이 레포 안에 없다.
- **R-4 (참고)** — `tools/MdbMigrationBaselineRun/Program.cs:192` 는 매개변수가 있는 문장 안에 인용 `'?'` 가 있다. 도구 · CI 밖이다.

## 남은 미검사 (㉠ 범위 밖)

1. 상류 MySqlConnector 2.5.0/2.6.2 해석기 원문 재판독 · DLL 실행 (앞선 목록 5)
2. 백오피스 운영 연결 문자열의 `AllowUserVariables` (앞선 목록 8 · 운영 접근 금지)
3. `91_seed_admin.sql:29` 치환값의 글자 구성 (앞선 목록 9)
4. 두 판 × 두 줄끝 실행 대조 — ㉡ 격리 CI 실행 대조 몫
