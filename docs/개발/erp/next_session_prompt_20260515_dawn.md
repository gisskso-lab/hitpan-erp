# 5/15 새벽~09:00 가동 인수인계 — 온전한 정공법 6축

**작성:** 2026-05-14 (수) 19:30+ / PM
**사장님 결재:** "온전한 정공법" 격언 (2026-05-14 저녁)
**가동 시각:** 2026-05-15 (목) **09:00**

---

## 0. 사장님 격언 (헌법 #26 + 정공법)

> **"데이터가 60만건이든 100만건이든 1000만건이든 — MDB 3개 파일 데이터 이관 총 시간이 1분을 넘지 않는다. 고객이 쓰는 거야 내가 쓰는 게 아니고."**
>
> **"온전한 정공법!!"**

PM 새벽~저녁 받아쓰기 사고 누적 — 봉합 변형·반쪽 정공법 폐기. 구조 변경만.

---

## 1. 작업 대상 — WS-20260514-11 (온전한 정공법 6축)

**문서:** `docs/work-orders/WS-20260514-11_온전한_정공법_6축.md`

| 축 | 작업 | 담당 |
|---|---|---|
| 1 | 마이그 1분 절대 (MySqlBulkCopy + LOAD DATA + 병렬 + Stream + 인덱스 DISABLE) | 백엔드 매니저 + DB 매니저 |
| 2 | 멱등성 영속 키 (source_hash + ON DUPLICATE KEY UPDATE) | 백엔드 + DB 듀얼 |
| 3 | 마이그 전용 connection 풀 분리 (IMigrationDbConnection) | 백엔드 매니저 |
| 4 | 진행률 SignalR Hub (폴링 폐기) | 백엔드 + 프론트 |
| 5 | POTHER 4 풀스택 (명함·AS·배송·달력) | DB + 백엔드 + 프론트 |
| 6 | PII AES + 5중 검증 정공법 (CodeQL custom + TruffleHog 룰셋 + Roslyn + ZAP) | 보안 매니저 + AI수석 |

---

## 2. 현재 코드 상태 (5/14 19:30 기준)

**최근 커밋 체인:**
```
29da760  fix(migration): CODE-01 즉시 봉합 — MigrationJobStore Singleton→Scoped
6ce43b4  docs(헌법): #26 마이그·대량처리 1분 절대 원칙 명문화 + WS-11 정공법 작지서 (초안)
00c8deb  fix(migration): WS-06/07/08/09/10 임원 합의 P0 5건 봉합
4a77dd0  fix(ci): P0 #2 SAST/DAST 3종 CI 가동
dc1e5c9  fix(migration): P0 #6 Sticky 헤더 + 13개 테이블별 카드 가시화
c186360  fix(migration): P0 #4 OLEDB SELECT 11개 ORDER BY
75a8202  fix(migration): P0 #5 raw_data AES + migration_errors INSERT
f4c0f5b  fix(migration): P0 #3 stock_ledger 1000행 청크 INSERT IGNORE
dbd948c  fix(migration): P0 #1 거대 단일 tx → 테이블별 분리
bcb0ff6  fix(migration): 옵션 A 봉합 + 5/14 새벽 학습 산출물 28종
```

**5건 봉합 + CODE-01 봉합 = fallback 안전망. 정공법 6축이 본 작업.**

**빌드:** errors 0 + warnings 0 (헌법 #19)
**API/Web:** 백그라운드 종료. 5/15 09:00 재가동 예정.

---

## 3. DB 상태 (5/14 18:00 기준)

- collections: **614,212행** (tenant-mig-test-001, 사장님 새벽 옵션 A 보존)
- stock_ledger: **116,420행**
- 백업: `backups/pre-ws09-10_clean.sql` (29MB)
- DDL 적용 완료: stock_ledger.move_type=varchar(10) BTREE, collections.source_type/source_id + uq_collections_source

---

## 4. 사장님 MDB 파일 (5/15 풀스택 검증 기준)

`C:\Users\소순근\Desktop\공영정보DB`
- PYOJUN.mdb — 14.9MB (마스터)
- PANDATA.mdb — **305MB** (거래)
- POTHER.mdb — **336MB** (명함·AS·배송·달력, 축 5 신규 마이그)

---

## 5. 5/15 타임라인

| 시각 | 작업 | 담당 |
|---|---|---|
| **09:00** | 가동 — PM 킥오프 + 6축 분담 | PM + 임원 12명 |
| 09:00~13:00 | 축 1 (MySqlBulkCopy + LOAD DATA) + 축 3 (마이그 풀 분리) + 축 5 DDL | 백엔드 + DB |
| 13:00~17:00 | 축 2 (source_hash 멱등) + 축 4 (SignalR) + 축 5 풀스택 | 백엔드 + 프론트 |
| 13:00~17:00 | 축 6 (PII AES + CodeQL custom + TruffleHog 룰셋) | 보안 + AI |
| 17:00~19:00 | 빌드 errors 0 + warnings 0 (헌법 #19) | QA |
| 19:00~21:00 | CodeQL + TruffleHog + ZAP DAST 통과 (헌법 #23 5중) | 검증팀장 |
| 21:00~24:00 | PM 단독 dry-run (60만/100만/1000만 3종) | PM |
| **5/16 09:00** | ⭐ **사장님 참관 통합 본런** | 사장님 + 임원 12명 |

---

## 6. 5/16 09:00 통합 본런 검증 게이트

- [ ] 헌법 #26: PYOJUN+PANDATA+POTHER ≤ 60초 (3 데이터 규모 모두)
- [ ] 멱등성: 같은 MDB 2회 마이그 시 모든 테이블 카운트 변화 0
- [ ] SET SESSION 풀 격리: 마이그 후 다른 컨트롤러 connection에서 fk_checks=1
- [ ] 진행률 SignalR: 폴링 0회 + 실시간 push
- [ ] POTHER 4: DOCNM/DOCAS/DELIVERY/CALENDAR 정상 마이그
- [ ] PII AES: error_message·error_detail VARBINARY 복호화 테스트
- [ ] 빌드 errors 0 + warnings 0
- [ ] CodeQL + TruffleHog 통과 + ZAP DAST 통과
- [ ] EVF 6대 영역 통과

---

## 7. 5/16 09:00 사장님 참관 동석

- PM
- 검증팀장 (머지 게이트 권한)
- 설계팀장
- DB 매니저
- CTO
- 백엔드 매니저
- 보안 매니저
- 프론트 매니저
- AI수석
- ERP 매니저
- 본부장
- 법무팀장 (12명)

---

## 8. 5/16 일정 주의

- 09:00 사장님 참관 본런
- 10:00~12:00 본런 결과 정리 → 5/16 보고서 12종 1항목 추가
- **09:00 P0 임원 보고서 12종 마감** (1일 연기분, 추가 연기 절대 불가)

---

## 9. 위험·완화

| 위험 | 완화 |
|---|---|
| 12시간 풀스택 가동 중 매니저 컨플릭트 | 6축 파일·테이블 분리, 머지 충돌 최소화 |
| MySqlBulkCopy + AES VARBINARY 호환성 | LOAD DATA BINARY 직접 처리 또는 FROM_BASE64 SQL |
| POTHER 336MB 첫 마이그 OOM | Stream 처리 (축 1-3)로 메모리 일정 |
| 1분 미달 시 추가 튜닝 | 5/15 21:00 측정 → 5/16 새벽 조정 가능 |
| SignalR Blazor 호환성 (5/13 야간 발견 패턴 있음) | 기존 Blazor SignalR 패턴 활용 |
| CodeQL custom 쿼리 빌드 시간 | 별도 워크플로우, 본 빌드와 분리 |

---

**더 이상 봉합 변형으로 사장님 격언 받아쓰지 않습니다.** 🫡
