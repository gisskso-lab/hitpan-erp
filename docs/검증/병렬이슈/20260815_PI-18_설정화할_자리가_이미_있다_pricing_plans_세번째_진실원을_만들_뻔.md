# 병렬이슈 PI-18 — 설정화할 자리가 **이미 있다**. 모르고 만들면 진실원이 셋이 된다

| 항목 | 내용 |
|---|---|
| 적발 | [3-V] 동시 병렬검증 **2회차** · 보안상무 안철수 · 2026-08-15 |
| 대상 | 작지서 작3 **P2**("기준값 설정화" · "ERP ↔ 백오피스 이중 진실원 해소") |
| 등급 | 🟠 **P1** — 지금 그대로 착수하면 **이중이 삼중이 된다.** 헌법 #11 위반 소지 |
| 상태 | ⬜ 미봉합 |

---

## 1. 무엇이 문제인가

작지서 P2 는 이렇게 지시한다.
> - [ ] `GetLimitsForTier`(`:662-669`) → **설정에서 읽기**
> - [ ] ERP ↔ 백오피스 **이중 진실원** 해소

**"설정" 이 무엇인지 안 적혀 있다.** 헌법 #21 때문에 `appsettings.json` 은 못 쓴다는 것만 적혀 있고
(작지서 3장: *"`appsettings*.json` **무접촉**"*), **어디에 넣을지는 비어 있다.**

🔴 그런데 실측하면 **본사가 관리하는 요금제 표가 이미 있고, 기기 한도 칸까지 갖고 있다.**
모르고 새로 만들면 **진실원이 셋** 이 된다 — 작지서가 없애겠다는 바로 그 문제를 키운다.

---

## 2. 근거 (실측)

### (1) `pricing_plans` 에 기기 한도 칸이 이미 있다

`src/HitPan.Backoffice.API/Controllers/DeviceRegistrationController.cs:62-70`
```sql
SELECT CAST(t.tenant_id AS CHAR) AS TenantId, ...
       COALESCE(p.max_pc_devices, 5)     AS MaxPcDevices,
       COALESCE(p.max_mobile_devices, 3) AS MaxMobileDevices
FROM tenants t
LEFT JOIN landing_signups ls ON ls.company_name = t.company_name
LEFT JOIN pricing_plans p   ON p.plan_id = COALESCE(ls.plan_type, 'basic')
```

🔴 **기본값 `(5, 3)` 이 `GetLimitsForTier` 의 `"basic" => (5, 3)` 과 정확히 같다.**
같은 숫자가 **두 곳에 각각 적혀 있다.**

### (2) 그 표는 **본사 화면에서 이미 수정할 수 있다** — 헌법 #11 이 요구하는 형태

`src/HitPan.Backoffice.API/Controllers/PricingAdminController.cs:118-138`
```sql
UPDATE ...
    max_pc_devices = @MaxPcDevices,
```
⇒ **어드민이 직접 설정하는 장치가 이미 완성돼 있다.**
메모리 진실원에도 남아 있다 — *"pricing_admin_managed(⭐가격·리워드 본사마스터화면·**코드금지**)"*.

🔴 **P2 가 하려는 일은 "새로 만들기" 가 아니라 "이미 있는 것에 ERP 를 연결하기" 다.**
작지서는 이 사실을 모르고 쓰였다. (명세서 §4-7 도 `pricing_plans` 를 언급하지 않는다)

### (3) 그대로 착수하면 진실원이 셋

| # | 자리 | 지금 |
|---|---|---|
| 1 | `TenantDeviceService.GetLimitsForTier:662-669` | 코드 상수 |
| 2 | `pricing_plans.max_pc_devices` / `max_mobile_devices` | 본사 화면에서 수정 가능 |
| 3 | 🔴 **P2 가 새로 만들 "설정"** | ← 여기가 추가되면 **삼중** |

게다가 PI-17 에서 확인된 `SessionLimitMiddleware.TierLimits` 까지 세면 **넷**이다.

---

## 3. 터지면 무슨 일이 나나

- 본사 직원이 **백오피스 화면에서 한도를 올렸는데 고객 ERP 는 안 바뀐다.**
  (ERP 는 자기 설정을 보고, 백오피스는 `pricing_plans` 를 본다)
- 🔴 **돈을 더 받았는데 기기가 안 늘어난다** — 사장님이 *"과금에 연결된거니 더욱 촘촘하게"* 라고 한 자리에서
  가장 나쁜 형태의 사고다.
- 어느 숫자가 맞는지 **대조할 기준이 사라진다.** §5-A-3 이 요구한 *"사람이 확인할 수 있어야 한다"* 가 무너진다.

### ⚠️ 다만 그대로 `pricing_plans` 를 보게 하면 **다른 사고**가 난다

`pricing_plans` 는 **본사(백오피스) DB 에 있다.** 고객 ERP 는 로컬 DB 로 돌고 **본사 의존 0** 이 원칙이다
(헌법 #30 · #18 · #22 · 메모리 `architecture_local_db_tunnel`).
🔴 **ERP 가 실시간으로 본사를 조회하게 만들면 헌법 위반이고, 본사가 죽으면 고객이 일을 못 한다.**

⇒ 정답은 **"본사가 정하고, 웹훅으로 로컬에 내려보내 로컬 값을 읽는다"** 이다.
그 배관도 **이미 있다** — `WebhookInboundController.cs:103-120` 이 `local_subscription` 에
`max_users`, `extra_device_slots` 를 받아 쓰고 있다.

---

## 4. 어떻게 고치나

1. **새 설정 저장소를 만들지 않는다.** `local_subscription` 에 **기기 한도 칸 2개를 추가**하고
   (`max_pc_devices`, `max_mobile_devices`), **웹훅이 채우게** 한다. 배관은 이미 있다.
2. `GetLimitsForTier` 는 **폴백으로만** 남긴다 — 로컬 값이 없을 때만 쓰는 기본값.
   (헌법 #37 — *"안 읽힌다 ≠ 잔재"*. 지우지 않는다)
3. **본사 `pricing_plans` 가 유일한 원본**임을 명세서에 명문화한다. 헌법 #11 정합.
4. 🔴 **웹훅이 안 왔을 때 무엇을 쓰는지 정한다.** 신규 설치 직후·통신 두절 중에는 값이 없다.
   이 자리를 안 정하면 **설치 직후 고객이 basic 한도를 받는다.**
5. 작지서 P2 의 *"설정에서 읽기"* 를 **위 1~4 로 구체화**한 뒤 착수한다.

---

## 5. 판정

🟠 **P2 는 착수 전 설계 보완이 필요하다.**
"설정화" 라는 말만 있고 **저장 위치·동기화 경로·폴백**이 비어 있는데,
실측하면 **셋 다 이미 존재하는 배관으로 풀린다.**
모르고 새로 만드는 것이 **가장 나쁜 결과**이므로 이 건은 착수 직전에 닫아야 한다.
