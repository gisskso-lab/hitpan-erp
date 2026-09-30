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
| 판 이력(봉합 05ⓑ) | `{app}\rollback\versions-seen.txt` | API 기동(갈래 M `InstalledVersionLedger`) — 읽기 = `RollbackMaterialFinder`(L) · 일꾼 S1(K2) |

> 🟢 **봉합 차수 합의(9/30 작1 봉합 · 설계 §14-1 05 · 갈래 L 이 먼저 적는다 — K·M 은 이 절을 읽고 맞춘다)** — `versions-seen.txt`
> - 위치 `{app}\rollback\versions-seen.txt`(파일 이름 상수 = `LocalSwapLauncher.VersionsSeenFileName`). 쓰기 전 폴더 문지기 `EnsureSafe(work)` — 실패하면 **안 쓰고 경고 로그만**(기동을 막지 않는다 · PM 결재 S-2).
> - 한 줄 = `{M.m.b}|{UTC o}`(예 `1.3.49|2026-09-30T13:00:00.0000000Z`) · 줄바꿈 `\r\n` · UTF-8(BOM 없음) · ASCII 로만. **덧붙이기만**(고치기·지우기 0 · 일꾼도 안 지운다).
> - 기록기(M): API 기동 때 자기 판(`VersionInfo.Current` 세 칸)을 적는다. **마지막 줄의 판과 같으면 안 쓴다.**
> - 읽는 쪽(L·K2): 칸이 두 개가 아니거나 판이 `M.m.b` 로 안 읽히는 줄은 **건너뛴다**. 읽힌 줄 가운데 **마지막 줄 판 = 지금 판**일 때만, 그 위로 올라가며 처음 만나는 **지금 판과 다른 판 = 직전 설치 판**. 마지막 줄 ≠ 지금 판 · 읽힌 줄이 하나뿐 · 파일 없음 = **직전 판 모름**.
> - 판정(재료 ②): ⓐ `rollback\prev` 폴더가 있고 `replaced-by.txt` 가 지금 판이 아니면(없거나 못 읽어도 같다) ② 도 거부 ⓑ 직전 판을 알면 ② 는 `hitpan-{직전 판}.zip` 만(지금 판보다 작아야 하고 지금 판 zip 도 있어야 한다 — 기존 규칙 유지) · 모르면 ⓐ 만(= 봉합 전과 같음 · 1.3.48 비상 경로 보존). 사유 = `no_previous_version`.

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

> 🟢 **봉합 차수 합의(9/30 작1 봉합 · 설계 §14-1 06ⓑ · 14-2 07·F-4 · 갈래 L)** — 위 표 `swap_in_progress` 줄의 「또는 작업 `HitPan-LocalSwap` 이 이미 있다」를 아래로 **좁힌다**(나머지 줄은 그대로).
> - **남은 작업(06ⓑ)**: 작업 `HitPan-LocalSwap` 이 있어도 「열린 요청(`requested`·`running`)이 **30분** 안에 갱신됨」 또는 「`swap.lock` 이 **30분** 안」일 때만 바쁨(이 둘은 위 표 판정이 먼저 잡는다). 둘 다 묵었으면 런처가 `/Delete /TN "HitPan-LocalSwap" /F` 로 지우고(경고 로그) 바쁨이 아니다. 지우기가 실패하면 그대로 `swap_in_progress`.
> - **묵은 요청서 끝 상태**: 열린 요청이 30분 넘게 안 바뀌었고 `swap.lock` 도 묵었으면(작업이 있든 없든) 런처가 끝 상태로 적는다 — `requested` → `refused`/`worker_not_started` · `running` → `broken`/`worker_interrupted`. `updated_at`·`step` 은 **안 바꾼다**(일꾼이 마지막으로 만진 시각 그대로 — 끝 상태를 적은 순간이 쿨다운을 새로 걸지 않게).
> - **끝나지 않은 교체**: 그 뒤(쿨다운 판정 다음) `{app}\{api,web,watchdog}.rbk` 가 하나라도 남아 있고 마지막 요청이 `broken` 이면 `update_in_progress` 대신 **`swap_interrupted`**(영구 「진행 중」 거짓 표시 없앰). 요청이 `broken` 이 아니면 종전대로 `update_in_progress`.
> - **예약(F-4 · 프로세스 안)**: `bool TryReserve(string owner)` — 비었거나 같은 주인이면 잡고(다시 잡으면 시각 갱신) `true` · 남이 쥐고 있으면 `false`. `void Release(string owner)` — 주인이 같을 때만 푼다(남의 것·빈 것은 무시). 예약은 **30분**(`OpenRequestStale`) 안 갱신이 없으면 없는 것으로 본다(해제 누락이 영구 바쁨으로 번지지 않게). API 가 재시작하면 사라진다(교체가 시작된 뒤는 `swap.lock` 이 이어받는다). 파일·작업 등록 0.
> - `string? CheckBusy()` = **누구의 예약이든** 있으면 `swap_in_progress`(화면 상태 조회용). 🆕`string? CheckBusy(string? owner)` = **남의 예약**만 `swap_in_progress`(자기 예약은 무시). `Launch` 는 `SwapLaunchInput.Owner`(🆕 끝 칸 · 기본 null)로 `CheckBusy(owner)` 를 부른다 ⇒ 예약한 호출부는 **같은 owner 를 `Owner` 에 넣어** `Launch` 해야 자기 예약에 막히지 않는다. 판정 순서: 예약 → 열린 요청 30분 → `swap.lock` 30분 → 묵은 것 정리 → 쿨다운 → 워치독 표식(`.rbk` 는 위 `swap_interrupted` 규칙).
> - **사전 판정(07)**: 🆕`string? CheckReady()` — `Launch` 앞 정적 판정만 떼어 낸 것: 윈도 아님 `not_windows` · 설치 루트 없음·슬롯 없음 `request_invalid` · 일꾼 원본 없음 `script_missing` · 작업 폴더 `{app}\rollback`·`run` 문지기 실패 `folder_unsafe`. 되면 null. 바쁨은 보지 않는다(→ `CheckBusy`). 호출부(M: `ManualUpdateService.CheckAsync`·`Start` · `LocalRollbackService.GetStatus`·`Start`)는 `CheckBusy` **앞**에서 부른다. `Launch` 도 첫머리에서 같은 함수를 부른다(판정 한 벌).

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
| `worker_not_started` | 런처(`CheckBusy` · 봉합 06ⓑ) | `requested` 로 30분 넘게 묵음 — 일꾼이 아예 못 떴다 · 끝 상태 `refused`(아무것도 안 멈춤) | 예약해 둔 작업이 시작되지 않아 아무것도 바꾸지 않았습니다. 다시 해 주세요 |
| `worker_interrupted` | 런처(`CheckBusy` · 봉합 06ⓑ) | `running` 으로 30분 넘게 묵음 — 일꾼이 도중에 끊겼다 · 끝 상태 `broken` | 버전을 바꾸던 도중 멈췄습니다 + `broken` 안내(다시 켜기 → 고객센터) |
| `swap_interrupted` | 런처(`CheckBusy` · 봉합 06ⓑ) | `.rbk` 가 남아 있고 마지막 요청이 `broken` — 「진행 중」 대신 | 이전 교체가 끝까지 되지 않았습니다. 고객센터로 연락 주세요(설계 §14-1 06 문언 그대로) |

> 대조 기준(9/30 I-API): 서버 상수 `SwapReasons`(20) + `ManualUpdateReasons`(11 · `ok` 포함 겹침 6) = 25개 전부가 이 표에 있다(node 로 글자 대조 · 빠짐 0). 화면 쪽은 I-WEB 게이트 F-C2 가 리플렉션으로 같은 상수를 전부 대조한다.
> 🟢 봉합 차수(9/30 · 갈래 L): `SwapReasons` 20 → **23**(위 3줄) ⇒ `ok` 뺀 서버 코드 24 → **27** · F-C2 하한 24 → 27 · F-C2 계약 코드 목록에 3개 덧붙임.

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
