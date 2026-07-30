# 3시스템 4중 분리 — 사장님 모두결재 박제 (2026-06-01 야간)

> 사장님 격언: **"히트판 웹은 로컬, 백오피스는 클라우드"**
> 결재: 사장님 모두결재 (2026-06-01 야간 호통 박제)
> 모 문서: `docs/설계/랜딩/EIGHT_PROPOSITIONS_BACKOFFICE_LANDING.md`

---

## 0. 사장님 핵심 박제 (4명제)

| # | 명제 |
|---|---|
| 1 | 히트판 ERP 웹 = **고객 PC 로컬** + cloudflared 터널 |
| 2 | 백오피스 = **본사 클라우드** (1대 서버 통합) |
| 3 | ERP 도메인 (정식) = `https://{고객사계정}.hitpan.kr/` (**가비아 서브도메인**) |
| 4 | ERP 도메인 (현재 베타) = `demo.hitpan.kr` (1곳 데모) |

---

## 1. 4중 분리 구현

| 분리 | 본사 (클라우드) | 고객 PC (로컬) |
|---|---|---|
| **DB** | `hitpan_backoffice` (시리얼·결제 메타만) | `hitpan_erp` (업무 데이터) |
| **코드 (Web)** | HitPan.Backoffice.Web + HitPan.Landing.Web | HitPan.Web (현재 ERP) |
| **코드 (API)** | HitPan.Backoffice.API + HitPan.Landing.API | HitPan.API (현재 ERP API) |
| **도메인** | hitpan.kr / backoffice.hitpan.kr | {고객사}.hitpan.kr (가비아 서브) |
| **네트워크** | Cloudflare WAF + 외부 노출 | cloudflared 터널 + 본사망 분리 |
| **인증** | Anonymous / Platform 권한 | Tenant 권한 |
| **권한** | 본사 운영팀·대리점 | 고객사 마스터·자식계정 |

---

## 2. 최종 도메인 매트릭스

### 정식 출시
- **랜딩페이지** (본사 클라우드): `hitpan.kr`
- **백오피스** (본사 클라우드): `backoffice.hitpan.kr`
- **본사 SaaS API** (본사 클라우드, 랜딩+백오피스 공용): `api.hitpan.kr`
- **히트판 ERP 웹** (고객 PC 로컬): `https://{고객사계정}.hitpan.kr/`
  - 가비아 서브도메인 자동 생성
  - cloudflared 터널로 고객 PC `localhost:5234` 매핑
- **ERP API** (고객 PC 로컬): `localhost:5257` 또는 터널

### 현재 베타
- **랜딩페이지**: `landing-demo.hitpan.kr` (또는 `hitpan.kr` 베타 노출)
- **백오피스**: `backoffice-demo.hitpan.kr`
- **본사 SaaS API**: `api-demo.hitpan.kr`
- **히트판 ERP 데모**: `demo.hitpan.kr` (1곳, 본사 데모 서버, 영업 시연용)
- **고객사 ERP**: 정식 흐름과 동일 (가비아 서브도메인)

---

## 3. 가입·발급·활성화 통합 흐름 (사장님 정정 반영)

### 3.1 랜딩 = Zero-DB 패스스루
랜딩 자체 DB·세션·스토리지 0. 입력 폼 → 본사 클라우드 API 패스스루만.

### 3.2 본사 백오피스 = 평문 사업자 정보 영구 미보유
사등 OCR 추출 정보 = 메모리 임시 보관(수 분~24h) → 고객 PC 전송 후 즉시 폐기. DB 박제 0.

### 3.3 사업자등록증 OCR 추출 정보 → 고객 PC ERP 계정관리 자동 등록 (사장님 정정 핵심)

```
[1. 랜딩 신규 가입]
   - 이메일·휴대폰·약관 + 사업자등록증 업로드 + 결제 토큰
   - 랜딩 = Stateless, sessionStorage 0
       ↓
[2. 본사 클라우드 API 메모리 처리]
   - 메모리 OCR (PaddleOCR/Tesseract 온프레미스)
   - 추출 정보: 사업자번호·상호·대표자·주소·업종·개업일
   - 국세청 진위확인 API (1회)
       ├─ False: 거부 + 메모리 폐기 + 환불
       └─ True: 다음 단계
       ↓
[3. 본사 메모리 임시 보관 — 평문 OCR 정보]
   - Redis 또는 In-Memory Cache (TTL 24h)
   - 시리얼 발급 + 고객 활성화 시점까지만 보관
   - **백오피스 DB에는 절대 저장 0** (8명제 #3 정합)
       ↓
[4. 본사 어드민 4-eyes 결재 + 시리얼·임시비번 발급]
   - HP-YYMM-XXXXXXXX-CRC
   - Argon2id 해시 → 백오피스 DB 박제
   - 평문 시리얼·임시비번 메모리 1초 → 즉시 폐기
       ↓
[5. 가비아 API — 서브도메인 생성]
   - {고객사계정}.hitpan.kr 신규 생성
   - 사장님 결재 + API 키 HSM 보관
       ↓
[6. Cloudflare cloudflared 터널 발급]
   - 신규 터널 ID + 자격증명 생성
   - 서브도메인 → 터널 ID 매핑
       ↓
[7. 이메일·SMS 2채널 전송]
   - 이메일: 시리얼 + 활성화 링크 + EXE 다운로드 안내
   - SMS: 임시 비번 + 만료 안내
       ↓
[8. 고객 PC EXE 설치 + 첫 활성화]
   - 시리얼 + 임시비번 입력
   - cloudflared 자격증명 DPAPI 봉인
   - 강제 비번 변경
       ↓
[9. 🔥 사업자등록증 OCR 정보 자동 전송 (사장님 정정 핵심)]
   - 본사 클라우드 API → 고객 PC ERP 1회 안전 전송
   - 채널: TLS 1.3 + cloudflared 터널 + E2E 암호화 (시리얼 기반 키)
   - 전송 정보: 사업자번호·상호·대표자·주소·업종·개업일
   - 고객 PC 로컬 hitpan_erp DB에 박제 (DPAPI 봉인)
   - 위치: ERP 계정관리 화면에 자동 등록 (고객 입력 0)
   - **전송 후 본사 메모리 즉시 폐기 (0초)**
       ↓
[10. ERP 정상 동작]
   - https://{고객사}.hitpan.kr → cloudflared 터널 → 고객 PC localhost:5234
   - 사업자 정보 = 계정관리에 이미 자동 등록됨 (고객 재입력 0)
   - 본사가 고객사 ERP 데이터 직접 접근 0 (헌법 #18·#22)
```

### 3.4 보안 강도

| 시점 | 본사 보유 평문 |
|---|---|
| 사등 업로드 직후 | 메모리 OCR 결과 (디스크 0) |
| 시리얼 발급 후 | 메모리 (TTL 24h, DB 0) |
| 고객 활성화 후 | **즉시 폐기 (0)** |
| 고객 활성화 안 함 (24h 초과) | **자동 폐기 + 재발급 요청 필요** |

본사 백오피스 DB = 평문 사업자번호·상호·대표자·주소 **영구 미보유** (8명제 #3 완성).

---

## 4. 단계적 분리 로드맵

### Phase 1: 논리적 분리 (현재 진행 중)
- HitPan.Web 1개 프로젝트 안에서 Pages 폴더 정리
- `Pages/Landing/` (현재 완료)
- `Pages/Platform/` (현재 완료)
- `Pages/Admin/` (백오피스 로그인만)
- 도메인 = `demo.hitpan.kr` 1개 (베타)

### Phase 2: 코드 물리 분리 (다음 단계, 사장님 결재 후)
- `HitPan.Landing.Web` 신규 csproj — Pages/Landing/ 이동
- `HitPan.Backoffice.Web` 신규 csproj — Pages/Platform/ + Pages/Admin/ 이동
- `HitPan.Web` 유지 — ERP만 (고객 PC 배포)
- 공통 의존성: HitPan.Domain · HitPan.Contracts · HitPan.Infrastructure 그대로

### Phase 3: API 물리 분리
- `HitPan.Backoffice.API` 신규 csproj — Admin/Sync/Refund/Reseller 컨트롤러 이동
- `HitPan.Landing.API` 신규 csproj — Landing/Auth 컨트롤러 이동
- `HitPan.API` 유지 — ERP만 (고객 PC 배포)
- 본사 ERP ↔ 고객 PC ERP 통신 = 단방향 Outbox 메시지 큐만

### Phase 4: 인프라 분리 (베타 출시 전)
- 본사 클라우드 서버 1대: `hitpan.kr` / `backoffice.hitpan.kr` / `api.hitpan.kr` 통합 호스팅
- 가비아 서브도메인 API 연동
- Cloudflare cloudflared 터널 자동 발급 시스템
- 본사망 ↔ Cloudflare 단방향 메시지 큐

### Phase 5: 정식 출시 (사장님 마스터플랜 v3 정합)
- 정식 도메인 전환 (`hitpan.kr` 메인)
- 베타 100곳 → 정식 고객 마이그
- 4축 헌장 (메모리 [project_four_axis_charter_20260515]) 통합 검증

---

## 5. 헌법 정합

| 헌법 | 4중 분리 정합 |
|---|---|
| #18 v3 본사로 업무 데이터 전송 금지 | ERP=로컬·DB 분리 = 본사 미보유 완성 |
| #22 데이터 최소주의 | 백오피스 평문 0 + 코드·도메인 분리 |
| #25 안전하게 (물리적 격리) | DB·코드·도메인·네트워크 4중 분리 = 완성형 |
| #28 Windows Update 후 cloudflared 자동 복구 | 정식 흐름에 워치독 5단계 포함 |
| #29 인프라 사전결재 | 가비아 API·Cloudflare 터널·DDL 모두 사장님 결재 후 |
| #30 고객 PC 자가 회복 | ERP 로컬 → 본사 의존 0 정합 |

---

## 6. 메모리 정합

- 🔥 [architecture_local_db_tunnel] = 핵심 아키텍처 (로컬 DB + 터널, 셀링 포인트)
- 🔥 [project_four_axis_separation_20260601] = 이 문서 정합
- [project_provisioning_design] = 원클릭 설치 + 자동 프로비저닝 흐름
- [project_cloud_server] = 본사 클라우드 1대 통합 (월 3~5만원)
- [project_domain_policy] = 베타·정식 도메인 정책
- [project_two_then_three_axis] = 2개 축 → 3개 축 단계 진입

---

## 7. PM 자수 박제 (사장님 호통)

PM 직전 답변 = ERP 도메인을 `demo.hitpan.kr` (베타) → 고객 PC 로컬 (정식)로 잘못 박제.

**사장님 정정:**
- ERP는 **처음부터 끝까지 로컬** (베타·정식 무관)
- demo.hitpan.kr = **데모용 1곳 (영업 시연·베타 체험)** 일뿐, 실제 고객사 운영은 처음부터 로컬
- 정식 도메인 = `{고객사}.hitpan.kr` (가비아 서브)

= 메모리 🔥 architecture_local_db_tunnel 망각 사고 자수.

---

## 8. 결재 안건 (사장님 모두결재 반영)

본 문서 박제 = **결재 완료 사항 모두 박제**:
- ✅ ERP = 로컬 (사장님 격언)
- ✅ 백오피스 = 클라우드 (사장님 격언)
- ✅ ERP 도메인 = `{고객사}.hitpan.kr` (가비아 서브)
- ✅ 현재 베타 데모 = `demo.hitpan.kr`

다음 단계 별도 결재:
- [ ] Phase 2 (코드 물리 분리) 진입 시점
- [ ] 가비아 API 키 확보 + HSM 보관
- [ ] Cloudflare 터널 자동 발급 시스템 구축
