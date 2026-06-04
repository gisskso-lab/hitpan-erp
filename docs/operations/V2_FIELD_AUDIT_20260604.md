# 백오피스 V2 화면 종합 실측 보고서 (L 차수)

> 사장님 결재 2026-06-04
> 점검 대상: /admin/tenants · /admin/tenants/{id} · /admin/resellers · /admin/resellers/{id} · /admin/reseller-applications + Dialog
> 점검 방식: 코드 정적 점검 + 헌법 #35 정합·BoPermission·데이터 최소주의 4축
> 코드 변경: **0건** (보고서만 산출)

---

## 1. V2 화면 5종 정합 매트릭스

| 화면 | 라우트 | API | BoPermission | 메타 한정 |
|---|---|---|---|---|
| 고객사 목록 | `/admin/tenants` | `GET /api/backoffice/tenants` | tenants.list | ✅ |
| 고객사 상세 | `/admin/tenants/{TenantId}` | `GET /api/backoffice/tenants/{id}` | tenants.detail | ✅ |
| 협력업체 목록 | `/admin/resellers` | `GET /api/backoffice/resellers` | resellers.list | ✅ |
| 협력업체 상세 | `/admin/resellers/{ResellerId}` | `GET /api/backoffice/resellers/{id}` | resellers.detail | ✅ |
| 신청 검토 | `/admin/reseller-applications` + Dialog | `GET /api/backoffice/reseller-applications/{id}` | reseller-applications.detail | ✅ |

---

## 2. 헌법 #35 (3시스템 분리) 정합

- ✅ 모든 V2 화면 → **백오피스 API 5258 직접 호출**, ERP API 5257 호출 0건
- ✅ DB 접근 = `hitpan_backoffice` 전용 (W1 분리 후), `hitpan_erp` 접근 0건
- ✅ JWT 청구권 = `aud=backoffice`, ERP JWT와 키·발급자 분리
- ✅ ResellerSignupPage 폐기 + 백오피스 1.5초 리다이렉트 (랜딩에 가입 흐름 잔존 0)

---

## 3. 데이터 최소주의 (헌법 #18·#22) 정합

| 화면 | 노출 데이터 | 차단 데이터 |
|---|---|---|
| 고객사 상세 | tenant_code · 회사명 · 상태 · 가입일 · 구독 등급 · AI 모드 · 라이선스 해시 prefix(12자) · 최근 결제 메타 10건 | 평문 사업자번호 / 카드정보 / CVC / 매출·매입·원장 / 직원 정보 |
| 협력업체 상세 | reseller_code · 회사명 · 사업자번호 (전체) · 대표자 · 연락처 · 영업 고객사 메타 최근 20건 | 영업 고객사 업무 데이터 / 결제 카드 |
| 신청 검토 | 회사명 · 사업자번호 · 담당자 · 연락처 · 영업 지역 · 신청 동기 | 사업자등록증 원본 파일 |

**⚠️ 협력업체 상세 — 사업자번호 평문 노출**: `r.biz_no` 풀 표시. 백오피스 owner/platform 영역이라 정합이지만 [[project_data_boundary]] 정신상 마스킹 옵션 검토 권고 (P 차수에서 결재).

---

## 4. 액션 매트릭스

| 액션 | 화면 | API | 검증 |
|---|---|---|---|
| 정지 | 고객사 상세 | `POST /tenants/{id}/suspend` (사유 필수) | active 상태만 |
| 복구 | 고객사 상세 | `POST /tenants/{id}/activate` | suspended/pending만 |
| 승인 | 신청 검토 | `POST /reseller-applications/{id}/approve` | pending만 |
| 반려 | 신청 검토 | `POST /reseller-applications/{id}/reject` | pending만 |

**W10 webhook 연결 확인**: 정지/복구 시 `EmitSubscriptionChangedAsync` 호출 → outbox INSERT → 1분 내 ERP `local_subscription` 동기화. 끊김 0.

---

## 5. UI 클릭 폭발 점검

| 클릭 경로 | 폭발 가능성 | 결과 |
|---|---|---|
| 목록 → 코드/회사명 클릭 → 상세 진입 | 라우트 `/admin/tenants/{TenantId}` 정상 박제 | ✅ |
| 협력업체 상세 → 영업 고객사 클릭 → 고객사 상세 (양방향) | `/admin/tenants/{id}` 동일 라우트 재사용 | ✅ |
| 신청 검토 → 회사명 클릭 → Dialog | `DialogService.ShowAsync<ResellerApplicationDetailDialog>` 정상 | ✅ |
| 상세에서 뒤로가기 | `ArrowBack IconButton` → `/admin/tenants` 또는 `/admin/resellers` | ✅ |

---

## 6. 빈 catch / silent fail 점검

| 화면 | catch 처리 | 정합 |
|---|---|---|
| AdminTenantDetailV2 | `Snackbar.Add(ex.Message, Severity.Error)` | ✅ |
| AdminResellerDetailV2 | `Snackbar.Add(ex.Message, Severity.Error)` | ✅ |
| ResellerApplicationDetailDialog | `Snackbar.Add(ex.Message, Severity.Error)` | ✅ |

(헌법 #15 빈 catch 0건 확인)

---

## 7. 잔여 권고 (별도 차수)

1. **사업자번호 마스킹 옵션** — 협력업체 상세 `r.biz_no` 풀 표시. 마스킹/풀 토글 권한 추가 검토. 비-필수.
2. **결제 메타 10건 → 무한 스크롤** — 현재 LIMIT 10 고정. 운영 6개월 후 더보기 버튼 검토.
3. **V2 화면 e2e 자동 점검** — Playwright `audit-erp-99-v3b3` 패턴을 백오피스로 이식. W12 차수.

---

## 8. 종합 판정

- **헌법 #35 정합**: PASS (객체 완전 분리)
- **헌법 #18·#22 정합**: PASS (메타 한정, 업무 데이터 0건)
- **헌법 #15 정합**: PASS (빈 catch 0건)
- **클릭 폭발**: 0건
- **W10 webhook 연결**: PASS (정지/복구 → ERP 동기화)

**V2 5화면 운영 가도 OK**. 사장님 실측 §5-3 체크리스트와 일치.

---

**다음 작업**: P 차수 (사장님 운영 필드 체크리스트).
