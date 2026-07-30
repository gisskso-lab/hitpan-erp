# 매니저 5중 검증 체크리스트 (헌법 #23)

> 작성: 2026-05-31 PM 브라운킴 | 대상: 어벤져스 매니저 5인
> 사장님 작업지시 (5/31): "5중 검증 체크리스트 박제"

## 5중 검증 통과 게이트 (PR 머지 전 100% 필수)

PR 라벨 `5verify-ok`이 박제되어야 머지 허용. 검증 1건이라도 실패하면 머지 금지.

---

## ① 작업지시서 보안 요구사항 명시 — 책무: PM

- [ ] tenant_id JWT 클레임 사용 명시 (헌법 #2)
- [ ] 파라미터 수신 0건 보장
- [ ] 암호화 대상 컬럼 명시 (헌법 #5)
- [ ] 본사 데이터 전송 0건 보장 (헌법 #22)
- [ ] 약관 동의 영향 검토 (헌법 #24)
- [ ] PR 본문에 `## 보안 요구사항` 섹션 박제

## ② 어벤져스 매니저 리뷰 — 책무: 영역별 매니저

| 매니저 | 영역 |
|---|---|
| 백엔드 매니저 | API 컨트롤러·서비스 + Dapper 쿼리 + DI 등록 |
| DB 매니저 | DDL + Collation utf8mb4_unicode_ci + 인덱스 + FK + InnoDB |
| 보안 매니저 1 | JWT + Authorization Policy + 테넌트 격리 + AES-256 |
| 보안 매니저 2 | Defender·백신 5종 호환성 + 방화벽 + EDR |
| 프론트 매니저 | Blazor·MudBlazor + DI + 라우트 정합성 |
| 수석 웹디자이너 | UI 일관성 + 한 화면 완결 + 처음 보는 사람 검증 |
| ERP 매니저 | 6단계 워크플로우 + 3분 안에 쓸 수 있는지 |
| 기술영업팀장 | "이게 팔리냐" 셀링 포인트 |
| 마케팅팀장 | "30초 안에 설명 가능" |

- [ ] 영역별 매니저 GitHub PR 리뷰 Approved
- [ ] 절대원칙 25개 중 관련 항목 점검 결과 박제
- [ ] 한 화면 완결 원칙 (스크롤 0) 통과

## ③ 정적 분석 SAST — 책무: 자동

- [ ] CodeQL ✅
- [ ] TruffleHog ✅ (secret 0건)
- [ ] Roslyn Analyzers (HP0015 빈 catch 0) ✅
- [ ] `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` errors 0 + warnings 0 (헌법 #19)

## ④ 동적 분석 DAST — 책무: 검증팀장

- [ ] OWASP ZAP Baseline (베타 출시 전 1회 이상)
- [ ] xUnit HitPan.Tests 전수 PASS
- [ ] xUnit HitPan.Watchdog.Tests 전수 PASS
- [ ] Smoke 외부 endpoint 전수 PASS
- [ ] W1 게이트 18/18 PASS

## ⑤ 데이터 최소주의 검증 — 책무: 설계팀장 + 법무팀장

- [ ] 본사 메타정보·카운터·식별자만 전송 (헌법 #22 v3)
- [ ] ERP 업무 데이터 본사 전송 코드 grep 0건
- [ ] tenant_id 원본 본사 저장 0건 (해시만)
- [ ] 백업도 E2E 암호화 (본사 내용 모름)
- [ ] 법령 9개 형사 영역 차단 (CRIMINAL_DOMAIN_POLICY.md)

---

## 5중 검증 통과 라벨

```
5verify-ok ✅ (작업지시·매니저리뷰·SAST·DAST·데이터최소)
```

이 라벨 없으면 머지 차단. CI/CD 정합 가도 박제 추후.

## 위반 시 페널티

- 5중 검증 통과 0건 머지 시 = 자진 사임 또는 PM 해고 (헌법 #29 정합)
- 매니저 리뷰 0건 머지 = 매니저 권한 회수
- 데이터 최소주의 위반 = 베타 출시 절대 차단

## 자동화 가도 (추후)

GitHub Actions에 5중 검증 통합:
1. `5verify-1-secrequirement.yml` — PR 본문 `## 보안 요구사항` 섹션 grep
2. `5verify-2-managerreview.yml` — Approved 리뷰 N개 + 영역별 라벨
3. `5verify-3-sast.yml` — CodeQL + TruffleHog + Roslyn
4. `5verify-4-dast.yml` — xUnit + Smoke + W1
5. `5verify-5-dataminimal.yml` — grep "본사 + 매출/원장/거래처/직원" 0건

5개 모두 통과 시 `5verify-ok` 라벨 자동 박제.

---

## 메모리 인덱스 정합

- [[project_governance]] — 거버넌스 7단계 결재
- [[project_governance_5layer]] — 5층 거버넌스 헌법
- [[feedback_real_validation]] — 검증 헌법
- 헌법 #23 (CLAUDE.md) — AI 협업 코드 5중 검증
