# V3 Lite (AhnLab) — 사전 false positive 보고

> 박제: 2026-05-29 | 발송 예정: 2026-05-30 (금) 09:00 | 담당: 보안 매니저 2
> 헌법 #31 (백신 5종 호환성 절대) + 헌법 #19 (보수적 가도)

---

## 발송 정보

| 항목 | 내용 |
|---|---|
| 수신 | v3sos@ahnlab.com |
| 참조 | security@hitpan.kr (본사 보안팀 박제) |
| 제목 | [false positive 사전 보고] 히트판 ERP 워치독 v1.1.0 빌드 — 격리 미요청 |
| 첨부 | (1) SHA256_v1.1.0.txt (2) HitPan_Watchdog_설명서_V3.pdf (선택) |
| 회신 기한 | 2026-06-05 (목) — 6/3 W1 게이트 + 영업일 2일 |

---

## 본문

안녕하십니까. **주식회사 히트판** 보안팀입니다.

당사는 중소사업자용 ERP "**히트판 (HitPan)**"을 개발·배포하고 있으며,
**2026년 6월 15일 베타 출시**를 앞두고 신규 빌드에 대한 **사전 false positive 보고**를 드립니다.

귀사 V3 Lite는 국내 점유율 상위 백신으로, 당사 고객 PC의 다수가 V3 Lite 환경에서 운영될 것으로 예상되어,
**격리 0건**을 절대 게이트로 박제하여 사전 협조를 요청드립니다.

---

### 1. 제품 정보

| 항목 | 내용 |
|---|---|
| 제품명 | 히트판 ERP (HitPan ERP) |
| 버전 | v1.1.0 |
| 배포 EXE | `HitPanSetup_v1.1.0.exe` (Inno Setup 6) |
| 빌드 일자 | 2026-06-03 (예정) |
| 코드 서명 | EV Code Signing (DigiCert, 박제 예정) |
| 빌드 환경 | .NET 8 self-contained + Inno Setup 6 |
| 배포 채널 | https://hitpan.kr/download (HTTPS only) |
| SHA256 | (빌드 후 별첨 SHA256_v1.1.0.txt) |

### 2. 포함된 실행 파일 — V3 Lite 호환성 확인 요청

| # | 파일 | 역할 | 동작 |
|---|---|---|---|
| 1 | `HitPan.API.exe` | ASP.NET Core 8 백엔드 | localhost:5257 |
| 2 | `HitPan.Web.exe` | Blazor WASM Host | localhost:5234 |
| 3 | `HitPan.Watchdog.exe` | Windows Service (자가 복구) | 1분 주기 자가 진단 |
| 4 | `cloudflared.exe` | Cloudflare Tunnel 클라이언트 | UDP 7844 외향 |
| 5 | `mariadb-11.4.10-winx64.msi` | MariaDB 공식 인스톨러 | TCP 3306 로컬 |

### 3. false positive 가능성 영역 (사전 공개)

- **Windows Service 등록**: `sc create HitPanWatchdog ...` → 자가 복구용 (악성 코드와 패턴 유사)
- **레지스트리 쓰기**: `HKLM\SOFTWARE\AhnLab\V3Lite\Exclusions` (백신 예외 자동 등록)
- **방화벽 규칙 추가**: `New-NetFirewallRule` 4개 (UDP 7844, TCP 3306·5234·5257)
- **Cloudflare Tunnel 외향**: UDP 7844 (Cloudflare 공식 프로토콜, QUIC 기반)
- **작업스케줄러 등록**: `HitPanGuardian` (워치독 사망 시 자동 부활 — 2층 자가 회복)

### 4. 검증 요청 사항

1. **사전 화이트리스트 등록**: 위 5개 EXE/MSI SHA256 해시
2. v3sos 데이터베이스 사전 등록 (배포 D-7 전 완료 목표)
3. **격리 발생 시 즉시 본사 연락** (security@hitpan.kr / 02-XXXX-XXXX 박제 예정)
4. (가능 시) 회신 메일에 등록 완료 일자 + 등록 번호 첨부

### 5. 본사 책임 약속

- 본 EXE는 **고객 PC에 자체 보유**되며, 본사 서버로 업무 데이터를 전송하지 않습니다 (헌법 #22 데이터 최소주의).
- 본사로 전송되는 메타 데이터는 **상태값(healthy/recovering/down) + 카운터**뿐이며, 매출·거래처·직원·세금계산서 등 업무 데이터 0건입니다.
- 5중 자동 검증(CodeQL + TruffleHog + xUnit + W1 게이트 + OWASP ZAP) 통과 후 배포합니다.

### 6. 본사 연락처

| 항목 | 내용 |
|---|---|
| 회사 | {{회사명}} |
| 담당 | 보안 매니저 2 ({{보안2직통}}) |
| 회신 이메일 | {{회신메일}} |
| 일반 문의 | {{일반메일}} |
| 대표 전화 | {{대표전화}} |
| 사업자 등록번호 | {{사업자번호}} |
| 회사 주소 | {{본사주소}} |

---

귀사의 빠른 검토와 협조 부탁드립니다.
중소사업자 ERP 시장에서 V3 Lite 사용자가 안전하고 끊김 없이 히트판 ERP를 사용할 수 있도록 사전에 협조를 요청드립니다.

감사합니다.

**주식회사 히트판 보안팀 드림**
2026-05-30

---

## 발송 체크리스트 (보안2 매니저용)

- [ ] SHA256_v1.1.0.txt 박제 (빌드 6/3 후)
- [ ] 본사 회사명·주소·전화·사업자번호 박제
- [ ] 메일 전 임원진 1회 검토
- [ ] 발송 시각 + 수신 확인 박제
- [ ] 회신 미수령 시 6/8 (월) 1차 리마인드 발송
- [ ] 회신 수령 시 즉시 PM 보고
