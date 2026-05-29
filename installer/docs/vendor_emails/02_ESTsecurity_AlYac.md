# 알약 (ESTsecurity) — 사전 false positive 보고

> 박제: 2026-05-29 | 발송 예정: 2026-05-30 (금) 09:00 | 담당: 보안 매니저 2
> 헌법 #31

---

## 발송 정보

| 항목 | 내용 |
|---|---|
| 수신 | esrc@estsecurity.com |
| 참조 | security@hitpan.kr |
| 제목 | [사전 false positive 보고] 히트판 ERP 설치 EXE v1.1.0 — 화이트리스트 등록 요청 |
| 첨부 | SHA256_v1.1.0.txt + (선택) 제품 설명서 PDF |
| 회신 기한 | 2026-06-05 (목) |

---

## 본문

안녕하십니까. **주식회사 히트판** 보안팀입니다.

당사는 중소사업자용 ERP "**히트판 (HitPan)**"을 개발·배포하고 있으며,
**2026년 6월 15일 베타 출시**를 앞두고 신규 빌드에 대한 **사전 false positive 보고**를 드립니다.

귀사 알약은 국내 무료 백신 점유율 상위로, 당사 고객 PC 다수가 알약 환경에서 운영될 것으로 예상되어,
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
| 배포 채널 | https://hitpan.kr/download (HTTPS only) |
| SHA256 | (별첨 SHA256_v1.1.0.txt) |

### 2. 포함된 실행 파일 — 알약 호환성 확인 요청

| # | 파일 | 역할 |
|---|---|---|
| 1 | `HitPan.API.exe` | ASP.NET Core 8 백엔드 (localhost:5257) |
| 2 | `HitPan.Web.exe` | Blazor WASM Host (localhost:5234) |
| 3 | `HitPan.Watchdog.exe` | Windows Service (자가 복구) |
| 4 | `cloudflared.exe` | Cloudflare Tunnel (UDP 7844) |
| 5 | `mariadb-11.4.10-winx64.msi` | MariaDB 공식 |

### 3. false positive 가능성 영역 (사전 공개)

- Windows Service 등록 (`HitPanWatchdog`)
- 레지스트리 쓰기 (`HKLM\SOFTWARE\ESTsoft\ALYac\Exclusions`)
- 방화벽 규칙 추가 (4건)
- Cloudflare Tunnel UDP 7844 외향
- 작업스케줄러 `HitPanGuardian` 등록

### 4. 검증 요청 사항

1. 5개 EXE/MSI SHA256 사전 화이트리스트 등록
2. 격리 발생 시 즉시 본사 연락
3. 회신 메일에 등록 완료 일자 첨부

### 5. 본사 책임 약속

- 본 EXE는 고객 PC 자체 보유, 본사 서버 업무 데이터 전송 0건 (헌법 #22).
- 본사 메타 데이터 = 상태값 + 카운터만.
- 5중 자동 검증 통과 후 배포.

### 6. 본사 연락처

| 항목 | 내용 |
|---|---|
| 회사 | 주식회사 히트판 |
| 담당 | 보안 매니저 2 |
| 이메일 | security@hitpan.kr |
| 전화 | (박제 예정) |

---

빠른 검토 부탁드립니다.

**주식회사 히트판 보안팀 드림**
2026-05-30

---

## 발송 체크리스트
- [ ] SHA256 박제 (6/3 빌드 후)
- [ ] 회사 정보 박제
- [ ] 발송 + 수신 확인
- [ ] 회신 미수령 시 6/8 리마인드
