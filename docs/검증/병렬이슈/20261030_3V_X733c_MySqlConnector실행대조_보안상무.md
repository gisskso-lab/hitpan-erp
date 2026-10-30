# [3-V] X733c MySqlConnector 격리 CI 실행 대조 장치 — 보안상무 안철수 병렬 반증

- 대상: `wt-x733c` (가지 `measure/20261030-x733c-mysqlconnector` · 바닥·HEAD = `6b15caf92ea124ddc8e06d664560c83ba2484d59`) 새 파일 7개 · 커밋 전 · CI 미실행
- 근거: 작업지시서 `20261030작_MySqlConnector실행대조_작업지시서.md` §3-5 · §3-6 · §6 · §PM 조건 ①~④ / HQA 판독 경계(issue comment 6520370807)
- 방식: 읽기 + scratchpad 실험만(`scratchpad/x733c_3v_exp/`) · 코드 수정 0 · 커밋·푸시·stash 0 · 이 PC DB 0 · 운영 0
- 레포 공개 여부 실측: `gh api repos/gisskso-lab/hitpan-erp -q .visibility` → **public** ⇒ 런 로그·산출물(14일)은 누구나 받는다. 아래 판단은 이 전제 위에 섰다.

## 판정: 🟡 조건부 — 필수 1건(문서 한 줄) 고치면 푸시 가능

## 필수 (푸시 막음)

| # | 자리 | 내용 | 고칠 것 |
|---|---|---|---|
| F1 | `docs/개발/erp/20261030작_MySqlConnector실행대조_개발명세서.md:91` | 사장님 금지 낱말(메모 「no_bak_variants」 목록) 글자가 그대로 1건 있다(점검 항목 이름으로 인용). 투입 카드 ⑥ 「새 파일 전부 0」 위반 · 사장님 「일체 금지」. 같은 줄의 「0」 주장은 범위가 워크플로뿐이라 문서 자신은 세지 않았다 | 「금지 낱말(헌법 메모 목록)」 처럼 낱말을 쓰지 않고 가리키는 말로 바꾼다. 고친 뒤 새 파일 7개 전부 다시 grep |

## 권고 (푸시 안 막음 · 다음 런 전 또는 결과 문서에 한 줄)

| # | 자리 | 내용 |
|---|---|---|
| R1 | 워크플로 `:470~522` · `compare_cells.py:111~147` | 비교기가 **칸 잡의 결론(needs.*.result)과 `[SKIP]` 검사 단계 결과를 안 본다.** 지금은 닻·측정 trx 계수(total≥1 · passed=total · failed 0)와 `HITPAN_REQUIRE_DB` 의 CI 실패 전환 덕에 새는 길이 실측상 안 보이지만, 「칸 잡 빨강 + 비교 0(같음)」 조합이 원리상 가능하다(예: 통과 처리된 시험이 `[SKIP]` 줄만 찍는 경우). `needs.measure.result`·`needs.measure-bo-mysql8.result` 를 env 로 넘겨 `success` 가 아니면 무효(2), 또는 `anchor.log`·`probe.log` 를 필수 파일에 넣고 `[SKIP]` 이 있으면 무효 — 둘 중 하나 |
| R2 | 워크플로 `:65·71·251·485` 등 | 외부 액션이 **태그 고정**(`checkout@v7`·`setup-dotnet@v6`·`upload-artifact@v5`·`download-artifact@v8`) — SHA 고정 아님. 레포 기존 워크플로 18곳과 같은 꼴이라 이 건만의 퇴보는 아니다. 공개 레포에서 런 안 난수 비번이 env(`GITHUB_ENV`)로 `upload-artifact` 단계까지 살아 있으므로, 계측 가지만이라도 SHA 고정을 권한다(별건 결재 거리로 기록 가능) |
| R3 | 워크플로 `:199·409` | NU1902 수를 **패키지 이름 없이** 센다(`grep 'warning NU1902'`). 다른 패키지에 NU1902 가 생기면 ① 2.6.2 칸은 영구 무효(안전 쪽 · 런 상한 4 소모) ② 2.5.0 칸의 판 증거 ④ 가 MySqlConnector 와 무관하게 채워진다(①②③ 이 교차 확인하니 통과로 새지는 않음). `grep 'warning NU1902.*MySqlConnector'` 로 좁히기 권고 |
| R4 | 측정 시험 `:102~105` | 깨짐 집합 불일치 단언 문구가 「장치 고장으로 멈춘다」. 비교기는 HQA 경계대로 「원인 미확정」 을 쓰는데 trx 메시지만 단정형이다(보안 아님 · [4] 참고) |
| R5 | 워크플로 `:93~97` | 제품 나무 단언이 `docs` 전체와 `src/HitPan.Tests/Measure` 전체를 뺀다. 실험상 csproj·기존 워크플로·`src/HitPan.Tests/` 바로 아래 파일 변경은 잡는다(아래 E3). `Measure/` 안의 `.cs` 는 HitPan.Tests 로 함께 빌드되므로 닻 시험에 영향을 줄 수 있는 유일한 열린 자리다 — 지금 새 파일 1개뿐이라 문제 없음 · 다음 차수에 파일이 늘면 목록 고정 권고 |

## 볼 것 6항목별 결과

1. **권한·트리거 — 🟢.** `permissions: contents: read` 하나(잡 단위 덮어쓰기 0) · 트리거 `push` 하나 · 가지 1 · 경로 3(`:15~22`). `pull_request`·`pull_request_target`·`workflow_run`·`workflow_dispatch`·`secrets.`·`github.event`·`head_ref` 0건(grep). `run:` 블록 안 `${{ }}` 끼워 넣기 0건(행렬 값은 env 로만 · 판정 코드도 env `:514~515`) ⇒ 가지 이름·커밋 글 주입 길 0. 포크 PR 경로 0. `persist-credentials: false` 3곳. DB 포트는 `127.0.0.1:3306` 로만 열림. 원격에 이 가지 아직 없음(`git ls-remote` 0줄). 참고: 이 가지 push 는 기존 `trufflehog.yml`(가지 제한 없음)도 함께 돌린다 — 런 상한 K6 셈에 안 들어가는 별도 런.
2. **DB 비번 — 🟢.** `openssl rand -hex 24` → 즉시 `::add-mask::` → `GITHUB_ENV`(`:147~149`) · 컨테이너에는 `-e 이름만`(값은 환경 · argv 0) · 기동 확인은 `MYSQL_PWD` 환경. 출하 DDL CLI 적재는 기존 `MigrationGateDb.ImportShippingDdl` 가 `-p<값>` 인자로 넘김 → 래퍼가 `docker exec … mariadb -p…` 로 전달 — 일회용 러너 안 프로세스 인자에만 보이고 로그는 가림 처리(기존 꼴 그대로). 측정 시험 `CellEnv.ToString()` 이 칸 이름만 내도록 덮음(`:597~598`). 업로드 전 검사 단계(`:239~247`)는 **실험 E1 에서 실제로 막았다.**
3. **general_log 로 나가는 것 — 🟢.** 로그 표를 통째로 내보내는 코드 0. 러너·적용기 문장은 `sha256 + 길이` 로만 기록(`:243~245` · `:312~314`), 원문으로 나가는 것은 대조군 ② 연결 하나의 문장(`:375~378` · 합성 `SELECT @P …` · P=7)뿐. 확장 모양(`ShapeAsync`)은 표·칸 형·DEFAULT·COMMENT·인덱스 — 행 값 0 ⇒ 출하 DDL 시드 값·계정 해시는 나가지 않는다. 예외는 첫 줄만(`FirstLine`) — 공개 레포 SQL 조각 수준. 산출물 전체가 이미 공개된 레포 내용의 파생값이라 본사 업무 데이터·고객 데이터 0(#18·#22).
4. **통과로 새는 길 — 🟢(R1 단서).** 비교기: 전제 하나라도 어긋남 → 2 · 비교불가 → 2 · 항목 0 → 2 · 예외 → 2 · `BaseException`(SystemExit 포함) → 2 · 반환값이 0/1/2 밖이면 2(`compare_cells.py:603~610·637~670`). 판정 단계는 `set +e … exit 0` 으로 코드를 출력에 담고, 마지막 단계가 `0→0 · 1→1 · 그 밖·빈칸→2` 로 옮긴다 — **실험 E2.** `continue-on-error` 0건. `|| true` 4곳은 모두 「grep 0건 → 0」 계수용이고 그 0 은 실패 쪽(2.5.0 판 증거 ① added=0 → 멈춤 / NU1902=0 → 2.5.0 칸 무효)으로만 흐른다. 시험 단계는 `set -euo pipefail` + `tee` ⇒ 닻 실패 시 측정 단계 미실행 → `probe.trx` 없음 → 무효. 칸 잡 결론을 안 보는 것만 R1.
5. **판 바꾸기·제품 나무 — 🟢.** `git status` = 새 파일만(수정 0). 판 바꾸기는 칸 작업 사본에서만 `sed` · 커밋 0. 대상 줄 실측: `Include="MySqlConnector"` 줄 16 = 실코드 8(2.6.2) + 주석 8(2.5.0 종전 줄) — `sed` 는 `Version="2.6.2"` 만 바꾸므로 주석은 안 건드리고 정확히 8파일 8줄(+ numstat 1/1 · added 8 이중 단언). `Directory.Packages.props`·`packages.lock.json` 없음(중앙 판 관리 우회 길 0). 제품 나무 단언은 **실험 E3 에서 진짜 동작.**
6. **글자 — 🟡(F1).** 새 파일 전부 grep: 실제 비번·토큰·연결 문자열 값 0 · `secrets.` 0 · 「AI·Claude·Cursor·GPT」 0. 연결 문자열 꼴은 `$"…Password={Pass};"` 보간 틀 1곳(`X733cMySqlConnectorProbeTests.cs:595`) — 값 없음 · 기존 `MigrationGateDb.ServerConnString` 과 같은 꼴(정보). 금지 낱말 1건 = F1.

## 실험 (scratchpad 만 · DB 0)

- **E1 비번 검사 단계** — 단계 본문을 그대로 옮긴 `step.sh` · `X733C_DB_PASS=$(openssl rand -hex 24)` 가짜 값 · `probe.json`(연결 문자열 꼴)·`probe.trx` 에 심음.
  결과: `rc=1` · `::error::` · 심은 2파일 가림(`Password=***`) · 남은 글자 0(`grep -rcF` 전부 0) · `secret_scrubbed.txt` = `scrubbed=2` → 비교기 `cell_prereq` 가 그 칸 무효.
  대조군: 안 심은 폴더 → `rc=0` · 표지 파일 없음 / 비번 빈값 → `rc=0`(검사 생략 · 그 경우 DB 단계가 이미 실패).
- **E2 비교기 종료 코드** — `py -3 -I compare_cells.py`: 빈 폴더 2 · 인자 없음 2 · 깨진 JSON 칸 2 · 결과 자리가 파일(쓰기 실패) 2 · 없는 폴더 2.
  마지막 단계 `case` 옮김: `0→0 · 1→1 · 2→2 · 3→2 · 127→2 · 빈칸→2`.
- **E3 제품 나무 단언** — scratchpad `git clone --shared --no-checkout` 사본에서 임시 index 로 나무를 만들어(원 레포·worktree 쓰기 0) 워크플로와 같은 `git diff --quiet 6b15caf9 <나무> -- . ':(exclude)…'` 실행.
  계측 파일만(대조군) rc=0 · csproj 판 내림 커밋 **rc=1** · 기존 `build-and-test.yml` 수정 **rc=1** · `src/HitPan.Tests/Directory.Build.props` 추가 **rc=1** · `Measure/` 아래 props rc=0(MSBuild 는 상위만 읽어 무해 · R5) · `docs/` 추가 rc=0(설계상 허용).

## 안 본 것 / 못 본 것

- 실제 CI 런(장치 런 1) 로그·산출물 — 런 전이라 못 봄. 런 1 뒤 산출물 6칸을 받아 비번 글자 0 · `secret_scrubbed.txt` 0 을 다시 확인할 것(15분).
- `dotnet restore` 실측(NU1902 다른 패키지 존재 여부 · R3) — 네트워크 restore 를 이 PC 에서 안 돌렸다. [4] 판 증거 쪽과 겹친다.
- 비교기 「다름」 검출(심은 차이) — [4] 검증팀장 몫(작업지시서 §6-2).

## 재확인 (고침분)

> 2026-10-30 · [3-V] 보안상무 안철수 · 읽기만(수정·커밋·푸시 0 · 이 PC DB 0 · 운영 0). 범위 = 개발명세서 §8 고침분 + 새로 생긴 자리.

### 판정: 🟢 통과 — 필수 0건 (F1 닫힘)

| 항목 | 결과 | 근거(실측) |
|---|---|---|
| F1 금지 낱말 | 🟢 닫힘 | 새 파일 7개 전부 금지 낱말 계열 grep **0**(파일별 0 · 7개 모두). 외부 노출 금지어(AI·Claude·Cursor·GPT) 0 |
| R1 칸 잡 결론 | 🟢 반영 | 워크플로 `:498~501` env 로 `needs.measure.result`·`needs.measure-bo-mysql8.result` 전달 → `compare_cells.py:373~380` 둘 다 `success` 아니거나 없으면 무효 사유 → `:731` → 2. 반증 J1(failure)·J2(env 없음)·J3(cancelled) 모두 2. `${{ }}` 15곳 전부 `name:`·`env:`·`with:` 자리 — `run:` 블록 안 0(awk 재계수) · `pull_request`/`workflow_dispatch`/`workflow_run`/`secrets.`/`github.event`/`head_ref` 0 ⇒ 주입 길 0 유지 |
| R3 NU1902 | 🟢 반영 | `:201·:413` `warning NU1902: Package .MySqlConnector. ` 만 판정에 씀 · 전체 수는 `nu1902_all_packages` 기록만. 이 PC restore 로그 재실행: 2.5.0 = 4/4 · 2.6.2 = 0/0 · 로그 안 NU1902 패키지는 MySqlConnector 하나뿐(대조) |
| R4 문구 | 🟢 반영 | 측정 시험 `:107`·머리 주석 `:36` 「무효 — 원인 미확정」 · 「장치 고장」 글자 0 |
| 다이제스트 고정 | 🟢 | `:38·:41` 둘 다 `sha256:` + 64자. **독립 대조**: Docker Hub 레지스트리에 태그·다이제스트 각각 HEAD → 둘 다 200 · `application/vnd.oci.image.index.v1+json` · 태그 `11.4`·`8.0` 의 현재 다이제스트 = 고정 값과 일치. index 안 플랫폼에 `linux/amd64` 있음(mariadb 4종 · mysql 2종) ⇒ 다중 아키텍처 index 맞음. 토큰은 메모리에서만 · 출력 0 |
| 서버 지문 | 🟢 | `RefuseUnlessMeasureServerAsync`(`:519~549`) 가 `CellEnv.ReadOrRefuse` 뒤 · 첫 쓰기 전. 읽기 `SELECT @@general_log, @@log_output` 하나 · `Pooling=false`. 실패 글자에 실리는 것은 두 변수 값뿐 — 비번·연결 문자열 0. `CellEnv` 는 `ToString() => Id`(`:635`)로 비번 칸이 찍히지 않음. 예외는 `FirstLine(ex.Message)` 만 · MySqlConnector 인증 예외 글자에 비번 없음 |
| 비번 산출물 검사 | 🟢 그대로 | `:242~249`·같은 꼴 MySQL 8 `:453~460` · `if: always()` · `grep -rlF` → 가림 + `secret_scrubbed.txt` + exit 1 → 칸 잡 빨강 → 이제 R1 로도 무효 · 비교기 `:124·:189` 로도 무효(M22) — 두 겹 |
| 새 감사 파일 | 🟢 | `nuget_audit.txt` = 수 3개(`nu1902`·`nu1902_all_packages`·`nu1900`) 뿐 · 자격증명 0 (N1 반증 0 = 형태 호환) |
| 통과로 새는 길 | 🟢 0 유지 | `continue-on-error` 0. `|| true` 6곳 = 계수용(2곳 늘어난 것은 `nu1902_all` 기록용 · 판정 무관). `|| exit 0` 2곳(`:245·:456`) = 비번·증거 폴더 없을 때 검사 건너뜀 — 그 경우 DB 단계 전에 멈춘 칸이라 증거 불완전 → 무효로만 흐름. 마지막 단계 `0→0 · 1→1 · 그 밖→2` 그대로. 반증 62가지 전부 기대대로 · 0 으로 샌 것 0 |

### 남은 필수
- 없음.

### 권고 (푸시 막지 않음)
- R2(외부 액션 SHA 고정) · R5(`Measure/` 파일 목록 고정) — 미반영 그대로 · 별건 결재 거리로 남김(§8-5 기록과 일치).
- R6(신규 · 정보) 서버 지문은 「general_log=ON · log_output=TABLE」 두 값만 본다 — 개발 PC 실물이 우연히 같은 설정이면 통과한다. 1차 방어는 `GITHUB_ACTIONS`·`RUNNER_TEMP`·OUT 경로·런 안 난수 비번 검사이고 지문은 2차 겹이라 지금 충분. 더 단단히 하려면 컨테이너 기동 때 런 안 난수 표식(예: 서버 변수 하나에 런 번호)을 넣고 지문에서 맞춰 보는 길이 있다.
- R7(신규 · 정보) 고정 index 가 레지스트리에서 지워지면 pull 실패 → DB 단계 빨강 → 무효(안전 쪽). §8-3 위험 1줄과 같은 판단.
- 「OFF 서버 거부」 경로와 대조군 ② 서버 거동은 첫 런이 실측 — 어긋나면 무효(2)로 떨어지는 것만 확인했다(통과로 새지 않음).
