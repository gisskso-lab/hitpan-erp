# 히트판 ERP 변경 이력

## [Beta 1.0] — 2026-05-05

### 무결성 (Integrity)
- 매입반품 확정 시 역분개 자동 기표 (AutoJournalHelper.RecordPurchaseReturnAsync)
- 매출취소(거래명세서) 시 역분개 + monthly_summary 음수 차감
- 세금계산서 취소 시 역분개 자동 기표 (RecordSalesCancelAsync)
- monthly_summary 멱등성 보강 — MonthlySummaryGuard.TryApplyAsync 전면 적용
- DTO 음수 금액 검증, BOM 재고부족 예외, 반품 partner_balance 역산

### 보안 (Security)
- MyISAM→InnoDB 40개 테이블 전환 (트랜잭션·FK 지원)
- [Authorize] 누락 25개 페이지 추가
- Idempotency-Key 헤더 적용 (매입확정·거래명세서확정 중복 방지)
- JWT secret 취약 기본값 차단 (Production Fail-Fast)
- Rate Limiting 미들웨어 (로그인 IP 기반 + 계정 잠금)

### 품질 (Quality)
- ExcelExportService decimal→double 불필요 캐스팅 제거 (ClosedXML 직접 지원)
- MdbMigrationService 빈 catch 블록 로깅 추가 (헌법 #15)
- DB-04 초기 테이블 10개 COLLATE=utf8mb4_unicode_ci 통일

### 워크플로우 (Workflow)
- 중복발주전환·단가0·이중확정 사전 차단
- 다이렉트 판매 시 수주 자동생성으로 정합성 확보
- 발주 is_auto 필터 + 정합성 감지 BackgroundService
- 수주→판매전환 정합성 수정

### 모바일 (Mobile)
- iOS 스타일 모바일 레이아웃 (헤더/탭바/대시보드 반응형)
- 전체메뉴 iOS 앱보관함 폴더 스타일
- 거래명세서·문서 7종 모바일 카드 목록 + PDF뷰
- 업체/상품 마스터 카드 목록
- PDF/Excel iOS 팝업 차단 수정

### 기능 (Features)
- ERP 로그 기록 메뉴 추가 (자료관리 > ERP 로그 기록)
- 업체 상세 — 전화/이메일 원터치 + 카카오·티맵 네비 연동
- 카카오내비·티맵 주소 복사+딥링크+웹폴백 구현
- 현황/통계 페이지 모바일 그리드 최적화

### DB 마이그레이션
- DB-33: sales_orders ENUM 확장
- DB-34: sales_orders is_auto 컬럼
- DB-35: journal_lines UNIQUE 제약
- DB-36: purchase_orders is_auto 컬럼
- DB-37: stock_ledger UNIQUE 제약
- DB-38: 전 테이블 InnoDB 강제 전환
