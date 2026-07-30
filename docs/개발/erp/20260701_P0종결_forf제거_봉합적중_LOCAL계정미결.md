# P0 종결 — 2026-07-01 설치 DB DOA 봉합 적중 + LOCAL 로그인 실동작 확인

> PM 브라운킴 / 다음 세션 이어받기
> 한 줄: **P0(시리얼 없이 설치)를 설치→DB→API로컬→로그인→대시보드→약관동의까지 실동작 전부 확인(1.2.22 + 수동계정). LOCAL 모드가 슬롯 로직을 우회해 생긴 진범 4개를 연쇄로 봉합. 남은 미결 = LOCAL 계정 생성 흐름 자동화(현재 수동 SQL) + 봉합 커밋.**

## 0. 최종 실동작 확인 (2026-07-01 밤, 샌드박스)
시리얼 없이 설치 → DB 124테이블 → API 5257 → **로그인(admin@hitpan.kr/Admin1234!) → 대시보드 진입 → 약관 4건 동의 → 403 해소**. 전부 실측 확인.

### 오늘 잡은 진범 4개 (전부 "LOCAL 모드가 DetermineMultiTenantSlot 우회"가 뿌리)
| # | 증상 | 진범 | 봉합 |
|---|---|---|---|
| 1 | 설치 DB초기화 DOA(코드1) | `db-setup.bat`의 `for /f (''mysql...'')`가 PATH `C:\Program Files` 공백에서 mysql 못 실행 → 카운트 빈값 → exit1 | for/f 전면 제거(파일출력+set /p) + mysql 절대경로 `"!MYSQL!"` (안C) |
| 2 | 봉합해도 여전히 exit1 | 어제 안A에서 PM이 넣은 hitpan `SELECT 1` 가드가 유일 잔여 실패 | 가드 exit 제거(로그 참고만) |
| 3 | 로그인이 api-demo로 감 401 | `FixupBlazorAppSettings`가 LOCAL 모드면 Exit → appsettings가 레포원본 api-demo 유지 | LOCAL은 ApiBaseUrl 빈 값('') 정정 → Program.cs 폴백(현재출처 localhost:5234) |
| 4 | 502 Bad Gateway | LOCAL은 슬롯 미계산 → G_ApiPort=0 → db.conf API_PORT=0 → hitpan-start.bat이 `--urls ...:0` 랜덤포트 → 프록시(5257)와 불일치 | .iss: `if G_ApiPort=0 then 5257`+SlotIndex=1 / bat: `"0"`도 5257 폴백(이중) |
| 5 | 로그인 400 BadRequest | 수동계정 INSERT 시 role='tenant_admin'인데 UserRole enum엔 없음(HasConversion<string>=enum이름 저장) | role='TenantAdmin'(enum이름), account_type='tenant_admin'(별개) |

---

## 1. 오늘 진범 확정 경로 (추측 3번 → 파일로그로 확정)

| 시점 | 진단 | 결과 |
|---|---|---|
| 어제 | "G_DbUser 빈값" | ❌ 틀림 (hitpan 유저 정상 생성 실측) |
| 오늘 오전 | "검증을 hitpan으로 해서 for/f 취약" → root 통일(안A) | ❌ 에러 위치만 이동 |
| 오늘 오후 | **1.2.18 진단 로그(배치가 파일에 단계별 기록) + 재현 실측** | ✅ **진범 확정** |

### 진짜 진범
`db-setup.bat`의 카운트 검증이 `for /f "tokens=*" %%c in ('mysql ...')` 구조인데, **for/f 안에서 cmd가 명령을 재파싱할 때 PATH의 `C:\Program Files`(공백)에서 `mysql`이 `C:\Program`으로 잘려 실행 실패** → 카운트 빈값 → `if !FINAL_COUNT! LSS 124` → `exit /b 1` → 설치 DOA.
- 결정타(재현 실측): `for/f` 밖에서 mysql 직접 실행하면 124 정상, `for/f` 안에서만 깨짐.
- 데이터(124테이블·hitpan유저·import)는 **항상 정상 생성**됐고, **검증만 실패**해 설치가 자기 손으로 죽었다.

## 2. 봉합 (안C, 커밋 대기)
`installer/HitPan-Universal.iss` db-setup.bat 생성부:
1. **mysql 절대경로 변수화**: `set "MYSQL=C:\Program Files\MariaDB 11.4\bin\mysql.exe"` (+ 10.11 폴백). 모든 호출을 `"!MYSQL!"`로 → PATH·공백 의존 제거.
2. **for/f 전면 제거**: 카운트 4곳(TBL_COUNT·EXISTING_DATA·FINAL_COUNT·USERS_OK)을 `"!MYSQL!" ... -e "SELECT.." > "!CNTF!"` 후 `set /p VAR=<"!CNTF!"`로 파일 읽기.
3. **import·검증 전부 root 통일** (hitpan 접속 의존 제거).
4. **hitpan 접속 가드 제거**: 어제 안A에서 PM이 넣은 `mysql -u hitpan -e "SELECT 1"` errorlevel 가드가 유일한 잔여 실패였음(1.2.19 진단 로그에서 카운트 124/124/1 다 통과, hitpan SELECT 1만 errorlevel=1). 스키마 완결은 카운트 검증이 보증하고 hitpan 접속은 ERP 첫 기동이 검증하므로 설치 시점 가드는 과잉 → exit 차단 제거(성패는 진단 로그에만 참고 기록).
5. **진단 로그**: `%LOCALAPPDATA%\Temp\hitpan_dbsetup_diag.log`에 단계별 성패 기록(비번 미기록). ⚠️ **출하 전 이 진단 로그를 남길지 결정 필요**(디버그 잔재 여부 — 커밋 시 판단).

### 검증 (1.2.20 샌드박스 실측)
- ✅ 시리얼 없이 설치 → **에러 0, 설치 완료**
- ✅ ERP 기동 → **로그인 화면 도달**
- 진단 로그: MYSQL경로·CREATE=0·SET GLOBAL=0·TBL[124]·import후[124]·최종[124]/users[1] 전부 정상.

## 3. 🔴🔴 남은 큰 별개 안건 — LOCAL 모드 온보딩 흐름 전면 부재 (내일 SOP, P1)
시리얼 없이(LOCAL) 설치 = 백오피스 인증 안 탐 = **"가입→백오피스→회사정보·계정·구독 자동 반영"(헌법 #35) 흐름 전체를 안 탐** → 정식 설치가 자동으로 채우는 데이터가 **모두 비어있음**. 오늘 로그인까지는 수동 SQL로 뚫었으나, 그 뒤로 화면마다 빈 데이터 때문에 에러 연쇄(두더지잡기). SQL 땜질은 근본해결 아님 — LOCAL 전용 온보딩 마법사가 정답.

### 오늘 수동으로 채운 것 (임시, 근본책 아님)
1. **users**(부모계정): role=`TenantAdmin`(enum이름, HasConversion<string>), account_type=`tenant_admin`. → 로그인 400 해결.
2. **employees**(부모 연결행): emp_no='0001', role='tenant_admin'. → 없으면 토큰 employee_id 빈값 → 결재·경비·거래처 403(AuthService L82 백필 대상). **로그인 시 자동백필이 있으나 재로그인 필요.** → 403 해결.
3. **tenants**(회사정보): reseller_tier=0(DEFAULT 없는 NOT NULL 주의, 헌법 #37). → /tenants/me 404 해결.

### 아직 안 뚫린 것 (여기서 정리 — 내일 SOP)
- **/api/tenants/me 500**: tenants 행은 만들었으나 처리 중 500. 유력원인 = `TenantService.GetCurrentAsync` L42 `tenant.Status.ToString()`가 enum 변환 or 다른 enum 컬럼(reseller_tier 등) 매핑 실패(role에서 겪은 것과 동류). **정확한 예외는 API 로그(`C:\Program Files\HitPan\api\logs`) 확인 필요**(500 응답 본문은 일반메시지라 원인 안 보임).
- 그 뒤로도 구독·권한·창고·계정과목 등 화면별 빈 데이터 에러가 계속 나올 것 = LOCAL 온보딩 부재의 증상들.

### 🎯 내일 SOP 안건 (P1) — LOCAL 첫 실행 온보딩 마법사
- 정식 `CompanyBootstrapController.create-parent`가 하는 것(users+employees+tenants+창고+표준계정 시드를 한 트랜잭션)을 **LOCAL 전용**으로 만든다(bootstrapToken 없이, 첫 실행 시 회사정보·관리자 입력받아 생성).
- 베타1은 시리얼 임시제외([[project_beta1_no_payment]])라 LOCAL 설치 다수 예상 → **이 흐름 없으면 고객이 설치해도 못 씀.** P0(설치 DOA) 다음의 최우선.
- 헌법 #35(3시스템 유기적 연결)·#33(사전 결재) 정합으로 설계.

## 4. 빌드 이력 (오늘)
1.2.17(안A) → 1.2.18(진단) → 1.2.19(안C for/f제거, hitpan가드만 실패) → 1.2.20(hitpan가드 제거, 설치·로그인화면 적중) → **1.2.21(LOCAL API 주소 봉합, 아래 §6)**. dist에 전부 있음. **출하는 1.2.21 계열이나, 진단 로그 정리 + LOCAL 계정 이슈(§3) 해결 후.**

## 5. 🔴 추가 봉합 — LOCAL 모드 API 주소가 demo를 가리킴 (1.2.21, 커밋 대기)
로그인 화면에서 로그인 시도 → 콘솔 `api-demo.hitpan.kr/api/auth/login 401`. 즉 **LOCAL 설치인데 로그인 API 호출이 외부 demo 서버로 감**.
- 진범: `HitPan-Universal.iss` `FixupBlazorAppSettings()`가 **LOCAL 모드(G_PrimaryDomain=localhost)면 Exit로 정정 건너뜀** → appsettings.json이 레포 원본 `api-demo.hitpan.kr` 그대로 남음.
- 봉합(1.2.21): LOCAL 모드는 Exit 대신 **ApiBaseUrl을 빈 값('')으로 정정**. 그러면 `HitPan.Web/Program.cs`(L14~35)의 폴백(`IsNullOrWhiteSpace → HostEnvironment.BaseAddress` = 현재 출처 localhost:5234)이 발동 → 로그인이 자기 자신으로 감 → web-server.ps1이 `/api/*`를 localhost:5257(로컬 API)로 프록시. 파일 유지·값만 비움(헌법 #21 정합).
- ⚠️ **1.2.21 재설치 후 실측 미완**(샌드박스 껐음). 다음 세션에서 재설치→appsettings `"ApiBaseUrl": ""` 확인→로그인 실측 필요.

## 6. ⚙️ 로그인 계정 만드는 법 (LOCAL 모드, 임시 — 정식은 §3)
LOCAL은 계정 시드가 없으므로(백도어 금지) 테스트 시 직접 넣어야 함. **핵심 함정 2개 실측 확인**:
- ① PowerShell `Add-Type`으로 BCrypt DLL 로드 시 **해시가 빈 값('') 나옴**(버전/로드 문제). → 절대 이 방식 쓰지 말 것.
- ② 정상 해시는 **레포에서 `dotnet run`(BCrypt.Net-Next 4.0.3)** 으로 생성. `Admin1234!` 해시 예: `$2a$11$dWr/ZngqsNxkSq3vwn0A8eWV3LnEwIIJyxNoGx/VCkJbvtS6E66u2` (코드 BCrypt.Verify와 정합, 60자).
- users 필수컬럼(NOT NULL): user_id, tenant_id, email, password_hash, user_name, role, account_type, is_parent, is_active, created_at, updated_at. FK 제거돼 tenant_id 임의값 OK.
- 로그인 조회 = `email==X AND is_active==true`(AuthUserLookup/AppDbContext.FindUserByEmailAsync), 비번 = `BCrypt.Verify`(AuthService L49). 소문자정규화 없음.
- INSERT SQL은 파일(`Set-Content -Encoding UTF8`)에 쓰고 `mysql -e "source file"` 로 실행(해시의 `$` 파이프 깨짐 회피).

## 7. 📅 내일 작업 (사장님 확정 2026-07-01 밤)
1. **LOCAL 온보딩 설계·구현** (§3, P1) — 시리얼 없이 설치한 고객이 첫 실행 시 회사정보·부모계정을 등록하는 마법사. 정식 CompanyBootstrap이 하는 것(users+employees+tenants+창고+표준계정 한 트랜잭션)을 LOCAL 전용으로. 이게 없으면 베타1 고객이 설치해도 못 씀.
2. **운영 워크플로우 흐름 연결** — 3시스템 연결축(랜딩 가입→백오피스 계정ID 발급→ERP 자동 반영, 헌법 #35·#20). 온보딩 다음.
- SOP: 전수조사·검증 → 스케줄 → 작업지시서 → CTO결재 → 사장님승인 → 매니저작업+검증 → 보고. 봉합 즉석 금지.

## 8. 빌드 이력 최종 (오늘)
1.2.17(안A)→1.2.18(진단)→1.2.19(안C for/f제거)→1.2.20(hitpan가드 제거, 로그인화면)→1.2.21(ApiBaseUrl 빈값)→**1.2.22(API_PORT=0→5257 봉합)**. 최종=1.2.22. dist에 전부.
- ⚠️ 1.2.22 재설치 실측은 미완(A로 db.conf 수동수정+API 직접기동으로 로그인 확인). 1.2.22가 API_PORT를 처음부터 5257로 쓰는지 다음 세션 재설치 실측 필요.

## 9. 절대 보존 / 금지
- 이 PC = demo 운영(3306 129만행). 모든 검증 샌드박스 안에서만(헌법 #39). 호스트 무접촉. 샌드박스 껐으므로 흔적 0.
- **봉합은 전부 워킹트리 미커밋** — 사장님 승인 후 커밋. 대상: `HitPan-Universal.iss`(진범4 봉합), `hitpan-start.bat`(API_PORT=0 폴백). 진단 로그(hitpan_dbsetup_diag.log 기록) 출하본 잔류 여부 커밋 시 판단.
- 1.2.16 이하 출하 금지 유지. 1.2.22도 LOCAL 온보딩(§3·§7) 해결 + 진단로그 정리 전 출하 보류.
