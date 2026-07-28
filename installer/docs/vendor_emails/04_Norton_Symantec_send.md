# Norton (Symantec/Gen Digital) — 사전 false positive 보고

> 박제: 2026-05-29 | 제출 예정: 2026-05-30 (금) 09:00 | 담당: 보안 매니저 2
> 헌법 #31

---

## 제출 정보 (웹 폼)

| 항목 | 내용 |
|---|---|
| 제출 URL | https://submit.symantec.com/false_positive/ |
| 대체 URL | https://submit.norton.com/false_positive/ |
| 양식 종류 | False Positive Submission |
| 첨부 형식 | ZIP (비밀번호 `infected`, 표준 관례) |
| 회신 기한 | 2026-06-08 (월) 목표 — 영업일 5일 |

> 참고: Norton은 글로벌 자동 분석 + 휴먼 검토 혼합. 등록 번호 자동 부여.

---

## 웹 폼 입력값

### Company Information
- **Company**: (주)공영정보 (HitPan Co., Ltd.)
- **Business Reg. No.**: 107-86-04427
- **Address**: 경기도 광명시 일직로43 1009호
- **Contact Name**: 보안 매니저 2
- **Email**: security@hitpan.kr
- **Phone**: 02-761-8966
- **Main Office**: 02-761-8966
- **Country**: South Korea

### Product Information
- **Product Name**: HitPan ERP
- **Version**: v1.1.0
- **Product Type**: Business / ERP Software
- **Customer Type**: Software Vendor (Pre-release submission)
- **Distribution URL**: https://hitpan.kr/download
- **Code Signing**: EV Code Signing — DigiCert (박제 예정)

### Files (ZIP 비밀번호 `infected`)
| # | 파일 | SHA256 |
|---|---|---|
| 1 | HitPan.API.exe | (별첨) |
| 2 | HitPan.Web.exe | (별첨) |
| 3 | HitPan.Watchdog.exe | (별첨) |
| 4 | cloudflared.exe | (Cloudflare 공식, 변형 없음) |
| 5 | HitPanSetup_v1.1.0.exe | (별첨, Inno Setup wrapper) |

### Description
```
HitPan is a Korean ERP solution for SMB (Small/Medium Business).
We are submitting our v1.1.0 release for whitelist consideration prior to
beta launch on June 15, 2026.

The Watchdog component (HitPan.Watchdog.exe) is a Windows Service that
performs self-healing for the Cloudflare Tunnel after Windows Updates,
which may pattern-match against known malware behaviors. It is signed
with an EV Code Signing certificate and the source code passes CodeQL
security scans.

We respectfully request pre-emptive whitelist registration to prevent
false positives at our customers' sites.
```

### Behaviors (declared)
- Windows Service installation
- Registry write to `HKLM\SOFTWARE\...\Exclusions` keys
- Firewall rule creation (4 rules)
- Outbound UDP 7844 (Cloudflare Tunnel — QUIC)
- Scheduled task creation (`HitPanGuardian`)

---

## 제출 후 후속 처리

1. 제출 직후 등록 번호(Ticket ID) 박제
2. **자동 분석 결과 (24시간)**: 박제
3. **휴먼 검토 결과 (3~5 영업일)**: 박제
4. False positive 확인 시 → 등록 완료 메일 박제
5. 미확인 시 → 6/8 (월) 후속 메일 (`enterprise.feedback@nortonlifelock.com`)

---

## 제출 체크리스트 (보안2 매니저용)
- [ ] SHA256 박제 (6/3 빌드 후)
- [ ] ZIP 비밀번호 `infected` + 동봉
- [ ] 회사 정보 박제
- [ ] 제출 + Ticket ID 박제
- [ ] 자동 분석 결과 박제
- [ ] 휴먼 검토 결과 박제
- [ ] 미회신 시 6/8 후속 발송
