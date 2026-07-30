# 백오피스 상세 PRD

> 작성일: 2026-05-06 | 상태: 확정

---

## 목적
본사 운영팀·대리점이 고객·대리점을 통합 관리하는 내부 도구.
고객사 업무 데이터는 절대 수집하지 않음 (헌법 #18).

---

## 계정 체계

| 계정 유형 | 접근 범위 |
|----------|----------|
| platform_admin | 전체 메뉴 접근 |
| reseller_admin | 담당 고객사만 조회 |

---

## 페이지 목록 및 흐름

```
/backoffice/login (로그인)
  ↓
/backoffice/dashboard (대시보드)
  ├── /backoffice/tenants (고객사 관리)
  │     ├── /backoffice/tenants/{id} (고객사 상세)
  │     ├── /backoffice/tenants/{id}/employees (직원 조회)
  │     ├── /backoffice/tenants/{id}/devices (기기 조회)
  │     └── /backoffice/tenants/{id}/subscription (구독 관리)
  ├── /backoffice/provisioning (프로비저닝 현황)
  ├── /backoffice/resellers (대리점 관리)
  │     ├── /backoffice/resellers/{id}/sales (영업실적)
  │     ├── /backoffice/resellers/{id}/promotions (프로모션)
  │     └── /backoffice/resellers/{id}/profits (영업이익)
  └── /backoffice/settings (설정)
```

---

## 페이지별 상세 요구사항

### /backoffice/dashboard (대시보드)
- 전체 고객사 수·활성·정지 현황
- 이번 달 신규 가입·해지 수
- Pull 동기화 마지막 성공 시각
- 프로비저닝 실패 건수 알림
- 대리점별 이번 달 실적 요약

### /backoffice/tenants (고객사 관리)
- 고객사 목록 (검색·필터: 상태·요금제·가입일)
- 데이터 원본: 랜딩페이지 Push
- 컬럼: 회사명·대표자·이메일·요금제·상태·가입일·도메인
- 액션: 상세보기·구독변경·정지·해지

### /backoffice/tenants/{id} (고객사 상세)
- 기본정보: 회사명·대표자·이메일·연락처·도메인
- 구독정보: 요금제·결제일·다음 결제일·결제수단
- 프로비저닝 정보: 터널ID·DNS 상태·마지막 접속일
- 직원 수·기기 수 요약 (Pull 복사본 기준)

### /backoffice/tenants/{id}/employees (직원 조회)
- 데이터 원본: ERP Pull (5분 주기 복사본)
- 조회만 가능 (수정 불가)
- 컬럼: 이름·이메일·직급·재직여부·등록일
- 마지막 동기화 시각 표시

### /backoffice/tenants/{id}/devices (기기 조회)
- 데이터 원본: ERP Pull (5분 주기 복사본)
- 조회만 가능 (수정 불가)
- 컬럼: 기기ID·기기명·등록일·마지막 접속일
- 요금제 기기 한도 대비 사용량 표시

### /backoffice/tenants/{id}/subscription (구독 관리)
- 현재 요금제·결제 이력
- 요금제 변경·일시정지·해지
- 환불 처리 (토스페이먼츠 연동)

### /backoffice/provisioning (프로비저닝 현황)
- 전체 프로비저닝 진행 상태 목록
- 상태: 대기·진행중·완료·실패
- 실패 건: 원인 표시 + 재시도 버튼
- DNS 상태·터널 상태 실시간 확인

### /backoffice/resellers (대리점 관리)
- 대리점 목록·계정 생성·수정
- reseller_admin 계정 발급

### /backoffice/resellers/{id}/sales (영업실적)
- 데이터 원본: 백오피스 직접 CRUD
- 월별 신규 고객·유지 고객·해지 고객 등록
- 담당 고객사 목록 연동

### /backoffice/resellers/{id}/promotions (프로모션)
- 데이터 원본: 백오피스 직접 CRUD
- 프로모션 기간·할인율·적용 고객사 등록

### /backoffice/resellers/{id}/profits (영업이익)
- 데이터 원본: 백오피스 직접 CRUD
- 월별 수수료·정산 금액 등록·조회

---

## API 설계

### 랜딩 → 백오피스 (Push)
```
POST /backoffice/api/tenants
  Body: { email, companyName, ceoName, phone, plan, accountName }
  → 테넌트 생성 + 프로비저닝 큐 적재

POST /backoffice/api/subscriptions
  Body: { tenantId, plan, paymentKey, amount }
  → 결제 정보 등록
```

### 백오피스 → ERP (Pull, 5분 주기)
```
GET {erpDomain}/api/sync/employees
  Header: Authorization: Bearer {SYNC_TOKEN}
  Response: [{ name, email, position, isActive }]

GET {erpDomain}/api/sync/devices
  Header: Authorization: Bearer {SYNC_TOKEN}
  Response: [{ deviceId, deviceName, registeredAt }]
```

### 프로비저닝 엔드포인트
```
POST /backoffice/api/provision
  → Cloudflare DNS 생성 + 터널 발급 + 라이선스 키 생성

GET /backoffice/api/provision/{tenantId}/status
  → 프로비저닝 진행 상태 확인

POST /backoffice/api/provision/{tenantId}/retry
  → 실패한 프로비저닝 재시도
```

---

## Pull 동기화 스케줄러

```
5분 주기 실행:
  1. 활성 테넌트 목록 조회
  2. 각 테넌트 ERP에 GET /api/sync/employees 호출
  3. 각 테넌트 ERP에 GET /api/sync/devices 호출
  4. 응답 데이터를 백오피스 DB에 Upsert
  5. 마지막 동기화 시각 갱신

실패 처리:
  - 무응답: 15분 후 재시도
  - 3회 연속 실패: 운영팀 이메일 알림
  - 실패해도 다음 테넌트 계속 진행
```

---

## 비기능 요구사항
- 내부 도구. 외부 공개 불필요
- platform_admin IP 화이트리스트 접근 권장
- 헌법 #18: 고객사 업무 데이터 절대 수집 금지
- Pull 데이터 허용 목록: 이름·이메일·직급·재직여부·기기ID·기기명·등록일만
