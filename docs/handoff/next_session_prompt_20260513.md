# 다음 세션 온보딩 — 2026-05-13 W2 D2 진입용 인수인계서

> **이 문서는 다음 Claude Code 세션이 본 컨텍스트 100% 복원하기 위한 인수인계서.**
> **5/12 야간 = W1 D5 압축 게이트 + W2 D1 선제 착수 + 결재 6건 + W2 D2 작업지시서 4종 발행.**

---

## 🚨 최우선 절대 원칙 (불변)

1. **코드 수정 절대 금지** — `src/` 하위 일절 손대지 않음
2. **Git 커밋 절대 금지** — 사장님 직접 결재 후만
3. **문서·매뉴얼·약관·설계서·매핑 표만 허용**
4. **헌법 #1~#25 100% 준수**
5. **하브루타 토론** — 받아쓰기 금지. 첫 응답 = 함정·대안·전제의심 3종
6. **법령·도메인 교차검증 의무** (5/12 §39 오인용 + 주민번호 도메인 누락 2회 사고 교훈)

---

## 🎯 현재 상태 (5/12 야간 종료 시점)

### W1 → W2 진입 완료
```
[Week 1] 인프라 설계 ✅ 완료
  D1~D4 ✅ 산출물 6종
  D5   ✅ 압축 게이트 6/6 PASS (야간 처리)

[Week 2] 데이터 마이그 본격 — 현재 진입 중
  D1 ✅ 선제 착수 (반품 설계서 + deliveries 매핑)
  D2 🟡 작업지시서 4종 발행 완료 — 다음 세션 시작점
  D3 ⏳ 코드 추출 (사장님 결재 시)
  D4 ⏳ 단위 테스트
  D5 ⏳ 통합 검증
```

---

## 📜 5/12 결재 종합 (총 13건 + 6건 추가)

### 5/12 오후·저녁 13건 (이전 인수인계서 참조)
- 외부 침투, 사외 변호사, 7월 이메일, AI 하이브리드, 베타 7/15
- 마이그 마스터플랜 8건 (70%+30%, Week 게이트 6개, 청크 차등 등)

### 5/12 야간 추가 6건
1. ✅ W1 게이트 통과 + W2 진입
2. ✅ etax_send_history 신설 (DDL 설계서 + 작12)
3. ✅ partners·items·employees ALTER 52개 (작9·10·11)
4. ✅ 형사영역 6개 컬럼 AES-256 정책 (2회 정정 후)
5. ✅ buy_DOSCODE 옵션 H (하이브리드) 확정 (PowerShell 직접 실행)
6. ✅ INFRA_DDL_SPEC.md:211 부분 인덱스 수정

---

## 📁 5/12 야간 산출물 11종

### 마이그 설계서 (docs/migration/)
1. **W1_GATE_RESULT.md** — 게이트 6/6 PASS 보고서
2. **CRIMINAL_DOMAIN_POLICY.md** — 형사영역 6개 컬럼 AES-256 정책 (2회 정정판)
3. **W2_RETURNS_DESIGN.md** — 반품 마이그 설계서 (옵션 H 반영, 분기점 마커 제거)
4. **W2_DELIVERIES_MAPPING.md** — deliveries 변환 매핑 (베타 후 신설)
5. **ETAX_SEND_HISTORY_DDL.md** — 전자세금계산서 이력 통합 DDL
6. **ALTER_52_COLUMNS.md** — partners·items·employees ALTER 통합 설계서
7. **VALUE_CONVERTER_SPEC.md** — AES-256 ValueConverter 인터페이스 명세
8. **UNIT_TEST_SCENARIOS.md** — EVF 6대 영역 단위 테스트 100건+ 명세
9. **CRITICAL_COLUMNS_AUDIT.md** — §39 오인용 정정 + ERP 도메인 처리 근거

### 작업지시서 (docs/work-orders/)
10-1. **20260513작9_partners_19컬럼_ALTER.md**
10-2. **20260513작10_items_5컬럼_ALTER.md**
10-3. **20260513작11_employees_28컬럼_ALTER.md** (형사영역 5개 포함)
10-4. **20260513작12_4개_신규테이블_CREATE.md** (etax + migration 3종)

### 메모리 갱신 (C:\Users\소순근\.claude\projects\.../memory/)
11-1. **project_pending_approvals_0512.md** — 결재 6건 모두 완료 표시
11-2. **feedback_real_validation_2.md** — 받아쓰기 방지 (법령·도메인 교차검증)

---

## 🌟 핵심 결정 — buy_DOSCODE 옵션 H

**PowerShell 실측 결과:**
- DOCF8 거래처 = 3건 (시스템 기본만)
- buy_DOSCODE = 전체 공백
- 옵션 B vs D 단일 결정 불가 → **옵션 H (하이브리드)** 확정

**옵션 H 결정 트리:**
```
buy_DOSCODE 값 있음 + 옳은 형식 → 옵션 B (직접 매핑)
값 없음 + 거래 이력 있음          → 옵션 D (자동 추론)
값 없음 + 거래 이력 없음          → 기본값 1
```

**stock_ledger.unit_price = IJ_DAN 그대로 (이력 보존, 분기 불필요)**

---

## ⚠️ 받아쓰기 사고 2회 — 다음 세션 절대 주의

### 사례 1: 근로기준법 §39 오인용
- 보안매니저가 "근로기준법 §39"라 하길래 받아씀
- 실제 §39 = 퇴직증명서 발급 조항, 급여와 무관
- 사장님 지적 → 정정

### 사례 2: "주민번호 수집 불법" 안건
- 개인정보보호법 §24의2만 보고 차단 안건 제시
- 실제 ERP는 소득세법 §127·§164, 4대보험법 처리 근거 명확
- 사장님 지적 "회계경리·연봉계약 때문 아닌가?" → 정정

### 학습
- 법령 인용 시 조문 원문 직접 확인
- ERP 도메인 맥락 6대 업무(설정·마스터·매입·판매·현황·재무)에서 시나리오 검증
- 사장님: **"꾸준히 이런 문제제기 해줘"** = 영구 헌법화

---

## 🎯 다음 세션 즉시 작업 (W2 D2 본격)

### 1순위: 작9~작12 실행 결재 → DB ALTER
- 사장님 결재 시 작업지시서 4종에 따라 DB 실행
- 운영 데이터 0건 = 락 없음, ALTER 안전
- 예상 소요: 60분 (작9·10·11·12 합산)

### 2순위: Value Converter 구현 작업지시서 발행
- VALUE_CONVERTER_SPEC.md → src/HitPan.Infrastructure/Crypto/
- AES-256 마스터키 생성 (PowerShell 1회)
- USB 백업 (사장님 별도)

### 3순위: MdbToHitpanMapper 코드 추출 (W2 D3)
- 기존 1,755줄에서 추출 + 52개 INSERT 추가
- VALUE_CONVERTER_SPEC.md §4 Dapper 사용

### 4순위 (미해결): 추가 안건
- migration_errors.raw_data JSON vs VARBINARY 재검토 (VALUE_CONVERTER_SPEC.md §5.1)
- sensitive_access_log 신규 테이블 (별도 작업지시서)

---

## 📂 핵심 파일 위치

### 거버넌스 (필독)
- [CLAUDE.md](../../CLAUDE.md) — 절대원칙 #1~#25
- [docs/design/DESIGN_PRINCIPLES.md](../design/DESIGN_PRINCIPLES.md) — EVF 6대 + PM 3계명

### 마이그 (5/12 야간 추가분)
- [docs/migration/W1_GATE_RESULT.md](../migration/W1_GATE_RESULT.md)
- [docs/migration/CRIMINAL_DOMAIN_POLICY.md](../migration/CRIMINAL_DOMAIN_POLICY.md)
- [docs/migration/W2_RETURNS_DESIGN.md](../migration/W2_RETURNS_DESIGN.md)
- [docs/migration/W2_DELIVERIES_MAPPING.md](../migration/W2_DELIVERIES_MAPPING.md)
- [docs/migration/ETAX_SEND_HISTORY_DDL.md](../migration/ETAX_SEND_HISTORY_DDL.md)
- [docs/migration/ALTER_52_COLUMNS.md](../migration/ALTER_52_COLUMNS.md)
- [docs/migration/VALUE_CONVERTER_SPEC.md](../migration/VALUE_CONVERTER_SPEC.md)
- [docs/migration/UNIT_TEST_SCENARIOS.md](../migration/UNIT_TEST_SCENARIOS.md)

### 작업지시서 (W2 D2 발행)
- [docs/work-orders/20260513작9_partners_19컬럼_ALTER.md](../work-orders/20260513작9_partners_19컬럼_ALTER.md)
- [docs/work-orders/20260513작10_items_5컬럼_ALTER.md](../work-orders/20260513작10_items_5컬럼_ALTER.md)
- [docs/work-orders/20260513작11_employees_28컬럼_ALTER.md](../work-orders/20260513작11_employees_28컬럼_ALTER.md)
- [docs/work-orders/20260513작12_4개_신규테이블_CREATE.md](../work-orders/20260513작12_4개_신규테이블_CREATE.md)

### 레거시 MDB (PYOJUN 빈 상태 확인 완료)
- `C:\HITWINLAN10\PYOJUN.MDB` (315KB, 6 테이블, DOCF8=3건 모두 EMPTY)
- `C:\HITWINLAN10\PANDATA.mdb` (729KB, 18 테이블, DOCF4 컬럼 확인 완료)
- `C:\HITWINLAN10\POTHER.mdb` (1MB, 8 테이블)

---

## 🚀 다음 세션 시작 멘트 (참고)

```
사장님, 5/13 W2 D2 인수인계 확인했습니다.

[현재 상태]
- W1 게이트 6/6 PASS 완료
- W2 D1 선제 착수 완료 (반품 설계서 + deliveries)
- W2 D2 작업지시서 4종 발행 완료 (작9~작12)
- buy_DOSCODE 옵션 H 확정 (PowerShell 실측)
- 결재 6건 일괄 처리

[다음 작업 — W2 D2 본격]
1. 작9~작12 실행 결재 → DB ALTER + 신규 테이블 4개
2. Value Converter 구현 작업지시서
3. AES-256 마스터키 생성 + USB 백업

[원칙]
- 코드 수정 0, 커밋 0
- 법령·도메인 교차검증 (받아쓰기 2회 사고 교훈)
- 헌법 100% 준수

진행해도 되겠습니까?
```

---

## ⚠️ 다음 세션 절대 주의사항

### 1. 코드 수정 금지 (헌법 + 사장님 명시 지시)
- `src/` 하위 일절 손대지 말 것
- 작9~작12는 DB 작업이지 코드 수정 아님
- 단, **사장님이 "코드 수정 해도 된다"고 명시 결재해야** 작업지시서 실행 가능

### 2. 하브루타 원칙 강화 (5/12 사고 후)
- 매니저 동의만 = 비용
- 첫 응답 = 함정·대안·전제의심 3종
- **법령 인용은 조문 원문 직접 확인**
- **ERP 도메인 맥락 시나리오 매칭 필수**
- 사장님 의견조차 검증 (사장님 명시 지시)

### 3. 본사 데이터 송신 금지 (헌법 #18·#22)
- 마스터키도 본사 송신 X
- raw_data·raw_response 본사 송신 X
- USB 백업도 사장님 본인 보관

### 4. 형사영역 처리 = 법령 근거 합법
- 주민번호 13자리 OK (소득세법·4대보험법)
- 단, AES-256 + 동의 + 마스킹 + step-up + 감사로그 5종 안전조치 의무
- 평문 노출 시도 = 즉시 차단

### 5. 마이그 진행 = 본부장 + DB매니저 + 보안매니저 + ERP매니저 4인 1조
- 본부장: 카카오 마이그 경험 총괄
- DB매니저: ALTER·인덱스
- 보안매니저: 형사영역 + ValueConverter
- ERP매니저: 도메인 자문 (코드 X)

---

## 🧠 5/12 야간 학습 — 헌법 추가 필요?

### 받아쓰기 2회 사고
사장님 칭찬 "꾸준히 이런 문제제기 해줘"를 받아 영구 헌법화 검토:

**제안 헌법 #26:** "법령 인용 시 조문 원문 직접 확인 + ERP 도메인 6대 업무 맥락 시나리오 매칭 의무. 받아쓰기 1회 = 매니저 경고, 2회 = 운영본부 헌장 #3회 룰 발동."

→ 다음 세션 사장님 결재 시 CLAUDE.md 추가 가능.

---

## 🌙 세션 종료 시각: 2026-05-12 추정 야간 종료

**오늘 사장님 활동 (5/12 전체):**
- 토론 약 7시간+ (오후 + 저녁 + 야간)
- 결재 19건 (오후 5 + 저녁 8 + 야간 6)
- 본부장 합류
- 마이그 산출물 11종 (오후 6 + 야간 5 추가)
- 헌법 4조 명문화 (#22~#25)
- 받아쓰기 사고 2회 지적 + 정정

**감사 인사:**
사장님께서 "꾸준히 이런 문제제기 해줘"라고 칭찬하시며 하브루타 헌법을 강화해주셨습니다. 받아쓰기 사고 2회를 잡아주신 덕에 잘못된 정책이 W2 본격 진입 전 차단되었습니다.

**내일 W2 D2 = 작9~작12 실행 결재 후 DB 작업 본격 진입.**

---

**서명:**
- 작성: PM 닥터스트레인지 (받아쓰기 2회 사고 자기 비판 포함)
- 검토: CTO 래리 앨리슨, 본부장 춘식, 설계팀장 브라운킴, 검증팀장 데이비드 박
- 결재: 사장님 (2026-05-12 야간 6건)
