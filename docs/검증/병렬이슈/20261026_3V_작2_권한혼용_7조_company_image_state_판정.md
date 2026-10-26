# [3-V] 작2 — 헌법 #7 심사: `GET api/settings/company-image-state` 조회 권한

> 보안상무 안철수 · 2026-10-26 · 읽기만(코드·시험·문서 수정 0줄 · git 0회 · 운영/고객PC 0회 · DB SELECT 0회)

## 머리 카드

| 칸 | 내용 |
|---|---|
| **판정** | 🔴 **불허** — ⒜「그 문만 역할(`system_admin`)로 넓히기」는 **채택 금지** |
| 근거 1 | **#7 위반은 아니다.** `system_admin` 은 **SaaS 계층 역할이 아니다** — ERP 토큰의 `account_type` 은 `tenant_admin`/`tenant_user` **둘뿐**이고(`AuthService.cs:1372`), `platform_admin`·`reseller_admin` 정책은 2026-06-18 격벽으로 제거됐다(`Program.cs:386-411` 주석). `system_admin` 은 **ERP 내부 직무역할 클레임**(`ClaimTypes.Role` = `employees.role`, 또는 옛 `users.role='TenantAdmin'` → `MapLegacyRole` 변환 `AuthService.cs:1443-1450`)이다. 레포 전체 `.sql` 에 `system_admin` **0건** ⇒ 계정계층 값으로 쓰인 적 없다 |
| 근거 2 | 🔴 **그런데 축이 다르다.** 그 문은 **계정계층 축**(부모/자식 · `account_type`)이고 화면 역할목록은 **직무역할 축**이다. 문을 역할 축으로 바꾸면 **`employees.role` 을 `tenant_admin`/`system_admin` 으로 받은 자식계정**이 **부모 전용 문**을 읽는다. `employees.role` 은 `varchar(30)` 자유값이고 **허용목록 검사 0건**(`EmployeeService.cs:520·572` 가 `request.Role` 을 그대로 넣는다) ⇒ 본인 승격은 아니나 **울타리를 「그럴 리 없다」로 세우는 자리**다 |
| 근거 3 | 🔴 **사장님 지시와 부딪친다.** 「설정관리 대메뉴 접근은 **부모계정만**」(2026-08-11 · `AccountTypeHelper.cs` 주석에 전문 보존) — 사이드바도 `AccountTypeHelper.IsTenantAdmin`(=`account_type`) 로 묶여 있다(`Sidebar.razor:44·62`). **문·메뉴는 같은 축인데 화면 하나만 다른 축**이다 ⇒ 틀린 쪽은 **문이 아니라 화면 역할목록**이다 |
| **대안 위험 순서** | 안전 → 위험: **⒝ 큰 경고만**(현 PM 지시 · 축 무변경) → **⒟ 화면을 `account_type` 축으로 정합**(권장 · 아래 §3) → **⒞ 역할문자열에서 `system_admin` 삭제**(🔴 고객 길 끊음 — §3) → **⒜ 문 넓히기**(불허) |
| ⚠️미측정 1 | **403 이 실제로 누구에게 나는지** — `role=system_admin` 이면 발급 시점에 `account_type=tenant_admin` 이라 **그 문을 통과한다**(§2). 403 은 `account_type` 이 `tenant_user` 인데 역할만 넓은 계정에서만 난다. **실계정 분포는 DB SELECT 금지(ACL)로 못 쟀다** |
| ⚠️미측정 2 | `TenantAdminOnly` **69곳 전수**(`src/HitPan.API/Controllers/` 30파일) 중 **설정·양식 축만** 화면 대조했다. 나머지는 **부분측정** |
| ⚠️미측정 3 | 실제 로그인 왕복(브라우저·터널)으로 403→`unknown` 을 **재현하지 않았다** — 코드 독해만이다 |

---

## 1. 돌려주는 값 — **업무 자료 아님 · 경로 누출 없음** 🟢

`SettingsController.cs:277-293` + `DescribeCompanyImage`(:296-325) 직접 확인.

- 응답 = `Dictionary<string,string>` · 키는 `CompanyImagePaths.Kinds` 의 **그림 종류**, 값은 **낱말 네 개 중 하나**(`present` · `missing` · `unusable` · `unknown`).
- 🟢 **경로·파일명·테넌트 식별자 0건** — 전체경로(`Path.GetFullPath`)는 **판정에만 쓰고 버린다.** 주석에도 *"경로는 내보내지 않는다"* 로 적혀 있고 **코드가 실제로 그렇다.**
- 🟢 `tenantId` 는 `HttpContext.Items["TenantId"]`(JWT 미들웨어) — **파라미터 수신 0건**(#2 정합). 비면 `Forbid()`.
- ⇒ **설정 상태**다. 업무 자료가 아니고 본사로 가지도 않는다(#18·#22 무관).
- 🔴 다만 **같은 화면의 옆 문** `GET api/settings/company`(`:54-55`)는 `SealUrl`·`LogoUrl` **경로 문자열을 그대로 돌려준다**(`PrintSettingsPage.razor:336-340` 이 받아 파일명까지 뽑는다). 이 문도 `TenantAdminOnly` 라 **지금은 같은 자격**이지만, ⒜ 로 축을 바꾸는 논의가 **이 문까지 번지면 경로가 새는 자리**다 ⇒ **⒜ 를 막아야 하는 두 번째 이유.**

## 2. `system_admin` 은 실제로 무엇을 할 수 있나 (세어 본 값)

| 확인한 자리 | 실측 |
|---|---|
| `Program.cs:374-388` | 역할 축 정책 6개(`SalesOnly`·`SalesManager`·`PurchaseOnly`·`AccountOnly`·`HROnly`·`AdminOnly`) **전부 `system_admin` 포함** · `AdminOnly` 는 **`system_admin` 단독** |
| `Program.cs:396-409` | `TenantOnly`·`TenantProfile`·`TenantAdminOnly` 는 **`account_type` 만** 본다 — 역할은 안 본다 |
| `FormTemplateController.cs:46·65·84·95·127·177·209` | 양식 템플릿 **쓰기·배경 업로드·삭제·미리보기 7문**이 `[Authorize(Roles="system_admin,tenant_admin")]` = **역할 축** |
| `FormTemplateController.cs:24·34` | 목록·기본값 **조회 2문은 `[Authorize]` 뿐** — 로그인만 하면 누구나 |
| `AuthService.cs:1362-1364` | 역할 클레임 = `employees.role` · **비면** `MapLegacyRole(users.role)` ⇒ `TenantAdmin`→`system_admin` |
| `UserService.cs:143·278·762` | `account_type` = `role==UserRole.TenantAdmin ? "tenant_admin" : "tenant_user"` ⇒ **두 값이 같이 정해진다** |
| `CompanyBootstrapProvisioner.cs:267·346·369` | 신규 설치 부모계정은 **`employees.role='tenant_admin'` + `account_type='tenant_admin'`** ⇒ **부모의 역할 클레임은 `tenant_admin`**, `system_admin` 이 아니다 |
| `EmployeePage.razor:138-141` | 화면 역할 선택은 **`TenantAdmin` / `sales_user` 둘뿐** ⇒ 화면으로는 `system_admin` 을 **못 만든다** |
| `installer/hitpan_db_clean.sql` `users` :8 | `account_type varchar(20) NOT NULL DEFAULT 'tenant_user'` · `DB-02_3tier_schema.sql:74-82` 에 **옛 대표 백필 `UPDATE` 0건** |

🔴 **따라서 결론 두 개.**
1. `system_admin` = **ERP 내부 직무역할**(옛 ERP 「관리자」의 새 이름). **SaaS 계층 아님** ⇒ 이름만으로 #7 을 들이대면 **오판**이다.
2. 🔴 **「`system_admin` 이면 403」은 조건부 참**이다 — `system_admin` 은 `employees.role` 이 비었을 때만 생기고 그때 `account_type` 은 `tenant_admin` 이라 **문을 통과한다.** 403 이 나는 건 **`account_type` 이 `tenant_user` 인데 역할만 넓은 계정**(옛 백필 안 된 대표 · API/일괄로 역할을 받은 자식)이다. **그 사람은 사장님 2026-08-11 지시대로라면 설정관리를 애초에 보면 안 되는 사람**이다 ⇒ **문을 넓혀 맞추는 방향이 거꾸로다.**

## 3. 대안 세 개 — 끊기는지 실측

| 안 | 고객 길 | 판정 |
|---|---|---|
| ⒜ 문만 역할로 넓힘 | 안 끊김 | 🔴 **불허** — 부모 전용 문을 자식 역할로 연다(§머리 근거 2·3) · 옆 문(`company`)으로 번지면 경로 누출(§1) |
| ⒝ 문 그대로 + **큰 경고**(현 PM 지시) | 안 끊김 · `unknown` 을 **모른다**고 말한다 | 🟢 **지금 그대로 진행 가능** — 축을 안 건드린다 |
| ⒞ 화면 역할목록에서 `system_admin` **문자열 삭제** | 🔴 **끊길 수 있다** — `employees` 행이 없거나 `role` 이 빈 **대표계정의 역할 클레임이 바로 `system_admin`**(`AuthService.cs:1362`). 지우면 **그 대표가 양식 편집에서 쫓겨난다**(문은 통과하는데 화면이 막는다) | 🔴 **문자열 수술 금지** |
| ⒟ (권장) 화면을 **`account_type` 축으로 정합** — `FormTemplatesPage.razor:3`·`FormTemplateEditorDialog` 가드를 사이드바·문과 **같은 판정**(`AccountTypeHelper.IsTenantAdmin`)으로 | 안 끊김 — 역할이 `system_admin` 이든 `tenant_admin` 이든 **부모면 통과**, 자식이면 애초에 못 들어온다 ⇒ **403 갈래가 구조적으로 사라진다** | 🟡 **별건 등재**(이번 판 범위 밖 · 화면 7문 역할 축까지 같이 봐야 한다) |

🔴 메뉴 실측: `/settings/form-templates` 링크는 `@if (_isTenantAdmin)` 안에 있다(`Sidebar.razor:44·62`) ⇒ **메뉴로 들어오는 사람은 전부 `account_type=tenant_admin`** = 403 을 안 받는다. 403 은 **주소를 직접 쳐서 들어온 경우**에만 난다. ⇒ **「고객 길이 끊겼다」는 전제 자체가 메뉴 경로에서는 성립하지 않는다.**

## 4. 403 갈래가 고객 길을 끊는 다른 자리 (부분측정)

- `TenantAdminOnly` 는 `src/HitPan.API/Controllers/` 에 **69곳 · 30파일**. 🔴 **같은 축 불일치(화면=역할 · 문=계정계층)가 설정관리 9화면에 공통**이다 — `PrintSettingsPage`·`BackupPage`·`DataResetPage`·`EmailSettingsPage`·`LogPage`·`PermissionPage`·`UserInfoPage`·`Settings.razor` 가 `Roles="system_admin,tenant_admin,TenantAdmin"` 인데 서버는 `TenantAdminOnly`. **메뉴가 `account_type` 으로 묶여 있어 지금까지 안 터졌다**(사이드바가 덮개 역할).
- 🔴 **이번 판 새 문의 비대칭 적발**: 같은 양식 화면에서 **배경 그림 올리기 `POST api/settings/form-templates/background`(:176-177)는 역할 축**, **회사 그림 올리기 `POST api/settings/company-image`(:219-220)는 계정계층 축**이다. **같은 화면의 같은 종류 올리기 두 문이 서로 다른 축** ⇒ 느슨한 쪽(배경)이 **자식계정에게 열려 있을 수 있다.** 별건 등재 권고(이번 판 봉합 범위 밖 · 조치 제안 아님 · 적발만).
- ⚠️ `SettingsController.cs:110 POST validate-unit-price` 는 정책 없이 클래스 `[Authorize]` 뿐 — 값 검증 전용으로 보이나 **본문 미독** ⇒ ⚠️미측정.

## 5. 이번 판에 요구하는 것

1. **⒜ 채택 금지** — `company-image-state` 는 `TenantAdminOnly` **그대로 둔다.**
2. **⒝(큰 경고)만 이번 판에 구현** — C-3 의 「권한 맞추기」 부분은 **보류**. 사장님께는 「권한을 안 맞춰도 **메뉴로 들어온 대표는 403 을 안 받는다**」가 실측값이라고 보고할 것.
3. **⒟·§4 비대칭 2건은 별건 등재** — 일정표 영향 0 (등재만 · 이번 판 착수 금지).
