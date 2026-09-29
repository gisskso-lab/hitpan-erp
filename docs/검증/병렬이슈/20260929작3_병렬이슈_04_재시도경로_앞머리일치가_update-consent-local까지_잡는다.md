# 20260929작3 병렬이슈 04 — 재시도 경로 「앞머리 일치」가 `update-consent-local` 까지 잡는다

| 항목 | 내용 |
|---|---|
| 작업 | 20260929작3 — 갈래 F 절F3(`MainPcRetryCoordinator.EligiblePathPrefixes` 에 `"/api/auth/update-consent"`) |
| 검증 | [3-V] 보안상무 안철수 (hp-verifier) · 2026-09-29 |
| 심각도 | 🟡 **하 (P2)** — 지금은 해가 없다. 나중에 조용히 터질 자리 |
| 상태 | 🔴미봉합 — 코드 커밋 전 · 설계 그대로면 생긴다 |
| 근거 등급 | 코드 추적 |

## 무엇이 문제인가

`src/HitPan.Web/Services/MainPcRetryCoordinator.cs:109-115` 는 `path.StartsWith(prefix)` 와 선행 슬래시 뺀 `prefix[1..]` 로 비교한다. 기존 항목은 `/api/backup/` 처럼 **끝이 `/`** 라 경계가 있었는데, 새 항목 `"/api/auth/update-consent"` 는 끝에 경계가 없다.

⇒ `Login.razor:300` 이 부르는 **`api/auth/update-consent-local`**(익명 · 로그인 전)도 「재시도 대상」으로 잡힌다.

지금은 `update-consent-local` 이 403 `main_pc_only` 를 내지 않으므로 재시도가 실제로 돌지 않는다(해 없음). 그러나:
- N-UPD7(Q-5) 에서 로그인 전 승인에 메인PC 제한을 넣으면(병렬이슈 03 권고 2-a) 그 403 에 대해 **로그인도 안 한 화면에서 출입증 왕복 + POST 재전송**이 돈다 — 의도하지 않은 경로 · 설계 §8 「새 재시도 입구를 익명 경로에 열지 않는다」와 어긋난다.
- 앞으로 `/api/auth/update-consent-*` 로 시작하는 다른 주소가 생겨도 같은 일이 생긴다.

## 권고
- 항목을 **정확히 그 주소**로 맞춘다: 경로가 `/api/auth/update-consent` 와 **같거나** 그 뒤가 `?`·`/` 로 이어질 때만. 또는 이 항목만 별도 정확 일치 목록으로.
- 게이트: `IsEligible("/api/auth/update-consent")`=참 · `IsEligible("api/auth/update-consent-local")`=**거짓**. 대조군 = 앞머리 일치로 되돌리면 두 번째가 FAIL.
