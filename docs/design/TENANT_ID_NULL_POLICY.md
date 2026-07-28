# tenant_id NULL 허용 정책

**제정일:** 2026-04-23
**근거 이슈:** I-001 (테스트에서 의도 불명확 지적 → 설계 결정으로 공식화)

## 정책 요지

**멀티테넌트 환경에서 `tenant_id NULL`은 "이 레코드가 특정 테넌트에 속하지 않음(=시스템 또는 글로벌)"을 의미한다.**

## 적용 대상 (NULL 공식 허용 4 테이블)

| 테이블 | 용도 | NULL 의미 |
|---|---|---|
| `common_codes` | 공통 코드 (거래유형·결제수단·상태값 등) | 전 테넌트 공유 글로벌 코드 |
| `audit_logs` | 감사 로그 | 시스템 레벨 이벤트 (로그인 시도·크론·배치) |
| `security_alerts` | 보안 알림 | 시스템 전역 보안 이벤트 |
| `user_sessions` | 세션 | 로그인 완료 전 테넌트 미결정 단계 |

## 적용 제외 (NULL 금지 = 업무 데이터)

`sales_orders`, `sales_deliveries`, `purchase_orders`, `purchase_receipts`, `stock_ledger`, `partners`, `items`, `employees`, `partner_balance`, `item_stock`, `tenant_settings`, `journal_entries`, `journal_lines`, `accounts`, `sales_returns`, `purchase_returns`, `cashbook`, `monthly_closing` 등 **업무 도메인 테이블은 전부 NOT NULL**.

## 무결성 검증 시 예외 처리

테스트·감사 스크립트에서 tenant_id NULL 검사 시 위 4개 테이블은 **제외**한다. 반대로 업무 테이블에서 NULL 발견 시 즉시 CRITICAL.

## 구현 원칙

- 위 4개 테이블의 `tenant_id` 컬럼은 **NULLABLE** 유지
- 조회 시 `WHERE tenant_id = @TenantId OR tenant_id IS NULL` 패턴 허용
- `common_codes`는 테넌트별 오버라이드가 필요하면 동일 code_group·code로 tenant_id 채운 레코드를 추가 (오버라이드 우선)

## 관련 원칙

- CLAUDE.md 절대원칙 #2: tenant_id는 JWT 클레임에서만 (업무 테이블 한정)
- CLAUDE.md 절대원칙 #7: SaaS 계층 ↔ ERP 내부 권한 혼용 금지
