# X733c MySqlConnector 격리 CI 실행 대조 장치 — [4] 작업리뷰서 (검증팀장 데이비드 박)

- 대상: 작업 폴더 `wt-x733c` 새 파일 7개(커밋 전 · CI 미실행) · 바닥 `6b15caf9`
- 근거: 작업지시서 `20261030작_MySqlConnector실행대조_작업지시서.md` §3·§5·§6-3·§PM(K6 = 장치 2 + 측정 2 = 4 · 조건 ①~④) · 정적 회신 §2-1·§4·§6-2
- 방법: 대상은 읽기만 했다. 실험은 사본 `scratchpad/x733c_v4_copy`(바닥 커밋 clone + 새 파일 7개 복사)와 `scratchpad/x733c_v4_tools`·`x733c_v4_fake` 에서만 했다. 이 PC DB 접속 0(가짜 TCP 수신기만 씀) · 운영 0 · 코드 수정·커밋·푸시·stash 0.
- 참고: 개발명세서 `:91` 금지 낱말 1건은 PM 이 이미 고쳤다([3-V] 필수). 이 리뷰는 그 줄을 다시 보지 않았다.

## 판정: 🟡 조건부 — 필수 2건을 고치기 전에는 푸시(= 런 1)하지 않는다

비교기가 「같음(0)」을 내지 말아야 할 경우 가운데 **두 갈래(모양 5개)가 0 으로 샌다.** 장치 뼈대(판 바꾸기 · 줄끝 · 판 증거 4종 · 실행 거부 · 무효 처리)는 실측으로 섰다.

---

## 1. 설계 대조 — 고정 항목이 코드에 실제로 있는가

| 항목 | 코드 위치 | 판정 |
|---|---|---|
| 같은 SQL 집합 | 워크플로 `:104` `git ls-tree` → `sqllist.txt` · 비교기 `group_prereq` 「SQL 목록 해시」·「출하 DDL sha256」 칸 사이 같음 · 수 113/16/2 단언 `compare_cells.py:226~229` | 🟢 |
| 같은 실행기 | 측정 시험 `:131~133` 실물 `MigrationRunner` · 백오피스 실물 `SchemaMigrator.ApplyAsync` · 닻 = G-W13-5 무수정(워크플로 `:211`) · 깨짐 집합 = 명단 단언 `X733c…Tests.cs:102` | 🟢 (§3 단서) |
| 같은 DB 초기 상태 | 칸마다 새 컨테이너 · 출하 DDL CLI 적재(`MigrationGateDb.ReadyAsync` `:117`) · 다이제스트 고정 여부 `compare_cells.py:277` · 서버 전제 10개 칸 사이 같음 | 🟢 |
| PASS/FAIL | 0 같음 / 1 다름 / 2 무효 · `compare_cells.py:603~` · 최종 단계 `:515~521` (빈 값 → 2) · 잡히지 않은 예외 → 2 `:667` | 🟢 |
| 대조군 ① DB-16 | 측정 시험 `:176~193` · 비교 항목 E6 | 🟢 |
| 대조군 ② | `controls/c2_param.sql` · `Control2Async` · 기대 판정 `compare_cells.py:35~36·475~476` | 🔴 **필수 ①** |
| 판 증거 4종 | ① 8파일 8줄 `:187~195` ② project.assets 5개 `:197~205` ③ 실제로 올라온 조립품 `:207~209` ④ NU1902 `:211~219` | 🟢 |
| 줄끝 재확인 | 변환 직후 단언 `eol_convert.py` · 실행기 폴더를 다시 셈(`dir_recount_*`) = 변환 결과 `compare_cells.py:230~250` | 🟡 권고 ① |
| 런 상한 4 · 재시도 0 | 재시도 루프 0(반복은 DB 기동 대기 `:168`·`:379` 둘뿐) · `continue-on-error` 0 · concurrency 취소 안 함 | 🟢 (§4 단서) |

## 2. 비교기 반증 — 가짜 칸 JSON 44가지 (`x733c_v4_tools/fakecells.py` · 결과 `x733c_v4_fake_results.txt`)

기준(6칸 모두 정상) → rc=0 · 비교 항목 24개. 이 기준에서 한 곳씩 바꿔 넣었다.

| 반증 | 기대 | 실측 | 판정 |
|---|---|---|---|
| 한 칸(2.6.2×crlf) 문장 하나 빠짐 | 1 | 1 (B1-crlf) | 🟢 |
| 네 칸 모두 같은 문장이 빠짐 | 0 | 0 | 설계상 못 봄(머리말에 적혀 있음) |
| 판 증거: assets 한 줄 빠짐 / 2.5.0 칸 NU1902=0 / audit 파일 없음 / 올라온 조립품 판 틀림 / numstat 7줄 / 2.6.2 칸 numstat 있음 | 2 | 2 | 🟢 |
| 줄끝 CR 수가 eol.json 에서만 틀림 | 2 | 2 | 🟢 |
| **crlf 칸 한 파일 CR=0 (eol.json·실행기 재계수 둘 다 같게) + assert_ok=true** | 2 | **0** | 🟡 권고 ① |
| 대조군 ② 가 2.6.2×CRLF 에서도 다름 / 4칸 모두 같음 / MySQL 8 칸 죽음 | 2 | 2 | 🟢 |
| **대조군 ② 2.6.2×LF = "error"** | 2 | **0** | 🔴 필수 ① |
| **대조군 ② 2.6.2×LF = "a=NULL,b=7"** | 2 | **0** | 🔴 필수 ① |
| DB-16 만 다른 예외(대조군 ① 예외 형 / 깨짐 오류 번호) | 1 | 1 (E6 / E2) | 🟢 |
| probe.json 잘림 / 배열 / eol.json 잘림 / files 키 없음 / trx 깨짐 | 2 | 2 | 🟢 |
| **모든 칸 general_log 수신 0건(빈 열)** | 2 | **0** | 🔴 필수 ② |
| **모든 칸 확장 모양 0줄** | 2 | **0** | 🔴 필수 ② |
| **모든 칸 백오피스가 같은 오류로 적용 0** | 2 | **0** | 🔴 필수 ② |
| **모든 칸 erp.files 50건(measured=113 그대로)** | 2 | **0** | 🔴 필수 ② |
| 한 칸만 files 잘림 / 2회차 빠짐 / 백오피스 오류 / 체크섬 / MySQL 8 수신 1건 빠짐 | 1 | 1 | 🟢 |
| 칸 없음 / HEAD 다름 / 다이제스트 미고정 / 닻 0건 / 측정 중단 / 나무 단언 실패 / 비번 가림 / 서버 변수 다름 / measured 112 / 재계수 짧음 | 2 | 2 | 🟢 |

## 3. 측정 시험

- **CI 밖에서 실행을 거부하나 (실측)**: 사본에서 빌드했다(Release · 2.6.2 · 경고 0 · 오류 0). 칸 환경 변수 없이 `dotnet test --filter …X733cMySqlConnectorProbeTests` 를 돌렸다 → **Failed**(SKIP 아님 · 13 ms). 사유 9줄이 나왔다. 이때 DB 주소는 가짜 수신기(127.0.0.1:39917)로 돌려 두었고, 수신기 접속은 **0** 이었다. 🟢
- **반증 (실측)**: 칸 환경 변수를 손으로 다 넣으면(GITHUB_ACTIONS=true 등) 거부를 지나 바로 DB 에 붙는다. 수신기 접속은 **1** 이었다. 첫 접속은 `MigrationGateDb.ReadyAsync`(`:117`)의 DROP/CREATE 다. 서버 전제 점검(`ServerFactsAsync` `:120`, general_log 켜짐)은 **그 뒤**에 돈다. 그래서 거부는 「실수 방지」일 뿐 구조적 차단은 아니다 → 권고 ②.
- **2.5.0 빌드 (실측)**: sed → restore → build 를 돌렸다. 오류 0 · 경고 8(전부 NU1902). project.assets 5개 = 2.5.0. 2.6.2 restore 는 NU1902 0 · NU1900 0. 🟢
- **같은 실행기 주장**: 측정 시험의 연결 문자열은 G-W13-5 게이트와 같다(180초 · GuidFormat=None · AllowUserVariables). 제품 공장(`MigrationDbConnectionFactory.cs`)과는 `AllowLoadLocalInfile`·`ApplicationName`·시간 초과·풀 크기가 다르다. 다만 이 넷은 드라이버의 문장 해석 옵션(`StatementPreparerOptions`: AllowUserVariables·NoBackslashEscapes·Guid*·DateTime*·AppendSemicolon — 반사로 열거 실측)에 들지 않는다. 그래서 이 차이로 칸 사이 결과가 갈릴 길은 찾지 못했다. 남는 길은 하나다. 풀 연결을 되돌릴 때 드라이버가 스스로 보내는 문장이 판마다 다르면 B1 이 「다름(1)」으로 나온다. 이건 0 으로 새는 길이 아니라 보수적인 쪽(1)으로 가는 길이다 → 권고 ③.

## 4. 워크플로

- **판 바꾸기 sed (사본에서 같은 명령 실측)**: numstat **8파일 각 1/1** · 더한 줄 **8** · 8 files changed, 8 insertions, 8 deletions. 주석 안의 옛 `Version="2.5.0"` 줄은 안 바뀐다. 🟢
- **eol_convert.py (사본 실측)**: crlf → 131개(erp 113 · backoffice 16 · control 2) · assert_ok true · CR 0 파일 0. 추적 파일 129개가 바뀌었다. 다시 lf 로 돌리면 추적 파일 변경 **0**(원래 바이트로 돌아옴). 출하 DDL sha256 은 전후가 같다. 🟢
- **`bash -n`**: run 블록 22개를 뽑아 검사했다 → 문법 실패 0. 🟢
- **칸 6 · 비교 잡 의존**: 행렬은 MariaDB 2×2 + MySQL 8 2×1 = 6. compare 는 `needs: [measure, measure-bo-mysql8]` + `if: always()`. 칸 하나가 죽어도 비교는 무효(2)로 끝난다(반증 M8). 🟢
- **첫 런 「무효(다이제스트 미고정)」 × 런 상한 4**: 실측 M11 → 2(무효)로 나오는 것을 확인했다. 그런데 런 1이 장치 런 1회를 설계상 반드시 쓴다. 그러면 장치 결함을 고칠 여유는 1회뿐이다(런 2에서 장치 문제가 또 나오면 측정 2회 = 본 측정 + 재현으로 끝난다). 여유가 0에 가깝다 → 권고 ④.
- ⚠️미완료: GitHub 위에서 돌린 실물 거동(액션 판 `checkout@v7`·`download-artifact@v8` 등은 레포의 기존 워크플로와 같은 판임만 확인) · 컨테이너 안 CLI 적재 · general_log 표 수집은 **CI 미실행이라 재지 않았다.**

## 5. 대조군 ② 기대의 근거 수준

- **드라이버 쪽은 오프라인에서 실측해 가설보다 한 단계 올렸다.** 사본에 작은 프로그램을 두 판으로 만들었다(`x733c_v4_tools/pp_2.5.0`·`pp_2.6.2`). 내부 `StatementPreparer.ParseAndBindParameters` 를 반사로 불러, `c2_param.sql` + `@P=7` 일 때 서버로 나갈 바이트를 뽑았다(DB 0).
  - 2.5.0 LF: `SELECT 7 AS a --\n, 7 AS b;`
  - 2.5.0 CRLF: `… --\r\n, 7 AS b;`
  - **2.6.2 LF: `SELECT 7 AS a --\n, @P AS b;` ← 두 번째 `@P` 를 치환하지 않음**
  - 2.6.2 CRLF: `… --\r\n, 7 AS b;`
- ②′(`c2b_qmark.sql` · 매개변수 0): 2.5.0 은 두 줄끝 모두, 2.6.2 는 CRLF 에서 드라이버가 「Parameter index 0 is invalid」 를 던진다. **2.6.2 LF 만 드라이버를 통과**해 서버로 간다(기록만 하는 항목).
- ⚠️ 서버 쪽은 미실행이다. `--` 바로 뒤 줄바꿈을 MariaDB 11.4 와 MySQL 8 이 주석 끝으로 받는지, 정의 안 된 사용자 변수 `@P` 가 NULL 이 되는지는 문서 근거뿐이다. 그래서 기대값은 **`a=7,b=NULL`** 이다. 서버가 다르게 받으면 4칸 모두 오류가 되고, 그 경우 무효(2)로 안전하게 떨어진다.

---

## 필수 (푸시 전)

1. **대조군 ② 생존 판정을 정확한 값으로 묶는다.** 지금은 `diff_ok = outcome != "a=7,b=7"`(`compare_cells.py:475`)이다. 그래서 2.6.2×LF 칸에서 연결 오류("error")나 엉뚱한 모양("a=NULL,b=7")이 나와도 「장치 살아 있음」으로 받고 0 이 나온다(반증 M4b·M4b2). 2.6.2×LF 는 `a=7,b=NULL` 이고 `server_received` 둘째 줄에 `@P` 가 그대로 있어야 생존이다. 나머지 칸은 `a=7,b=7` 이고 오류 없음. (근거: §5 오프라인 실측)
2. **빈 측정을 무효로 만드는 바닥 단언을 둔다.** 칸마다 다음을 본다.
   - MariaDB: `len(erp.files) == all_count == measured`. 깨지지 않은 파일 회차마다 `log_queries` 가 1건 이상.
   - `erp_shape.count` > 0 · `backoffice_shape.count` > 0.
   - 백오피스 `applied_count` ≥ 1 이고 `log_queries` ≥ 1.

   하나라도 어긋나면 그 칸 전제 실패(2)로 둔다. 지금은 네 칸이 똑같이 비면 「같음」(반증 M7·M7b·M7c·M25)이다. 칸 사이가 같은지만 보는 비교는 둘 다 비어 있을 때도 같다고 답한다.

## 권고

1. 비교기가 줄끝을 따로 다시 센다. 실행기 재계수(`dir_recount_*`)로 crlf 칸은 `cr == lf == crlf`, lf 칸은 `cr == 0` 을 직접 확인한다(`assert_ok` 깃발만 믿지 않는다 · 반증 M3b).
2. 측정 시험이 DB 에 쓰기 **전에** 서버 지문을 본다(general_log=ON·log_output=TABLE 이고 이름이 `gate_` 인지 등). 그 뒤에 `ReadyAsync` 를 부른다. 또 `HITPAN_X733C_OUT` 이 `RUNNER_TEMP` 아래인지 확인한다. 지금은 손으로 넣은 환경 변수 하나면 3306 실물에 DROP 이 갈 수 있다(§3 실측).
3. B1·BB2 가 「다름」이면 상세에 드라이버가 스스로 보낸 문장(풀 되돌림 등)과 파일 문장을 나눠 적는다. `log_other_commands` 를 함께 출력한다.
4. 런 1을 측정 런으로 쓰려면 푸시 전에 이미지 다이제스트를 미리 고정해 둔다(레지스트리 조회만 · DB 0). 그러면 장치 여유 1회를 되찾는다.
5. NU1902 세기를 `MySqlConnector` 줄로만 거른다. 지금은 다른 꾸러미 권고가 생기면 2.5.0 칸 ≥1 은 거짓으로 서고, 2.6.2 칸 =0 은 무효로 떨어진다. 후자는 안전한 쪽이다.
6. 개발명세서 「같은 실행기」 문단에 한 줄을 덧붙인다. 「G-W13-5 와 같은 연결 성질이고, 제품 공장과는 문장 해석 밖의 옵션 4개가 다르다」.

## 실험 산출물 (scratchpad · 지우지 않음)

- 사본 `x733c_v4_copy` · restore/빌드 로그 `x733c_v4_restore250.log`·`x733c_v4_restore262.log`·`x733c_v4_build.log`·`x733c_v4_build250.log`
- 줄끝 `x733c_v4_eol_crlf.json`·`x733c_v4_eol_lf.json` · 실행 거부 `x733c_v4_testA.log`·`x733c_v4_testB.log`·`x733c_v4_canary1.log`
- 비교기 반증 `x733c_v4_tools/fakecells.py` → `x733c_v4_fake/` · `x733c_v4_fake_results.txt`
- 드라이버 오프라인 `x733c_v4_tools/pp_2.5.0`·`pp_2.6.2` · run 블록 `x733c_v4_tools/blocks/`

---

## 재확인 (고침분) — 2026-10-30 · 검증팀장 데이비드 박

- 대상: `wt-x733c` 의 고친 새 파일(개발명세서 §8) — 읽기만. 실험은 새 폴더 `scratchpad/x733c_re4/` 에서만(바닥 `6b15caf9` 를 `git archive` 로 풀고 고친 파일 6개를 덮은 사본 `copy2`). 이 PC DB 0 · 운영 0 · 수정·커밋·푸시 0.
- 고친 쪽 반증표(62가지)를 믿지 않고 **직접 다시 돌렸다**: 62/62 기대대로(`x733c_re4/re62_results.txt`). 그와 별도로 내 변형 28가지(`x733c_re4/tools/re4.py` → `re4_results.txt`)도 28/28 기대대로였다. 0 으로 샌 것은 **0** 이다. 변형마다 어느 단언이 잡았는지도 확인했다(아래).

### 판정: 🟢 통과 — 필수 2건 닫음 · 남은 필수 0 · 푸시(= 측정 런 1) 가능

| 항목 | 재실행 | 결과 |
|---|---|---|
| 필수 1 대조군 ② | M4b(2.6.2×LF=error) · M4b2(a=NULL,b=7) → **2** · 새 변형: 2.6.2×LF `a=7,b=7`(수신 둘째 줄 @P 그대로 / 7 로) · 2.5.0×LF `a=7,b=NULL` · 수신 문장 없음 / 키 없음 / 두 문장으로 쪼개짐 · 결과 끝 공백 · error=`{}`·`""` · MySQL 8 2.6.2 `a=7,b=7` · MySQL 8 2.5.0 `a=7,b=NULL` · 2.6.2×CRLF `a=7,b=NULL`+@P · 한 칸 대조군 ② 없음 · 문자열로 옴 · 모든 칸 `a=7,b=NULL` → 전부 **2**. 「대조군 ②」 사유가 잡았다(16/17건 · 문자열 1건은 다른 단언) | 🟢 닫음 |
| 필수 2 바닥값 | M7 → 2(바닥 ② 10건) · M7b → 2(바닥 ③⑤ 10건) · M7c → 2(바닥 ④ 6건) · M25 → 2(바닥 ① 4건) · M3b → 2(줄끝 직접 재계수 2건) | 🟢 닫음 |
| 바닥이 정상 칸을 잘못 무효로 만드나 | 깨진 10건이 2회차에서 실패하고 그 회차 수신이 0건이어도 → **0** · 1회차 실패 + 수신 0건이어도 → **0** · MySQL 8 칸에 ERP 키가 없어도 → **0**. 실물 줄끝 기록(`x733c_v4_eol_crlf.json`·`…lf.json` 의 after 131파일)에 `eol_recount_ok` 를 돌림 → crlf·lf 둘 다 어긋남 **0**. 실행기 재계수는 측정 이음 파일을 쓰기 **전**(`:127` < `:198`)이라 거짓 무효 없음 | 🟢 |
| 칸 잡 결론 env | YAML 파서로 확인: compare 단계 env = `needs.measure.result`·`needs.measure-bo-mysql8.result` · `run:` 안 `${{` **0** · J1~J3(failure/없음/cancelled) → 2 | 🟢 |
| NU1902 MySqlConnector 만 | 두 칸 잡 모두 같은 grep(`:201`·`:413`). 내 restore 로그로 다시 셈 → 2.5.0 = **4**(두 로그) · 2.6.2 = **0**. 비교기는 `nu1902=` 키만 읽는다(`nu1902_all_packages` 와 안 섞임) | 🟢 |
| 줄끝 직접 재계수 | `eol.json after` + 실행기 폴더 두 출처를 비교기가 직접 센다(M3b·E1·E2 → 2) | 🟢 |
| 다이제스트 고정 | Docker Hub 메타데이터 조회만(받기·실행 0): 두 다이제스트 모두 **HTTP 200** · 오늘 태그 `11.4`·`8.0` 이 가리키는 값과 **같음**. env 가 차 있으면 `pinned=true` | 🟢 |
| CI 밖 거부(지문) | 새 사본 빌드 errors 0 · warnings 0. 세는 가짜 수신기(127.0.0.1:39931)를 두고 `dotnet test --filter …X733c…` 를 돌렸다. ⓐ 칸 환경 없음 → Failed · 접속 **0** ⓑ OUT 이 RUNNER_TEMP 밖 → Failed · 「RUNNER_TEMP 아래가 아니다」 · 접속 **0** ⓒ 다 맞게 넣음 → 접속 **1** · 스택 = `RefuseUnlessMeasureServerAsync` · 로그에 `ReadyAsync`·`MeasureErpAsync` **0회**(DROP/CREATE 앞에서 멈춤) | 🟢 |
| 워크플로 구문 | PyYAML 6.0.1 로 읽힘 · 잡 3(measure 2×2 · measure-bo-mysql8 2×1 · compare needs 둘 + `always()`) · fail-fast false · continue-on-error 0 · run 블록 **22개 `bash -n` 실패 0** | 🟢 |
| 런 상한 4 | 트리거 = 그 가지 push 하나 + 경로 필터(계측 3자리). docs 는 나무 단언에서도 제외(`:98`). 다이제스트가 고정돼 있으므로 **첫 푸시 = 측정 런 1** 이다. 유효하면 런 2(재현)로 끝나고, 장치 고장 수리에 2회가 남는다 | 🟢 |

### 권고 (푸시를 막지 않음)

1. 바닥 ② 의 「회차마다 수신 ≥ 1」 은 실행기가 스스로 보내는 문장만으로도 선다. 실행기는 매 회차 `CREATE TABLE IF NOT EXISTS schema_migrations`·`SELECT`·`INSERT` 를 보낸다(`MigrationRunner.cs:83~210`). 그래서 이 단언은 「로그가 살아 있다」 는 증명이지 「파일 본문이 도착했다」 는 증명이 아니다(G3: 모든 칸이 1건씩이면 → 0). 이것은 네 칸이 똑같이 누락되면 못 보는 한계(M1b, 머리말에 적힘)와 같다. 더 조이려면 깨지지 않은 회차마다 ≥ 4건으로 둔다.
2. 대조군 ② 정확값의 서버 쪽 근거는 아직 문서뿐이다. MariaDB 11.4·MySQL 8 의 `--`+줄바꿈 처리와 general_log 기록 꼴이 그렇다. 어긋나면 무효(2)로 떨어지므로 안전하다. 다만 기대값을 고치는 푸시는 런 1회를 쓴다. 이 경우를 「장치 런」 으로 셀지를 런 계획에 미리 한 줄 적는다.
3. 줄끝 직접 재계수 상세에서 `eol.json after` 출처의 파일 이름이 `?` 로 찍힌다(이름이 부모 칸에 있음 · 판정 영향 0 · 표시만).
4. 지문 거부의 「general_log OFF 서버」 경로는 실서버로 재지 못했다(이 PC DB 금지). 코드 순서와 ⓒ 스택으로만 확인했다. 첫 런에서 칸 컨테이너가 지문을 통과하는지가 실측이다.
5. (정보) `trufflehog.yml` 은 모든 push 에 돈다. 계측 가지 푸시에도 같이 돌지만 x733c 런 상한과는 무관하다. [4] 권고 3(드라이버가 스스로 보낸 문장 분리)은 범위 밖으로 기록돼 있어 그대로 받는다.

### 재확인 산출물 (scratchpad · 지우지 않음)

- `x733c_re4/re4_results.txt`(내 변형 28) · `x733c_re4/re62_results.txt`(고친 쪽 62 재실행) · `x733c_re4/tools/`(re4.py · eolreal.py · yamlchk.py · refuse.sh · listener.py)
- 거부 로그 `x733c_re4/refuse_{A_no_env,B_out_outside,C_all_env}.log` · 접속 수 `x733c_re4/conn_*.txt` · 사본 `x733c_re4/copy2`(빌드 산출물 포함) · run 블록 `x733c_re4/blocks/`
- 덧붙이기 전 원본 `x733c_re4/x733c_4.before.md`(기존 줄 0 변경 대조용)
