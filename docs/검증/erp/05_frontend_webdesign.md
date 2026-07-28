# 05. 프론트 매니저 + 수석 웹디자이너 정독 보고서

**작성:** 프론트 매니저 (Meta 창립, Harvard) + 웹디자이너 (RCA + Pratt, Apple 30년)
**일자:** 2026-05-14 새벽

---

## 1. UX 함정 3개

### 함정 #1: 진행률 메시지 무한 반복 (L:245)
```csharp
_statusMessage = $"[{status.Status}] {status.CurrentStep} (경과 {status.ElapsedSeconds}초)";
```
- 2초 폴링 × 900회(30분) 같은 메시지 반복
- **사용자는 어느 테이블까지 됐는지 모름**
- 좀비 롤백(백엔드 락 대기) 상태에서도 같은 메시지 — `failed` 상태 도달 불가
- 5/14 새벽 사고: 사장님 "5분 지나도 안 끝나" 진단 = 정확히 이 함정

### 함정 #2: 스크롤 강제 (L:59-66)
```razor
@if (_loading) {
    <MudProgressLinear Indeterminate="true" ... />
    <MudText>@_statusMessage</MudText>
}
```
- 진행 바·메시지 Sticky 미적용
- 미리보기 테이블(L:82-98) 확인 후 [이관 시작] 클릭 → 진행 메시지 보려면 **스크롤업 필수**
- **한 화면 완결 원칙 위반** (사장님 격언)

### 함정 #3: 부분 실패 표시 불가 (L:259-277)
- 완료 후에만 결과 테이블 표시
- 마이그 중 "건수 0인 테이블"과 "실패"를 구분 못 함
- 옵션 B(테이블별 진행률)에서 즉시 가시화 필요

---

## 2. 진행률 정직성 (5/14 새벽 사고)

사장님: "5분 경과 → 건수 0" → 좀비 롤백 의심

**현 UI 한계:**
- `status == "completed"` 도달 전까지 결과 정보 0
- 백엔드 `lock_wait=600`(10분) 대기 시 폴링은 계속 `running`
- 사용자: "정말 진행 중인가? 아니면 락 대기인가?" 구분 불가

**권고:** 폴링 응답에 `currentTable`, `tablesCompleted`, `tablesFailed` 추가. 테이블별 카드 실시간 표시.

---

## 3. 디자인 일관성

✅ 충족:
- MudBlazor 일관성 OK
- 헌법 #14 (Razor C# raw string 금지) 준수 (L:302-305는 C# 섹션이라 허용)

⚠️ 개선:
- BackupPage는 dialog 사용 / MdbMigration은 인라인 — 아키텍처 일관성 (낮은 중요도)

---

## 4. 옵션 B UI 권고 (5단 구조)

```
┌─────────────────────────────────────────┐
│ [Sticky 헤더] 전체: 12/16 테이블 (75%)  │
├─────────────────────────────────────────┤
│ [완료] Partner (12,450건)               │
│ [진행] Item (5,230 / 18,900 27%)        │
│ [대기] BOM, Employees, ...              │
│ [실패] (없음)                           │
└─────────────────────────────────────────┘
```

체크리스트:
1. 폴링 응답 DTO 확장: `currentTable`, `processedCount`, `totalCount`, `failedTables[]`
2. UI: 테이블별 MudLinearProgress
3. 에러: `failedTables` 배열에 장애 테이블명+이유
4. 완료 후: Status="success"/"partial"/"failed" 구분

---

## 5. 서브에이전트(프론트 5명) 분담

| 역할 | 담당 | 파일 |
|---|---|---|
| 프론트 아키텍트 | 옵션 B 폴링 DTO 설계 + API 스펙 | MigrationController JobStatus 확장 |
| 웹 개발자 #1 | 테이블별 진행 카드 컴포넌트 | MdbMigration.razor L:60-66 교체 |
| 웹 개발자 #2 | Sticky 헤더 + 스크롤 레이아웃 | MigrationProgressHeader.razor 신규 |
| UX 리드 | "좀비 상태" 감지 UI (5분 경과·진도 변화 0) | 타이머 + 차이 감지 |
| QA / 웹퍼블리셔 | 반응형(모바일) + 접근성 | L:72-94 미디어쿼리 |

---

## 결론

폴링 패턴은 안전(524 회피)하나 테이블별 미표시 = 좀비 롤백 구분 불가. 옵션 B 구현 시 Sticky 헤더 + 테이블별 카드 4-5개 → "지금 어디?" 해결.
