# 🟡 작지서 — expense_account_map 어드민 화면 (작H 범위 확장)

> **문서번호**: 20260526작3
> **결재**: 2026-05-25 PM 일괄 결재 P1-4
> **담당**: DB 매니저 + 백엔드 매니저 + 프론트 매니저
> **가도 시각**: 작H (회계 처리) 발행 시 범위 확장
> **마감**: 1단계 ERP 완성(6/29) 가도 내 포함

---

## 🚨 한 줄 결산

**세법 개정 매년 12월 → 코드 하드코딩 매핑은 매번 빌드·배포 사고. 어드민이 직접 매핑 변경 가능 화면으로 빌드 회피.**

---

## 📋 가도 영역

### Step 1: DB 매니저 (작H 발행 시)

#### DDL 작지서
```sql
-- 경비 계정 매핑 테이블 신설 (테넌트별)
CREATE TABLE expense_account_map (
  map_id INT UNSIGNED NOT NULL AUTO_INCREMENT,
  tenant_id INT UNSIGNED NOT NULL,
  expense_category VARCHAR(100) NOT NULL COMMENT '경비 분류 (예: 차량유지비·식대·접대비)',
  account_code VARCHAR(20) NOT NULL COMMENT '회계 계정코드 (예: 522·524·813)',
  account_name VARCHAR(100) NOT NULL COMMENT '계정명',
  tax_deductible TINYINT(1) NOT NULL DEFAULT 1 COMMENT '세무공제 가능 여부',
  is_active TINYINT(1) NOT NULL DEFAULT 1,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (map_id),
  UNIQUE KEY uk_tenant_category (tenant_id, expense_category),
  INDEX idx_tenant_active (tenant_id, is_active),
  CONSTRAINT fk_expense_map_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(tenant_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
COMMENT='경비 분류 → 회계 계정 매핑 (테넌트별, 어드민 직접 관리)';
```

#### 기본 시드 데이터
```sql
-- 모든 신규 테넌트에 자동 시드 (트리거 또는 SaaS 가입 시 INSERT)
INSERT INTO expense_account_map (tenant_id, expense_category, account_code, account_name, tax_deductible)
VALUES
  (?, '차량유지비', '522', '차량유지비', 1),
  (?, '식대', '524', '복리후생비', 1),
  (?, '접대비', '813', '기업업무추진비', 1),
  (?, '교통비', '522', '여비교통비', 1),
  (?, '통신비', '514', '통신비', 1),
  (?, '소모품비', '530', '소모품비', 1),
  (?, '임차료', '519', '임차료', 1),
  (?, '광고선전비', '833', '광고선전비', 1),
  (?, '수수료', '831', '지급수수료', 1),
  (?, '기타경비', '999', '기타잡비', 0);
```

---

### Step 2: 백엔드 매니저 (작H 발행 시)

#### API 컨트롤러 (`ExpenseAccountMapController.cs`)
```csharp
[ApiController]
[Route("api/admin/expense-account-map")]
[Authorize(Policy = "TenantAdmin")]
public class ExpenseAccountMapController : ControllerBase
{
    private readonly IExpenseAccountMapService _service;
    private readonly ILogger<ExpenseAccountMapController> _logger;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var maps = await _service.GetAllByTenantAsync();
        return Ok(maps);
    }

    [HttpPost]
    public async Task<IActionResult> Create(ExpenseAccountMapDto dto)
    {
        var result = await _service.CreateAsync(dto);
        return Ok(result);
    }

    [HttpPut("{mapId}")]
    public async Task<IActionResult> Update(int mapId, ExpenseAccountMapDto dto)
    {
        var result = await _service.UpdateAsync(mapId, dto);
        return Ok(result);
    }

    [HttpDelete("{mapId}")]
    public async Task<IActionResult> Delete(int mapId)
    {
        // 소프트 삭제: is_active = 0
        await _service.DeactivateAsync(mapId);
        return Ok();
    }
}
```

#### 경비처리 서비스 분기 (`ExpenseService.cs`)
```csharp
public async Task<Result> RegisterExpenseAsync(ExpenseRegisterDto dto)
{
    // 어드민 매핑 조회 (테넌트별)
    var map = await _expenseMapService.GetByCategoryAsync(dto.ExpenseCategory);
    if (map == null)
    {
        return Result.Fail($"경비 분류 '{dto.ExpenseCategory}' 매핑 없음. 어드민 설정 필요");
    }

    // 매핑된 계정으로 회계 분개 INSERT (헌법 #3 INSERT ONLY)
    await _journalService.InsertJournalAsync(new JournalLineDto
    {
        AccountCode = map.AccountCode,
        AccountName = map.AccountName,
        Amount = dto.Amount,
        TaxDeductible = map.TaxDeductible,
        // ...
    });

    return Result.Ok();
}
```

---

### Step 3: 프론트 매니저 (작H 발행 시)

#### Blazor 페이지 (`Pages/Admin/ExpenseAccountMap.razor`)
```razor
@page "/admin/expense-account-map"
@attribute [Authorize(Policy = "TenantAdmin")]

<MudTable Items="@Maps" Hover="true">
  <HeaderContent>
    <MudTh>경비 분류</MudTh>
    <MudTh>계정 코드</MudTh>
    <MudTh>계정명</MudTh>
    <MudTh>세무공제</MudTh>
    <MudTh>활성</MudTh>
    <MudTh>관리</MudTh>
  </HeaderContent>
  <RowTemplate>
    <MudTd>@context.ExpenseCategory</MudTd>
    <MudTd>@context.AccountCode</MudTd>
    <MudTd>@context.AccountName</MudTd>
    <MudTd><MudSwitch @bind-Checked="context.TaxDeductible" /></MudTd>
    <MudTd><MudSwitch @bind-Checked="context.IsActive" /></MudTd>
    <MudTd>
      <MudButton OnClick="() => EditMap(context)">수정</MudButton>
      <MudButton OnClick="() => DeleteMap(context.MapId)">삭제</MudButton>
    </MudTd>
  </RowTemplate>
</MudTable>

<MudButton OnClick="AddMap" Color="Color.Primary">새 매핑 추가</MudButton>
```

---

## 🎯 정합성 게이트

### 헌법 정합
- ✅ 헌법 #3 (INSERT ONLY 원장) — journal_lines INSERT만
- ✅ 헌법 #11 (권한 어드민 직접) — 매핑 어드민 직접 관리
- ✅ 헌법 #13 (DESCRIBE 의무)
- ✅ 헌법 #17 (ENGINE=InnoDB 명시)
- ✅ 헌법 #18 (본사 데이터 0) — tenant_id 격리

---

## 📅 가도 스케줄

| 시각 | 영역 | 담당 |
|---|---|---|
| 작H 발행 시 | DDL + 기본 시드 | DB 매니저 |
| 작H +1일 | API 컨트롤러 + 경비처리 분기 | 백엔드 매니저 |
| 작H +2일 | Blazor 어드민 화면 | 프론트 매니저 |
| 작H +3일 | 검증팀장 5중 검증 | 검증팀장 |

---

## 💎 ERP 매니저 30년 정합

- 매년 12월 세법 개정 시 코드 변경 → 빌드·배포 사고 빈번
- 본 작지서 적용 시 어드민이 직접 매핑 추가·변경 → 빌드 0회
- 베타 30곳 출시 후 12월 첫 세법 개정 시점에 효과 검증

**작성**: 2026-05-25 PM 브라운킴
**상태**: 작H 발행 시 범위 확장 대기
