# 작업지시서 20261030작 — MySqlConnector 2.5.0 ↔ 2.6.2 × LF ↔ CRLF 격리 CI 실행 대조 (㉡)

> 🔴🔴 **PM 결재 전 코딩 금지** (#33 · #42). 이 문서는 **초안**이다(작성 = 설계팀장 존 · 결재 = PM 브라운킴 · 작성자 ≠ 결재자).
> 🔴 **#729 가지(`6b15caf9`) 무접촉 · 제품 코드 0줄 · 기존 워크플로·잡 무접촉 · 새 워크플로 1개만** — [결재 X733c](20261030_결재_X733c_정적마무리_격리실행대조_대표.md) 「㉡ 운용」 그대로.
> 결과의 **해석**은 이 문서의 몫이 아니다(HQA · CTO 몫 · 결재 「닫지 않는 것」).

## §0 [카드]

| 항목 | 내용 |
|---|---|
| 작성 | 설계팀장 존 · 2026-10-30 · 대상 고정 SHA = `6b15caf92ea124ddc8e06d664560c83ba2484d59`(#729 머리) · 읽기만 |
| 결재 | ⏳ PM 브라운킴 (이 문서) — 대표 결재는 [X733c](20261030_결재_X733c_정적마무리_격리실행대조_대표.md) 결정 1·3 으로 끝남 |
| 오더 | X733c ㉡ — [정적 회신](../검증/외부/20261030_외부지적서_MySqlConnector_1차_정적회신_ab.md) §6-2 설계 그대로 · 루틴 [2]→PM→[3]→[3-V]·[4]→런 |
| 바뀌는 축 | 판 {2.5.0, 2.6.2} × 줄끝 {LF, CRLF} = **4칸** (그 밖은 전부 고정 · §3-5) |
| 하는 것 | 계측 가지 1(바닥 `6b15caf9`) · 초안 PR 1(「계측용 · 머지 금지」) · 새 워크플로 1 · 측정 시험 1 · 보조 파이썬 2 · 대조군 SQL 2 |
| 하지 않는 것 | #729 가지 커밋 · 제품 프로젝트 줄 · `build-and-test.yml` 등 기존 워크플로 · `appsettings*.json`(#21) · 이 PC DB · 운영·demo(#39) · 결과 해석 |
| 추천 요지 | 트리거 = **계측 가지 push 만**(§3-1) · 판 = **잡 안에서 csproj 활성 줄 8개만 글자 바꾸기 · 커밋 0**(§3-2) · 줄끝 = **체크아웃 직후·빌드 전** 129+대조군 변환 · 출하 DDL 은 LF 그대로(§3-3) · 실행기 = **G-W13-5 무수정 실행 + 측정 시험(같은 조립품)**(§3-4) · DB = 단계 기동 컨테이너 · 다이제스트 고정(§3-5) · 판정 = 비교 잡이 기계로(§3-6) |
| 대조군 | ① `DB-16` 원본 → 4칸 같은 거부 · ② 합성 `SELECT @P AS a --`⏎`, @P AS b;`(P=7) → **2.6.2×LF 칸에서만 `b` 다름** · 4칸 같으면 런 무효 (§5) |
| 규모 | **M** — 새 파일 6(워크플로 1 · 시험 1 · 파이썬 2 · SQL 2) · 제품 0 · DDL 0 · 갈래 1 |
| 에이전트 상한 | 3 — hp-implementer 1 · hp-verifier 2([3-V] 보안상무 · [4] 검증팀장 따로) |
| CI 런 상한 | **장치 4 + 측정 2 = 6** (추천 · K6) — 넘으면 멈추고 PM 보고 |
| 비밀값 | **새 비밀값 0** — DB 비번은 런 안 난수 + 가림(§3-5) · 문서·로그·커밋에 자격증명 모양 글자 0 |
| 확인 필요 | **K1~K8** (§7) — 첫째 K2 = 측정 시험 자리(「제품 `src/` 0줄」 의 읽기) |
| ⚠️ 정정 | 오더문의 `src/HitPan.IntegrationTests/.../ErpMigrationApplyGateTests.cs` 는 실제로 **`src/HitPan.Tests/Integrity/ErpMigrationApplyGateTests.cs`** 다(§2-3) |
| 금지 | 빌드 0/0(#19 · 측정 칸 경고 예외는 §3-2 근거) · 빈 catch(#15) · 덮어쓰기(#1) · 외부 노출 금지 낱말(#23) |
| 산출 | 개발명세서 → `docs/개발/erp/` · 작업리뷰서 → `docs/검증/erp/` · [3-V] → `docs/검증/병렬이슈/` · 결과 문서 → `docs/검증/외부/`(K7) |
| DoD·롤백 | §8 |

## §1 [근거·범위]
- 결재 원문(대표 10/30): 「(c) 결정 - 피엠 추천안대로!! ① 정적 검증 마무리와 격리 CI 실행 대조를 함께 진행」 → 결정 3 「㉡ 범위 = 정적 회신 §6-2 설계 그대로」.
- ㉡ 운용(결재 문서 :21~28): 고정 축 2개 · 고정 항목 4개(같은 SQL 집합 · 같은 실행기 · 같은 DB 초기 상태 · PASS/FAIL 기준) · 대조군 2개 · #729 가지 무접촉 · 계측 가지+초안 PR · 제품 `src/` 0줄 · 새 워크플로 1개 · DB = CI 컨테이너만.
- HQA 요청 원문 :31 「(c) 승인된 격리 CI 환경에서 2.5.0↔2.6.2 × LF↔CRLF 의 결과/오류/실행수/스키마 대조군」 — 이 문서가 재는 것은 이것뿐이다. ㉠(정적 마무리)은 별건.
- 오더에 없는 것 넣지 않음(#33): 업무 SQL 전수 실행 · 고객 PC(윈도우) 실행 · 운영 백오피스 연결 옵션 · 트리거·루틴 모양 비교(§6-2 에 없음).

## §2 [사실] (고정 SHA `6b15caf9` 읽기 · 실행 0)
### §2-1 기존 CI (`.github/workflows/build-and-test.yml`)
- 트리거 `:8~12` — push·pull_request 모두 `develop`·`main` 만. ⇒ 다른 이름 가지 push, 또는 base 가 그 둘이 아닌 PR 에서는 **이 워크플로가 안 돈다**.
- `db-gate` `:288~` — `ubuntu-latest` · 서비스 `mariadb:11.4`(태그만, 다이제스트 고정 없음 `:294`) · 비번은 글자 고정(`:296`) · apt 클라이언트 3회 재시도 `:315~325` · 대기 `:327~335` · `max_connections` `:337~342` · 출하 DDL **CLI 적재** `:344~358`(`:350~355`) · 빌드 `:360~362` 는 **`/warnaserror` 없음**(Windows `build` 잡 `:73` 에만 있음) · 시험 필터 `:1349~1353` · SKIP 0 검사 `:1354~1363` · 결과 업로드 `:1365~1371`. G-W13 등록 `:1158~1197`.
- `bo-mysql8-gate` `:1476~` — `mysql:8.0` 서비스 · `BackofficeSchemaMysql8Gate` 필터 `:1535`.
- 다른 워크플로 트리거: `codeql`·`watchdog-ci`·`deploy-ncp` = main/develop 한정 · `zap-baseline` = main push·수동 · `deploy-update`·`build-installer` = 수동·태그 · **`trufflehog` = 모든 push·PR**(계측 가지에서도 돈다 · 무해).
### §2-2 판이 드는 자리
- MySqlConnector 직접 참조 csproj **8** — 활성 줄은 전부 `Version="2.6.2"`, 옛 `2.5.0` 줄은 **주석 안**: `src/HitPan.API/HitPan.API.csproj:40`(주석 `:35`) · `HitPan.Application.csproj:21`(`:16`) · `HitPan.Backoffice.API.csproj:28`(`:23`) · `HitPan.Infrastructure.csproj:31`(`:26`) · `HitPan.IntegrationTests.csproj:21`(`:16`) · `tools/MdbMigrationBaselineRun/…csproj:37`(`:32`) · `tools/MigrationIdempotencyCheck/…csproj:31`(`:26`) · `tools/MigrationSmokeTest/…csproj:25`(`:20`).
- 중앙 판 관리 없음: `Directory.Packages.props` 0 · `Directory.Build.props` 는 판 번호(`HitPanVersion`)만 · 판 속성 변수 0.
- `HitPan.Tests.csproj` 는 MySqlConnector 를 **직접 참조하지 않음** — `:26·27·32·43` ProjectReference(Application·Infrastructure·API·Backoffice.API)로 넘겨받음.
- 경고=오류 설정: `TreatWarningsAsErrors=true` 는 `HitPan.Watchdog`·`Watchdog.Tests`·`tools/MdbMigrationBaselineRun.csproj:18` 셋. NU1902 억제는 `HitPan.Application.csproj:54~59` 에서 **거둬졌다**(다시 켜지 말 것 주석).
- `src/HitPan.sln` 에 `HitPan.IntegrationTests`·`tools/*` **없음**(13 프로젝트).
- 게시 대상: `build-installer.yml:88·99·138` · `deploy-update.yml:198·207·232` · `deploy-ncp.yml:67·70·73` — API·Web·Watchdog·Backoffice.API·Backoffice·Landing. **시험 프로젝트는 게시되지 않는다.**
### §2-3 실행기
- ERP 러너 `src/HitPan.Application/Services/MigrationRunner.cs` — 폴더 후보 `:230~251`(1순위 `AppContext.BaseDirectory/Migrations/SQL`) · `:64` 나열 · `:113` UTF-8 통째 읽기(줄끝 정규화 0) · `:130` 체크섬(줄끝 포함) · `:135` Dapper 매개변수 0 실행.
- 제품 연결 성질 `src/HitPan.Infrastructure/Persistence/MigrationDbConnectionFactory.cs:57~59` — `AllowUserVariables=true` 등.
- 게이트 `src/HitPan.Tests/Integrity/ErpMigrationApplyGateTests.cs` — G-W13-5 `:305~404`(전건 「적용됨」 시드 → 한 건씩 이력 삭제 → **실물 러너 두 바퀴** → 깨짐 집합이 `KnownNotReapplyable` 과 **양방향 일치**해야 초록 · 측정 수 ≥100 · 적용 수 단언) · 명단 `:523~535`(10건 · `DB-16` = DELIMITER 거부) · DB 이름 환경변수(이름에 test/gate 필수) `:545~563` · `ReadyAsync` = DROP/CREATE + 출하 DDL CLI 적재 `:565~590`·`:718~742` · `RunRealRunnerAsync` `:600~604` · 모양 `ShapeAsync` `:611~` = 표·칸 형·널·인덱스 이름만(**COLUMN_DEFAULT · COLUMN_COMMENT 없음**) · `MaterializeRepoSql` `:651~658` = **레포 원문**(`src/HitPan.API/Migrations/SQL`)을 러너 폴더로 `File.Copy`(바이트 그대로) · 연결 `:745~752` `AllowUserVariables=true`.
- ⇒ G-W13-5 의 러너가 읽는 SQL = **체크아웃된 레포 원문의 바이트 복사본**. 빌드가 `HitPan.API.csproj:95` 로 출력에 깔아 둔 복사본은 `MaterializeRepoSql` 이 지우고 다시 깐다.
- 백오피스 `src/HitPan.Backoffice.API/Services/SchemaMigrator.cs` — 생성자 `:31`(스크립트 폴더를 인자로 받음) · `ApplyAsync` `:49` · `:93` 이름순 · `:108` 읽기 · MariaDB 면 `:131~136` 통째 1개 명령 · MySQL 이면 `:145` → `:218~` 자체 분리(`SplitStatements` `:365`) 후 문장별 명령.
### §2-4 상류 해석기 (공개 원문 판독 · 실행 아님)
- 2.6.0 변경 이력: 「Handle all comment-start characters during SQL parsing」(#1624) · 「Fix termination of quoted parameters」(#1627). 2.6.1·2.6.2 에 해석 관련 항목 없음.
- 2.5.0 `src/MySqlConnector/Core/SqlParser.cs`: `--` 다음 글자가 **공백(' ') 일 때만** 주석.
- 2.6.2 같은 파일: `--` 다음 글자가 `'\0'~' '` 또는 `'\x7F'` 이면 주석 시작 · 주석 끝 판정(`'\n'`)은 **주석 상태에 들어간 뒤의 글자**에서만 ⇒ `--` 바로 뒤 `\n` 은 주석을 **열기만 하고 닫지 않아** 다음 줄 끝까지 먹는다. `--\r\n` 은 `\r` 이 열고 `\n` 이 닫는다.
- ⇒ 대조군 ② 기대(2.6.2×LF 만 다름)는 **상류 원문과 맞는다(정적)**. DLL 실행 확인은 ⚠️미측정 — 이 런이 처음이다.

## §3 [만들 것] — 쟁점별 안 비교 + 추천
### §3-1 가지·PR·트리거
- 가지: `measure/20261030-x733c-mysqlconnector` · 바닥 `6b15caf9` 고정(scratchpad 수동 worktree · `commit-tree` 쓰면 BASE SHA 한 번 고정). 계측 파일만 더한다.
- 트리거 안:

| 안 | 내용 | 막히는 점 |
|---|---|---|
| A `pull_request` | 초안 PR 이벤트로 돈다 | 충돌 PR(mergeable=false)이면 **안 돈다** · base 필터를 안 걸면 남의 PR 에서 돈다 |
| B `workflow_dispatch` | 수동 실행 | 기본 가지(main)에 파일이 없으면 **못 쓴다** — main 에 넣는 순간 결재 범위(가지 밖) 이탈 |
| **C `push` (추천)** | `on: push: branches: [measure/20261030-x733c-mysqlconnector]` + `paths:` 계측 파일만 | 병합 가능 여부·기본 가지 무관 · 그 이름 가지 말고는 **어디서도 안 돈다**. 재실행은 Actions 「Re-run」 |

- 초안 PR: 제목 「[계측용 · 머지 금지] X733c MySqlConnector 실행 대조」 · 초안 상태 유지 · base 는 K1.
- `permissions: contents: read` · `concurrency: x733c`(취소 안 함) · `runs-on: ubuntu-24.04`(고정).
### §3-2 판 바꾸기 (칸마다 2.5.0 / 2.6.2)

| 안 | 방법 | 판정 |
|---|---|---|
| **A 잡 안 글자 바꾸기 (추천)** | 체크아웃 뒤, 2.5.0 칸만 `Include="MySqlConnector"` 이면서 `Version="2.6.2"` 인 줄을 `2.5.0` 으로 — 커밋 0 · 런 끝나면 사라짐 | 단순·투명. 주석 속 옛 줄은 `2.5.0` 이라 안 걸린다. **바뀐 줄 = 정확히 8파일 8줄**을 `git diff --numstat` 로 단언, 2.6.2 칸은 `git diff --quiet` 단언 |
| B MSBuild `-p:` 속성 | 명령줄로 판 지정 | csproj 가 글자 판이라 **안 먹는다** — 먹게 하려면 제품 csproj 수정 ⇒ 기각 |
| C `Directory.Packages.props`(중앙 관리) | `VersionOverride` | 켜려면 8 csproj 의 `Version=` 을 빼야 함 ⇒ 제품 줄 ⇒ 기각 |
| D 잡 안 `Directory.Build.targets` 생성 | `<PackageReference Update="MySqlConnector" Version=…/>` | 커밋 0 으로 가능하나 글자 바꾸기보다 덜 보인다. A 실패 시 예비 |

- 기록(칸 JSON): ① `git diff --numstat` 원문 ② restore 된 각 프로젝트 `obj/project.assets.json` 의 `libraries` 키 `MySqlConnector/<판>` — **측정 그래프 5개**(Application·Infrastructure·API·Backoffice.API·Tests) 모두 칸 판과 일치 단언 ③ 실행 중 **실제 올라온 조립품** `typeof(MySqlConnection).Assembly` 의 InformationalVersion. 셋 중 하나라도 어긋나면 그 칸 무효.
- 나머지 3 csproj(IntegrationTests·tools 2)는 글자만 바꾸고 restore 하지 않는다(측정 그래프 밖 · `MdbMigrationBaselineRun` 은 경고=오류라 2.5.0 에서 NU1902 로 restore 가 죽는다 — 측정과 무관한 실패를 들이지 않는다).
- **NU1902(2.5.0 칸)**: 억제하지 않는다(K5). 근거 — ① 측정 그래프에 경고=오류 프로젝트가 없고 `db-gate` 와 같은 빌드(`:362` · `/warnaserror` 없음)라 경고로만 남는다 ② 그 경고 수 자체가 **판 바꾸기가 실제로 됐다는 넷째 증거**다: 2.5.0 칸 ≥1 · 2.6.2 칸 0 이어야 한다(아니면 무효). ③ `NoWarn` 등 억제 추가는 제품 규칙(`HitPan.Application.csproj:59`)과 같은 냄새라 피한다. 2.5.0 은 **측정 칸에서만** 쓰고 산출물은 어디에도 올리지 않는다.
### §3-3 줄끝 바꾸기
- 대상: 고정 SHA 의 `src/HitPan.API/Migrations/SQL/DB-*.sql` 113 + `installer/backoffice/*.sql` 16 + 대조군 SQL 2. **출하 DDL `installer/hitpan_db_clean.sql` 은 바꾸지 않는다** — CLI 경로라 드라이버와 무관하고, 4칸의 **DB 초기 상태를 같게** 두는 쪽이 고정 항목이다.
- 시점: **체크아웃 직후 · restore/빌드 전**(출력 폴더 복사본까지 같은 줄끝이 되게).
- 방법: `python3 -I tests/measure/x733c/eol_convert.py <lf|crlf>` — 바이트로 `\r\n`→`\n` 정규화 뒤 crlf 칸만 `\n`→`\r\n`(마지막 줄 처리 정확 · `sed 's/$/\r/'` 는 끝 줄에 맨 `\r` 를 남길 수 있어 안 씀). 홀로 `\r` 있으면 멈춤.
- 기록(파일마다): 전후 바이트 수 · LF 수 · CR 수 · CRLF 수 · sha256. 단언 = LF 칸 CR 0 · CRLF 칸 CR = LF.
- **실행기가 실제로 읽는 파일 재확인**: 측정 시험이 `MaterializeRepoSql` 직후 러너 폴더(`AppContext.BaseDirectory/Migrations/SQL`)와 백오피스에 넘긴 폴더의 CR/LF 수를 다시 세어 JSON 에 넣는다. 원본 수와 다르면 그 칸 무효.
### §3-4 같은 실행기

| 안 | 내용 | 판정 |
|---|---|---|
| A G-W13-5 만 그대로 | 필터 실행 · 코드 0 | 초록이면 「깨짐 집합 = 명단 10」 은 확정되나 **사유·적용 수·모양·대조군·바이트가 안 남는다** ⇒ §6-2 PASS ②③④ 못 잼 |
| B 측정 전용 시험만 | 새 시험이 같은 러너를 같은 순서로 돌리며 JSON 기록 | 루프를 베낀 것이라 「같은 실행기」 반증 여지 |
| **C A + B (추천)** | 칸마다 ① G-W13-5 **무수정** 실행(닻) ② 측정 시험 실행 → 칸 JSON. 측정 시험의 깨짐 집합이 G-W13-5 결과와 어긋나면 **장치 고장**으로 그 칸 무효 | 같은 실행기를 원본으로 확인 + 기록은 측정 시험이 |

- 측정 시험(`X733cMySqlConnectorProbeTests`)이 하는 일: `MigrationGateDb`(같은 조립품 · internal) 로 고정 DB 이름 `gate_x733c`(이름 고정 ⇒ 오류 문구 속 무작위 이름 차이 0) → 출하 DDL 적재 → G-W13-5 와 같은 시드·한 건씩·두 바퀴로 **실물 `MigrationRunner`** → 파일마다 {1·2회차 성공, 오류 번호, 예외 형, 메시지 첫 줄} · 적용 수 · 깨짐 집합 → 확장 모양(표·칸 형·널·**COLUMN_DEFAULT·COLUMN_COMMENT**·인덱스 · `schema_migrations` 제외) → 백오피스 `SchemaMigrator.ApplyAsync`(빈 DB · 폴더 = 기존 백오피스 게이트가 넘기는 자리와 같게 — [3] 이 `BackofficeSchemaMysql8GateTests` 를 읽고 맞춘다) 적용 파일 수 · `_schema_migrations` 행 → 대조군 ①·②(§5) → 바이트(§3-6 K4).
- 환경변수 `HITPAN_X733C_CELL` 이 없으면 시험은 돌지 않고 끝난다 — 그러나 이 워크플로의 비교 잡은 **칸 JSON 이 없으면 빨강**이라 조용한 초록이 없다. 이 가지는 기존 CI 가 안 돈다(§2-1).
- 자리(`src/HitPan.Tests/Measure/` vs 레포 밖)는 **K2**.
### §3-5 같은 DB 초기 상태
- MariaDB: 서비스 컨테이너 대신 **단계에서 `docker run`** — 이미지 `mariadb:11.4@sha256:<다이제스트>`(값은 [3] 이 첫 장치 런에서 `docker inspect` 로 확정해 워크플로에 고정 · ⚠️미정) · 비번은 그 단계에서 `openssl rand` 로 만들고 `::add-mask::` · 환경변수로만 넘김(문서·커밋에 글자 0). 근거 = 서비스 컨테이너 env 는 단계 출력을 못 받아 글자 고정 비번이 된다(`:296` 꼴 반복 안 함).
- 칸마다 새 VM·새 컨테이너 · 고정 이름 빈 DB `gate_x733c` · `utf8mb4_unicode_ci` · 출하 DDL(같은 SHA · LF 그대로) CLI 적재 → 전건 「적용됨」(G-W13-5 선행 절차).
- 칸 JSON 에 `VERSION()` · `@@version_comment` · `@@sql_mode` · `@@character_set_server` · `@@collation_server` · `max_connections` · 이미지 다이제스트 · `dotnet --version` · 러너 이미지 · 출하 DDL sha256. **4칸이 다르면 런 무효**(판정 전 전제).
- 백오피스 MySQL 8 칸: 정적 결과 = 백오피스 SQL 코드 영역 `@` **0** · `?` **0**(정적 회신 §2 표 · :142~143) ⇒ 드라이버 해석 차이가 날 자리가 없다. 다만 운영 판(MySQL 8)의 **자체 분리 경로**(`SchemaMigrator.cs:145·218~`)는 MariaDB 칸에서 한 번도 안 돈다. 안 = K3 · **추천 2잡**(2.5.0·2.6.2 × LF — 백오피스 배포·CI 가 둘 다 LF · 정적 회신 §4 :229) · `mysql:8.0@sha256:<다이제스트>` 고정 · 기존 `BackofficeSchemaMysql8Gate` 무수정 실행 + 측정 시험의 백오피스 부분.
### §3-6 PASS/FAIL 기계 판정 (비교 잡 `compare`)
- `needs: [measure]` · `if: always()` · 칸 JSON 4(+MySQL 8 2) 내려받아 `python3 -I tests/measure/x733c/compare_cells.py` → `verdict.json` · `verdict.md`(칸×항목 표) 업로드 · 종료 코드 = PASS 0 / FAIL 1 / 무효 2.
- **전제(하나라도 어긋나면 「무효 = 미측정」)**: 같은 SHA · 같은 SQL 목록 해시(`git ls-tree` 결과) · 판 증거 4종 일치(§3-2) · 줄끝 단언(§3-3) · DB 전제 동일(§3-5) · G-W13-5 가 SKIP 없이 돌고 측정 수 ≥100 · 측정 시험 깨짐 집합 = 그 칸 G-W13-5 결과 · **대조군 ② 생존**.
- **PASS(4칸 모두 같아야)** — 정적 회신 §6-2 그대로: ① 실패 마이그 집합(= 명단 10) · 사유 종류(오류 번호 · 예외 형 · 메시지 첫 줄) ② 적용 건수 ③ 확장 모양 집합 ④ 백오피스 적용 파일 수 · `_schema_migrations` 파일명 집합 · 대조군 ① 같은 거부.
- **FAIL(하나라도)**: 위 항목 한 줄이라도 다름 · 바이트(K4) 1바이트라도 다름. 칸 사이 차이는 **원인 파일:줄을 적기 전까지 FAIL**(해석은 HQA·CTO).
- **축별로만 비교하는 값**(줄끝이 들어가 당연히 갈리는 값 — 판 축으로만 같아야 함): 러너 체크섬(`MigrationRunner.cs:130`) · 백오피스 `content_hash` · 파일 sha256 · 서버가 받은 문장 바이트.
- 판정 문구는 「같다 / 다르다 / 무효」 셋뿐 — 「호환」「안전」 같은 해석 낱말을 쓰지 않는다.
### §3-7 바이트(정적 회신 §6-1 「0단계」)
- 안 = K4. **추천 = 칸 안 `general_log`**: 컨테이너에서 `log_output=TABLE` · `general_log=1` → 러너 연결이 보낸 `Query` 만 걸러(거른 규칙을 JSON 에 적음) 마이그별 sha256. 같은 줄끝끼리 2.5.0 ↔ 2.6.2 비교. 대조군 ② 도 서버가 받은 문장(두 번째 `@P` 치환 여부)이 그대로 보인다. 내부 API·DLL 반사 0.
- 예상 가능한 차이 후보(⚠️ 실행 전): 2.6.2 해석기는 문장이 주석 안에서 끝나면 개행을 덧붙이는 표지(`NeedsNewline`)가 있다 — 2.5.0 에 같은 동작이 있는지 미판독. 나오면 FAIL 로 적고 원인 줄을 단다.

## §4 [안 할 것]
- #729 가지·PR 에 커밋·푸시·리베이스 · 제품 프로젝트(게시 6종 · Domain · Contracts · Analyzers) 한 줄 · 8 csproj 커밋 · 기존 워크플로·잡·게이트 파일 수정 · `KnownNotReapplyable` 수정 · `NoWarn` 등 억제 추가.
- main·develop 에 이 워크플로를 올림 · `workflow_dispatch` 로 바꾸려고 main 에 파일 넣기.
- 이 PC DB 시험 · 운영·demo 접근 · 운영 백오피스 연결 문자열 · 고객 PC/윈도우 실행.
- 업무 SQL 전수 실행 · 트리거·루틴·뷰 모양 비교 · 역순 측정 · 판 3개 이상.
- FAIL 칸 재실행으로 초록 만들기(§6-3) · 결과 해석·권고 문구.

## §5 [대조군]

| # | 내용 | 기대 | 어긋나면 |
|---|---|---|---|
| ① `DB-16` 원본 | 같은 SHA · 무수정 파일을 G-W13-5 방식으로 **한 건만**(이력 삭제 → 러너) | 4칸 모두 **같은 거부** — 같은 예외 형 · 같은 메시지 첫 줄 · 적용 0 | FAIL(판·줄끝이 `DELIMITER` 판정에 닿는다) |
| ② 합성(살았나) | `tests/measure/x733c/controls/c2_param.sql` = `SELECT @P AS a --`⏎`, @P AS b;` · §3-3 변환을 **같이 받음** · 러너와 같은 성질 연결(`AllowUserVariables=true`)로 매개변수 P=7 실행 | 2.5.0×LF · 2.5.0×CRLF · 2.6.2×CRLF = `a=7, b=7` · **2.6.2×LF = `b` ≠ 7**(상류 원문상 NULL — 두 번째 `@P` 가 주석에 먹혀 서버엔 사용자 변수로 감 · §2-4) | 4칸 같음 ⇒ **장치 죽음 → 런 전체 무효**. 엉뚱한 칸이 다름 ⇒ 무효 + 보고 |
| ②′ `.sql` 경로 변형(기록만) | `c2b_qmark.sql` = `SELECT 1 AS a --`⏎`, ? AS b;` 를 러너 폴더에만 `DB-9999zzx733c_probe.sql` 로 두고 **실물 러너**(매개변수 0)로 실행 · 끝나면 지움(레포 0) | 판정하지 않는다 — 4칸 결과(성공/오류 번호/메시지 첫 줄)만 표에 | — |

- 대조군 파일은 제품 SQL 집합 밖(`tests/measure/x733c/controls/`) · 목록 해시에 따로 넣는다.

## §6 [CI·갈래·규모·멈춤]
### §6-1 워크플로 꼴 (`.github/workflows/x733c-mysqlconnector-measure.yml` · 새 파일 1)
- 잡 `measure` — `strategy.matrix: {driver: [2.5.0, 2.6.2], eol: [lf, crlf]}` · `fail-fast: false` · `timeout-minutes: 40`. 단계 순서: 체크아웃 → [장치] 제품 나무 = `6b15caf9` 단언(`git diff --quiet 6b15caf9 HEAD -- . ':!tests/measure/x733c' ':!.github/workflows/x733c-mysqlconnector-measure.yml' ':!<K2 자리>'`) → [장치] 판 바꾸기 → [장치] 줄끝 바꾸기 → [장치] DB 기동·전제 기록 → restore/빌드(`HitPan.Tests.csproj` 만 · `db-gate :361~362` 꼴) → [시험] G-W13-5 무수정 → [시험] 측정 시험 → SKIP 0 검사(`:1354~1363` 꼴) → 칸 JSON·trx·restore 로그 업로드(보존 14일).
- 잡 `measure-bo-mysql8` — K3 채택 시 2칸.
- 잡 `compare` — §3-6.
### §6-2 갈래·규모
- 갈래 1(hp-implementer) — 새 파일 6: 워크플로 1 · 측정 시험 1 · `eol_convert.py` · `compare_cells.py` · 대조군 SQL 2. **추정** 합계 500~700줄.
- [3-V] 보안상무: 비번 가림 · 자격증명 모양 0 · 외부 전송 0 · 트리거가 계측 가지 밖에서 안 도는지. [4] 검증팀장: 판 증거 4종 · 줄끝 재확인 · 비교 잡이 「다름」을 실제로 잡는지(심은 차이 1회 — 예: 칸 JSON 하나의 적용 수를 손으로 바꾼 사본을 비교기에 넣어 FAIL 나오는지 · 로컬 파이썬만 · DB 0).
- 런 시간 **추정**: 칸당 15~25분(apt·restore·빌드 ~8 · G-W13-5 ~4 · 측정 ~6) · 4칸 병렬 ⇒ 런 1회 ~25~30분. ⚠️ 실측 전.
### §6-3 런 상한·멈춤
- 상한(K6 추천): **장치 4 · 측정 2 · 합계 6**. 장치 런 = 판·줄끝·DB·빌드·대조군 생존이 서기까지. 측정 런 = 전제 전부 선 뒤 본 측정 1 + 같은 커밋 재현 1.
- 측정 결과 FAIL 은 **재실행 금지** — 그대로 보고(재현 런은 「무효」 일 때만 쓴다).
- 멈추고 PM 보고: ① 제품 나무 단언 실패 ② 바뀐 줄 ≠ 8 ③ 2.5.0 칸 restore/빌드가 죽음 ④ 대조군 ② 가 두 번 죽음 ⑤ G-W13-5 가 어느 칸에서든 빨강(결과로 기록하고 멈춤 · 고치지 않음) ⑥ 측정 시험을 붙이려 기존 시험 파일을 고쳐야 함 ⑦ 새 비밀값이 필요해짐 ⑧ 런 상한 도달 ⑨ 잡 40분 초과.

## §7 [확인 필요] — PM 결정

| K | 쟁점 | 선택지(원문 전부) | 추천 · 근거 |
|---|---|---|---|
| K1 | 초안 PR base | ⓐ `main` — diff 에 #729 변경까지 보임 · 기존 Build & Test·CodeQL 이 PR 에서 또 돎 · 실수 머지 시 #729 가 [4-X] 없이 main 에 들어감 ⓑ #729 가지 `fix/20261030-MySqlConnector-판올림` — diff = 계측 파일만 · 기존 워크플로 안 돎(§2-1) · 실수 머지 시 #729 SHA 가 바뀜 ⓒ PR 없이 가지만 | **ⓑ** — 리뷰가 계측 파일만 보고 런 낭비 0. 실수 머지 위험은 초안 상태 유지 + 제목 「머지 금지」로 막고, ⓐ 의 실수 머지(main 오염)보다 작다. ⓒ 는 결재(초안 PR)와 어긋남 |
| K2 | 측정 시험 자리 | ⓐ `src/HitPan.Tests/Measure/`(계측 가지에만 · 머지 안 함) — 같은 조립품이라 `MigrationGateDb`(internal)를 그대로 씀 ⓑ 레포 밖 새 시험 프로젝트 `tests/measure/x733c/Probe/` — 게이트 원본 파일을 링크 컴파일(`ErpMigrationApplyGateTests.cs`·`ErpCsOriginQueryGateTests.cs`·`DbGateEnvironment.cs` 등 · 끌려오는 파일 수 ⚠️미측정) | **ⓐ** — 근거: 결재 문구는 「**제품** `src/` 0줄」 이고 `HitPan.Tests` 는 게시 6종 어디에도 없다(§2-2) · 가지는 머지되지 않아 main 의 `src/` 는 어느 안이든 0줄 · 같은 조립품이라 하네스 동일성이 가장 높다. 「`src/` 글자 그대로」로 읽으시면 ⓑ |
| K3 | 백오피스 MySQL 8 칸 | ⓐ 0잡(정적 `@`0·`?`0 으로 갈음) ⓑ 2잡(2.5.0·2.6.2 × LF) ⓒ 4잡(× LF·CRLF) | **ⓑ** — 정적상 노출 0 이라 4칸은 과함. 그러나 운영 판의 자체 분리 경로는 MariaDB 칸이 못 재므로 운영 줄끝(LF) 한 줄은 실행 증인을 둔다 |
| K4 | 바이트(0단계) | ⓐ 칸 안 `general_log` ⓑ DB 없이 두 DLL 해석기 직접 호출(내부 형 반사 · 공개 API 로 바이트를 뽑을 수 있는지 미확인) ⓒ 생략(§6-2 FAIL 항목 하나 빠짐) | **ⓐ** — 서버가 실제로 받은 문장이라 반사 0 · 이미 띄운 컨테이너 안에서 끝남 |
| K5 | 2.5.0 칸 NU1902 | ⓐ 억제 없이 경고로 두고 「판 바뀜 증거」로 셈 ⓑ 그 칸 명령줄에만 `-p:NuGetAudit=false` | **ⓐ** — 측정 그래프에 경고=오류 프로젝트 0 · 억제는 제품 규칙과 같은 냄새 |
| K6 | 런 상한 | ⓐ 장치 4 + 측정 2 = 6 ⓑ G1-3 선례 꼴 장치 6 + 측정 4 = 10 ⓒ PM 지정 | **ⓐ** — 행렬 1잡 꼴이 이미 `db-gate` 에서 서 있어 장치 위험이 G1-3(브라우저)보다 작다 |
| K7 | 결과 공개 | ⓐ 칸 JSON·`verdict.md` 를 `docs/검증/외부/` 에 docs PR 로 main 에(HQA 는 푸시된 main 만 읽음) ⓑ Actions 산출물 링크만(14일 뒤 소멸) | **ⓐ** — 해석 주체(HQA·CTO)가 읽을 수 있어야 함 · 산출물 소멸 대비 |
| K8 | 계측 가지 지우는 때 | ⓐ HQA·CTO 대조 끝난 뒤 ⓑ 결과 docs PR 머지 직후 | **ⓐ** — 대조 중 재현 요청이 올 수 있음 |

- 그 밖 ⚠️미측정(결정 아님 · [3] 첫 장치 런에서 확정): 이미지 다이제스트 값 · 2.5.0 restore 실제 경고 수 · 백오피스 게이트가 넘기는 스크립트 폴더 자리 · `general_log` 거르기 규칙 · 런 시간.

## §8 [DoD · 롤백]
### DoD
1. 계측 가지 바닥 = `6b15caf9` · 그 위 커밋의 변경 경로 = 계측 파일만(`git diff --name-only 6b15caf9..HEAD` 원문을 개발명세서에).
2. 전제 전부 선 측정 런 1회 — 칸 JSON 4(+K3) · `verdict.json`·`verdict.md` · trx · restore 로그 업로드 · 런 링크는 **커밋 SHA 고정**.
3. 대조군 ② 생존 기록(2.6.2×LF 칸만 다름) · 대조군 ① 4칸 표 · ②′ 4칸 표.
4. [3-V]·[4] 문서 · [4] 의 「비교기가 심은 차이를 잡는가」 1회.
5. 결과 문서(K7) — 판정 낱말은 「같다 / 다르다 / 무효」 만 · 해석 0 · HQA·CTO 대조 대기로 넘김.
6. #729 가지·PR 무변경 확인(`git ls-remote` 로 `6b15caf9` 그대로).
### 롤백
- 초안 PR 닫기(머지 없음) → 계측 가지 원격 삭제(K8 시점) → scratchpad worktree 제거. 워크플로는 그 가지에만 있으므로 가지와 함께 사라진다. main·#729·운영 영향 0. Actions 산출물은 14일 뒤 자동 소멸(K7 ⓐ 면 문서에 남음).

## §PM PM 결재 (브라운킴 · 10/30)
- 🟢 **결재** — 대표 결재 (c) ① 의 ㉡ 범위 그대로 · 새 워크플로 1 + 측정 시험 · 계측 가지 `measure/20261030-x733c-mysqlconnector`(바닥 `6b15caf9`)에만 · **#729 가지 커밋 0** · 기존 워크플로·잡 0줄 · 제품 게시 프로젝트 0줄 · 이 PC DB 0 · 운영 0.
- 결정: **K1 = ⓑ**(#729 가지 base · 초안 · 제목 「계측용 · 머지 금지」) · **K2 = ⓐ**(`src/HitPan.Tests/Measure/` — 시험 프로젝트 · 게시 6종 밖 · 계측 가지에만 · 머지 0. 대표 결재 문구 「제품 `src/` 0줄」 의 제품 = 게시 프로젝트로 읽음 → 상신 때 한 줄로 밝힘) · **K3 = ⓑ** · **K4 = ⓐ** · **K5 = ⓐ** · **K6 = ⓒ PM 지정: 장치 2 + 측정 2 = 4**(대표께 「CI 런 약 1~2회」 로 보고했다 — 4 를 넘기면 멈추고 대표 보고) · **K7 = ⓐ** · **K8 = ⓐ**.
- 🔴 조건:
  - ① 대조군 ②(합성 문장)가 **2.6.2 × LF 칸에서만** 다르지 않으면 그 런은 무효(장치 고장) — 결과를 「같음 = 안전」 으로 읽지 않는다.
  - ② 판 증거 4종(diff 8파일 8줄 · `project.assets.json` · 실제 올라온 조립품 판 · NU1902 수)이 하나라도 빠지면 그 칸 무효.
  - ③ DB 비번은 런 안 난수 · 가림 등록 · 로그·산출물에 자격증명 모양 0.
  - ④ 측정 시험의 깨짐 집합 ≠ G-W13-5 결과면 장치 고장으로 멈춤.
- 순서: [3] 구현(계측 가지 · 개발명세서) → [3-V]·[4] 둘 다 🟢 → 푸시(= 런 1) → 판독 → 결과 docs PR → #733 HQA 대조 → CTO [5] → 대표 #729 머지 상신.
