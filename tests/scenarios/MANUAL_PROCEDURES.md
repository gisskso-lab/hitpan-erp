# 수동 11건 시나리오 절차서

> 헌법 #27 정합. 자동화 9건은 `Run-All-Scenarios.ps1`로 박제 완료.
> 본 문서는 하드웨어 / 외부 인프라 / 본사 PC 환경이 필요한 11건의 절차.
> 각 시나리오는 **시작 → 워치독 자동 봉합 → PASS 기준 → 봉합 후 정상 복귀** 4단으로 진행.

---

## S01 — Windows Update 강제 재부팅 (인프라)

### 시작
관리자 PowerShell:
```powershell
# 즉시 재부팅 예고 (60초 카운트다운)
shutdown /r /t 60 /c "S01 시나리오 시작"
# 또는 wuauclt /detectnow + 강제 재부팅
```

### 워치독 자동 봉합 (예상)
- WS-28-A: EventID 1074 TrustedInstaller 감지 → `post_reboot_check.flag` 박제
- 재부팅 후 BIOS POST → Windows 부팅 → 시작 30초 delay → 워치독 Service 자동 시작
- WS-28-B: flag 발견 → 5분 안 4 서비스 (MariaDB·cloudflared·API·Web) 시작 검증
- 외부 `/health` 200 응답까지 자동 대기

### PASS 기준
- 재부팅 후 **5분 이내** `https://{subdomain}.hitpan.kr/health` 200
- EventID 28004 (WS-28-B post-reboot recovery completed) 박제

### 봉합 후
- `_b.ClearFlag()` 자동 실행으로 flag 제거 박제

---

## S02 — UPS 정전 후 복전 (인프라)

### 시작
1. PC 콘센트를 **UPS 출력 포트**로 옮긴다 (이미 UPS 보유 시)
2. UPS 입력 콘센트를 30초간 분리
3. 복전 (UPS 입력 복귀)

### 봉합 (예상)
- UPS 자체 배터리로 PC 유지 → 다운타임 0
- UPS 무 시 강제 종료 → 복전 후 자동 부팅 → S01과 동일 흐름

### PASS 기준
- UPS 30초 정전: 다운타임 0
- UPS 없음: 복전 후 5분 안 /health 200

### 봉합 후
- UPS 배터리 충전 확인

---

## S03 — UPS 없는 정전 (인프라)

### 시작
관리자 / 사장님 직접: PC 콘센트 강제 분리

### 봉합 (예상)
- 강제 종료 → MariaDB innodb_force_recovery 가능성
- 복전 후 자동 부팅 → 워치독이 WS-28-I로 MariaDB Service 재시작 시도

### PASS 기준
- 복전 후 10분 이내 /health 200
- MariaDB 데이터 손실 0건 (사용자 매뉴얼 강조 — 자동 백업 24시간 주기)

### 봉합 후
- 사용자 매뉴얼: **UPS 권고** (헌법 #24 가르침 의무)
- 본사 CS 가르침: UPS 모델 추천 (APC BR550G 등 5만원대)

---

## S04 — KT 회선 다운 (네트워크)

### 시작 (수동 또는 자동)
```powershell
# 자동 시뮬: 네트워크 어댑터 비활성화
Disable-NetAdapter -Name "이더넷" -Confirm:$false
Start-Sleep -Seconds 180
Enable-NetAdapter -Name "이더넷" -Confirm:$false
```

### 봉합 (예상)
- WS-28-E: 외부 /health 3회 실패 → 본사 emergency 알림 시도
- 본사 알림도 실패 (회선 다운) → 워치독 로컬 큐에 적재
- 회선 복귀 시 큐 자동 flush + 메타 ping 정상화

### PASS 기준
- 회선 복귀 후 5분 안 healthy 상태 복귀
- 본사가 down 상태를 인지 (지연 ping 수신)

### 봉합 후
- LTE 백업 라우터 권고 매뉴얼 안내

---

## S05 — 공유기 재부팅 (네트워크)

### 시작
공유기 전원 어댑터 30초 분리 → 재연결

### 봉합 (예상)
- DHCP 재발급 → 동일 LAN IP 유지 시 영향 없음
- 변경 시 cloudflared가 자동 재바인딩

### PASS 기준
- 공유기 부팅 완료 후 3분 안 healthy

### 봉합 후
- 정적 IP 권고 (사용자 매뉴얼)

---

## S08 — AhnLab V3 격리 (보안SW)

### 시작
V3 Lite 설치된 본사 PC에서 설치 EXE 실행

### 봉합 (예상)
- `AntivirusExceptions.ps1`이 레지스트리 `HKLM\SOFTWARE\AhnLab\V3Lite\Exclusions`에 `HitPan` 등록
- V3가 워치독·cloudflared·MariaDB 스캔 안 함

### PASS 기준
- V3 격리 0건 (5개 EXE 모두 정상 동작)
- 본사 PC 5대 × 5 EXE = 25 검증 PASS

### 봉합 후
- 격리 발생 시 본사 CS 즉시 연락 + AhnLab 사전 보고 메일 결과 확인

---

## S09 — 알약 격리 (보안SW)

S08과 동일 절차. 레지스트리 `HKLM\SOFTWARE\ESTsoft\ALYac\Exclusions` 확인.

---

## S11 — cert.pem 손상 (자격증명)

### 시작
```powershell
$cert = "$env:USERPROFILE\.cloudflared\cert.pem"
if (Test-Path $cert) {
    Copy-Item $cert "$cert.bak"
    Add-Content $cert "GARBAGE"
}
```

### 봉합 (예상)
- cloudflared 인증 실패 → WS-28-C가 token 재발급 시도
- cert.pem은 회전 불가 (장기 인증서) → 본사 emergency 알림

### PASS 기준
- 5분 안 본사가 cert 재발급 안내 자동 전송

### 봉합 후
```powershell
Move-Item "$env:USERPROFILE\.cloudflared\cert.pem.bak" "$env:USERPROFILE\.cloudflared\cert.pem" -Force
Restart-Service cloudflared
```

---

## S12 — SSD 비트 플립 (물리)

### 시작
DR 시나리오. 실 테스트 어려움 → 검증 PC에서 **인위적 파일 손상**으로 대체:
```powershell
$db = "C:\Program Files\MariaDB 11.4\data\hitpan_erp\items.ibd"
Stop-Service MariaDB
[System.IO.File]::AppendAllBytes($db, [byte[]]@(0xFF,0xFF,0xFF))
Start-Service MariaDB
```

### 봉합 (예상)
- MariaDB innodb_corruption 감지 → Service 시작 실패
- 워치독 WS-28-I가 MariaDB Service 시작 시도 3회 → 본사 emergency

### PASS 기준
- 5분 안 본사 emergency 수신
- 자동 백업 복원 가이드 자동 안내 (사용자 매뉴얼)

### 봉합 후
- innodb_force_recovery 매뉴얼 + 자동 백업에서 복원

---

## S13 — RAM 오류 (물리)

### 시작
실 테스트 불가. **Windows MemTest** 또는 **사전 학습용**:
- 메모리 오류 발생 시 Windows BSOD → 자동 재부팅 → S01과 동일 흐름

### PASS 기준
- BSOD 후 자동 재부팅 → 5분 안 /health 200

---

## S17 — DDoS (외부공격)

### 시작 (Cloudflare 의존)
- 외부 부하 테스트 도구 (예: hey, k6) 또는 베타 후 별도 시험
- 본 시나리오는 **Cloudflare 자동 차단** 의존 → 직접 시뮬레이션 보류

### PASS 기준
- 동일 IP 5분 100req/s 초과 시 Cloudflare 자동 차단 응답 (403/429)

---

## 종합 체크리스트

| ID | 시작 가능 | 검증 환경 | 예상 시간 |
|---|---|---|---|
| S01 | 즉시 | 검증 전용 PC | 5분 |
| S02 | UPS 필요 | UPS 보유 PC | 5분 |
| S03 | 즉시 | 검증 전용 PC | 10분 |
| S04 | 자동 어댑터 토글 | 검증 PC | 5분 |
| S05 | 공유기 필요 | 본사 사무실 | 5분 |
| S08 | V3 설치 PC | 본사 PC #1 | 10분 |
| S09 | ALYac 설치 PC | 본사 PC #2 | 10분 |
| S11 | 즉시 | 검증 PC | 5분 |
| S12 | 즉시 (인위적 손상) | 검증 PC | 15분 |
| S13 | 실 테스트 불가 | 학습용 문서 | N/A |
| S17 | Cloudflare 의존 | 베타 후 | N/A |

자동 9건 + 수동 11건 = **20/20 PASS** 후 베타 발진.

---

**문서 끝.**
