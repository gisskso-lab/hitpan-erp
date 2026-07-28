# 20개 강제 시나리오 검증 스크립트 골격

> 헌법 #27. 베타 발진 전 20/20 PASS 필수. 5분 이내 자동 복구 = PASS.

---

## 카테고리 1 — 인프라 (3건)

### S01. Windows Update 강제 재부팅

```powershell
# 트리거
shutdown /r /t 60 /c "S01 시나리오"
# 측정 (재부팅 후 자동 실행)
$start = Get-Date
do { Start-Sleep 10; $r = try { (Invoke-WebRequest "https://demo.hitpan.kr/health" -TimeoutSec 5).StatusCode } catch { 0 } }
while ($r -ne 200 -and ((Get-Date) - $start).TotalMinutes -lt 5)
$pass = $r -eq 200 -and ((Get-Date) - $start).TotalMinutes -lt 5
```
**PASS 기준:** 5분 안 /health 200.

### S02. 정전 후 복전 (UPS 보유)

수동: PC 콘센트 분리 → 30초 후 복귀 → 5분 안 자동 복구.

### S03. UPS 없는 정전

수동: 강제 전원 차단 → 복전 → 사용자 알림 박제 확인.

---

## 카테고리 2 — 네트워크 (3건)

### S04. KT 회선 다운

```powershell
Disable-NetAdapter -Name "이더넷" -Confirm:$false
Start-Sleep 120
# 본사 메타 ping = status:"down" 박제 확인
Enable-NetAdapter -Name "이더넷" -Confirm:$false
# 5분 안 status:"healthy" 복귀
```

### S05. 공유기 재부팅

수동: 공유기 전원 30초 차단 → 복귀.

### S06. DNS 사고

```powershell
Add-Content C:\Windows\System32\drivers\etc\hosts "0.0.0.0 demo.hitpan.kr"
Start-Sleep 180
# 워치독 fail streak 3 → 본사 알림 박제
(Get-Content C:\Windows\System32\drivers\etc\hosts) | ? { $_ -notmatch "demo.hitpan.kr" } | Set-Content C:\Windows\System32\drivers\etc\hosts
```

---

## 카테고리 3 — 보안 SW (3건)

### S07. Windows Defender 격리 시뮬

```powershell
# 일부러 EICAR 파일을 cloudflared 옆에 배치 (격리 트리거)
# 워치독이 격리 감지 → 본사 알림 + 매뉴얼 안내
```

### S08. AhnLab V3 격리

V3 Lite 설치된 본사 PC 5대에서 실측. 자동 예외 등록 확인 = 격리 0건.

### S09. 알약 격리

ALYac 설치 본사 PC 5대 실측. 자동 예외 = 격리 0건.

---

## 카테고리 4 — 자격증명 (2건)

### S10. TunnelSecret 회전

```powershell
$tunnelId = $env:HITPAN_TUNNEL_ID
$cred = "$env:USERPROFILE\.cloudflared\$tunnelId.json"
# 인위적으로 손상
Add-Content $cred "GARBAGE"
# cloudflared 재기동 → "Invalid tunnel secret" 로그 → WS-28-C 자동 재발급
Restart-Service cloudflared
Start-Sleep 60
# /health 200 복귀 확인
```

### S11. cert.pem 손상

수동: cert.pem 변조 → 본사 알림 박제 확인.

---

## 카테고리 5 — 물리 (2건)

### S12. SSD 비트 플립

DR 백업 복원 절차 박제 (수동).

### S13. RAM 오류

Windows 자동 재부팅 → 5분 안 복구.

---

## 카테고리 6 — 인적 (2건)

### S14. 사용자 Ctrl+C로 cloudflared 종료 시도

cloudflared가 Service로 등록되어 콘솔 종료 불가 확인.

### S15. 사용자 EXE 삭제

```powershell
Stop-Service cloudflared
Remove-Item "C:\Program Files\HitPan\payload\cloudflared.exe" -Force
# WS-28-D 자동 재설치 트리거 (2분 안)
Start-Sleep 180
Test-Path "C:\Program Files\HitPan\payload\cloudflared.exe"   # True 기대
```

---

## 카테고리 7 — 응용 (1건)

### S16. cloudflared 비정상 종료

```powershell
Get-Process cloudflared | Stop-Process -Force
# Service auto-restart (sc failure actions) → 1분 안 복귀
Start-Sleep 90
(Get-Service cloudflared).Status   # Running 기대
```

---

## 카테고리 8 — 외부 공격 (4건)

### S17. DDoS

Cloudflare 자동 차단 의존 (외부 부하 테스트는 베타 후 별도).

### S18. SQL Injection

```bash
curl "https://demo.hitpan.kr/api/items?q=';DROP TABLE items;--"
# 응답 = 400 또는 정상 처리. items 테이블 존재 확인.
```

### S19. XSS

```bash
curl -X POST "https://demo.hitpan.kr/api/items" \
  -d '{"name":"<script>alert(1)</script>"}' -H "Content-Type: application/json"
# 저장된 name = HTML-escaped 박제
```

### S20. Brute force 로그인

```powershell
1..100 | % {
  Invoke-RestMethod -Uri "https://demo.hitpan.kr/api/auth/login" `
    -Method Post -Body (@{email="t@t"; password="wrong$_"} | ConvertTo-Json) `
    -ContentType "application/json" -ErrorAction SilentlyContinue
}
# 5회 실패 후 IP 5분 lockout 박제
```

---

## 종합 PASS 게이트

| # | 시나리오 | 자동 복구 시간 | PASS |
|---|---|---|---|
| 1~16 | 자동 | < 5분 | 16/16 |
| 17~20 | 보안 | 차단 | 4/4 |

**전체 20/20 PASS만 베타 1주차 발진 가능.**

베타 1주차: 5곳 / 2주차: 10곳 / 3주차: 20곳. 사고 1건 = 5주 연장.

---

**문서 끝.** 다음: 본사 메타 ping JSON 스키마 + 매니저 작업지시서.
