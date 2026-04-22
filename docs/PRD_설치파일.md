# 히트판 ERP 설치파일 PRD (Product Requirements Document)

**문서 버전**: 1.0.0  
**작성일**: 2026-04-22  
**작성자**: PM (HitPan)  
**대상 산출물**: 대리점·베타 고객·최종 사용자에게 배포할 원클릭 설치 프로그램 (.exe)  
**연관 커밋**: 대리점 시연 직전 VBS 환경변수 누락으로 API 기동 실패 사건 (2026-04-22)

---

## 1. 목적

히트판 ERP를 처음 보는 사용자(대리점 사장·경리·중소기업 실무자)가 **아무런 배경지식·개발환경 없이** 원클릭으로 설치하고 즉시 사용할 수 있도록 한다. 설치 실패는 세일즈 실패로 직결되므로 "한 번에 되는 것"이 최우선 품질 지표.

---

## 2. 타겟 사용자

| 페르소나 | 특성 |
|---|---|
| 대리점 사장 | 50대, 설치 마법사는 익숙하지만 CMD는 낯섦. 한글 UI 기대 |
| 중소기업 경리 | 40대, Windows 기본 기능만. 오류 메시지 영어면 포기 |
| 공구상사 대표 | 60대, 바탕화면 아이콘 더블클릭까지만. 그 이상 조작 불가 |

**공통**: bat 우클릭 → 관리자 권한 실행을 **몰라도** 설치가 되어야 한다.

---

## 3. 핵심 요구사항 (MUST)

### R1. 단일 `.exe` 설치 프로그램
- ZIP + bat 조합 금지 (실패율 높음)
- Windows 표준 설치 마법사 UI (Inno Setup 또는 동등)
- 관리자 권한 승격은 OS가 정식 처리 (UAC 프롬프트 1회)

### R2. 완전 자동화
사용자 조작은 **"다음 → 다음 → 마침"** 외 없음:
- MariaDB 자동 설치 (`.msi` 포함)
- DB 생성·계정 생성·스키마 import·샘플 데이터 주입
- 환경변수·보안 키 자동 생성
- 방화벽 포트 자동 허용
- 바탕화면 바로가기 자동 생성

### R3. 설치 직후 연결 보장
- 설치 완료 시점 → DB smoke test 통과 확인 후에만 `Finished`
- 마법사 마지막 단계 [지금 실행] 체크 → 서버 기동 → 브라우저 자동 열림
- 사용자는 **로그인 화면만 보면** 된다

### R4. 관찰 가능한 설치
- 설치 중 모든 단계를 `%TEMP%\hitpan-install.log`에 기록
- 실패 시 해당 단계에서 즉시 오류 다이얼로그 + 로그 위치 안내
- 설치 도중 창이 조용히 꺼지는 일 절대 없음

### R5. 멱등성 (Idempotency)
- 이미 MariaDB가 있는 PC → 건너뛰고 DB·계정만 생성
- 이미 `hitpan` 계정이 있는 PC → root 접근 없이 바로 스키마 import
- 재설치 시 기존 DB 데이터 손상 없음

---

## 4. 비기능 요구사항 (SHOULD)

### 4.1 성능
- 전체 설치 완료 **3분 이내** (10Mbps 이상 네트워크 기준)
- MariaDB `.msi` `/passive` 옵션 사용 (자동 진행 UI)
- DB 스키마 import **1분 이내** (공구상가 5년치 샘플 약 41MB 기준)

### 4.2 크기
- 설치 파일 **150MB 이하**
- 구성: API 자체포함 빌드(~137MB) + MariaDB MSI(~75MB) + DB 덤프(~41MB) → LZMA2 압축 후 ~130MB

### 4.3 로캘
- 한국어 설치 마법사 (Inno Setup `Korean.isl`)
- 오류 메시지는 한글 기본, 기술 로그는 영문 허용

### 4.4 호환성
- Windows 10 (1809) 이상
- x64만 지원 (ArchitecturesAllowed=x64)

---

## 5. 설치 시나리오

### S1. 깨끗한 PC (MariaDB 없음)

```
1. 사용자: .exe 더블클릭
2. UAC 프롬프트 → "예"
3. 마법사: 라이선스 → 경로 선택 (기본 C:\HitPan) → 설치
4. 자동 진행 (2~3분):
   ├── MariaDB 11.4 MSI /passive 설치
   ├── 서비스 기동 확인 (polling 60초)
   ├── root(Hitpan2025!)로 DB + hitpan 계정 생성
   ├── hitpan_db.sql 스키마·샘플 import
   ├── JWT·AES 랜덤 키 생성 → .env 파일 작성
   ├── DB smoke test (tenants 테이블 SELECT)
   └── 방화벽 포트 5234 허용
5. 마법사 완료 화면 → [지금 실행] 체크 유지 → 마침
6. start-hitpan.vbs 자동 실행 → 서버 기동 → 브라우저 자동 열림
7. 로그인 화면 → tenant@hitpan.kr / Admin1234!
```

### S2. 기존 MariaDB가 있는 PC

1~5 동일, 단 MariaDB MSI 설치 건너뛰기:
- 기존 MariaDB 감지 → 기존 설치 사용
- root 자동(`Hitpan2025!`) 실패 → 사용자에게 root 비번 1회 입력
- `hitpan` 계정이 이미 있으면 → root 단계 전부 건너뛰기

### S3. 이미 히트판 ERP가 설치된 PC (재설치·업그레이드)

- 기존 `.env`, `hitpan-keys.conf`, `hitpan\logs\` 보존
- ERP 실행파일(`hitpan\`)만 교체
- DB는 기존 유지 (사용자 데이터 보존)

---

## 6. 연결 검증 절차 (DoD · Definition of Done)

설치가 "완료"로 인정되려면 **아래 8항 전부 통과**해야 한다:

1. `HitPan.API.exe` 파일 존재 확인
2. MariaDB 서비스 `Running` 상태
3. `hitpan` 계정으로 `SELECT 1` 성공
4. `hitpan_erp` 데이터베이스 존재
5. `tenants` 테이블에 레코드 존재 (샘플 import 확인)
6. `{app}\hitpan\.env` 파일 존재 + 필수 키 전부 기록 (DB_*, JWT_SECRET, ERP_ENCRYPTION_KEY)
7. 포트 5234 방화벽 허용
8. `http://localhost:5234/health` HTTP 200 (옵션: 설치 후 smoke)

한 가지라도 실패 → 오류 다이얼로그 + 로그 경로 표시 + 설치 "실패"로 마크.

---

## 7. 장애 복구 · 진단

### 7.1 실패 지점별 안내 메시지

| 실패 지점 | 사용자 메시지 | 해결 가이드 |
|---|---|---|
| MariaDB 설치 실패 | "데이터베이스 설치 중 오류" | `prereqs/mariadb.msi` 수동 실행 안내 |
| 서비스 기동 안 됨 | "MariaDB 서비스가 시작되지 않음" | 서비스 관리자 경로 안내 |
| root 로그인 실패 | "관리자 비밀번호를 입력하세요" | 입력 프롬프트 |
| 스키마 import 실패 | "데이터베이스 초기화 실패" | `%TEMP%\hitpan-install.log` 확인 |
| 포트 5234 사용 중 | "포트 충돌. 다른 프로그램 종료 필요" | `netstat -ano` 안내 |

### 7.2 로그 위치 (원격 진단용)

| 파일 | 기록 내용 |
|---|---|
| `%TEMP%\hitpan-install.log` | 설치 스크립트 단계별 로그 |
| `%TEMP%\Setup Log YYYY-MM-DD #N.txt` | Inno Setup 자체 로그 |
| `{app}\hitpan\logs\hitpan-YYYYMMDD.log` | API 서버 Serilog (14일 보존) |

---

## 8. 보안 요구사항

- 설치 시점 **설치본마다 고유한 JWT·AES 키 생성** (`System.Security.Cryptography.RandomNumberGenerator`, 64바이트/32바이트)
- `.env` 파일은 `{app}\hitpan\` 폴더에만 저장 (네트워크 공유 금지)
- MariaDB `hitpan` 계정은 `localhost`로만 접근 허용 (외부 노출 차단)
- 방화벽 포트 5234는 `Private/Domain` 프로필에서만 (Public 프로필 제외 권장, 추후 옵션화)
- `root` 비번은 `.env`·`.log` 어디에도 기록 금지

---

## 9. 빌드 요구사항 (개발팀 전용)

### 9.1 빌드 환경
- Windows 10/11 x64
- .NET 8 SDK
- MariaDB 11.4 클라이언트 (mariadb-dump용)
- Inno Setup 6

### 9.2 빌드 산출물 구성

설치 `.exe` 내부에 포함될 자료:

```
hitpan/                     ── API 자체포함 빌드 (dotnet publish --self-contained)
  HitPan.API.exe            ── 진입점
  wwwroot/                  ── Blazor WebAssembly (publish 후 이식)
  *.dll, *.json 등          ── 런타임 구성

hitpan_db.sql               ── mariadb-dump 산출 (스키마 + 샘플)

prereqs/
  mariadb.msi               ── MariaDB 11.4 Windows x64 설치 파일

scripts/
  start-hitpan.vbs          ── 바로가기 타깃 (서버 기동 + 브라우저)
  stop-hitpan.bat           ── 서버 중지
  open-browser.vbs          ── 보조
  install-setup.ps1         ── Inno Setup [Run]에서 호출되는 후처리 (DB/env/키)
```

### 9.3 빌드 파이프라인 (요약)

```powershell
# 1. API publish (self-contained)
dotnet publish src/HitPan.API/HitPan.API.csproj -c Release -r win-x64 --self-contained true -o <build>/hitpan

# 2. Web publish → wwwroot 이식
dotnet publish src/HitPan.Web/HitPan.Web.csproj -c Release -o <build>/tmp-web -p:PublishTrimmed=false
Copy-Item <build>/tmp-web/wwwroot/* <build>/hitpan/wwwroot -Recurse -Force
'{"ApiBaseUrl":"http://localhost:5234"}' | Out-File <build>/hitpan/wwwroot/appsettings.json -Encoding utf8

# 3. DB 덤프 (MariaDB 전용)
& "C:\Program Files\MariaDB 11.4\bin\mariadb-dump.exe" `
  -uhitpan -pHitpan2025! --no-tablespaces --default-character-set=utf8mb4 `
  --routines --triggers --add-drop-table `
  hitpan_erp > <build>/hitpan_db.sql

# 4. MariaDB MSI 사전 배치 (<build>/prereqs/mariadb.msi)

# 5. Inno Setup 컴파일
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" <build>/hitpan-installer.iss
# → <build>/output/히트판_ERP_설치_v<VER>.exe
```

### 9.4 빌드 산출물은 리포지토리에 포함하지 않음

- `installer-build/` 전체 `.gitignore` 처리 (build artifacts)
- 본 PRD(`docs/PRD_설치파일.md`)만 커밋
- 빌드는 CI 또는 로컬 개발자가 9.3에 따라 수행

---

## 10. 품질 게이트 (출시 전 체크리스트)

- [ ] 깨끗한 가상머신(Windows 10·11)에서 `.exe` 실행 → 3분 이내 로그인 도달
- [ ] 기존 MariaDB가 있는 PC에서 재설치 → 데이터 손상 없음
- [ ] 설치 중 네트워크 끊김 시뮬레이션 → 명확한 실패 메시지
- [ ] 설치 로그에 민감정보(root 비번, 평문 JWT 시크릿) 없음
- [ ] 방화벽 off 상태에서도 localhost 접근 가능
- [ ] 바탕화면 아이콘·시작메뉴 항목 정상 생성
- [ ] 제거 후 `{app}` 폴더 깨끗이 삭제 (단, 사용자 DB는 별도 안내)
- [ ] Windows Defender SmartScreen "알 수 없는 게시자" 경고 → 코드 서명 검토 (장기)

---

## 11. 향후 개선 로드맵

| 우선 | 항목 | 시기 |
|---|---|---|
| P1 | 설치 `.exe` 코드 서명 인증서 (SmartScreen 경고 제거) | 베타 이후 |
| P2 | MSI 방식 배포 (그룹정책 배포 지원) | 기업 고객 유치 시 |
| P3 | 자동 업데이트 (설치 후 버전 업그레이드) | 정식 출시 |
| P4 | 다국어 설치 마법사 (영·중·일) | 글로벌 진출 |
| P5 | MariaDB 대신 SQLite 옵션 (소규모 체험판) | 체험판 배포 |

---

## 12. 변경 이력

| 버전 | 일자 | 변경 |
|---|---|---|
| 1.0.0 | 2026-04-22 | 최초 작성. v1.0.4 설치 실패(VBS 환경변수 누락) 사건 이후 전면 재정리 |
