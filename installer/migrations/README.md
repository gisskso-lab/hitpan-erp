# ⚠️ 이 폴더는 마이그레이션 자리가 **아니다**

> 발견·정리: 2026-08-12 (사장님 실측 적발)

## 고객에게 실제로 가는 자리는 여기다

```
src/HitPan.API/Migrations/SQL/DB-NN_<이름>.sql
```

`HitPan.API.csproj` 가 **그 폴더만** 게시물(payload)로 복사한다.
게시 워크플로(`deploy-update.yml`)도 그 폴더만 세어 `requiresMigration` 과 대조하고,
`scripts/ddl-smoke-test.sh` 도 그 폴더 기준으로 clean DDL 시드와 맞춰 본다.

**이 폴더(`installer/migrations/`)에 SQL 을 두면 배포본에 안 실린다.**
게시해도, 고객이 업데이트를 받아도, **DB 는 안 바뀐다.**

## 실제로 난 사고 (2026-08-12)

AI 연동 3사 확장(DB-91)을 이 폴더에 두고 1.2.71 을 게시했다.
샌드박스가 업데이트까지 정상으로 받았는데 —

> **AI 도우미 연동** 화면: *"연동 상태를 불러오지 못했습니다"* · 키 입력칸 안 나옴

새 컬럼을 읽는 SQL 이 컬럼이 없어 500 을 냈다.
**빌드 0/0 · 시험 286건 · ddl-smoke PASS · 게시 워크플로 success** — 전부 통과했는데도 그랬다.
개발 PC 에서는 손으로 마이그를 돌려놔서 멀쩡했다. **고객 화면을 열어야만 드러났다.**

⇒ 지금은 `src/HitPan.API/Migrations/SQL/DB-91_ai_provider_3way.sql` 로 옮겼고,
`MigrationLocationGuardTests` 가 이 폴더에 `.sql` 이 생기면 **시험을 실패시킨다.**

## 남아 있는 파일

| 파일 | 상태 |
|---|---|
| `20260619_ai_chatbot_byok_memory.sql.NOT_DEPLOYED` | 🔴 **고객에게 간 적 없음** |

6/19 작성분(AI 장기기억 `ai_work_memory`)도 같은 사고였다.
**진짜 폴더에도·clean DDL 에도·실제 DB 에도 그 테이블이 없다** — 두 달간 아무도 몰랐다.
코드 참조도 0건이라 기능이 동작한 적이 없다.

지우지 않고 남긴다(헌법 #1·#41 — 문서·자산은 상태만 바꾼다).
**살리려면** 이번 오더 범위 밖이므로 **별도 결재** 후
`DB-NN_` 규칙으로 위 진짜 폴더에 옮기고 clean DDL 시드에 등록해야 한다.

## 새 마이그레이션을 만들 때

1. `src/HitPan.API/Migrations/SQL/DB-{다음번호}_{이름}.sql` 로 만든다
2. `ADD COLUMN IF NOT EXISTS` 등 **멱등**으로 쓴다 (업데이트 재시도로 두 번 돌 수 있다)
3. `installer/hitpan_db_clean.sql` 에 **컬럼 정의 + `schema_migrations` 시드**(`('DB-NN','clean-ddl',1)`) 둘 다 넣는다
4. `bash scripts/ddl-smoke-test.sh` 로 **"시드 == 파일"** 이 맞는지 확인한다
5. 게시할 때 **`requiresMigration=true`**
