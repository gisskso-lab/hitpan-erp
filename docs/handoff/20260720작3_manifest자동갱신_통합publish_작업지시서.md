# 작업지시서 20260720작3 — manifest 자동갱신 (통합 publish-update.sh)

> 발행: PM 브라운킴 / 2026-07-20 / 브랜치 develop
> 상태: [3] 작업지시서 → [4] **CTO 결재 완료(부분수정후승인)** → **[5] 대표 결재 대기**
> CTO 처방 반영: §7 에 B-1·B-2·B-3(blocking) + N-1·N-2·N-3(non-blocking) 못박음. 이 조항 준수가 착수 조건.
> 성격: NCP 업데이트 배포의 **수작업 3단(서명 손붙여넣기·manifest 복사·zip 복사)** 을 사장님 1회 실행 스크립트로 통합. 코드 아님(bash 스크립트 + 문서). 헌법 #29(NCP=사장님 손)·#22(개인키 NCP 격리) 정합.
> 발견 경위: 실측 준비 중 **NCP manifest.json 이 1.2.30 에 고정**(최신 1.2.33 을 안 가리킴). 워치독은 `updates.hitpan.kr/manifest.json` 한 파일만 최신 판정원으로 보는데, 그 파일 교체가 완전 수작업이라 빠뜨리면 전 고객 업데이트가 정지.

---

## 1. 문제 (실측으로 확인)

### 워치독의 최신 판정 = manifest.json 한 파일
`UpdateClient.cs:72-73` — 워치독은 오직 `GET https://updates.hitpan.kr/manifest.json` 하나만 읽는다. 이 파일의 `version` 이 "최신"의 **단일 진실원**이다. zip 을 packages/ 에 아무리 올려도, 이 manifest 를 안 바꾸면 고객은 그 버전을 **영영 못 본다**.

### 현재 배포는 수작업 3단 (누락 = 전 고객 정지)
| 단계 | 지금 방식 | 누락 시 |
|---|---|---|
| ① zip 을 `packages/` 로 배치 | 사장님 손 (scp/업로드) | 다운로드 404 |
| ② manifest 서명 | `sign-manifest.sh` 가 **서명값을 화면 출력만** | — |
| ③ 서명값을 manifest.json 에 붙여넣기 | **사장님이 손으로 편집** | 서명 없는 manifest = 전 고객 거부 |
| ④ manifest.json 을 웹루트로 복사 | 사장님 손 (경로·이름 수동) | 옛 버전 고정 (=지금 1.2.30) |

**증상 = 지금 이 상태.** 1.2.33 zip·EXE 는 NCP 에 있으나 manifest.json 이 1.2.30 → 워치독은 "최신 1.2.30" 으로 판단해 1.2.33 로 안 올라간다.

### 왜 자동화가 필요한가 (사장님 지시)
> "항상 최신버젼을 가르키도록 해야지."

사람이 4단계를 매 릴리스마다 손으로 하면 언젠가 한 단계를 빠뜨린다(지금이 그 증거). 게다가 실측 순서(1.2.33 설치 → 1.2.34 업로드 → **업데이트 관측**)가 성립하려면, 1.2.34 를 올리는 순간 manifest 가 **자동으로** 1.2.34 를 가리켜야 한다.

---

## 2. 설계 — 통합 `installer/updates/publish-update.sh` (NCP 전용, 사장님 1회 실행)

### 대전제 (헌법)
- **개인키는 NCP `/var/hitpan/update-keys/update_private.pem` (600 root) 에만.** 스크립트도 NCP 에서 돈다. CI·GitHub·PM PC 어디서도 서명 금지(헌법 #22·#29, CTO C-1 기존 처방 유지).
- **PM 은 이 스크립트를 실행하지 않는다.** PM 이 만드는 것 = 스크립트 파일 + 절차 문서. 실행은 사장님(헌법 #29).
- **build-manifest.ps1(빌드타임, PM PC)** 은 그대로 — zip·manifest 본문(서명 없음)·sidecar 를 만든다. publish-update.sh 는 그 **뒷단(NCP 배치·서명·교체·검증)** 만 통합한다. 둘의 경계 = "서명 없는 본문까지는 빌드 PC / 서명·배치는 NCP".

### 스크립트가 하는 일 (사장님이 인자 2개만 주고 1회 실행)
```
sudo bash publish-update.sh \
  --zip     hitpan-1.2.34.zip \
  --manifest manifest-1.2.34-unsigned.json     # build-manifest.ps1 산출물(서명 없음)
```

1. **입력 검증**
   - zip·manifest 파일 존재 확인. manifest 의 `version`·`sha256`·`sizeBytes`·`downloadUrl` 읽기.
   - **zip 실측 sha256 == manifest.sha256** 확인 (한 바이트라도 다르면 즉시 중단 — 잘못된 짝 배포 원천 차단).
   - **zip 파일명 == manifest.downloadUrl 의 파일명** 확인.

2. **다운그레이드 방지 게이트** (핵심 안전장치)
   - 현재 웹루트 `manifest.json` 의 version 을 읽어, **새 version 이 더 높은지** SemVer 비교(워치독 `IsNewerVersion` 과 동일 규칙).
   - 낮거나 같으면 **중단**(옛 버전으로 되돌리는 실수·replay 차단). 단 `--allow-republish` 플래그로 동일버전 재배포는 명시 허용(서명 갱신 등).

3. **zip 배치** → `packages/{zip}` 로 복사. 이미 있으면 sha256 대조 후 동일하면 통과.

4. **서명** (기존 sign-manifest.sh 로직 흡수 또는 호출)
   - `BuildSigningPayload` 규격 문자열 생성 → `openssl dgst -sha256 -sign` → Base64.
   - **서명값·kid 를 manifest.json 에 자동 삽입**(jq 로 필드 추가 — 손붙여넣기 제거).

5. **원자적 교체** (절대 원칙)
   - 서명 완성된 manifest 를 **임시파일에 먼저 쓰고**, `mv tmp manifest.json` 으로 원자 교체.
   - 이유: 워치독이 하필 교체 도중에 GET 하면 반쪽 파일을 물어 파싱 실패 → 그 사이클 업데이트 스킵. mv 는 원자적이라 항상 옛것 아니면 새것, 반쪽 없음.
   - **옛 manifest.json 은 `manifest.json.bak-{옛버전}-{released}` 로 백업**(롤백 재료).

6. **자기 검증 (published 후 스스로 확인 — "돌려봤더니 됐다" 금지)**
   - `curl https://updates.hitpan.kr/manifest.json` 로 **실제 서빙되는** 파일을 다시 받아:
     - version == 방금 올린 version 인가
     - signature 필드가 비어있지 않은가
     - 그 manifest 의 서명이 **공개키로 검증되는가**(NCP 에 공개키도 두고 openssl 로 verify — 워치독이 거부할 manifest 를 미리 걸러냄)
     - downloadUrl 이 실제 200 으로 받아지는가(zip 접근성)
   - 하나라도 실패 → **백업본으로 자동 롤백**(mv bak → manifest.json) + 붉은 경고. "올렸는데 최신 안 가리킴" 을 배포 시점에 잡는다.

### 산출 (한눈에)
```
[성공]
  ✅ manifest.json = 1.2.34 (서명 유효, 공개키 검증 통과)
  ✅ packages/hitpan-1.2.34.zip (sha256 일치, 200 접근 가능)
  ✅ 백업: manifest.json.bak-1.2.33-20260714T...
[실패 예]
  🔴 서명 검증 실패 → 롤백 완료, manifest.json 은 1.2.33 그대로 유지(정지 아님)
```

---

## 3. 왜 이 설계인가 (택1 근거 — 사장님 "통합 publish.sh 1개" 승인)

- **③ 손붙여넣기 제거** = 서명 없는 manifest 를 올리는 사고(전 고객 거부) 원천 차단.
- **④ 원자 교체 + 자기검증** = "올렸는데 최신 안 가리킴"(지금 1.2.30 증상)을 배포 순간에 스스로 잡는다.
- **다운그레이드 게이트** = 실수로 옛 manifest 를 올려도 막힌다(워치독 방어선을 서버에도 이중으로).
- 개인키는 여전히 NCP 밖으로 안 나감. 스크립트가 NCP 에서 도니 헌법 #22·#29 무손상.
- build-manifest.ps1(빌드) ↔ publish-update.sh(배포) 경계가 "서명 전/후"로 깔끔 = 서명 규격 단일 출처(BuildSigningPayload) 유지.

---

## 4. 범위 밖 (이번 작지서 아님 — 혼동 방지)

- **자동 업로드(PM PC → NCP 자동 전송)는 안 한다.** 헌법 #29 — zip·manifest 를 NCP 로 올리는 것도, publish-update.sh 실행도 사장님 손. PM 이 만드는 건 사장님이 NCP 에서 실행할 스크립트뿐. "자동갱신" = manifest 를 최신으로 맞추는 4단계의 자동화이지, 사장님 손을 빼는 게 아니다.
- 고리4 워치독 코드(파일교체·재시작·롤백)는 이미 20260720작1 로 반영됨 — 이 작지서는 **서버측 배포 파이프라인**만.
- requiresMigration 자동 산출(build-manifest 의 -RequiresMigration 스위치)은 기존대로 명시 입력 유지 — 이번 범위 아님.

---

## 5. CTO 결재 요청

1. 통합 publish-update.sh 설계 승인 여부.
2. **자기검증에 공개키 verify 를 넣는 것**(NCP 에 공개키 사본 배치) 정합 확인 — 개인키가 아니라 공개키라 유출 위험 없음. 워치독이 거부할 manifest 를 배포 시점에 미리 거르는 이중 안전.
3. **다운그레이드 게이트**(서버측 SemVer 비교) 가 워치독 IsNewerVersion 과 규칙 일치하는지 — 판정 규칙 이원화로 어긋나지 않게 "동일 규칙" 명시.
4. **원자 교체(tmp→mv) + 백업 + 실패 시 롤백** 이 "manifest 반쪽 서빙"·"올렸는데 옛것" 을 실제로 막는지.
5. sign-manifest.sh 를 publish-update.sh 가 **흡수**할지 **호출**할지(서명 규격 단일 출처 유지가 조건).
6. 검증: 수정 후 **주입 테스트** — ⓐ 옛 버전 manifest 를 올려 다운그레이드 게이트가 막는지(+**1.2.34.0 을 올려도 1.2.34 대비 '같은 버전'으로 판정돼 `--allow-republish` 없이 막히는지** = F-4 표기차 함정, B-3), ⓑ 서명 필드를 고의로 비우거나 서명을 다른 키로 만들어 자기검증이 롤백하는지, ⓒ 정상 1.2.34 를 올려 curl 로 최신 확인 + **공개키 verify 가 규격 문자열 대상으로 통과**하는지(B-2), ⓓ channel 대문자·requiresMigration boolean 이 payload 에서 소문자·문자열로 정확히 변환돼 워치독 검증기와 왕복 일치하는지(B-1). "돌려봤더니 됐다" 금지(20260720작2 G-2M 정신).

---

## 7. CTO 처방 — 착수 조건 (2026-07-20 CTO 부분수정후승인)

> 아래 B-1~B-3 은 **착수 전 반드시 준수**. 어기면 "정상 릴리스가 전 고객 PC 에서 거부" 사고 재발. 이 작지서 최대 리스크 = **서명 대상 규격이 세 벌(BuildSigningPayload.cs / sign-manifest.sh / publish-update.sh)로 갈라지는 것**.

### B-1 (blocking) — 서명 payload 를 publish-update.sh 가 스스로 조립하지 말 것
- publish-update.sh 는 manifest.json 에서 값을 재파싱해 payload 를 **다시 짜지 않는다.** 검증된 `sign-manifest.sh` 의 `printf 'hitpan-update-v1\nversion=...\nchannel=...\n...'` 규격 블록을 **유일 출처로 재사용**한다 → sign-manifest.sh 를 `source` 하거나, 값만 뽑아 `sign-manifest.sh --private ... --version ...` 로 **호출**(흡수하되 payload 조립 코드 복제 금지).
- 값 추출 시 정규화 함정(unsigned manifest 는 build-manifest.ps1 산출 그대로라 아래 변환 필수):
  - `channel` 은 **"Normal"(대문자)** 로 저장됨 → payload 는 **소문자** "normal". (sign-manifest.sh 가 이미 `tr` 로 소문자화 — 그 로직을 타야 함.)
  - `requiresMigration` 은 JSON **boolean** `true/false` → payload 는 **문자열** "true"/"false". jq 추출 시 `.requiresMigration`(boolean) 을 `if .requiresMigration then "true" else "false" end` 로 명시 변환.
  - `sizeBytes` 는 큰 정수 → **jq 숫자(double)로 다루면 2^53 밖에서 손상**. 반드시 문자열로 다뤄라(`jq -r` + 필요시 `tostring`, 또는 원본 manifest 에서 정수 그대로).

### B-2 (blocking) — 자기검증 openssl verify 대상 = 파일이 아니라 규격 문자열
- §2-6 "서명이 공개키로 검증되는가" 는 **manifest.json 파일 전체를 verify 하는 게 아니다**(그러면 항상 실패). 서명 대상은 `BuildSigningPayload` 6필드 규격 문자열이다.
- 자기검증도 **B-1 과 동일한 payload 조립(sign-manifest.sh 규격)** 을 재사용해, curl 로 받은 서빙본에서 payload 를 재조립 → `openssl dgst -sha256 -verify pubkey.pem -signature sig.bin` 로 verify.
- ⭐ 명문화(오판 차단): **서명 대상이 파일이 아니므로, jq 삽입 시 JSON 필드순서·BOM·개행은 서명 유효성과 무관하다.** 워치독이 파싱 가능한 유효 JSON UTF-8(BOM 없음)이기만 하면 됨. "jq 순서 때문에 서명 깨질라" 는 오판이다.

### B-3 (blocking) — 다운그레이드 게이트 SemVer 를 IsNewerVersion 규칙과 동일하게
- bash 에서 `sort -V`·`[[ "$a" > "$b" ]]` 같은 소박한 비교 금지. 워치독 `IsNewerVersion`(UpdateClient.cs) 규칙을 그대로 옮긴다:
  - 각 버전을 `major.minor.build` **3정수로 파싱**(Revision 무시, 결측 자리는 0 보정 — "1.2"→"1.2.0"), 정수 튜플 비교.
  - 파싱 불능 시 **중단(fail-closed)**.
- 언어가 달라 코드 공유 불가 → **주입 테스트로 규칙 일치를 고정**. §8 주입테스트 ⓐ 에 "**1.2.34.0** 을 올려도 1.2.34 대비 '같은 버전'으로 판정돼 `--allow-republish` 없이는 막힌다"(F-4 함정) 포함.

### N-1 (non-blocking) — 캐시 무력화
- 자기검증 `curl` 이 nginx/CDN 캐시로 옛 manifest 를 물면 롤백 오발. → manifest.json 서빙에 `Cache-Control: no-cache` 확인(**워치독도 옛것 물면 최신 못 봄** — 이 작지서 부수로 반드시 확인) + 자기검증 curl 에 `-H 'Cache-Control: no-cache'` + 캐시버스터 쿼리. packages/*.zip 은 immutable 캐시 OK.

### N-2 (non-blocking) — 동시실행·원자성
- `flock /var/hitpan/update-keys/publish.lock` 로 직렬화. `set -euo pipefail`.
- **tmp 파일은 웹루트와 동일 파일시스템에 생성**(다른 마운트면 mv 가 copy+rm 이 돼 원자성 깨짐) — 작지서 명시.

### N-3 (non-blocking) — 백업 회전
- `.bak-{버전}-{released}` 무한 적재 → 디스크풀. 최근 N개만 유지.

### 절차문서 의무 (헌법 #29)
- 사장님이 sudo 1회 실행 시 **이 스크립트가 만지는 파일 목록**(packages/{zip} 생성, manifest.json 교체, .bak 생성)을 절차문서에 명시 — 사장님이 무엇을 실행하는지 알게.

---

## 8. 실측 연결 (이 작지서 통과 후)

이 스크립트가 서면, 사장님 실측 순서가 성립한다:
```
① (지금) 1.2.34 는 빌드만, NCP 미업로드 — manifest 는 1.2.33 가리키게 먼저 정정
② 1.2.33 설치마법사 EXE 로 Sandbox 신규 설치 → 정상 설치 확인 (실측 1: 설치마법사)
③ publish-update.sh 로 1.2.34 업로드 → manifest 자동으로 1.2.34 가리킴
④ 설치된 1.2.33 PC 가 워치독으로 1.2.34 로 스스로 올라가는지 관측 (실측 2: 버전 업데이트)
```
**먼저 할 일(이 작지서와 별개, 사장님 NCP 손):** 현재 1.2.30 에 고정된 manifest 를 **1.2.33 으로 정정** — 그래야 "1.2.33 설치 후 1.2.34 로 올라감" 이 관측된다. 1.2.30→1.2.33→1.2.34 순서가 맞다.

---

## 9. 사장님 3방향 정합 (2026-07-20 지시) — 이 작지서의 자리매김

사장님이 못박은 세 방향 중 **이 작지서 = 2번(당장 구현)**. 나머지 둘과의 관계를 여기 고정한다.

### 2번 — UPDATE 자동화 (이 작지서, 당장) = "프로젝트 완성의 열쇠"
> "히트판 버전 업그레이드 시 파일은 항상 최신을 가리키고, 자동 업데이트 되도록."
- 워치독 파일교체·재시작·롤백(고리4, 20260720작1) + **이 작지서(manifest 항상 최신)** = 자동 업데이트 완성.
- 이게 서야 3번(베타 테스트배포·전자세금계산서·토스 구독결재)이 얹힌다. **최우선.**

### 1번 — 워치독→백오피스 실시간 CS 보고 (기본 구조만 지금, 심화는 강지원팀장 회의 후)
> "워치독이 감지한 문제가 본사 백오피스에 실시간 보고 → CS·유지보수 파이프라인."
- **기본 구조는 이미 있다(추가 구현 아님, 자리 확인):**
  - `MetaPingPayload`(watchdog/Telemetry) — status·recent_recovery_count·watchdog_version·process_status·last_recovery 를 백오피스로 송신. 헌법 #22 정합(tenant 해시·버전·상태 라벨만, 매출·거래처·직원 0).
  - `EmergencyPayload` — reason·stage·timestamp 로 **문제 발생 즉시** 송신하는 경로가 이미 뚫려 있다.
  - 이 작지서의 자동업데이트도 결과를 남긴다: `local_update_apply_status`(성공/롤백/실패) — 이게 meta-ping 에 실려 나가면 "어느 고객이 어느 버전으로 올라갔나/롤백됐나"를 백오피스가 실시간 집계한다(성공률·정지공격 감지).
- **지금 작지서에서 하는 것**: 자동업데이트가 apply 결과를 남기는 자리(local_update_apply_status)를 유지 — 1번 CS 보고의 **데이터 원천**이 여기서 생긴다. 즉 2번을 제대로 하면 1번의 재료가 자동으로 쌓인다.
- **지금 안 하는 것(강지원팀장 회의 후)**: apply_status·recovery 를 백오피스 CS 화면에 어떻게 보여줄지, 알림·티켓·SLA 파이프라인 설계. → 별도 작지서. 이 작지서는 **송신 구조를 깨지 않게 유지**하는 선까지.
- ⚠️ 헌법 #22 경계 재확인: CS 보고에 실리는 건 **운영 메타(버전·상태·복구 라벨·tenant 해시)뿐**. 매출·거래처·직원·원장은 절대 안 실린다. 강지원팀장 회의에서 이 경계를 CS 요구와 충돌 없이 지키는 게 핵심.

### 3번 — 베타 테스트배포 → 전자세금계산서(메이크빌)·토스 구독결재
- **2번 선행 필수.** 자동업데이트가 안 서면 베타 고객에 봉합을 못 밀어 넣는다(매번 재설치 = 사장님이 없앤 경로).
- 이 작지서가 곧 3번의 진입 조건. 순서: **2번(이 작지서) → 실측 → 1번 기본구조 확인 → 베타 → 세금계산서·구독결재.**
