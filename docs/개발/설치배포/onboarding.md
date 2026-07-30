# 개발자 온보딩 가이드
> 히트판 SaaS ERP | Claude 기획설계팀 작성

---

## 시작 전 필독

이 프로젝트는 **Claude 기획설계팀 + Cursor 개발팀** 협업 구조로 운영됩니다.

- **설계 결정권**: Claude 기획설계팀
- **구현 실행권**: Cursor 개발팀 (당신)
- **최종 승인권**: 오너

설계 변경이 필요하다고 판단되면 **임의로 변경하지 말고** PR 코멘트 또는 Issue에 제안을 남겨주세요. Claude 팀 검토 후 반영합니다.

---

## 필수 사전 숙지 문서 (순서대로 읽을 것)

1. **`.cursorrules`** — 개발 규칙 전체. 모든 코드는 이 파일 기준
2. **`docs/design/hitpan_db_ddl_FINAL_v1.0.sql`** — DB 스키마 전체
3. **`docs/design/`** — ERD, 설계 명세서, 아키텍처 PPT

---

## 작업 흐름

```
Claude 팀이 GitHub Issue에 작업 지시서 등록
    ↓
개발자가 feature 브랜치 생성
    feature/#이슈번호-작업명
    ↓
Cursor로 레포 열고 작업 지시서 + .cursorrules 참고해서 구현
    ↓
PR 생성 → Claude 팀 리뷰 요청
    ↓
리뷰 반영 → develop 머지
```

---

## 브랜치 규칙

```bash
# 기능 개발
git checkout -b feature/#001-purchase-receipt-service

# 긴급 수정
git checkout -b hotfix/#002-stock-ledger-null-fix
```

- `main` 직접 푸시 **금지**
- `develop` 직접 푸시 **금지**
- 반드시 PR → 리뷰 → 머지 순서

---

## Cursor 사용 팁

작업 지시서를 Cursor 채팅에 붙여넣을 때 이 형식을 씁니다:

```
아래 작업 지시서대로 구현해줘.
기존 코드 스타일과 .cursorrules 규칙을 반드시 따라줘.
@docs/design/hitpan_db_ddl_FINAL_v1.0.sql 참고해서 작업해줘.

--- 작업 지시서 ---
[Issue 내용 붙여넣기]
```

---

## 자주 하는 실수 TOP 5

1. **tenant_id를 파라미터로 받는 것** — JWT 클레임에서만 추출
2. **stock_ledger를 UPDATE하는 것** — INSERT ONLY, 취소는 역방향 INSERT
3. **금액에 double 쓰는 것** — 반드시 decimal
4. **암호화 컬럼 평문 저장** — Value Converter 미적용 PR은 즉시 반려
5. **draft 상태에서 원장 반영** — confirmed 전환 시점에만

---

## 코드 리뷰 요청 방법

PR을 올린 후, 리뷰가 필요한 코드를 Claude에게 붙여넣어 주세요.

```
아래 코드 리뷰해줘.
히트판 .cursorrules 기준으로 문제 있는 부분 찾아줘.

[코드 붙여넣기]
```
