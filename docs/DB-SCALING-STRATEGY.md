# 히트판 ERP — DB 스케일링 전략 (MVP → 베타 → 정식)

**작성**: 2026-04-22 · 브라운킴(설계팀장) + DB매니저 공동  
**배경**: MySQL은 FK와 파티셔닝 동시 사용 불가. FK 무결성을 우선시하여 파티셔닝을 보류하고, 대안 전략으로 **월별 스냅샷 + Archive 테이블** 조합 채택.

---

## 1. 현재 상태 (MVP)

| 항목 | 값 |
|---|---|
| 가장 큰 테이블 | `stock_ledger` 65K 행 (1 테넌트, 5년) |
| FK 제약 | 54개 (stock_ledger는 items · warehouses FK 보유) |
| 주요 인덱스 | `idx_tenant_item_date`, `idx_tenant_date`, `idx_sl_source_type` |
| 응답 속도 | 65K 집계 58ms (임계값 500ms 대비 여유) |
| 판정 | **MVP 규모에서는 파티셔닝 불필요** |

---

## 2. 스케일 분기점

| 단계 | 원장 예상 규모 | 조치 |
|---|---|---|
| MVP (~30테넌트 × 1년) | 약 500K 행 | **현 상태 유지** (인덱스만) |
| 베타 (~100테넌트 × 1년) | 약 1.5M 행 | **월말 스냅샷 테이블 활성화** (아래 3절) |
| 정식 1년 (~300테넌트 × 1년) | 약 5M 행 | **Archive 테이블 + Read Replica** (아래 4절) |
| 정식 3년 (~1000테넌트 × 3년) | 50M+ 행 | **FK 완화 + 파티셔닝** (아래 5절) |

---

## 3. 월말 스냅샷 테이블 (베타 진입 시 활성화)

기존 `stock_monthly_snapshot` 테이블이 존재하므로 **배치만 활성화**하면 됨.

### 3.1 스키마 (기존)
```sql
-- 이미 존재
CREATE TABLE stock_monthly_snapshot (
  snapshot_id BIGINT AUTO_INCREMENT,
  tenant_id VARCHAR(36),
  item_id VARCHAR(36),
  warehouse_id VARCHAR(36),
  ym VARCHAR(7),                    -- 'YYYY-MM'
  opening_qty DECIMAL(15,3),
  in_qty DECIMAL(15,3),
  out_qty DECIMAL(15,3),
  closing_qty DECIMAL(15,3),
  avg_cost DECIMAL(15,4),
  created_at DATETIME(6)
);
```

### 3.2 적재 배치 (매월 1일 02:00)
```sql
-- 전월 스냅샷 적재
INSERT INTO stock_monthly_snapshot (...)
SELECT tenant_id, item_id, warehouse_id,
  DATE_FORMAT(DATE_SUB(CURDATE(), INTERVAL 1 MONTH), '%Y-%m') AS ym,
  -- 전월초 잔액
  COALESCE((SELECT closing_qty FROM stock_monthly_snapshot
            WHERE ym = DATE_FORMAT(DATE_SUB(CURDATE(), INTERVAL 2 MONTH), '%Y-%m')
              AND tenant_id=l.tenant_id AND item_id=l.item_id AND warehouse_id=l.warehouse_id), 0) AS opening_qty,
  SUM(qty_in), SUM(qty_out),
  COALESCE(opening_qty,0) + SUM(qty_in) - SUM(qty_out),
  AVG(unit_cost), NOW(6)
FROM stock_ledger l
WHERE ym = DATE_FORMAT(DATE_SUB(CURDATE(), INTERVAL 1 MONTH), '%Y-%m')
GROUP BY tenant_id, item_id, warehouse_id;
```

### 3.3 리포트 질의 최적화
- **이전**: `stock_ledger` 5년치 65K 행 스캔
- **이후**: `stock_monthly_snapshot` + 당월 `stock_ledger` delta만 집계 → **10배 속도**

---

## 4. Archive 테이블 + Read Replica (정식 1년차)

### 4.1 콜드 데이터 분리
- 24개월 이상 경과 원장 → `stock_ledger_archive` 이관
- 활성 테이블은 최근 24개월만 유지 → **인덱스 재구축 필요 없이 크기 제어**
- 조회 뷰로 통합: `CREATE VIEW v_stock_ledger_all AS SELECT * FROM stock_ledger UNION ALL SELECT * FROM stock_ledger_archive`

### 4.2 Read Replica
- MariaDB 기본 replication으로 **리포트/BI 전용 슬레이브** 구성
- 애플리케이션은 `DbConnectionFactory`에서 Read/Write 라우팅

---

## 5. FK 완화 + 파티셔닝 (정식 3년차, 50M+ 행)

이 시점에서는 **FK 제약을 stock_ledger에서만** 제거하고 애플리케이션 레벨 검증으로 대체:

```sql
-- 1. 기존 FK 제거
ALTER TABLE stock_ledger
  DROP FOREIGN KEY fk_sl_item,
  DROP FOREIGN KEY fk_sl_warehouse;

-- 2. 월별 파티션 적용 (120개 파티션 = 10년)
ALTER TABLE stock_ledger PARTITION BY RANGE (YEAR(ledger_date)*100 + MONTH(ledger_date)) (
  PARTITION p202601 VALUES LESS THAN (202602),
  PARTITION p202602 VALUES LESS THAN (202603),
  -- ...
  PARTITION pfuture VALUES LESS THAN MAXVALUE
);

-- 3. 애플리케이션에서 item/warehouse 존재성 검증
-- (이 시점에서는 이미 데이터 정합성이 검증된 운영 상태이므로 리스크 낮음)
```

---

## 6. 체크포인트

| 시점 | 확인 지표 | 임계값 |
|---|---|---|
| 월말 | 단일 테넌트 최대 원장 행수 | 50K 초과 시 3절 활성화 |
| 분기말 | 가장 느린 리포트 응답시간 | 1s 초과 시 4절 적용 |
| 연말 | DB 총 크기 | 20GB 초과 시 5절 적용 |

---

## 7. 관련 파일

- `scripts/backup-daily.ps1` / `backup-daily.sh` — 일일 백업 (cron / Task Scheduler)
- `scripts/seed-tools-5years-part1~3b.sql` — 공구상가 5년치 테스트 시드
- `src/HitPan.Application/Interfaces/IReportQueryBuilder.cs` — 리포트 쿼리 추상화 (베타 중 분할 예정)
