# 23. MdbMigration.razor 전수 정독서 (344줄, 세미콜론·괄호까지)

**파일:** `src/HitPan.Web/Pages/Settings/MdbMigration.razor`

---

## 1. 디렉티브 (L:1-7)

```razor
@page "/settings/mdb-migration"
@namespace HitPan.Web.Pages.SettingsUi
@attribute [Authorize]
@inject HttpClient Http
@inject ISnackbar Snackbar
```

---

## 2. UI 헤더 (L:9-19)

```razor
<PageTitle>데이터 이관</PageTitle>

<div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:16px">
    <MudText Typo="Typo.h6">기존 히트판 데이터 이관</MudText>
    <MudText Style="font-size:12px;color:var(--mud-palette-text-secondary)">
        기존 히트판의 데이터를 새 히트판으로 간편하게 가져옵니다
    </MudText>
</div>

<MudAlert Severity="Severity.Warning" Class="mb-4" Dense="true" Icon="@Icons.Material.Filled.Warning">
    이관 전 반드시 기존 데이터를 백업하세요. 중복 데이터가 생길 수 있습니다.
</MudAlert>
```

---

## 3. 입력 폼 (L:22-57)

### 3.1 MDB 폴더 경로 (xs=12, md=6)
```razor
<MudTextField T="string" Label="MDB 폴더 경로" @bind-Value="_folderPath"
              Variant="Variant.Outlined" Margin="Margin.Dense"
              Placeholder="예: C:\HITWIN"
              HelperText="기존 히트판이 설치된 폴더 경로를 입력하세요 (보통 C:\HITWIN)" />
```

### 3.2 MDB 비밀번호 (핫픽스 2026-05-13, xs=12, md=2)
```razor
<MudTextField T="string" Label="MDB 비밀번호" @bind-Value="_mdbPassword"
              Variant="Variant.Outlined" Margin="Margin.Dense"
              InputType="InputType.Password"
              HelperText="MDB에 비번이 걸려있으면 입력 (없으면 비워두세요)" />
```

### 3.3 미리보기 버튼 (xs=12, md=2)
```razor
<MudButton Variant="Variant.Outlined" Color="Color.Primary"
           StartIcon="@Icons.Material.Filled.Search"
           OnClick="PreviewAsync"
           Disabled="@(_loading || string.IsNullOrWhiteSpace(_folderPath))"
           Style="height:40px;margin-top:4px;width:100%">
    미리보기
</MudButton>
```

### 3.4 이관 시작 버튼 (xs=12, md=2)
```razor
<MudButton Variant="Variant.Filled" Color="Color.Primary"
           StartIcon="@Icons.Material.Filled.PlayArrow"
           OnClick="StartMigrationAsync"
           Disabled="@(_loading || _previewResult == null)"
           Style="height:40px;margin-top:4px;width:100%">
    이관 시작
</MudButton>
```

---

## 4. 진행 상태 (L:59-66)

```razor
@if (_loading)
{
    <MudProgressLinear Indeterminate="true" Color="Color.Primary" Class="mb-4" />
    <MudText Typo="Typo.body2" Align="Align.Center" Class="mb-4" Style="color:var(--mud-palette-text-secondary)">
        @_statusMessage
    </MudText>
}
```

⚠️ **프론트 함정 #2 — Sticky 아님 → 스크롤 강제**

---

## 5. 결과 2단 (L:68-151)

### 좌측: 기존 히트판 데이터 (L:73-101)
```razor
<MudPaper Elevation="0" Class="pa-4" Style="...">
    <MudText Typo="Typo.subtitle2" Class="mb-3">
        <MudIcon Icon="@Icons.Material.Filled.Storage" Size="Size.Small" Class="mr-1" />
        기존 히트판 데이터
    </MudText>

    @if (_previewResult != null)
    {
        <MudTable Items="_previewResult" Dense="true" Hover="true" Elevation="0" ...>
            <HeaderContent>
                <MudTh>테이블명</MudTh>
                <MudTh Style="text-align:right">레코드 수</MudTh>
            </HeaderContent>
            <RowTemplate>
                <MudTd DataLabel="테이블명">@context.Key</MudTd>
                <MudTd DataLabel="레코드 수" Style="text-align:right">
                    @context.Value.ToString("N0")
                </MudTd>
            </RowTemplate>
        </MudTable>

        <MudText Typo="Typo.body2" Class="mt-2" Style="...">
            총 @_previewResult.Values.Sum().ToString("N0") 건
        </MudText>
    }
</MudPaper>
```

### 우측: 이관 결과 (L:104-151)
- MudIcon = `Icons.Material.Filled.CloudUpload`
- Status 아이콘 분기:
  - `context.Count > 0` → `CheckCircle` (Success)
  - else → `Remove` (Default)

---

## 6. @code 상태 변수 (L:155-161)

```csharp
private string _folderPath = @"C:\HITWIN";
private string _mdbPassword = ""; // 핫픽스 2026-05-13
private bool _loading;
private string _statusMessage = "";
private Dictionary<string, int>? _previewResult;
private MigrationResult? _migrationResult;
private List<MigrationRow> _migrationRows = new();
```

---

## 7. PreviewAsync (L:164-201)

```csharp
private async Task PreviewAsync()
{
    try
    {
        _loading = true;
        _statusMessage = "기존 데이터를 분석하는 중...";
        _migrationResult = null;
        _migrationRows.Clear();
        StateHasChanged();

        var url = $"api/migration/legacy-mdb/preview?folderPath={Uri.EscapeDataString(_folderPath)}";
        if (!string.IsNullOrEmpty(_mdbPassword))
        {
            url += $"&mdbPassword={Uri.EscapeDataString(_mdbPassword)}";
        }
        _previewResult = await Http.GetFromJsonAsync<Dictionary<string, int>>(url);

        if (_previewResult == null || _previewResult.Count == 0)
        {
            Snackbar.Add("기존 히트판 데이터를 찾을 수 없습니다.", Severity.Warning);
        }
        else
        {
            Snackbar.Add($"총 {_previewResult.Values.Sum():N0}건 발견", Severity.Success);
        }
    }
    catch (Exception ex)
    {
        Snackbar.Add($"미리보기 실패: {ex.Message}", Severity.Error);
        _previewResult = null;
    }
    finally
    {
        _loading = false;
        _statusMessage = "";
    }
}
```

⚠️ **백엔드 함정 #1 — finally에 `_mdbPasswordContext.Value = null` 누락** (서비스 측 PreviewCoreAsync에서)

---

## 8. StartMigrationAsync — 백그라운드 폴링 (L:203-299)

### 8.1 Start (L:213-227)
```csharp
var payload = new { folderPath = _folderPath, mdbPassword = string.IsNullOrEmpty(_mdbPassword) ? null : _mdbPassword };
var startResp = await Http.PostAsJsonAsync("api/migration/legacy-mdb/start", payload);
if (!startResp.IsSuccessStatusCode) { ... return; }
var startBody = await startResp.Content.ReadFromJsonAsync<StartResponse>();
var jobId = startBody?.JobId;
if (string.IsNullOrEmpty(jobId)) { ... return; }
```

### 8.2 Poll 루프 (L:229-287)
```csharp
var maxAttempts = 900; // 30분 = 1800초 ÷ 2초
for (int i = 0; i < maxAttempts; i++)
{
    await Task.Delay(2000);
    var statusResp = await Http.GetAsync($"api/migration/legacy-mdb/status/{jobId}");
    if (!statusResp.IsSuccessStatusCode) { continue; }
    var status = await statusResp.Content.ReadFromJsonAsync<JobStatus>();
    if (status is null) continue;

    _statusMessage = $"[{status.Status}] {status.CurrentStep} (경과 {status.ElapsedSeconds}초)";
    StateHasChanged();

    if (status.Status == "completed" && status.Result is not null)
    {
        // 16개 필드 복사
        // _migrationRows = new List<MigrationRow> { new("업체(거래처)", ...), ... };
        return;
    }
    if (status.Status == "failed")
    {
        Snackbar.Add($"이관 실패: {status.ErrorMessage}", Severity.Error);
        return;
    }
}
Snackbar.Add("폴링 타임아웃 (30분). 백엔드 로그를 확인하세요.", Severity.Warning);
```

### 8.3 16개 MigrationRow 매핑 (L:259-277)
```csharp
_migrationRows = new List<MigrationRow>
{
    new("업체(거래처)",      _migrationResult.Partners),
    new("상품(품목)",        _migrationResult.Items),
    new("BOM(자재명세서)",    _migrationResult.BomHeaders),
    new("사원",              _migrationResult.Employees),
    new("거래명세 매입(K2)",   _migrationResult.PurchaseOrders),
    new("거래명세 매출(K2)",   _migrationResult.SalesOrders),
    new("매입발주(IU)",       _migrationResult.PurchaseOrdersFromIU),
    new("매출주문(IO)",       _migrationResult.SalesOrdersFromIO),
    new("세금계산서(TX)",     _migrationResult.TaxInvoices),
    new("재고원장(입출고)",    _migrationResult.StockLedger),
    new("수금",              _migrationResult.Collections),
    new("경비(현금출납)",     _migrationResult.Cashbook),
    new("전표(비용처리)",     _migrationResult.Expenses),
    new("어음(EU+EQ)",       _migrationResult.Bills),
    new("카드결제(CD)",       _migrationResult.CardPayments),
    new("은행거래(BANKF)",    _migrationResult.BankTransactions),
};
```

---

## 9. 내부 DTO (L:301-342)

### StartResponse (L:301-305)
```csharp
private class StartResponse
{
    public string JobId { get; set; } = "";
    public string Status { get; set; } = "";
}
```

### JobStatus (L:307-315)
```csharp
private class JobStatus
{
    public string JobId { get; set; } = "";
    public string Status { get; set; } = "";
    public string CurrentStep { get; set; } = "";
    public int ElapsedSeconds { get; set; }
    public MigrationResult? Result { get; set; }
    public string? ErrorMessage { get; set; }
}
```

### MigrationResult (L:320-339) — 16 int + Total
```csharp
private class MigrationResult
{
    public int Partners { get; set; }
    public int Items { get; set; }
    public int BomHeaders { get; set; }
    public int Employees { get; set; }
    public int PurchaseOrders { get; set; }
    public int SalesOrders { get; set; }
    public int StockLedger { get; set; }
    public int Collections { get; set; }
    public int Cashbook { get; set; }
    public int Expenses { get; set; }
    public int PurchaseOrdersFromIU { get; set; }
    public int SalesOrdersFromIO { get; set; }
    public int TaxInvoices { get; set; }
    public int Bills { get; set; }
    public int CardPayments { get; set; }
    public int BankTransactions { get; set; }
    public int Total { get; set; }  // 현재 미사용
}
```

### MigrationRow record (L:342)
```csharp
private record MigrationRow(string Name, int Count);
```

---

## 10. Snackbar 메시지 전수 11종

| line | 심각도 | 메시지 |
|---|---|---|
| 184 | Warning | `기존 히트판 데이터를 찾을 수 없습니다.` |
| 188 | Success | `총 {count}건 발견` |
| 193 | Error | `미리보기 실패: {ex.Message}` |
| 218 | Error | `시작 실패 ({statusCode}): {err}` |
| 225 | Error | `잡 ID를 받지 못했습니다.` |
| 238 | (상태) | `진행률 조회 일시 실패 (재시도 {i+1}/{maxAttempts})` |
| 245 | (상태) | `[{Status}] {CurrentStep} (경과 {ElapsedSeconds}초)` |
| 279 | Success | `이관 완료! 총 {total:N0}건 (경과 {elapsedSeconds}초)` |
| 284 | Error | `이관 실패: {ErrorMessage}` |
| 288 | Warning | `폴링 타임아웃 (30분). 백엔드 로그를 확인하세요.` |
| 292 | Error | `이관 오류: {ex.Message}` |

---

## 11. 헌법 #14 검증 (Razor C# raw string 금지)

```bash
grep -n '"""' MdbMigration.razor
```
**결과: 0건. ✅ 헌법 #14 준수.** (C# 섹션 내 `@code` 안에도 `"""` 미사용)
