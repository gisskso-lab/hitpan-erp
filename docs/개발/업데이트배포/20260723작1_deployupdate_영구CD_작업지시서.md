# 작업지시서 20260723작1 — 자동업데이트 영구 CD 파이프라인 (deploy-update.yml)

> 발행: PM 브라운킴 조율 → AI수석 PRD 작성 / 2026-07-23 / 브랜치 develop
> 성격: 신규 CI/CD 워크플로우 + NCP 서명 관여 = 인프라 코드 (헌법 #29·#33·#34 정합)
> **이 문서는 20260720작4를 처음부터 다시 쓰는 게 아니라, 현재 실측(webroot 버그·게시함정3·베타/정식 채널·재빌드 SSOT)으로 보강한 판이다.**
> 입력: `20260723_4인회의_CICD자동화_협의종합_PM조율.md`(뼈대) + `20260723_전수조사_CICD자동화_4인_종합보고.md`(현황) + `20260720작4`(승계 원본)
> **Claude 구현 최적화**: §4 재사용/불가 대조표(복붙 오판 차단) + §3.3 인자분기표(문장해석 대신 표 배선) + §5 보안 불변식 self-review 체크리스트.

---

## §0. 상태 · 결재선

### 결재 승계
- 20260720작4는 **[4] CTO 부분수정후승인 완료 → [5] 대표결재 대기** 상태였다. 본 작1은 그 안건을 승계한다.
- 승계 후 **보강분(아래 5건)이 새로 추가**됐으므로, 대표결재는 **보강분 재승인**을 포함해 다시 받는다.

### 작4 대비 보강 5건 (재승인 대상)
| # | 보강 | 근거 |
|---|---|---|
| 보강1 | webroot 기본값 **`/var/www/updates.hitpan.kr` → `/var/www/updates` 정정** (작4 §8 line8·B-4의 "publish 기본값 맞음"은 진실원 실측이 뒤집음) | 조율 §1-D, publish-update.sh:41 |
| 보강2 | **재빌드 방식 확정** (아티팩트 승계 아님) — "게시할 커밋=빌드한 커밋" SSOT 불변식 | 조율 §1-C |
| 보강3 | **G7 베타/정식 채널 파라미터 배선을 G1에 내장** (environment input=beta/production, webroot/feed-url/download-url-base 분기). 단 베타 피드 물리생성은 별건 | 조율 §2 |
| 보강4 | **게시함정3 흡수** (manifest 파일명 rename / webroot 정정본 / 채널 Major는 상류 주입) | 종합보고 §3 |
| 보강5 | **trufflehog EC PRIVATE KEY 룰 편입** (개인키 오커밋 최종 안전망) | 조율 §4, 백엔드 §4 |

### 결재란
- **CTO 결재**: ______________________ (일자: ______)
- **사장님 결재**: ______________________ (일자: ______)
- 결재 형식: OK / 부분수정후승인 / 반려 / 보류 (헌법 #33 4분기)

---

## §1. 목적 · 범위

### 목적
`git push`(또는 dispatch) 한 번으로 자동업데이트 zip·manifest 가 `updates.hitpan.kr` 에 서명·게시되는 **영구 CD 파이프라인** 완성. 이 손게시 불안정이 "UPDATE 실측 0회 통과"의 구조적 진범이다(종합보고 §0). 헌법 #34: 손 타는 배포는 앞으로 없앤다.

### 범위 (IN)
- `.github/workflows/deploy-update.yml` **신설** (build잡 + deploy잡).
- publish-update.sh **webroot 기본값 정정**(보강1) — NCP 상주본 갱신은 G-A 사장님 sudo(§2).
- ddl-smoke-test.sh **CI화 env 파라미터화**(G3, §6) — G1과 병렬 독립.
- trufflehog-config.yaml **EC PRIVATE KEY 룰 1개 추가**(보강5, §7).
- CODEOWNERS **신설**(G-E, §2).

### 범위 밖 (OUT — 별건 결재)
| 항목 | 왜 별건 | 근거 |
|---|---|---|
| 태그 자동 트리거 (push→자동게시) | 오배포 위험. G1 완주 후 실측으로 정책 확정 | G5, 조율 §4 |
| 베타 피드 **물리 생성**(nginx location·서브도메인) | DNS·인증서 = 헌법 #29 정면. G1은 파라미터 **배선만**, production만 실동작 | 조율 §2, 마이클 |
| 신규설치 EXE의 NCP 배포 자동화 | 별개 경로 | G2 |
| MetaPing 400 규명 (본사 버전 가시성) | 별도 작지서 | 종합보고 §4 |

---

## §2. 착수 게이트 (BLOCKING — 코드 전 사장님·NCP 선결)

> **아래 미충족 시 워크플로우는 `.disabled` 로 두고 활성화 금지**(작4 B-4 정신 승계). G-A·G-B·G-C는 **사장님 sudo 1회 세션으로 묶는다.**

| # | 게이트 | 현 상태 (실측) | 조치 | 결재 |
|---|---|---|---|---|
| **G-A** | publish-update.sh + sign-manifest.sh **NCP 상주 배치** (`/opt/hitpan/installer/updates/` — **동일 디렉토리 필수**: publish 가 `source $SCRIPT_DIR/sign-manifest.sh`, publish-update.sh:96·99) | `/root/` 임시 | 사장님 sudo 1회 (2파일 함께) | #29 1회 |
| **G-B** | **sudoers 화이트리스트** — 스크립트 1개·`--zip`/`--manifest` **2인자만 NOPASSWD**, 오버라이드(`--webroot`·`--private`·`--feed-url`·`--allow-republish`) 차단 | 없음 (현 광역 sudo) | 사장님 sudo 1회 (`/etc/sudoers.d/`) | #29 1회 |
| **G-C** | webroot 기본값 **`/var/www/updates.hitpan.kr` → `/var/www/updates`** 정정 (publish-update.sh:41) | 버그 | G-A 배치 시 **정정본 함께** 올림 | CODEOWNERS |
| **G-D** | manifest.json **no-cache 헤더** 확인 (`curl -I https://updates.hitpan.kr/manifest.json`) | **미확정 (실측 대기)** | 실측 → no-cache 없으면 조건부 nginx 별건 | 확인 / 조건부 #29 |
| **G-E** | `environment: production` **required reviewer** + **CODEOWNERS 신설** | 웹엔 environment만(deploy-ncp.yml:76), **CODEOWNERS 0건**(실측 확인) | 레포 설정 + 사장님 reviewer 등록 + CODEOWNERS 파일 | 레포 설정 |
| **G-F** | ddl-smoke-test.sh **CI화 선행** (MYSQL 경로:18 · DBUSER:21 파라미터화 + MariaDB 서비스 컨테이너) | 로컬 수동 전용 | 스크립트 env화 (하위호환 유지) | — |

**핵심 인식 (작4 CTO 처방 승계):** 개인키가 GHA 로 새는 게 진짜 위험이 아니다(서명은 NCP 안에서만 = 맞음). **진짜 위험 = GHA runner 가 NCP 에서 sudo 로 스크립트를 실행한다는 것.** 스크립트를 바꿔치기하면 NCP SYSTEM 획득 → 개인키로 뭐든 서명. **G-A(상주본만)·G-B(2인자 화이트리스트)가 이걸 물리적으로 가둔다.**

**G-D 실측 미완 = 착수 불가.** 자기검증 curl(publish-update.sh:258-264)이 캐시버스터로 no-cache 를 이미 쓰지만, 워치독 클라이언트가 받는 실서빙에 no-cache 헤더가 없으면 전 고객이 캐시된 옛 manifest 를 볼 수 있다. `curl -I` 회신 전 활성화 금지.

---

## §3. 워크플로우 설계 — `.github/workflows/deploy-update.yml`

> 아래는 **무엇을 만드는가**의 명세다. 의사코드·인자표까지만. 실제 YAML 은 구현 착수 시.

### §3.1 트리거 (inputs)

`workflow_dispatch` (수동) — 태그 자동은 범위 밖(§1, G5).

| input | 타입 | 기본 | 용도 |
|---|---|---|---|
| `version` | string | (필수) | 3자 버전. build잡이 `-p:HitPanVersion` 으로 단일 주입 (Directory.Build.props:33 오버라이드) |
| `channel` | choice `Major`/`Normal`/`Emergency` | `Major` | **게시함정3-③ 흡수**: 채널은 여기(상류 input)서 주입. **게시 시 jq 로 손수정 금지.** build잡이 build-manifest.ps1 `-Channel` 로 그대로 전달 (build-installer.yml:210·231 동일 패턴) |
| `environment` | choice `production`/`beta` | `production` | **보강3(G7 배선)**: deploy잡 인자분기 스위치(§3.3). **G1 착수 시 production만 실동작**, beta 분기는 배선만 대기 |

**N-1 정합 (fail-closed):** build잡 첫 스텝에서 `version` input == `-p:HitPanVersion` == manifest·zip 파일명 3자 정합 검증. 불일치 시 build 진입 즉시 중단.

### §3.2 build 잡 (windows-latest) — **재빌드 방식**

> **보강2 확정: 아티팩트 승계 아님, 재빌드.** 근거 = "게시할 커밋 = 빌드한 커밋" SSOT 불변식. 버전 출처는 Directory.Build.props FileVersion **단방향**(build-installer.yml:204 주석), build-manifest.ps1 이 api EXE 의 FileVersion 을 읽어 manifest 를 만든다. 다른 커밋의 아티팩트를 승계하면 이 단방향 불변식이 깨진다.

build-installer.yml 의 검증된 산출 시퀀스를 재사용(복붙이 아니라 **동일 스텝 구성**):

```
build (windows-latest):
  1. checkout
  2. resolve version (build-installer.yml:59-69 패턴)
  3. dotnet publish api / web / watchdog (-p:HitPanVersion=<version>)   ← build-installer.yml:76-135
  4. Copy Blazor static → api/wwwroot                                   ← build-installer.yml:104-115 (터널 서빙, 작3 봉합)
  5. build-manifest.ps1 → hitpan-<version>.zip + manifest(unsigned)     ← build-installer.yml:207-236
       · -Channel <channel input>   (게시함정3-③: 상류 주입)
       · -DownloadUrlBase <§3.3 표의 download-url-base>   (environment 분기)
       · HITPAN_RELEASED_AT 주입 (재현가능빌드, build-installer.yml:225)
  6. upload-artifact (zip + unsigned manifest)
```

**⚠️ Claude 주의:** build-installer.yml 은 EXE·GitHub Release 까지 굽는다. deploy-update.yml build잡은 **update zip + unsigned manifest 까지만** 필요하다. EXE 컴파일(ISCC)·Create Release 스텝은 **가져오지 말 것**(G2 별건).

### §3.3 deploy 잡 (ubuntu-latest, needs build, environment) — **인자분기표**

> **Claude 최적화 포인트: 아래 표를 그대로 배선하라. 문장 해석 금지.**
> deploy잡은 `environment: ${{ inputs.environment }}` 를 걸고, 아래 값을 매핑한다.

| environment | webroot (publish-update.sh) | feed-url (자기검증) | download-url-base (build-manifest.ps1) | G1 착수 시 실동작 |
|---|---|---|---|---|
| **production** | (인자 **안 넘김** — 정정된 기본값 `/var/www/updates` 사용) | (인자 **안 넘김** — 기본값 `https://updates.hitpan.kr/manifest.json`) | `https://updates.hitpan.kr/packages` | ✅ **실동작** |
| **beta** | `/var/www/updates/beta` | `https://updates.hitpan.kr/beta/manifest.json` | `https://updates.hitpan.kr/beta/packages` | ⏸ **배선만** (베타 피드 물리생성 별건까지 대기) |

**⭐ production 은 webroot·feed-url 을 CI 가 인자로 안 넘긴다** (조율1 확정). 이유:
1. sudoers 화이트리스트가 `--zip`/`--manifest` 2인자만 허용(G-B) — production 경로에 `--webroot` 를 넘기면 sudoers 위반.
2. 정정된 스크립트 기본값(`/var/www/updates`)이 정식 피드다.
3. beta 는 나중에 필요 시 sudoers 에 **beta용 두 번째 화이트리스트 라인 추가**(사장님 sudo) + build-manifest download-url-base 분기로 실동작. **G1(정식) 착수엔 영향 0.**

deploy잡 스텝 시퀀스:
```
deploy (ubuntu-latest, needs build, environment: <input>):
  1. download-artifact (zip + unsigned manifest)
  2. SSH 키 셋업 (deploy-ncp.yml:85-92 복붙)
  3. 【데이터만 전송】 zip·unsigned manifest → NCP /tmp
       · ★ 게시함정3-① 흡수: manifest 를 /tmp 에서 목적지 파일명으로 rename
         (CI 산출 manifest.json → manifest-<ver>-unsigned.json, publish 가 --manifest 로 받는 이름)
       · ★ 실행 스크립트(.sh)는 절대 rsync 금지 (B-1). 상주본만 호출.
  4. 【상주본 호출】 SSH 로 sudo publish-update.sh (2인자만):
       sudo bash /opt/hitpan/installer/updates/publish-update.sh \
         --zip /tmp/hitpan-<ver>.zip \
         --manifest /tmp/manifest-<ver>-unsigned.json
       → NCP 안에서 서명(개인키 NCP)·원자교체·자기검증·롤백. 개인키 GHA 로 안 나옴.
       → beta 필요 시에만 (별건 후) --webroot/--feed-url 을 beta 화이트리스트 라인으로 추가
  5. 【이중 확인】 curl 로 updates.hitpan.kr/manifest.json 최신 확인 (N-3 이중 안전망)
       · 실패 시 "이미 publish-update.sh 자기검증이 롤백함" 로그 명시 — GHA 롤백 스텝 추가 금지(§4)
```

### §3.4 자기검증 (이중, 중복 롤백 금지)
- 실제 롤백 주체 = **publish-update.sh 자기검증**(publish-update.sh:244-289, `rollback()` 함수). 서빙본 재취득 → 버전·서명(공개키 verify)·zip 200 확인 → 실패 시 .bak 자동 롤백.
- GHA deploy잡의 curl(스텝5)은 **이중 안전망일 뿐**. **GHA 에 별도 롤백 스텝을 만들면 publish-update.sh 롤백과 이중경합**(§4 불가 대조표). 실패 시 "이미 롤백됨" 로그만.

---

## §4. 재사용 · 불가 대조표 (복붙 오판 방지 — Claude 필독)

> **deploy-ncp.yml(웹 CD 완성본)이 패턴 원본이다. 아래 표대로 복붙 범위를 가른다.**

| 구간 | 재사용 | 근거 (파일:줄) |
|---|---|---|
| SSH 키 셋업 (ssh-keyscan·chmod600) | ✅ **복붙** | deploy-ncp.yml:85-92 |
| Secrets 3종 (`NCP_SSH_PRIVATE_KEY`·`NCP_HOST`·`NCP_USER`) | ✅ **복붙** | deploy-ncp.yml:87-97 (동일 자격증명) |
| build/deploy 2잡 + `needs` | ✅ **복붙** | deploy-ncp.yml:70-73 |
| environment 게이트 | ✅ **복붙 + reviewer 추가** | deploy-ncp.yml:76 (G-E) |
| build잡 산출 스텝 (publish·manifest) | ✅ **동일 구성** (EXE·Release 스텝 제외) | build-installer.yml:76-236 |
| **rsync 위치이동 (→/opt)** | ❌ **금지** | 데이터만 /tmp, 실행코드 상주본 호출 (B-1). deploy-ncp.yml:107-116 의 /tmp→/opt 이동 패턴을 가져오지 말 것 |
| **광역 sudo (임의 셸 `sudo rsync`·`sudo systemctl`)** | ❌ **금지** | 개인키 열람 우회로 (백엔드 §4). deploy-ncp.yml:113-120·148-157 sudo 블록 미복사 |
| **GHA 롤백 스텝** | ❌ **금지** | publish-update.sh 자기검증이 롤백 → 이중경합 (deploy-ncp.yml:197-213 을 가져오지 말 것) |
| **release installer version 동기화 스텝** | ❌ **불필요** | 그건 웹 CD 전용(deploy-ncp.yml:137-165), update CD 무관 |

---

## §5. 보안 불변식 (구현 중 절대 위반 금지 — self-review 체크리스트)

> **Claude 최적화 포인트: 구현 후 아래 7개를 한 줄씩 자가 점검하라. 하나라도 ✗ 면 커밋 금지.**

- [ ] **1. 개인키 GHA 반입 0** — Secrets·파일·환경변수 어디에도 update_private.pem 없음. 서명은 NCP publish-update.sh 안에서만.
- [ ] **2. 실행 스크립트(.sh) rsync 0** — deploy잡이 옮기는 것은 zip·unsigned manifest **데이터뿐**. .sh 는 NCP 상주본(G-A) 호출만.
- [ ] **3. sudo 대상 = publish-update.sh 1개, 인자 = --zip/--manifest 만** — 오버라이드(`--webroot`·`--private`·`--feed-url`·`--allow-republish`) 봉인. production 경로는 인자 안 넘김.
- [ ] **4. CI 는 스테이징(/tmp)만 쓰기** — update-keys(`/var/hitpan/update-keys/`, 700 root)는 배포계정 읽기조차 불가.
- [ ] **5. 서명 규격 3벌 무변경** — `UpdateManifestSigning.cs` = `sign-manifest.sh` = `publish-update.sh`. build-manifest.ps1·publish-update.sh 규격 로직 손대지 않음.
- [ ] **6. 버전 SSOT 단조증가** — Directory.Build.props FileVersion 단방향. 다운그레이드 방어(publish-update.sh:154-194 cmp_three)가 여기 의존. 버전 하드코딩 회귀 0.
- [ ] **7. cloudflared·DNS·방화벽·systemctl 미접촉** (CD 런타임) — 헌법 #29 벽 완전 무접촉.

---

## §6. G3 — DDL 스모크 CI화 (G1과 병렬 독립)

> **G1 무관하게 착수 가능**(조율 §4 트랙2). build-installer zip 생성 **선행 차단 게이트**.

### 무엇을 만드는가
`.github/workflows/` 에 ddl-smoke 스텝/잡 — clean DDL → 빈 DB import → 테이블수(125)·핵심컬럼·schema_migrations 시드 자기참조 게이트(ddl-smoke-test.sh:66-89) 통과 확인. **500이면 빌드 실패** → zip 생성 전 차단.

### ddl-smoke-test.sh 파라미터화 (하위호환 유지)
| 줄 | 현재 (로컬 전용) | CI env화 |
|---|---|---|
| :18 | `MYSQL="/c/Program Files/MariaDB 11.4/bin/mysql.exe"` | `MYSQL="${HITPAN_MYSQL:-/c/Program Files/MariaDB 11.4/bin/mysql.exe}"` (기본값 = 로컬 하위호환) |
| :21 | `DBUSER="root"` (무비번) | `DBUSER="${HITPAN_DB_USER:-root}"` + 비번 env(`HITPAN_DB_PASS`) 선택 주입 |

### CI 배선
- **MariaDB 서비스 컨테이너** (GitHub Actions `services:` — MariaDB 11.4, root 계정).
- 스텝에서 `HITPAN_MYSQL=mysql`(리눅스 클라이언트 경로)·`HITPAN_DB_USER=root`·`HITPAN_DB_PASS=<service 비번>` 주입.
- **하위호환 절대**: env 미주입 시 로컬(윈도 MariaDB 경로·root 무비번) 그대로 동작해야 함. 개발자가 `bash scripts/ddl-smoke-test.sh` 손실행 계속 가능.

---

## §7. 보강5 — trufflehog EC PRIVATE KEY 룰 편입

> 개인키 오커밋 **최종 안전망**(조율 §4, 백엔드 §4). 초저비용 편입.

### 무엇을 만드는가
`.github/trufflehog-config.yaml` 의 `detectors:` 리스트(현재 3개: hitpan-db-password·hitpan-aes-key·hitpan-jwt-secret)에 **EC PRIVATE KEY 룰 1개 추가**.

### 명세 (의사)
```
- name: hitpan-ec-private-key
  keywords:
    - "BEGIN EC PRIVATE KEY"
    - "BEGIN PRIVATE KEY"
  regex:
    key: '-----BEGIN (EC )?PRIVATE KEY-----'
```
- 대상 = update 서명 개인키(`update_private.pem`, EC 키) 형식. **NCP 전용 개인키가 실수로 레포에 커밋되는 걸 CI 가 잡는다.**
- 기존 3룰·워크플로우(trufflehog.yml) 구조 무변경, 리스트에 1개만 append(헌법 #1 "추가만").

---

## §8. 검증 DoD (완료 정의 — "돌려봤더니 됐다" 금지)

> 헌법 SOP: 완료 = 빌드 0/0 + ddl-smoke + 독립반증. 아래는 **실측환경 production 피드 최신화 실제 확인**.

### G1 (deploy-update.yml) DoD
1. **실측환경 production 피드 최신화 실측** — dispatch 로 워크플로우 실행 → `curl -fsS https://updates.hitpan.kr/manifest.json` 의 `.version` 이 **방금 올린 버전과 일치**. (publish-update.sh 자기검증 통과 + GHA 이중확인 통과)
   - ⚠️ **같은 버전 재실행은 감지 0** — 실측하려면 **직전보다 높은 새 버전**을 dispatch 해야 한다(publish-update.sh:186 다운그레이드 게이트). 예: 현재 1.2.39 → 1.2.40 dispatch.
2. **개인키 부재 확인** — GHA Secrets·아티팩트·로그 어디에도 update_private.pem 흔적 0.
3. **sudoers 실측** — deploy잡이 `--webroot` 를 넘기면 sudoers 거부되는지(2인자 화이트리스트 검증). production 경로는 2인자만.
4. **롤백 무경합 확인** — 자기검증 실패 케이스에서 publish-update.sh 만 롤백하고 GHA 는 "이미 롤백됨" 로그만(이중 롤백 0).
5. **environment reviewer 게이트 발동** — deploy잡 진입 시 사장님 승인 대기 실측(G-E).

### G3 (DDL 스모크 CI) DoD
- MariaDB 서비스 컨테이너에서 clean DDL import → **PASS(exit 0)** 재현. 일부러 컬럼 1개 뺀 DDL 로 **FAIL(exit 1)** 도 재현(게이트 실효 확인).
- 로컬 `bash scripts/ddl-smoke-test.sh` 손실행 여전히 동작(하위호환).

### G7 배선 DoD (production만 실동작)
- `environment: beta` 로 dispatch 시 인자분기표(§3.3)대로 beta webroot/feed-url/download-url-base 가 **주입은 되되**, 베타 피드 미생성이라 실동작은 production 만. (beta 실게시는 별건 후)

### 보강5 DoD
- 일부러 EC PRIVATE KEY 문자열을 커밋 시도 → trufflehog 워크플로우가 **alert**. (룰 실효 확인)

---

## §9. 착수 순서 (의존관계)

```
[선결] §2 착수 게이트 — G-A·B·C (사장님 sudo 1회 세션) + G-D 실측 + G-E 레포설정
   │
   ├─ 트랙1 (핵심 직렬): G1 deploy-update.yml (G7 파라미터 §3.3 내장) → [완주 후] G5 트리거정책 (별건)
   │
   ├─ 트랙2 (병렬 독립): G3 DDL스모크 CI화 (§6, G-F env화 선행) ─ G1 무관 착수
   │
   └─ 편입 (초저비용): 보강5 trufflehog EC룰 (§7)
```

- **G-D no-cache 미실측 = G1 활성화 불가.** `.disabled` 로 커밋 → 실측 회신 후 활성화.
- 별건(후속): G7 베타 피드 물리생성(nginx) / G2 EXE배포 / MetaPing 400.

---

## §10. 참조 파일 (구현 착수 시 열 것)

| 파일 | 역할 | 핵심 줄 |
|---|---|---|
| `.github/workflows/deploy-ncp.yml` | 웹 CD 완성본 = 패턴 원본 | SSH:85-92 / 2잡:70-76 / **롤백 미복사:197-213** |
| `.github/workflows/build-installer.yml` | build잡 산출 원본 | publish:76-135 / wwwroot:104-115 / **update pkg:207-236** |
| `installer/updates/publish-update.sh` | NCP 상주 서명·게시·롤백 | webroot기본값:41(정정대상) / source:96·99 / 다운그레이드:154-194 / 자기검증·롤백:244-289 |
| `installer/updates/sign-manifest.sh` | 서명 규격 단일출처 | (미열람 — G-A 배치 시 동반 확인, 조율 §8) |
| `installer/updates/build-manifest.ps1` | zip·unsigned manifest 산출 | -Channel·-DownloadUrlBase 인자 |
| `Directory.Build.props` | 버전 SSOT | HitPanVersion:33(현 1.2.39) / FileVersion:46 |
| `scripts/ddl-smoke-test.sh` | DDL 스모크 (CI 미연결) | MYSQL:18 / DBUSER:21 (env화 대상) |
| `.github/trufflehog-config.yaml` | SAST 커스텀 룰 | detectors:4-24 (EC룰 append) |
| (신설) `.github/CODEOWNERS` | 리뷰 게이트 | 현재 0건 (G-E 신설) |

---

## §11. 헌법 정합

#22(개인키 NCP·본사 최소주의) · #23(5중 검증 ③ SAST trufflehog·④ DAST) · #29(인프라 조작 사전 승인 = G-A·B·C 사장님 sudo) · #33(의중 재확인 = CTO·사장님 결재선) · #34(영구 CD·정식 완성도) · #36(clean DDL 단일 진실원 = G3 게이트) · #39(운영 읽기만·검증은 테스트환경 = ddl-smoke 임시DB) · SOP(봉합 즉석금지 = 이 작지서 자체).
