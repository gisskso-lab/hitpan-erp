# WS-20260514-10 — DB-02 collections 멱등성 UNIQUE 인덱스 추가

**발행:** 2026-05-14 (수) / PM
**결재:** 사장님 (CTO·DB 매니저·본부장 일치)
**담당:** DB 매니저 + DB개발자 1
**헌법:** #3 (INSERT ONLY 멱등)
**마감:** 5/14 11:00 (WS-20260514-08 선행 의존성)

---

## 배경

- **현상**: collections 테이블에 멱등 키 UNIQUE 부재. `collection_id = Guid.NewGuid()` 매번 새로 발급
- **위험**: 옵션 B 재실행 시마다 614,212행씩 누적 → Aging/매출잔액 KPI 직격
- **현재 614,212행 보존 상태** — 재실행 1회 = 1,228K, 2회 = 1,842K로 증식

## 봉합 범위

### 1. 컬럼 추가 (이미 존재 시 활용)

collections 테이블에 마이그 멱등 키용 컬럼:
- `source_type varchar(30) DEFAULT NULL` (예: 'migration', 'manual')
- `source_id varchar(50) DEFAULT NULL` (예: 'mig-2018-12-31-12345-001')

⚠️ **헌법 #13 DESCRIBE 의무**: 컬럼 이미 존재할 가능성 확인 먼저

### 2. UNIQUE 인덱스 추가

```
uq_collections_source UNIQUE (tenant_id, source_type, source_id)
WHERE source_type IS NOT NULL  -- partial index (MariaDB 미지원 시 일반 UNIQUE)
```

### 3. 기존 614K 행 처리

- 기존 데이터는 source_type=NULL이므로 UNIQUE 충돌 0
- 신규 마이그 INSERT만 멱등 키 활용

## 의존성

- WS-20260514-08 (CODE-06)이 이 UNIQUE 키 활용 → **이 작지서 먼저 완료 필요**

## 검증 게이트

- [ ] DESCRIBE collections 결과 PM·DB 매니저 양자 확인
- [ ] 백업 완료 (mysqldump collections)
- [ ] ALTER 후 SHOW CREATE TABLE 확인
- [ ] 기존 614,212행 데이터 손실 0 확인
- [ ] EXPLAIN INSERT IGNORE 인덱스 활용 확인

## 산출물

- ALTER DDL 1건
- 마이그 후 collections 카운트 모니터링 SQL 1건
