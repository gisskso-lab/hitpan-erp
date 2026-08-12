# PI-06 — requiresMigration 미주입으로 게시본 1.2.68 폐기

| 항목 | 내용 |
|---|---|
| 적발일 | 2026-08-12 |
| 적발자 | PM 자체 실측 (게시 직후 manifest 대조) |
| 등급 | **P1** — 고객 피해 0(워치독이 차단), 게시본 1건 폐기 |
| 상태 | 🟢 봉합 완료 (1.2.69 재게시 + 게시 게이트 신설) |
| 관련 | [PI-02](20260811_PI-02_마이그레이션_엉뚱한폴더_QR500.md) — **같은 자리 1차** |

---

## 1. 무엇이 났나

**1.2.68 을 DB-90(신규 마이그레이션)과 함께 게시했는데 manifest 가 `requiresMigration: false` 로 나갔다.**

```json
{
  "version": "1.2.68",
  "requiresMigration": false,     ← DB-90 이 신규인데 false
  "releasedAt": "2026-08-12T05:55:34Z"
}
```

원인은 단순하다. PM 이 `gh workflow run` 으로 dispatch 할 때 **`requiresMigration` 입력을 넘기지 않았고**, 워크플로 기본값 `'false'` 가 들어갔다.

```bash
# PM 이 실제로 친 명령 — requiresMigration 이 없다
gh workflow run deploy-update.yml --ref main -f version=1.2.68 -f channel=Major
```

## 2. 무엇을 잃었나 / 무엇을 안 잃었나

| | |
|---|---|
| ✅ **고객 피해 0** | 워치독 교차검증이 막는다 — `false` 인데 SQL 이 있으면 업데이트를 강제 중단한다 |
| ❌ **게시본 1건 폐기** | 다운그레이드 게이트(직전보다 높아야 함) 때문에 1.2.68 재사용 불가 ⇒ **1.2.69** 로 재게시 |
| ❌ 폰 승인 1회 추가 | 사장님 손이 한 번 더 갔다 |

**막아준 것이 우리 설계였다는 점은 기록해 둔다.** 워치독 교차검증이 없었으면 고객 DB 에 DB-90 이 안 들어간 채로 양식 인쇄옵션이 500 으로 죽었을 것이다 — PI-02 와 똑같이.

## 3. 🔴 같은 자리에서 두 번째다

| | 언제 | **누가** 안 넘겼나 | 결과 |
|---|---|---|---|
| **PI-02** | 1.2.63 | **워크플로**가 `build-manifest.ps1` 에 `-RequiresMigration` 을 안 넘김 | 고객 DB 에 `device_register_tokens` 미생성 → **QR 발급 500** (사장님 실측 적발) |
| **PI-06** | 1.2.68 | **사람**이 workflow 입력을 안 넘김 | 게시본 폐기 |

**곳은 같고 주체만 바뀌었다.** PI-02 를 봉합할 때 워크플로 쪽 구멍은 막았지만, **사람이 고르는 입력** 자체는 그대로 뒀다. 그래서 같은 결함이 다른 문으로 들어왔다.

> 워크플로 주석에 이미 이렇게 적혀 있었다:
> *"왜 default 로는 부족한가 — 종전에도 default 는 Major 였다. 그런데 **사람이 고를 수 있으면 고른다.** 기본값은 규율이 아니다."*
>
> 채널에는 이 교훈을 적용해 선택지를 없앴는데, **requiresMigration 에는 적용하지 않았다.** 오늘 그 자리에서 났다.

## 4. 봉합

### 4-1. 즉시 — 1.2.69 재게시
`requiresMigration=true` 로 재게시하고 **실측으로 확인**했다.

| 검증 | 결과 |
|---|---|
| 실서버 manifest `version` | **1.2.69** |
| 실서버 manifest `requiresMigration` | **true** ✅ |
| 패키지 다운로드 | HTTP 200 · 103,432,564 bytes |
| sha256 (manifest 값과 대조) | **일치** `83eb86bb…c354e2` |
| 패키지 내 DB-88·89·90 | **3건 전부 탑재** |
| 패키지 내 마이그 총건수 | 61건 (소스와 동일) |
| EXE 내 봉합분 | ComposeBodyCoords · ParseFieldCoords · AppendTotalRow · GridExportController · IsRunningTotalColumn **5/5** |
| EXE FileVersion | **1.2.69.0** (manifest 와 정합) |

### 4-2. 재발방지 — 게시 전 fail-closed 게이트
`deploy-update.yml` 에 **`Verify requiresMigration against payload`** 스텝 신설.

- 게시물 안에 `DB-*.sql` 이 **0건이면 즉시 중단** (csproj 복사 지시가 깨진 것 — 과거 P0)
- 직전 게시 시각(`manifest.releasedAt`) 이후 **새로 추가된** DB-NN SQL 을 **git 으로** 센다
- 새 SQL 이 있는데 `requiresMigration=false` 면 **게시 중단**
- 반대(SQL 없는데 true)는 워치독이 무시하므로 **경고만** — 안전측은 true 다

**검산 (가정이 아니라 실제 이력으로 돌렸다):**
```
기준 커밋 : c10d0c5  (1.2.67 게시 시각 2026-08-11T01:15:45Z)
새 SQL    : DB-90_form_templates_print_options.sql
⇒ 오늘 1.2.68 을 false 로 dispatch 했을 때 이 게이트가 있었으면 중단됐다
```

## 5. ⚠️ 봉합하다 하마터면 "막는 척만 하는 게이트" 를 만들 뻔했다

처음에는 서버의 사이드카 `migrations-<ver>.txt` 로 직전 게시본과 대조하게 짰다.
**실측해 보니 403 이다:**

```
migrations-1.2.67.txt → HTTP 403
migrations-1.2.68.txt → HTTP 403
```

nginx 가 막고 있어 영영 안 읽힌다. 그러면 대조가 **조용히 건너뛰어져** 게이트가 있는데 아무것도 안 막는 상태가 된다.
⇒ CI 에 이미 있는 **git 이력**으로 바꿨다.

같은 이유로 **`fetch-depth: 0`** 이 함께 필요하다. `actions/checkout` 기본값은 depth 1 이라 이력이 없고, 그러면 git 대조가 **역시 조용히 건너뛰어진다.** 게이트와 한 몸이라 같이 넣었다.

> **교훈**: 게이트를 넣을 때는 **그 게이트가 실제로 발동하는지**를 먼저 실측한다.
> 안 읽히는 자료로 대조하는 게이트는 없는 것보다 나쁘다 — 있다고 믿게 만들기 때문이다.

## 6. ⬜ 남은 것

- **`requiresMigration` 입력 자체를 없애고 자동 산출로 가는 것이 정답이다.**
  `build-manifest.ps1` §24 에 *"자동 산출은 아직 못박지 않았다 (사장님·CTO 결재 대기)"* 로 남아 있어
  이번에는 **입력을 유지한 채 교차검증만** 넣었다. 결재가 나면 입력을 지운다.
  ⇒ 채널을 고정한 것과 같은 처리다 — **선택지를 없애야 규율이다.**
- **사이드카 403** — 워치독 교차검증 재료로 쓰려면 열어야 한다(별건).

---

*작성: PM · 2026-08-12*
