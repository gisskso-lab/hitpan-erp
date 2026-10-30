# [3-V] MySqlConnector 2.5.0 → 2.6.2 판 올림 · NU1902 억제 거둠 — 보안상무 병렬검증

| 항목 | 값 |
|---|---|
| 검증자 | 보안상무 안철수 ([3-V] · 독립 · 만든 사람 = 설계팀장 존·개발 갈래·PM) |
| 대상 | 가지 `fix/20261030-MySqlConnector-판올림` · 바닥 `fa9b9687` → c1 `f2ee7f73` · c2 `c5e57046` · c3 `bfe2932c` · `git diff fa9b9687 bfe2932c -- src tools` (csproj 8개 · 판 줄 8 + NoWarn 1) |
| 근거 | 작업지시서 `docs/운영기록/20261030작_MySqlConnector판올림_작업지시서.md` §0·§5-2·§7·§PM · 개발명세서 `docs/개발/erp/20261030작_MySqlConnector판올림_개발명세서.md` · 선행검증 `docs/검증/선행/20261030_선행검증_MySqlConnector_NU1902.md` §1·§2·§6 · 로그 scratchpad `my3-logs/`(레포 밖 · grep 으로만) |
| 방법 | 읽기만 — git diff·git grep(커밋 `bfe2932c` 고정) · csproj XML 파싱(주석 제외 실효값) · 로그 grep. **빌드 0 · 앱 실행 0 · DB 접속 0 · 운영(NCP·백오피스 서버) 접속 0** |
| 판정 | 🟡 **조건부** — 코드 쪽 봉합은 진짜다(억제 0 · 판 하나 · 자격증명 0). 남은 하나는 **운영 백오피스 연결이 loopback 인지 레포로는 못 정한다**는 것 ⇒ 필수 M-1 |

---

## 1. 억제가 숨었나 — **없다** 🟢

### 1-1. 거둔 NoWarn 이 정말 사라졌나
- `src/HitPan.Application/HitPan.Application.csproj:56` 의 `<NoWarn>$(NoWarn);NU1902</NoWarn>` 는 `:54` `<!--` 로 열고 `:59` `-->` 로 닫는 **XML 주석 안**에만 있다. 바로 위 `:48~53` 주석은 `:53` 에서 이미 닫혀 있어 경계가 겹치지 않는다.
- XML 파서로 레포 전 csproj·props·targets 를 읽은 실효값(주석 제외): MySqlConnector 관련 `NoWarn` 0 · `WarningsNotAsErrors` 0 · `NuGetAudit*` 0 · PackageReference 단위 `NoWarn` 속성 0.
- 실효 `NoWarn` 은 `src/HitPan.Web/HitPan.Web.csproj:14` `MUD0002;RZ10012` 하나뿐 — 이 차수 전부터 있던 것 · NU 계열 아님 · 무관.
- 변경 8개 csproj 의 주석 안 하이픈 두 개 연속 = 0건(파일별로 셈) — 주석이 깨져 옛 줄이 살아날 길 없음.

### 1-2. 다른 자리에 새로 생겼나
- `git diff --stat fa9b9687 bfe2932c -- '*.props' '*.targets' '*nuget*' '.github' '*.yml' global.json` = **변경 0**.
- 레포 안 `Directory.Build.props` 1개(판 번호 단일출처만 · 억제 0) · `*.targets`·`nuget.config`·`packages.lock.json` 없음.
- 워크플로 억제 흔적: `build-and-test.yml:73` 은 `/p:TreatWarningsAsErrors=true /warnaserror` 그대로 · `codeql.yml:48` `TreatWarningsAsErrors=false` 는 이 차수 전부터(분석용 빌드) — 둘 다 diff 밖.

### 1-3. 대조군이 실제로 섰나
- 양성(c1 · NoWarn 만 거둠): `my3-logs/c1-restore.log` NU1902 `MySqlConnector 2.5.0` **4건** — 억제가 가리던 것이 MySqlConnector 하나였다는 작업지시서 §2-3 과 같다.
- 음성(c2 · 판 올림): `c2-restore.log`·`c2-restore-again.log`·`c2-build-sln.log` NU19xx **0** · `c2-build-sln.log` 경고 0 · 오류 0.
- 감사가 살아 있다는 표지: `c2-vuln-transitive.log` 에 간접 High 4종이 **계속 보인다**(§5) — 감사가 꺼져서 초록인 것이 아니다.

## 2. 판이 갈렸나 — **안 갈렸다** 🟢
- XML 파싱 실효값: MySqlConnector 직접 참조 **8곳 전부 `2.6.2`** — `src` 5(API · Application · Backoffice.API · Infrastructure · IntegrationTests) · `tools` 3(MdbMigrationBaselineRun · MigrationIdempotencyCheck · MigrationSmokeTest). `tests` 등 다른 csproj 에는 직접 참조 0. 레포 csproj 총 18개.
- 다른 MySQL 꾸러미: `src/HitPan.Infrastructure/HitPan.Infrastructure.csproj:32` Pomelo `[8.0.0,9.0.0)`(범위식 · 전부터) — 간접으로 MySqlConnector 를 끌지만 직접 참조 2.6.2 가 이긴다(NuGet 직접 우선). `MySql.Data` 0.
- tools·IntegrationTests 빌드: `c2-build-mdb/idem/it.log` 0/0 · `c2-build-smoke-plain.log` 경고 4 = CA1416 전부(기준 나무 `base-build-smoke-plain.log` 에도 있음 · 이 차수 무관).

## 3. 2.6.1 비번 전송 조건 변화 × 백오피스 운영 연결 — **⚠️미측정(운영 확인 필요)**

### 3-1. 레포 안 근거
| 자리 | 파일:줄 | 내용 |
|---|---|---|
| 조립 | `src/HitPan.Backoffice.API/Controllers/BackofficeAuthController.cs:150~152` (같은 꼴 컨트롤러 다수 · `Program.cs:163` 스키마 적용기) | `GetConnectionString("BackofficeDb")` 를 **그대로** 쓴다. 코드에서 `SslMode`·`Server` 를 덧붙이거나 바꾸는 자리 0 ⇒ 운영값이 곧 실효값 |
| 레포 기본값 | `src/HitPan.Backoffice.API/appsettings.json:12~13` | `Server=localhost` · `SslMode` 미지정(⇒ 드라이버 기본 `Preferred`) |
| 운영값이 사는 곳 | `installer/deploy/deploy-web.sh:61` | 배포 rsync 가 `appsettings.Production.json` 을 **exclude** — 운영 연결 문자열은 서버에만 있고 레포에 없다(설계상 맞음 · #22) |
| 시크릿 주입 | `scripts/deploy-ncp-secrets.sh` | systemd drop-in `Environment=` 등록 — 연결 문자열 키(`ConnectionStrings__*`) 등록 흔적 0 ⇒ 운영값은 위 Production 파일 쪽으로 보이나 **⚠️미측정** |
| 기록 | `docs/운영기록/20260606_PM_종합_인수인계서.md:42` · `docs/개발/erp/20260606_토_NCP_인프라_토대_완료.md:49` | 「백오피스 DB = 같은 서버 안에 직접 설치 · Cloud DB 안 씀」 |
| ERP 쪽 | 선행검증 §2 표(`InfrastructureExtensions.cs:20` `DB_HOST` 기본 localhost · 설치판 `DB_HOST=localhost`) · `docs/검증/erp/20260701_P0진범확정_검증게이트_forf_hitpan접속.md:16`(계정 플러그인 `mysql_native_password`) | loopback + caching_sha2 아님 ⇒ **안 닿음**(이중으로 밖) |

### 3-2. 왜 「같은 서버」 기록으로 닫을 수 없나 (반증)
- **「같은 서버」 ≠ loopback.** 운영 `Server=` 가 서버 자기 사설 IP·공인 IP·도메인이면 같은 기계여도 loopback 이 아니다 → 2.6.1 조건(loopback 아님 + 인증서 미검증 TLS + `caching_sha2_password` 전체 인증)에 걸린다.
- **배포 순간에는 안 터지고 나중에 터진다.** 전체 인증은 MySQL 쪽 인증 캐시가 비었을 때만 탄다(MySQL 재시작 · 권한 재적재 · 계정 변경). 배포는 앱만 재시작하므로 캐시가 살아 있어 빠른 인증으로 붙는다 ⇒ 다음 MySQL 재시작(OS 업데이트 등) 때 백오피스 전체가 DB 연결 거부.
- **배포 헬스체크가 못 잡는다.** `installer/deploy/deploy-web.sh:229` 는 `credentials/healthz` 응답 `200|401|403` 을 통과로 본다 — DB 연결 성립을 증명하지 않는다 ⇒ 자동 롤백도 안 돈다.
- **CI 는 이 축의 증거가 아니다.** CI DB 게이트는 전부 127.0.0.1(작업지시서 §5-2 표) — 초록이어도 이 위험은 0 으로 측정된 것이 아니다.
- 2.6.1 의 동작 변화 자체는 작업지시서 §5-2 의 원본 판독을 인용한 것이다. **보안상무가 드라이버 원본을 다시 읽지는 않았다(⚠️)**.

### 3-3. 판정
- ERP(고객 PC) = **안 닿음**. 백오피스 운영 = **⚠️미측정** — 기록대로(localhost·127.0.0.1·소켓)면 안 닿고, 자기 IP·도메인이면서 계정이 `caching_sha2_password` 면 **닿는다**(지연 장애).
- 머지 = `deploy-ncp.yml:31~35` 경로(`src/HitPan.Backoffice.API/**`)로 배포 런 자동 발생 · `:88` `environment: production` 승인 대기.

### 3-4. 대표께 올릴 운영 확인 질문 (한 줄)
> 「운영 백오피스 `BackofficeDb` 연결 문자열의 `Server` 값이 localhost·127.0.0.1·소켓 중 하나입니까(서버 자기 IP·도메인이면 아님), 그리고 그 DB 계정 인증 방식이 `caching_sha2_password` 입니까 — Server 칸과 plugin 칸만 읽기(#29 승인 · #39 읽기만 · 비번 칸 표시 금지)」

## 4. 자격증명 모양 — **0** 🟢
- 셈 대상: `git diff fa9b9687 bfe2932c` 추가 줄 전부(문서 3개 포함) · 커밋 메시지 3개 · `my3-logs/` 18개 파일.
- 셈 결과: 비번·토큰·키 모양(`pwd|password|secret|token|apikey` 뒤 값 · 클라우드 키 접두 · 개인키 머리 · JWT 머리 · 알려진 개발 비번 머리) **0** · 연결 문자열 조각(`Uid=` 등) **0**. 값은 출력하지 않고 개수만 셌다.

## 5. 간접 High 4종 — 이 차수로 **안 바뀜** · 별건 유지가 맞다
| 꾸러미 | 프로젝트(`c2-vuln-transitive.log`) | 등급 의견 |
|---|---|---|
| Newtonsoft.Json 11.0.1 | HitPan.Infrastructure → ERP API | 공유 프레임워크 소속이 아니라 **실제로 이 판이 실린다** ⇒ 4종 중 가장 실재 · 별 차수 1순위 |
| Microsoft.AspNetCore.SignalR.Common 8.0.11 | HitPan.Web (WASM) | WASM 은 공유 프레임워크가 없어 꾸러미 판 그대로 실린다 · 영향 경로 ⚠️미측정 · 2순위 |
| Microsoft.Extensions.Caching.Memory 8.0.0 | HitPan.Infrastructure | ASP.NET Core 공유 프레임워크 통일로 실행 판이 더 높을 수 있음 ⚠️미측정 |
| System.Text.Json 8.0.0 | HitPan.Watchdog · Watchdog.Tests | 런타임 공유 판 통일 가능 ⚠️미측정 · 워치독은 #43 불변 영역이라 별 차수에서도 결재 필요 |
- MySqlConnector 2.6.2 가 이 넷을 끌어오지 않는다(Backoffice.API 는 프로젝트 참조 0 · 넷 모두 판 올리기 전부터 있던 것 · 선행검증 30초 요약 7). 이 차수에 섞으면 범위 확장(작업지시서 §7) ⇒ **별건 유지 동의**.

---

## 6. 결론

### 필수 (머지 전)
- **M-1** K-4 를 머지 상신 때 함께 닫는다: §3-4 질문으로 운영 `Server`·plugin 두 칸 확인(대표 #29 승인 · 읽기만). loopback 이거나 plugin 이 caching_sha2 가 아니면 배포 런 승인 · loopback 아님 + caching_sha2 이면 **머지 전 멈춤**(대처 = `SslCa` 지정 또는 Server 를 loopback 으로 — 별 결재) · 확인 못 하면 배포 런 **즉시 취소**(묵은 승인 대기 런 금지). CI 초록을 이 항목의 근거로 쓰지 않는다.

### 권고
- **R-1** 8곳 판 맞춤이 사람 주석(「8곳이 같아야 한다」)에만 기대고 있다. CI 감사(`c2-vuln.log`)는 솔루션 12개 프로젝트만 보며 **IntegrationTests·tools 3곳은 감사·빌드 밖**이다 ⇒ 별건으로 중앙 판 관리 또는 판 일치 검사 게이트(XML 파싱 · 대조군 = 일부러 한 곳 낮춘 판이 빨강).
- **R-2** 종전 줄을 주석으로 남겨(#1) 글자 grep 은 `MySqlConnector ... 2.5.0` 을 8건 더 본다. 판 검사·취약 검사 장치는 글자가 아니라 XML 실효값으로 재야 한다(주석 안 2.5.0 이 걸리지 않음을 대조군으로 보일 것).
- **R-3** 배포 헬스체크(`deploy-web.sh:229`)에 DB 연결 성립을 확인하는 칸이 없다 — 이번 같은 지연 장애를 배포가 못 잡는다. 별건 후보(#29 영역 · 결재 필요).
- **R-4** 간접 High 별 차수 [1-V] 순서 = Newtonsoft.Json → SignalR.Common → 나머지 2종(실행 판 실측 먼저).

### 기록 (이 차수 무관 · 전부터)
- `src/HitPan.Backoffice.API/appsettings.json:12~13` · `tools/MigrationSmokeTest/Program.cs:14` 에 개발용 연결 문자열이 레포에 들어 있다(값 미표시) — #21 로 appsettings 는 손대지 않는 영역 · 운영값은 서버 Production 파일(exclude)이라 이 길로 운영 비번이 새지는 않음.
- `tools/MigrationSmokeTest/Program.cs:14` `SslMode=None` — TLS 경로가 아니므로 2.6.1 변화 대상 밖(개발 도구 · localhost).
- `codeql.yml:48` TWAE 끔 · `HitPan.Web.csproj:14` NoWarn 2종 — 전부터 · NU 계열 아님.

## 미측정 (⚠️)
1. 운영 백오피스 `BackofficeDb` 의 `Server` 값 · `SslMode` · 계정 인증 플러그인 — #29·#39 로 안 봄(M-1).
2. 2.6.1 드라이버 원본의 조건문 — 작업지시서 §5-2 인용 · 직접 재판독 0.
3. 간접 High 4종의 실행 시 실제 로드 판 · 영향 경로.
4. CI 런 결과(c2 푸시 뒤) — 이 문서는 로컬 로그와 커밋 내용만 봤다.
