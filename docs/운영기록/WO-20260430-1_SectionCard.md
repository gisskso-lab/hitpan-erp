# 작20260430-1 — SectionCard 'Class' 파라미터 미정의 (P0 빠른 트랙)

## 🔴 우선순위
P0 (빠른 트랙: 매니저 + CTO 검증, 4~12h)

## 발생 증상
다중 페이지 진입 시 런타임 예외:
```
System.InvalidOperationException: Object of type
'HitPan.Web.Components.Common.SectionCard' does not have a property
matching the name 'Class'.
```
- 빌드는 통과, 화면 진입 시 흰 페이지 또는 일부 영역 깨짐
- Blazor WebAssembly 렌더 단에서 발생

## 진범 추정
- SectionCard 컴포넌트 사용처에서 `Class="..."` 파라미터 전달 중인데
  컴포넌트가 해당 파라미터를 정의하지 않음
- **절대원칙 #12 위반: 인터페이스 변경 후 모든 구현체 grep 누락**

## 담당
- 메인: 프론트 매니저 (직접 처리, 어려운 영역)
- 검증: CTO Final Verifier (절대원칙 #12 사후분석 동행)

## 작업 범위
1. SectionCard 컴포넌트 정의 확인 (`HitPan.Web/Components/Common/SectionCard.razor`)
2. 사용처 grep — `<SectionCard ... Class=` 전수 조사
3. 두 가지 해결안 중 선택:
   - A안: SectionCard에 `[Parameter] public string? Class { get; set; }` 추가 + 렌더링 시 외부 클래스 적용
   - B안: 사용처에서 `Class=` → `class=` 또는 다른 정의된 파라미터로 변경
4. **헌법 4조 검증 (5중 만족 + 캡처)**

## 사후 분석 (CTO 동행)
- 어느 PR에서 인터페이스 변경/사용처 추가됐는지 git blame
- grep 누락 원인 = 매니저 검증 단계 빠뜨림? 검증팀 자동화 부재?
- 재발 방지: PR 템플릿에 "인터페이스 변경 시 grep 결과 첨부 의무" 추가

## 검증 보고서 양식
헌법 4조 5중 + 캡처 (CONSTITUTION_20260430.md 참조)

## SLA
- 매니저 1차 검증: 4h 이내
- CTO 3차 검증: 24h 이내 (빠른 트랙은 검증팀 사후 감사)
