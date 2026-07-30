# 베타 출시용 무료 인프라 옵션 비교 보고서
> 문서번호: INFRA-FREE-OPTIONS-20260427
> 작성일: 2026-04-27
> 작성자: Claude Code (어벤져스 리서치)
> 대상 결정자: 사장님 / PM(닥터스트레인지) / CTO Final Verifier

---

## ⚠️ 조사 한계 고지 (먼저 박음)

이 문서는 **2026-01 이전 학습 데이터 기반**으로 작성됐다. 본 세션에서 WebSearch/WebFetch 도구 권한이 거부되어 **실시간 2026-04 시점 약관 재확인 불가**. 무료 정책은 분기마다 자주 바뀐다(Railway 무료 폐지, PlanetScale 무료 폐지, Heroku 폐지 전례).

**계약 직전 반드시 재확인할 항목**은 각 표에 🔴 마크로 박았다. 사장님이 최종 결정 전, 마커스 리(인프라 매니저) 또는 어드민이 각 벤더 공식 페이지 확인 후 PM 승인 받고 진행할 것.

---

## 1. 요약 (1페이지) — 사장님 1분 결정용

### 결론 한 줄
**"트랙 A(로컬) = Cloudflare Tunnel + .workers.dev / 트랙 B(클라우드) = Oracle Cloud Always Free Seoul + Cloudflare DNS"** 가 비용 0원, 한국 리전, 데이터 측정 가능, 갈아타기 쉬움 4박자 다 맞는 안.

### 1분 요약 표

| 영역 | 1순위 추천 | 비용 | 핵심 이유 |
|---|---|---|---|
| 도메인 | Cloudflare `.workers.dev` + 추후 `.app` 구매(연 $14) | 0원 (베타) → 연 1.7만원 (정식) | wildcard 무료, DNS 자유, 갈아타기 쉬움 |
| 터널링 (트랙 A) | Cloudflare Tunnel (cloudflared) | 0원 무제한 | 명명 터널 무제한, 고정 도메인, 자동 재연결, 도메인 통합 |
| 클라우드 (트랙 B) | Oracle Cloud Always Free (서울 리전) | 0원 영구 | 4 OCPU ARM + 24GB RAM, .NET 풀 지원, 한국 리전, 데이터 측정용 충분 |
| 백업/Fallback | Tailscale (10곳 미만) + Fly.io (소규모) | 0원 | Oracle 장애 대비 |

### 사장님 헌법 부합 체크
- §18 (데이터 경계): ✅ 터널은 데이터 우회 통과만, Cloudflare는 페이로드 저장 안 함
- §19 (zero warnings): 인프라 영역이라 해당 없음
- 트랙 B 데이터 측정: ✅ Oracle VM에 직접 모니터링 도구(Prometheus/Grafana) 박을 수 있음

### 의사결정 5문답 → 본 문서 5장 참조

---

## 2. 영역 1: 무료 도메인 서비스

### 2.1 후보 비교표

| 후보 | 무료 한도 | wildcard 서브도메인 | DNS 컨트롤 | 한국 친화 | 벤더 락인 | 데이터 추출 | 부하 견딤 | §18 부합 |
|---|---|---|---|---|---|---|---|---|
| **`.workers.dev` (Cloudflare)** | 영구 무료, 계정당 1개 루트 (예: `hitpan.workers.dev`) — 그 아래 워커별 서브 무한 | ⭕ 워커 라우팅으로 사실상 wildcard | ❌ DNS 직접 제어 X (워커 라우팅만) | 🟡 한국 PoP O, 한국 도메인 X | ⭐⭐ (워커 라우팅 의존, 갈아타기 쉬움) | ⭕ 코드만 옮기면 됨 | ⭕⭐⭐⭐⭐⭐ Cloudflare 글로벌 엣지 | ⭕ 메타데이터만 |
| **`.app` (Google 운영, 유료)** | 연 $14 정도 (비교 기준) | ⭕ 무제한 (DNS만 잡으면) | ⭕ 완전 자유 | 🟡 글로벌 | ⭐ (옮기기 쉬움, DNS 표준) | ⭕ | 레지스트라 의존 | ⭕ |
| **`.tk/.ml/.ga/.cf/.gq` (Freenom)** | 연 무료 (1년 갱신) 🔴 **2024년 신규 등록 전면 중단·서비스 사실상 종료** | ⭕ | ⭕ | ❌ | ⭐⭐⭐⭐⭐ (회수 위험 극심) | ❌ 강제 회수 사례 | ❌ 신뢰 0 | ❌ 평판 위험 |
| **`is-a.dev` / `js.org`** | 무료, 개인 오픈소스 프로젝트만 (PR로 신청, 운영자 승인) | ❌ wildcard 거의 불가 | 🟡 제한적 (CNAME 위주) | ❌ | ⭐⭐⭐ (운영자 의존) | ⭕ | 🟡 작은 커뮤니티 | 🟡 상업용 부적합 (TOS 위반 가능) |
| **`.dev` `.io` (유료 비교용)** | `.dev`= 연 $12, `.io`= 연 $30~50 | ⭕ 무제한 | ⭕ | 🟡 | ⭐ | ⭕ | 레지스트라 의존 | ⭕ |
| **가비아 한국 도메인 `.kr` `.co.kr`** | 연 1.1만~2.2만원, 무료 X | ⭕ 무제한 | ⭕ | ⭕⭐⭐⭐⭐⭐ | ⭐ | ⭕ | 가비아 안정 | ⭕ |
| **DuckDNS / No-IP** | 무료 동적 DNS, 5개 호스트 | ❌ wildcard X | 🟡 A 레코드만 | 🟡 | ⭐⭐ | ⭕ | 🟡 가정용 수준 | ⭕ |
| **Cloudflare 자체 도메인 등록** | 도매가 판매 (.com $10/년 등 마진 0) | ⭕ | ⭕⭐⭐⭐⭐⭐ | 🟡 | ⭐ | ⭕ | ⭐⭐⭐⭐⭐ | ⭕ |

### 2.2 실용 분석

**베타 단계 (0원 안)**:
- `hitpan.workers.dev` 하나 잡고, 워커 라우팅으로 `app.hitpan.workers.dev`, `tenant1.hitpan.workers.dev` ... 박을 수 있음. 단 이건 Workers 위에 얹히는 것. .NET Core 백엔드 호스팅은 Workers에서 안 됨 → **터널 진입점 도메인** 또는 **정적 자산** 용도로만 적합.
- 트랙 A(로컬+터널)는 Cloudflare Tunnel이 자체적으로 `*.cfargotunnel.com` 같은 임시 호스트를 발급. Zero Trust 무료에서 named tunnel + 자체 도메인 하나(`.workers.dev` 또는 사 도메인) 연결 시 `tenant1.hitpan.app` 형태 가능. **`.workers.dev`는 Tunnel용 hostname으로 직접 못 씀** 🔴 — 반드시 등록 도메인(=`.app` 또는 `.kr`) 필요.

**핵심 발견**:
> Cloudflare Tunnel을 wildcard 서브도메인으로 운영하려면 **본인 소유 도메인 1개는 사야 함**. `.workers.dev`는 Workers 전용이라 Tunnel hostname으로 할당 불가. → **`hitpan.app` 연 $14 또는 `.kr` 연 1.1만원은 사실상 필수.** 무료 도메인 안 성립.

**정식 전환 시**:
- `.app` (Google 운영, HSTS preload 강제 = 항상 HTTPS, 보안 ⭐⭐⭐⭐⭐) → SaaS 추천
- `.co.kr` 또는 `.kr` → 한국 신뢰 ⭐⭐⭐⭐⭐, 검색 SEO 한국 우대
- **이중 보유 권장**: `hitpan.app` (글로벌·SaaS) + `hitpan.co.kr` (한국 마케팅용 리다이렉트)

### 2.3 권고
**도메인은 무료에 집착하지 말고 연 $14 (~1.7만원)는 박자.** 베타 20곳 모집·기술영업팀장 시연 시 `tenant1.workers.dev` 보다는 `tenant1.hitpan.app`이 신뢰도 ⭐⭐⭐⭐⭐ 차이.

---

## 3. 영역 2: 무료 터널링 서비스

### 3.1 후보 비교표

| 후보 | 무료 한도 | 동시 10곳 가능 | 고정 도메인 | 자동 재연결 | 인증·보안 | EXE 통합 단순도 | 벤더 락인 | §18 부합 |
|---|---|---|---|---|---|---|---|---|
| **Cloudflare Tunnel (cloudflared)** 🥇 | 무제한 무료 (Zero Trust Free, 50 사용자까지) | ⭕ (10곳 무난) | ⭕ named tunnel + DNS | ⭕ 데몬 자동 재연결 | ⭕⭐⭐⭐⭐⭐ Access policy + WAF | ⭕ Windows 서비스 등록 가능, MSI 제공 | ⭐⭐ (터널 구성만 옮기면 됨) | ⭕ 페이로드 저장 X, 메타데이터만 |
| **ngrok 무료** | 동시 1 터널 / 정적 도메인 1개 / 월 1GB / 분당 40 connections | ❌ 동시 1개 = 10곳 불가 | 🟡 Free에서 정적 도메인 1개 추가됨(2024) | ⭕ | 🟡 basic auth | ⭕ 매우 쉬움 | ⭐⭐⭐ | ⭕ |
| **Tailscale Funnel** | Personal Free 100 디바이스, Funnel 도메인 1개 (`.ts.net`) | 🟡 디바이스 수는 OK, Funnel 동시 노출은 노드별 | 🟡 `.ts.net` 고정, 사 도메인 X | ⭕ | ⭕⭐⭐⭐⭐⭐ WireGuard | 🟡 Tailscale 클라 추가 설치 | ⭐⭐⭐ | ⭕ |
| **frp (자체 호스팅)** | 무제한, 본사 서버에서 frps 직접 운영 | ⭕ (서버 사양 따라) | ⭕ 본사 도메인 사용 | 🟡 frpc 설정 필요 | 🟡 token 기반 (HTTPS는 본사가 제공) | 🟡 frpc 바이너리 + 설정 파일 | ⭐ | 🟡 본사 서버 통과 = §18 위반 위험 ❗ |
| **localtunnel** | 무료, `loca.lt` 서브도메인 | 🟡 안정성 떨어짐 (오픈소스 무료 인스턴스) | ❌ 매번 랜덤 | ❌ | ❌ 인증 X | ⭕ npm i 한 줄 | ⭐ | ⭕ |
| **Pinggy** | 60분 세션, 무료 임시 URL | ❌ 60분 끊김 | ❌ 랜덤 | 🟡 | 🟡 | ⭕ | ⭐⭐ | ⭕ |
| **bore (러스트)** | 자체 호스팅 무료 | ⭕ | ⭕ 사 도메인 | 🟡 | 🟡 | 🟡 바이너리 배포 | ⭐ | 🟡 본사 서버 경유 |
| **Pagekite** | 31일 무료 평가 후 유료 | ❌ 평가만 | ⭕ | ⭕ | ⭕ | ⭕ | ⭐⭐⭐ | ⭕ |
| **Serveo** | 무료 SSH 터널 (불안정, 자주 다운) | ❌ | 🟡 | ❌ | ❌ | 🟡 SSH 필요 | ⭐ | 🟡 |

### 3.2 실용 분석

**Cloudflare Tunnel 압도적 1위**:
- 무료, 무제한 named tunnel
- 고객 PC에서 `cloudflared service install <token>` 한 줄이면 Windows 서비스로 등록 → PC 부팅 시 자동 시작
- 본사가 발급한 토큰을 EXE 인스톨러에 박아두면 v1.0.7에서 원클릭 가능
- `tenant1.hitpan.app` → 고객 PC localhost:5257 매핑이 본사 콘솔에서 일괄 관리됨
- **§18 부합 핵심**: Cloudflare는 트래픽을 패스스루만 함. 본사 DB로 가는 게 아님.
- 🔴 **확인 필요**: Zero Trust Free의 사용자 수 한도(과거 50명 → 현재? 베타 20곳 × 평균 3명 = 60명일 때 유료 전환 위험). 인증을 Cloudflare Access 안 쓰고 ERP 자체 JWT만 쓰면 사용자 한도와 무관.

**ngrok 탈락**: 동시 1터널 = 10곳 불가. 유료(월 $10) 가도 1터널은 동일 → 트랙 A에 부적합.

**frp 자체 호스팅 위험**: 본사 서버 경유 = §18 데이터 경계 위반 위험. 페이로드가 본사 메모리를 통과하는 순간 "수신"으로 간주될 수 있음. 보안 매니저 사전 자문 필수.

**Tailscale Funnel 세컨더리**: Cloudflare 장애 시 fallback. WireGuard 기반 = 보안 ⭐⭐⭐⭐⭐. 단 `.ts.net` 도메인 강제 = 고객 시연 시 신뢰도 떨어짐.

### 3.3 권고
**Primary = Cloudflare Tunnel / Secondary = Tailscale Funnel(고급 보안 요구 고객용)**.
v1.0.7 EXE에 cloudflared 자동 설치 + 본사 토큰 발급 API 박는 게 트랙 A의 본 줄기.

---

## 4. 영역 3: 무료 클라우드 서비스

### 4.1 후보 비교표

| 후보 | 무료 한도 | .NET 8 호스팅 | DB 무료 (MySQL/MariaDB) | 베타 10곳 가능 | 콜드스타트·슬립 | 한국 리전 | 측정 도구 | 갈아타기 |
|---|---|---|---|---|---|---|---|---|
| **Oracle Cloud Always Free** 🥇 | ARM Ampere A1: 4 OCPU + 24GB RAM (1~4 VM 분할) / AMD: 2 VM × 1/8 OCPU + 1GB / 200GB 블록 / 10TB 송신/월 / Autonomous DB 2개 × 20GB / **영구 무료** | ⭕⭐⭐⭐⭐⭐ Ubuntu/Oracle Linux + Docker → .NET 풀 지원 | ⭕ MySQL HeatWave Free 50GB / Autonomous DB | ⭕ 24GB RAM이면 충분 | 없음 (상시 켬) | ⭕ 서울(춘천) Chuncheon 리전 ✅ | 🟡 OCI Monitoring 무료 | ⭕ Docker → 어디든 |
| **Cloudflare Workers + D1** | 100k req/day, CPU 10ms/req(무료), D1 5GB | ❌ .NET 호스팅 불가 (V8 isolate) | 🟡 D1 = SQLite 호환만 | 🟡 ERP 백엔드는 못 올림, 정적·BFF만 | 없음 | ⭕ 한국 PoP | ⭕ Analytics 자동 | ❌ Workers 고유 API 박히면 락인 |
| **AWS Free Tier** | t2.micro 750h/월 12개월만, RDS db.t2.micro 750h/월 12개월만 | ⭕ 가능 | ⭕ MySQL | 🟡 1 VM은 빠듯 | 없음 | ⭕ 서울 (ap-northeast-2) | ⭕ CloudWatch 일부 무료 | ⭕ |
| **GCP Free Tier** | e2-micro 1대 영구 무료 (us-central1만 무료, 서울은 유료) | ⭕ 가능 | ❌ Cloud SQL 무료 X | ❌ e2-micro 1대 = 0.25 vCPU + 1GB → 베타 10곳 불가 | 없음 | ❌ 무료는 미국만 | ⭕ | ⭕ |
| **Fly.io** | 🔴 2024.10 Hobby 무료 → "최대 $5 사용권" 으로 전환됨. 사실상 소규모만 무료 | ⭕ Docker | 🟡 LiteFS / Postgres 무료 일부 | 🟡 베타 10곳은 한도 초과 가능 | 없음 (machines auto-stop 옵션) | ⭕ NRT(도쿄) 가능, 서울 X | ⭕ Grafana | ⭕ Docker |
| **Render Free** | Web Service 무료 (15분 비활성 시 슬립 🔴) | ⭕ Docker | ❌ Postgres 무료 90일만 | ❌ 슬립 = ERP 치명적 | ❌ 15분 슬립 | ❌ 글로벌만 | 🟡 | ⭕ |
| **Railway** | 🔴 2023.08 무료 종료, 현재 $5 trial credit만 | ⭕ | ⭕ | ❌ | - | - | - | - |
| **Supabase** | DB 500MB Postgres / 2 프로젝트 / 7일 비활성 시 일시정지 | ❌ DB만 | ⭕ Postgres 500MB/proj | 🟡 ERP는 MariaDB라 마이그 필요 | 7일 슬립 | ⭕ 도쿄 (아시아) | ⭕ | 🟡 Postgres 종속 |
| **PlanetScale** | 🔴 2024.04 Hobby Free 종료. 현재 최저 $39/월 | - | - | - | - | - | - | - |
| **Aiven Free** | 🔴 2024.03 Free 종료. $300 trial credit만 | - | - | - | - | - | - | - |
| **NCP (네이버 클라우드)** | Free Trial 3개월 + 10만원 크레딧 | ⭕ | ⭕ MySQL | 🟡 3개월만 | 없음 | ⭕⭐⭐⭐⭐⭐ 한국 | ⭕ | ⭕ |
| **카카오 i 클라우드** | Free Trial 30만원 크레딧 (3개월) | ⭕ | ⭕ | 🟡 3개월만 | 없음 | ⭕⭐⭐⭐⭐⭐ 한국 | ⭕ | ⭕ |
| **NHN Cloud (TOAST)** | Free Trial 10만원 크레딧 | ⭕ | ⭕ | 🟡 단기만 | 없음 | ⭕ 한국 | ⭕ | ⭕ |

🔴 = 2024~2025년 정책 변경 확인된 항목. Oracle / NCP / 카카오 / NHN 약관은 2026-04 시점 재확인 필수.

### 4.2 실용 분석

**Oracle Cloud Always Free 압도적 1위**:
- 4 OCPU ARM + 24GB RAM = **소형 VPS 4대 분량 영구 무료**. 베타 10곳 멀티테넌트 1개 인스턴스로 충분.
- 서울(춘천) 리전 = 한국 데이터 보관, 개인정보보호법 친화 ⭐⭐⭐⭐⭐
- Docker + Ubuntu = .NET Core 8 + MariaDB 11 풀 지원
- 10TB 월 송신 = ERP 트래픽으로는 사실상 무제한
- 단점: ARM 아키텍처(.NET 8은 ARM64 지원 OK), Oracle 콘솔 학습곡선
- 🔴 **위험**: Always Free 인스턴스 회수 정책 — 비활성 시 자동 회수된 사례 있음 (2022~2023). 정기 헬스체크 + cron 트래픽 주입으로 활성 유지 필요.

**Cloudflare Workers는 트랙 B 메인 불가**:
- .NET 8 런타임 못 박음. V8 isolate 환경. 코드 전부 JS/TS/Rust로 재작성해야 함 = 갈아타기 비용 ⭐⭐⭐⭐⭐ → 락인 극심
- 단, **정적 자산 + BFF 라우팅 + DDoS 방어** 용도로 Oracle 앞에 박으면 시너지

**한국 클라우드(NCP/카카오/NHN)** 는 베타 단계엔 단기 크레딧만 → 베타 3개월 후 종료 시 Oracle로 이전 비용 발생. **정식 출시 시 전환 후보**로 키핑.

### 4.3 데이터 측정 지표 6개 매핑

| 지표 | Oracle VM에서 측정 방법 | 비용 |
|---|---|---|
| 거래처당 월 거래건수 | DB 쿼리: `SELECT tenant_id, COUNT(*) FROM journal_lines GROUP BY tenant_id, MONTH(created_at)` | 0원 |
| 거래처당 누적 DB 용량 | `SELECT table_schema, SUM(data_length+index_length) FROM information_schema.tables` | 0원 |
| 동시 사용자 수 | API 미들웨어에서 활성 세션 카운트 → Prometheus | 0원 |
| API 호출 빈도 | Nginx access log + GoAccess | 0원 |
| 스토리지 증가율 | cron 일별 DB 사이즈 스냅샷 | 0원 |
| 백업 크기 | mysqldump | 0원 |

→ **모두 Oracle VM 안에서 0원으로 측정 가능**. 사장님 데이터 측정 목적 충족.

### 4.4 권고
**Primary = Oracle Cloud Always Free (서울)**.
**Backup = Fly.io 도쿄(소규모 Fallback) 또는 NCP Free Trial 1세트**(Oracle 회수 시 즉시 이전).

---

## 5. 시너지 분석 (영역 통합)

### 5.1 조합별 평가

| 조합 | 비용 | 안정성 | 락인 | §18 | 평가 |
|---|---|---|---|---|---|
| **Cloudflare 단독** (도메인+터널+Workers) | 0원 | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ Workers 락인 | ✅ | ❌ .NET 호스팅 불가 = 발목 |
| **Oracle + Cloudflare Tunnel + `.workers.dev`** | 0원 | ⭐⭐⭐⭐⭐ | ⭐⭐ (둘 다 Docker/표준) | ✅ | ⭐⭐⭐⭐⭐ 본 안 |
| **Oracle + Cloudflare Tunnel + `.app` 구매** | 연 1.7만원 | ⭐⭐⭐⭐⭐ | ⭐⭐ | ✅ | ⭐⭐⭐⭐⭐ 신뢰도 + 본 안 |
| **Tailscale + 자체도메인 + Oracle** | 연 1.7만원 | ⭐⭐⭐⭐⭐ 보안 최강 | ⭐⭐⭐ | ✅ | 🟡 베타에 과함 — 정식 엔터프라이즈 옵션으로 키핑 |
| **NCP 단독 한국 안** | 3개월 무료 → 월 5~10만원 | ⭐⭐⭐⭐⭐ | ⭐⭐ | ✅ 국내법 최강 | 🟡 베타 후 비용 폭발 |
| **AWS Free Tier + Route53** | 12개월 무료 → 이후 월 $20+ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ AWS 락인 | ✅ | 🟡 12개월 후 폭탄 |

### 5.2 핵심 통찰
**Oracle Always Free + Cloudflare 조합은 두 회사가 서로 경쟁사라 락인 X**. Oracle VM은 Docker 표준 → 어디든 옮김. Cloudflare Tunnel 구성은 텍스트 파일 1개 → 다른 터널로 이전 가능. 이게 사장님 "잘 되는 거 안 건드림 + 갈아타기 부드럽게" 헌법에 가장 부합.

---

## 6. 3가지 시나리오 권고안

### 시나리오 A: 최소 비용 안전 안 (사장님 베타 정신과 가장 일치)
```
도메인:  hitpan.workers.dev (무료) + Cloudflare Tunnel용 .app 1개 구매(연 $14)
터널:   Cloudflare Tunnel (cloudflared) — 무료, 무제한
클라우드: Oracle Cloud Always Free (서울 리전)
         - VM: ARM Ampere 4 OCPU + 24GB RAM 1대
         - DB: 같은 VM 내 MariaDB 11.4 (또는 MySQL HeatWave Free 50GB)
백업:   주 1회 mysqldump → Cloudflare R2 (월 10GB 무료)
```
- **연 비용**: 약 1.7만원 (도메인만)
- **베타 20곳 운영 가능**: ✅
- **데이터 측정**: ✅ Oracle VM에서 직접
- **유료 전환 곡선**: Oracle 유료 ($25~/월부터) 또는 NCP/AWS 이전. **코드 변경 0줄** (Docker라서)

### 시나리오 B: 갈아타기 안 안 (정식 전환 부드러움 최우선)
```
도메인:  hitpan.app + hitpan.co.kr (이중 보유) — 연 약 3만원
터널:   Cloudflare Tunnel (트랙 A) + 직접 HTTPS(트랙 B는 터널 안 씀)
클라우드: Oracle Cloud Always Free (베타) → 정식 시 NCP 이전
DB:     MariaDB Docker (어디든 동일하게 떠짐)
백업:   Restic + Cloudflare R2 / NCP Object Storage (이중)
```
- **연 비용**: 약 3만원 (도메인 2개)
- **베타→정식 코드 변경**: 0줄 (Docker compose 그대로 NCP에 던지면 됨)
- **갈아타기 비용**: ⭐ (도메인 NS 변경만)

### 시나리오 C: 한국 우선 안 (법적 안전 최우선)
```
도메인:  hitpan.co.kr (가비아) + hitpan.kr (보조) — 연 약 4만원
터널:   Cloudflare Tunnel (글로벌 안정) — 한국 PoP 사용
클라우드: NCP Free Trial 3개월 → 정식 NCP / 카카오 i 클라우드 이전
DB:     NCP Cloud DB for MariaDB (관리형)
백업:   NCP Object Storage
```
- **연 비용**: 베타 약 4만원 + 3개월 후 월 5~10만원
- **법적 안전**: ⭐⭐⭐⭐⭐ (개인정보 국내 보관, 클라우드 보안인증 CSAP 가능)
- **리스크**: 베타 3개월 안에 매출 안 나면 비용 부담

### 추천 결정
**시나리오 A로 베타 → 시나리오 B 또는 C로 정식 전환**.
사장님 "베타 무료 → 측정 → 가격 책정" 전략과 정확히 일치.

---

## 7. 사장님이 답해야 할 5개 질문 (조사로 안 풀림)

1. **베타 도메인 신뢰도 우선순위**: `tenant1.hitpan.workers.dev` (무료, 신뢰도 ⭐⭐⭐) vs `tenant1.hitpan.app` (연 1.7만원, ⭐⭐⭐⭐⭐) — 1.7만원 박을지?

2. **데이터 주권 강도**: 베타 20곳 중 공기업·금융 관련 고객이 있는가? 있으면 **Oracle 서울 리전도 외산이라 거부될 수 있음** → 시나리오 C(NCP) 강제. 없으면 시나리오 A 가능.

3. **Cloudflare 락인 허용 범위**: 베타에서 Cloudflare Access(인증), Workers(BFF), R2(스토리지)까지 박을지, 아니면 Tunnel만 쓸지? 깊이 박으면 편하지만 갈아타기 비용 ⭐⭐⭐.

4. **Oracle Always Free 회수 위험 감수**: 비활성 시 회수 사례 있음. **고객사 데이터 들어간 VM이 회수되면 큰일** → 일별 백업 + 모니터링 필수. 이 운영 부담을 마커스 리(인프라 매니저)가 감당할 수 있는가?

5. **베타 종료 후 데이터 이관 시점**: 트랙 B 클라우드 베타 10곳이 정식 전환 시 → ① 동일 Oracle VM에 그대로 / ② NCP 이전 / ③ 고객 PC 로컬(트랙 A로 강제 전환). 베타 계약서에 이전 정책 명시해야 하는데, 어느 안인가?

---

## 부록 A: 재확인 체크리스트 (계약 직전, 마커스 리 담당)

- [ ] Cloudflare Zero Trust Free 사용자 한도 (50명 → 변경 여부)
- [ ] Cloudflare Tunnel 무료 무제한 named tunnel 정책 유지 여부
- [ ] Oracle Cloud Always Free ARM Ampere 24GB 정책 유지 여부 (2026-04 시점)
- [ ] Oracle 서울(춘천) 리전 Always Free 가능 여부 (일부 리전 제한 있음)
- [ ] Oracle Always Free 비활성 회수 정책 최신 (얼마간 idle이면 회수?)
- [ ] ngrok Free 정적 도메인 1개 정책 유지 여부
- [ ] Tailscale Personal Free 100 디바이스 정책 유지 여부
- [ ] Cloudflare R2 월 10GB 송신 무료 한도
- [ ] NCP / 카카오 i 클라우드 / NHN Cloud 무료 크레딧 최신 정책
- [ ] `.app` 도메인 가격 (Squarespace Domains, Cloudflare Registrar 비교)

## 부록 B: 출처 (학습 데이터 기반, 2026-01 이전)

- Cloudflare Workers Limits: developers.cloudflare.com/workers/platform/limits
- Cloudflare Zero Trust Free Plan: cloudflare.com/plans/zero-trust-services
- Oracle Cloud Always Free: oracle.com/cloud/free
- ngrok Pricing: ngrok.com/pricing
- Tailscale Pricing: tailscale.com/pricing
- Fly.io Pricing 변경: fly.io/blog (2024-10 무료 전환 공지)
- Railway 무료 종료: railway.app/changelog (2023-08)
- PlanetScale Hobby 종료: planetscale.com/blog (2024-03)
- Freenom 신규 등록 중단: registry.freenom.com 공지 (2023~2024)
- 가비아 도메인 가격: gabia.com
- NCP / 카카오 i / NHN 무료 크레딧: 각 사 공식 페이지

🔴 **2026-04 시점 변경 가능성 있음 — 부록 A 체크리스트 완료 후 의사결정**.

---

## 변경 이력

| 일자 | 작성자 | 내용 |
|---|---|---|
| 2026-04-27 | Claude Code | 초안 작성 (작업지시 기반) |
