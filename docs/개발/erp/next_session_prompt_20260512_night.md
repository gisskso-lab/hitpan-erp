# 다음 세션 온보딩 프롬프트 — 2026-05-12 밤 인수인계

> **이 문서는 다음 Claude Code 세션이 본 컨텍스트 100% 복원하기 위한 인수인계서.**
> **5/12 사장님 결재 13건 + 마이그 마스터플랜 + 헌법 #20~#25 명문화 + 본부장 합류 종합.**

---

## 🚨 최우선 절대 원칙

1. **코드 수정 절대 금지** — `src/` 하위 일절 손대지 않음
2. **Git 커밋 절대 금지** — 사장님 직접 결재 후만
3. **문서·매뉴얼·약관·설계서·매핑 표만 허용**
4. **헌법 #1~#25 100% 준수**
5. **하브루타 토론** — 받아쓰기 안 함. 첫 응답 = 함정·대안·전제의심 3종

---

## 🎯 최신 상태 (5/12 22:00 기준)

### 현재 진행: 마이그레이션 마스터플랜 W1 D4 완료

```
[Week 1] 인프라 설계
  D1 ✅ 빈 MDB 실측 (32개 테이블 발견)
  D2 ✅ 컬럼·PK 전수 조사
  D3 ✅ 매핑 표 + DDL + API 스펙
  D4 ✅ 5개 클래스 분리 + 핵심 컬럼 감사 ← 오늘
  D5 🟡 게이트 점검 + 헌법 검증 ← 다음 세션 시작점

[Week 2~6] 5/20 ~ 6/23
  P0 4건 → 청크·멱등 → 양식·신규 메뉴 → 검증 → 외부 침투

[7/15] ⭐ 베타 출시 절대 게이트
```

### 진행률: 80% (W1 계획 대비 빠름)

---

## 📜 5/12 사장님 결재 — 13건 종합

### 첫 일괄 결재 5건 (오후)
1. **외부 침투 테스트:** 그레이해커 + 라온시큐어 (500만원)
2. **사외 변호사:** 법무법인 바른 + 디라이트 (400만원)
3. **본사 운영 이메일:** 7월 초 `ops@hitpan.kr` 분리
4. **AI 챗봇:** 하이브리드 (Haiku 4.5 + Sonnet 4.6 자동 승급)
5. **베타 출시:** 2026-07-15 월요일

### 마이그 마스터플랜 결재 8건 (저녁, 하브루타 후)
1. 기존 70% 유지 + 30% 신규 (새로 짜기 아님)
2. 테이블별 청크 차등 + 동적 조정
3. 단가 매핑 옵션 B(컬럼 있으면) vs D(자동 추론) — W2 D1 확정
4. 6~8주 + Week 게이트 6개
5. 빈 MDB 즉시 실측 (W1 D1)
6. 양식 30종 + 이미지 25개 마이그 (W4)
7. 외부 그레이해커 침투 6/17~19
8. 헌법 #20·#22·#23 재확인

---

## 📜 헌법 신규 명문화 — #22·#23·#24·#25

[CLAUDE.md:222-225](../../CLAUDE.md) 추가 완료:

- **#22 본사 데이터 최소주의** — "본사가 안 가지면 본사가 털릴 일 없다"
- **#23 AI 협업 코드 5중 검증 (바이브코딩 헌법)** — 외부 마케팅에 "AI" 단어 금지
- **#24 책임 분산 + 가르침 의무** — 본사·고객·제3자 3축 책임 분리
- **#25 3대 개발 원칙** — 쉽게·정확하게·안전하게

---

## 👥 16명 팀 구성 (본부장 합류 후)

### 3대 임원
1. CTO 래리 앨리슨 (Oracle 30년)
2. PM 닥터스트레인지 (MIT, Google 30년)
3. 설계팀장 브라운킴 (SAP SE 30년)

### 신규 합류
4. **본부장 춘식** (MIT, 네이버 20년 + 카카오 PM) ⭐ 오늘 합류

### 어벤져스 매니저 8명
5. AI수석 / 6. 백엔드 / 7. DB / 8. 보안 / 9. 프론트 / 10. 웹디자이너 / 11. ERP / 12. 기술영업

### 추가 팀장
13. 마케팅팀장 / 14. 법무팀장

### 에이전트급
15. 검증팀장 데이비드 박 / 16. 감사팀장 / 17. 인프라매니저 마커스 리

---

## 📁 5/12 작성 산출물 6종

```
docs/migration/
  ├ MIGRATION_MASTER_PLAN.md       하브루타판 마스터플랜 (단일 진실 원천)
  ├ MDB_FULL_MAPPING.md             32개 테이블 매핑 표
  ├ INFRA_DDL_SPEC.md               3개 인프라 테이블 DDL
  ├ INFRA_API_SPEC.md               4개 API 스펙
  ├ CLASS_SEPARATION_SPEC.md        5개 클래스 분리 설계
  └ CRITICAL_COLUMNS_AUDIT.md       DOCF4·DOCFS·DOCSW 정독

docs/manual/
  └ HITPAN_USER_MANUAL.md           매뉴얼 1차 초안 (ERP매니저)
```

---

## 🔬 W1 D1~D4 핵심 발견 7건

### 1. 32개 테이블 발견 (코드 23개 + 신규 9개)
```
PYOJUN.MDB     6개 (COSTNO 33건, DOCF8 3건, DOCFS 0, DOCRT 0, DOCSW 0, SETUP 172건)
PANDATA.mdb   18개 (모두 0건 — 빈 테스트 MDB)
POTHER.mdb     8개 (CALENDAR 7,305건, DOCSC 274건, 나머지 0)
```

### 2. ⭐ 단가등급 후보 컬럼 `buy_DOSCODE` (Text5)
- 옵션 B 가능성 90%+
- W2 D1 사장님 실 데이터로 확정

### 3. 🌟 DOCF4 전자세금계산서 발행 이력 4개 컬럼
- TX_READDT, TX_REPORTDT, TX_SENDDT, TX_PDT
- `etax_send_history` 신설 필요

### 4. 🚨 헌법 #18 형사 영역 6개 컬럼
- SW_JUMIN, SW_PAY×4, buy_topjumin
- AES-256 Value Converter + 별도 동의 필수

### 5. ✅ 모든 32개 PK 정의됨
- 체크포인트 100% 작동 가능
- 복합 PK는 JSON 직렬화 (DOCFB 5컬럼 등)

### 6. 보강 ALTER 컬럼 52개
- partners 19개 / items 5개 / employees 28개

### 7. POTHER 4개 베타 후 (사장님 결정)
- CALENDAR: 즉시 마이그 (Phase 1)
- DOCNM(명함) / DELIVERY(배송) / DOCAS(AS) / DOCME(메모): 베타 후 신설

---

## 🗺️ 마이그 6~8주 일정 (확정)

```
[W1 5/13~19] 인프라 설계 (현재 진행)
[W2 5/20~26] P0 4건: 특별단가·반품·견적·deliveries 변환
[W3 5/27~6/2] 청크·멱등 적용 + 누락 컬럼 보강
[W4 6/3~9] 양식 30종 + 이미지 25개 + 신규 메뉴 4개
[W5 6/10~16] 검증 + EVF ⑥ 3년치 시뮬
[W6 6/17~23] 그레이해커 침투 + CTO 종합 판정

[6/30] 마이그 완성
[7/1~14] 전자세금계산서 국세청 연동 (병렬)
[7/15] ⭐ 베타 출시
```

### Week 게이트 6개 객관 기준
2개 fail = 7주 / 3개 fail = 8주 자동 발동

---

## 🎯 다음 세션 즉시 작업 (W1 D5)

### 1순위: Week 1 게이트 점검
- [ ] 3개 인프라 테이블 DDL 설계 완료 검증
- [ ] 4개 API 스펙 완료 검증
- [ ] 5개 클래스 분리 설계 완료 검증
- [ ] 32개 테이블 매핑 표 완료 검증
- [ ] 핵심 컬럼 감사 완료 검증
- [ ] 헌법 #1~#25 위반 0건 검증

### 2순위: 주간 종합 보고서 작성
- `docs/migration/W1_WEEKLY_REPORT.md`
- W1 성과 + W2 계획 + 사장님 결재 사항

### 3순위 (사장님 결재 시): W2 진입 준비
- W2 D1: 사장님 실 데이터로 `buy_DOSCODE` 분포 확인 → 단가 옵션 확정

---

## 📂 핵심 파일 위치

### 거버넌스 (필독)
- [CLAUDE.md](../../CLAUDE.md) — 절대원칙 #1~#25 (#22~#25 오늘 추가)
- [docs/헌법/DESIGN_PRINCIPLES.md](../design/DESIGN_PRINCIPLES.md) — EVF 6대 + PM 3계명 + 4프로토콜
- [docs/governance/CONSTITUTION_20260430.md](../governance/CONSTITUTION_20260430.md) — 헌법 #18 v3

### 마이그 (W1 산출물)
- [docs/migration/MIGRATION_MASTER_PLAN.md](../migration/MIGRATION_MASTER_PLAN.md) ⭐ 단일 진실 원천
- [docs/migration/MDB_FULL_MAPPING.md](../migration/MDB_FULL_MAPPING.md)
- [docs/migration/INFRA_DDL_SPEC.md](../migration/INFRA_DDL_SPEC.md)
- [docs/migration/INFRA_API_SPEC.md](../migration/INFRA_API_SPEC.md)
- [docs/migration/CLASS_SEPARATION_SPEC.md](../migration/CLASS_SEPARATION_SPEC.md)
- [docs/migration/CRITICAL_COLUMNS_AUDIT.md](../migration/CRITICAL_COLUMNS_AUDIT.md)

### 매뉴얼
- [docs/manual/HITPAN_USER_MANUAL.md](../manual/HITPAN_USER_MANUAL.md) — 1차 초안

### 레거시 MDB
- `C:\HITWINLAN10\PYOJUN.MDB` (308KB, 6 테이블)
- `C:\HITWINLAN10\PANDATA.mdb` (712KB, 18 테이블)
- `C:\HITWINLAN10\POTHER.mdb` (1,032KB, 8 테이블)
- `C:\HITWINLAN10\POST.mdb` (8.7MB, 우편번호 — 마이그 제외)

### 마이그 소스 코드 (현 1,755줄)
- [src/HitPan.Application/Services/MdbMigrationService.cs](../../src/HitPan.Application/Services/MdbMigrationService.cs)
- [src/HitPan.API/Controllers/MigrationController.cs](../../src/HitPan.API/Controllers/MigrationController.cs)
- [src/HitPan.Web/Pages/Settings/MdbMigration.razor](../../src/HitPan.Web/Pages/Settings/MdbMigration.razor)

---

## 🧠 5/12 토론 핵심 결론 (받아쓰기 금지 사례)

### 사례 1: "엎는 수준인가?" 토론
- 사장님 질문: "기존 다 엎고 새로 짜야 되나?"
- PM 답변: "**70% 유지 + 30% 신규** = 새로 짜는 게 아님"
- 근거: 헌법 #1, 카카오 경험, 1,755줄 노하우 보존

### 사례 2: "전자세금계산서 먼저?" 영업 vs 개발 토론
- 영업 관점: 세금계산서 = 베타 게이트 필수
- 개발 관점: 의존성·블록·EVF 검증 = 마이그 먼저
- **결론: 마이그 먼저** (개발 관점 6:0 우세)

### 사례 3: 하브루타 6인 60분 토론
- 1차 회의록 안전답 폐기 3건
- 신규 함정 발견 4건 (ORDER BY 누락, last_pk_value, 옵션 D 자동 추론, Week 게이트)
- "보안매니저 평가: 2개월 사고 예방"

### 사례 4: ERP매니저 코드 짓기 사고
- 사장님이 "학습하라" 했는데 코드만 짬
- 호된 질책 → 매뉴얼 작성 + 본부장 풀스택 지원
- 헌법: ERP매니저 = 매뉴얼 + 시나리오 / 코드 작성 X

---

## 🚀 다음 세션 시작 멘트 (참고)

```
사장님, 5/12 인수인계 확인했습니다.

[현재 상태]
- W1 D4 완료 (마이그 마스터플랜 6종 산출)
- 헌법 #22~#25 명문화 완료
- 본부장 춘식 합류
- 결재 13건 처리

[다음 작업 — W1 D5]
- Week 1 게이트 점검 (6개 기준)
- 헌법 #1~#25 위반 0건 검증
- 주간 종합 보고서 작성

[원칙]
- 코드 수정 0, 커밋 0
- 하브루타 토론, 받아쓰기 금지
- 헌법 100% 준수

진행해도 되겠습니까?
```

---

## 📋 미해결 결재 사항 (다음 세션 우선 처리)

1. **W1 D5 게이트 점검 결재** → 통과 시 W2 진입
2. **etax_send_history 신설 결재**
3. **partners·items·employees ALTER 52개 컬럼 결재** (W2 적용 예정)
4. **헌법 #18 형사 영역 6개 컬럼 AES-256 정책 결재**
5. **사장님 실 데이터로 buy_DOSCODE 확인 일정**

---

## ⚠️ 다음 세션 절대 주의사항

### 1. 코드 수정 금지 (헌법)
- 사장님 명령 "코드 수정 절대 금지" 유지
- `src/` 하위 일절 손대지 말 것
- CLAUDE.md 등 거버넌스 문서는 사장님 결재 사항 반영만 허용

### 2. 하브루타 원칙
- 매니저 동의만 = 비용 (헌법: 받아쓰기 운영진은 비용)
- 첫 응답 = 함정·대안·전제의심 3종 후 동의·반박
- 사장님 의견조차 검증 의무

### 3. 본사 데이터 송신 금지 (헌법 #18·#22)
- 고객사 업무 데이터 본사 X
- 메타정보·카운터·식별자만 OK
- raw_data AES-256 필수

### 4. ERP매니저 코드 짓기 금지
- 매뉴얼·시나리오·도메인 자문만
- 코드 작성 시도 시 즉시 중단

### 5. 마이그 진행 = 본부장 + DB매니저 + ERP매니저 3인 1조
- 본부장: 카카오 마이그 경험 총괄
- DB매니저: 스키마·인덱스
- ERP매니저: 도메인 자문 (코드 X)

---

## 🎓 학습 미완 사항 (사장님 명령: 퇴근 후)

- 16명 + 본부장 학습 진행 중 (1시간씩)
- 5/13 9시 마감: 12명 박사논문급 보고서
- 5/14 9시 마감: 매뉴얼·기능정의서·AI CS·약관 4종
- 5/16 9시 마감: 코드분석·약관 등 깊이 보고서
- 5/19 9시 마감: 12개 종합 보고서 + CTO 종합

---

## 🌙 세션 종료 시각: 2026-05-12 22:30 추정

**오늘 사장님 활동:**
- 토론 약 7시간
- 결재 13건
- 본부장 합류
- 마이그 6종 산출물
- 헌법 4조 명문화
- 식사 1회

**감사 인사:**
사장님 식사 시간 동안 6인 하브루타로 1차 안전답 폐기 + 진짜 답 도출.
보안매니저 평가: "2개월 사고 예방."

**내일 W1 D5 = Week 1 게이트 통과 → W2 진입 준비.**

---

**서명:**
- 작성: PM 닥터스트레인지
- 검토: CTO 래리 앨리슨, 설계팀장 브라운킴, 본부장 춘식
- 결재: 사장님 (2026-05-12)
