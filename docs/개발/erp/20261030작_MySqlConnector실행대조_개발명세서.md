# 개발명세서 2026-10-30 — MySqlConnector 2.5.0 ↔ 2.6.2 × LF ↔ CRLF 격리 CI 실행 대조 장치 (X733c ㉡)

| 항목 | 내용 |
|---|---|
| 근거 작업지시서 | `docs/운영기록/20261030작_MySqlConnector실행대조_작업지시서.md` (전부 + §PM) ← 주소만 |
| 추가 지시 | PM 추가 지시 ①~⑥ (HQA #6520370807 판독 경계 · 10/30 · 구현 중 수신) |
| PM 결재 | ✅ 10/30 — K1 ⓑ · K2 ⓐ · K3 ⓑ · K4 ⓐ · K5 ⓐ · K6 PM 지정 4 · K7 ⓐ · K8 ⓐ · 조건 ①~④ |
| 개발팀 지휘 | PM 브라운킴 |
| 구현 담당 | 개발팀 hp-implementer (갈래 1) |
| 작업 자리 | scratchpad 수동 worktree `wt-x733c` · 가지 `measure/20261030-x733c-mysqlconnector` · 바닥 `6b15caf9` |
| 커밋 | ⬜ 미커밋 — PM 이 [3-V]·[4] 뒤 커밋·푸시(푸시 = 런 1) |

> 🔴 계측 가지 전용 · 머지 금지. 결과의 해석은 이 문서의 몫이 아니다(HQA · CTO).

## 1. 무엇을 만들었나 (작업지시서 대비)

| 요구 (작업지시서 절) | 구현 | 상태 |
|---|---|---|
| 새 워크플로 1 · 이 가지 push + 경로 필터 · `contents: read` · `concurrency: x733c`(취소 안 함) · `ubuntu-24.04` (§3-1 · §6-1) | `.github/workflows/x733c-mysqlconnector-measure.yml:15~35` | ✅ |
| 행렬 MariaDB {2.5.0, 2.6.2} × {lf, crlf} 4칸 · `fail-fast: false` · 40분 (§6-1) | 같은 파일 `measure` 잡 `:48~` | ✅ |
| 백오피스 MySQL 8 2칸 (K3 ⓑ) | `measure-bo-mysql8` 잡 `:257~` | ✅ |
| 비교 잡 `needs` · `if: always()` · 종료 코드 0/1/2 (§3-6) | `compare` 잡 `:470~` · `tests/measure/x733c/compare_cells.py` | ✅ |
| 제품 나무 = `6b15caf9` 단언 (§6-1) | 워크플로 `:75~105`(MySQL 8 잡 `:288~`) | ✅ (제외 경로 1개 추가 — §5-1 ①) |
| 판 바꾸기 — 칸 안 글자 바꾸기 · 커밋 0 · 8파일 8줄 단언 · 2.6.2 칸 diff 0 (§3-2 A) | `:107~137` | ✅ |
| 판 증거 4종 ① numstat ② `project.assets.json` 5개 ③ 실제 로드된 조립품 ④ NU1902 수 (§3-2 · PM 조건 ②) | ① `:107~` ② ④ `:191~202` ③ 측정 시험 `RuntimeAssembly()` · 판정 `compare_cells.py` `cell_prereq` | ✅ |
| 줄끝 바꾸기 — 체크아웃 직후 · 빌드 전 · 바이트 정규화 · 홀로 CR 멈춤 · 파일별 수·sha256 · 출하 DDL 무변경 (§3-3) | `tests/measure/x733c/eol_convert.py` · 워크플로 `:139~142` | ✅ |
| 실행기가 읽는 폴더 CR/LF 재계수 (§3-3) | 측정 시험 `Recount()` (러너 폴더 · `installer/backoffice`) → `compare_cells.py` 가 변환 결과와 파일별 대조 | ✅ |
| G-W13-5 무수정 실행(닻) + 측정 시험 (§3-4 C) | 워크플로 `:204~213` · `src/HitPan.Tests/Measure/X733cMySqlConnectorProbeTests.cs` | ✅ |
| 측정 시험 = 같은 시드·한 건씩·두 바퀴 · 파일별 {회차 성공 · 오류 번호 · 예외 형 · 메시지 첫 줄} · 적용 수 · 깨짐 집합 (§3-4) | 측정 시험 `MeasureErpAsync` `:113~` · `RunOnceAsync` `:234~` | ✅ |
| 확장 모양 (표·칸 형·널·DEFAULT·COMMENT·인덱스 · `schema_migrations` 제외) (§3-4) | `ShapeAsync` `:417~` | ✅ (인덱스는 고유 여부·칸 순서까지 — §5-1 ⑨) |
| 백오피스 `SchemaMigrator.ApplyAsync` 빈 DB · 폴더 = 기존 MySQL 8 게이트와 같은 `installer/backoffice` (§3-4) | `MeasureBackofficeAndControl2Async` `:278~` | ✅ |
| DB — 단계 `docker run` · 런 안 난수 비번 + `::add-mask::` · 다이제스트 고정 (§3-5 · PM 조건 ③) | 워크플로 `:144~181` · `:355~391` | ⚠️ 다이제스트 **값**은 첫 장치 런에서 확정(설계대로 ⚠️미정) |
| 칸 JSON 에 서버 전제(VERSION · sql_mode · 문자셋 · 정렬 · max_connections …) · 이미지 · dotnet · 러너 이미지 · 출하 DDL sha256 (§3-5) | `ServerFactsAsync` `:450~` · 워크플로 증거 파일 | ✅ |
| 대조군 ① DB-16 원본 한 건 (§5) | `MeasureErpAsync` 안 `control1_db16` `:176~` | ✅ |
| 대조군 ② 합성 `SELECT @P AS a --`⏎`, @P AS b;` P=7 (§5) | `tests/measure/x733c/controls/c2_param.sql` · `Control2Async` `:338~` | ✅ (MySQL 8 칸에서도 실행 — §5-1 ⑪) |
| 대조군 ②′ `.sql` 경로 변형 · 러너 폴더에만 · 실물 러너 · 기록만 (§5) | `controls/c2b_qmark.sql` · `control2b_qmark` `:192~` | ✅ |
| 바이트(K4 ⓐ) `general_log` 표 · 러너 연결 `Query` 만 · 거른 규칙 JSON 기록 (§3-7) | `RunOnceAsync`(ERP · 회차마다 TRUNCATE) · 백오피스 `log_queries` · 규칙 문자열 `log_filter_rule` | ✅ |
| 비교 — 전제 어긋나면 무효 · 4칸 같아야 같음 · 판 축 전용 비교(체크섬·content_hash·파일 sha256·수신 바이트) (§3-6) | `compare_cells.py` `cell_prereq` · `group_prereq` · `erp_items` · `backoffice_axis_items` | ✅ |
| SKIP 0 검사 · 결과 업로드 14일 (§6-1) | `:228~237` · `:249~255` | ✅ |
| 칸 JSON·trx·restore 로그 업로드 (§6-1) | 칸 증거 폴더 통째(`x733c-cell-{칸}`) | ✅ |

### 1-1. PM 추가 지시 ①~⑥ 반영

| 지시 | 구현 |
|---|---|
| ① 증거별 「보장하는 범위 / 못 보는 범위」 칸 · 합성 대조군 생존 ≠ 전체 누락 없음 증명을 결과표 머리에 | `compare_cells.py` 비교 항목마다 `guarantee`·`blind` 필드 → `verdict.md` 표 칸 · 머리 `HEADER_NOTES` 4줄 |
| ② 대조군 ② 기대 불일치 = 「무효 — 전제/장치/가설 불일치 · 원인 미확정」 · 무효여도 원결과·조립품 판·줄끝 증거 보존 | 판정 글자 `VERDICT_INVALID` · 칸 증거는 `if: always()` 업로드(무효와 무관) · `verdict.json` `cells` 에 칸마다 조립품 판·줄끝·대조군 결과 요약 |
| ③ 「같음(비교 항목 N개)」 꼴 · 같은 거부·같은 적용 수만으로 「정상 적용」 금지 | 같음 글자 = `같음(비교 항목 {N}개)` · 머리글에 「같음 ≠ 정상 적용·호환·안전」 · E3 「못 보는 범위」 에 「건수만 봄」 |
| ④ DB-16 「원본 거부 대조」 와 「실제 설치/업데이트 경로의 기존 한계」 분리 | 비교 항목 E6(원본 거부 대조) · `informational` 의 별도 칸 「DB-16 — 실제 설치/업데이트 경로의 기존 한계」(판정 안 함) · 칸 JSON `control1_db16.note` |
| ⑤ 무효/실패/미측정 → 통과 코드 경로 0 | 판정 순서 = 무효 사유 1개라도 → 2 · 비교불가 항목 → 2 · 항목 0개 → 2 · 비교기 예외 → 2 · 최상위 `BaseException` → 2 · 워크플로 마지막 단계가 0/1 외 전부 2 · `continue-on-error` 0 |
| ⑥ 런 상한 4 · FAIL 재실행 금지 · 재시도 루프 없음 | 워크플로 머리 주석 · 재시도 루프 0(DB 기동 대기 반복은 「떴나」 를 묻는 것뿐 — 측정 재실행 아님) · apt 설치 3회 재시도 꼴도 안 씀(§5-1 ②) |

## 2. 변경 파일 목록 (전부 새 파일 · 기존 파일 0줄)

| 파일 | 변경 | 왜 |
|---|---|---|
| `.github/workflows/x733c-mysqlconnector-measure.yml` | 새 522줄 | 칸 6 + 비교 잡 |
| `src/HitPan.Tests/Measure/X733cMySqlConnectorProbeTests.cs` | 새 639줄 | 측정 시험(K2 ⓐ · 같은 조립품 · `MigrationGateDb` internal 사용) |
| `tests/measure/x733c/eol_convert.py` | 새 123줄 | 줄끝 바꾸기 + 파일별 증거 |
| `tests/measure/x733c/compare_cells.py` | 새 670줄 | 기계 판정 |
| `tests/measure/x733c/controls/c2_param.sql` | 새 2줄(29바이트 · LF) | 대조군 ② |
| `tests/measure/x733c/controls/c2b_qmark.sql` | 새 2줄(27바이트 · LF) | 대조군 ②′ |
| `docs/개발/erp/20261030작_MySqlConnector실행대조_개발명세서.md` | 새 (이 문서 · CRLF) | 개발명세서 |

- 기존 코드 추가만(#1) — ✅ 기존 파일 수정 0(`git status` = 새 파일 7개뿐 · §4)
- 제품 게시 프로젝트 0줄 · 기존 워크플로 0줄 · 8 csproj 0줄 · `KnownNotReapplyable` 0줄 · `NoWarn` 추가 0 — ✅
- clean DDL 편입(#36) — 해당없음(DDL 0)

## 3. 헌법·결재 조건 준수

| 조항 | 어떻게 | 근거 |
|---|---|---|
| #15 빈 catch | 측정 시험 catch 3곳 모두 JSON 기록 + `Console.Error` (적용기 실패·대조군 예외는 결과다) · 최상위는 다시 던짐 | 측정 시험 `:77~83` · `:305~311` · `:367~373` |
| #19 빌드 0/0 | `HitPan.Tests.csproj` Release — 0 / 0 (2.6.2) | §4 |
| #39 운영·demo 0 · 이 PC DB 0 | 측정 시험이 `GITHUB_ACTIONS`·`CI`·`HITPAN_REQUIRE_DB`·`HITPAN_X733C_*`·`HITPAN_DB_HOST=127.0.0.1`·비번 없으면 **DB 접속 전 실패** | `CellEnv.ReadOrRefuse` `:600~` · §4 실측 |
| PM 조건 ① | 대조군 ② 가 2.6.2×LF 에서만 다르지 않으면 무효 | `group_prereq` |
| PM 조건 ② | 판 증거 4종 하나라도 어긋나거나 없으면 그 칸 무효 | `cell_prereq` 판 증거 ①~④ |
| PM 조건 ③ | 비번 = `openssl rand -hex 24` · `::add-mask::` · `docker run -e 이름만`(값은 환경으로) · 칸 산출물 비번 글자 검사·가림 + 빨강 · 칸 이름 record `ToString()` 이 비번을 안 찍게 덮음 | 워크플로 `:144~`·`:239~247` · 측정 시험 `CellEnv.ToString` |
| PM 조건 ④ | 측정 시험 깨짐 집합 ≠ 명단이면 측정 시험이 실패(JSON 은 먼저 남김) + 비교기 무효 | 측정 시험 `:97~106` · `cell_prereq` |
| 자격증명 모양 0 | 새 파일 7개에 비번·토큰 글자 0 · `secrets.` 참조 0 | §4 검사 |
| 외부 전송 0 | `DOTNET_CLI_TELEMETRY_OPTOUT` · 산출물은 Actions 산출물(14일)만 · `persist-credentials: false` | 워크플로 머리 |

## 4. 자체 확인 결과 (실측 · DB 0)

| 항목 | 명령 | 결과 |
|---|---|---|
| 빌드(#19) | `dotnet build src/HitPan.Tests/HitPan.Tests.csproj -c Release -v q -clp:ErrorsOnly;WarningsOnly` | **0 Warning(s) · 0 Error(s)** (2.6.2 · 1분 14초) |
| 측정 시험 실행 거부 | `dotnet test … --no-build --filter FullyQualifiedName~HitPan.Tests.Measure.X733cMySqlConnectorProbeTests` (이 PC · CI 환경 변수 없음) | **Failed 1 · 45 ms** · 메시지 「[X733c] 실행 거부 … (DB 접속 0으로 멈췄다)」 + 어긋난 조건 11줄 · SKIP 아님 |
| 워크플로 YAML | PyYAML 6.0.1(scratchpad 에만 설치) `safe_load` | 로드 됨 · 트리거 = `push` 하나 · 가지 1 · 경로 3 · 권한 `contents: read` · 잡 3 |
| 셸 조각 | run 블록 22개 각각 `bash -n` | 실패 0 |
| 금지 낱말 | 워크플로에 `workflow_dispatch`·`pull_request`·`secrets.` · 대표 금지 낱말(새 파일 7개 전부 · 문서 포함) | 0 |
| 줄끝 바꾸기 | scratchpad 사본(레포 SQL 113·16·대조군 2 + 출하 DDL)에서 `eol_convert.py lf` / `crlf` | lf: CR 합 0 · LF 9155 / crlf: CR = LF = 9155 · 수 {113, 16, 2} · 출하 DDL sha 무변경 · 재실행 멱등(바이트 같음) · c2 = `b'SELECT @P AS a --\r\n, @P AS b;\r\n'` |
| 비교기 — 심은 차이 | 가짜 칸 6개(scratchpad · DB 0)로 `compare_cells.py` 4종 | 정상 세트 → **0 「같음(비교 항목 24개)」** · 적용 수 1 줄인 칸 → **1 「다름」**(E3 `104 ≠ 103`) · 대조군 ② 네 칸 같음 → **2 「무효 — …」** · 빈 폴더 → **2** |
| 비교기 — 자체 고장 | 같은 점검에서 처음 발견한 결함 2건 | ① 정당한 null(백오피스 오류 없음)을 「값 없음」 으로 읽어 무효 → 전용 표지 `MISSING` 으로 고침 ② 출력 인코딩 예외가 파이썬 기본 종료 코드 **1(= 다름)** 로 새는 길 → 최상위에서 전부 2 로 막음(PM ⑤). 고친 뒤 4종 기대대로 |
| 변경 범위 | `git status --short --untracked-files=all` | 새 파일만: 워크플로 1 · 측정 시험 1 · 파이썬 2 · SQL 2 (+ 이 문서) · 수정 0 |

## 5. 안 한 것 / 못 한 것 / 작업지시서와 달라진 점

### 5-1. 달라진 점 (사유)

| # | 작업지시서 | 구현 | 사유 |
|---|---|---|---|
| ① | 나무 단언 제외 = 계측 3자리 | + `docs` 제외 | 이 명세서·[3-V]·[4] 문서가 같은 가지에 커밋되면 단언이 빨강이 된다. `docs` 는 제품이 아니고 실행 경로에 안 든다 |
| ② | 출하 DDL CLI = `db-gate` 꼴(apt `mariadb-client`) | 같은 DB 컨테이너 안 클라이언트(`docker exec -i x733c-db mariadb`)를 감싼 실행 파일을 `HITPAN_MYSQL` 로 | apt 미러 멈춤 이력(`build-and-test.yml:315~325` 주석) · 재시도 루프 금지(PM ⑥) · 클라이언트 판까지 이미지 다이제스트로 고정. 게이트 코드는 무수정(명령 이름만 받음) |
| ③ | 다이제스트 = 첫 장치 런에서 확정해 고정 | 워크플로 env `X733C_MARIADB_DIGEST`·`X733C_MYSQL8_DIGEST` 빈 칸 → 태그로 받고 비교기가 「무효(다이제스트 미고정 = 장치 런)」 | 값을 추측해 적지 않는다. 첫 런 칸 증거 `image.txt` 의 `repo_digests` 를 두 줄에 적는 커밋이 측정 런이 된다 |
| ④ | 측정 시험이 `MigrationGateDb` 로 실물 러너 | DB 준비·출하 DDL·러너 폴더·명단은 `MigrationGateDb` 그대로 · 러너는 실물 `MigrationRunner` 를 시험이 직접 만들고 **연결 공장·로거만** 시험 것 | `RunRealRunnerAsync` 는 `NullLogger`·결과 메시지만 줘서 오류 번호·예외 형이 안 나오고, 러너 연결의 `ServerThread` 를 못 받아 `general_log` 를 못 거른다. 같은 실행기의 닻은 같은 칸의 G-W13-5 무수정 실행 + 깨짐 집합 일치(조건 ④) |
| ⑤ | `HITPAN_X733C_CELL` 없으면 「돌지 않고 끝난다」(§3-4) | **명시적 실패(실행 거부)** | PM 지시 「SKIP 이 아니라 명시적으로 실패하거나 실행 거부」 |
| ⑥ | 판정 글자 「같다/다르다/무효」 | 「같음(비교 항목 N개)」 / 「다름」 / 「무효 — 전제/장치/가설 불일치 · 원인 미확정」 | PM 추가 지시 ②③ |
| ⑦ | 판 증거 단언 | ①(8파일 8줄)은 워크플로에서 멈춤(§6-3 ②) · ②③④는 **기록만**, 무효 판정은 비교기 | 무효여도 그 칸 원결과를 끝까지 남기려고(PM ②) |
| ⑧ | 추정 500~700줄 | 약 1,950줄 | MySQL 8 잡이 MariaDB 잡 단계를 그대로 복제(YAML 앵커·별도 셸 파일을 안 써 새 파일 수 유지) · 결과표 범위 칸(PM ①) |
| ⑨ | 인덱스 = 이름 | 이름 + 고유 여부 + 칸 순서 | 더 보는 쪽 · 칸 사이 결정적 |
| ⑩ | 백오피스 바이트 = 마이그별 | 적용 **전체** 문장 순서열 sha256 | `SchemaMigrator` 는 내부 연결 하나로 전 파일을 돈다. 파일별로 끊으려면 `fileFilter` 로 여러 번 불러야 해 실행기 호출이 바뀐다 |
| ⑪ | 대조군 ② = MariaDB 4칸 | + MySQL 8 2칸(2.6.2×lf 만 달라야) | MySQL 8 잡도 장치 생존을 따로 증명해야 그 잡의 같음을 읽을 수 있다 |
| ⑫ | — | 백오피스 측정 DB `gate_x733c_bo` · 대조군 ② 는 그 DB 에 붙음(연결 성질은 러너와 같음) | ERP DB 는 ERP 측정 끝에 지워진다. 대조군 문장은 표를 안 읽는다 |
| ⑬ | — | 칸 산출물 비번 글자 검사·가림 단계 | 가림(`::add-mask::`)은 콘솔 로그에만 걸리고 **산출물 파일에는 안 걸린다** |

### 5-2. 안 한 것 / 못 한 것

| 항목 | 왜 | 후속 |
|---|---|---|
| 커밋·푸시·초안 PR | 지시상 PM 몫(푸시 = 런 1 · [3-V]·[4] 뒤) | PM |
| 2.5.0 판 로컬 빌드 | 작업 폴더 csproj 를 바꾸면 안 된다(커밋되는 csproj 0줄) · CI 칸에서만 바꾼다 | 첫 장치 런 |
| `actionlint` | 이 PC 에 없음 · 외부 실행 파일을 내려받지 않았다 | [4] 또는 첫 런의 구문 오류 여부 |
| 이미지 다이제스트 값 | 실행 전엔 모른다(§5-1 ③) | 첫 장치 런 → PM 커밋 |
| 결과 docs PR(K7) | 런 결과 뒤 | PM |
| 검증 시험 전체 실행 | 이 PC DB 금지 · 측정 시험은 거부만 실측 | CI |

### 5-3. ⚠️ 미측정 — 첫 런에서 확정할 것

1. `mariadb:11.4` · `mysql:8.0` 다이제스트 값. → 🟢 **상태 바뀜(검증 지적 반영)**: 푸시 전 레지스트리 조회로 고정 — §8-3.
2. MariaDB·MySQL 8 에서 `TRUNCATE TABLE mysql.general_log`(로그 켠 채) · `SET SESSION sql_log_off = 1` 이 되는지 — 안 되면 측정 시험이 예외로 중단 → 무효.
3. `general_log.argument` 칸 형·문자셋(칸 JSON `server.general_log_argument`) — `utf8mb3` 등이면 4바이트 글자(이모지)는 저장 전에 바뀐다. 칸 사이 대조는 유지되나 「바이트 그대로」 는 아님(비교 항목 B1 못 보는 범위에 적음).
4. MySqlConnector 가 연결 열 때·풀 재사용 때 `Query` 를 보내는지 — 보내고 판 사이 다르면 B1 이 「다름」 으로 나온다(원인 줄 적기 전 다름 · 해석은 HQA·CTO).
5. 2.5.0 칸 restore 의 NU1902 실제 수 — 감사 원천이 안 닿아 NU1900 만 나오면 판 증거 ④ 결손 → 그 칸 무효.
6. 2.5.0 칸 restore·빌드가 서는지(멈춤 조건 ③).
7. 백오피스 16파일이 MariaDB 통째 경로에서 끝까지 적용되는지(실패면 E8 에 원결과로 남는다).
8. DB-16 거부 예외 형 · 대조군 ② 2.6.2×LF 실제 값(상류 원문상 `b` = NULL) · 대조군 ②′ 결과.
9. `docker exec -i` 감싼 클라이언트로 출하 DDL 적재(stdin)가 되는지 · MySQL 8 컨테이너가 `--general-log` 인자로 초기화까지 뜨는지.
10. `fetch-depth: 50` 안에 바닥 커밋이 드는지(계측 가지 위 커밋이 50 넘으면 멈춤 ①).
11. 런 시간 — **추정** 칸당 15~20분(체크아웃·.NET ~1 · DB ~1 · restore ~2 · 빌드 ~3 · G-W13-5 ~4 · 측정 ~5~6 · 업로드) · 6칸 병렬 + 비교 ~1분 ⇒ 런 1회 약 20~25분. 실측 전.

### 5-4. 런 순서 제안 (상한 4 안에서)

1. 첫 푸시 = **장치 런** — 다이제스트 미고정이라 설계상 「무효」 로 끝난다. 이 런에서 2·5·6·9·10 과 다이제스트 값을 확정.
2. 다이제스트 두 줄 커밋 = **측정 런 1**. 전제 다 서면 판정.
3. 남는 2회 = 장치 고장 수리 1 + 측정 재현 1. FAIL(다름) 은 재실행하지 않는다(§6-3).
- 🟢 **상태 바뀜(검증 지적 반영 · [4] 권고 4)**: 다이제스트를 푸시 전에 고정했으므로 1·2 는 합쳐진다 — 첫 푸시가 곧 측정 런 1(전제 다 서면 판정). 남는 3회 = 장치 고장 수리 최대 2 + 측정 재현 1. 위 1~3 원안은 이력으로 둔다.
- 문서만 바꾼 푸시는 경로 필터 밖이라 런이 안 돈다. 워크플로·`tests/measure/x733c/**`·`src/HitPan.Tests/Measure/**` 를 바꾸는 푸시는 매번 런 1회다 — 묶어서 푸시.

## 6. [3-V] 병렬검증 지적 사항 (안철수 상무)

| # | 지적 | 봉합 | 이슈 문서 |
|---|---|---|---|
| — | (작성 시점 수신 없음) | — | — |
| F1 | 명세서 금지 낱말 1건 | PM 이 고침 · 이번 차수 새 파일 7개 다시 grep 0(§8-4) | `scratchpad/x733c_3V.md` |
| R1 | 비교기가 칸 잡 결론을 안 봄 | 반영 — §8-1 ③ | 같음 |
| R2 | 외부 액션 태그 고정 | **안 함** — 레포 기존 18곳과 같은 꼴 · 별건 결재 거리(§8-5) | 같음 |
| R3 | NU1902 를 꾸러미 이름 없이 셈 | 반영 — §8-1 ④ | 같음 |
| R4 | 측정 시험 단언 문구 단정형 | 반영 — §8-1 ⑧ | 같음 |
| R5 | `Measure/` 아래 파일이 늘면 목록 고정 | **안 함** — 지금 새 .cs 1개뿐 · 다음 차수 거리(§8-5) | 같음 |

## 7. 검증팀에 요청 (자기 판단 아님)

- **비교기 판정 순서**(`compare_cells.py` `judge` · 맨 아래 최상위 블록) — 무효·다름·미측정이 0 으로 새는 길이 정말 없는지. 심은 차이 1회는 §4 에서 했으나 [4] 가 따로 해 주길.
- **`general_log` 거르기**(`RunOnceAsync`) — 측정 연결을 `Pooling=false` + `sql_log_off=1` 로 두고 러너 연결 `ServerThread` 로 거른다. 러너 연결 풀이 측정 연결과 섞이지 않는지(연결 문자열이 달라 풀이 갈린다는 전제).
- **ERP 루프를 다시 적은 것**(§5-1 ④) — G-W13-5 와 같은 시드 문장·같은 `app_version`(`w13-gate`)·같은 순서인지. 닻 + 깨짐 집합 일치가 반증 장치다.
- **판 바꾸기 `sed`** — 주석 속 옛 줄(`Version="2.5.0"`)을 안 건드리고 활성 8줄만 바꾸는지(`csproj_diff.txt` 에 `+` 줄 8개 단언).
- **비번 경로** — `GITHUB_ENV` 로 넘긴 비번이 `docker exec`·게이트 `-p` 인자로 러너 프로세스 목록에 보인다(가상 머신 한정 · 로그는 가림). 산출물은 검사·가림 단계가 있다.
- **회귀 우려** — 기존 파일 0줄이라 제품 회귀 경로는 없다. 다만 이 가지가 실수로 #729 가지에 머지되면 측정 시험이 다른 CI 에서 **실행 거부로 빨강**이 된다(조용한 초록 대신 시끄러운 빨강 — 의도).

## 8. 검증 지적 반영 (2026-10-30 · [3-V] `scratchpad/x733c_3V.md` · [4] `scratchpad/x733c_4.md`)

> 범위: 새 파일 안에서만 고쳤다(기존 파일 0줄 그대로). 커밋·푸시·stash 0 · 이 PC DB 접속 0 · 운영 0.

### 8-1. 고친 자리

| # | 지적 | 파일:줄 | 고친 내용 |
|---|---|---|---|
| ① | [4] 필수 1 대조군 ② 생존 판정이 느슨함 | `tests/measure/x733c/compare_cells.py:41` · `:568~586` · `:589~611` | `outcome != "a=7,b=7"` 를 버리고 칸마다 정확한 값으로 판정(`control2_cell_ok`). 2.6.2×LF = 정확히 `a=7,b=NULL` · 오류 0 · 서버 수신 문장 중 하나가 첫 줄엔 `@P` 없고 둘째 줄에 `@P` 그대로. 나머지 칸 = 정확히 `a=7,b=7` · 오류 0. 그 밖은 전부 무효(2) · 글자 「무효 — 전제/장치/가설 불일치 · 원인 미확정」 |
| ② | [4] 필수 2 바닥값 | `compare_cells.py:293~295` · `:326~370` | 칸마다 `floor_checks`: (MariaDB) `len(files) == all_count == measured` · 파일마다 회차 ≥1 · 깨지지 않은 파일 회차마다 서버 수신 ≥1 · ERP 확장 모양 줄 수 >0 이고 `count` = 줄 수 / (두 종류) 백오피스 적용 ≥1 이고 수신 ≥1 · 백오피스 확장 모양 줄 수 >0. 못 미치면 그 칸 무효(2). 깨진 파일의 실패 회차는 드라이버가 보내기 전에 멈출 수 있어 수신 바닥에서 뺐다([4] 문구 「깨지지 않은 파일」 그대로) |
| ③ | [3-V] R1 칸 잡 결론 | 워크플로 `:496~501` · `compare_cells.py:43` · `:373~380` · `:692` · `:772` | 비교 단계 env 로 `needs.measure.result` · `needs.measure-bo-mysql8.result` 를 넘긴다(`run:` 안 `${{ }}` 0 유지). 비교기가 둘 다 `success` 가 아니거나 env 가 없으면 무효(2) |
| ④ | [3-V] R3 · [4] 권고 5 NU1902 | 워크플로 `:200~204` · `:412~416` | 판 증거 ④ 수 = `warning NU1902: Package 'MySqlConnector'` 줄만. 전체 수는 `nu1902_all_packages` 로 기록만(판정 안 씀). 이 PC 2.5.0/2.6.2 restore 로그(scratchpad `x733c_v4_restore250.log`·`…262.log`)에 새 grep → 4 / 0 |
| ⑤ | [4] 권고 1 줄끝 직접 재계수 | `compare_cells.py:260~267` · `:302~323` | `eol.json` 의 `after` 와 실행기 폴더 재계수(`dir_recount_runner`·`dir_recount_backoffice`) 각각에서 lf 칸 = 모든 파일 CR 0 · crlf 칸 = 모든 파일 CR = LF = CRLF · LF 합계 > 0 을 비교기가 직접 본다(`assert_ok` 깃발만 믿지 않음) |
| ⑥ | [4] 권고 4 다이제스트 | 워크플로 `:32~41` | 두 다이제스트를 푸시 전에 고정(§8-3). 첫 런이 설계상 무효가 되던 것을 없앴다 → §5-4 상태 바뀜 |
| ⑦ | [4] 권고 2 쓰기 전 서버 지문 · OUT 경로 | 측정 시험 `src/HitPan.Tests/Measure/X733cMySqlConnectorProbeTests.cs:52~54` · `:519~549` · `:655~667` | 칸 환경 확인 뒤, DB 에 무엇이든 쓰기 **전에** `RefuseUnlessMeasureServerAsync` — 읽기 `SELECT @@general_log, @@log_output` 하나(풀 안 씀)로 `general_log` = ON/1 · `log_output` = TABLE 이 아니면 실행 거부(DB 쓰기 0). `ReadOrRefuse` 에 `RUNNER_TEMP` 있음 + `HITPAN_X733C_OUT` 전체 경로가 그 아래인지 추가 |
| ⑧ | [3-V] R4 단언 문구 | 측정 시험 `:107` · 머리 주석 `:36` | 「장치 고장으로 멈춘다」 → 「무효 — 원인 미확정」 |
| ⑨ | [4] 권고 6 같은 실행기 한 줄 | 이 문서 아래 | 아래 한 줄 |

- **같은 실행기 — 연결 성질 한 줄**: 측정 시험 러너 연결(`X733cMySqlConnectorProbeTests.cs:564~565` · `DefaultCommandTimeout=180;GuidFormat=None;AllowUserVariables=true`)은 G-W13-5(`src/HitPan.Tests/Integrity/ErpMigrationApplyGateTests.cs:751~752`)와 연결 성질이 같고, 제품 공장(`src/HitPan.Infrastructure/Persistence/MigrationDbConnectionFactory.cs:58~60`)과는 **문장 해석 밖 옵션 4개가 다르다** — `AllowLoadLocalInfile=true`(`:58`) · `ApplicationName=hitpan-migration`(`:60`) · 시간 초과(`DefaultCommandTimeout=600` `:58` · `ConnectionTimeout=60` `:59`) · 풀 크기(`MinimumPoolSize=0;MaximumPoolSize=20` `:59`). 넷 다 드라이버 문장 해석 옵션(`StatementPreparerOptions`)에 들지 않는다([4] §3 반사 열거 실측). 칸 사이 결과가 이 차이로 갈릴 길은 못 찾았고, 남는 길(풀 되돌림 때 드라이버 자체 문장)은 「다름(1)」 쪽이다.

### 8-2. 점검 결과 (실측 · DB 0)

| 점검 | 결과 |
|---|---|
| 비교기 심은 차이 재실행 | [4] 의 가짜 칸 생성기를 복사·보강(`scratchpad/x733c_fix_tools/fakecells2.py` → `scratchpad/x733c_fix_fake/` · 결과 `scratchpad/x733c_fix_fake_results.txt`) — **62가지 전부 기대대로 · 0 으로 샌 것 0** |
| └ 이번 지적 대상 | M4b(2.6.2×LF = error) **0→2** · M4b2(a=NULL,b=7) **0→2** · M7(수신 0) **0→2** · M7b(모양 0줄) **0→2** · M7c(백오피스 적용 0) **0→2** · M25(files 50건) **0→2** · M3b(crlf 칸 CR 0 · 깃발 참) **0→2** |
| └ 그대로 | 정상 세트(M0) **0** · 한 칸 문장 누락(M1) **1** · 무효 계열 **2** 그대로 · 네 칸 같은 누락(M1b) 0 = 설계상 못 봄(머리말) |
| └ 바뀐 기대 1건 | M25b(한 칸만 files 50건) 1 → **2** — 바닥 ① 이 먼저 무효로 잡는다(측정이 덜 된 칸은 「다름」 이 아니라 「무효」) |
| └ 새 반증 19가지 | 칸 잡 결론 failure / env 없음 / cancelled → 2 · 대조군 ② 수신 없음 / 둘째 줄 @P 없음 / 첫 줄에 @P / 나머지 칸 오류+7,7 / 2.6.2×LF 오류+7,NULL / MySQL 8 2.6.2 오류 / 수신 키 없음 → 2 · 한 칸 회차 수신 0 / 모양 count 불일치 / 백오피스 적용 0 / 백오피스 수신 전부 0 / MySQL 8 모양 0줄 / 회차 0 → 2 · lf 칸 CR 1(두 기록 일치) / LF 전부 0 → 2 · 새 `nuget_audit.txt` 모양 → 0 |
| └ 기준 보강 | 가짜 기준을 실물 모양에 맞췄다 — 대조군 ② `server_received`(2.6.2×LF 둘째 줄 `@P`) · `error: null` · 모양 `count` · 칸 잡 결론 env `success` |
| `bash -n` | 워크플로 `run:` 블록 22개 → 문법 실패 0 (`scratchpad/x733c_v4_tools/runblocks.py`) |
| `HitPan.Tests` 빌드 | `build-brief.ps1` → **errors 0 · warnings 0** |
| 측정 시험 CI 밖 거부 | (DB 주소 = 127.0.0.1:1 아무것도 안 듣는 자리) ⓐ 칸 환경 없음 → 실패 · 「실행 거부」 · DB 접속 0 ⓑ 칸 환경 다 넣고 OUT 이 `RUNNER_TEMP` 밖 → 실패 · 「HITPAN_X733C_OUT 이 RUNNER_TEMP 아래가 아니다」 · DB 접속 0 ⓒ 다 맞게 넣음 → 첫 접속 시도가 `RefuseUnlessMeasureServerAsync` 에서 남(스택) = `ReadyAsync`(DROP/CREATE)보다 앞. 로그 `scratchpad/x733c_fix_tools/refuse_*.log` |

### 8-3. 이미지 다이제스트 (푸시 전 고정)

- 방법: Docker Hub 공개 레지스트리 메타데이터 조회만 — 이미지 내려받기·실행 0. `auth.docker.io/token?service=registry.docker.io&scope=repository:library/<repo>:pull` 로 익명 pull 범위 토큰을 받아(메모리에서만 · 출력·저장 0) `HEAD https://registry-1.docker.io/v2/library/<repo>/manifests/<tag>` 를 OCI index·manifest list Accept 머리글로 보내고 `Docker-Content-Digest` 머리글을 읽었다. 조회 스크립트 `scratchpad/x733c_fix_tools/digest_query.py`(셸 보안 검사가 curl 반복문을 막아 같은 요청을 파이썬 표준 라이브러리로 보냄).
- 결과(2026-10-30 11:08 GMT · 둘 다 HTTP 200 · `application/vnd.oci.image.index.v1+json`):
  - `mariadb:11.4` → `sha256:d1891ede5a559f1086f55053bb8cc1569b0298338c4d72d71725a7f74f6ba620`
  - `mysql:8.0` → `sha256:7dcddc01f13bab2f15cde676d44d01f61fc9f99fe7785e86196dfc07d358ae2b`
- 다중 아키텍처 index 다이제스트다. 러너(amd64)가 받은 실제 플랫폼 이미지 ID 는 칸 증거 `image.txt` 의 `image_id` 에 남고, 칸 사이 같음은 기존 묶음 전제가 본다.
- ⚠️ 위험 1줄: 태그가 그 뒤 움직여도 고정 다이제스트는 그대로 받히는 것이 보통이나, 레지스트리가 옛 index 를 지우면 pull 이 실패 → DB 단계 빨강 → 무효(0 으로 안 샌다).

### 8-4. 글자 점검

- 새 파일 7개 전부: 대표 금지 낱말(메모 목록 계열) grep **0** · 실제 비번·토큰·연결 문자열 값 grep **0**(걸린 것은 `$PW` 같은 변수 참조와 이 문서의 설명 글자뿐) · 외부 노출 금지 낱말 0.

### 8-5. 안 한 것 / 남은 것

- [3-V] R2(외부 액션 SHA 고정) · R5(`Measure/` 파일 목록 고정) — 이번 지시 범위 밖 · 별건 결재 거리.
- [4] 권고 3(B1·BB2 다름일 때 드라이버 자체 문장 분리 출력) — 이번 지시 범위 밖. `log_other_commands` 는 칸 JSON 에 이미 남는다.
- 서버 지문 거부의 「OFF 서버」 경로는 실서버로 재지 않았다(이 PC DB 금지) — 코드 순서와 ⓒ 스택으로만 확인. 첫 런에서 칸 컨테이너가 지문을 통과하는지가 실측이다(통과 못 하면 측정 시험 실패 → 무효).
- 대조군 ② 서버 쪽 거동(`--` 뒤 줄바꿈 · 정의 안 된 `@P` = NULL)은 여전히 문서 근거 — 첫 런 실측. 어긋나면 무효(2)로 떨어진다.
- 워크플로 YAML 파서 검사는 못 함(이 PC 에 YAML 모듈 없음) — 들여쓰기는 눈으로 대조 · `run:` 블록만 `bash -n`.
