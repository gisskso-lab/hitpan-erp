# 20개 강제 시나리오 검증 자동화

> 헌법 #27. 5분 이내 자동 복구 = PASS. 20/20 PASS만 베타 발진.

## 실행 (관리자 권한 PowerShell)

```powershell
# 비파괴만 (안전 모드)
.\Run-All-Scenarios.ps1 -SkipDestructive

# 특정 시나리오만
.\Run-All-Scenarios.ps1 -Only S07,S18,S19,S20

# 전체 (검증 전용 PC에서만)
.\Run-All-Scenarios.ps1
```

## 자동화 가능 (코드 박제 완료)

| ID | 영역 | 시나리오 | 상태 |
|---|---|---|---|
| S06 | 네트워크 | DNS 사고 (hosts 변조) | ✅ |
| S07 | 보안SW | Defender 격리 (EICAR) | ✅ |
| S10 | 자격증명 | TunnelSecret 회전 | ✅ |
| S14 | 인적 | 콘솔 종료 시도 | ✅ |
| S15 | 인적 | EXE 삭제 → 자동 재설치 | ✅ |
| S16 | 응용 | cloudflared 비정상 종료 | ✅ |
| S18 | 외부공격 | SQL Injection | ✅ |
| S19 | 외부공격 | XSS | ✅ |
| S20 | 외부공격 | Brute force | ✅ |

## 수동/하드웨어 필요 (체크리스트만)

| ID | 시나리오 | 필요 |
|---|---|---|
| S01 | Windows Update 재부팅 | `shutdown /r /t 60` 수동 |
| S02 | UPS 정전 복전 | UPS 콘센트 분리 |
| S03 | UPS 없는 정전 | 전원 강제 차단 |
| S04 | KT 회선 다운 | `Disable-NetAdapter` 또는 ISP 차단 |
| S05 | 공유기 재부팅 | 공유기 전원 30초 |
| S08 | V3 격리 | V3 Lite 설치 PC |
| S09 | 알약 격리 | ALYac 설치 PC |
| S11 | cert.pem 손상 | 인증서 변조 |
| S12 | SSD 비트 플립 | DR 백업 복원 |
| S13 | RAM 오류 | 하드웨어 |
| S17 | DDoS | Cloudflare 의존 (외부 부하 테스트) |

## 베타 발진 게이트

- 자동 9건 + 수동 11건 = **20/20 PASS** 후 베타 1주차 5곳 발진
- 사고 1건 = 본 원인 봉합 + EXE 자동 업데이트 + 1주 추가 모니터링 = 5주 연장
