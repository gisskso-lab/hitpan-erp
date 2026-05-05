# 히트판 3개 시스템 아키텍처 설계

> 확정일: 2026-05-06 | 전원 합의

---

## 시스템 역할 분리

| 시스템 | 역할 | 소유 데이터 |
|--------|------|------------|
| 랜딩페이지 | 가입·결제·다운로드 | 없음 (백오피스로 전달) |
| 백오피스 | 고객관리·대리점관리·프로비저닝 | 테넌트 마스터, 구독결제, 대리점 데이터 |
| 히트판 ERP | 업무 전담 | 직원, 기기, 모든 업무 데이터 (원본) |

---

## 데이터 흐름 (방식 B — Pull 기반)

```
[랜딩페이지]
  회원가입 → POST /backoffice/api/tenants (즉시 Push)
  결제완료 → POST /backoffice/api/subscriptions (즉시 Push)

[히트판 ERP → 백오피스] Pull (5분 주기, 백오피스 스케줄러)
  GET {erp}/api/sync/employees   → 이름·이메일·직급·재직여부만
  GET {erp}/api/sync/devices     → 기기ID·기기명·등록일만

[ERP → 백오피스] 즉시 Push (구독결제 변경 시만)
  POST /backoffice/api/subscriptions

[백오피스 독자 CRUD]
  대리점 영업이익·실적·프로모션 — 백오피스 DB에만 존재
```

---

## 데이터 원본 소유권

```
본사 백오피스 원본:
  - tenants (테넌트 마스터)
  - subscriptions (구독결제)
  - reseller_sales / reseller_promotions / reseller_profits (대리점)

고객사 ERP 원본:
  - users (직원계정) → 백오피스가 복사본 보유
  - tenant_devices (기기) → 백오피스가 복사본 보유
  - 모든 업무 데이터 (매출/매입/원장/재고 등) → 절대 본사로 안 나감
```

---

## 백오피스 DB 테이블

```sql
tenants              -- 랜딩 가입 시 생성 (원본)
tenant_employees     -- ERP Pull 복사본 (5분 주기)
tenant_devices       -- ERP Pull 복사본 (5분 주기)
subscriptions        -- 즉시 Push 원본
reseller_sales       -- 백오피스 직접 CRUD
reseller_promotions  -- 백오피스 직접 CRUD
reseller_profits     -- 백오피스 직접 CRUD
```

---

## API 설계

### 랜딩 → 백오피스 (즉시 Push)
```
POST /backoffice/api/tenants         테넌트 생성 + 프로비저닝 트리거
POST /backoffice/api/subscriptions   결제 정보 등록
```

### 백오피스 → ERP (Pull, 5분 주기)
```
GET /api/sync/employees   직원 목록 (이름·이메일·직급·재직여부만)
GET /api/sync/devices     기기 목록 (기기ID·기기명·등록일만)
```
- 인증: 본사 전용 Sync 토큰 (일반 JWT와 완전 분리, 읽기 전용)
- ERP .env: BACKOFFICE_SYNC_TOKEN

### 백오피스 독자 CRUD
```
/backoffice/api/resellers/*   대리점 영업이익·실적·프로모션
```

---

## ERP 화면별 데이터 흐름

| ERP 화면 | 원본 위치 | 백오피스 반영 |
|----------|----------|--------------|
| 일반정보설정 | 백오피스 tenants | 조회만 |
| 직원계정관리 | ERP 로컬 DB | Pull 5분 주기 |
| 기기관리 | ERP 로컬 DB | Pull 5분 주기 |
| 구독/결제 | 백오피스 subscriptions | 변경 시 즉시 Push |

---

## 장애 대응

| 상황 | ERP 영향 | 대응 |
|------|----------|------|
| ERP 인터넷 끊김 | 없음 (ERP 정상 운영) | Pull 재시도 15분 후 |
| 백오피스 서버 장애 | 없음 (ERP 정상 운영) | 3회 실패 시 백오피스 알림 |
| Pull 지연 | 없음 | 마지막 성공 시각 백오피스 표시 |

---

## 헌법 #18 준수 — Pull 허용 데이터 목록

```
허용:
  employees: 이름, 이메일, 직급, 재직여부
  devices: 기기ID, 기기명, 등록일

절대 금지:
  매출·매입·원장·재고·세금계산서·거래처·상품
  단 한 컬럼도 포함 불가
```

---

## 원클릭 설치 프로비저닝 흐름

```
1. 랜딩페이지 회원가입
   → 백오피스 자동 처리:
     - hitpan-{계정명}.kr DNS 생성 (Cloudflare API)
     - cloudflared 터널 생성 + credentials 발급
     - 라이선스 키 생성
     - 이메일 발송 (다운로드 링크 + 라이선스 키)

2. EXE 다운로드 → 실행
   → 라이선스 키 입력 (유일한 입력)
   → 인스톨러 자동 처리:
     - 환경 체크 (MariaDB·닷넷·포트 충돌)
     - 본사 API에서 credentials + 도메인 수신
     - MariaDB 설치/재사용
     - 닷넷 런타임 설치
     - 히트판 API 배포
     - cloudflared 서비스 등록
     - appsettings.json 자동 기입
     - 바탕화면 바로가기 + 엣지·크롬 즐겨찾기 추가

3. 완료 → https://hitpan-{계정명}.kr 자동 오픈
```

---

## 첫 로그인 약관 동의 흐름

```
로그인 완료
  → GET /api/agreements/me
  → 미동의 또는 구버전 동의 → 약관 동의 페이지 강제 이동
  → 필수 4개 동의 (이용약관·개인정보·위탁·만14세)
  → 동의 일시·IP DB 기록
  → 대시보드 진입
```
