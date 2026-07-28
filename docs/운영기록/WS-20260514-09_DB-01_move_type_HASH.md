# WS-20260514-09 — DB-01 stock_ledger.move_type HASH UNIQUE 봉합

**발행:** 2026-05-14 (수) / PM
**결재:** 사장님 (DB 매니저·ERP·영업·마케팅·설계팀장 일치)
**담당:** DB 매니저 + DB개발자 1
**헌법:** #13 (DESCRIBE 의무), #17 (InnoDB)
**마감:** 5/14 11:00 (ALTER DDL은 dry-run 전에 적용)

---

## 배경

- **현상**: `stock_ledger.move_type` 컬럼이 **longtext** (실측 _db_columns_dump.txt:310 확인)
- **인덱스**: `uq_stock_ledger_source HASH(tenant_id, source_type, source_id, item_id, move_type)` — 5컬럼 HASH UNIQUE의 마지막 키가 longtext
- **위험**:
  - HASH 인덱스가 BLOB/TEXT 직접 못 받음 → 묵시 prefix 적용 추정 상태
  - V3 60만 row 진입 시 페이지 분할 폭주 가능 (DB 매니저 평가)
  - **116K → 232K 증식 위험** (PYOJUN 두 번 마이그 시)
- **DB 매니저 강력 경고**: "게이트 진입 후 발견 시 60분 안에 못 잡는다"

## ⚠️ 헌법 #13 의무 — DESCRIBE 선행

ALTER 전 반드시:
1. `DESCRIBE stock_ledger;` 결과 move_type 컬럼 정확 확인
2. `SHOW CREATE TABLE stock_ledger;` 인덱스 정의 확인
3. 현재 데이터 분포: `SELECT DISTINCT move_type, COUNT(*) FROM stock_ledger GROUP BY move_type;`
   - 예상 결과: 'in', 'out' 2종만
4. DDL 적용 안전성: `SHOW INDEX FROM stock_ledger;`

## 봉합 범위

1. move_type을 `varchar(10) NOT NULL` 로 ALTER (현재 116,420행 → ALTER 시간 측정)
2. uq_stock_ledger_source HASH 인덱스 재생성
3. 적용 전 DB 백업 (mysqldump stock_ledger)

## 검증 게이트

- [ ] DESCRIBE 결과 PM·DB 매니저 양자 확인
- [ ] 백업 완료 확인
- [ ] ALTER 후 `SHOW CREATE TABLE` 재확인
- [ ] 기존 116,420행 데이터 손실 0 확인
- [ ] 인덱스 동작 확인: `EXPLAIN INSERT IGNORE INTO stock_ledger ...` USING index 표시

## 산출물

- ALTER DDL 1건 (백업 + 적용 + 검증)
- 운영 매뉴얼 보강 (move_type enum 명문화)
