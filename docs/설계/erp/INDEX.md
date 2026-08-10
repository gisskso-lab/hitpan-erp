# INDEX — docs/설계/erp

> ERP 설계·PRD·ERD·아키텍처
> 문서 41개 | 자동생성 2026-07-28 | 헌법 #41
> **담당**: 설계팀장 존(=풀스택 매니저) · PM · CTO 래리  ([폴더담당 표](../../POLICY_폴더담당.md))

> 최근 12건만 표시. 전체 41건 = [INDEX_ALL.md](INDEX_ALL.md)

| 문서 | 제목 | 최종수정 |
|---|---|---|
| **[20260810_PRD_사업자정보_수집보관_체인.md](20260810_PRD_사업자정보_수집보관_체인.md)** 🆕🔴 | 🟢 **사업자정보 수집·보관·반영 PRD** — 등록증 1장으로 기본사업장정보가 **자동으로 써지게** 한다(사장님 의도). 무단사용 차단 **2중 관문**(①가입 국세청API ②설치후 OCR잠금). ⚠️**신규 설계 아님** — 랜딩 설계에 `is_match`·약관 4건이 이미 있는데 코드가 미이행. 🔴검증팀 반증 5건 반영(§6-5) | 2026-08-10 |
| **[20260810_디자인명세서_기본사업장정보_화면.md](20260810_디자인명세서_기본사업장정보_화면.md)** 🆕✅📐 | ✅ **안 A 확정본 — 구현자가 이것만 보고 만든다**. 색상은 기존 `hitpan.css:755` 토큰 재사용(신규 정의 금지) · 6행 유지 · 폭 합계 12 · **`ReadOnly` 확정**(`Disabled` 는 글자가 흐려지고 복사 불가) · 버튼/머리글/CSS 사양 · 🔴문구 정정 2건 · 완료판정 8항 | 2026-08-10 |
| **[20260810_디자인안_기본사업장정보_화면.md](20260810_디자인안_기본사업장정보_화면.md)** 🆕✅🎨 | ✅ **사장님 결재 = 안 A(구분선 분리형)**. 🔒자동(위)/⬜입력(아래) 분리·헬퍼텍스트 제거·`ReadOnly` 확정. 🔴문구 2건 정정(`:26` 3개 명시가 거짓 됨 · `"랜딩"`은 내부 이름) | 2026-08-10 |
| **[20260810_법무검토_사업자정보_현행법_경쟁사비교.md](20260810_법무검토_사업자정보_현행법_경쟁사비교.md)** 🆕⚖️ | **현행법 + 경쟁사 4곳 비교**. 🟢사장님 안이 업계보다 타이트(등록증 보관 명시 0건). 🔴**보유기간 5년 강제**(국세기본법 제85조의3·부가세법 제71조) — 현행 약관 "탈퇴 즉시 파기"와 충돌. ⭐#18·#22 법적 근거 확보. ⚠️위하고·SERP 미확인 | 2026-08-10 |
| [WATCHDOG_WS28_SKELETON.md](WATCHDOG_WS28_SKELETON.md) | 워치독 WS-28-A~I 9단계 — C# Windows Service 의사코드 골격 | 2026-07-28 |
| [VELOPACK_PHASE2_INTEGRATION_PLAN.md](VELOPACK_PHASE2_INTEGRATION_PLAN.md) | Velopack 자동 업데이트 Phase 2 통합 계획 | 2026-07-23 |
| [THREE_SYSTEM_ARCHITECTURE.md](THREE_SYSTEM_ARCHITECTURE.md) | 히트판 3개 시스템 아키텍처 설계 | 2026-07-23 |
| [TENANT_ID_NULL_POLICY.md](TENANT_ID_NULL_POLICY.md) | tenant_id NULL 허용 정책 | 2026-07-23 |
| [TABLE_SPEC.md](TABLE_SPEC.md) | 히트판 백오피스 — 테이블명세서 | 2026-07-23 |
| [STATE_MACHINE_SUBSCRIPTION.md](STATE_MACHINE_SUBSCRIPTION.md) | 상태 머신 — Tenant / Subscription / Payment | 2026-07-23 |
| [SEQUENCE_3SYSTEMS.md](SEQUENCE_3SYSTEMS.md) | 3시스템 시퀀스 다이어그램 (랜딩 · 백오피스 · ERP) | 2026-07-23 |
| [SCREEN_SPEC.md](SCREEN_SPEC.md) | 히트판 백오피스 — 화면정의서 | 2026-07-23 |
| [SCENARIO_20_VERIFY_SKELETON.md](SCENARIO_20_VERIFY_SKELETON.md) | 20개 강제 시나리오 검증 스크립트 골격 | 2026-07-23 |
| [PRD_THREE_SYSTEMS.md](PRD_THREE_SYSTEMS.md) | 히트판 PRD — 3개 시스템 제품 요구사항 정의서 | 2026-07-23 |
| [PRD_ERP.md](PRD_ERP.md) | 히트판 ERP 상세 PRD | 2026-07-23 |
| [PAYMENT_INTERFACE.md](PAYMENT_INTERFACE.md) | 결제 어댑터 인터페이스 명세 | 2026-07-23 |
