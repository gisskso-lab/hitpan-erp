# 작20260503-10 — partner_balance 정합성 봉합 (P0 최긴급)

## 🔴 우선순위
P0 최긴급 (사장님 직감 발견 → 전수조사로 확정. 베타 출시 블로커 1순위)

## 사장님 발견 (2026-05-03 라운드 4 전수조사)
> "워크플로우 흐름에 따라 전체적으로 데이터 전수조사 해봐. 뭐가 안 맞는 거 같은데?"

CTO 전수조사 결과 — **거래처 잔고 무결성 완전 붕괴**:

| 거래처 | 매출 실측 | partner_balance.total_sales | 매입 실측 | partner_balance.total_purchase |
|---|---:|---:|---:|---:|
| 공영정보 | 24,222 | **0** ❌ | 1,100,233,038 (11억) | **0** ❌ |
| (이름 깨짐) | 1,210,000 | **partner_balance row 자체 없음** | - | - |

→ 화면에서 "11억 받을 게 있는데 음수로 표시"

## 진범 (CTO Phase A 완료, 4개 확정)

### 진범 1 (P0 최긴급)
- **위치:** `src/HitPan.Application/Services/SalesService.cs`
- **증상:** `IEventPublisher` 멤버 자체 없음 → `delivery.confirmed` / `delivery.cancelled` 이벤트 발행 X
- **영향:** SyncEventPublisher가 짜놓은 partner_balance 갱신 코드가 **호출되지 않음**

### 진범 2 (P0 최긴급)
- **위치:** `src/HitPan.Application/Services/PurchaseService.cs`
- **증상:** 동일 — `IEventPublisher` 멤버 없음 → `purchase.confirmed` 발행 X
- **영향:** total_purchase 갱신 X (단, item_stock은 PurchaseService 내부 직접 UPDATE로 정상 작동)

### 진범 3 (P1, 사장님 결재 필요)
- **위치:** `SalesService.cs:566` `AND d.status <> 'cancelled'`
- **증상:** 취소 거래명세서가 화면에서 영구 숨김 (DB 7건 / 화면 4건)
- **결재:** 의도된 동작? 아니면 토글 필요?

### 진범 4 (P3 별건)
- **증상:** `?????` 거래처명 인코딩 사고 (DB 1건, partner_balance row 없음)
- **별도 분석 작업지시서로 분리**

## 헌법 영향
- **절대원칙 #20 (워크플로우 끊김 금지) 위반의 결과**
- 사장님 격언 ① 매입→재고 / ② BOM / ③ 판매→재고→세금계산서 중
  → "거래처 매출/매입 잔고" 끊김 (격언 확장)
- 헌법 #18 v3 미저촉 (본사 데이터 무관, 고객사 ERP 내부 정합성)
- 헌법 4조 #4 (DB↔백엔드↔프론트 끊김 0) 위반 → 봉합

## 처방 (A안 — CTO 권고, 사장님 결재 받음)

### Phase B-1 (CTO 단독, 5분) — 이중 차감 위험 점검
- SalesService·PurchaseService 내부 item_stock UPDATE 흔적 grep
- SyncEventPublisher의 item_stock UPDATE와 중복 여부 확인
- → 이중 차감 위험 1줄 보고

### Phase B-2 (사장님 결재) — 처리 방향
- 위험 있으면: 이벤트의 item_stock 부분 제거 (이미 Service가 처리)
- 위험 없으면: 그대로

### Phase B-3 (CTO 단독, 30분) — 코드 봉합
1. `SalesService` 생성자에 `IEventPublisher _events` 주입
2. `ConfirmDeliveryAsync` 트랜잭션 끝에 `_events.PublishAsync("delivery.confirmed", ...)`
3. `CancelConfirmedDeliveryAsync` 끝에 `delivery.cancelled`
4. `PurchaseService` 동일 패턴
5. 빌드 errors 0 + warnings 0 (절대원칙 #19)

### Phase B-4 (CTO 단독, 5분) — 데이터 1회성 재계산
```sql
-- 기존 partner_balance 무결성 회복 (실측 트랜잭션 합산)
UPDATE partner_balance pb
   SET total_sales = (SELECT COALESCE(SUM(total_amount+vat_amount), 0)
                        FROM sales_deliveries
                       WHERE tenant_id = pb.tenant_id AND partner_id = pb.partner_id
                         AND status='confirmed'),
       total_purchase = (SELECT COALESCE(SUM(total_amount+vat_amount), 0)
                          FROM purchase_receipts
                         WHERE tenant_id = pb.tenant_id AND partner_id = pb.partner_id
                           AND status='confirmed'),
       last_updated_at = NOW(6);
-- partner_balance row 누락된 거래처는 INSERT
```

### Phase C (사장님 검증) — 헌법 4조 5중
1. 신규 거래명세서 1건 생성 + 확정
2. partner_balance 즉시 자동 갱신 확인
3. 신규 거래 취소 → 자동 환원 확인
4. 신규 매입 1건 → total_purchase 자동 갱신
5. 회귀 검사 (재고원장·재고잔량·다른 화면)

## 데이터 연결성 보호 (사장님 헌법 항목)
| 연결성 | 봉합 후 |
|---|---|
| 외래키 무결성 | 그대로 (안 건드림) |
| 참조 무결성 (확정→잔고) | **회복** |
| 트랜잭션 원자성 | **강화** (이벤트 동일 tx) |
| 워크플로우 인과 (취소→환원) | **회복** |
| 재고 정합성 (item_stock) | Phase B-1로 보호 |

## 담당
- 메인: CTO Final Verifier (래리 앨리슨)
- 보조: 백엔드 매니저 (코드 리뷰)
- 데이터: DB 매니저 (재계산 SQL 검증)

## SLA
- 빠른 트랙 (P0)
- Phase B-1: 5분
- Phase B-2 결재: 5분
- Phase B-3: 30분
- Phase B-4: 5분
- Phase C: 사장님 검증 10~15분
- **합계: 1시간 이내**

## 결재 항목
- ☐ A안 (이벤트 모델 정상화) 진행
- ☐ Phase B-1 → B-2 → B-3 → B-4 → C 순차 진행
- ☐ 진범 3 (취소 거래명세서 화면 숨김) 처리 방향 결재 (별도 또는 함께)
- ☐ 진범 4 (`?????` 거래처) 별도 작업지시서 분리

## 후속 작업 (별도 발행)
- 진범 4 인코딩 사고 분석 (사장님 검증 발견 #6 한글 라벨링과 묶음 가능)
- 검증팀 사후 감사 (왜 이 함정이 사장님 검증 전까지 안 잡혔나)
