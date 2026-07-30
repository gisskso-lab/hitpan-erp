# PM 작업 스케줄표 — 자동배포 영구CD · MetaPing · 잠복사고 (2026-07-23 밤)

> PM 브라운킴 / SOP [2] 스케줄표 / 오늘 밤 전수검사(전수조사4인·회의·검사2·검사3) 종합
> 기준: 의존관계 · 결재벽 · 워크플로우 영향 · 잠복위험 · 사장님 손 필요분
> **코드 0. 스케줄 정리만.** 실제 코드는 각 항목 SOP [3]~[5] 결재 후.

---

## 0. 스케줄 짜는 원칙 (사장님 헌법)

1. **원래 목표 = 백지 UPDATE 사상 첫 통과** — 모든 우선순위는 이 목표에 얼마나 직결되나로 정렬.
2. **잠복 사고가 목표를 막으면 최우선** — "봉합했는데 실측서 또 터짐"(두더지잡기) 차단.
3. **사장님 손 필요분(NCP sudo·GitHub·아키텍처 결정)은 병목** — 미리 좁혀 대기시간 최소화.
4. **MetaPing은 병행**(검사2 판정: 자동배포 실측 안 막음) — 목표 경로에서 분리.

---

## 1. 🔴🔴 발견된 치명 결함 (검사로 이미 확인됨 — 사장님 지시로 스케줄 편입)

> 사장님 지시(2026-07-23): "B처럼 치명적 결함이 발견됐다면 포함시켜서."
> 아래는 **P0-검사 대기가 아니라 이미 검사로 확인된 치명 결함**. 어제 봉합한 진범 #1(schema_migrations 시드/실제 불일치)과 **같은 계열**.

| # | 치명 결함 | 실측 근거 | 증상 | 어제 진범과 관계 |
|---|---|---|---|---|
| **CF-1** | **licenses·watchdog_pings·watchdog_emergencies 가 clean DDL 본문에 없음** (schema_migrations 시드행만) | 검사3: hitpan_db_clean.sql 에 CREATE TABLE 본문 0, `('DB-70','clean-ddl',1)` 시드만(line 3067) | **신규설치 고객 PC ERP 에 이 3개 테이블 안 생김.** 시드는 "적용됨"이라 마이그 러너도 재생성 안 함 | 헌법 #36 위반. 어제 진범 #1(시드 0행→오판)의 **역방향**(시드는 있는데 본문 없음) |
| **CF-2** | **licenses 테이블 INSERT 경로 0건** (비어있음) | 검사3: `INSERT INTO licenses` grep 0건(수동 INSERT 문서뿐) | 테이블 생겨도 행 없어 **워치독 Bearer 검증 전 고객 401/503** | MetaPing·워치독 인증 공통 선결 |
| **CF-3** | **admin 조회 API(watchdog summary·emergencies) 부재** | 검사3: AdminWatchdogMonitor.razor:139-141 호출하나 백오피스 API 컨트롤러 27개 중 없음 | **백오피스 워치독 모니터 화면 현재 404**(이미 깨짐) | 별건이나 CS 가시성 결함 |

### ✅ P0-검사 완료 (2026-07-23 밤) — 판정: **(c) 완전 무관, 트랙A 바로 진행 가능**
CF-1·CF-2 는 백지 UPDATE 실측을 **막지 않는다.** 자동업데이트 경로는 licenses/watchdog 3테이블과 물리 분리(HTTP 피드 + local_update_* + schema_migrations 만 사용, 이들은 clean DDL 에 다 있음).
- **Q1 자동업뎃 의존?** ❌ 0 — manifest HTTP GET·파일교체·local_update_*(DB-82·83·W4-6)·schema_migrations 만. licenses/watchdog SELECT 0(UpdateClient.cs:66-224, UpdateOrchestrator.cs:132-811).
- **Q2 워치독 필요?** ❌ MetaPing 은 back.hitpan.kr 로 감(로컬 아님). Bearer 즉석계산(MetaPingClient.cs:144-152), 로컬 licenses 안읽음.
- **Q3 기동/로그인/업무 막나?** ❌ 마이그 자동실행 0(Program.cs:82)·업무 컨트롤러 참조 0·CsAutoDispatch 기본 off. WatchdogBearerMiddleware 는 `/watchdog/*` 만 가로챔(WatchdogBearerMiddleware.cs:27).
- **Q4 종단?** ❌ 안 멈춤. 설치→기동→로그인→워치독→1.2.40 감지→교체 도달.
- **CF 실제 파급 = 로컬 ERP `/watchdog/*` 수신기능뿐인데, 워치독은 본사로 보내지 로컬로 안 보냄 = 아무도 호출않는 죽은 수신구.**

### 🔴 백지 실측 판독 오염 방지 (검사 경고 — 두더지잡기 재발차단)
1. "자동업뎃 됐는데 워치독 통신 안됨"을 실패로 **오판 금지**. 성패 판정 = `local_update_apply_status` 행 + `/health` 버전 + 교체된 HitPan.API.exe FileVersion.
2. MetaPing 400(별건)은 자동업뎃 성패 지표 **아님**. 본사 대시보드 버전 안떠도 "실패" 결론 금지.
3. 실측 중 로컬 `/watchdog/*` **호출 말 것**(호출하면 CF로 500/401=정상 증상).
4. 1.2.40 에 **새 마이그(DB-*.sql) 싣지 말 것**(실으면 교차검증 게이트가 정상 차단, 고리5 미구현. "됐는데 왜 안되나" 오판방지).

### CF 봉합은 별건 P0 (트랙B, 헌법 #36)
CF-1·CF-2 는 반드시 봉합하되 "본사가 워치독 핑 받는 미래기능"용이지 고객 자동업데이트와 경로 갈라진 별건. 트랙A 뒤/병행.

---

## 2. 트랙 A — 자동배포 활성화 (원래 목표 직결, 핵심)

> 검사2 로드맵 + 구멍3건 반영. **MetaPing 없이 백지 Sandbox 직접관측으로 UPDATE 첫통과 확인 가능**(검사2 판정).

| 순위 | 항목 | 담당/결재 | 의존 | 비고 |
|---|---|---|---|---|
| A-1 | **버전 1.2.39→1.2.40** (Directory.Build.props:33, 1줄) | 작지서→CTO→사장님 | 없음 | 같은버전 감지0이라 실측엔 새버전 필수 |
| A-2 | **활성화 작지서** (구멍①②③ 반영: G-C blocking승격·beta production-only잠금·품질게이트규율) + CODEOWNERS 신설 | 작지서→CTO→사장님 | 없음 | 코드작업. deploy-update.yml.disabled→활성 준비 |
| A-3 | **사장님 NCP sudo 1회 세션** (순서엄수: G-A 상주배치+G-C webroot정정 → sign-manifest 상주확인 → G-B sudoers 2인자) | 🔴 사장님 손(#29) | A-2 후 | 병목. G-C 반드시 함께(구멍①) |
| A-4 | **GitHub 설정** (G-E production reviewer=사장님 등록 / F-1=production-only 승인) | 🔴 사장님 클릭 | A-2 후 | NCP 아님. G-D는 이미 소멸 |
| A-5 | **.disabled 제거→활성화** (파일명 변경) | 작지서 | A-3·A-4 후 | |
| A-6 | **1.2.40 dispatch 실측** (build→reviewer승인→deploy→curl 1.2.40 확인) | 실측 | A-5 후 | |
| A-7 | **백지 Sandbox 종단** (1.2.39설치→1.2.40 자동감지·교체 = UPDATE 사상 첫통과) | 실측 | A-6 후 | **원래 목표 달성점** |

**트랙 A 병목 = A-3(NCP sudo)·A-4(GitHub).** 검사2로 이미 좁혀둠(G-D 소멸, 실제 sudo=G-A·B·C뿐).

---

## 3. 트랙 B — 치명 결함(CF) 봉합 (헌법 #36, P0-검사 결과에 따라 트랙A와 선후 변동)

| 순위 | 항목 | 담당/결재 | 대응 CF | P0-검사 연동 |
|---|---|---|---|---|
| B-1 | **licenses·watchdog_pings·watchdog_emergencies clean DDL 본문 편입** (#36) | 작지서→CTO→사장님 | CF-1 | **"실측 막음" 판정시 → 트랙A보다 선행** |
| B-2 | **licenses 발급경로 설계** (언제/누가 INSERT) | 작지서→CTO→사장님(결정) | CF-2 | 워치독인증·MetaPing 공통 선결 |
| B-3 | **ddl-smoke 게이트가 CF-1 을 왜 못잡았나** 검사 + 게이트 강화 | 검사→작지서 | CF-1 재발방지 | ddl-smoke 가 "시드는 있는데 본문 없음"을 못잡은 사각지대. 강화하면 CF-1류 재발 CI 차단 |
| B-4 | **admin 조회 API 신규** (백오피스 모니터 404 봉합) | 작지서(트랙C와 합칠수도) | CF-3 | 트랙C(MetaPing)와 겹침 |

**⚠️ 주의**: B-1 이 트랙A 백지실측을 막으면(P0-검사), **트랙A보다 먼저**. B-3 는 CF-1 재발방지라 어제 ddl-smoke 자기참조 게이트(진범#1 봉합)와 정합 — **본문/시드 양방향 검사로 강화**가 정공법.

---

## 4. 트랙 C — MetaPing 봉합 (병행, 자동배포 실측 안 막음)

> 검사3: "옮기기 아니라 새로짓기". 검사2: 자동배포와 독립 병행. **원래 목표 경로 밖.**

| 순위 | 항목 | 담당/결재 | 비고 |
|---|---|---|---|
| C-0 | **사장님 아키텍처 결정3** (수신주체=백오피스? / licenses INSERT 시점? / tenant_id_hash 역매핑 #22충돌?) | 🔴 사장님 결정 | 작지서 전 선결 |
| C-1 | 백오피스 수신컨트롤러(ping INSERT) 신규 | 작지서→CTO→사장님 | C-0 후 |
| C-2 | admin 조회API(summary·emergencies) 신규 (현재 백오피스 모니터화면 404) | 작지서 | C-0 후 |
| C-3 | licenses/watchdog 테이블 백오피스 DB 신설 + 인증경로([AllowAnonymous]+자체검증) | 작지서 | C-0·B-2 후 |
| C-4 | 자동배포 3필드(latest_version·update_channel·consent_message) DTO·테이블 반영 | 작지서 | C-1 후 |

**트랙 C는 "본사 버전 가시성·선제CS"용.** 자동배포 켜진 뒤 "게시 성공률 집계"에 필요. 급하지 않음(목표 안 막음).

---

## 5. 통합 우선순위 (P0-검사 완료로 확정)

```
✅ P0-검사 완료: CF-1·CF-2 는 백지실측 안 막음 (완전 무관)
   │
   ├─ 트랙A(자동배포 활성화) ← 최우선, 바로 진행. A-1~A-7 → 백지 UPDATE 첫통과
   │     병목 = A-3(NCP sudo)·A-4(GitHub) 사장님 손
   │
   ├─ 트랙B(CF봉합, 헌법#36 별건 P0) ← 트랙A 뒤/병행. B-1·B-2·B-3(ddl-smoke 양방향 강화)
   │     "본사 워치독핑 수신 미래기능"용, 고객 자동업뎃과 경로 갈라짐
   │
   └─ 트랙C(MetaPing) = 병행. C-0 사장님 결정3 대기. 목표 안막음.
```

**확정**: 원래 목표(백지 UPDATE 첫통과)로 가는 최단경로 = **트랙A 즉시 진행**. 트랙B·C는 목표 안 막는 별건. **길이 뚫렸다.**

---

## 6. 사장님 손 필요분 (병목 — 미리 정리)

| 항목 | 종류 | 트랙 |
|---|---|---|
| NCP sudo 1회 세션 (G-A·B·C) | #29 인프라 | A-3 |
| GitHub reviewer·F-1 결정 | 설정 클릭 | A-4 |
| 아키텍처 결정3 (MetaPing 수신주체·licenses·역매핑) | 결정 | C-0 |
| 각 작지서 CTO후 최종결재 | 결재 | 전 트랙 |

---

## 7. 다음 행동 (PM 권고)

1. **B 검사 재개** → P0-검사(잠복사고가 백지실측 막나) 확정. 이게 트랙B 순위를 가름.
2. 그 결과로 트랙A vs 트랙B 선후 확정.
3. 트랙A 활성화 작지서(A-2) 작성 → CTO → 사장님 → NCP세션(A-3) → 백지실측.
4. 트랙C(MetaPing)는 사장님 아키텍처 결정3(C-0) 받은 뒤 별도.
