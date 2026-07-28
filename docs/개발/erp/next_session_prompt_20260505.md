# 히트판 ERP — 세션 인수인계표
> 작성일: 2026-05-05 | 다음 세션 시작 시 이 파일을 먼저 읽을 것

---

## ✅ 오늘 완료 (2026-05-05)

| 커밋 | 내용 |
|---|---|
| `721b8e0` | C-1 MyISAM→InnoDB 전환 (운영DB 40개 직접 적용 + DB-38 마이그레이션) + H-6 Authorize 누락 25페이지 추가 |
| `39db285` | H-7 DTO 음수 검증 + H-5 BOM 재고부족 예외 + C-2 반품 partner_balance 역산 |

### 상세
- **C-1**: 운영 MariaDB 40개 테이블 MyISAM → InnoDB 직접 전환 완료. `DB-38_force_innodb.sql` 신규 설치용으로 생성
- **H-6**: Blazor 25개 페이지 `@attribute [Authorize]` 추가 (Purchase, Sales, Settings, Quotes, Platform, Reseller, Welcome 등)
- **H-7**: Create 요청 DTO 5종 `[Range]` 검증 (Qty>0, Amount/UnitPrice>=0) — PurchaseOrder/Receipt/SalesOrder/Delivery/Quotation
- **H-5**: BomService `GREATEST(qty-x, 0)` 제거 → 재고 부족 시 `InvalidOperationException` 명시적 예외 (생산·해체 2곳)
- **C-2**: `ConfirmPurchaseReturnAsync` — `partner_balance.total_purchase` 차감 추가 (반품 확정 시 거래처 잔액 역산)

---

## 🔜 다음 세션 최우선 작업

### C-3: BOM 생산 트랜잭션 래핑 확인
- 파일: `src/HitPan.Application/Services/BomService.cs`
- `ProduceAsync` 메서드 — 완제품↑ + 자재↓ 이미 트랜잭션 내에 있는지 확인
- 없으면 `BeginTransactionAsync` 래핑 추가

### H-4: monthly_summary 역산 (취소/반품)
- 매입반품 확정 시 monthly_summary 차감 → **이미 완료** (`ConfirmPurchaseReturnAsync` step 3에 있음)
- 판매취소 시 monthly_summary 차감 → `SalesService.CancelDeliveryAsync` 확인 필요

### H-8: 세금계산서 취소 역분개
- 파일: `src/HitPan.Application/Services/TaxInvoiceService.cs`
- `CancelAsync` 메서드에 `journal_lines` 역분개 INSERT 추가

---

## 📊 감사 점수 현황 (2026-05-05 기준)

| 축 | 이전 | 현재(예상) |
|---|---|---|
| 백엔드 | 76 | 82 |
| DB 스키마 | 57 | 80 (MyISAM 해결) |
| 워크플로우 연속성 | 72 | 78 |
| 프론트/API | 78 | 82 |
| **종합** | **71** | **80** |

---

## 📋 잔여 감사 이슈 (안전 수행 순서)

| ID | 내용 | 우선순위 |
|---|---|---|
| C-3 | BOM ProduceAsync 트랜잭션 래핑 | 내일 P0 |
| H-4 | 판매취소 monthly_summary 역산 확인 | 내일 P0 |
| H-8 | 세금계산서 취소 역분개 | 내일 P0 |
| H-1 | 매입반품 역분개 journal_lines | P1 |
| H-2 | 판매취소 역분개 journal_lines | P1 |
| H-3 | 세금계산서 취소 E2E 검증 | P1 |

---

## 🗓️ 이후 일정 (변경 없음)

| 날짜 | 내용 |
|---|---|
| 5/7 | C-3 + H-4 + H-8 완료 |
| 5/8 | H-1 + H-2 + H-3 저널·재무 |
| 5/9 | 전수조사 재실행 (목표 88점↑) |
| 5/12~16 | EVF 6대 영역 압박 테스트 |
| 5/17~22 | 베타 9곳 배포 + 핫픽스 |
| 5/23 | MVP 정식 론칭 🎯 |

---

## 🔧 개발 환경

- API: `localhost:5257`
- Blazor: `localhost:5234`
- DB: `hitpan_erp` (MariaDB 11.4.10) / hitpan / Hitpan2025!
- 테스트 계정: `tenant@hitpan.kr` / `Admin1234!`
- 현재 브랜치: `develop`
