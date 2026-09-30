# 20260930작1 병렬이슈 01 — `%ProgramData%\HitPan\{rollback,manual}` 을 일반 사용자가 먼저 만들면, SYSTEM 이 도는 일꾼 스크립트·요청 파일·받은 zip 을 바꿔칠 수 있다

| 항목 | 내용 |
|---|---|
| 작업 | 20260930작1 — 갈래 A(`LocalSwapLauncher`·`local-swap.ps1`) · 갈래 U(`manual\staging` · `usage.jsonl`) |
| 검증 | [3-V] 보안상무 안철수 (hp-verifier) · 2026-09-30 |
| 심각도 | 🔴 **상 (P0 후보)** — 이 PC 의 **관리자 아닌 Windows 사용자**(또는 그 권한으로 도는 악성 프로그램)가 **SYSTEM 권한 실행**을 얻는다 |
| 상태 | 🔴미봉합 — 설계 단계 적발(코드 미착수 · 갈래 A worktree 변경 0) · 설계가 막는 방법을 적지 않았다 |
| 근거 등급 | 실측(이 PC 폴더 권한 읽기) + 설계 추적 |

## 무엇이 문제인가

설계는 두 줄로 끝난다.
- §2 「`%ProgramData%\HitPan\rollback\` 은 SYSTEM·Administrators 만 쓰기(일반 사용자 쓰기 0) — 요청 파일 바꿔치기 차단」
- §13-6 「폴더 `%ProgramData%\HitPan\{rollback,manual}\` — **API 가 처음 쓸 때 만들고** ACL 을 건다」

그런데 그 위 폴더는 이미 일반 사용자가 하위 폴더를 만들 수 있다(이 PC 실측 · 읽기만):

```
icacls C:\ProgramData\HitPan
  BUILTIN\Users:(I)(OI)(CI)(RX)
  BUILTIN\Users:(I)(CI)(WD,AD,WEA,WA)     ← 파일·폴더 만들기 허용 (ProgramData 기본 상속)
  CREATOR OWNER:(I)(OI)(CI)(IO)(F)        ← 만든 사람이 그 폴더의 전권
```

⇒ N+1 이 깔리기 **전에** 일반 사용자가 `C:\ProgramData\HitPan\rollback\run\` · `manual\staging\` 을 먼저 만들어 **소유자**가 된다. 「처음 쓸 때 만들고 ACL 을 건다」는 이미 있는 폴더 앞에서 만들기를 건너뛰고, 권한 줄을 SYSTEM·Administrators 로 바꿔도 **소유자는 권한 줄을 다시 쓸 수 있다**(소유자 고유 권리).

그 다음 가능한 것:
| 바꿔치는 것 | 시점 | 결과 |
|---|---|---|
| `rollback\run\local-swap.ps1` (§2 ③ 복사본) | API 복사 뒤 ~ 1회용 작업 시작 전 | **임의 스크립트가 SYSTEM 으로 돈다** |
| `request.json` (from·to·재료 경로·sha256) | API 쓰기 뒤 ~ S0 | 1회용 번호는 명령줄에서 읽어 맞춰 두면 그만 · 재료 경로·해시를 바꾼다 |
| `manual\staging\hitpan-{V}.zip` | S1 해시 대조 뒤 ~ 해제 | 대조한 파일과 푼 파일이 다르다(TOCTOU) ⇒ 서명 안 된 세 벌이 SYSTEM 서비스로 뜬다 |
| 폴더를 교차점(junction)으로 | API 쓰기 전 | SYSTEM 이 남의 위치에 쓴다(임의 파일 쓰기) |

- 교체되는 세 벌 중 `api` 는 **SYSTEM 예약작업으로 도는 프로그램**이다(`installer/HitPan-Universal.iss:2888` `/RU SYSTEM /RL HIGHEST`) ⇒ 바꿔치기가 곧 SYSTEM 상시 실행이다.
- 설계 §3-1 재료 ① 은 `{app}\rollback\prev\` (Program Files — 일반 사용자 쓰기 불가)인데 §13-2 는 「`rollback\prev\`」라고만 적어 **어느 뿌리인지 흐리다**. `%ProgramData%\HitPan\rollback\prev\` 로 구현되면 옛 판 세 벌 자체를 바꿔친다 — 옆의 `sha256.txt` 도 같은 폴더라 신뢰 기준이 못 된다.

## 전례 — 이미 한 번 막은 모양이다
`BackupService.EnsureRestrictedBackupFolderWindows`(`src/HitPan.Application/Services/BackupService.cs:820-866`) 가 똑같은 선점을 막으려고 세 가지를 한다(병렬이슈29 · C-12): ⓐ 소유자 = SYSTEM·Administrators·실행계정 아니면 거부 ⓑ 그룹(Users·Everyone …) 권한 줄 거부 ⓒ 그 폴더와 위 폴더 전부 재분석 지점(교차점) 거부 · 만든 **직후 다시** 검사. 그리고 병렬이슈29 는 ⓒ 가 없을 때 교차점으로 우회된 사례다. 이 트랙 설계에는 이 전례가 **옮겨지지 않았다.**

## 권고 (봉합 조건 — [4] 가 확인)
1. `rollback\` · `rollback\run\` · `rollback\stage\` · `manual\` · `manual\staging\` 모두 C-12 ⓐⓒ 를 **쓰기 직전마다** 적용한다(만들 때 한 번이 아니라). 실행 파일이 놓이는 곳이므로 ⓑ 는 백업보다 **더 좁게** — SYSTEM·Administrators 밖의 **쓰기** 줄은 개별 사용자라도 거부.
2. 이미 있는 폴더가 조건에 안 맞으면 **거부**(사유 한 줄 · 작업 등록 0). 지우고 다시 만드는 쪽을 택하면 지운 뒤 만든 것을 다시 검사한다.
3. `request.json`·스크립트 복사본은 **새 이름으로 새로 만든다**(`FileMode.CreateNew`) — 남이 먼저 만든 파일을 덮어쓰지 않는다.
4. update 모드 S1: 해시 대조한 **같은 열린 파일**에서 푼다(또는 대조 전에 SYSTEM 전용 폴더로 복사 뒤 대조·해제). 대조와 해제 사이에 경로를 다시 열지 않는다.
5. 재료 ① 뿌리를 `{app}\rollback\prev\`(Program Files)로 **못 박는다** — 설계 §13-2 문구 정정.
6. 일꾼 S0 도 폴더·파일 소유자를 한 번 더 본다(API 가 검사한 뒤 바뀐 경우).

## 게이트 제안 (대조군 필수)
| 게이트 | 통과 | 대조군(FAIL 해야) |
|---|---|---|
| G-ACL1 | 시험 대역에서 `rollback\run\` 을 **다른 소유자**로 미리 만든 상태 → 요청 거부 · `request.json`·작업 등록 0 | 소유자 검사 뺀 사본 |
| G-ACL2 | 폴더를 교차점으로 미리 만든 상태 → 거부 | 재분석 지점 검사 뺀 사본 |
| G-ACL3 | S1 대조 뒤 zip 을 바꾸는 대역 → 해제된 내용 = 대조한 내용(또는 거부) | 경로를 다시 여는 사본 |

## 이 PC 에서 한 것
`icacls C:\ProgramData` · `C:\ProgramData\HitPan` · `…\Watchdog` **읽기만**. 폴더 생성·권한 변경 0(#39).
