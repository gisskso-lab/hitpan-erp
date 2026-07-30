# 작업지시서 20260425작8 — JWT/Refresh 토큰 HttpOnly 쿠키 전환

## 0. 메타

| 항목 | 값 |
|---|---|
| **문서번호** | 20260425작8 |
| **발행일** | 2026-04-25 |
| **발행자** | PM 닥터스트레인지 |
| **A 책임자** | 보안 매니저 |
| **결재 트랙** | **풀** |
| **민감 영역** | 인증 / API 시그니처 |
| **Contract-First 대상** | ✅ (로그인·리프레시 API 응답 포맷 변경) |
| **EVF 영향 영역** | ③ 악의 / ⑤ 무지 |
| **예상 소요** | 보안 개발자 1명 + 프론트 개발자 1명 × 2일 |
| **Sprint** | Sprint 2 (5/3~5/9) |

## 1. 배경 (Why)

4/25 프론트 크로스체크 #4에서 보안 P0-B-N2를 진단하며, 현재 JWT access·refresh 토큰이 **`HitPanProtectedLocalStorage`(이름만 Protected, 실제는 Base64 + localStorage)** 에 저장되고 `window.hitpanStorage.get('access_token')`으로 **브라우저 콘솔에서 누구나 탈취 가능**함을 확인.

금일(4/25) P0-B 핫픽스로 sessionStorage 전환 + 전역 헬퍼 은닉 일부 적용해 **XSS 노출 창을 축소**했으나, 근본 해결(HttpOnly 쿠키)은 미완. JavaScript에서 읽을 수 없는 HttpOnly + SameSite=Strict 쿠키로 전환해야 XSS 1회에 전 테넌트 계정 탈취되는 위협 모델 자체가 해소된다.

금일 베타 직전 치명 리스크로 분류되어 사장님 지시로 P1 작지서 분리. 토큰 저장 방식 변경은 Blazor WASM ↔ API 양쪽 Contract 변경이라 Sprint 2에서 2일 소요 예상.

## 2. 목표 산출물 (What)

1. **API 측 쿠키 설정**:
   - `/api/auth/login` 응답: Body에 access·refresh 노출하지 않음 → `Set-Cookie: hitpan_access=...; HttpOnly; Secure; SameSite=Strict; Path=/api; Max-Age=...` + `Set-Cookie: hitpan_refresh=...` 동일 플래그
   - `/api/auth/refresh` 쿠키 갱신 방식
   - `/api/auth/logout` 쿠키 만료 처리
   - CORS 설정: `credentials: include` 허용 + Origin whitelist
2. **프론트 측 전환**:
   - `HitPanApiAuthHandler.SendAsync`에서 `Authorization: Bearer ...` 제거 (쿠키 자동 전송으로 대체)
   - `HitPanProtectedLocalStorage` 용도를 비민감 설정(테마/필터)만으로 축소
   - `HitPanAuthStateProvider`: 로그인 여부 판단 로직을 API 핑 엔드포인트(`/api/auth/me`)로 전환
3. **storage.js 정리**:
   - sessionStorage 라우팅 로직 제거 (쿠키가 처리)
   - 민감 키 목록(`SESSION_KEYS`) 비움
4. **테스트**:
   - CSRF 방어(SameSite=Strict + Origin 체크) 검증
   - 로그아웃 시 쿠키 소거 검증
   - Refresh 자동 갱신 시나리오

## 3. 비범위 (What Not)

- Refresh Token Rotation(회전) 구현은 별도 작지서 (현재 구조 그대로 유지).
- OAuth2/OpenID Connect 도입은 범위 외.
- 다중 세션 동시 관리(기기별 로그아웃)는 SessionLimitMiddleware 개편과 함께 별도.

## 4. RACI

| 역할 | 담당자 |
|---|---|
| **R** | 보안 개발자 1명 + 프론트 개발자 1명 |
| **A** | 보안 매니저 |
| **C** | 백엔드 매니저 (CORS·쿠키 도메인 협의) / 마커스 리 (Cloudflare/배포 협의) |
| **V** | 올리버 임 (ISO 컴플라이언스 관점 검토) / 데이비드 박(DV-S) |
| **F** | CTO 래리 앨리슨 → 사장님 |

## 5. 수용 기준 (Done Criteria)

- [ ] 브라우저 DevTools에서 `document.cookie` 조회 시 `hitpan_access`·`hitpan_refresh` **미노출** (HttpOnly 플래그 검증)
- [ ] 브라우저 콘솔에서 `sessionStorage`·`localStorage` 어디에도 JWT 없음 (grep 무결성)
- [ ] OWASP ZAP 자동 스캔으로 XSS → 토큰 탈취 시나리오 차단 확인
- [ ] 로그인·로그아웃·리프레시·403 핸들러(작6/금일 적용분)와의 회귀 없음
- [ ] 빌드 errors 0 + warnings 0 (헌법 #19)

## 6. 의존성

- 작7(DB 드리프트) 완료 후 시작 권장 (인증 스키마도 감사 대상)
- 로컬 터널링 트랙·클라우드 트랙 모두 쿠키 도메인 설정 검증 필요(§14 투트랙)
