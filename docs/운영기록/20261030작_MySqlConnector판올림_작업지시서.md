# 작업지시서 20261030작 — MySqlConnector 2.5.0 → 2.6.x 판 올림 (NU1902 · GHSA-473q-m89c-ghf8) · 꺼 둔 NU1902 경고 되살림

> 🔴🔴 **PM 결재 전 코딩 금지** (#33 · #42). 이 문서는 **초안**이다(작성 = 설계팀장 존 · 결재 = PM 브라운킴 · 작성자 ≠ 결재자).
> 🔴 **제품 코드(.cs) 0줄이 목표** — 고치는 것은 csproj 의 꾸러미 판 줄과 NoWarn 한 줄뿐. `.cs` 를 고쳐야 붙는다면 **멈추고 보고**(§8-3).

## §0 [카드]

| 항목 | 내용 |
|---|---|
| 작성 | 설계팀장 존 · 2026-10-30 · 기준 나무 = origin/main `fa9b9687` (가지 `fix/20261030-MySqlConnector-판올림`) |
| **결재** | ⬜ PM 브라운킴 |
| 오더 | 대표 10/30 「결재 루틴대로 즉시 착수」 — PM 추천(5곳 같은 판 · 꺼 둔 NU1902 되살림 · 억제·방치 비추천) 승인 (PM 전달 · ⚠️ 결재 기록 문서가 이 나무에 아직 없음 → PM 이 주소를 §1 에 채운다) |
| 근거 | [1-V] [`docs/검증/선행/20261030_선행검증_MySqlConnector_NU1902.md`](../검증/선행/20261030_선행검증_MySqlConnector_NU1902.md) (조건부 통과) |
| 왜 | 고객 위험 때문이 **아니다**(1-V §2 — 우리는 취약 조건 밖). **#19 빌드 0/0 · 백오피스 배포 정지(1-V ③) · 감사 가림(1-V ④)** 때문이다 |
| 추천 판 | **2.6.2** (K-1 · §4) — 2.6.1 대비 추가분이 우리 경로에 닿지 않음(원본 비교로 확인) |
| 규모 | **S** — csproj 5개(판 5줄 + NoWarn 1줄) · K-2 채택 시 8개(판 8줄 + NoWarn 1줄) · `.cs` 0 · DDL 0 · 워크플로 0 |
| 🆕 새 적발 | **tools 3곳도 2.5.0 직접 참조 + src 프로젝트 참조** → 5곳만 올리면 tools 빌드가 판 내림(NU1605)으로 깨질 것(예측 · §2-2 · K-2) |
| 🆕 새 적발 | **2.6.1 에서 비번 전송 조건이 바뀌었다** — 원격(loopback 아님) + 인증서 미검증 TLS 에서 `caching_sha2_password` 전체 인증 시 **연결 거부**. CI 는 전부 127.0.0.1 이라 **못 잡는다** → 백오피스 운영 DB 위치 확인 필요(§5-2 · K-4) |
| 위험 자리 | 2.6.0 SQL 해석 강화 3건 → 정적 grep 전수(§5-1 · 현재 해당 0) + CI DB 게이트(대표 경로 동작) |
| 대조군 | 커밋 2개로 나눔: c1 = NoWarn 만 제거(빨강 · NU1902 **4곳** 기대) → c2 = 판 올림(초록 · NU19xx 0) · 로컬 양성 대조 = 간접 high 4종이 계속 보임(§6) |
| 갈래 | 1갈래(hp-implementer 1) · [3-V] 보안상무 1 · [4] 검증팀장 1 = **에이전트 상한 3** |
| CI 런 상한 | **4** (c1 대조 1 · c2 초록 1 · 여유 2) — 넘으면 멈춤 · 시험 실패 재실행 금지 |
| 하는 것 | §3 — 판 줄 교체(종전 줄 주석 보존 #1) · `Application.csproj:47` NoWarn 거둠 |
| 하지 않는 것 | §7 — 억제 일체 · TWAE 끄기 · 다른 꾸러미 · `.cs` · 워크플로 · `appsettings*`(#21) · 이 PC DB 시험 · 운영 쓰기(#39) |
| 순서 | **이 차수 머지가 #699 머지 상신보다 먼저**(CTO 조건 6 — Build & Test 빨강 면제 없음) · #699 초록 받는 길 = K-7 |
| 머지 뒤 | `deploy-ncp.yml` 이 자동으로 돈다(경로 `src/HitPan.Backoffice.API/**`) → 배포 잡 `environment: production` 승인 대기 → K-5(#29) |
| 확인 필요 | 8건(§9) — 첫째 K-1 판 · 둘째 K-2 tools · 🔴 K-4·K-5 는 머지 전 정해야 함 |
| 금지 | 빌드 0/0(#19) · 덮어쓰기(#1) · 자격증명 모양 문자열(코드·로그·문서·커밋) · XML 주석 안 하이픈 두 개 연속(§3-3) |
| 산출 | 개발명세서 → `docs/개발/erp/` · 작업리뷰서 → `docs/검증/erp/` · [3-V] → `docs/검증/병렬이슈/` · 폴더 INDEX 한 줄(#41) |
| DoD | §10 |

## §1 [오더]
- 원문(PM 전달): 대표 10/30 「결재 루틴대로 즉시 착수」. 승인 대상 = PM 추천 「5곳 같은 판으로 올림 · 꺼 둔 NU1902 경고 되살림 · 억제·방치 비추천」.
- 결재 기록 주소: 🟢 [`20261030_결재_4X2차최종범위_MySqlConnector판올림착수_대표.md`](20261030_결재_4X2차최종범위_MySqlConnector판올림착수_대표.md)(PR #728 · 원문 「2. 결재 루틴대로 즉시 착수」 · PM 기입 10/30) · ⬛ 옛 칸: ⬜ (PM 기입 — 10/30 결재 기록 [`20261030_결재_699외부검증2차범위착수_MySqlConnector선행검증_대표.md`](20261030_결재_699외부검증2차범위착수_MySqlConnector선행검증_대표.md) 는 「선행검증 착수」 까지만 적혀 있고 「판 올리기 = 선행검증 → 설계 → 대표 결재」 로 남겨 두었다 · 판 올림 결재 원문 줄이 들어간 기록이 필요하다).
- 이 문서는 그 「설계」다. 오더에 없는 것(다른 꾸러미 · 간접 high 4종 · 워크플로 주석 정리)은 **넣지 않는다**(#33) — 별건 기록만(§7).
- 오더의 「5곳」 은 1-V §5 의 직접 참조 5곳이다. 1-V 가 못 센 tools 3곳은 오더 밖 → K-2 로 묻는다.

## §2 [사실] (파일:줄 읽기 · 로컬 restore·list 만 · 실행 0)
### §2-1 직접 참조 (전부 `Version="2.5.0"`)
| # | 파일:줄 | 솔루션 | 비고 |
|---|---|---|---|
| 1 | `src/HitPan.API/HitPan.API.csproj:33` | 포함 | |
| 2 | `src/HitPan.Application/HitPan.Application.csproj:14` | 포함 | `:40~47` NoWarn NU1902(MailKit 사유) |
| 3 | `src/HitPan.Backoffice.API/HitPan.Backoffice.API.csproj:21` | 포함 | NCP 배포 대상 |
| 4 | `src/HitPan.Infrastructure/HitPan.Infrastructure.csproj:24` | 포함 | `:25` Pomelo `[8.0.0,9.0.0)` → 8.0.0 (의존 `MySqlConnector >= 2.3.5` 하한만 — 1-V §5) |
| 5 | `src/HitPan.IntegrationTests/HitPan.IntegrationTests.csproj:14` | **밖**(sln grep 0) | `:20` Application 참조 |
| 6 🆕 | `tools/MdbMigrationBaselineRun/MdbMigrationBaselineRun.csproj:30` | 밖 | `:18` **TWAE=true** · `:22·23·26` Application·Infrastructure·API 참조 |
| 7 🆕 | `tools/MigrationIdempotencyCheck/MigrationIdempotencyCheck.csproj:24` | 밖 | `:19·20` Application·Infrastructure 참조 |
| 8 🆕 | `tools/MigrationSmokeTest/MigrationSmokeTest.csproj:18` | 밖 | `:13·14` Application·Infrastructure 참조 |
- 간접: `HitPan.Tests` → 2.5.0(Application·Infrastructure·API·Backoffice.API 참조로 끌어옴). 판 올림 뒤 자동으로 따라온다.
- 워크플로가 tools 3곳을 빌드하는 자리: `.github/` grep **0** ⇒ CI 는 tools 를 안 잰다.

### §2-2 tools 3곳을 안 올리면 (🆕 · ⚠️예측 · 미측정)
- tools 는 자기 줄에서 2.5.0 을, 참조한 src 프로젝트에서 2.6.x 를 받는다 ⇒ NuGet 「판 내림」 NU1605 (SDK 기본 오류 승격 대상) ⇒ tools 3곳 restore/빌드 실패 예측. CI 는 이걸 안 재므로 **조용히 깨진 채 남는다.**
- 반대로 올리면 tools 도 2.6.x 로 컴파일만 확인(로컬 · DB 접속 실행 0).

### §2-3 로컬 실측 (W · 10/30 · SDK 10.0.204 · 로그 = scratchpad `my2-logs/`)
| 명령 | 결과 |
|---|---|
| `dotnet restore src/HitPan.sln --force-evaluate` | 종료 0 · NU1902 **3줄**(API·Infrastructure·Backoffice.API) · 다른 NU19xx 0 — 1-V §4 와 같다 |
| `dotnet list src/HitPan.sln package --vulnerable` | 취약 = **MySqlConnector 2.5.0 뿐**, 4곳(Application·Infrastructure·API·Backoffice.API) · 나머지 9개 프로젝트 0 |
- ⇒ `HitPan.Application` 의 직접 참조 중 취약한 것은 **MySqlConnector 하나**(MailKit 4.16.0 은 0) ⇒ NoWarn 을 거두고 판을 올리면 Application 에서 새로 드러날 NU1902 는 **0 이 기대값**(⚠️ 판 올린 뒤 실측은 [3] 몫).

### §2-4 경고가 오류로 바뀌는 자리
- `build-and-test.yml:69` restore(경고) → `:73` build `/p:TreatWarningsAsErrors=true /warnaserror`(오류). DB 게이트 `:359~361` · 백오피스 스키마 게이트 `:1515~1518` 은 `HitPan.Tests` 만 빌드(TWAE 없음).
- `deploy-ncp.yml:62~64` 백오피스 API·백오피스·랜딩 TWAE 빌드 · 트리거 `:27~35` main push + 백오피스·랜딩 경로 ⇒ **이 차수 머지 = 자동 배포 런 1회**(빌드 잡 → 배포 잡 `:88 environment: production` 승인 대기).
- `build-and-test.yml:20~48` 주석(10/14) — 「TWAE 끄기 · NoWarn/WarningsNotAsErrors 추가 · `--no-restore` 로 조회 건너뛰기 = 전부 금지 · 봉합은 꾸러미 갱신 차수 + 결재뿐」 ⇒ 이 차수가 그 「봉합」 꼴이다.

## §3 [고칠 것] ([3] 개발 지시)
### §3-1 판 줄 (#1 — 종전 줄은 주석으로 남긴다 · 10/18 작8 꼴 `Application.csproj:30~34` 그대로)
- §2-1 의 #1~#5 (K-2 채택 시 #6~#8 까지) 각 줄을 같은 판(K-1 · 추천 2.6.2)으로. 각 자리 바로 위에 주석 1덩어리: 차수명 · 결재 · 종전 줄 원문 · 「이 판은 N곳이 같아야 한다(한 곳만 올리면 판이 갈린다)」.
- `Version` 은 **정확한 판 하나**로 적는다(범위식 금지 — 판 갈림 방지).

### §3-2 NoWarn 거두기 — `src/HitPan.Application/HitPan.Application.csproj:47`
- `<NoWarn>$(NoWarn);NU1902</NoWarn>` 줄을 주석으로 감싸 살려 둔다(#1). 위 MailKit 사유 주석(`:41~46`)은 그대로 두고 그 아래에 한 덩어리: 「사유(MailKit 4.14 권고) 소멸 — 지금 4.16.0 · `--vulnerable` 0(§2-3) · 이 억제가 가리던 것은 MySqlConnector 하나였다 · 다시 켜지 마라(build-and-test.yml 10/14 주석)」.
- 다른 프로젝트의 NoWarn(`HitPan.Web.csproj:14` MUD0002·RZ10012)은 **무접촉**.

### §3-3 주의
- 🔴 XML 주석 안에 **하이픈 두 개 연속 금지**(빌드가 깨진다 — 10/14 메모가 「강제 재평가 옵션의 앞 두 글자를 적을 수 없다」 고 쓴 이유). 명령 옵션은 이름만 적는다(예: 「force-evaluate」).
- 커밋은 **c1 = §3-2 만**, **c2 = §3-1** 로 나눈다(대조군 §6-1). 커밋 메시지는 `-F` 파일 · 직후 `git log -1 --format="%s"` 확인.
- 레포 루트 `git add -A` 금지 — 파일 이름으로만 add.

## §4 [장단·추천] 판 선택 (K-1)
근거 = 상류 릴리스 노트 + 판 사이 원본 비교(`gh api repos/mysql-net/MySqlConnector/compare/…`).
| | 2.6.1 (2026-06-25) | **2.6.2 (2026-08-11 · 추천)** |
|---|---|---|
| 들어 있는 것 | GHSA 봉합 · `Prepare` + `INSERT … RETURNING` 수정(#1652) | 2.6.1 전부 + `SkipCertificateRevocationCheck` 연결 옵션(#1615 · **opt-in**) |
| 2.6.1→2.6.2 원본 변화 | — | `ServerSession.cs` +27/−9 = 새 옵션 분기(`SslMode=VerifyFull` 일 때만) + 인증서 오류 상세를 **Trace 로그**로(로그 수준 Trace 일 때만) · `Adler32.cs` 삭제(커밋 「Delete dead code」) · 나머지는 시험·문서·CI |
| 우리 경로에 닿나 | 2.6.0 변화 전부 + GHSA 봉합(§5) | 2.6.1 과 **같다** — SslMode 지정 0(1-V §2) · MySqlConnector Trace 로그 배선 0(grep `OpenTelemetry\|ActivityListener` src 0) |
| 장점 | 고친 판 중 변화 최소 · 회귀 시 용의자 적음 | 최신 · 2.6.1 대비 우리 경로 동작 차 0 · 출시 11주(2주 규칙 넘음) · 다음 권고 때 한 칸 덜 감 |
| 단점 | 곧 낡음 | 추가분 7파일만큼 「안 쓰는 코드」 가 더 들어옴 |
- **추천 2.6.2** — 위험은 2.6.0 에 몰려 있고 두 판 모두 그것을 품는다. 2.6.2 의 더한 것은 opt-in 옵션·Trace 로그·죽은 코드 삭제뿐이라 우리 동작 차가 없다. PM 이 「최소 변화」 를 우선하면 2.6.1 도 같은 시험으로 통과 판정 가능.

## §5 [위험 자리] 2.6.0~2.6.1 동작 변화 × 우리 코드
### §5-1 SQL 매개변수 해석 강화 (2.6.0) — 정적 grep 전수가 1차 증거
상류 이슈 원문으로 범위를 좁혔다: #1627 = 따옴표 매개변수(`@'이름'`) 뒤 문자열 · #1624 = 하이픈 두 개 + **탭** 주석 · #1632 = `Parameters[i] = …` 이름 중복 시 컬렉션 깨짐.
| G | 무엇 | 명령(레포 루트 · `src` 제품 코드 · 시험 제외) | 지금 | 판정 |
|---|---|---|---|---|
| G1 | 따옴표 매개변수 | `grep -rnE "@['\"\`][A-Za-z_]"` (.cs · .sql) | **0** | 무관 |
| G2 | 탭 주석 | `grep -rnP` 「하이픈2+탭」 (.cs · `installer/**/*.sql` · `Migrations/SQL`) | **0** | 무관 |
| G3 | 줄끝 하이픈2(뒤가 줄바꿈) | `grep -rnE "\-\-\s*$"` | .cs 19줄(예: `CsRequestController.cs:360` · `EmployeeService.cs:84`) · .sql 91파일 | 신 판은 주석 시작으로 봄 · 구 판은 아님 — **뒤에 글자가 없어** 결과 같음(이론). [4] 가 19줄 뒤에 글자 0 인지 재확인 |
| G4 | 매개변수 인덱서 대입 | `grep -rnE "Parameters\[[^]]+\]\s*="` | **0** | 무관 |
| G5 | 직접 `Parameters.Add*` | `grep -rn "Parameters\.\(AddWithValue\|Add\)("` | 13줄 · 2파일(`MdbMigrationService.cs:6309~6317` · `SchemaMigrator.cs:154·155·500`) · 이름 중복 0(읽음) | 낮음 |
| G6 | 나머지 1,430여 곳 | Dapper 호출 | 매개변수는 Dapper 가 이름 1번씩 넣음 | G1~G4 가 0 이면 해석 변화 노출 없음 · 동작 증거는 §6 CI |
| G7 | `BINARY(32/36)` (#1625) | `grep -rliE "binary\((32\|36)\)"` 모든 .sql | **0** (`binary(16)` 은 `DB-60_backoffice_v1.sql` 1곳 — #1625 범위 밖) | 무관. ERP `GuidFormat=None`(`ConnectionStringGuidGuardTests`) · 백오피스는 기본값이나 `char(36)` 만 → 영향 없음(1-V 의 「GuidFormat 확인 필요」 를 이걸로 닫는다) |
| G8 | traceparent 전송(#1635) · `EnableResultSetHeaderEvent`(#1633) · DI API 이동(#1649) | `grep -rn "OpenTelemetry\|ActivityListener\|AddSource(\|EnableResultSetHeader\|AddMySqlDataSource"` | **0** | 상류 코드상 trace 정보는 MySqlConnector 활동(Activity)이 있을 때만 실림 → 듣는 쪽 0 ⇒ 미전송 |
| G9 | `RETURNING`·`Prepare` · 음수 DATETIME(#1630) · XA(#1631) | `RETURNING` 0 · `PrepareAsync` 1(`MdbMigrationService.cs:6318`) · `DateTime.MinValue` 2(`CsOutboxSenderWorker.cs:38` · `WatchdogUpdateCoreAdapter.cs:340`) · XA 0 | 낮음 — [3] 이 MinValue 2곳이 SQL 매개변수로 가는지 읽어 개발명세서에 적는다 |
- 🔴 G1~G4·G7·G8 은 **c2 커밋 뒤 [3]·[4] 가 각자 다시 돌려** 숫자를 개발명세서·작업리뷰서에 적는다(작성자 숫자 받아쓰기 금지).

### §5-2 🆕 비번 전송 조건 변화 (2.6.1 GHSA 봉합) — CI 사각
- 2.6.1 `ServerSession.cs` 원본: 평문 비번 전송(`SendClearPasswordAsync`) 앞에 **「loopback·유닉스 소켓이거나, TLS 인증서 검증 오류 0」** 조건이 새로 붙었다. `caching_sha2_password` 의 캐시 실패(전체 인증) 경로가 TLS 위에서 이 함수를 탄다.
- ⇒ **원격 DB + `SslMode` 기본(Preferred) + 서버 자체서명 인증서 + MySQL 8 기본 인증** 이면, DB 재시작 뒤 첫 인증 등 캐시 실패 때 **연결이 거부**된다(「requires a secure connection with a verified server certificate」). 2.5.0 은 보냈다.
| 시스템 | 연결 | 판정 |
|---|---|---|
| ERP (MariaDB) | `DB_HOST=localhost`(1-V §2) | loopback ⇒ **변화 없음** |
| 백오피스 (MySQL 8) | 레포 `appsettings.json` localhost · 설계 기록 「같은 서버에 직접 설치 · Cloud DB 안 씀」(`docs/운영기록/20260606_PM_종합_인수인계서.md:42` · `docs/개발/erp/20260606_토_NCP_인프라_토대_완료.md:49`) | 기록대로면 loopback ⇒ 변화 없음 · **운영 실값 ⚠️미측정**(1-V 미측정 1) → K-4 |
| CI 게이트 | 전부 `127.0.0.1` | loopback ⇒ **이 변화를 못 잰다** — CI 초록은 이 축의 증거가 아니다 |

## §6 [대조군 · CI 게이트] (이 PC DB 시험 금지 → CI 1순위)
### §6-1 감사가 실제로 돌았다는 증거 (10/14 교훈 — 초록이 「감사 안 돎」 이 아님을 보인다)
| 증거 | 어떻게 | 기대 |
|---|---|---|
| ⓐ 심은 빨강 (CI) | c1(NoWarn 만 거둠 · 판 2.5.0 그대로) 푸시 → Build 잡 로그 | restore NU1902 **4줄**(Application 새로 포함) · build 오류 그 2배 · `error CS` 0 ⇒ 감사가 돌고 있고 Application 가림이 풀렸다 |
| ⓑ 초록 (CI) | c2(판 올림) 푸시 → Build 잡 로그 grep | `NU190` **0** · `NU1900`(자료 조회 실패) **0** · `NU1605`·`NU1608` **0** |
| ⓒ 양성 대조 (로컬) | c2 뒤 `dotnet list src/HitPan.sln package --vulnerable --include-transitive` | MySqlConnector **0** · 간접 high 4종(1-V ⑤)은 **계속 보임** ⇒ 취약점 자료가 살아 있는 상태에서의 0 이다 |
| ⓓ 로컬 restore | c2 뒤 `dotnet restore src/HitPan.sln` force-evaluate | NU19xx 0 (c1 상태에선 4줄 — [3] 이 두 숫자 다 적는다) |
- ⓐ 를 CI 에서 할지 로컬만 할지 = K-3 (추천 CI 1런 — 「CI 가 Application 을 다시 본다」 는 CI 로그로만 증명된다).

### §6-2 CI 게이트별로 무엇을 잡나
| 게이트 (build-and-test.yml) | 이 차수에서 잡는 것 | 증거로 쓸 때 조건 |
|---|---|---|
| Build (errors 0 + warnings 0) `:18` | NU19xx · NU1605 · 컴파일 · 단위·통합 시험(`ConnectionStringGuidGuardTests` 포함) | §6-1 ⓑ grep 결과를 붙인다 |
| **DB 게이트 (Integrity)** `:288` — MariaDB · 출하 DDL | 2.6.x 로 실제 Dapper SQL·트랜잭션·날짜 경로 (G6·G9) · EF(Pomelo) 경로 | 초록 + `[SKIP]` 0(`:1354`) + **실행 수가 직전 main 초록 런과 같다**(줄어든 초록 금지) + AppDbContext 쓰는 게이트 1건 이상 실행(예: `StockWarehouseIntegrityGateTests` · `EmployeeAccountLinkGateDbTests` — [4] 가 trx 에서 이름 확인) |
| **백오피스 스키마 게이트 (MySQL 8)** `:1477` | 백오피스 `SchemaMigrator`(`MySqlCommand` 로 .sql 실행 · G3 의 .sql 91파일) · MySQL 8 인증 | 초록 + 실행 수 동일 · 🔴 loopback 이라 §5-2 는 못 잼 |
| DDL Smoke `:1378` | **무관** — mysql 클라이언트로 DDL 만 넣음(MySqlConnector 안 씀) | 초록이어도 이 차수 증거 아님 |
| 권한 메뉴 · 마이그 번호 | 무관 | — |
- 커버 한계(정직하게): DB 게이트는 필터 목록만 돈다(`:364~` 「업무 로직 게이트 12종」 등) — Dapper 1,430여 곳 전부가 아니다. 그래서 해석 변화는 §5-1 정적 grep(전수) + CI(대표 경로 동작) **두 겹**으로 본다.

## §7 [안 할 것]
- 억제 일체: `NoWarn`·`WarningsNotAsErrors`·`NuGetAuditSuppress`·`NuGetAudit=false`·`NuGetAuditLevel` 올림 · 조회 건너뛰기(no-restore 꼼수) · `TreatWarningsAsErrors` 끄기 (build-and-test.yml 10/14 주석 · #19·#25).
- 다른 꾸러미 판 올림(Pomelo 포함 — 범위식 그대로). 간접 high 4종(1-V ⑤)은 **별건 기록만** — 판 올림 뒤 `--include-transitive` 결과가 바뀌는지만 개발명세서에 적는다.
- 제품 코드(.cs) 수정 · 연결 문자열에 `SslMode`·`SslCa` 등 추가 — 필요해지면 **멈추고 보고**.
- 워크플로 수정(`build-and-test.yml` 주석이 NU1903 시절 이야기로 낡은 것 포함 → 별건 후보) · `deploy-*`·`build-installer`·`watchdog-ci` 무접촉 · `appsettings*.json`(#21).
- 이 PC 에서 `dotnet test`·앱 실행·DB 접속(로컬 3306 = 실물) · 운영(NCP·demo) 쓰기(#39).
- ERP 게시 dispatch 를 이 차수 안에서 하지 않는다(K-6).

## §8 [갈래 · 규모 · 런 상한 · 멈춤]
### §8-1 순서
1. [3] c1(§3-2) → 로컬 restore NU1902 4줄 확인 → (K-3 이 CI 면) 푸시 · 런 1 · Build 빨강 사유가 NU1902 단독인지 로그로 확인.
2. [3] c2(§3-1) → 로컬 restore 0 · `dotnet build src/HitPan.sln -c Release` TWAE + warnaserror 0/0 · IntegrationTests(+K-2 tools) **빌드만** 0/0 · §6-1 ⓒ → 푸시 · 런 1.
3. [3-V] 보안상무 — 도는 중에: §5-2 운영 DB 위치 판정 근거 · 억제 0 grep · 로그·문서에 자격증명 모양 0.
4. [4] 검증팀장 — c1 부터 동시 착수: §5-1 grep 재실행 · §6-2 조건 · 독립 반증 3건+(예: 판 갈림 grep · c1 런에서 Application 줄 실재 · DB 게이트 실행 수 대조).
5. PM 결재 → (CTO [5]) → 머지 → deploy-ncp 빌드 잡 초록 확인(1-V ③ 예측 해소 실측) → K-5.
### §8-2 런 상한 = 4 (c1 1 · c2 1 · 여유 2). 수동 재실행·추가 푸시 모두 센다. 시험 실패는 재실행 금지.
### §8-3 멈춤 (넘지 말고 보고)
- `.cs` 를 고쳐야 한다 · 억제가 필요해 보인다 · NU1605/NU1608/NU1903 등 새 NU 가 나온다 · DB 게이트·백오피스 게이트가 NU 아닌 사유로 빨강 · 실행 수가 줄었다 · Pomelo 쪽 `MissingMethodException`·`TypeLoadException` · 런 상한 초과.

## §9 [확인 필요 K-n] (PM 결정용 · 선택지 + 추천)
| K | 물음 | 선택지 | 추천 |
|---|---|---|---|
| K-1 | 판 | ⓐ 2.6.2 ⓑ 2.6.1 | **ⓐ** (§4) |
| K-2 | tools 3곳(§2-1 #6~#8)도 올리나 — 오더 「5곳」 밖 | ⓐ 8곳 함께 ⓑ 5곳만(tools 는 별건) | **ⓐ** — 안 올리면 NU1605 로 tools 가 조용히 깨짐(예측 · CI 무측정). 오더 문언 밖이므로 PM 이 대표께 한 줄 보고 필요 여부 판단 |
| K-3 | 심은 빨강 ⓐ 를 CI 에서 | ⓐ CI 1런(c1 따로 푸시) ⓑ 로컬만 | **ⓐ** (§6-1) — 런 상한 4 안 |
| K-4 🔴 | 백오피스 운영 DB 가 loopback 인가(§5-2) | ⓐ 배포 승인 전 PM 이 NCP 연결 문자열에서 **Server 항목만** 읽어 기록(읽기만 #39 · 외부 클라우드 접속이므로 대표 승인 #29) ⓑ 설계 기록(6/06 「같은 서버」)을 근거로 확인 생략 | **ⓐ** — 기록은 6월 것이고 1-V 도 미측정으로 남겼다 · 원격이면 머지 전 멈춤(연결 거부 위험) |
| K-5 🔴 | 머지 뒤 자동 배포 런의 승인 | ⓐ K-4 가 loopback 이면 대표 승인으로 즉시 승인 ⓑ 확인 못 하면 **즉시 취소**하고 기록 | ⓐ, 안 되면 ⓑ — 묵은 승인 대기 런 금지(되돌림 폭탄 선례) |
| K-6 | ERP 쪽(고객 PC) 반영 | ⓐ 다음 정기 게시에 실음 ⓑ 이 차수 전용 게시 | **ⓐ** — 고객 위험 0(1-V §2) · 게시는 PM 직접 dispatch 규칙(PM 전결) |
| K-7 | #699 초록 받는 길 — head `0b2f9343` 는 [4-X] 2/3 기준 SHA 후보 | ⓐ 이 차수 머지 뒤 #699 를 닫았다 다시 열어 새 병합 ref 로 런(head 불변) ⓑ main 을 #699 에 합쳐 푸시(head 바뀜 → [4-X] 기준 재결정) | **ⓐ** — ⚠️ `reopened` 런이 새 main 위 병합 ref 를 쓰는지는 런 로그의 체크아웃 SHA 로 확인 · #699 런 상한에 셈 |
| K-8 | 간접 high 4종 | 별 차수 [1-V] | 기록만(이 차수 밖) |

## §10 [DoD · 롤백]
- DoD (전부 · 숫자로):
  ① 판 갈림 0 — `grep -rn 'Include="MySqlConnector"' --include=*.csproj src tools`(주석 줄 제외)가 전부 K-1 판 · 5줄(K-2 ⓐ 면 8줄)
  ② 억제 0 — `grep -rn "NU1902\|NuGetAudit\|WarningsNotAsErrors" --include=*.csproj --include=*.props src tools` 결과가 주석 안뿐
  ③ 로컬: restore NU19xx 0 · sln Release TWAE+warnaserror 0/0 · IntegrationTests(+tools) 빌드 0/0 · `--vulnerable` MySqlConnector 0 · `--include-transitive` 결과 기록(4종 그대로인지 · 바뀌면 적는다)
  ④ CI: §6-1 ⓐ(K-3 ⓐ 면) 빨강 사유 NU1902 단독 4곳 · ⓑ Build 초록 NU190x 0 · NU1900 0 · DB 게이트 초록 SKIP 0 실행 수 동일 · 백오피스 스키마 게이트 초록 실행 수 동일 · CodeQL·TruffleHog 초록 · 런 번호·런 수(≤4)
  ⑤ §5-1 G1~G4·G7·G8 를 [3]·[4] 가 각자 재실행한 숫자
  ⑥ [3-V]: K-4 판정 근거 · 자격증명 모양 0 · [4]: 독립 반증 3건+
  ⑦ 머지 뒤: deploy-ncp 빌드 잡 초록(런 번호) · 배포 잡 승인/취소 기록(K-5)
  ⑧ 문서: 개발명세서 · 작업리뷰서 · 폴더 INDEX 한 줄(#41) · 1-V ③④ 상태 갱신
- 롤백: PR 커밋 되돌리기(`git revert`) 하나 — 2.5.0 + NoWarn 으로 돌아가고 Build 잡은 알려진 NU1902 빨강으로 복귀(제품 동작은 오늘과 같음). 백오피스가 배포 뒤 DB 연결에 실패하면 → 직전 배포본으로 되돌림(10/2 NCP 되돌리기 선례 · ⚠️ 이 차수에서 절차 재실측 안 함) 후 §5-2 원인 조사. DDL·데이터 변경 0 이라 DB 쪽 되돌릴 것 없음.
- [4-X] 대상 여부는 WORKFLOW §8-1 순서(3인 → CTO → 대표)로 정한다 — 이 문서가 정하지 않는다.

## 미측정 (⚠️)
1. 판 올린 뒤의 모든 숫자(NU19xx · 빌드 0/0 · 게이트) — 코드 수정 0 이라 안 쟀다.
2. tools 3곳 NU1605 — 예측(§2-2).
3. 백오피스 운영 DB 위치(§5-2 · K-4).
4. Pomelo 8.0.0 × MySqlConnector 2.6.x 실행 호환 — nuspec 하한만 확인 · 실행 증거는 DB 게이트 EF 경로.
5. `reopened` 런의 병합 ref 동작(K-7).

## §PM PM 결재 (브라운킴 · 10/30)
- 🟢 **결재** — 판 2.6.2 · csproj 판 줄 + `Application.csproj:47` NoWarn 거둠 · 제품 `.cs`·워크플로·DDL 0 · 억제 0 · 런 상한 4 · 순서 §8-1.
- PM 대조(직접 확인): tools 3곳(`MdbMigrationBaselineRun.csproj:30` · `MigrationIdempotencyCheck.csproj:24` · `MigrationSmokeTest.csproj:18`)이 2.5.0 을 직접 참조하면서 `src/` 프로젝트를 참조한다 — §2-2 사실과 같다 · `deploy-ncp.yml:31~35` 트리거 경로에 `src/HitPan.Backoffice.API/**` 가 있다 → 이 차수 머지 = 백오피스 자동 배포 런 발생(K-5) — 사실과 같다.
- 확인 필요 결정:
  - K-1 = **ⓐ 2.6.2** · K-3 = **ⓐ CI 1런**(c1) · K-6 = **ⓐ 다음 정기 게시** · K-8 = 기록만.
  - K-2 = **ⓐ 8곳 함께 · 🔴 대표께 한 줄 보고**(오더 「5곳」 밖 — 같은 꾸러미 판 맞춤이고 안 올리면 tools 가 깨지는 예측) · 대표가 「5곳만」 이면 tools 3줄을 빼고 별건으로 돌린다(구현은 진행).
  - K-4 · K-5 = 🔴 **대표 결재 사항** — 머지 상신 때 함께 여쭌다(운영 백오피스 연결 문자열 Server 항목 읽기 = #29 사전 승인 · #39 읽기만 / 자동 배포 런 승인·취소). PM 은 추천 ⓐ(읽어서 loopback 이면 승인 · 아니면 머지 전 멈춤 · 확인 못 하면 런 즉시 취소)을 올린다. 🔴 묵은 승인 대기 런 금지.
  - K-7 = **보류** — #699 머지 상신 때 3인 → CTO → 대표로 정한다([4-X] 2/3 라운드 중 #699 force-push·rebase 금지 = 대표 결재 CTO 조건 4 · ⓐ 닫았다 다시 열기는 이와 맞음 · ⓑ 는 라운드 중 불가).
- 커밋·푸시 운용: [3] 은 c1·c2 를 **로컬 커밋까지만**(파일 이름 하나씩 add · `-F` 메시지 · push 0) → PM 이 c1 을 먼저 푸시(초안 PR) → 런 판독 → c2 푸시 → 런 판독. 검토용 브랜치 푸시는 [6](main 머지)과 다르다(WORKFLOW §8-1 #2-a).

## §PM2 PM 확인 — CI 판독 · 대표 확인 · [3-V]·[4] (브라운킴 · 10/30)
- 🟢 **K-2 대표 확인** — 원문 「머지 완료. 이의없음 그대로 가」(10/30) → 8곳(src 5 · tools 3) 그대로.
- **CI 런 2회 / 상한 4**:
  - c1 `f2ee7f73`(대조군) — Build & Test 런 `41008333213` 🔴 · 사유 **NU1902 단독** · `error NU1902` 4곳 × 2(API · **Application** · Backoffice.API · Infrastructure) · `error CS` 0 → §6-1 ⓐ 기대와 같다(감사가 돌고 · Application 가림이 풀렸다).
  - c2+c3 `bfe2932c` — Build & Test 런 `41009025196` 🟢 전 잡 · Build 로그 `NU190x` **0** · `NU1605`/`NU1608` **0** · DB 게이트 합계 **813** = 기준 런 `41004671856`(`fa9b9687`) 813 · 「skipped」 줄 9 = 9 · 백오피스 스키마 게이트 9·6·4 = 기준 9·6·4 · CodeQL 🟢 · TruffleHog PR·push 🟢 → §6-1 ⓑ · §10 ④ 충족. (기준 런은 Build 잡이 NU1902 로 실패해 단위시험이 안 돌았다 — Build 잡 시험 수는 대조 불가 · c2 단위시험 172·2867·396·3·4 실행.)
  - 기록: c1 첫 push 의 TruffleHog **push 런** `41008324839` 🔴 = 새 가지 첫 push 때 BASE 없음 → 이력 전체 검사에서 **옛 미검증 결과 6건**(c1 SHA 는 HEAD 로만 등장) · 같은 날 문서 가지 3개(`68eb42f7`·`aa819f64`·`4a43597f`)도 같은 꼴 · PR 런은 🟢 → 이 차수와 무관 · **별건 후보**(새 가지 첫 push 의 이력 전체 검사 잡음).
- **[3-V] 보안상무 🟡**(병렬이슈 `20261030_3V_MySqlConnector판올림_보안상무.md`) — 봉합 진짜(억제 0 · 판 하나 · 자격증명 0) · 필수 **M-1 = K-4**(운영 BackofficeDb Server 값 loopback 여부 · 계정 인증 방식 — 장애는 배포 때가 아니라 **다음 DB 재시작 때** · 배포 헬스체크가 못 잡음) · 권고 R-1~R-4.
- **[4] 검증팀장 🟡**(작업리뷰서 `20261030_작업리뷰서_MySqlConnector판올림.md`) — 로컬 전부 재현 통과 · [3] 다른 점 4개 원인 설명됨 · Pomelo 공개 API 삭제 0 · 필수 M-1(CI 판독 = 위에서 충족) · M-2(K-4·K-5 대표 결재) · 권고 A-1(줄 끝 `--` + LF 면 다음 줄까지 주석 · 지금 노출 0) · A-2.
- **[4-X] 대상 여부** — 3인 결정 중(아래에 덧붙임).
- 남은 것: [4-X] 칸 → [5] CTO → 대표 머지 상신(K-4 운영 읽기 승인 · K-5 배포 승인/취소 함께).

### §PM2-1 [4-X] 대상 여부 — 3인 (10/30)
| 누구 | 의견 | 근거 |
|---|---|---|
| PM 초안 | ⬛ ~~대상 아님(꾸러미 판 줄뿐 · 제품 로직 0 · 위험은 레포 밖 운영 연결 = K-4)~~ → 🆕 **정정: 대상 · 질문 1** | 검증팀장 반증을 받는다 — 「제품 로직 0」 은 틀렸다(2.6.x 는 우리 SQL 전부를 해석하는 규칙이 바뀌었다) |
| 검증팀장 | **대상 · 질문 1개로 좁힘** | 해석기 변화(줄 끝 `--` + LF 주석 · 문장 분할 · 매개변수)는 레포 안 위험이라 외부가 정적으로 볼 수 있다 · 지금 반증은 검증팀장 혼자 만든 모사기(상류 원본 실행 아님) · `hitpan_db_clean.sql` 소비처 경로 미대조 → 독립 눈 한 번(SoD) |
| 보안상무 | 대상 아님(동의) · 조건: 운영 Server·plugin 값은 공개 문서·PR·커밋에 0 · 결과는 「통과/멈춤」 한 단어 | 남은 위험(M-1)은 레포 밖 운영 연결 · 공개 의뢰서가 운영 백오피스 끊김 조건에 시선을 모으면 안 된다 |
- **PM 종합 = 대상 · 질문 1 (2:1)** — 보안상무 반대 근거(운영 연결 노출)는 SQL 해석 질문과 겹치지 않는다 → 의뢰서에 **운영 연결·K-4 관련 글자 0**(보안 조건 그대로 받음).
- 대상(안): PR #729 차이(csproj 9개) + 레포 SQL(`.cs` 안 SQL · `.sql` · `hitpan_db_clean.sql` 과 그 C# 소비처) · 우리 판정문(작업리뷰서·병렬이슈) 링크 0(G1-3 H 2/3 과 같은 원칙).
- 질문(안 · 하나): 「MySqlConnector 2.6.x 해석기 변화(줄 끝 `--` 뒤 줄바꿈 처리 · 문장 분할 · 매개변수 치환)가 이 레포의 SQL 에서 매개변수가 치환되지 않거나 문장이 빠지는 자리를 만드는가」.
- 다음: CTO → 대표(최종 범위) → 발행. 라운드 1/최대 3 · 미회신 24시간 · G1-3 H 2/3 과 동시 진행(서로 다른 PR 댓글).
