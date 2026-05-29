# 백신 매뉴얼 — Norton / McAfee (수동 예외 등록)

> 헌법 #31 정합. Norton·McAfee는 한국 시장 점유율 낮으나, 글로벌 백신 호환성 셀링 포인트.
> 자동 4종 (Defender·V3·알약·Naver)과 달리 사용자 1회 수동 등록 필요.
> 설치 EXE 5분 자가 점검 실패 시 본 매뉴얼 자동 안내.

---

## 0. 격리 여부 확인 (공통)

1. 시작 → "이벤트 뷰어" 검색 → 실행
2. 좌측: Windows 로그 → Application
3. 우측 필터: 원본 = `HitPanWatchdog` 또는 EventID = `28003`
4. 메시지에 "quarantined" / "blocked" / "격리" 포함되면 백신 격리 발생

---

## 1. Norton 360 / Norton AntiVirus

### A. 폴더 예외 등록 (3단계)

1. **Norton 메인 → 설정(Settings)** 클릭
2. **상세 설정 → 검사 및 위험요소 → 검사에서 제외할 항목 → 구성**
3. **추가 → 폴더** → `C:\Program Files\HitPan` 선택 → **확인**
4. 추가로 다음 폴더 예외 등록:
   - `C:\Program Files\HitPan\payload`
   - `C:\Program Files\MariaDB 11.4`
   - `C:\Users\<현재사용자>\.cloudflared`

### B. 프로세스 예외 등록 (3단계)

1. **상세 설정 → 검사 및 위험요소 → AutoProtect 검색에서 제외할 프로세스 → 구성**
2. **추가 → 찾아보기** → 다음 EXE 각각 선택:
   - `C:\Program Files\HitPan\payload\cloudflared.exe`
   - `C:\Program Files\HitPan\payload\HitPan.Watchdog\HitPan.Watchdog.exe`
   - `C:\Program Files\MariaDB 11.4\bin\mysqld.exe`
3. **확인 → 적용**

### C. 격리된 파일 복원 (격리 발생 시)

1. **Norton 메인 → 보안 → 기록 보기**
2. 표시 = **격리** 선택
3. HitPan 관련 항목 우클릭 → **복원 및 향후 제외**

### D. 검증 명령 (PowerShell 관리자)

```powershell
# 격리 0건이면 폴더·EXE 모두 존재
Test-Path "C:\Program Files\HitPan\payload\cloudflared.exe"      # True 기대
Test-Path "C:\Program Files\HitPan\payload\HitPan.Watchdog\HitPan.Watchdog.exe"  # True 기대
Get-Service cloudflared, HitPanWatchdog                            # Running 기대
```

---

## 2. McAfee Total Protection / LiveSafe

### A. 폴더 예외 등록

1. **McAfee 메인 → 내 보호(My Protection)**
2. **실시간 검색(Real-Time Scanning) → 설정(Settings)**
3. **제외할 파일·폴더(Excluded Files) → 파일 추가/폴더 추가**
4. 다음 4개 폴더 추가:
   - `C:\Program Files\HitPan`
   - `C:\Program Files\HitPan\payload`
   - `C:\Program Files\MariaDB 11.4`
   - `C:\Users\<현재사용자>\.cloudflared`
5. **저장**

### B. 방화벽 예외 (Firewall)

1. **메인 → PC 보안 → 방화벽 → 설정**
2. **포트 및 시스템 서비스 → 추가**
3. 다음 3개 등록:
   - `cloudflared` UDP 7844 Outbound
   - `MariaDB` TCP 3306 Inbound (Private)
   - `HitPan.API` TCP 5257 Inbound (Private)

### C. 격리된 파일 복원

1. **메인 → 격리된 항목(Quarantined Items)**
2. HitPan 관련 항목 선택 → **복원(Restore)**
3. **신뢰할 수 있는 파일 목록에 추가**

### D. 검증 (Norton과 동일)

---

## 3. 사후 검증 — 자가 점검 재실행

```powershell
& "C:\Program Files\HitPan\scripts\SelfCheck.ps1"
# PASS 기대 → EventID 31010
# FAIL 시 본사 자동 알림 발송됨
```

---

## 4. 본사 CS 연락처

- 전화: **(추후 박제)**
- 이메일: **support@hitpan.kr**
- 챗봇: `https://{subdomain}.hitpan.kr/chat`

본 매뉴얼 적용 후에도 격리가 반복되면 즉시 CS에 워치독 EventID + Norton/McAfee 격리 로그를 첨부해 연락 주십시오.

---

**문서 끝.** 본 매뉴얼은 설치 EXE 시점에 `C:\Program Files\HitPan\docs\백신_매뉴얼_Norton_McAfee.pdf`로 자동 배치.
