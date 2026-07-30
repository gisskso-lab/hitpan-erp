# 작업지시서 20260425작9 — MudFileUpload MudBlazor 9.3 마이그레이션

## 0. 메타

| 항목 | 값 |
|---|---|
| **문서번호** | 20260425작9 |
| **발행일** | 2026-04-25 |
| **발행자** | PM 닥터스트레인지 |
| **A 책임자** | 프론트 매니저 (FE) |
| **결재 트랙** | **경량** (UI만, 보안·DB 영향 없음) |
| **EVF 영향** | 없음 |
| **예상 소요** | 2~3h |
| **선행 의존성** | 없음 |
| **트리거** | 사장님 헌법 #19 warnings 0 적용 후 잔존 9건 (`MUD0002`) |

## 1. 배경

사장님 헌법 #19 적용 시 MUD0002 경고 36건 → 9건(MudFileUpload만) 잔존.
임시로 csproj에 `<NoWarn>$(NoWarn);MUD0002</NoWarn>` 박았으나 **회피가 아닌 추적 약속**.
본 작업으로 정식 마이그레이션 후 NoWarn 제거.

## 2. 영향 파일 (5개)

| # | 파일 | 경고 패턴 |
|---|---|---|
| 1 | `Pages/Settings/PrintSettingsPage.razor` | ChildContent / MaximumFileSize |
| 2 | `Pages/Finance/TaxExportPage.razor` | ChildContent |
| 3 | `Pages/HR/LaborContractSignPage.razor` | ChildContent ×2 / MaximumFileSize ×2 |
| 4 | `Pages/Tax/CertificateUploadDialog.razor` | ChildContent |

## 3. 마이그레이션 가이드 (MudBlazor 9.3)

### 변경 전 (v7 패턴)
```razor
<MudFileUpload T="IBrowserFile" FilesChanged="OnSelected" MaximumFileSize="10485760">
    <ButtonTemplate>
        <MudButton>...</MudButton>
    </ButtonTemplate>
</MudFileUpload>
```

### 변경 후 (v9.3 패턴)
```razor
<MudFileUpload T="IBrowserFile" FilesChanged="OnSelected" MaxFileSize="10485760">
    <ActivatorContent>
        <MudButton>...</MudButton>
    </ActivatorContent>
</MudFileUpload>
```

주요 변경:
- `MaximumFileSize` → `MaxFileSize`
- `ButtonTemplate` / `ChildContent` → `ActivatorContent`
- 파일 검증은 MudBlazor가 자동 처리 (파일 크기 초과 시 콜백 미호출)

## 4. 완료 기준 (DoD)

- [ ] 5개 파일 모두 신 패턴으로 변경
- [ ] csproj `<NoWarn>` 항목에서 `MUD0002`와 `RZ10012` 모두 제거 (`ActivatorContent` 인식 RZ10012 7건도 같은 마이그레이션으로 해소)
- [ ] 빌드 errors 0 + warnings 0
- [ ] 사장님 PC에서 파일 업로드 동작 검증 (인감/인증서/세금자료 등 실 업로드 1회)

## 5. 일정

이번 주 완료 권고. Sprint 1 회고(5/2) 전 처리.

## 6. 참고
- MudBlazor 8.0 Migration Guide: https://github.com/MudBlazor/MudBlazor/issues/9656
- 검증팀 검토 후 머지
