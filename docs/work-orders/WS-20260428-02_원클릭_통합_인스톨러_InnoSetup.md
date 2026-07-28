# WS-20260428-02 — 원클릭 통합 인스톨러 (Inno Setup, 옵션 X2 + Gard 4개)

## 발행 정보

- **발행일**: 2026-04-28
- **발행자**: PM (닥터스트레인지) + CTO (래리 앨리슨) + 마커스 리 (인프라 매니저)
- **승인자**: 사장님 ("A1으로 가자", 2026-04-28)
- **우선순위**: 🔴 P0 — 마일스톤 1 핵심 산출물
- **선행 작지서**: WS-20260428-01 (로컬 터널링 설계)
- **사장님 직접 인용**:
  > "사용자 입장에선 웹페이지에서 히트판 사용설치 버튼 하나 클릭하면 바로 원클릭 설치되서 바로 사용할수 있게!!!!!"
  > "A1으로 가자" (X2 → X1 점진 전환)

---

## 1. 전략 — 옵션 X2 + Gard 4개 (X1 갈아타기 안전 보장)

### 1.1 옵션 X2 (베타 1~3개월) — 반자동
```
1. 베타 고객 → 사장님/영업팀 접촉 (전화·카톡·이메일)
2. 마커스 리 → 토큰 발급 + EXE 빌드 (5분/곳)
3. 본사 → 다운로드 링크 발송 (이메일/카톡 또는 R2)
4. 고객 → 더블클릭 → Inno Setup 마법사 → "다음 → 설치"
5. 자동 설치 → 데스크톱 아이콘 → 즉시 사용
```

**고객 마찰**: 더블클릭 1번 + 다음 클릭 몇 번. 끝.

### 1.2 X1 갈아타기 (정식 출시 직전)
- 인스톨러 자체 = **0줄 변경** (Gard 4개 덕분)
- 백오피스 토큰 자동 발급 + EXE 자동 빌드 (마일스톤 3에서 박음)
- 랜딩 "사용설치" 버튼 (마일스톤 4)
- X1 전환 비용 = +14시간 (인스톨러 매몰비용 0)

### 1.3 Gard 4개 (지금 박을 때 미리 깔아두는 X1 디딤돌)

| Gard | 내용 | X1 전환 시 효과 |
|---|---|---|
| **Gard 1** | Inno Setup 빌드 시 토큰을 명령행 옵션으로 주입 (`/DTunnelToken=...`) | 백오피스 API에서 동일 명령 호출 |
| **Gard 2** | EXE 파일명에 tenant 식별자 (`HitPan-Setup-tenant-001.exe`) | 백오피스 추적 + 다운로드 링크 자동화 |
| **Gard 3** | `build-installer.ps1` PowerShell 자동화 스크립트 | 마커스 리 수동 호출 ↔ 백오피스 API 호출 동일 |
| **Gard 4** | EXE 호스팅 = Cloudflare R2 무료 (10GB/월) | X1에서도 동일 호스팅 사용 |

---

## 2. 기술 설계

### 2.1 의존성 번들 (인스톨러에 포함)

| 의존성 | 용도 | 크기 | 출처 |
|---|---|---|---|
| .NET 8 Runtime (ASP.NET Core Hosting Bundle) | ERP 백엔드·프론트 실행 | ~80MB | Microsoft 공식 |
| MariaDB 11.4 MSI | DB 엔진 | ~200MB | mariadb.org |
| Visual C++ Redistributable 2015~2022 | MariaDB 의존 | ~25MB | Microsoft 공식 |
| cloudflared.exe | 터널 클라이언트 | ~15MB | GitHub Cloudflare |
| HitPan ERP 산출물 (api/ + wwwroot/) | ERP 본체 | ~150MB | 자체 빌드 |
| hitpan_db.sql | 초기 스키마 + 샘플 데이터 | ~5MB | 자체 |
| **총합** | | **~475MB** | |

### 2.2 Inno Setup 인스톨러 흐름

```
[STEP 1: 환영 + 라이선스]
   "히트판 ERP를 설치합니다"
   사장님 헌법 §18 (데이터 경계) 안내 박힘

[STEP 2: 설치 경로]
   기본: C:\HitPan
   변경 가능

[STEP 3: 진행률 (자동, 사용자 입력 0)]
   ├─ .NET 8 Runtime 설치 (조건부: 미설치 시만)
   ├─ Visual C++ Redist 설치 (조건부)
   ├─ MariaDB 11.4 설치 (조건부, 백그라운드 silent install)
   ├─ MariaDB root 비밀번호 자동 생성 (랜덤 32자)
   │  → 안전 보관 파일 hitpan-keys.conf (관리자 권한만)
   ├─ hitpan_erp 데이터베이스 + 사용자 생성
   ├─ hitpan_db.sql import (스키마 + 샘플 200개 × 3종)
   ├─ HitPan ERP 파일 복사 (api/, wwwroot/)
   ├─ JWT_SECRET / AES_KEY 자동 생성
   ├─ cloudflared.exe Windows 서비스 등록 (토큰 사전 주입)
   └─ 데스크톱 아이콘 + 시작메뉴

[STEP 4: 완료]
   "설치 완료 — 데스크톱의 [HitPan ERP] 아이콘을 더블클릭하세요"
   기본 로그인: tenant@hitpan.kr / Admin1234!
```

### 2.3 토큰 사전 주입 메커니즘 (Gard 1)

#### 빌드 시점
```powershell
# build-installer.ps1
ISCC.exe HitPan.iss `
    /DTunnelToken="eyJh..." `
    /DTenantId="tenant-001" `
    /DOutputName="HitPan-Setup-tenant-001.exe"
```

#### Inno Setup 스크립트 안에서
```iss
#define TunnelToken ""
#define TenantId ""

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then begin
    // cloudflared service install <token>
    Exec(ExpandConstant('{app}\cloudflared.exe'),
         'service install {#TunnelToken}',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // tenant_id를 ERP 환경변수에 박음
    SaveToFile(ExpandConstant('{app}\tenant.conf'),
               'TENANT_ID={#TenantId}', False);
  end;
end;
```

### 2.4 자동화 빌드 스크립트 (Gard 3)

```powershell
# installer/build-installer.ps1
param(
    [Parameter(Mandatory)] [string]$TenantId,
    [Parameter(Mandatory)] [string]$Token,
    [string]$OutputDir = "dist"
)

# 1. 의존성 번들 디렉토리 준비 (캐시 사용)
$bundleDir = "installer-build/bundle"
if (-not (Test-Path "$bundleDir/dotnet-hosting.exe")) {
    Invoke-WebRequest -Uri "https://aka.ms/dotnet/8.0/dotnet-hosting-win.exe" -OutFile "$bundleDir/dotnet-hosting.exe"
}
# (MariaDB MSI, VC++ Redist, cloudflared.exe도 동일 캐시)

# 2. ERP 빌드 산출물 복사
dotnet publish src/HitPan.API -c Release -o $bundleDir/api
dotnet publish src/HitPan.Web -c Release -o $bundleDir/web

# 3. Inno Setup 컴파일
$outputName = "HitPan-Setup-$TenantId.exe"
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" `
    "installer/HitPan.iss" `
    "/DTunnelToken=$Token" `
    "/DTenantId=$TenantId" `
    "/DOutputName=$outputName" `
    "/DOutputDir=$OutputDir"

Write-Host "빌드 완료: $OutputDir/$outputName"
```

### 2.5 호스팅 (Gard 4) — Cloudflare R2

```
구조:
  hitpan-installers (R2 버킷, 무료 10GB/월)
    └ tenant-001/HitPan-Setup-tenant-001.exe
    └ tenant-002/HitPan-Setup-tenant-002.exe
    ...

다운로드 URL (signed, 24시간 만료):
  https://r2.hitpan.app/tenant-001/HitPan-Setup-tenant-001.exe?token=...
```

---

## 3. 코드 가드레일

### 3.1 절대 금지
1. ❌ 토큰을 `.iss` 스크립트에 하드코딩 (Gard 1 위반)
2. ❌ 빌드된 EXE를 Git에 커밋 (`.gitignore` 가드 박힘)
3. ❌ R2 버킷 URL을 코드에 하드코딩 (X1 전환 시 변경 가능성)
4. ❌ 토큰을 평문으로 로그·이메일·채팅에 노출
5. ❌ 빌드 산출물을 평문 HTTP로 호스팅

### 3.2 필수
1. ✅ 모든 토큰은 **빌드 시점 명령행 옵션으로만 주입**
2. ✅ 토큰 보관 파일은 ICACLS로 Administrators/SYSTEM만
3. ✅ 다운로드 링크는 **signed URL** (24시간 만료)
4. ✅ 인스톨러는 코드사인 (Phase 2: 정식 출시 전)

---

## 4. EVF 6대 영역 검증 (베타 출시 절대 게이트)

| 영역 | 검증 | 합격 |
|---|---|---|
| **부하** | 베타 10곳 동시 다운로드 | R2 송신 한도 안에서 OK |
| **장애** | 설치 도중 인터넷 끊김 | 의존성 번들 = 인터넷 불필요 |
| **장애** | MariaDB 설치 실패 | 명확한 에러 메시지 + 로그 |
| **장애** | cloudflared 토큰 검증 실패 | 설치 계속 (터널 외 ERP 작동) |
| **악의** | EXE 변조 | Phase 2 코드사인 |
| **악의** | 토큰 도용 | signed URL + tenant 격리 |
| **혼돈** | 사용자가 도중 취소 | Inno Setup 자동 롤백 |
| **혼돈** | 이미 설치된 PC에 재설치 | 기존 감지 후 업그레이드 |
| **무지** | 처음 보는 직원이 5~10분 안에 완료 | 마법사 UI |
| **노후** | 1년 후 고객 PC에서 정상 작동 | Windows 호환성 검증 |

---

## 5. 작업 분해

| # | 작업 | 담당 | 시간 |
|---|---|---|---|
| 1 | Inno Setup 6 설치 + 학습 | 마커스 리 | 2시간 |
| 2 | 의존성 번들 캐시 디렉토리 구성 | 마커스 리 | 2시간 |
| 3 | `HitPan.iss` 작성 (메인 인스톨러) | 마커스 리 + CTO | 4~6시간 |
| 4 | `build-installer.ps1` 자동화 (Gard 3) | CTO | 2시간 |
| 5 | 토큰 사전 주입 코드 (Gard 1) | CTO | 1시간 |
| 6 | 클린 PC 테스트 (3대 정도) | 마커스 리 + 사장님 | 4시간 |
| 7 | 트러블슈팅 + EVF 통과 | CTO | 2시간 |
| 8 | 사장님 시연 + 보완 | 사장님 | 2시간 |
| **합계** | | | **약 19시간 (3일)** |

### 일정 (4/29~5/1)
| 일자 | 작업 |
|---|---|
| **4/29 (화)** | 1·2·5 — Inno Setup 셋업 + 의존성 번들 + 토큰 주입 |
| **4/30 (수)** | 3·4 — 메인 .iss 작성 + 자동화 스크립트 |
| **5/1 (목)** | 6·7·8 — 클린 PC 테스트 + EVF + 사장님 시연 |

---

## 6. 거버넌스 — 7단계

| 단계 | 담당 | 상태 |
|---|---|---|
| 1. 작지서 발행 | PM + CTO + 마커스 리 | ✅ 본 문서 |
| 2. 설계 검토 | 설계팀장 + 보안 매니저 | ⏳ 대기 |
| 3. 어벤져스 리뷰 | 백엔드 매니저 + UX/UI + 기술영업팀장 + ERP 매니저 | ⏳ 대기 |
| 4. 사장님 승인 | 사장님 | ✅ "A1으로 가자" (2026-04-28) |
| 5. 구현 | 마커스 리 (주) + CTO (보조) | ⏳ 대기 |
| 6. CTO 검증 | EVF 통과 + 4축 무결성 회귀 0건 | ⏳ 대기 |
| 7. 사장님 시연 | 클린 PC 1대 직접 설치 | ⏳ 대기 |

---

## 7. 사장님 헌법 부합 체크리스트

- [x] **§18 본사 데이터 경계** — 인스톨러 = 고객 PC 셋업, 본사 미수신
- [x] **§19 errors 0 / warnings 0** — Inno Setup 영역, 코드 변경 0줄
- [x] **§20 워크플로우 끊김 금지** — 4축 무결성 0건 영향
- [x] **잘 되는 거 안 건드림** — 기존 install.bat 유지, 새 .iss 추가만
- [x] **쉬워야 한다** — 더블클릭 1번 + 다음 클릭 = 끝
- [x] **점진 비용** — Inno Setup 무료, R2 10GB 무료, .NET/MariaDB/cloudflared 무료
- [x] **갈아타기 부드러움** — Gard 4개로 X1 전환 시 인스톨러 0줄 변경

---

## 8. 미해결 확장 의문 (참고만)

### Q1. 인스톨러 코드사인?
정식 출시 시점 권장 (~$200/년). 베타엔 SmartScreen 경고 우회 가능.
**→ 별도 작지서 (정식 출시 직전)**

### Q2. macOS 인스톨러?
베타 베타 고객 = 윈도우 도소매 → Windows만 우선. macOS는 정식 검토.

### Q3. 자동 업데이트?
Squirrel.Windows 또는 Inno Setup의 자체 업데이트. 베타 1개월 후 결정.
**→ 별도 작지서**

### Q4. 백오피스에서 본 작지서 자동화 (X1 전환)?
마일스톤 3 백오피스 작지서에서 본 작지서의 `build-installer.ps1`을 API로 호출.

### Q5. R2 버킷 권한 정책?
보안 매니저 자문 필수. signed URL TTL, 최대 다운로드 횟수 등.
**→ 마커스 리 + 보안 매니저 협업, 별도 작지서**

---

## 9. 변경 이력

- 2026-04-28 — PM + CTO + 마커스 리 발행, 사장님 승인 ("A1으로 가자")
