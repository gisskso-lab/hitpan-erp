# 인수인계서 — 2026-05-04 (워크플로우 정합성 + ERP 로그 기록)

## 🎯 한 줄 결론

**다이렉트 판매 정합성 확보 + ERP 로그 기록 메뉴 완성.** `demo.hitpan.kr/data/logs` 정상 동작 확인.

---

## ✅ 오늘 완료된 것

### 1. 다이렉트 판매 정합성 (DB-34)

| 항목 | 내용 |
|---|---|
| 문제 | 수주 없이 거래명세서 직접 생성 시 수주↔거래명세서 연결 없음 → 데이터 양 많을 때 정합성 깨짐 |
| 해결 | 거래명세서 생성 시 `OrderId` 없으면 백엔드에서 `is_auto=true`, `status=Closed` 수주 자동생성 |
| 추가 문제 | 자동생성된 Closed 수주가 수주서 목록에 노출됨 (워크플로우 흐름 오염) |
| 해결 | `GetOrdersAsync` SQL에 `AND o.is_auto = 0` 필터 추가 |
| DB 변경 | `sales_orders` 테이블에 `is_auto TINYINT(1) NOT NULL DEFAULT 0` 컬럼 추가 |
| 마이그레이션 | `src/HitPan.API/Migrations/SQL/DB-34_sales_orders_is_auto.sql` |

**커밋:**
- `ffd9014` feat(sales): 다이렉트 판매 시 수주 자동생성으로 정합성 확보
- `8b35507` fix(sales): 다이렉트 판매 자동생성 수주 목록 노출 차단 (DB-34)

**수정 파일:**
- `src/HitPan.Application/Services/SalesService.cs` — `CreateDeliveryAsync` 자동수주 블록 + `GetOrdersAsync` is_auto 필터
- `src/HitPan.Domain/Entities/SalesOrder.cs` — `IsAuto` 프로퍼티 추가
- `src/HitPan.Infrastructure/Persistence/Configurations/SalesOrderConfiguration.cs` — `is_auto` 컬럼 매핑

---

### 2. ERP 로그 기록 메뉴 (신규)

| 항목 | 내용 |
|---|---|
| 위치 | 사이드바 > 자료관리 > ERP 로그 기록 |
| URL | `/data/logs` |
| 권한 | `TenantAdminOnly` (관리자만 접근) |
| 탭1 | 업무 동작 로그 — `audit_trail` 테이블 (create/confirm/cancel 등 도메인 이벤트) |
| 탭2 | API 요청 로그 — `audit_logs` 테이블 (method/endpoint/status_code, 4xx/5xx 빨간 강조) |
| 필터 | 날짜 범위(기본 어제~오늘) + 조회 버튼 + 오류만 보기 버튼 |
| 용도 | 고객지원 시 오류·데이터 누락 원인 추적 |

**커밋:**
- `2b646f8` feat(logs): ERP 로그 기록 메뉴 추가 — 자료관리 > ERP 로그 기록

**신규 파일:**
- `src/HitPan.API/Controllers/LogController.cs` — `GET /api/logs/audit-trail`, `GET /api/logs/api-requests`
- `src/HitPan.Web/Services/LogService.cs` — 클라이언트 서비스 + DTO
- `src/HitPan.Web/Pages/Settings/LogPage.razor` — UI 페이지

**수정 파일:**
- `src/HitPan.Web/Layout/Sidebar.razor` — 자료관리 그룹에 메뉴 추가
- `src/HitPan.Web/Program.cs` — `LogService` DI 등록

---

### 3. 500 버그 수정 — MariaDB 파라미터 충돌

| 항목 | 내용 |
|---|---|
| 증상 | `/api/logs/audit-trail` 및 `/api/logs/api-requests` 호출 시 500 오류 |
| 원인 | SQL 파라미터 `@ToNext` 안에 `@To`가 포함되어 MySqlConnector가 `@To`를 별도 파라미터로 파싱 → `Parameter '@To' must be defined` |
| 해결 | 파라미터명 `@From`/`@To`/`@ToNext` → `@DateFrom`/`@DateTo` 로 변경 |
| 부수 수정 | C# raw string literal(`"""..."""`) → 일반 문자열 연결로 교체 (CLAUDE.md 원칙 #14: Razor 파일 raw string 금지 준용) |

**커밋:**
- `2e0e0de` fix(log): MariaDB 파라미터 충돌 수정 - @To 를 @DateFrom/@DateTo 로 변경

---

## ⚠️ DB 마이그레이션 필수 확인

```sql
-- DB-34: sales_orders is_auto 컬럼 (아직 로컬에만 적용됨)
ALTER TABLE sales_orders
  ADD COLUMN is_auto TINYINT(1) NOT NULL DEFAULT 0
    COMMENT '다이렉트 판매 시 자동생성 수주 (목록 표시 제외)';
CREATE INDEX idx_sales_orders_is_auto ON sales_orders(tenant_id, is_auto);
```

파일: `src/HitPan.API/Migrations/SQL/DB-34_sales_orders_is_auto.sql`

**신규 고객사 배포 시 이 SQL 반드시 실행 필요.**

---

## 📋 다음 세션 최우선 작업

### P0 — 백오피스 워크플로우 설계 (사장님 지시 미착수)

오늘 CTO 브리핑에서 언급됐지만 로그 버그 수정으로 인해 착수 못 함.

> "백오피스로 계정관리, 대리점관리 하려면 계정관리, 그룹웨어 워크플로우 흐름을 설계하고 프로그램에 정립이 되야 프로그램과 연결되는 백오피스 흐름이 자연스러울거야."

현재 상태:
- `/admin/*` 페이지들 — placeholder "준비 중" 상태
- `/reseller/*` 페이지들 — placeholder "준비 중" 상태
- 사이드바에서는 제거됐으나 코드 보존 중 (백오피스 분리 예정)

설계가 필요한 흐름:
1. **본사 백오피스** → 테넌트 계정 생성/관리/라이선스 발급
2. **대리점 포털** → 담당 고객사 현황/수수료/KPI 조회
3. ERP ↔ 백오피스 데이터 경계 (헌법 #18: 업무 데이터 절대 전송 금지)

### P1 — 베타 테스트 준비

- 베타 체험단 20곳 온보딩 시나리오 최종 점검
- `demo.hitpan.kr` 실 사용 시나리오 워크스루

---

## 🔧 현재 실행 환경

| 구분 | 상태 |
|---|---|
| API 서버 | `localhost:5257` (dotnet run, 수동 시작) |
| Web 서버 | `localhost:5234` (PID 19116, 자동 유지) |
| Cloudflare 터널 | `demo.hitpan.kr` / `api-demo.hitpan.kr` → 로컬 터널 |
| DB | MariaDB 11.4, `hitpan_erp`, `hitpan` / `Hitpan2025!` |
| 테스트 계정 | `admin@hitpan.kr` / `Admin1234!` (TenantAdmin) |

**주의:** API 서버는 재부팅 후 수동으로 `dotnet run --no-build` 실행 필요.
`src/HitPan.API` 디렉토리에서 실행할 것.

---

## 📌 알려진 이슈 / 기술 부채

| 번호 | 내용 | 우선순위 |
|---|---|---|
| 1 | API 서버 Windows 서비스 등록 안 됨 — 재부팅 후 수동 시작 필요 | P1 |
| 2 | `audit_trail` 테이블 기록 범위 제한적 — 일부 도메인 이벤트 미기록 | P1 |
| 3 | 로그 페이지 페이지네이션 없음 — limit 300/500 하드캡 | P2 |
| 4 | 백오피스 `/admin/*` `/reseller/*` 페이지 전부 placeholder | P0 (다음 세션) |

---

## 🔑 오늘의 핵심 교훈

**MariaDB 파라미터 이름 규칙:** `@To`, `@From` 같은 짧은 파라미터명은 더 긴 파라미터명(`@ToNext`) 안에 포함될 경우 MySqlConnector가 충돌 파싱함. 날짜 파라미터는 항상 `@DateFrom` / `@DateTo` 처럼 구체적 이름 사용.

**구 바이너리 문제:** `dotnet run --no-build`로 시작된 프로세스는 코드 수정 후 반드시 종료 + 빌드 + 재시작 필요. 빌드만 하고 재시작 안 하면 구 DLL로 계속 서빙.
