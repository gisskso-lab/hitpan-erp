# 히트판 디자인 시스템 v1 (2026-04-22)

**철학:** *"쉬움으로 이긴 히트판의 DNA + 현대적 통일성"*

신규 페이지부터 필수 적용. 기존 페이지는 v1.1에서 점진 교체.

---

## 🎨 디자인 토큰

### 색상

| 용도 | 변수 | 값 | 사용처 |
|---|---|---|---|
| Primary | `--hitpan-primary` | `#0F6E56` | 브랜드 | 매출·핵심 CTA |
| Info | `--hitpan-info` | `#1976D2` | 보조 정보 |
| Warning | `--hitpan-warning` | `#F57C00` | 매입·주의 |
| Error | `--hitpan-error` | `#D32F2F` | 위험·미수금 |
| Success | `--hitpan-success` | `#2E7D32` | 완료·승인 |
| Purple | `--hitpan-purple` | `#7B1FA2` | 결재 |

### 간격 (8점 그리드)

```css
--hitpan-gap-xs: 4px    /* 아이콘↔텍스트 */
--hitpan-gap-sm: 8px    /* 인접 요소 */
--hitpan-gap-md: 16px   /* 섹션 내부, 카드 간격 */
--hitpan-gap-lg: 24px   /* 섹션 간 */
--hitpan-gap-xl: 32px   /* 페이지 상하 */
```

### 보더 & 반경

- 보더: `--hitpan-border` = `0.5px solid var(--mud-palette-lines-default)`
- 반경: `--hitpan-radius-lg` = `12px` (카드 기본)

---

## 🧩 재사용 컴포넌트

### 1. PageHeader

페이지 최상단 — 제목 + 부제 + 액션 버튼 영역.

```razor
<PageHeader Title="재고 현황" Subtitle="전체 창고의 현재 재고 및 가용 수량">
    <Actions>
        <MudButton Variant="Variant.Filled" Color="Color.Primary">신규</MudButton>
        <MudButton Variant="Variant.Outlined">엑셀</MudButton>
    </Actions>
</PageHeader>
```

### 2. KpiCard

좌측 컬러바 + 아이콘 + 라벨 + 값 + 부가설명. 대시보드 스타일 표준.

```razor
<KpiCard Color="KpiCard.KpiColor.Primary"
         Icon="@Icons.Material.Filled.PointOfSale"
         Label="오늘 매출"
         Value="@FormatAmount(x)"
         EmphasizeValue="true"
         Sub="전일 대비 +12%" />
```

**KpiColor 6종:** Primary / Info / Warning / Error / Success / Purple

### 3. SectionCard

페이지 내 영역 구분용 카드. 테이블·차트·폼의 공통 래퍼.

```razor
<SectionCard Title="월별 추이">
    <ApexChart ... />
</SectionCard>

<SectionCard Variant="SectionCard.SectionVariant.Flat">
    <MudTable ... />  @* 테이블 꽉 채울 때 *@
</SectionCard>
```

**Variant 3종:** Default(패딩16) / Tight(8) / Flat(0)

### 4. EmptyState (기존)

데이터 없을 때 안내 카드. 이미 구현 완료.

### 5. ConfirmDialog (기존)

삭제·중요 변경 확인 다이얼로그.

---

## 📐 페이지 표준 구조

```
┌──────────────────────────────────────────────────┐
│ PageHeader (Title + Subtitle + Actions)           │ ← 필수
├──────────────────────────────────────────────────┤
│ MudGrid > KpiCard * N (선택)                      │
├──────────────────────────────────────────────────┤
│ SectionCard [필터 영역] (선택)                      │
├──────────────────────────────────────────────────┤
│ SectionCard(Flat) + MudTable (주 콘텐츠)           │
└──────────────────────────────────────────────────┘
```

---

## ✅ MudBlazor 컴포넌트 표준

| 컴포넌트 | 표준 옵션 |
|---|---|
| `MudTable` | `Dense="true" Hover="true" Striped="true" FixedHeader="true"` |
| `MudTextField` | `Variant="Variant.Outlined" Margin="Margin.Dense"` |
| `MudSelect` | `Variant="Variant.Outlined" Margin="Margin.Dense"` |
| `MudDatePicker` | `DateFormat="yyyy-MM-dd" Variant="Variant.Outlined" Margin="Margin.Dense"` |
| `MudPaper` | **인라인 스타일 금지** — 반드시 SectionCard 사용 |
| 로딩 표시 | **MudProgressLinear 단일 표준** (Circular 금지) |

---

## 🚫 금지 사항

| 금지 | 사용 |
|---|---|
| `Style="border:0.5px solid..."` 인라인 복붙 | SectionCard / 클래스 |
| `Typo.h5 / h4` 페이지 제목 | `PageHeader.Title` (h1, 18px) |
| `MudProgressCircular` 페이지 로딩 | `MudProgressLinear` 통일 |
| 색상 HEX 직접 입력 | CSS 변수 `var(--hitpan-primary)` |
| 제멋대로 마진 `mb-2/3/4/5` 혼용 | `--hitpan-gap-*` 토큰 참조 |

---

## 📋 적용 상태 (2026-04-22)

| 페이지 | 상태 |
|---|---|
| Dashboard (대시보드) | ✅ 완료 (시범) |
| Stock / Inventory | 🔴 대기 |
| Sales / Purchase | 🔴 대기 |
| Finance | 🔴 대기 |
| HR | 🔴 대기 |
| Settings | 🔴 대기 |

**v1.1 백로그:** 40+ 페이지 순차 교체
