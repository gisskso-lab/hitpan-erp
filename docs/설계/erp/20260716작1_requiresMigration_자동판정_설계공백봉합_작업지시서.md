# 20260716작1 — requiresMigration 자동판정 + 마이그 SQL 배포누락 P0 봉합 작업지시서

> 발행: PM 브라운킴 (2026-07-16) / SOP [3] 작업지시서 → [4] CTO 결재 → [5] 사장님 승인 → [6] 매니저 지시 → [7]+[7-V] 작업·검증 동시
> 근거: 작업지시서 `20260715작1_고리4` §247-263 설계공백 + 병렬 검증 4라인(설계팀·검증팀·CTO·보안상무) 수렴
> 상태: **CTO 결재 대기** (사장님이 "작지서화 후 결재" + "봉합 후 재-publish 실측" 방향 지정)

---

## 0. 이 작업지시서가 나온 경위 (병렬 검증체계 가동)

고리4 자동 업데이트에서 "이번 릴리스가 DB 스키마를 바꾸는가(`requiresMigration`)를 사람 손 안 타고 기계가 어떻게 판정하나"가 작업지시서 20260715작1에 **명세되지 않은 설계 공백**이었다. PM이 임의로 채우지 않고 **4개 진영을 병렬로 붙여 동시 검증·설계**했다(SOP [7]+[7-V]).

**네 진영이 서로 보지 않고 같은 결론에 수렴** — 받아쓰기(헌법 #32)가 아니라는 실증:
- **설계팀**(DB+백엔드 매니저): 대안 3개 생성 → 대안 C 권고(이원 판정). **조사 중 진짜 P0 발견**.
- **검증팀**(빌드·배포 정합): 그 P0를 **독립 실측 확증**(다른 경로로 같은 결론).
- **CTO**(중간 결재): 오판 비대칭·기준선 함정·심사 rubric 독립 수립. blocking 9건.
- **보안 상무**(안철수): 서명·키 경로 감수. 조건부 승인, blocking 2건 + 조건 2건.

---

## 1. 문제 정의 — 두 겹

### 🔴 P0 (선결) — 마이그 SQL이 배포본에 안 들어간다
**설계팀·검증팀 SoD 독립 확증.**
- 소스 `src/HitPan.API/Migrations/SQL/DB-*.sql` = **55개**
- `dotnet publish` 산출물 = **0개** (`Migrations` 폴더 자체가 안 생김)
- 원인: `HitPan.API.csproj`에 `.sql` 복사 지시 없음(`.sql`은 .NET SDK 기본 Content 아님). 복사 항목은 `AI\*.md` 하나뿐(line 49).
- 대조군 `AI/*.md` 3개는 정상 복사 → publish 자체는 정상, **오직 SQL만 누락**.
- 최근 배포 EXE 1.2.29~1.2.33 **전부 마이그 SQL 미포함** 확인.

**귀결**: 스키마 변경 릴리스가 나가도 고객 PC에서 `MigrationRunner.ResolveMigrationsDirectory()`가 폴더를 못 찾아 "마이그 0건 no-op". 신버전 api가 구스키마에서 500 → 롤백 → DB 오염 → 복구 불가. **헌법 #19·#20 위반.** 그리고 지금 `build-manifest.ps1`의 마이그 스캐너·`local_update_` 호환성 게이트(CTO C-2)가 **전부 빈 폴더를 훑고 있어** 무력.

### 설계 공백 — requiresMigration 자동 판정
"릴리스가 DB를 바꾸는가"를 손입력 스위치(`build-manifest.ps1 -RequiresMigration`)가 아니라 기계가 결정해야 하는데, "무엇 대비 신규"인지(기준선)가 미명세.

---

## 2. 확정 설계 (네 진영 수렴)

### 2.1 핵심 원리 — CTO 통찰
> **"이 판정의 본질은 '정확한 판정'이 아니라 '틀릴 때 어느 쪽으로 틀리게 할 것인가'다."**

결재사항 #3(롤백 시 DB 자동복원 안 함) 때문에 오판 비용이 극단적으로 비대칭:
| 오판 | 결과 | 회복 |
|---|---|---|
| **false 오판**(마이그 필요한데 "없음") | 자동 적용 → 구스키마 500 → 롤백 → DB 오염 | **영구 고립**(CS 수동 개입 필수) |
| **true 오판**(마이그 없는데 "있음") | 정지 → CS 안내, ERP는 구버전 정상 가동 | 지연·번거로움(데이터 무손상) |

⟹ **모든 모호함·판정불능·비교실패·예외는 `requiresMigration=true`(정지)로 귀결.** fail-safe 방향 = 정지.

### 2.2 이원 판정 (설계팀·CTO 독립 수렴)
판정 주체를 신뢰 경계에 따라 둘로 나눈다:

| 주체 | 무엇을 보나 | 판정 |
|---|---|---|
| **릴리스 파이프라인**(본사 빌드머신) | zip 안 `Migrations/SQL/DB-*.sql`의 **정적 목록**(고객 DB 못 봄) | manifest `requiresMigration` 산출(정적) |
| **워치독**(고객 PC 최종 게이트) | 고객 DB `schema_migrations` **실측** vs zip 안 DB-*.sql | 최종 판정 — 미적용 id 존재 시 `false`여도 **강제 정지** |

**워치독이 주 판정, 파이프라인은 보조.** 이유(CTO §2): 서명은 manifest 진위만 보증하지 내용 정합은 안 본다. 본사가 **정직하게 실수한** false를 서명해도 서명은 유효하다. 워치독이 실제 zip을 해제해 고객 DB와 대조하는 것이 마지막 그물.

### 2.3 기준선 = 고객 PC 실제 DB 상태 (CTO blocking B-2)
`schema_migrations`(DB-84, 이미 존재)가 "이 PC가 무엇을 적용했나"의 진실원. checksum도 이미 기록(`MigrationRunner:130`).
- **미적용 migration_id 존재** → 마이그 필요(정지)
- **동일 id인데 checksum 불일치** → 내용 변경 마이그로 간주(정지) — 파일명 재사용·내용변경 방어(CTO B-3)

파일명 목록끼리만 비교하는 방식(대안 A)은 파일명 재사용·내용변경·수동마이그·롤백재설치에서 뚫린다(CTO 기준선 함정 표). **최종 게이트는 반드시 고객 DB 실측 참조.**

### 2.4 clean DDL 시드 (설계팀 대안 C 핵심 + 미결 #1)
**문제**: clean DDL 신규설치 고객은 스키마는 최신인데 `schema_migrations`가 **비어 있다**(시드 INSERT 0건 확인). 그러면 DB-02~84 전부 "미적용"으로 읽혀 **모든 릴리스가 오탐→영구정지**.
**봉합**: clean DDL이 "그 시점까지 구조적으로 반영한 DB-NN 전부를 success=1로 시드"(checksum 포함). 헌법 #36 정공법(clean DDL = 신규설치 단일 진실원이므로 여기 손대는 게 정당).

> ⚠️ **설계팀 미결 #1 — 사람 판단 1회 필요**: 55개 DB-*.sql을 "순수 DDL / 데이터 INSERT 겸함"으로 **DB 매니저가 1회 분류**해야 시드 목록이 정확하다. clean DDL이 이미 담은 것만 시드해야 함(안 담긴 데이터 마이그를 시드하면 그 데이터가 영영 안 들어감).

---

## 3. 작업 범위 (W 항목)

### W-0 · [선결 P0] 마이그 SQL publish 편입 + CI 스모크 게이트
- `HitPan.API.csproj`에 추가(검증팀 확증 방법, `AI\*.md`와 동일 검증된 패턴):
  ```xml
  <None Update="Migrations\SQL\DB-*.sql" CopyToOutputDirectory="PreserveNewest" />
  ```
- **검증(사장님 지정 "재-publish 실측")**: 재-publish 후 산출물 `Migrations/SQL/DB-*.sql` + 자동업데이트 zip 포함 재실측.
- **CI 스모크 게이트 신설(CTO 부분수정 4)**: 개수만으론 불충분(0바이트 복사·소스 56번째 추가 시 하드코딩 55 통과 못 잡음). **산출물 DB-*.sql 파일명 집합 == 소스 파일명 집합**(개수 아니라 집합 동일성) + **각 파일 `Length > 0`**. 다르면 빌드 실패. 위치 = `build-and-test.yml`. (2단계로 ddl-smoke 실측 실행까지 확장은 W-1 완료 후.)

### W-1 · clean DDL schema_migrations 시드
- **[선결]** DB 매니저가 55개 DB-*.sql을 "DDL만 / 데이터 겸함"으로 분류 → clean DDL이 담은 것 목록 확정.
- clean DDL(`installer/hitpan_db_clean.sql`)에 `INSERT INTO schema_migrations (migration_id, checksum, success) VALUES ...` 시드 편입(확정 목록만, checksum 포함).
- 헌법 #36 정합 — clean DDL이 신규설치 단일 진실원. 출하 DDL 4게이트·ddl-smoke 재검증.

### W-2 · 파이프라인 정적 산출 (build-manifest.ps1)
- `-RequiresMigration` 손 스위치 제거 → zip 안 DB-*.sql **최대 migration_id** 추출, 직전 릴리스 대비 자동 산출.
- **CTO B-9**: 손입력이 남는 기간엔 자동값을 **강화만 가능(false→true), 완화 불가(true→false)**. 자동 우선.
- `local_update_` 호환성 게이트(이미 배선)가 이제 실효(W-0으로 폴더가 채워지므로).

### W-4 · 판정 파서 3곳 정합 (설계팀 미결 #3) — **W-3보다 먼저 (CTO 부분수정 1)**
> ⚠️ **CTO 실사 적발 — 순서 역전 수정**: 현재 파서 semantics가 이미 갈라져 있다.
> - `MigrationRunner.cs:71` → schema_migrations에 **정규화 id**(`DB-8b`→`DB-08b`)
> - `build-manifest.ps1:88` → 사이드카에 **raw 파일명 + lexical 정렬**(`DB-10`<`DB-2`)
> 워치독(W-3)이 사이드카(raw) vs DB(정규화)를 차집합하면 **전부 불일치로 오정지**(B-4 위반). 따라서 파서 정합이 W-3의 **정확성 선결**이다(cleanup 아님).
- `DB-(num)(suffix)_` 정규식 + 정규화 id 규칙을 MigrationRunner·CI·워치독 3곳이 **동일하게** 쓴다(max id·차집합 어긋남 방지).
- **파일명 재사용 금지 규약(CTO 부분수정 2 보완)**: 같은 DB-NN을 다른 내용으로 재사용 금지 — 새 번호 발급. 이 규약이 checksum 유예 기간(W-3) 동안 B-3 공백을 메운다. 파서 규칙에 못박음.

### W-3 · 워치독 실측 교차검증 (최종 게이트) — **W-1·W-4 완료 후**
- 워치독이 `SELECT migration_id, checksum FROM schema_migrations WHERE success=1`(mariadb CLI, `WatchdogConsentReader` 패턴 재사용) vs zip 안 DB-*.sql(파일명 + sha256).
- 미적용 id 존재 **또는** 동일 id checksum 불일치 → `requiresMigration=false`여도 **강제 정지 + CS 기록**.
- 게이트 위치: 다운로드·백업 **후** / W4-1(정지) **전**(작업지시서 20260715작1 §255).
- **CTO B-5(1차 범위 봉쇄)**: `MigrationRunner.ApplyPendingAsync()` 호출 **0건**(실사 확증: `grep MigrationRunner src/HitPan.Watchdog/`=0). 출력은 불리언. "정지" 후 자동 DB조작 0건. 실제 마이그 실행은 고리5.
- **checksum 유예 조건(CTO 부분수정 2)**: 1차는 "미적용 id 존재" 판정만으로 좁히고 checksum 대조는 2단계로 미룬다(오탐 리스크 회피). **단 그동안 "동일 id·다른 내용" 방어가 1차에 없음을 명시**하고, W-4의 파일명 재사용 금지 규약으로 그 공백을 메운다. checksum 정규화 = MigrationRunner `File.ReadAllTextAsync(UTF8)` 재해시(raw 바이트 아님, BOM·CRLF 문자열에 실림)와 동일 방식.
- **정지 진단 필드(CTO 부분수정 3, B-7)**: 정지 시 `local_update_status`에 **사유 필드**(어느 파일/미적용 id, 파이프라인값 vs 실측값) 기록 + 메타핑 **정지 카운터**(스키마·id 목록·데이터 0건, B-8·C-4). "CS 기록"이 아니라 어느 필드·어느 카운터인지 배선 명시.

---

## 4. CTO blocking 9건 (어떤 설계든 통과 전제)
| # | 조건 |
|---|---|
| B-1 | fail-safe 기본값 — 판정불능·모호·예외 전부 `true`(정지). "못 판정=통과" 0건 |
| B-2 | 최종 게이트(워치독)는 `schema_migrations`+checksum **실측 참조** 필수 |
| B-3 | id 존재만으로 skip 금지 — 같은 id 다른 checksum = 변경 간주 |
| B-4 | 파이프라인·워치독 **비교 가능한 기준선** — 거짓 불일치로 정상 릴리스 오정지 금지 |
| B-5 | 1차 범위 봉쇄 — MigrationRunner 호출 0, 불리언 출력, 정지 후 DB조작 0 |
| B-6 | 단일 진실원 — 마이그 여부 진실원 신설 금지(schema_migrations + DB-*.sql이 이미 진실원) |
| B-7 | 정지 진단 가시화 — 사유(어느 파일/미적용 id, 파이프라인값 vs 실측값)를 `local_update_status`+메타핑 |
| B-8 | 본사 데이터 최소 — 본사로 가는 건 "마이그 필요로 정지함" 메타 사실뿐. 스키마·id목록·데이터 전송 금지(#22) |
| B-9 | 손 스위치 자동값 강화만 가능(완화 불가) |

## 5. 보안 상무 조건 (서명·키 — W4-2 착수 전)
| # | 조건 |
|---|---|
| C-1 (BLOCKING) | 공개키 도착·내장 전까지 W4-2 착수 금지. 순서: NCP 키 생성 → 공개키 내장/db.conf 주입 → 짝 정합 검증 → W4-2 |
| C-2 (BLOCKING) | `IsNewerVersion` 다운그레이드 차단이 replay 방어 유일선 — 절대 약화 금지 |
| C-3 | db.conf 공개키 override 사용 시 기동 로그에 kid·지문 남길 것(감사성, 공개키라 무해) |
| C-4 | 워치독 로컬 스키마 판정 결과를 본사로 전송 금지(로컬 읽기 OK, 메타핑은 성공/실패 카운터만 = B-8과 동일) |

**상무 감수 통과분**: 서명 형식 이중수용(P1363‖DER)·Base64 이중디코드·db.conf override — 전부 실측으로 위조 시도 거부 확인, 표면 안 넓힘. 개인키 격리(레포 PRIVATE KEY 0건, EmbeddedPublicKeys 비어 fail-closed, CI 서명 배선 0건) 확인.

---

## 6. 검증 (Sandbox 휘발성 — demo 불가침 헌법 #39)
| 게이트 | 내용 |
|---|---|
| W-0 실측 | 재-publish → DB-*.sql 55개 + zip 포함. CI 스모크(개수 불일치 시 빌드실패) |
| W-1 | clean DDL 시드 후 신규설치 → schema_migrations에 확정 목록 success=1. ddl-smoke 통과 |
| 오탐 | 마이그 없는 릴리스 → 자동 적용됨(clean DDL 고객도 오탐 정지 0) |
| 오답 | `requiresMigration=false`인데 zip에 신규 DB-NN 주입 → 워치독 강제 정지(G-2M) |
| fail-safe | 판정 예외 주입 → 정지로 귀결(B-1) |
| 본사 격리 | 정지 통지에 스키마·id 목록·데이터 0건(B-8·C-4) |

3관문: 빌드 0/0 + ddl-smoke + 검증팀 SoD 독립반증.

---

## 7. 손댈 파일
- `src/HitPan.API/HitPan.API.csproj` (W-0 SQL 복사)
- `.github/workflows/` (W-0 CI 스모크 게이트)
- `installer/hitpan_db_clean.sql` (W-1 시드) — **DB 매니저 55개 분류 선결**
- `installer/updates/build-manifest.ps1` (W-2 자동 산출)
- `src/HitPan.Watchdog/AutoUpdate/` (W-3 워치독 교차검증 — 신규)
- 파서 정합 3곳 (W-4)

## 8. 범위 밖 (고리5·별건)
- `MigrationRunner.ApplyPendingAsync()` 실제 호출(마이그 실행) — 고리5
- DB 자동복원 — 결재 #3로 1차 제외
- checksum 정규화 완성(2단계 가능)

---

## 9. 착수 조건·순서 (CTO 결재 반영)
- **CTO 결재 = 부분수정후승인** (4건 반영 완료: ①W-4→W-3 순서 역전 ②checksum 유예 조건+파일명 재사용 금지 ③B-7 정지 진단 필드 ④W-0 검증 집합+비어있지않음). **사장님 결재 완료(2026-07-16).**
- **착수 순서(수정)**: `W-0 / (W-1 ‖ W-2) / W-4 / W-3`
  - **W-0**: 독립·저위험(csproj 1줄+CI, 기존코드 무수정). 나머지의 전제. **승인 전 코드·CI 우선착수 허가**(CTO), 단 배포(EXE 재빌드)는 별건 결재. 실측 결과 사장님 보고.
  - **W-1**: **DB 매니저 55개 SQL 분류가 하드 blocker** — 분류 없이 시드하면 clean DDL 미포함 데이터 마이그 오시드(헌법 #20 위반). 분류 완료 전 시드 착수 금지. W-0과 병렬 가능.
  - **W-2**: W-0 완료 후(빈 폴더면 무의미). W-1과 병렬 가능.
  - **W-4 → W-3**: 파서 정합 먼저(오정지 선결).
  - **W-3**: W-1(시드)·W-4(파서) 둘 다 완료 후.
- 서명 W4-2 관련은 상무 C-1(사장님 NCP 키) 선행 절대(별 트랙).
