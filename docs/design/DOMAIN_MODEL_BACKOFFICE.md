# 백오피스 P0 7영역 도메인 모델

> 작성일: 2026-05-26
> 작성자: PM (브라운킴)
> 헌법 정합: #18 v3 (본사 데이터 0) / #22 (데이터 최소주의) / #24 (책임 분산) / #25 (3대 원칙) / #29 (인프라 사전 승인)
> 결재: 사장님 일괄 결재 ("응 모두결재!!", 2026-05-26)

---

## 0. 설계 헌장

### 0.1 본사 보유 절대 한계
- **본사가 보유하는 것**: 메타정보·카운터·식별자뿐
- **본사가 절대 보유 못 하는 것**: 매출/매입/원장/거래처/직원/상품/재고/세금계산서/결재 내역 등 ERP 업무 데이터 일체
- 백업조차 E2E 암호화 → 본사가 내용 모름

### 0.2 결제 = 인터페이스만
- `IPaymentProvider` 인터페이스 + 3 어댑터 (Toss / KCP / Mock)
- MVP는 MockPaymentAdapter로 가도, 베타 직전 TossPaymentsAdapter 활성화

### 0.3 도메인 모델 표기 약속
- 엔티티 = PascalCase, 컬럼 = snake_case
- 상태 머신은 별도 문서 `STATE_MACHINE_SUBSCRIPTION.md` 참조
- 시퀀스는 별도 문서 `SEQUENCE_3SYSTEMS.md` 참조

---

## P0-1. 고객사 관리 (Tenants)

### 1.1 엔티티

#### Tenant (고객사 마스터)
| 컬럼 | 타입 | 설명 |
|---|---|---|
| tenant_id | bigint PK | 고객사 식별자 (본사 발급) |
| tenant_code | varchar(20) UNIQUE | 도메인용 계정명 (`hitpan-{code}.kr`) |
| business_no | varchar(13) UNIQUE | 사업자등록번호 (10자리 + 메타) |
| representative_name | varchar(50) | 대표자명 |
| status | enum | pending / active / suspended / terminated |
| signed_up_at | datetime | 가입 일시 |
| activated_at | datetime NULL | 결제 완료·라이선스 발급 일시 |
| suspended_at | datetime NULL | 정지 일시 (미납·약관 위반) |
| terminated_at | datetime NULL | 해지 일시 (고객 요청·환불 완료) |
| created_at / updated_at | datetime | 감사용 |

#### BusinessInfo (사업자 메타)
| 컬럼 | 타입 | 설명 |
|---|---|---|
| business_info_id | bigint PK | |
| tenant_id | bigint FK | |
| business_no | varchar(13) | (Tenant 미러, 검증용) |
| business_name | varchar(100) | 상호 |
| business_type | varchar(50) | 업종 (서비스/도소매/제조) |
| business_item | varchar(100) | 종목 |
| address | varchar(200) | 사업장 주소 |
| address_detail | varchar(100) | 상세 주소 |

#### ContactInfo (연락처 메타)
| 컬럼 | 타입 | 설명 |
|---|---|---|
| contact_info_id | bigint PK | |
| tenant_id | bigint FK | |
| email | varchar(120) | 대표 이메일 (로그인 ID 후보) |
| phone | varchar(20) | 휴대폰 (PASS·카카오 본인확인) |
| backup_email | varchar(120) NULL | 백업 연락처 |

### 1.2 상태
`pending → active → suspended → terminated` (자세한 전환 조건 → `STATE_MACHINE_SUBSCRIPTION.md`)

### 1.3 본 영역 절대 금지
- 본사는 Tenant 산하 ERP 업무 데이터(매출·매입·거래처·세금계산서) 보유 금지
- 자식계정 비밀번호 평문/해시 본사 보유 금지 (ERP 로컬에만)

---

## P0-2. 구독 관리 (Subscriptions)

### 2.1 엔티티

#### Plan (요금제 마스터)
| 컬럼 | 타입 | 설명 |
|---|---|---|
| plan_id | int PK | |
| plan_code | varchar(20) UNIQUE | basic / pro / enterprise |
| display_name | varchar(50) | 베이직 / 프로 / 엔터프라이즈 |
| monthly_price | decimal(10,0) | 29000 / 59000 / 100000 |
| device_limit | int | 동시 사용 기기 수 |
| ai_token_quota | int | 월 AI 토큰 (100K / 500K / 3M) |
| is_active | bit | |

#### Subscription
| 컬럼 | 타입 | 설명 |
|---|---|---|
| subscription_id | bigint PK | |
| tenant_id | bigint FK | |
| plan_id | int FK | |
| status | enum | trial / active / past_due / cancelled |
| billing_cycle | enum | monthly / yearly |
| started_at | datetime | |
| current_period_start | datetime | 현 결제 주기 시작 |
| current_period_end | datetime | 현 결제 주기 종료 |
| cancel_requested_at | datetime NULL | 해지 신청 (이용 종료일까지 active) |
| cancelled_at | datetime NULL | 실제 해지 일시 |

#### BillingCycle (주기 이력)
| 컬럼 | 타입 | 설명 |
|---|---|---|
| cycle_id | bigint PK | |
| subscription_id | bigint FK | |
| cycle_no | int | 1, 2, 3... |
| period_start / period_end | datetime | |
| amount | decimal(10,0) | |
| payment_id | bigint FK NULL | 결제 연결 |

### 2.2 영역
- 구독 시작 (결제 성공 후 active)
- 자동 갱신 (current_period_end 도래 시)
- 해지 신청 → 이용 종료일까지 active 유지 → 자동 cancelled
- 다운그레이드 (다음 주기부터) / 업그레이드 (즉시, 일할 차액)

---

## P0-3. 계정 관리 (Accounts)

### 3.1 엔티티

#### Account (2계층)
| 컬럼 | 타입 | 설명 |
|---|---|---|
| account_id | bigint PK | |
| tenant_id | bigint FK | |
| account_type | enum | owner (대표 1) / staff (자식 N) |
| login_id | varchar(50) | 로그인 ID (tenant 내 UNIQUE) |
| display_name | varchar(50) | 표시명 |
| email | varchar(120) NULL | |
| phone | varchar(20) NULL | |
| status | enum | active / locked / disabled |
| last_login_at | datetime NULL | |

> 비밀번호 해시 / 권한 매핑은 **ERP 로컬 DB**에만. 본사는 식별자만 보유 (헌법 #18 v3).

#### Role / Permission (메타만, 마스터는 ERP 로컬)
| 컬럼 | 타입 | 설명 |
|---|---|---|
| role_id | int PK | |
| role_code | varchar(30) | tenant_admin / accountant / sales / warehouse 등 |
| display_name | varchar(50) | |

### 3.2 영역
- 자식계정 생성: 대표(owner) 결재 → 본사는 식별자만 발급
- 권한 부여: ERP 로컬에서 처리. 본사는 카운터만 (몇 명, 어떤 역할)
- 잠금/해제: 5회 비번 실패 시 자동 locked
- 비밀번호 재설정: 이메일·휴대폰 본인확인 후 ERP 로컬에서 처리

### 3.3 2계층 분리 (헌법 #25)
- 부모계정(owner) 인증 = 본사가 책임
- 자식계정(staff) 인증·권한 = 고객 책임 (ERP 로컬)
- 본사는 자식계정 비번·권한 모름

---

## P0-4. 결제 관리 (Payments)

### 4.1 인터페이스

```csharp
public interface IPaymentProvider {
    Task<PaymentResult> RequestPaymentAsync(PaymentRequest req);
    Task<PaymentResult> RefundAsync(string transactionId, decimal amount, string reason);
    Task<PaymentStatus> QueryStatusAsync(string transactionId);
    string ProviderCode { get; } // toss / kcp / mock
}
```

### 4.2 어댑터
- **TossPaymentsAdapter** — 베타 직전 활성화 (사장님 결재 완료, 결제 일원화)
- **KcpAdapter** — 백업 어댑터 (Toss 장애 대비)
- **MockPaymentAdapter** — 개발·테스트·MVP 가도용

### 4.3 엔티티

#### Payment
| 컬럼 | 타입 | 설명 |
|---|---|---|
| payment_id | bigint PK | |
| tenant_id | bigint FK | |
| subscription_id | bigint FK NULL | |
| cycle_id | bigint FK NULL | |
| provider_code | varchar(20) | toss / kcp / mock |
| transaction_id | varchar(100) | PG 거래 ID |
| amount | decimal(10,0) | |
| status | enum | pending / success / failed / refunded / partial_refunded |
| paid_at | datetime NULL | |
| failed_at | datetime NULL | |
| fail_reason | varchar(200) NULL | |

#### PaymentMethod (수단 메타, 카드번호·CVC 본사 보유 금지)
| 컬럼 | 타입 | 설명 |
|---|---|---|
| method_id | bigint PK | |
| tenant_id | bigint FK | |
| provider_code | varchar(20) | |
| billing_key | varchar(200) | PG 발급 빌링키 (자동결제용) |
| card_brand | varchar(20) | 카드사 표시명만 (예: 신한) |
| card_last4 | varchar(4) | 끝 4자리만 |
| is_default | bit | |

#### Refund
| 컬럼 | 타입 | 설명 |
|---|---|---|
| refund_id | bigint PK | |
| payment_id | bigint FK | |
| amount | decimal(10,0) | |
| reason | varchar(200) | |
| status | enum | pending / success / failed |
| requested_at / completed_at | datetime | |

#### Invoice (세금계산서·영수증 메타)
| 컬럼 | 타입 | 설명 |
|---|---|---|
| invoice_id | bigint PK | |
| payment_id | bigint FK | |
| invoice_type | enum | tax_invoice / receipt |
| issued_at | datetime | |
| pdf_url | varchar(500) NULL | (단기 만료 URL) |

### 4.4 절대 금지
- 카드번호·CVC·유효기간 평문/암호문 본사 보유 금지 (PCI-DSS)
- billing_key는 토큰(불가역) — 카드정보 복원 불가

---

## P0-5. 대리점 관리 (Resellers)

### 5.1 엔티티

#### Reseller
| 컬럼 | 타입 | 설명 |
|---|---|---|
| reseller_id | bigint PK | |
| reseller_code | varchar(20) UNIQUE | |
| business_no | varchar(13) UNIQUE | |
| business_name | varchar(100) | |
| representative_name | varchar(50) | |
| contact_email | varchar(120) | |
| contact_phone | varchar(20) | |
| status | enum | pending / active / suspended / terminated |
| signed_up_at | datetime | |

#### ResellerContract
| 컬럼 | 타입 | 설명 |
|---|---|---|
| contract_id | bigint PK | |
| reseller_id | bigint FK | |
| commission_rate | decimal(5,2) | % (예: 10.00) |
| contract_start / contract_end | date | |
| is_active | bit | |

#### ResellerCustomer (대리점 ↔ 고객사 매핑)
| 컬럼 | 타입 | 설명 |
|---|---|---|
| mapping_id | bigint PK | |
| reseller_id | bigint FK | |
| tenant_id | bigint FK | |
| mapped_at | datetime | 영업 매칭 일시 |
| mapping_source | enum | landing_code / manual / referral |
| is_active | bit | |

#### Commission (월별 수수료 정산)
| 컬럼 | 타입 | 설명 |
|---|---|---|
| commission_id | bigint PK | |
| reseller_id | bigint FK | |
| settle_month | char(7) | YYYY-MM |
| gross_revenue | decimal(12,0) | 산하 고객사 매출 합 |
| commission_rate | decimal(5,2) | |
| commission_amount | decimal(12,0) | |
| status | enum | pending / approved / paid |
| paid_at | datetime NULL | |

### 5.2 영역
- 대리점 등록 (본사 백오피스 관리자 결재)
- 영업 고객 매핑 (랜딩 가입 시 추천 코드 / 수동 / 추천)
- 수수료 계산: 월별 산하 고객사 결제액 × rate
- 정산: 익월 10일 일괄 송금

---

## P0-6. 백오피스 인증 (Admin Auth)

### 6.1 엔티티

#### AdminUser
| 컬럼 | 타입 | 설명 |
|---|---|---|
| admin_id | bigint PK | |
| login_id | varchar(50) UNIQUE | |
| password_hash | varchar(200) | bcrypt / argon2 |
| display_name | varchar(50) | |
| email | varchar(120) | |
| phone | varchar(20) | |
| status | enum | active / locked / disabled |
| totp_secret_enc | varbinary(200) | AES-256 암호화 (2FA) |
| totp_enabled | bit | |
| last_login_at | datetime | |

#### AdminRole
| 컬럼 | 타입 | 설명 |
|---|---|---|
| role_id | int PK | |
| role_code | varchar(30) | super_admin / ops_manager / cs_agent / accountant |
| display_name | varchar(50) | |

#### AdminRoleMap
| admin_id (FK) / role_id (FK) | 다대다 |

#### AdminSession
| 컬럼 | 타입 | 설명 |
|---|---|---|
| session_id | varchar(64) PK | |
| admin_id | bigint FK | |
| issued_at / expires_at | datetime | |
| ip_address | varchar(45) | |
| user_agent | varchar(500) | |
| revoked_at | datetime NULL | |

### 6.2 영역
- 로그인: ID/PW + TOTP (2FA 강제)
- 권한 검증: 역할 기반 (RBAC) + Policy
- 세션 관리: JWT + Refresh, IP/UA 변경 시 재인증
- 2FA: TOTP (Google Authenticator·Authy), 신규 가입 시 즉시 설정 강제

### 6.3 헌법 #25 정합
- 본사 관리자 ↔ 고객사 owner ↔ 자식계정 = 3축 완전 분리
- 본사 관리자가 고객사 데이터 접근 시 JIT CS 토큰 + 사장님 결재

---

## P0-7. 모니터링 (Telemetry)

### 7.1 엔티티

#### TenantHeartbeat
| 컬럼 | 타입 | 설명 |
|---|---|---|
| heartbeat_id | bigint PK | |
| tenant_id | bigint FK | |
| ping_at | datetime | |
| client_version | varchar(20) | ERP 클라이언트 버전 |
| db_version | varchar(20) | MariaDB 버전 |
| os_version | varchar(50) | Windows 버전 |
| watchdog_status | enum | healthy / warning / critical |

#### UsageMetric (카운터·집계만)
| 컬럼 | 타입 | 설명 |
|---|---|---|
| metric_id | bigint PK | |
| tenant_id | bigint FK | |
| metric_date | date | |
| menu_open_count | int | 메뉴별 진입 카운트 (JSON 가능) |
| api_call_count | int | |
| error_count | int | |
| ai_token_used | int | AI CS 토큰 사용량 |
| active_user_count | int | 일 활성 사용자 수 |

> ❌ 매출·매입 금액·거래처명·상품명·세금계산서 내용 → 절대 본사 보유 금지 (헌법 #18 v3)

#### AlertRule
| 컬럼 | 타입 | 설명 |
|---|---|---|
| rule_id | int PK | |
| rule_code | varchar(50) | heartbeat_missing / error_spike / token_exhausted |
| threshold | varchar(100) | JSON: {"minutes": 30} 등 |
| severity | enum | info / warning / critical |
| notify_channels | varchar(200) | email,slack,sms (CSV) |

### 7.2 영역
- 헬스체크: ERP → 백오피스 (Pull, 5분 주기) — 헌법 #18 v3 정합
- 사용량 집계: 일/주/월 단위
- 알림 발송: 임계치 초과 시 본사 ops_manager·CS에게

---

## 사장님 결재 영역 (요약)

| # | 결재 영역 | 사장님 결정 사항 |
|---|---|---|
| A | 요금제 가격 | 29k / 59k / 100k 확정 (월) |
| B | 결제 1순위 어댑터 | Toss 일원화 (사장님 5/12 결재) |
| C | 자동 갱신 | 매월 동일 일자 / 실패 시 3회 재시도 / 모두 실패 시 past_due |
| D | 해지 시점 | 신청 → 이용 종료일까지 active → 자동 cancelled (즉시 차단 아님) |
| E | 대리점 수수료율 | 계약 단위 협상 (기본 10%, 협상 가능) |
| F | 2FA 강제 | 본사 관리자 100% 강제 (예외 없음) |
| G | 텔레메트리 주기 | 5분 (헌법 #18 v3 본사 데이터 0 정합) |
| H | 알림 임계치 | 핫라인: heartbeat 30분 미수신 / 에러 spike 5분 100건 |

> 일괄 결재 완료 ("응 모두결재!!", 2026-05-26)

---

## 다음 가도 (W2)

- API 설계 (`API_SPEC_BACKOFFICE.md`) — 영역별 엔드포인트
- DDL 박제 (`hitpan_backoffice_db_ddl_v1.0.sql`)
- 인증 게이트웨이 흐름 (`AUTH_FLOW_BACKOFFICE.md`)
