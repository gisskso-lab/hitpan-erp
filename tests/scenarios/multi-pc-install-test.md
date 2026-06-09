# 다른 PC 설치마법사 시험 시나리오 (Phase 1 Week 3)

**작성**: 2026-06-09 PM
**근거**: 사장님 결재 Plan `cicd-velvety-reef.md` Day 13~17
**목적**: 사장님 PC 외 다른 PC에서 가입 → 설치 → 작동 흐름 100% 검증

---

## 시험 환경 (3대)

| 대상 | OS | 백신 | 권한 |
|---|---|---|---|
| **VM-1** | Windows 10 Pro 22H2 | Windows Defender 기본 | 관리자 |
| **VM-2** | Windows 11 Pro 23H2 | V3 Lite 또는 알약 | 관리자 |
| **VM-3** | Windows Server 2022 | Defender + 방화벽 강화 | 관리자 |

### 사전 준비
- 깨끗한 VM (.NET 미설치, MariaDB 미설치, cloudflared 미설치)
- 네트워크: 인터넷 정상 + 회사망 방화벽 시험 (별도 1회)
- 가입에 사용할 시험 사업자등록증 3종 (실 사업자 X)

---

## 시험 시나리오 7단계 (각 VM 동일)

### S1. 랜딩 가입
- [ ] `https://landing.hitpan.kr` 접속
- [ ] 가입 폼 입력 (회사명/사업자번호/대표/연락처/이메일/희망도메인)
- [ ] 도메인 별칭 실시간 중복검증 통과 확인
- [ ] 사업자등록증 사진 업로드
- [ ] 약관 4종 동의 + 가입 신청
- [ ] **검증**: `landing_signups` 박힘 + 신청 접수 메일 수신

### S2. 백오피스 승인
- [ ] `https://back.hitpan.kr` 마스터 로그인
- [ ] 신규 가입자 목록에 신청 박힘 확인
- [ ] 사업자등록증 사진 확인 후 승인
- [ ] **검증**: 시리얼 키 메일 수신 + `tenants.status='active'`

### S3. EXE 다운로드 + 실행
- [ ] GitHub Releases에서 최신 EXE 다운로드
- [ ] 더블클릭 → 관리자 권한 UAC 통과
- [ ] **검증**: SmartScreen 경고 처리 (코드 서명 전까지는 "추가 정보 → 실행" 안내)

### S4. 시리얼 입력 → 부트스트랩
- [ ] 마법사 시리얼 입력 화면
- [ ] 시리얼 입력 → 다음
- [ ] **검증**: 백오피스 부트스트랩 응답 200 + 회사정보 자동 표시

### S5. 사일런트 설치 (10분 이내)
- [ ] .NET 8 Runtime 자동 설치
- [ ] MariaDB 11.4 자동 설치 (root 비번 자동 생성)
- [ ] cloudflared 자동 설치 + 터널 토큰으로 서비스 등록
- [ ] 워치독 Windows 서비스 등록
- [ ] 백신 예외 + 방화벽 규칙 등록
- [ ] **검증**: `Get-Service hitpan-watchdog,cloudflared,MariaDB` 모두 Running

### S6. ERP 자동 시작 + 브라우저 열림
- [ ] 설치 완료 즉시 ERP API 자동 시작
- [ ] 브라우저로 `{고객별칭}.hitpan.kr` 자동 열림
- [ ] 로그인 화면 표시
- [ ] **검증**: HTTP 200 + 로그인 가능

### S7. 통신 무결성 자가 진단
- [ ] 워치독 1분 자가 진단 작동 확인 (`logs/watchdog.log`)
- [ ] `/health` 엔드포인트 200 응답
- [ ] **검증**: 헌법 #27 통신 무결성 통과

---

## 백신 호환성 시험 (헌법 #31)

각 VM에서 다음 5종 백신 1대씩 별도 시험:

| 백신 | 시험 절차 | 통과 기준 |
|---|---|---|
| Windows Defender | 기본값 + 실시간 보호 ON | 격리 0건 |
| V3 Lite | 기본값 + 실시간 검사 ON | 격리 0건 |
| 알약 | 기본값 + 실시간 검사 ON | 격리 0건 |
| 네이버 백신 | 기본값 | 격리 0건 |
| Norton/McAfee | 30일 체험판 | 격리 0건 |

**격리 발생 시**: 1클릭 봉합 매뉴얼 적용 후 격리 해제 가능 여부 확인

---

## 통신 무결성 시험 (헌법 #27, 17개 시나리오)

### 강제 시나리오 (Watchdog WS-28A~F 검증)
1. **Windows Update 재부팅** → 5분 자동 복구 확인
2. **cloudflared 강제 종료** → 자동 재시작 확인
3. **MariaDB 강제 종료** → 자동 재시작 확인
4. **방화벽 규칙 삭제** → 자동 재등록 확인
5. **터널 시크릿 무효화** → 자동 재발급 확인

각 시나리오 후 **5분 이내 자가 복구** 확인 (헌법 #28 정합)

---

## 자동화 도구

### Playwright 시나리오 (가입 → 승인 → 다운로드)
```bash
# tests/scenarios/audit-multi-pc-install.js (별도 작성)
node tests/scenarios/audit-multi-pc-install.js --vm vm1
```

### PowerShell 사후 점검
```powershell
# tests/scenarios/PostInstall-Verify.ps1 (작성 예정)
.\PostInstall-Verify.ps1 -ExpectedDomain 'mycompany.hitpan.kr'
```

---

## 통과 기준

| 항목 | 통과 |
|---|---|
| S1~S7 7단계 | 3 VM × 100% 통과 |
| 백신 5종 | 격리 0건 (또는 1클릭 봉합 매뉴얼 적용 후 0건) |
| 통신 무결성 17개 | 5분 이내 자동 복구 100% |
| 설치 시간 | 10분 이내 |

**한 항목이라도 불통과 = 베타1 출시 불가** (사장님 격언 + 헌법 #25·#31 정합)

---

## 후속 작업

- 시험 결과 보고서: `tests/scenarios/reports/multi-pc-install-{timestamp}.json`
- 사고 발견 시 즉시 P0 핫픽스 + 재시험
- 통과 후 사장님 결재 → 베타1 대리점 5곳 배포
