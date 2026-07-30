# 작업지시서 — 고리4 P1: MigrationRunner DI 배선 (수동 호출만, 자동실행 0건)

> 문서번호: 20260630작4 / 작성: PM 브라운킴 / 2026-06-30
> SOP [3]작업지시서 → [4]CTO결재 → [5]사장님 승인 **전까지 코드 0건**
> 기반: `20260630_작2_고리4구현_단계분할_작업지시서_CTO결재용.md` §5 P1 결재완료 + 오늘 배선지점 실측
> 정합 헌법: #1(추가만)·#13·#33(의중 재확인)·#36·#39(운영 보호)

---

## 1. 사장님 의중 (오늘 재확인 완료 — 박제)

> **"자동마이그 시점 = 수동 호출만(자동실행 0건)"** — 사장님 결재 2026-06-30.
> 근거: 이 PC = demo 운영(hitpan_erp 129만행). Production 자동마이그 켜면 검증 안 된 ALTER가 운영DB에 즉시 날아감(헌법 #39 위반). → **앱시작 자동호출 0건, DI 등록만.**

이 결정은 `IMigrationRunner.cs:20-21` 원설계 의도("앱 시작 시 자동 실행 배선 0건 … ①단계는 '클래스 존재 + 호출하면 동작'까지만")와 **정확히 일치**한다.

---

## 2. 현재 위치 (오늘 코드 실측)

| 재료 | 상태 | 근거 |
|---|---|---|
| `IMigrationRunner` 인터페이스 | ✅ 완성 | `IMigrationRunner.cs` |
| `MigrationRunner` 구현 | ✅ 완성(멱등·순차·#15·#16·#26) | `MigrationRunner.cs` |
| `IMigrationDbConnectionFactory` | ✅ **이미 DI 등록** | `InfrastructureExtensions.cs:46` (Singleton) |
| **`IMigrationRunner` DI 등록** | 🔴 **0건** | `Program.cs` grep 무매칭 |

→ **진짜 남은 것 = `IMigrationRunner` → `MigrationRunner` DI 등록 한 줄.** 의존(connFactory·logger)은 이미 컨테이너에 있음.

---

## 3. 봉합안 (구현은 결재 후)

### 변경 ① — Program.cs DI 등록 한 줄 추가

**위치**: `Program.cs:71` `AddInfrastructure()` **직후**(connFactory가 거기서 등록되므로 그 뒤).
**추가**(헌법 #1 — 기존 줄 무수정, 신규 한 줄만):
```csharp
// 고리4 P1 (사장님 결재 2026-06-30, 작4): DB 스키마 마이그(DB-*.sql) 적용 주체 등록.
//   ★ 수동 호출만 — 앱시작 자동 실행 배선 0건(IMigrationRunner.cs:20 ①범위, 헌법 #39 운영 보호).
//   IMigrationDbConnectionFactory(InfrastructureExtensions.cs:46)·ILogger 는 이미 등록됨.
builder.Services.AddScoped<IMigrationRunner, MigrationRunner>();
```
- `using` 필요: `HitPan.Application.Interfaces`(IMigrationRunner)·`HitPan.Application.Services`(MigrationRunner) — Program.cs 상단에 이미 다른 Application using 있으면 FQN 또는 기존 using 활용. **구현 시 실측해 맞춤**(헌법 #12 — 소비처/네임스페이스 확인).

### 변경 안 함 (명시적 — 범위 밖, 절대 안 건드림)
- ❌ **app.Build() 이후 앱시작 자동호출 블록 추가 안 함.** (사장님 §1 결재 — 자동실행 0건)
- ❌ Development 분기 자동마이그 안 함.
- ❌ 환경변수 스위치 안 만듦.
- ❌ MigrationRunner.cs·IMigrationRunner.cs·MigrationDbConnectionFactory.cs **무수정**(이미 완성).

→ P1 = **순수 DI 등록 한 줄.** 이것만으로 "수동 호출 시 동작" 준비 완료. 운영DB 영향 0.

---

## 4. 헌법 #12 — 인터페이스/소비처 확인

- `IMigrationRunner` 구현체 = `MigrationRunner` **단 하나**(grep으로 구현 1건 확인 완료). 다중 구현 없음 → 등록 1줄로 충분.
- DI 등록 후 **소비처(호출자)는 아직 0건이 정상** — ①범위는 "호출하면 동작"까지. 실제 호출(소비)은 고리4②③(Velopack 적용 시점)에서 배선. 이건 덩어리2식 "자리 선점"이 아니라, **수동 운영 도구로 즉시 사용 가능**(아래 §5).

---

## 5. 효과 — DI 등록만으로도 헌법 #39 사고 근원 대체 가능

DI 등록되면, 향후 **검증된 테스트 환경(B갈래)에서** 다음처럼 수동 호출로 ALTER를 자동·멱등 적용할 수 있다(사람 손 ALTER = 헌법 #39 사고 근원을 대체):
- 일회성 호출 엔드포인트 or 관리 콘솔 명령(②③에서 배선) → `ApplyPendingAsync()` → schema_migrations 기준 미적용분만 순차 적용.
- 단 **본 작4 범위는 DI 등록까지.** 호출 배선·검증은 B갈래(M4) 선행 후 ②③.

---

## 6. ⚠️ 결재벽 + 검증 순서 (헌법 #29·#39)

- **코드(Program.cs 한 줄 추가)는 결재 후 PM 가능.** 운영DB 안 건드림(자동실행 0건이므로).
- **빌드 0/0 검증 = 이 PC에서 PM 가능**(DI 등록은 컴파일·DI 컨테이너 구성 검증만, DB 접속·마이그 실행 0). API를 **띄우지 않고** `dotnet build`만 — 운영 무손상.
- **실DB 마이그 검증(ApplyPendingAsync 실제 실행) = B갈래(M4 슬롯) 선행 필수.** 운영(demo) 절대 금지(헌법 #39). → M4 후로 보류.

→ 즉 **작4 = "DI 등록 + 빌드0/0"까지 오늘 PM 완결 가능. 실DB 멱등검증은 M4 후.** ("코드 미리, 실검증 나중" — 사장님 결재 흐름)

---

## 7. 검증팀(데이비드 박) 동시 검증 [7-V]

- SoD: 구현자(PM) ≠ 검증자(데이비드 박)
- 3관문(이번 작4 해당분):
  1. **빌드 0/0** — 솔루션 전체 errors 0 + warnings 0(헌법 #19).
  2. **DI 정합** — IMigrationRunner 해석 가능(GetRequiredService 모의 or 컨테이너 빌드 검증), 순환참조 0.
  3. **독립반증** — "app.Build 이후 자동호출 블록이 정말 0건인지"(사장님 §1 결재 준수) + "MigrationRunner.cs/IMigrationRunner.cs 무수정인지" 데이비드 박이 git diff로 반증.
- ddl-smoke(빈DB→clean DDL) = 본 작4는 DDL 변경 0이라 무관(스키마 무변경). M4때 ApplyPendingAsync 멱등검증에서 수행.

---

## 8. 정직 고지 (헌법 #32)

- 본 작4는 **위험도 낮음**(DI 한 줄, 자동실행 0, 운영DB 무접촉). 작2가 P1을 "가장 안전, 의존 0"으로 분류한 것과 일치.
- 단 **"고리4 P1 완료"가 "고리4 완료"는 아니다.** ②(Velopack)·③(롤백상태머신)은 여전히 미구현, M4 격리환경 후. 부풀리지 않는다.
- DI 등록만으로 **마이그가 자동으로 도는 게 아니다** — 호출 배선(②③)이 있어야 실제 동작. 작4는 "그릇 등록"까지.

---

## 9. 사장님 결재 요청

- [ ] **작4 범위** = Program.cs DI 등록 한 줄 + 빌드0/0 검증까지(자동호출 0건) — 동의?
- [ ] **자동실행 0건**(앱시작 자동마이그 안 함, 수동 호출만) — 오늘 §1 재확인대로, 동의?
- [ ] **실DB 멱등검증은 M4 후**(B갈래), 오늘은 빌드0/0까지 — 순서 동의?

---
**상태: [3] 작업지시서 완료. [4] CTO 결재 → [5] 사장님 승인 대기.**
