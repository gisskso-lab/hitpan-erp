# [1-V] 선행검증 — MySqlConnector 2.5.0 보안 권고(NU1902 · GHSA-473q-m89c-ghf8)로 CI `Build` 잡 간헐 빨강 (2026-10-30)

| | |
|---|---|
| 오더 | 대표 결재 10/30 「추천대로」 — `docs/운영기록/20261030_결재_699외부검증2차범위착수_MySqlConnector선행검증_대표.md` (⚠️ 이 문서 작성 시점 origin/main `0b43a39e` 에는 아직 없음 · PM 가지에 있는 것으로 봄) |
| 담당 | [1-V] 검증팀장 데이비드 박 (hp-verifier · 읽기와 측정만) |
| 기준 | origin/main `0b43a39e` 를 뽑은 격리 worktree (가지 `docs/20261030-02-선행-MySqlConnector-NU1902`) |
| 접촉 | 코드·csproj·꾸러미 0줄 · CI 재실행·dispatch 0 · 운영(NCP·demo) 0 · DB 0 · 앱 실행 0 · 로컬은 `dotnet restore --force-evaluate` · `dotnet list package` 만 |
| 판정 | **조건부 통과(착수 가능)** — 권고 실재 · 우리 제품은 취약 조건 밖(코드 근거) · 간헐 원인은 **가설 2단**(측정으로 좁힘, 확정 아님) · 🔴 **새 적발 3건**(아래 30초 요약 ③④⑤) |

---

## 30초 요약

1. **권고 실체** — `SslMode=VerifyCA/VerifyFull` + `SslCa` 없음 + 비밀번호 있음 조합에서, 중간자가 아무 자체서명 인증서로 TLS 를 맺고 평문 비밀번호 인증을 요구하면 **인증서 확인 전에 비밀번호가 나간다**(CWE-200 · moderate). 영향 `< 2.6.1` · **고친 판 2.6.1**(2026-06-25 출시) · 최신 2.6.2. GitHub 권고 게시 = **2026-10-28 19:49 UTC**.
2. **우리는 취약 조건 밖** — 레포 전체(`src`·`installer`·`.github`·`scripts`)에 `SslMode`·`SslCa`·`VerifyFull`·`VerifyCA` **0건**(도구 1곳 `SslMode=None` 제외) ⇒ 기본값 `Preferred` = 권고 범위 밖. ERP 는 `DB_HOST=localhost`(loopback = 권고가 명시한 비해당). 백오피스 운영 연결 문자열은 서버 환경에 있어 ⚠️미측정.
3. 🔴 **간헐 원인(가설 · 측정으로 좁힘)** — 빨강/초록 런의 이미지·SDK·명령은 **같다.** 차이는 `dotnet restore` 단계에서 **권고 줄이 나왔냐 안 나왔냐** 하나뿐. ⓐ 권고 게시 후 약 9시간 동안은 어느 지역 러너도 못 봤고(10/29 00:09 northcentralus 초록) ⓑ 10/29 04:54~06:23 에는 **러너 지역과 맞물려** 갈렸다(빨강 = northcentralus·centralus·chilecentral / 초록 = eastus·westus·westus3). ⇒ nuget.org 취약점 자료의 **반영 지연 + 지역별 배포 시차**로 본다. 10/30 04:20 런은 **westus3 에서도 빨강** ⇒ 지금은 「간헐」이 아니라 **계속 빨강**으로 넘어갔을 가능성이 높다(n=1).
4. **로컬 재현** — W 에서 `dotnet restore src/HitPan.sln --force-evaluate` → **NU1902 3줄**(API · Infrastructure · Backoffice.API 각 1). CI 9줄 = restore 3 경고 + build 6 오류(3곳 × 2번).
5. 🔴 **새 적발 ③ 백오피스 배포가 막힌다(예측)** — `deploy-ncp.yml:62` 이 `HitPan.Backoffice.API` 를 `/p:TreatWarningsAsErrors=true` 로 **restore 포함 빌드** ⇒ 다음 백오피스 배포는 NU1902 오류로 멈출 것. 마지막 배포 런 = 10/27(권고 게시 전) ⇒ 아직 아무도 안 맞았다. ⚠️ 실제 배포 런으로는 미측정(돌리지 않음). ERP 게시(`deploy-update.yml`)·설치판(`build-installer.yml`)은 경고 승격이 없어 **안 막힌다**.
6. 🔴 **새 적발 ④ `HitPan.Application` 이 NU1902 를 통째로 끈다** — `src/HitPan.Application/HitPan.Application.csproj:47` `<NoWarn>$(NoWarn);NU1902</NoWarn>`. 사유 주석(:41-46)은 MailKit 4.14 권고였는데 지금 MailKit 은 4.16.0(:15)이고 `--vulnerable` 에 안 걸린다 ⇒ **이 억제가 지금 가리는 것은 MySqlConnector 하나뿐.** 앞으로 들어올 moderate 권고도 이 프로젝트에선 조용히 묻힌다.
7. 🔴 **새 적발 ⑤ 간접 꾸러미의 high 권고 4종이 감사에서 빠져 있다** — net8 대상 프로젝트는 NuGet 감사가 **직접 참조만** 본다. `dotnet list package --vulnerable --include-transitive` 결과: `Microsoft.Extensions.Caching.Memory 8.0.0`(High) · `Newtonsoft.Json 11.0.1`(High) · `Microsoft.AspNetCore.SignalR.Common 8.0.11`(High · Web) · `System.Text.Json 8.0.0`(High · Watchdog). 이번 차수 범위 밖 — 별건으로 올린다.
8. **추천(결정은 대표)** — ㉠ **2.6.1(또는 2.6.2)로 4곳 함께 올림** + 판 올림 전 시험 2개 축(아래 §6). ㉡ 억제는 비추천(#19 문언은 지켜도 #25 「안전하게」·10/14 워크플로 주석 「NoWarn 추가 금지」와 충돌). ㉢ 방치는 백오피스 배포 정지 때문에 불가.

---

## 1. 권고 실체

명령: `MSYS_NO_PATHCONV=1 gh api advisories/GHSA-473q-m89c-ghf8`

| 항목 | 값 |
|---|---|
| 제목 | MySqlConnector: Account password disclosed to a man-in-the-middle in zero-configuration TLS mode |
| 심각도 · CWE · CVE | medium · CWE-200 · CVE 없음 |
| 게시 | 2026-10-28T19:49:44Z (github_reviewed 같은 시각) |
| 영향 판 | `MySqlConnector < 2.6.1` |
| 고친 판 | **2.6.1** (릴리스 2026-06-25 — 「Fix GHSA-473q-m89c-ghf8」) · 그 뒤 2.6.2(2026-08-11 · `SkipCertificateRevocationCheck` 추가) |
| 취약 조건(전부 충족 시) | ① `SslMode = VerifyCA` 또는 `VerifyFull` ② `SslCa` 미지정 ③ 비밀번호 비어 있지 않음 |
| 비해당(권고 원문) | `SslCa` 지정 · CA 검증 TLS · **로컬 연결(Unix 소켓 / loopback)** · 빈 비밀번호 · `SslMode=Preferred/Required`(원래 서버 인증을 안 하는 모드라 범위 밖) |
| 우회책(권고 원문) | `SslCa` 지정 · 로컬 연결 · 평문 인증 회피 |

nuget.org 취약점 자료 실측(10/30 04:24 UTC · `https://api.nuget.org/v3/vulnerabilities/index.json`):
- base = `2026.10.28.05.41.06`(권고 게시 전) — 이 권고 **0건**
- update = `2026.10.28.05.41.06/2026.10.29.23.41.18/vulnerability.update.json`(116바이트) — 내용 = `mysqlconnector` · GHSA-473q-m89c-ghf8 · severity 1 · `(, 2.6.1)` **1건뿐**
- 색인 응답 머리 `Cache-Control: max-age=0, no-cache, no-store` · 3회 반복 조회 모두 같은 update 경로

## 2. 우리가 그 기능을 쓰나 — **안 쓴다(코드 근거) · 백오피스 운영값은 ⚠️미측정**

grep(대소문자 무시) `ssl ?mode|SslCa|VerifyFull|VerifyCA|MySqlSslMode|ssl-?mode|AllowPublicKeyRetrieval|clear_?password` — `docs`·`node_modules` 제외 레포 전체:
- 일치 = `tools/MigrationSmokeTest/Program.cs:14` `SslMode=None` 1건(개발 도구) · 나머지는 시험 코드의 `VerifyCalls` 변수명(무관).
- `src` 제품 코드·`installer`·`.github/workflows`·`scripts` 에 SSL 모드 지정 **0건** ⇒ MySqlConnector 기본값 `Preferred` ⇒ 권고 범위 밖.

연결 문자열 조립 자리(대표): `src/HitPan.Infrastructure/Extensions/InfrastructureExtensions.cs:55` · `src/HitPan.Infrastructure/Persistence/AppDbContextFactory.cs:21` · `src/HitPan.Infrastructure/Persistence/MigrationDbConnectionFactory.cs:56` · `src/HitPan.API/HostedServices/*` 4곳 · `src/HitPan.API/BackgroundServices/CsOutboxSenderWorker.cs:387` · `src/HitPan.API/Controllers/WebhookInboundController.cs:268` — 전부 SSL 항목 없음.

| 시스템 | DB 위치 | 판정 |
|---|---|---|
| ERP (MariaDB) | `DB_HOST` 기본 `localhost` — `InfrastructureExtensions.cs:20` · `CsOutboxSenderWorker.cs:379` · `WebhookInboundController.cs:262` · 설치판 `installer/HitPan-Universal.iss:2456` `DB_HOST=localhost` | loopback = 권고 비해당 + SSL 모드 미지정. **이중으로 밖** |
| 백오피스 (MySQL 8) | 레포 `src/HitPan.Backoffice.API/appsettings.json:12-13` = localhost · SSL 미지정. 운영값은 `Program.cs:163` `GetConnectionString("BackofficeDb")` 로 서버 환경에서 읽음 | 레포 기준 밖. **운영 서버 실제 연결 문자열은 ⚠️미측정**(#39 · 이번 차수에서 안 봄) |

⇒ **이 권고로 고객 비밀번호가 새는 길은 코드상 없다.** 판을 올리는 이유는 「실제 위험」이 아니라 **#19 빌드 0/0 · 배포 정지 · 감사 가림 방지**다. (판단 근거가 되는 사실이므로 대표 결재 때 이렇게 읽혀야 한다.)

## 3. 왜 간헐인가 — 가설 2단 (확정 아님)

### 3-1. 경고가 오류로 바뀌는 자리 (확정)

- `Directory.Build.props` — 버전만, `NuGetAudit*`·`TreatWarningsAsErrors` **없음**(:31-48).
- 어느 csproj 에도 `NuGetAudit`·`NuGetAuditLevel`·`NuGetAuditMode` **없음** ⇒ SDK 기본값(감사 켜짐 · low 이상 · net8 대상 = 직접 참조만).
- `.github/workflows/build-and-test.yml:69` `dotnet restore src/HitPan.sln` → 이때 **경고** NU1902 3줄.
- `.github/workflows/build-and-test.yml:73` `dotnet build ... --no-restore /p:TreatWarningsAsErrors=true /warnaserror` → assets 파일에 남은 restore 경고를 다시 내면서 **오류** NU1902(3곳 × 2번 = 6줄).
- `HitPan.Application` 은 `:47` NoWarn 때문에 안 나옴 · `HitPan.IntegrationTests` 는 `src/HitPan.sln` 에 없어(grep 0) CI 가 restore 안 함 ⇒ 5곳 중 3곳만 찍힌다.

### 3-2. 빨강·초록 런 대조 (Build 잡 로그 · `gh api repos/.../actions/jobs/<id>/logs` 후 grep)

| 시각(UTC) | 커밋 | 잡 | 지역 | NU190x | 결과 |
|---|---|---|---|---|---|
| 10/28 19:51 | dbf2a10a | 123062023808 | centralus | 0 | 🟢 |
| 10/28 19:58~20:20 | 5런 | — | eastus·eastus·westus3·westus2·westus3 | 0 | 🟢 |
| 10/28 23:56~10/29 00:53 | 9런 | — | westus3·southcentralus·eastus·**northcentralus**(00:09)·eastus·eastus2·**centralus**(00:36)·westcentralus·westus | 0 | 🟢 |
| 10/29 03:45 · 03:47 | 2b20c563 · 02cb19ed | — | westus2 · westus2 | 0 | 🟢 |
| 10/29 04:01 | 3c22ac40 | 123255102930 | eastus | 0 | 🟢 |
| 10/29 04:34 | 1a142dd3 | 123265238354 | westus | **0** | 🔴 다른 원인(T7) |
| 10/29 04:54 | 817ea2ec | 123271097046 | **northcentralus** | 9 | 🔴 |
| 10/29 05:07 | 2546599a (main) | 123274914986 | westus3 | 0 | 🟢 |
| 10/29 05:18 | d3bd2841 | 123278186559 | eastus | 0 | 🟢 |
| 10/29 05:24 | b9921fff ★ | 123280197651 | **centralus** | 9 | 🔴 |
| 10/29 05:26 | 09a6e424 | 123280706964 | **northcentralus** | 9 | 🔴 |
| 10/29 05:31 | 0b43a39e (main) | 123282266413 | westus | 0 | 🟢 |
| 10/29 06:23 | 0b2f9343 (#699) | 123298303062 | **chilecentral** | 9 | 🔴 |
| 10/30 04:20 | 68eb42f7 | 123889541907 | **westus3** | 9 | 🔴 |

★ PM 사실 목록에 없던 4번째 빨강(런 40823210551).

**모든 런 같음**: 러너 이미지 `win25-vs2026/20261021.278` · 프로비저너 `20261021.618` · setup-dotnet 이 런타임 10.0.14 + SDK 8.0.427 설치 · 같은 명령. 캐시 단계 없음(`actions/cache`·setup-dotnet `cache:` 미사용 → 매번 빈 NuGet 캐시). 빨강/초록 차이는 **restore 단계 로그의 `warning NU1902` 유무 하나**(예: 123271097046 로그 196·199·204행). NU1900(취약점 자료 조회 실패) 줄은 어느 런에도 **0** ⇒ 초록 런은 「조회 실패」가 아니라 **「권고 없는 자료를 받았다」**.

**가설**
- **H1 반영 지연(유력)** — GitHub 게시(10/28 19:49) 뒤 nuget.org 자료에 들어가기까지 시간이 걸렸다. 첫 빨강 04:54 · 그 전엔 northcentralus·centralus 도 초록 ⇒ 약 9시간 동안 아무 지역도 못 봄.
- **H2 지역별 배포 시차(유력 · n 작음)** — 04:54~06:23 의 빨강 4건이 전부 중부·남미 지역, 초록 4건이 전부 동·서부 지역. 같은 시각대(05:24 centralus 빨강 / 05:31 westus 초록)에 갈림 ⇒ 시간 순서만으로는 설명이 안 되고 **지역(=nuget.org 배포망 접점)** 과 맞물린다.
- 10/30 westus3 빨강 ⇒ **지금은 배포망 전체에 퍼졌을 가능성**(n=1 · ⚠️ 더 재야 함).
- ⚠️ 10/29 당시 nuget.org 색인이 가리키던 update 경로 이력은 **지금 되짚을 수 없다**(update 디렉터리가 갱신마다 새 이름 · 목록 조회 불가) ⇒ H1·H2 는 런 로그와의 정합까지만 쟀다.

**10/14 가설과 대조** — 10/14 「통과 런엔 NU19xx 가 한 줄도 없다 = 감사가 결과를 안 남겼다」는 **이번에도 관측은 같다**(초록 런 NU190x 0). 다만 이번 측정으로 한 걸음 더 갔다: NU1900 도 0 이므로 「감사가 안 돌았다」가 아니라 **「돌았는데 받은 자료에 권고가 없었다」** 쪽이다. 10/14 메모의 「권고 DB 갱신 타이밍 의심」(build-and-test.yml:36 · HitPan.API.csproj:18)과 **방향이 같다**.

## 4. 로컬 재현

명령: W 에서 `dotnet restore src/HitPan.sln --force-evaluate` (빌드·실행 X · 출력은 scratchpad `my1v-logs/local-restore.log`)

| 항목 | 값 |
|---|---|
| 이 PC SDK | `dotnet --version` = **10.0.204** (global.json 없음 → 가장 높은 SDK) |
| 종료 코드 | 0 (경고는 restore 를 막지 않음) |
| NU1902 | **3줄** — `HitPan.API.csproj` · `HitPan.Backoffice.API.csproj` · `HitPan.Infrastructure.csproj` 각 1 |
| 다른 NU19xx | 0 |

주의: 이 PC 는 SDK 가 CI 와 다를 수 있고(⚠️ CI 가 실제로 쓴 SDK 판은 로그에 안 찍힘 — `dotnet --version` 단계 없음) 로컬 NuGet HTTP 캐시가 있다. `--force-evaluate` 로 재평가는 강제했으나 취약점 자료 캐시까지 비웠는지는 따로 재지 않았다. 지금(10/30) 자료에 권고가 들어 있으므로 3줄은 예상과 맞다.

## 5. 올리면 닿는 곳

**직접 참조 5곳(전부 2.5.0)**: `src/HitPan.API/HitPan.API.csproj:33` · `src/HitPan.Application/HitPan.Application.csproj:14` · `src/HitPan.Backoffice.API/HitPan.Backoffice.API.csproj:21` · `src/HitPan.Infrastructure/HitPan.Infrastructure.csproj:24` · `src/HitPan.IntegrationTests/HitPan.IntegrationTests.csproj:14`(솔루션 밖 — 그래도 함께 올려야 판이 안 갈림).

**간접 끌어옴**(`dotnet list src/HitPan.sln package --include-transitive`):
- `HitPan.Tests` → MySqlConnector 2.5.0 (간접)
- `Pomelo.EntityFrameworkCore.MySql` 은 범위 `[8.0.0,9.0.0)` 로 **8.0.0** 이 풀림(`Infrastructure.csproj:25`). 8.0.0 nuspec 의 의존 = `MySqlConnector >= 2.3.5`(하한만) ⇒ **2.6.x 로 올려도 NU1605(판 내림) 충돌 없음**.
- Dapper 2.1.72 는 MySqlConnector 를 안 끌어옴(공용 ADO 추상만).
- `MySqlConnector.DependencyInjection`·`AddMySqlDataSource` 사용 0건(2.6.0 에서 API 위치 이동 — 영향 없음).

**2.6.0 에 들어온 동작 변화 중 우리에게 닿을 수 있는 것**(릴리스 노트 원문 · 시험 대상):
| 변화 | 우리 쪽 노출 | 판정 |
|---|---|---|
| `BINARY` 열을 Guid 로 감지 안 함(#1625) | ERP 연결 문자열은 `GuidFormat=None`(`InfrastructureExtensions.cs:55` 등 · `ConnectionStringGuidGuardTests` 가 전수 강제) · 출하 DDL `installer/hitpan_db_clean.sql` 에 `binary(16)` 0 · `varbinary` 13 | ERP 낮음. **백오피스 `appsettings.json` 연결 문자열엔 `GuidFormat` 없음** → ⚠️ 백오피스 스키마 확인 필요 |
| 매개변수 해석·재작성 강화 — 따옴표 매개변수 종료 수정(#1627) · 모든 주석 시작 문자 처리(#1624) · 인자 검사 강화(#1632) | Dapper 원문 SQL 다수(`@` 매개변수 · `AllowUserVariables=true`) | **가장 큰 위험** — DB 게이트 잡 전량 재실행이 필요 |
| 음수 DATETIME ticks 거부(#1630) · XA/트랜잭션 정리(#1623·#729) | 날짜·트랜잭션 경로 | DB 게이트로 확인 |
| net8.0 대상 꾸러미 있음(2.6.2 nuspec: net6/8/9/10 · netstandard2.0/2.1) | — | 대상 프레임워크 문제 없음 |

**출하·게시 워크플로가 막히나**
| 워크플로 | 자리 | 경고 승격 | 지금 막히나 |
|---|---|---|---|
| `build-and-test.yml` | :69 restore · :73 build | `/p:TreatWarningsAsErrors=true /warnaserror` | 🔴 막힘(이미 관측) |
| `deploy-ncp.yml` (백오피스·랜딩) | :62 `dotnet build HitPan.Backoffice.API ... /p:TreatWarningsAsErrors=true`(restore 포함) | 있음 | 🔴 **막힐 것(예측)** · 마지막 런 40497035284 = 10/27(권고 전) · ⚠️ 실측 안 함 |
| `deploy-update.yml` (ERP 자동업데이트 zip) | :189 restore · :198 publish | 없음(:44 주석 「warnaserror 품질게이트가 없다」) | 🟢 안 막힘 — 다만 2.5.0 이 고객 PC 로 그대로 나감(§2 대로 실제 위험은 없음) |
| `build-installer.yml` (설치판) | :88 publish | 없음 | 🟢 안 막힘 |
| `watchdog-ci.yml` | Watchdog 만 · MySqlConnector 미참조 | csproj 자체 TWAE | 무관 |
| `codeql.yml` | :48 | `TreatWarningsAsErrors=false` | 무관 |

## 6. 선택지 (결정은 대표)

| | 내용 | 장점 | 단점 | 헌법 |
|---|---|---|---|---|
| **㉠ 고친 판으로 올림** (추천) | 5곳을 **같은 판**(2.6.1 최소 · 2.6.2 최신)으로 함께. 10/18 작8(NU1903) 과 같은 꼴 — 종전 줄은 주석으로 남김(#1) | 권고 해소 · CI·백오피스 배포 정상화 · 감사가 다시 깨끗해짐 | 2.6.0 의 SQL 해석 변화가 Dapper 원문 SQL 에 닿을 수 있음 → **DB 게이트 잡 전량 + 백오피스 스키마 게이트**가 초록이어야 함. ERP 판 올림 = 게시 1회 · 백오피스 = NCP 배포 1회 | #19 충족 · #25 「안전하게」 충족 · #1 정합 |
| ㉡ 이 권고만 억제 (`NuGetAuditSuppress Include="https://github.com/advisories/GHSA-473q-m89c-ghf8"`) | 판 안 바꿈 | 동작 변화 0 · 즉시 초록 · §2 근거(취약 조건 밖)가 있음 | `build-and-test.yml:44-46` 10/14 주석이 「권고를 가리는 것이지 고치는 게 아니다 — 전부 금지」로 명시(문언은 NoWarn 이지만 취지 같음) · 백오피스 운영 SSL 설정이 ⚠️미측정이라 「안 쓴다」를 100% 단정 못 함 | #19 문언은 충족 · #25 와 긴장 |
| ㉢ 그대로 두고 감수 | 아무것도 안 함 | 0 | 🔴 **백오피스 배포가 막힌다**(§5) · Build 잡이 계속 빨강 → 다른 진짜 빨강을 가림(04:34 T7 처럼) | #19 위반 상태 지속 — **불가** |

**덧붙여 결재 거리로 올릴 것(이번 차수 밖 · 별건)**
- ④ `HitPan.Application.csproj:47` 통째 NoWarn NU1902 — 사유(MailKit)가 사라졌다. ㉠ 을 택하면 같은 차수에서 거둘지(그러면 이 프로젝트 감사가 다시 살아남) 대표 결재.
- ⑤ 간접 high 권고 4종(§30초 요약 7) — 감사 모드가 직접 참조만이라 CI 에 안 보일 뿐 실재한다. 별 차수 [1-V] 대상.
- 10/30 런처럼 이미 빨강인 PR 이 쌓이는 중 — 10/14 주석(build-and-test.yml:38-42) 대로 「빨강 안고 머지」는 매번 대표 지시가 필요.

---

## 미측정 (⚠️)

1. 백오피스 **운영 서버**의 실제 `BackofficeDb` 연결 문자열(SSL 모드·DB 위치) — #39 로 이번엔 안 봄. ㉡ 을 고르려면 반드시 필요, ㉠ 이면 불필요.
2. `deploy-ncp.yml` 이 실제로 NU1902 에 막히는지 — 런을 돌리지 않았다(예측은 :62 의 TWAE + restore 포함 빌드에서 나옴).
3. 10/29 당시 nuget.org 취약점 색인 이력 · 지역별 응답 — 지금 되짚을 수 없다. H1·H2 는 가설로 남는다.
4. CI 가 실제로 쓴 SDK 판(8.0.427 인지 이미지 내장 더 높은 판인지) — 로그에 `dotnet --version` 이 없다. 결과(직접 참조 3곳만 경고)는 어느 쪽이든 같다.
5. 2.6.x 판 올림 뒤 Dapper SQL·백오피스 Guid 매핑 영향 — 판을 안 올렸으니 당연히 안 쟀다(㉠ 의 시험 대상).
6. 로컬 `--force-evaluate` 가 취약점 자료 HTTP 캐시까지 새로 받았는지.

## 다음 단계 제안

1. 대표 결재: ㉠/㉡/㉢ 중 택1 (+ ④ NoWarn 거둘지).
2. ㉠ 이면 [2] 설계팀장 작업지시서 — 5 csproj 같은 판 · 종전 줄 주석 보존(#1) · 게이트 = Build 잡 0/0 · **DB 게이트 잡 전량** · 백오피스 MySQL 8 스키마 게이트 · 백오피스 `GuidFormat` 확인 · 게시(ERP 1회 · NCP 1회)는 PM 직접 dispatch 규칙대로.
3. [3-V] 보안상무는 ⑤(간접 high 4종)를 별 차수 후보로 받아 둔다.
