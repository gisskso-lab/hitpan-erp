# 병렬이슈 PI-10 — P2 "설정화" 의 저장 위치가 미정이다. 그리고 기기 설정은 **이미 appsettings 에 있다**

| 항목 | 내용 |
|---|---|
| 적발 | [3-V] 동시 병렬검증 · 보안상무 안철수 · 2026-08-15 |
| 대상 | 명세서 §4-7 · 스케줄 P2 · 작지서 §2 "하는 것 — P2" |
| 등급 | 🟠 **P1** — 헌법 #21 과 정면으로 부딪히는데 설계가 회피만 하고 답을 안 냈다 |
| 상태 | ⬜ 미봉합 |

---

## 1. 무엇이 문제인가

세 문서 모두 P2 를 **"요금 기준값을 설정에서 읽는다"** 로 정했다.
그리고 세 문서 모두 **"단, `appsettings.json` 은 건드리지 않는다"**(헌법 #21)고 적었다.

🔴 **그런데 어디에 저장하는지를 아무도 정하지 않았다.**

- 스케줄 P2 §위험: *"설정 저장 위치를 **별도로 정한다**"* ← 정한다고만 하고 안 정함
- 작지서 §3 #21: *"`appsettings*.json` **안 건드림**. **별도 자리**"* ← "별도 자리" 가 어디인지 없음

⇒ **구현자가 정하게 된다.** 명세서 §3 서문이 스스로 경고한 바로 그 상황이다:
> *"설계는 둘 중 하나를 진실로 정해야 한다. **정하지 않으면 구현자가 임의로 고른다.**"*

---

## 2. 근거 (실측) — 기기 설정은 **이미 appsettings.json 에 있다**

`src/HitPan.Infrastructure/Services/TenantDeviceService.cs:50-56`
```csharp
public TenantDeviceService(IDbConnection db, IAuditService audit, IConfiguration? config = null)
{
    // 설정이 없으면 꺼짐(false) — 안전측. 종전 동작 그대로다.
    _approvalEnabled = config?.GetValue<bool>("DeviceApproval:Enabled") ?? false;
}
```

그리고 그 주석이 **직접 지목한다** (`:45-46`)
```
/// ■ 끄는 법
///   appsettings.json → "DeviceApproval": { "Enabled": false }
```

실측 확인 — `src/HitPan.API/appsettings.json:15`
```json
"DeviceApproval": {
```

🔴 **이미 있다.** 같은 서비스(`TenantDeviceService`)의 기기 관련 설정이
**이미 `appsettings.json` 에 자리를 잡고 있다.**

⇒ 구현자가 *"옆에 한 줄 더 넣으면 되겠네"* 라고 판단할 확률이 매우 높다.
그 순간 **헌법 #21 위반 + 커밋 훅 차단**이다.
⚠️ 전례가 있다 — [[project_appsettings_backoffice_url_pending]] 는
`BackofficeApiBaseUrl` **한 줄** 때문에 훅에 막혀 커밋이 보류됐다.

---

## 3. 이게 터지면 무슨 일이 나는가

### (1) 구현이 끝난 뒤에야 막힌다 — 가장 비싼 자리에서

P2 구현을 다 하고 커밋 단계에서 훅에 걸린다. 그때 저장 위치를 다시 정하면
**설정 읽는 코드 전체를 다시 짠다.**

### (2) 헌법 #21 의 취지를 우회하는 봉합이 나온다

`appsettings.json` 이 막히면 구현자가 `appsettings.Local.json` 같은 **새 파일**을 만들 수 있다.
그러면 **업데이트 때 그 파일이 보존되는지 아무도 모른다**
([[feedback_update_not_reinstall]] — *"db.conf 업데이트시 보존"* 이 이미 교훈으로 있다).

⇒ 🔴 **업데이트 한 번에 요금 한도가 기본값으로 돌아간다.** 과금 사고다.

### (3) 이중 진실원이 삼중이 된다

명세서 §2-5 는 *"ERP switch ↔ 백오피스 `plans` 테이블"* **이중**이라 했다.
새 저장소를 만들면 **삼중**이 된다. P2 의 완료 기준
*"ERP 와 백오피스가 같은 값을 본다"* 가 오히려 더 멀어진다.

---

## 4. 어떻게 고쳐야 하나

### (1) 저장 위치를 **설계 단계에서** 못박는다 — 권고: `local_subscription` 표

이미 있는 길을 쓴다. 스케줄 P2 §위험이 스스로 답을 절반 적어놨다:
> *"본사↔ERP 전달 경로(`WebhookInboundController`)가 이미 있다. **새로 만들지 말고 그 길을 쓴다.**"*

실측 — `WebhookInboundController.cs:103,120` 이 이미 `local_subscription` 에
`subscription_tier` · `extra_device_slots` 를 **쓰고 있다.**
그리고 `TenantDeviceService.cs:88,238` 이 **그 표에서 읽고 있다.**

🔴 **길이 이미 뚫려 있다.** 여기에 `max_pc_devices` · `max_mobile_devices` ·
추가슬롯 산식·가격 컬럼을 더하면:
- `appsettings` 무관 → **헌법 #21 자동 준수**
- 본사가 값을 바꾸면 웹훅으로 내려온다 → **코드 배포 불필요** (P2 목표 달성)
- 백오피스 `pricing_plans` 가 원본, ERP `local_subscription` 이 수신 캐시 → **진실원 하나**

⚠️ 이때 **헌법 #37** 에 걸린다 — 수신 캐시 컬럼은 *"안 읽힌다 ≠ 잔재"* 다. **제거 금지**를 문서에 함께 적는다.

### (2) DDL 변경이 따라온다 — 명시해야 한다

`local_subscription` 에 컬럼을 더하면 **DDL 변경**이다.
⇒ `src/HitPan.API/Migrations/SQL/DB-NN_*.sql` + clean DDL 편입(#36).
**P2 를 "코드만 고치는 단계"로 적어둔 것이 틀렸다.** 스케줄에 DDL 단계를 넣어야 한다.

### (3) G-8 을 실제로 잡히게 다시 쓴다

현행 G-8 = *"요금 기준값이 **코드 리터럴에 없는지**"*.
🔴 이 게이트는 **우회가 너무 쉽다.** `const int Basic = 5;` 를 다른 파일로 옮기면 통과한다.

⇒ 다시 쓴다: **"DB 값을 바꾸면 재빌드·재배포 없이 한도가 바뀌는가"** 를 시험한다.
작지서 §5 가 *"게이트는 반증까지 확인한다"* 며 인용한
**8/15 메신저 교훈(`ChatWindowGuardTests` 가 글자만 검사해 통과)** 이
🔴 **G-8 에 그대로 재현돼 있다.** 교훈을 적어놓고 같은 형태의 게이트를 다시 만들었다.

---

## 5. 등급 판정 근거

**P1** — 이유:
1. 헌법 #21 은 **커밋 훅으로 강제**되므로 구현 후 반드시 드러난다(무한 잠복은 아님)
2. 그러나 드러나는 시점이 **구현 완료 후**라 재작업이 크다
3. 🔴 **G-8 이 글자만 보는 게이트**라 우회를 못 잡는다 — 이건 P1 중에서도 무겁다
4. 저장 위치를 잘못 고르면 **업데이트 때 한도가 초기화**되는 과금 사고로 번진다
