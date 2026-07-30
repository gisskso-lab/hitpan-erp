# EXE 빌드 GO 결재 요청서

> 발행: 2026-06-05 PM (브라운킴)
> 메모리 `project_exe_build_hold` 정합 — 사장님 GO 결재 후에만 build-installer.ps1 실행 가능
> 본 문서는 보충 영역 정의 + 결재 요청

---

## 1. EXE 빌드 보류 사유 (기존 박제)

> "EXE 빌드 박제 절대 홀드 (2026-06-02 사장님 명령): ERP 빠진 부분 보충 1회 후 GO 결재 후에만 build-installer.ps1. 누구도 사전 박제 불가."

본 세션 시작 시점 기준 잔여 보충 영역 본 PM 정의:

### 1.1 보충 완료 영역 (본 세션 산출)
- ✅ W11 Owner 백엔드 + 화면 5개
- ✅ W9 대리점 정산·시리얼 백엔드 + 화면 2개
- ✅ W10 webhook 진범 #1 봉합 (CAST GUID)
- ✅ W14 도메인 자동 발급 골격 (활성화는 환경변수 후)
- ✅ 랜딩 가입 진범 #2 봉합 (tenants.biz_no NULL → '')
- ✅ 백오피스 사이드바 W11/W9/W14 6개 메뉴 추가
- ✅ 백오피스 V2 안내 모드 잔여 보강 (2개)

### 1.2 미완 보충 영역 (EXE 빌드 전 결재 필요)
- ⚠️ Cloudflare 환경변수 (CLOUDFLARE_API_TOKEN·ZONE_ID·ACCOUNT_ID) 미설정
- ⚠️ HITPAN_BO_MFA_KEY 환경변수 = 본 세션 User scope 등록, Machine scope 미적용
- ⚠️ ERP 부트스트랩 토큰 실제 흐름 통합 실측 미진행 (W12 e2e는 화면 로드만)
- ⚠️ 백오피스 안내 모드 화면 17개 잔존 (W11/W9 외 영역 — Billing·Commission·Promotions 등)
- ⚠️ 사이드바 외 다른 사이드바 (BackofficeSidebar·ResellerSidebar) 미정합 점검

---

## 2. 결재 요청 옵션

### A. 즉시 GO — 본 세션 보충본 그대로 EXE 빌드
- 장점: 빠른 베타 배포
- 위험: §1.2 미완 영역 고객사 환경에서 발견 시 핫픽스 필요

### B. 조건부 GO — Cloudflare 환경변수만 적용 후 EXE
- 장점: 도메인 자동 발급 영역 정합
- §1.2 다른 영역은 별도 차수

### C. 보충 후 GO — §1.2 미완 영역 모두 봉합 후 EXE
- 장점: 정식 완성도
- 시간: 추가 세션 2~3회

### D. 보류 유지 — 결재 영역 더 추가 검토 후 다음 결재
- 본 PM 판단 불가 영역 (사장님 직접 결재)

---

## 3. 본 PM 추천

**B (조건부 GO)** — Cloudflare 환경변수 + HITPAN_BO_MFA_KEY Machine scope 적용 후 EXE 빌드. §1.2 다른 미완은 정식 출시 전 별도 차수.

이유:
- W14 도메인 자동 발급은 EXE 배포 시 핵심 기능 — 환경변수 없으면 503 노출
- HITPAN_BO_MFA_KEY는 dotnet run 새 셸마다 재설정 필요 — Machine scope가 정식
- 안내 모드 화면 17개는 베타 출시 후 정정 가능 (사용자 영향 적음)

---

## 4. 결재 받으면 진행 흐름

1. 사장님 옵션 (A/B/C/D) 결재
2. (B 선택 시) `CLOUDFLARE_ENV_GUIDE_20260605.md` §3 사장님 직접 진행
3. 본 PM이 `build-installer.ps1` 실행 가능 결재 받음
4. 빌드 후 산출물 (EXE 위치·크기·체크섬) 보고
5. 사장님 서명·배포 결재

---

**결재 요청**: A / B / C / D 중 선택 + 추가 조건 있으면 명시 부탁드립니다.
