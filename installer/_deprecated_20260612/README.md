# Deprecated Installer Scripts (2026-06-12)

## 폐기 결재
- 사장님 결재 2026-06-12 — WS-20260612-01 Q1=A (단일화)
- HitPan-Universal.iss로 모든 가도 통합, 본 영역 죽은 코드

## 폐기 이유

### BootstrapInstall.ps1
- HitPan-Universal.iss `CurStepChanged` 영역이 직접 백오피스 API 호출
- BootstrapInstall.ps1은 호출 0건 (죽은 코드)
- 멀티사업자 로직은 HitPan-Universal.iss로 이전

### InstallCloudflared.ps1
- `POST /provisioning/tunnel` 엔드포인트 NCP 백오피스 영역 0건
- 호출 0건 (죽은 코드)
- cloudflared 설치 영역 HitPan-Universal.iss로 통합

### SelfCheck.ps1
- `HITPAN_SUBDOMAIN` 환경변수 의존하는데 박는 영역 0건
- HitPan-Universal.iss에서 호출 0건
- 자가 점검 로직 HitPan-Universal.iss `CurStepChanged` 영역으로 통합

## 복구
- Git 히스토리에 보존됨 (사고 시 참고용)
- 새로 작성하지 말고 HitPan-Universal.iss 영역 정정으로 가도
