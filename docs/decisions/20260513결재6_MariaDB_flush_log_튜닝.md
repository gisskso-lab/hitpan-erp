# 결재 안건 #6 — MariaDB innodb_flush_log_at_trx_commit 마이그 한정 변경

> **상신:** 2026-05-13 PM 닥터스트레인지
> **수신:** 사장님
> **긴급도:** P2 (작15 청크 처리 진입 전 결재 필요)
> **담당 매니저 리뷰:** DB 매니저 (1차) + 보안 매니저 (2차)

---

## 1. 배경

W3_CHUNK_ALGORITHM.md §6 MariaDB 튜닝 권장:
- `innodb_flush_log_at_trx_commit = 1` (기본, 매 트랜잭션 fsync) → 100만건 마이그 시 디스크 IO 병목
- **마이그 동안만** `=2` (1초당 1회 fsync)로 변경 권장 → 성능 5~10배 향상

## 2. 리스크

- `=2` 상태에서 **DB 서버 크래시(전원 차단·OS 패닉) 시 최근 1초 분량 데이터 손실 가능**
- 마이그 작업 중 발생 시 → 청크 단위 트랜잭션이므로 마지막 미커밋 청크만 손실 → 재개 가능

## 3. 선택지

### A안: 변경 안 함 (기본 `=1` 유지)
- 장점: 데이터 안전성 최대
- 단점: 100만건 처리 60분 → 5~6시간으로 확대

### B안: SESSION 한정 변경 (PM 추천)
```sql
SET SESSION innodb_flush_log_at_trx_commit = 2;
-- 마이그 진행
SET SESSION innodb_flush_log_at_trx_commit = 1; -- 복원
```
- 장점: 영향 범위 = 마이그 connection 1개만. 동시 사용자 영향 0
- 단점: SESSION이라도 try/finally 복원 실패 시 좀비 connection 위험

### C안: GLOBAL 변경 (마이그 시작/종료 시 토글)
- 장점: 모든 connection 적용
- 단점: 다른 사용자 트랜잭션도 영향 → 동시성 사고 시 데이터 손실 확대

## 4. 어벤져스 리뷰

| 매니저 | 의견 | 추천 |
|---|---|---|
| DB 매니저 | SESSION 한정은 표준 패턴. try/finally + health check 시 안전 | **B안** |
| 보안 매니저 | 형사영역 AES 컬럼 손실 시 복구 불가 → 마이그 중 크래시 시 청크 재개로 충분 | **B안** |
| 백엔드 매니저 | `IDbConnection.Open` 시점에 SET 1줄 + Dispose 시 복원 | **B안** |
| 본부장 춘식 | 100만건 6시간 → 1시간은 베타 일정에 결정적 | **B안** |
| AI수석 | 청크 단위 트랜잭션이므로 손실 < 1청크 = 최대 5,000행 → 재개로 복구 | **B안** |

**전원 B안 추천.**

## 5. PM 권고

**B안 SESSION 한정 변경:**
- `IDbConnection` open 시 `SET SESSION innodb_flush_log_at_trx_commit = 2`
- try/finally로 복원 보장
- 마이그 종료 시 health check (SHOW SESSION VARIABLES 검증)
- 작15 D3에 코드 1줄 + 단위 테스트 1 케이스

## 6. 결재 요청

- [ ] A안 (변경 안 함)
- [ ] **B안 (SESSION 한정)** ← PM 추천 + 어벤져스 만장일치
- [ ] C안 (GLOBAL 변경)
- [ ] 보류

**결재 시점:** 작15 D3 진입 전 (5/16 권장)
