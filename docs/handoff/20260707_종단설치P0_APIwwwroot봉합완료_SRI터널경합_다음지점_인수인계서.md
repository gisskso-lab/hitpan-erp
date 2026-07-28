# 인수인계서 — 종단 설치 P0: API wwwroot 봉합 완료 / SRI 터널 경합 미해결 / 다음 지점

- **작성**: PM (브라운킴), 2026-07-07 새벽 (사장님 퇴근, 커밋·인수인계 미리 결재)
- **세션 성격**: 종단 설치 테스트 (외부환경 첫 통과 시도) — 6번째·7번째 벽 연속 규명
- **커밋**: `6b23258` (W1~W4 봉합 + 작지서 v1·v2)
- **관련 메모리**: [[project_ops_workspace_priority_20260706]] [[project_reinstall_p0_login401_20260704]] [[feedback_dev_pc_proves_nothing]]

---

## 0. 사장님 목적 (절대 망각 금지)

> **"당장 test1이 돌아가는 게 목적이 아니다. 앞으로 이런 일이 안 일어나도록 봉합하는 게 목적이다."**
> **"개발환경 PC 정상작동은 큰 의미 없다. 터널 경유 종단이 진짜."**

- test1/test2 = 테스트 계정(Sandbox 휘발성). 봉합 대상 = **정본 빌드/코드** (실고객 EXE).
- 완료 기준 = **깨끗한 Sandbox → 터널 URL로만 → setup→부모계정→로그인→6단계 오류·끊김 0** 실측 로그. (개발PC·localhost 정상 = 증명 0)

---

## 1. 오늘 세션 전체 흐름 (요약)

Sandbox 백지환경에 EXE를 설치해 터널로 종단 테스트하며 **벽을 연속 규명**:

| 벽 | 진범 | 상태 |
|---|---|---|
| ①~④ (7/4밤~) | 터널 EventLog잔재·db.conf키·/api/setup화이트리스트·DB생성 | ✅ 봉합됨(이전 세션) |
| ⑤ (오늘 저녁) | EXE 1.2.24 Web WASM에 LicenseSetupPage 실종(빌드크래시) | ✅ 봉합(1.2.25, Web완전성게이트) |
| ⑥ (오늘 밤) | **API wwwroot 옛 WASM(5/20) — 터널=5257 직결이라 그게 서빙됨** | ✅ 봉합(1.2.26, W1~W4) |
| ⑦ (새벽, 미해결) | **터널 SRI 실패 — wasm 5909↔6933 비결정적 (터널 origin 경합)** | 🔴 내일 |

---

## 2. ⑥번 벽 = 오늘의 핵심 봉합 (완료, 커밋 6b23258)

### 진범 (실측 3중 확정)
- **터널 test1.hitpan.kr = 5257(API) 직결** (`/health` 200=API전용·web-server5234엔 없음 / ingress 코드값 `http://localhost:5257` InstallerBootstrapController.cs:133, CloudflareDomainService.cs:259 / 터널WASM바이트=API옛것 일치)
- 실고객은 **오직 api\wwwroot 에서 화면(WASM)을 받음.** web-server.ps1(5234)=로컬 전용.
- 빌드가 web\wwwroot만 최신화, **api\wwwroot는 소스트리 옛 잔재(5/18~20, setup 라우트 없음) 그대로 실음** → 터널 setup NotFound → 부모계정 불가 → 로그인 401.

### 봉합 방향 역전 (중요 — 받아쓰기 방지 교훈)
- **초기안 v1 = "API가 WASM 서빙 제거" → P0 오답.** 터널=5257 직결이라 제거하면 실고객 백지 크래시.
- **실측이 뒤집음.** 4인 매니저 만장일치 "제거 아니라 갱신". **설계·검증팀장 자기 제거안 정직 철회, CTO도 v1 조건부승인 철회.** 네트워크매니저(마이클)만 처음부터 "파일 최신화, 포트 손대지마"로 일관 = 맞음.
- **교훈**: PM이 "API 서빙 제거"로 크게 틀리게 잡았고 CTO도 그 위에서 결재. 실측(터널=5257) + 마이클 반박이 4인 전원 바로잡음. SoD 검증이 작동한 증거.

### W1~W4 봉합 내용 (build-installer-universal.ps1)
- **W1**: Web publish 후 `web/wwwroot → api/wwwroot` 복사(갱신). 터널·ingress·포트 무손상(헌법#29).
- **W3**: web==api WASM SHA256 일치 강제 게이트, 불일치 exit1 (CTO조건, 2벌 드리프트 차단).
- **W2**: API publish 전 `bundle/api` 청소 + **소스 `src/HitPan.API/wwwroot` 삭제**(진짜 재오염원) + `.gitignore` 등재.
- **W4**: 리포 백업잔재 삭제 (api-wwwroot-backup-20260526 + 착수중 추가발견 api-backup-*·hitpan-apiwwwroot 합 500MB, 전부 git미추적·빌드미참조).

### 검증 완료 (정합 확인)
- EXE 1.2.26 재빌드: W1 복사·W3 게이트 통과 로그 실증.
- 설치 디스크 `api\wwwroot\System.ObjectModel.wasm` = **5909(새것)**, boot.json 새것.
- **API(localhost:5257) 직접 서빙 = 5909(새것) 정상.**
- 소스 wwwroot 삭제 후 API 빌드 0/0.

---

## 3. ⑦번 벽 = 미해결 (내일 최우선)

### 증상
- 1.2.26 깨끗한 Sandbox 설치 → 터널 test1.hitpan.kr/setup/license 접속 → **setup 화면 로딩됨(NotFound 사라짐 = ⑥봉합 성공)** → 그러나 **SRI 무결성 검증 실패** (`System.ObjectModel.wasm` integrity 불일치 → 브라우저 차단 → "오류가 발생했습니다").

### 🔴 PM 오판 정정 (사장님이 잡음 — 받아쓰기 직전)
- **PM이 "Cloudflare 엣지캐시"로 단정 → 틀림.** 사장님 "캐시라고 단정말고 구조적 문제인지 정확히 파악" + "너네 가정이 틀린거잖아".
- **실측 반증**: 같은 URL 5회 = `5909,5909,6933,6933,5909` **비결정적**(캐시면 일관돼야 함). `?cb=` 캐시버스팅도 6933 나옴 = **캐시 절대 아님.**
- **디스크·API직접(5257) = 5909(새것) 정상인데 터널만 5909↔6933 랜덤.**

### 진짜 진범 (구조적)
**cloudflared 터널에 origin 2개(새 5909 = 지금 Sandbox + 옛 6933 = 좀비 커넥션)가 붙어 요청마다 라운드로빈 경합.** 네트워크매니저(마이클) 초기 경고 = 남은벽 **e "1 tenantCode ↔ 여러 origin, 비결정적 라우팅"**. 이전 Sandbox 종료했지만 그 터널 커넥션이 Cloudflare 엣지에 좀비로 살아 새 Sandbox와 `hitpan-t001` 터널 공유.

### 사장님 정공법 (캐시 퍼지 거부, 더 정확한 방법)
- **test1 삭제 안 하고 test2 새 가입** — 터널명 = `hitpan-{tenantCode}`(CloudflareDomainService.cs:182) → test1=hitpan-t001, **test2=hitpan-t002 물리 분리** → test1 좀비 경합과 무관.
- **🔴 그런데 test2도 401 뜸** — 진범이 test1 터널 경합만이 아닐 수 있음(구조적 or 봉합 미완).

### test2 실측 확정된 것
- 백오피스: **T-002 "테스트2" domain_alias=test2, active·is_locked_from_landing=0·serial_verified=0** (부모계정 없는 갓 가입 상태, setup 검증 딱 맞음).
- 도메인 = **test2.hitpan.kr**, 새 터널 hitpan-t002.

---

## 4. ▶️ 내일 최우선 (한 벽씩, 검증팀장 방식)

### 4-1. test2 401이 어느 단계인지 실측 (진범 판별의 핵심)
test2.hitpan.kr 로 종단 시도하며:
1. **setup 화면이 뜨나?** (터널로 test2.hitpan.kr/setup/license)
   - 안 뜨거나 SRI 실패 → wasm 문제 지속
   - 뜨면 → wasm 정상, 401은 다른 단계
2. **test2 wasm 일관성 실측** (서버측, PM이 가능):
   ```
   for i in 1..5: curl test2.hitpan.kr/_framework/System.ObjectModel.wasm → 풀어서 크기
   ```
   - **일관되게 5909** → 진범 = test1 좀비 커넥션(hitpan-t001만 오염). ⑥봉합 정상, 실고객 무관. test2로 종단 계속.
   - **5909↔6933 랜덤** → 구조적 터널 발급/커넥션 관리 결함 = 실고객도 겪음. 이게 진짜 벽.

### 4-2. 만약 구조적이면 (터널 origin 경합)
- cloudflared 터널이 왜 origin 2개를 물고 라운드로빈하는지 = 관리형 터널(config_src=cloudflare) 커넥션 정리 로직 규명. 재설치/재기동 시 이전 커넥션 무효화 안 되는 구조 확인.
- 네트워크매니저(마이클) 주도. "1 tenantCode ↔ 1 활성 오리진" 불변식 강제 필요.

### 4-3. SRI 자체가 실고객 업데이트 위험인지 (별개 근본)
- Cloudflare/터널이 `.wasm`을 캐시하거나 변형하면 **업데이트 배포 시 실고객도 boot.json(SRI)과 어긋나 깨질 수 있음.** ⑦ 뚫은 뒤 이 근본 확인.

### 4-4. SRI 통과 후 남은 벽 a~e (순차 표면화 예정)
- a: create-parent BOOTSTRAP_TOKEN_KEY 정합 (+ CTO 추가: `TenantMiddleware /api/setup` 화이트리스트 회귀 재확인, 커밋 47b4a02·fa3f03f 손댐)
- b: 로그인 후 회사정보 자동반영(local_company)
- c: **6단계 clean DB 시드 500** (재끊김 확률 최고, W5 스모크가 사전 차단)
- d: 재설치 터널 secret 복구(헌법#28) 백지PC 발화
- e: 2대째/재설치 터널 name 재사용 UUID 경합 (= ⑦의 근본과 동일 뿌리)

---

## 5. P1 백로그 (CTO 지목, 종단 통과 후 별건)

- **정공법(one-served-copy)**: API가 HitPan.Web을 ProjectReference → web publish 산출물 자동 편입 → **web-server.ps1(5234) 폐기, 2벌 wwwroot → 1벌.** (실측: HitPan.Web은 API 참조 안 함=순환 없음). 즉효(복사)+W3게이트는 2벌 드리프트를 "막는" 것이지 "제거"가 아님. 근본 제거 = 1벌 통일.
- **W5 종단 스모크 게이트**: 3흐름(매입/판매/BOM 확정)+컬럼 정합. 6단계 clean DB 500 사전 차단. 종단 직후 즉시.

---

## 6. 환경·자산 정보

- **NCP 접속**: `ssh -i C:\Users\소순근\Downloads\hitpan-key.pem root@211.188.58.140` (root)
- **백오피스 DB**: hitpan_backoffice (NCP). T-001 test1, T-002 test2.
- **EXE**: `C:\HitPanTest125\HitPan-ERP-Setup-1.2.26.exe` (격리 폴더, W1~W4 담김)
- **.wsb 매핑**: `installer/sandbox-test.wsb` → `C:\HitPanTest125`(1.2.26만) → `C:\hitpan-test`
- **읽어주세요**: `C:\HitPanTest125\읽어주세요.txt` (1.2.26 종단용)
- **시리얼**: test1=`HITP-4ZQK-NEVE-2MCJ-WDJT` / test2=별도 발급(사장님 손, DB엔 해시만)
- **Sandbox**: 지금 켜져 있을 수 있음. 내일 깨끗한 재현 위해 껐다 새로 켜기 권장.
- **봉합 재빌드 명령**: `.\installer\build-installer-universal.ps1 -Version 1.2.xx -BackofficeApi https://back.hitpan.kr -SkipBundleDownload`

---

## 7. 정직한 현재 완성도 평가 (받아쓰기 금지, 의사결정용)

- **⑥번 봉합(API wwwroot 최신화) = 완결·정합.** 빌드·설치·API 직접 서빙 전부 5909 실측 확정. 커밋됨.
- **종단은 아직 미통과.** ⑦(SRI/터널 경합)에서 막혔고, 그 뒤 a~e도 미검증.
- **"봉합했다 ≠ 종단 통과"** (검증팀장 못박음). 완료는 터널 경유 종단 실측 로그뿐.
- 오늘 성과 = **⑤⑥ 두 벽 봉합 + 빌드 재발방지 게이트 2개 신설(Web완전성·web=api정합)** + ⑦ 진범을 "캐시"에서 "구조적 터널 경합"으로 정정(사장님이 받아쓰기 차단).
- **내일 = test2 401 실측으로 ⑦이 test1일회성이냐 구조적이냐 판별이 첫 단추.**
