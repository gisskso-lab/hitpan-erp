# 계약 2026-09-30 — 교체 일꾼 `local-swap` 요청서(`request.json` schema:1) · 사유 코드 표

| 항목 | 내용 |
|---|---|
| 소유 | 20260930작1 갈래 **A**(교체 일꾼·수동 되돌리기) — 이 문서가 바뀌면 A 가 바꾸고 U·F·G 에 알린다 |
| 근거 | 설계 [`20260930_설계_PC안롤백_고객데이터불변.md`](20260930_설계_PC안롤백_고객데이터불변.md) §2·§3·§4·§13-2·§13-7 · 작업지시서 [`20260930작1_PC안롤백_작업지시서.md`](../../운영기록/20260930작1_PC안롤백_작업지시서.md) §10 + 개정 결재 줄 |
| 따르는 쪽 | 갈래 U(수동 업데이트 — `ILocalSwapLauncher` 로 `mode=update` 요청) · 갈래 F(화면 — 사유 코드 → 문구) · 갈래 G(게이트) |
| 상태 | 🟡 초판 — A 구현과 함께 커밋. 바꿀 때는 `schema` 번호를 올린다(일꾼은 모르는 번호를 거부한다) |

---

## §1 한 틀 — 누가 무엇을 하나

```
API (A: LocalRollbackService / U: ManualUpdateService)
  ① 전제 판정(읽기만)  ② ILocalSwapLauncher.Launch(SwapRequest)
       └ 바쁨 검사(§4) → 작업 폴더 준비 → request.json 쓰기 → 일꾼 스크립트를 run\ 로 복사
         → 1회용 SYSTEM 작업 HitPan-LocalSwap 등록 → /Run → 202
일꾼 local-swap.ps1 -Mode update|rollback -Ticket <번호>   (SYSTEM · API 밖에서 돈다)
       S0 번호·상태 대조 → S1 재료 → S2 안전망 → S3 정지 → S4 교체 → S5 기동 → S6 확인
       → 성공 정리 / S7 원위치 → request.json 끝 상태 · 이벤트로그 · usage.jsonl 한 줄 → 작업 스스로 삭제
```

- **일꾼은 DB 를 모른다.** 스크립트·`LocalRollback/*`·`LocalSwap/*` 에 DB 연결·DB 설정 파일·SQL 파일 낱말 0(G-D1). 자료 백업은 **U 가 API 쪽에서 요청 전에** 끝낸다.
- **작업 이름 하나 = 자물쇠.** 두 모드가 같은 이름 `HitPan-LocalSwap` 과 같은 `request.json` 을 쓴다 ⇒ update 진행 중 rollback(또는 그 반대)은 바쁨으로 거부(G-U5).

## §2 폴더

> 🔴 **개정(같은 날 · [3-V] 병렬이슈 01~04 반영 — 아래 표보다 우선)**: 작업 폴더·요청서·일꾼 사본·기록·usage.jsonl 은 전부 **`{app}\rollback\`** 아래(`%ProgramData%\HitPan` 은 Users 가 하위 폴더를 먼저 만들 수 있다 — 01). 수동 받는 곳 = **`{app}\manual\staging\`**(U). 쓰기 전 `BackupService.EnsureRestrictedSystemFolder`(C-8·C-12) — 걸리면 `folder_unsafe` · 작업 등록 0. 한 번에 하나 = `{app}\rollback\swap.lock`(`번호|UTC` · CreateNew · 일꾼이 끝에 지움) · `schtasks /Create` 에 `/F` 없음 · 끝난 교체 뒤 **10분 쿨다운**(`cooldown`) · 요청서 `requested_at` 10분 지나면 일꾼 거부(03). 되돌리기 성공 = `{app}\rollback\rolled-back.txt`(`to|from|UTC`) — 첫 칸 = 지금 판이면 `rollback_chain_blocked` · update 성공이 지운다(02). `/TR` = 고정 틀(설치 경로·모드 상수·서버 번호뿐 · 04). 새 사유 코드: `folder_unsafe` · `cooldown` · `rollback_chain_blocked`.

> 🟢 **I-API 개정(9/30 · 20260930작1 통합 갈래 I-API)**: 아래 표를 위 개정 줄대로 **`{app}` 한 벌**로 고쳤다. `%ProgramData%\HitPan\…` 경로는 이 계약에 **0**(워치독 재료 ② 는 SYSTEM 프로필 `LocalApplicationData` — `%ProgramData%` 아님). 폴더 판정 = `BackupService.EnsureRestrictedSystemFolder` **한 벌**(런처 `SwapFolderGuard` · 수동 `ManualFolders.EnsureRestricted` 둘 다 이것만 부른다). 규칙 대조표 = 개발명세서 I-API §2.

| 무엇 | 경로 | 누가 만든다 |
|---|---|---|
| 작업 폴더 | `{app}\rollback\` | 런처(처음 쓸 때 · 공용 판정 C-8: 상속 끊고 SYSTEM·Administrators·실행 계정만 · 쓰기 전마다 C-12) |
| 한 번에 하나 잠금 | `{app}\rollback\swap.lock`(`번호\|UTC` · CreateNew) | 런처(만든다) · 일꾼(끝에 지운다 · 번호가 같을 때만) |
| 요청서 | `{app}\rollback\request.json` | 런처가 쓰고, 일꾼이 상태만 고쳐 쓴다 |
| 일꾼 사본 | `{app}\rollback\run\local-swap.ps1` | 런처(`{api}\Rollback\local-swap.ps1` 에서 복사 — api 폴더는 곧 바뀐다) |
| 일꾼 기록 | `{app}\rollback\logs\swap-{ticket}.log` | 일꾼 |
| 연쇄 차단 표식 | `{app}\rollback\rolled-back.txt`(`to\|from\|UTC`) | 일꾼(rollback 성공) · update 성공이 지운다(02) |
| 준비 폴더 | `{app}\rollback\stage\{to}\{api,web,watchdog}` | 일꾼(S1 · zip 해제 — 같은 볼륨이라 S4 가 이름 바꾸기로 끝난다) |
| 이전 판(재료 ①) | `{app}\rollback\prev\{api,web,watchdog}` + `version.txt` + `replaced-by.txt` + `sha256.txt` | 일꾼(update 성공 정리에서만) |
| 옛 세 벌 임시 | `{app}\{api,web,watchdog}.rbk` | 일꾼(S4 · 성공 정리/S7 에서 사라진다) |
| 수동 기록(한 파일) | `{app}\rollback\usage.jsonl` | 일꾼(넘긴 뒤 끝 상태 한 줄) · API `ManualUsageLog`(넘기기 전 거부 한 줄) — 덧붙이기만 · 한 사용 = 한 줄 |
| 수동 받는 곳 | `{app}\manual\staging\` | U(다운로드 · `{app}\manual` 부터 공용 판정) — 일꾼은 update 성공 뒤 그 zip 만 지운다 |
| 워치독 받는 곳(재료 ②) | `%SystemRoot%\System32\config\systemprofile\AppData\Local\HitPan\Updates\staging\hitpan-{V}.zip` | 워치독(읽기만 · 삭제·수정 0) |

## §3 `request.json` — schema 1

- 인코딩: UTF-8(BOM 없음) · **ASCII 로만**(비 ASCII 는 `\uXXXX` — System.Text.Json 기본값). 일꾼(PS 5.1)이 한글 경로도 깨지지 않게 읽는다.
- 이름 규칙: snake_case. 모르는 칸은 일꾼이 무시한다. 빠진 **필수** 칸은 `refused/request_invalid`.

| 칸 | 형 | 필수 | 누가 | 뜻 |
|---|---|---|---|---|
| `schema` | int | ✅ | 런처 | `1`. 다르면 일꾼 거부 |
| `ticket` | string(32 hex) | ✅ | 런처 | 1회용 번호. 작업 명령줄 `-Ticket` 과 같아야 한다(다르면 **아무것도 안 멈추고** 종료 — 손 실행 거부 · G-S3) |
| `mode` | `update`\|`rollback` | ✅ | 호출자 | 명령줄 `-Mode` 와 같아야 한다 |
| `state` | string | ✅ | 런처→일꾼 | §5 |
| `reason` | string\|null | — | 일꾼·런처 | §6 사유 코드(끝 상태가 `success` 가 아닐 때) |
| `from` | `M.m.b` | ✅ | 호출자 | 지금 판(= API `VersionInfo.Current`) |
| `to` | `M.m.b` | ✅ | 호출자 | 바꿀 판. rollback 은 `to < from`, update 는 `to > from` — 어기면 `request_invalid` |
| `material.kind` | `prev`\|`staging_zip`\|`manual_zip` | ✅ | 호출자 | rollback = `prev` 또는 `staging_zip` · update = `manual_zip` |
| `material.path` | string | ✅ | 호출자 | `prev` = `{app}\rollback\prev` · zip = 파일 경로. **허용 뿌리 밖이면 거부**(§2 세 곳만) |
| `material.sha256` | string(64 hex)\|null | update ✅ | U | update 는 일꾼이 zip 해시를 **다시** 재서 대조(G-S1 — 틀리면 아무것도 안 멈춤) |
| `app_root` | string | ✅ | 런처 | 설치 폴더 `{app}`. `api\` 와 `watchdog\` 가 있어야 한다 |
| `slot` | int ≥1 | ✅ | 런처 | 작업 이름 번호(`HitPan-ERP-API-tenant-{slot}` 등). 일꾼은 설정 파일을 열지 않는다 |
| `api_port` | int | ✅ | 런처 | S6 `http://127.0.0.1:{api_port}/health` |
| `requested_by` | string | ✅ | 호출자 | 요청자 사용자 id(JWT 에서) |
| `requested_at` | ISO-8601 UTC | ✅ | 런처 | |
| `entry` | `menu`\|`login` | ✅ | 호출자 | 입구(13-5 기록) |
| `auto_state` | string\|null | — | 호출자 | 그때 자동 경로 상태(`UpdateIssueJudge` Kind 등 · 읽기만) — 일꾼이 usage.jsonl 에 그대로 옮긴다 |
| `updated_at` | ISO-8601 UTC | — | 일꾼 | 상태 바꿀 때마다 |
| `step` | `S0`~`S7`\|null | — | 일꾼 | 마지막으로 들어간 단계 |
| `log` | string | — | 일꾼 | 일꾼 기록 파일 경로 |

## §4 바쁨 판정(런처 · 두 모드 공통 · G-U5)

아래 하나라도 있으면 `Launch` 거부(작업 등록 0 · `request.json` 안 건드림):

| 코드 | 무엇을 보나 |
|---|---|
| `swap_in_progress` | `request.json` 의 `state` 가 `requested`·`running` 이고 `updated_at`(없으면 `requested_at`)이 **30분** 안 · 또는 작업 `HitPan-LocalSwap` 이 이미 있다 |
| `update_in_progress` | `{app}\update-swap.marker` 존재 · `{app}\update.lock` 이 15분 안(내용 `UTC|판` — 워치독 `UpdateLockFile` 규칙 그대로 · 말이 안 되면 무시) · `{app}\watchdog.new` 존재 · 작업 `HitPanWatchdogSelfReplace`·`HitPanWatchdogSelfReplaceRecover` 존재 |

- 일꾼은 S0 에서 `{app}\update.lock` 을 **자기가 잡는다**(`UTC|to` · 단계마다 새로 씀 · 끝에 지움) — 워치독 코드 무변경 · 그 잠금 규칙을 따를 뿐(13-2). ⚠️U-9: 워치독이 묵은 잠금으로 보고 지우는 조건은 15분 경과뿐(`UpdateLockFile.IsUpdateInProgress`).

## §5 상태

| 상태 | 누가 | 뜻 | 멈춘 것 |
|---|---|---|---|
| `requested` | 런처 | 작업 등록 직전 | 없음 |
| `running` | 일꾼 S0 | 번호 대조 통과 | S3 부터 |
| `success` | 일꾼 | `to` 로 떠서 확인됨 | 없음(다 켜짐) |
| `refused` | 일꾼 S0~S2 · 런처 | **아무것도 안 멈추고** 그만둠 | 없음 |
| `reverted` | 일꾼 S7 | 바꾸다 실패 → `from` 으로 되돌려 확인됨 | 없음 |
| `broken` | 일꾼 S7 | 되돌리기도 확인 못 함 — 이벤트로그 Error · 부팅 안전망이 keepalive 를 되살린다 ⇒ 작3 §4 J-4 | 알 수 없음 |

## §6 사유 코드 표 (화면 문구는 F · 설계 §8·§13-8)

| 코드 | 어디서 | 뜻 | 화면 문구(설계 기본) |
|---|---|---|---|
| `ok` | API | 열 수 있다 | — |
| `main_pc_only` | API 문(403 · `[MainPcOnly]` 그대로) | 메인PC 아님 | 회사 자료가 들어 있는 컴퓨터(메인PC)에서만 할 수 있습니다 — 작3 통일안(`UpdatePromptPlan.MainPcPhrase` · 작업지시서 §11 PM 판정) · ⬛ 초판 「대표 컴퓨터에서만」은 폐기(9/30 I-API · I-WEB 발견 §5-3) |
| (403 정책) | API 문(`TenantAdminOnly`) | 관리자 아님 | 관리자 계정으로만 할 수 있습니다 |
| `not_windows` | API | 윈도가 아님(개발 환경) | 이 컴퓨터에서는 할 수 없습니다 |
| `no_previous_version` | API(A) | 재료 ①·② 둘 다 없음 / 바로 앞 판이 아님 | 이 컴퓨터에 되돌릴 이전 버전이 없습니다 — 고객센터로 연락 주세요 |
| `rollback_chain_blocked` | API(A · `RollbackMaterialFinder`) | 지금 판이 되돌리기로 온 판 — 한 단계보다 더 앞으로 연쇄 되돌리기 금지(병렬이슈 02) | 이미 한 번 되돌린 버전입니다. 한 단계보다 더 앞으로는 되돌릴 수 없습니다 |
| `update_in_progress` | API·일꾼 S0 | §4 | 업데이트가 진행 중입니다 |
| `swap_in_progress` | API | §4 | 업데이트가 진행 중입니다 |
| `cooldown` | 런처(`CheckBusy`) | 끝난 교체(`refused` 제외) 뒤 10분 안 — 반복 재기동 막기(병렬이슈 03) | 방금 버전을 바꾸었습니다. 10분쯤 지난 뒤 다시 해 주세요 |
| `disk_low` | API(A: P-e · U: `UpdateDiskSpaceGuard`) | 저장 공간 부족 | 저장 공간이 부족합니다 |
| `ticket_invalid` | API(A) | 확인 번호가 없거나 10분 지남 · 그 사이 재료가 바뀜 | 화면을 새로 고친 뒤 다시 해 주세요 |
| `script_missing` | 런처 | `{api}\Rollback\local-swap.ps1` 없음(출력 복사 누락) | 고객센터로 연락 주세요 |
| `folder_unsafe` | 런처(`ISwapFolderGuard`) | 작업 폴더가 안전하지 않음(링크·권한 — 병렬이슈 01) · 아무것도 안 바꿈 | 히트판이 설치된 폴더를 안전하게 쓸 수 없어 아무것도 바꾸지 않았습니다 — 고객센터로 연락 주세요 |
| `task_register_failed` | 런처 | 1회용 작업 등록·실행 실패(U-3) | 고객센터로 연락 주세요 |
| `request_invalid` | 런처·일꾼 S0 | 요청서 칸 누락·판 순서 어긋남·허용 뿌리 밖 경로·schema 다름 | 고객센터로 연락 주세요 |
| `material_invalid` | 일꾼 S1 | 세 벌 없음 · FileVersion ≠ `to` · `sha256.txt` 불일치 · zip 해제 실패 | 이 컴퓨터에 되돌릴 이전 버전이 없습니다 — 고객센터로 연락 주세요 |
| `hash_mismatch` | 일꾼 S1(update) | zip 해시 ≠ `material.sha256` | (U) 새 버전을 받아 오지 못했습니다 |
| `safety_net_failed` | 일꾼 S2 | 부팅 복원 안전망 등록·확인 실패 | 고객센터로 연락 주세요 |
| `stop_failed` | 일꾼 S3 → S7 | 정지 실패 | 원래 버전으로 돌려 두었습니다 |
| `swap_failed` | 일꾼 S4 → S7 | 교체 실패 | 원래 버전으로 돌려 두었습니다 |
| `verify_failed` | 일꾼 S6 → S7 | 180초 안에 `to` 로 안 뜸 | 원래 버전으로 돌려 두었습니다 |
| `revert_failed` | 일꾼 S7 | 원위치 확인 실패(`broken`) | 고객센터로 연락 주세요 |
| U(수동 업데이트) | API(U · `ManualUpdateReasons`) | `feed_unreachable` · `signature_invalid` · `no_newer_version` · `backup_failed` · `download_failed` (+ 위 표의 `not_windows`·`update_in_progress`·`swap_in_progress`·`disk_low`·`hash_mismatch` 를 같은 글자로 씀) | 설계 §13-8 · 화면 `LocalSwapUiText` |
| ⬛ `launcher_not_wired` | (U 임시 · 폐기) | 런처가 이어지기 전 자리표 — I-API 1 이 런처를 이어 없앴다(`ManualUpdateService.cs` 주석) · 서버가 더는 보내지 않는다 | 화면은 옛 판 대비 문구만 남김 |

> 대조 기준(9/30 I-API): 서버 상수 `SwapReasons`(20) + `ManualUpdateReasons`(11 · `ok` 포함 겹침 6) = 25개 전부가 이 표에 있다(node 로 글자 대조 · 빠짐 0). 화면 쪽은 I-WEB 게이트 F-C2 가 리플렉션으로 같은 상수를 전부 대조한다.

## §7 끝에 남는 것

- `request.json` 끝 상태(`success`·`refused`·`reverted`·`broken`) + `reason` + `step` — API `GET` 이 **다음 로그인 뒤** 읽어 알린다(API 는 교체 중 멈춘다).
- 이벤트로그(원천 `HitPanWatchdog` · 새 원천 등록 0): rollback **28040** 시작 · 28041 성공 · 28042 거부 · 28043 원위치 · 28044 망가짐 / update **28060**~28064 같은 순서.
- `usage.jsonl` 한 줄(끝 상태에서만 · 덧붙이기만): `{"at","entry","mode","from","to","result","reason","requested_by","auto_state","ticket"}` — U 의 `ManualUsageLog` 는 **요청 전 거부**만 적는다(같은 사용을 두 줄로 적지 않는다).
- 1회용 작업 `HitPan-LocalSwap` 은 일꾼이 끝에서 `/Delete` 한다(G-SV). S2 에서 **자기가 새로 만든** `HitPan-ERP-keepalive-restore` 만 성공 뒤 지운다(원래 있던 것은 둔다).

## §8 재료 ① `prev` 형식 (update 성공 정리가 만든다)

- `version.txt` = 옛 판(`from`) 한 줄 · `replaced-by.txt` = 새 판(`to`) 한 줄 — **`replaced-by` ≠ 지금 판이면 ① 은 「바로 앞 판」 이 아니다**(그 사이 자동 업데이트가 한 번 더 갔다 ⇒ 무시).
- `sha256.txt` = 한 줄에 `<64hex>  <상대경로>`(상대경로 = `api\HitPan.API.exe` 처럼 prev 기준). 일꾼 S1 이 전부 다시 잰다.
- 1세대만: 새로 만들기 전에 `prev` 를 비운다.

## §9 이벤트 번호 표 (20260930작1 I-API 3 · 원천 `HitPanWatchdog` 한 곳 · 새 원천 등록 0)

> 근거 = PM P-4 결재(대역 28060~) · §7. 실측 `git grep -nE "\b280[0-9]{2}\b" -- src installer scripts .github`(2026-09-30 · `80f9fd03`) — 아래 표 밖의 280xx 는 레포에 0.
> 게이트 = `LocalSwapEventIdGateTests`(코드에서 번호를 뽑아 이 표와 대조 · 겹침 0 · 음성대조군 = 일꾼 update 기준을 28065 로 옮긴 사본은 FAIL).

| 번호 | 누가 쓰나 | 뜻 | 코드 위치 |
|---|---|---|---|
| 28008 | 설치(워치독 설치 스크립트 · 기존) | 설치 경고 | `installer/scripts/InstallWatchdog.ps1` |
| 28030 · 28031 | 워치독 자기교체(기존) | 자기교체 정보·경고 | `src/HitPan.Watchdog/AutoUpdate/UpdateOrchestrator.cs` |
| 28040 | 일꾼 rollback | 시작 | `Rollback/local-swap.ps1` `$EventBase`(+0) |
| 28041 · 28042 · 28043 · 28044 | 일꾼 rollback | 성공 · 거부 · 원위치 · 망가짐 | 같은 파일 `Complete-Swap`(+1~+4) |
| 28045 ~ 28059 | — | 비움(rollback 예비) | — |
| 28060 | 일꾼 update | 시작 | `$EventBase`(+0) |
| 28061 · 28062 · 28063 · 28064 | 일꾼 update | 성공 · 거부 · 원위치 · 망가짐 | `Complete-Swap`(+1~+4) |
| 28065 | API 수동 업데이트(U) | 넘기기 **전** 끝난 사용(거부·받기 실패·백업 실패 등) — `usage.jsonl` 1줄과 짝 | `ManualUsageLog.EventIdRefusedBeforeHandOff` |
| 28066 ~ 28079 | — | 비움(update 예비) | — |

- 🔴 되돌리기 **요청 전** 거부(GET 판정·확인 번호 틀림)는 이벤트 0 — 화면에만 알린다(§7 · A 명세서 §5).
- 번호를 새로 쓸 때는 이 표에 먼저 줄을 넣고 게이트 기대값을 같이 바꾼다(표 밖 번호 = 게이트 FAIL).
