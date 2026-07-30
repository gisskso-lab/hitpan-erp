# 작업지시서 v2 (전면 재작성) — API wwwroot 최신화 + 빌드 정합 + 실고객 종단 재발 방지

- **문서번호**: 20260706작-v2 (v1 폐기 — W1 방향 실측으로 뒤집힘)
- **작성**: PM (브라운킴)
- **일자**: 2026-07-06 심야 → 07-07
- **근거**: 4인 매니저 2라운드 + 실측 되먹임 3라운드 재정렬 + CTO 결재 + 5인 실측
- **SOP 단계**: [3] 재작성 작업지시서 → **[4] CTO 재결재 → [5] 사장님 승인** 후 [6] 착수
- **헌법 정합**: #33·#36·#39·#29(인프라 손대지 않음)·#20·#19

---

## 0. 사장님 목적 (절대 망각 금지)

> **"당장 test1이 돌아가는 게 목적이 아니다. 앞으로 이런 일이 안 일어나도록 봉합하는 게 목적이다."**

- test1 = 테스트 계정(Sandbox 휘발성). 실사용 아님. **봉합 대상 = 정본 빌드/코드/잔재** (실고객 EXE).
- 테스트 의미 = 실고객 종단 전체 오류·끊김 0: ①랜딩 가입→백오피스 자동연결 ②터널 ERP 접속→setup→부모계정→로그인 ③6단계 기능 전부.

---

## 1. ⚠️ v1 폐기 사유 (봉합 방향 실측으로 역전)

**v1 작업지시서는 "API가 WASM 서빙 안 하게 제거(W1)"였다. 이는 P0 사고를 유발하는 오답으로 확정되어 폐기한다.**

### 역전시킨 결정적 실측 (터널 = 5257 API 직결, 3중 확정)
| 실측 | 결과 | 의미 |
|---|---|---|
| 터널 `GET /health` | **200 (JSON)** | HealthController(API 전용). web-server.ps1(5234)엔 /health 없음(SPA fallback→HTML) → **API 직결 증거** |
| 터널 `GET /api/health` | 401 | API 인증 미들웨어 처리 → API 직결 |
| 터널 WASM 바이트 | 3,197,209 = **localhost:5257(API 옛것)과 일치** | 터널이 API wwwroot를 서빙 |
| ingress 코드값 | `http://localhost:5257` (InstallerBootstrapController.cs:133, CloudflareDomainService.cs:259) | 터널 origin = API |

### 결론: API가 WASM을 서빙하는 것은 정상(필수)
- **터널로 들어오는 실고객은 오직 `api\wwwroot`에서 화면(WASM)을 받는다.** web-server.ps1(5234)은 **로컬(메인PC 브라우저) 전용**, 터널로 안 감.
- **W1(API의 UseBlazorFrameworkFiles/MapFallbackToFile 제거)을 하면 → 터널 실고객이 화면을 못 받아 백지 크래시 = 헌법 #20 P0 사고.**
- 4인 재정렬 결과: 설계팀장·검증팀장 자기 이전 소견(제거안) **정직 철회**. 네트워크 매니저(갱신안) 일관 유지. **4인 만장일치 = 제거 아니라 갱신.**

---

## 2. 진범 (실측 확정)

**API가 서빙하는 `api\wwwroot`가 2026-05-18~20자 옛 WASM(3,197,209B, setup 라우트 없음)인데, 빌드가 이를 최신화하지 않는다.**

| 근거 | 파일:라인 / 실측 |
|---|---|
| API가 WASM 서빙(정상) | `src/HitPan.API/Program.cs:355~372, 392~395` (hasBlazor→서빙) |
| **소스트리 옛 wwwroot 존재** | `src/HitPan.API/wwwroot/_framework/HitPan.Web.wasm` = 5/18~20 옛것. **git 추적 0건**(순수 로컬 잔재, CTO 실측 확인) |
| 빌드가 최신화 안 함 | `installer/build-installer-universal.ps1` = web publish→api\wwwroot 복사 루프 **0건** |
| publish 청소 안 함 | `dotnet publish -o "$BundleDir/api"`(L97)가 옛 wwwroot 잔재 그대로 실음 |
| 설치본 반영 | `HitPan-Universal.iss:88` = `bundle/api/*` 통째 복사 |
| **결정타** | bundle/api: dll=7/6(새것), wwwroot=5/20(옛것) |
| 리포 잔재 | `installer/api-wwwroot-backup-20260526/`(82MB·1322파일·git 미추적) |

**정합 원리**: 터널=5257 직결 확정이므로 **API가 화면 서빙의 정본 진입점**이다. web-server(5234)는 로컬 전용. 따라서 api\wwwroot를 최신 WASM으로 정합시키면 된다. **터널·ingress·포트는 손대지 않는다**(헌법 #29, 통신 무결성 무손상).

---

## 3. 봉합 범위

### W1. API wwwroot 최신화 (핵심) — 2안 중 CTO/사장님 택일

**공통 목표**: EXE 안 `api\wwwroot` = web publish의 최신 WASM(setup 라우트 담김)이 되게 한다.

- **안 W1-즉효 (빌드 복사)**: `build-installer-universal.ps1`에서 Web publish 후 그 산출물(`bundle/web/wwwroot/*`)을 `bundle/api/wwwroot/`에 **덮어쓰기 복사**. + 소스트리 `src/HitPan.API/wwwroot` 삭제 + `.gitignore` 등재.
  - 장점: 빌드 스크립트 소수 줄, 즉시. 단점: "복사"라 빌드 스크립트가 진실원(구조 아님).
- **안 W1-정공법 (ProjectReference)**: `HitPan.API.csproj`가 `HitPan.Web`을 ProjectReference → `UseBlazorFrameworkFiles`가 web publish 산출물을 API wwwroot에 자동 편입. + 소스트리 정적 wwwroot 삭제.
  - **실측: HitPan.Web은 API를 참조 안 함(Contracts만) → 순환참조 없음. 정공법 안전.**
  - 장점: 프로젝트 구조가 진실원, 매 빌드 자동 정합(드리프트 원천 제거). 단점: 구조 변경 회귀 위험(빌드·DI 영향 전수 필요).
  - **설계팀장·풀스택 지목 = 근본안.** web-server(5234)·2벌 wwwroot 폐기까지 가면 one-served-copy 완성.

> **PM 권고**: 즉효(복사)로 이번 종단을 먼저 뚫고, 정공법(ProjectReference+5234 폐기)은 종단 통과 후 별도 구조 작업으로. 단 CTO 판단 우선.

### W2. 빌드 산출물 청소 — publish 전 api 폴더 청소
- `build-installer-universal.ps1:97` API publish 직전 `Remove-Item -Recurse -Force "$BundleDir/api"`. + **소스트리 `src/HitPan.API/wwwroot` 삭제**(CTO 지목 진짜 재오염원) + `.gitignore` 등재.

### W3. api\wwwroot 정합 FAIL 게이트 (헌법 #36 Web판 대칭)
- publish·복사 후 `bundle/api/wwwroot/_framework/HitPan.Web.wasm`이 **최신(web publish와 동일 바이트/해시)**인지 실측. 옛것이거나 없으면 **FAIL 출시 차단**.
- (기존 Web 완전성 게이트 L104~117과 대칭. 이번엔 "있으면 FAIL"이 아니라 "web과 일치해야 통과"로.)

### W4. 리포 잔재 삭제
- `installer/api-wwwroot-backup-20260526/`(82MB·1322파일·git 미추적) 삭제.

### W5. 종단 스모크 게이트 (별건 — CTO 승인대로)
- 3흐름(매입/판매/BOM 확정) + 컬럼 정합 스모크. W1~W4와 결합도 낮아 별건. **단 종단 직후 즉시 착수**(6단계 clean DB 500 = 재끊김 확률 최고).

---

## 4. 완료 기준 (봉합 커밋 ≠ 완료 — 검증팀장 못박음)

**개발PC·로컬 5234 정상은 증명 0 ([[feedback_dev_pc_proves_nothing]]). 오직 터널 경유 종단 실측 로그만 완료.**

1. 정본 재빌드 → **깨끗한 Sandbox**(새로 켠 백지) EXE 한 방 설치
2. **터널 URL(test1.hitpan.kr)로만** (localhost 금지):
   - ① 받은 WASM = 방금 publish 새것(바이트/해시 대조, 옛것 배제) → **setup 화면 표시**
   - ② 부모계정 생성 201 + DB users 1행
   - ③ 로그인 200 + `/me` 복호화 200(회사정보 자동반영)
   - ④ 6단계 각 1개(설정·마스터·매입·판매·현황·재무) 200 + 화면 렌더
3. **SoD**: 검증팀이 Sandbox 종단 로그 독립 재현·첨부한 것만 종합보고. 로그 없는 "통과"=반려.
4. **한 벽씩**: 벽 봉합→즉시 터널 종단 실측→다음 벽 (일괄 후 종단 금지=두더지잡기 재발).

---

## 5. 갱신 후 남는 벽 (이번 W1~W4로 안 풀림 — 종단서 순차 표면화)

지금까지 옛 WASM이 서빙돼 아래 화면들이 **도달조차 못 했다.** 최신화하면 비로소 터널 경유로 실행돼 드러남:

| # | 벽 | 지목 |
|---|---|---|
| a | create-parent 토큰키(BOOTSTRAP_TOKEN_KEY) 정합 | 풀스택 (fa3f03f가 최근 손댐) |
| b | 로그인 후 회사정보 자동반영(local_company) | 풀스택 |
| c | **6단계 clean DB 시드 500** (재끊김 확률 최고) | 설계팀 (W5가 사전 차단) |
| d | 재설치 터널 secret 복구(헌법 #28) 백지PC 발화 | 네트워크 |
| e | 2대째 설치 터널 name 재사용→UUID 경합 | 네트워크 ("1 tenantCode↔1 활성 오리진" 불변식 미강제) |

→ 각 벽 종단서 드러나면 개별 근본 봉합.

---

## 6. 착수 금지 조항

- **[4] CTO 재결재 → [5] 사장님 승인** 전 코드 1줄도 안 댐 (헌법 #33·SOP).
- **터널 ingress·포트 변경 금지** (5257 유지). 이걸 5234로 바꾸는 안B = 헌법 #29 인프라조작 + SPOF + 기존 터널 재PUT 필요 = 4인 기각.
- test1 인스턴스 손수술(대시보드·수동교체) 금지 = 거짓봉합.
- 빌드·재빌드는 깨끗한 환경. 오늘 아침 크래시(불완전 WASM 출하) 재발 방지 = Web 완전성 게이트 + W3 api 정합 게이트 이중 차단.

---

## 7. PM 종합 판단 + CTO 재결재 요청

**핵심 반전**: v1의 "API 서빙 제거"는 터널=5257 직결 실측으로 **P0 사고 오답 확정, 폐기.** 진짜 봉합 = **API wwwroot를 최신 WASM으로 갱신**(제거 아님), 터널·포트 불변. 4인 만장일치.

**CTO 재결재 요청 사항**:
1. W1을 **즉효(복사)로 먼저 종단 뚫고 정공법(ProjectReference)은 별건**으로 — 이 PM 권고 승인 여부. (즉효로 이번 사장님 목적=종단 통과를 빠르게 확인 후, 구조 정공법은 회귀 위험 큰 별도 작업)
2. W2에 소스트리 `src/HitPan.API/wwwroot` 삭제 + .gitignore 포함 확인.
3. W3 정합 게이트(web=api wwwroot 동일 해시) 방식 승인.
4. W5 별건 분리 + 종단 직후 즉시 착수 조건 재확인.
5. 남은 벽 a~e 종단 순차 대응 방침.
