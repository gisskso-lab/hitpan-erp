# HitPan ERP 인스톨러 — 빌드 가이드

> **WS-20260428-02**: 원클릭 통합 인스톨러 (Inno Setup, X2 + Gard 4개)
> **대상**: 마커스 리 (인프라 매니저), 본사 어드민
> **버전**: 1.0.7
> **작성**: 2026-04-28

---

## 📦 자산 구조

```
installer/
├── HitPan.iss              ← 메인 Inno Setup 스크립트 (인스톨러 본체)
├── build-installer.ps1     ← 빌드 자동화 (Gard 3, X1 갈아타기 디딤돌)
├── download-bundle.ps1     ← 의존성 번들 캐시 다운로드 (1회만)
├── bulk-create-tunnels.ps1 ← 베타 9곳 일괄 토큰 발급 (Cloudflare API)
├── install.bat             ← (구) v1.0.6 BAT 인스톨러 — 보존
├── install-tunnel.bat      ← cloudflared 단독 설치 (수동 운영용)
├── uninstall-tunnel.bat    ← 터널 깨끗한 제거
├── hitpan-start.bat        ← ERP 시작 스크립트
└── web-server.ps1          ← 웹서버 헬퍼
```

`installer-build/bundle/` (Git 추적 X, 캐시):
- `dotnet-hosting.exe` (.NET 8)
- `mariadb.msi`
- `vc_redist.x64.exe`
- `cloudflared.exe`
- `api/` `web/` (ERP 산출물)
- `hitpan_db.sql`

`dist/` (Git 추적 X, 빌드 결과):
- `HitPan-Setup-tenant-001.exe` (한 고객당 1개 EXE)
- ...

---

## 🚀 빠른 시작 — 베타 첫 1곳 빌드

### 사전 준비 (1회만)
1. **Inno Setup 6** 설치 (https://jrsoftware.org/isdl.php) — 무료
2. **.NET 8 SDK** 설치 (이미 있으면 skip)
3. PowerShell 7+ 권장 (Windows PowerShell 5.1 도 작동)

### 빌드 명령
```powershell
# 1. 의존성 번들 1회 다운로드 (~320MB, 캐시)
.\installer\download-bundle.ps1

# 2. 베타 첫 1곳 EXE 빌드
.\installer\build-installer.ps1 `
    -TenantId tenant-001 `
    -Token "eyJhXXXXXXXXXXXXXXXXXX..."

# 결과: dist/HitPan-Setup-tenant-001.exe (~475MB)
```

### 베타 10곳 일괄 빌드
```powershell
# 토큰 9개 일괄 발급 (Cloudflare API)
$env:CF_API_TOKEN = "<Cloudflare API Token>"
$env:CF_ACCOUNT_ID = "<Cloudflare Account ID>"
.\installer\bulk-create-tunnels.ps1 -StartIndex 2 -EndIndex 10

# 결과: tunnels.csv (Administrators only)
# CSV에서 Token 컬럼 읽어서 build-installer.ps1 반복 호출
Import-Csv installer\tunnels.csv | ForEach-Object {
    .\installer\build-installer.ps1 `
        -TenantId $_.TunnelName `
        -Token $_.Token `
        -SkipBundleDownload  # 캐시 재사용
}
```

---

## 🔐 보안 가드 (사장님 헌법 §18 부합)

### 절대 금지
- ❌ EXE를 Git에 커밋 (`dist/` 는 `.gitignore`)
- ❌ 토큰을 평문으로 채팅·이메일·로그에 노출
- ❌ `tunnels.csv` 를 Git/공유 폴더에 둠
- ❌ 다운로드 링크를 평문 HTTP로 호스팅

### 필수
- ✅ 토큰은 **빌드 시점 명령행 인자로만** 주입 (`-Token`)
- ✅ 환경변수 `CF_API_TOKEN` 은 LastPass / Bitwarden 에서 가져옴
- ✅ EXE 호스팅 = Cloudflare R2 signed URL (24h TTL)
- ✅ 토큰 보관 파일 (`hitpan-tunnel.conf`, `tunnels.csv`) = ICACLS Administrators/SYSTEM only

---

## 🎯 Gard 4개 (X1 갈아타기 디딤돌)

| Gard | 위치 | 설명 |
|---|---|---|
| **1** | `HitPan.iss` `#define TunnelToken` | 토큰을 빌드 시점 명령행으로 주입 |
| **2** | `build-installer.ps1` `$outputName` | EXE 파일명에 tenant 식별자 박음 |
| **3** | `build-installer.ps1` 자체 | 마커스 리 수동 ↔ 백오피스 API 동일 호출 |
| **4** | (미박힘, 마일스톤 3에서) | Cloudflare R2 호스팅 — 백오피스에서 자동 업로드 |

→ X1 전환 시 인스톨러 코드 **0줄 변경**, 백오피스가 `build-installer.ps1` 을 API로 호출만 하면 됨.

---

## 🧪 테스트 절차 (마커스 리)

### 클린 PC 테스트 (필수)
1. 윈도우 11 클린 VM 준비 (.NET·MariaDB 미설치 상태)
2. EXE 다운로드 → 우클릭 → "관리자 권한으로 실행"
3. 마법사 따라 "다음 → 설치" (사용자 입력 0건)
4. 5~10분 자동 설치
5. 데스크톱 [HitPan ERP] 더블클릭 → 브라우저 자동 열림
6. 로그인: `tenant@hitpan.kr / Admin1234!`
7. 4축 무결성 검증 SQL 1회 실행 (인수인계서 §4축 SoT)

### EVF 6대 영역 점검
- 부하: 동시 다운로드·설치 100건 시뮬레이션
- 장애: 설치 도중 인터넷 끊기 → 의존성 번들로 계속 설치
- 악의: EXE 변조 (Phase 2 코드사인)
- 혼돈: 도중 취소 → 자동 롤백
- 무지: 처음 보는 직원 5~10분 안에 완료
- 노후: 1년 후 같은 EXE 재설치 가능

→ WS-20260428-02 §4 EVF 표 참조.

---

## 🔄 X1 풀자동 전환 시 (정식 출시 직전)

마일스톤 3 백오피스에서 다음을 박으면 X1 완성:

1. **본사 백오피스** "베타 신청 받기" → 자동:
   - Cloudflare API 호출 → 토큰 발급
   - `build-installer.ps1 -TenantId X -Token Y` 호출
   - `dist/` → R2 업로드
   - signed URL 생성
   - 고객에게 이메일·카톡 자동 발송

2. **마일스톤 4 랜딩** "사용설치" 버튼 → 자동:
   - URL 파라미터 `?tenant=X` 로 받아서
   - signed URL로 다이렉트 다운로드

→ **인스톨러 자체 (`HitPan.iss`, `build-installer.ps1`) 변경 0줄.**

---

## ⚠ 트러블슈팅

### "ISCC.exe 를 찾을 수 없음"
→ Inno Setup 6 설치 + 기본 경로 `C:\Program Files (x86)\Inno Setup 6\` 확인

### "dotnet publish 실패"
→ 솔루션 빌드부터 통과해야 함. `dotnet build src/HitPan.sln` 먼저.

### "MariaDB 다운로드 실패"
→ `archive.mariadb.org` 차단 시 → 가비아·NHN 등 한국 미러 검토 (마커스 리 부록 A 약관 재확인)

### "cloudflared service install 실패"
→ 토큰 형식 (`eyJ` 시작) + 인터넷 + 관리자 권한 3개 동시 확인

### "EXE 빌드는 됐는데 클린 PC 설치 안 됨"
→ 의존성 번들 누락 가능. `download-bundle.ps1` 재실행 후 `-Force` 로 강제 재다운로드.

---

## 📞 지원

- 인프라: 마커스 리 (인프라 매니저, 4/28 합류)
- 백엔드 의존성: 백엔드 매니저
- 보안: 보안 매니저 (코드사인·토큰 정책)

문서: `docs/work-orders/WS-20260428-02_원클릭_통합_인스톨러_InnoSetup.md`

---

## 변경 이력

- 2026-04-28 — 마커스 리 + CTO 작성 (X2 + Gard 4개)
