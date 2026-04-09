---
name: 작업 지시서
about: Claude 기획설계팀이 작성하는 개발 작업 지시서
title: '[BE/FE/DB] 작업명'
labels: task
assignees: ''
---

## 🎯 작업 목표
<!-- 이 작업으로 무엇을 달성하는지 1~2문장으로 -->

## 📁 작업 대상 파일
```
src/
  HitPan.Application/
    Services/           ← 신규 또는 수정
  HitPan.Domain/
    Entities/           ← 신규 또는 수정
```

## 📋 구현 명세
<!-- 처리 순서, 비즈니스 로직, 사용할 테이블/컬럼 명시 -->

```csharp
// 구현 흐름 예시
1. 입력값 검증
2. 비즈니스 로직 처리
3. DB 저장
4. 이벤트 발행
```

## 🔗 참고 문서
- [ ] docs/design/hitpan_db_ddl_FINAL_v1.0.sql
- [ ] .cursorrules

## ⚠️ 주의사항
<!-- 이 작업에서 특히 주의할 설계 규칙 -->
- tenant_id는 JWT 클레임에서 추출
- 

## ✅ 완료 기준
- [ ] 기능 동작 확인
- [ ] 단위 테스트 최소 3개
- [ ] .cursorrules 규칙 준수 확인
- [ ] Claude 팀 코드 리뷰 완료

## 🏷️ 메타
- Phase:
- 레이어: BE / FE / DB
- 예상 소요: 
