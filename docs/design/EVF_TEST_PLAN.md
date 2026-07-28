# EVF (Extreme Verification Framework) 테스트 계획서
> 사장님 품질 헌법: "코드를 짜고 고객한테 첫 등장하기 전까진, 우리는 우리가 만든 프로그램을 가장 극한의 환경에서 검증해야 한다."
>
> 작성일: 2026-05-09 | 검증 기간: 2026-05-12~16 | MVP 론칭: 2026-05-23

---

## 개요

### EVF 베타 출시 절대 게이트
6개 영역 모두 PASS 안 나오면 베타 출시 보류. 사장님 절대 명령.

### 책임자
| 역할 | 담당 |
|---|---|
| 종합 판정 | CTO 래리 앨리슨 |
| 부하·장애 | 브라운킴 |
| 악의·혼돈·노후 | 데이비드 박 |
| 무지 | ERP 매니저 |

### 테스트 환경
- **대상 서버**: demo.hitpan.kr (API: api-demo.hitpan.kr)
- **DB**: MariaDB 11.4.10 (InnoDB)
- **테스트 계정**: admin@hitpan.kr / Admin1234!
- **도구**: autocannon, Playwright, k6, OWASP ZAP, 수동 시나리오
- **기존 스크립트**: `tools/smoke-test/evf-load.mjs`, `tools/smoke-test/evf-no-load.mjs`

---

## 영역 1: 부하 (Load)

### 목표
동시 100세션, 3년치 누적 데이터 환경에서 응답 p99 < 2초

### 합격 기준
| 항목 | 기준 |
|---|---|
| 동시 접속 | 100세션 유지 10초, 에러율 0% |
| 응답 p99 | < 2,000ms |
| 재고 조회 | 100만 건 데이터 기준 < 2초 |
| 대시보드 KPI | 집계 쿼리 < 3초 |
| 매입/판매 확정 | 트랜잭션 유지, 에러 없음 |

### 시나리오
```
S1. 동시 100세션 — GET /api/stock (10초)
S2. 동시 100세션 — GET /api/partners (10초)
S3. 동시 50세션 — POST /api/purchases/{id}/confirm (멱등 포함)
S4. 대용량 조회 — 수불부 3년치 (item_stock_ledger 100만 행)
S5. 대시보드 — monthly_summary 집계 동시 30세션
```

### 실행 방법
```bash
cd tools/smoke-test
TOKEN=$(node get-token.mjs) node evf-load.mjs
```

### 합격/불합격 판정
- p99 < 2000ms + errors = 0 → **PASS**
- p99 ≥ 2000ms 또는 errors > 0 → **FAIL** → 인덱스 추가 후 재실행

---

## 영역 2: 장애 (Failure)

### 목표
DB 재시작, 네트워크 단절, 디스크 부족 상황에서 데이터 유실 없이 복구

### 합격 기준
| 항목 | 기준 |
|---|---|
| DB 재시작 | API 자동 재연결, 503 후 정상 복구 |
| 트랜잭션 중 장애 | 롤백 완전 처리, 부분 커밋 없음 |
| 타임아웃 | 30초 초과 시 명확한 오류 반환 |
| 재시작 후 무결성 | 원장(stock_ledger, journal_lines) 데이터 유실 없음 |

### 시나리오
```
F1. DB 프로세스 강제 종료 → API 재연결 확인
F2. 매입 확정 트랜잭션 중 DB 연결 끊김 → 롤백 확인
F3. API 서버 재시작 → 진행 중이던 세션 처리 확인
F4. 응답 타임아웃 (30초 초과) → 명확한 오류 메시지 확인
F5. 재시작 후 stock_ledger / journal_lines 건수 일치 확인
```

### 실행 방법
```bash
# F1: DB 프로세스 강제 종료 (서버 측 수동)
# MariaDB 서비스 stop → 30초 대기 → start
# API 상태 폴링: GET /health

# F2~F5: 수동 시나리오 체크리스트
```

### 합격/불합격 판정
- 모든 시나리오에서 부분 커밋 0건 + 복구 확인 → **PASS**
- 부분 커밋 1건 이상 → **FAIL** → P0 즉시 처리

---

## 영역 3: 악의 (Malicious)

### 목표
OWASP Top 10 + 멀티테넌트 침투 시도 전부 차단

### 합격 기준
| 항목 | 기준 |
|---|---|
| SQL Injection | 500 응답 없음, 데이터 노출 없음 |
| JWT 위조 | 401 반환 |
| tenant 월경 | 타 테넌트 데이터 0건 반환 |
| XSS | 스크립트 미실행 |
| 무인증 접근 | 401 반환 |
| Rate Limit | 과도한 요청 429 반환 |

### 시나리오
```
M1. SQL Injection — GET /api/partners?search=' OR '1'='1
M2. JWT 위조 — 임의 서명 토큰으로 /api/stock 접근
M3. 무인증 접근 — Authorization 헤더 없이 모든 API 시도
M4. tenant 월경 — URL에 타 tenant_id 삽입 시도
M5. XSS — 업체명/상품명에 <script>alert(1)</script> 입력
M6. Rate Limit — 1분에 200회+ 연속 요청
M7. 관리자 권한 탈취 — 일반 직원 JWT로 /api/users/permissions 접근
```

### 실행 방법
```bash
cd tools/smoke-test
TOKEN=$(node get-token.mjs) node evf-no-load.mjs
```

### 합격/불합격 판정
- M1~M7 전부 차단 확인 → **PASS**
- 1건이라도 데이터 노출 또는 500 응답 → **FAIL** → 즉시 P0 보안 패치

---

## 영역 4: 혼돈 (Chaos)

### 목표
동시 확정, 중복 저장, 잘못된 순서 시도 시 멱등성 보장 + 명확한 오류 반환

### 합격 기준
| 항목 | 기준 |
|---|---|
| 중복 확정 | 동일 ID 100회 POST → 1건만 처리 |
| 동시 매입 확정 | 동시 50req → stock_ledger 중복 없음 |
| 순서 위반 | draft → confirmed 건너뛰기 → 명확한 오류 |
| 음수 재고 | 재고 0 상태에서 판매 확정 → 차단 |
| 단가 0 | 단가 0원 확정 시도 → validation 차단 |

### 시나리오
```
C1. 매입 확정 동시 100회 POST → stock_ledger 1건 확인 (Idempotency-Key)
C2. 동시 BOM 생산 50req → 자재 음수 방지 확인
C3. 거래명세서 취소 후 세금계산서 발행 시도 → 차단 확인
C4. 재고 0 상태 판매 확정 → 400 오류 + 재고부족 메시지 확인
C5. 동일 발주 ID 중복 전환 → 2번째 요청 409/400 반환
```

### 실행 방법
```bash
cd tools/smoke-test
TOKEN=$(node get-token.mjs) node evf-no-load.mjs
# C1~C5는 evf-no-load.mjs 영역4 섹션 실행
```

### 합격/불합격 판정
- 중복 건수 0 + 음수 재고 0 + 모든 오류 명확 반환 → **PASS**
- 중복 원장 1건 이상 → **FAIL** → P0 즉시 처리

---

## 영역 5: 무지 (Usability)

### 목표
신규 직원(설명 없음)이 30분 안에 핵심 업무 완료

### 합격 기준
| 항목 | 기준 |
|---|---|
| 매입 흐름 | 발주 → 매입 확정까지 30분 이내, 무설명 |
| 판매 흐름 | 견적 → 거래명세서까지 30분 이내, 무설명 |
| 오류 메시지 | 오류 발생 시 다음 행동 유도 메시지 있음 |
| 필수 필드 | 빈 칸 제출 시 어떤 필드인지 명확 표시 |
| 챗봇 도움 | 막히면 챗봇에게 물어봐서 해결 가능 |

### 시나리오
```
U1. 신규 직원 5명 — 업체 등록 → 발주 → 매입 확정 (무설명)
U2. 신규 직원 5명 — 상품 등록 → 견적 → 수주 → 거래명세서 (무설명)
U3. 오류 상황 강제 발생 → 오류 메시지만 보고 해결 가능 여부 확인
U4. 챗봇 "재고 조회 방법" 질문 → 3번 이내 원하는 화면 도달
U5. 연차 신청 → 결재 대기 확인 (무설명)
```

### 실행 방법
- 베타 출시 1주 전 내부 직원 5명 직접 사용 세션 (ERP 매니저 감독)
- 막힌 지점 기록 → UI 개선 사항 도출

### 합격/불합격 판정
- 5명 중 4명 이상 30분 내 완료 → **PASS**
- 5명 중 2명 이상 실패 → **FAIL** → UX 개선 후 재실행

---

## 영역 6: 노후 (Aging)

### 목표
3년치 데이터 누적 환경에서 속도 저하 없음 + 백업/복원 정상

### 합격 기준
| 항목 | 기준 |
|---|---|
| 3년치 조회 | 수불부/손익현황 p99 < 2초 |
| 인덱스 효율 | EXPLAIN 결과 Full Scan 없음 |
| 백업 | mysqldump 성공 + 파일 무결성 확인 |
| 복원 | 복원 후 데이터 건수 일치 |
| 월마감 성능 | 3년 누적 기준 월마감 재집계 < 10초 |

### 시나리오
```
A1. 3년치 샘플 데이터 INSERT (tools/generate-sample.js 활용)
A2. GET /api/stock/ledger?year=2023 → 응답 시간 측정
A3. GET /api/accounting/profit?from=2023-01 → 응답 시간 측정
A4. EXPLAIN SELECT on stock_ledger → type != 'ALL' 확인
A5. mysqldump → 체크섬 확인 → 복원 → 건수 비교
A6. POST /api/accounting/monthly-closing/{year}/{month} 재집계 시간 측정
```

### 실행 방법
```bash
# A1: 대용량 데이터 생성
node tools/generate-sample.js --years=3 --rows=100000

# A2~A3: 응답시간 측정
TOKEN=$(node get-token.mjs) node evf-load.mjs --aging

# A4: 쿼리 플랜
mysql -u hitpan -p hitpan_erp -e "EXPLAIN SELECT ..."

# A5: 백업/복원
mysqldump -u hitpan -p hitpan_erp > backup_evf.sql
mysql -u hitpan -p hitpan_evf_restore < backup_evf.sql
```

### 합격/불합격 판정
- 전 항목 기준 충족 + Full Scan 0 → **PASS**
- 응답 2초 초과 또는 Full Scan 존재 → **FAIL** → 인덱스 추가 후 재실행

---

## 전체 일정

| 날짜 | 영역 | 담당 | 비고 |
|---|---|---|---|
| 5/12 (월) | 영역1 부하 + 영역6 노후 | 브라운킴 + 데이비드 박 | 대용량 데이터 준비 필요 |
| 5/13 (화) | 영역2 장애 | 브라운킴 + 데이비드 박 | 서버 재시작 권한 필요 |
| 5/14 (수) | 영역3 악의 + 영역4 혼돈 | 데이비드 박 | ZAP 설치 필요 |
| 5/15 (목) | 영역5 무지 | ERP 매니저 | 내부 직원 5명 섭외 필요 |
| 5/16 (금) | 전체 재실행 + CTO 종합 판정 | 래리 앨리슨 | FAIL 항목 핫픽스 후 재실행 |

---

## EVF 결과 기록 양식

```
## EVF 최종 판정 — 2026-05-16

| 영역 | 판정 | 핵심 지표 | 담당자 |
|---|---|---|---|
| ① 부하 | PASS/FAIL | p99=?ms, errors=? | 브라운킴 |
| ② 장애 | PASS/FAIL | 부분커밋=?, 복구=? | 브라운킴 |
| ③ 악의 | PASS/FAIL | 차단건수=?/7 | 데이비드 박 |
| ④ 혼돈 | PASS/FAIL | 중복원장=?, 음수재고=? | 데이비드 박 |
| ⑤ 무지 | PASS/FAIL | 완료=?/5명 | ERP 매니저 |
| ⑥ 노후 | PASS/FAIL | p99=?ms, FullScan=? | 데이비드 박 |
| **종합** | **PASS/FAIL** | | **래리 앨리슨** |

베타 출시 승인: ☐ 승인 / ☐ 보류
```

---

## 사전 준비 체크리스트 (5/12 이전 완료)

- [ ] 3년치 샘플 데이터 생성 스크립트 준비 (`generate-sample.js`)
- [ ] OWASP ZAP 설치 및 설정
- [ ] 내부 직원 5명 5/15 일정 확보 (ERP 매니저)
- [ ] 데모 서버 DB 재시작 권한 확인 (브라운킴)
- [ ] k6 또는 autocannon 최신 버전 설치 확인
- [ ] EVF 결과 기록 시트 공유 (노션 또는 Google Sheets)
