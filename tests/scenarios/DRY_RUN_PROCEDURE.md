# 검증팀 자동 9건 dry-run 절차서

> 박제: 2026-05-29 | 실행 예정: 2026-05-31 (토) ~ 2026-06-02 (월)
> 담당: 검증팀장 + 검증 시니어 1명 보조
> 대상: S06·S07·S10·S14·S15·S16·S18·S19·S20 (자동 9건)
> 게이트: 9/9 PASS = 6/3 W1 통과 정합

---

## 1. 사전 준비 (5/31 09:00)

### 1.1. 환경 확인
- [ ] Windows 11 Pro (사장님 PC 또는 동등 PC)
- [ ] PowerShell 5.1 (`$PSVersionTable.PSVersion`)
- [ ] 인터넷 연결 (api-demo.hitpan.kr 외부 접근)
- [ ] 관리자 권한 (sc·net 명령 가능)
- [ ] cloudflared 설치 (5/27 새벽 영구 봉합 후)

### 1.2. 빌드 + 배포
```powershell
# 1. develop 최신 pull
git pull origin develop

# 2. Release 빌드
dotnet build src/HitPan.sln -c Release

# 3. xUnit 사전 통과 확인
dotnet test src/HitPan.Watchdog.Tests/ -c Release
# → 34/34 PASS 필수

# 4. demo 외부 smoke 사전 통과
& "tests\scenarios\Smoke-ExternalEndpoints.ps1"
# → 8/8 PASS 필수
```

### 1.3. 사장님 결재 사전 확인
- [ ] **헌법 #29 정합 — 인프라 조작 사전 승인** = 검증팀장이 직접 실행하는 시나리오 9건 중 `sc`·`net stop`·`Stop-Service` 호출 = **사장님 1회 결재 의뢰**

---

## 2. 자동 9건 dry-run 실행 절차

### 2.1. 일괄 실행 (권장)
```powershell
& "tests\scenarios\Run-All-Scenarios.ps1" -ApiUrl "https://api-demo.hitpan.kr"
# → 9 시나리오 순차 실행 + JSON 결과 박제
```

### 2.2. 개별 실행 (디버깅용)
```powershell
& "tests\scenarios\S06.ps1" -HealthUrl "https://api-demo.hitpan.kr/health"
& "tests\scenarios\S07.ps1" -HealthUrl "https://api-demo.hitpan.kr/health"
& "tests\scenarios\S10.ps1" -HealthUrl "https://api-demo.hitpan.kr/health"
& "tests\scenarios\S14.ps1" -HealthUrl "https://api-demo.hitpan.kr/health"
& "tests\scenarios\S15.ps1" -HealthUrl "https://api-demo.hitpan.kr/health"
& "tests\scenarios\S16.ps1" -HealthUrl "https://api-demo.hitpan.kr/health"
& "tests\scenarios\S18.ps1" -HealthUrl "https://api-demo.hitpan.kr/health"
& "tests\scenarios\S19.ps1" -HealthUrl "https://api-demo.hitpan.kr/health"
& "tests\scenarios\S20.ps1" -HealthUrl "https://api-demo.hitpan.kr/health"
```

### 2.3. 시나리오별 PASS 조건

| # | 시나리오 | 시뮬레이션 | PASS 조건 | MTTR 목표 |
|---|---|---|---|---|
| S06 | Defender 격리 | 백신 격리 트리거 → 자동 봉합 | /health 200 회복 | 5분 |
| S07 | 방화벽 차단 | UDP 7844 차단 → 자동 재허용 | /health 200 회복 | 3분 |
| S10 | EDR 차단 | EDR 패턴 매칭 → 사전 화이트리스트 | 격리 0 | 즉시 |
| S14 | 네트워크 단절 | 인터넷 단절 → 재연결 시 자동 복구 | /health 200 회복 | 5분 |
| S15 | 물리 사고 | 디스크 IO 실패 시뮬 → 알림 | emergency 박제 | 5분 |
| S16 | 인적 사고 | 권한 없는 사용자 접근 → 차단 | 401/403 응답 | 즉시 |
| S18 | TunnelSecret 무효화 | secret 변조 → WS-28-C 자동 재발급 | /health 200 회복 | 3분 |
| S19 | 4 프로세스 종료 | 강제 종료 → WS-28-I 자동 재기동 | 4/4 Running | 1분 |
| S20 | Bearer lockout | 잘못된 토큰 10회 → 차단 | bypass 200 = 0건 | 즉시 |

---

## 3. 결과 박제 양식

### 3.1. 시나리오별 JSON 결과
```json
{
  "scenario": "S18",
  "executed_at": "2026-05-31T10:00:00+09:00",
  "executor": "검증팀장",
  "status": "PASS",
  "mttr_seconds": 142,
  "expected_mttr_seconds": 180,
  "evidence": {
    "before_status": "down",
    "after_status": "healthy",
    "recovery_logs": ["...", "..."]
  },
  "notes": ""
}
```

### 3.2. 종합 보고서 양식
```markdown
## 자동 9건 dry-run 결과 (2026-05-31~06-02)

### 종합
- 9/9 PASS / N FAIL
- 평균 MTTR: NN초
- 헌법 #27 정합: ✅/⚠️/❌

### 시나리오별
| # | 시나리오 | 결과 | MTTR | 비고 |
|---|---|---|---|---|
| S06 | Defender 격리 | ✅ PASS | 142초 | - |
| S07 | 방화벽 차단 | ✅ PASS | 90초 | - |
| ... | ... | ... | ... | ... |

### FAIL 시
- 진범 박제 (5whys)
- 봉합 작지서 발행 (PM 의뢰)
- 재실행 일자 박제

### 6/3 W1 게이트 GO 권고
- [ ] GO (9/9 PASS)
- [ ] CONDITIONAL GO (8/9 PASS + 경증 FAIL)
- [ ] NO-GO (2건 이상 FAIL 또는 1건 중증 FAIL)
```

---

## 4. FAIL 발생 시 대응 절차

### 4.1. 즉시 진단 (FAIL 발생 30분 안)
1. 진범 5whys 박제
2. 로그 박제 (`logs/watchdog/`·`logs/api/`)
3. PM 즉시 보고

### 4.2. 봉합 가도 (FAIL 발생 24시간 안)
- 경증 (스크립트 버그): 검증팀 단독 봉합 + 재실행
- 중증 (워치독 로직 결함): PM + 백엔드 매니저 작지서 발행 + 봉합 + 재실행
- 치명 (헌법 #27 위반): 사장님 긴급 보고 + W1 게이트 연기 결재

### 4.3. 재실행 + PASS 박제
- 재실행 시점 박제
- PASS 시 W1 게이트 정합 복귀

---

## 5. 헌법 정합 확인

| 헌법 | 정합 |
|---|---|
| #19 errors 0 + warnings 0 | dry-run 전 빌드 통과 박제 |
| #22 본사 데이터 0 | 시나리오 9건 모두 본사로 업무 데이터 전송 0건 박제 |
| #27 통신 무결성 절대 | 5분 이내 자동 복구 = PASS 조건 |
| #28 자동 봉합 9단계 | S18·S19 직접 검증 |
| #29 인프라 조작 사전 승인 | sc·net 호출 시 사장님 1회 결재 |
| #31 백신 5종 호환 | S06 + 본사 5대 PC 25 조합 정합 |

---

## 6. 일정

| 일자 | 작업 | 담당 |
|---|---|---|
| 5/30 (금) | 사장님 인프라 조작 결재 의뢰 | PM |
| 5/31 (토) 09:00 | 환경 사전 점검 + 빌드 + xUnit + Smoke | 검증팀장 |
| 5/31 (토) 14:00 | S06·S07·S10 실행 | 검증팀장 |
| 6/1 (월) 09:00 | S14·S15·S16 실행 | 검증팀장 |
| 6/2 (화) 09:00 | S18·S19·S20 실행 | 검증팀장 |
| 6/2 (화) 18:00 | 종합 보고서 박제 + PM 보고 | 검증팀장 |
| 6/3 (수) 09:00 | W1 게이트 통과 결재 의뢰 | PM → 사장님 |

---

## 7. 후속 — 수동 11건 (W2)

자동 9건 PASS 후 수동 11건은 W2 D1~D7 (6/4~6/10) 검증팀 가도.
절차서: `tests/scenarios/MANUAL_PROCEDURES.md`

64시간 무인 시뮬레이션 (6/11~6/14) 통과 후 6/15 베타 발진.

---

**문서 끝.** 검증팀장 작3 정합 박제.
