# McAfee (Trellix) — 사전 false positive 보고

> 박제: 2026-05-29 | 제출 예정: 2026-05-30 (금) 09:00 | 담당: 보안 매니저 2
> 헌법 #31

---

## 제출 정보 (웹 폼)

| 항목 | 내용 |
|---|---|
| 제출 URL | https://www.mcafee.com/enterprise/en-us/threat-center/threat-landscape/sample-submission.html |
| 대체 URL | https://kc.mcafee.com/corporate/index?page=content&id=KB85567 (Sample 제출 가이드) |
| 양식 종류 | Sample Submission (Vendor / Pre-release) |
| 첨부 형식 | ZIP (비밀번호 `infected`) |
| 회신 기한 | 2026-06-08 (월) 목표 |

---

## 웹 폼 입력값

### Customer Information
- **Customer Type**: Software Vendor (Pre-release whitelist request)
- **Company**: 주식회사 히트판 (HitPan Co., Ltd.)
- **Contact**: 보안 매니저 2 (박제 예정)
- **Email**: security@hitpan.kr
- **Phone**: (박제 예정)
- **Country**: South Korea

### Product Information
- **Product**: HitPan ERP v1.1.0
- **Product Category**: Business / ERP Software
- **Pre-release Date**: 2026-06-15 (Beta) / 2026-07-14 (GA Target)
- **Distribution URL**: https://hitpan.kr/download
- **Code Signing**: EV Code Signing — DigiCert (박제 예정)

### Files (ZIP 비밀번호 `infected`)
| # | 파일 | SHA256 |
|---|---|---|
| 1 | HitPan.API.exe | (별첨) |
| 2 | HitPan.Web.exe | (별첨) |
| 3 | HitPan.Watchdog.exe | (별첨) |
| 4 | cloudflared.exe | (Cloudflare 공식) |
| 5 | HitPanSetup_v1.1.0.exe | (별첨, Inno Setup) |

### Reason for Submission
```
Pre-release whitelist request — HitPan ERP v1.1.0 is a Korean SMB ERP
scheduled for beta launch on June 15, 2026.

The HitPan.Watchdog.exe component is a Windows Service that auto-heals
the Cloudflare Tunnel after Windows Updates. Its behavior (sc create,
registry writes to AV exclusion lists, firewall rule creation, scheduled
task) may pattern-match against known malware indicators.

All binaries are signed with an EV Code Signing certificate. The source
code passes CodeQL (security-extended + security-and-quality) and
TruffleHog secret scans on every commit.

We respectfully request pre-emptive whitelist registration to prevent
false positives at customer sites during beta and GA launch.
```

### Declared Behaviors
- Windows Service installation: `sc create HitPanWatchdog`
- Registry exclusion writes: `HKLM\SOFTWARE\McAfee\...\Exclusions` (best-effort)
- Firewall rule creation: 4 rules (UDP 7844, TCP 3306/5234/5257)
- Outbound network: UDP 7844 (Cloudflare Tunnel — QUIC)
- Scheduled task: `HitPanGuardian` (Watchdog self-resurrection)
- Auto-update via Velopack (Phase 2, post-GA)

---

## 제출 후 후속 처리

1. 제출 직후 Submission ID 박제
2. **자동 분석 결과 (24시간)** 박제
3. **휴먼 검토 (3~7 영업일)** 박제
4. 화이트리스트 등록 확인 메일 박제
5. 미회신 시 6/8 (월) 후속 발송 (`virus_research@mcafee.com` 대체)

---

## 제출 체크리스트
- [ ] SHA256 박제
- [ ] ZIP 비밀번호 `infected` + 동봉
- [ ] 회사 정보 박제
- [ ] 제출 + Submission ID 박제
- [ ] 자동 분석 결과 박제
- [ ] 휴먼 검토 결과 박제
- [ ] 미회신 시 6/8 후속 발송
