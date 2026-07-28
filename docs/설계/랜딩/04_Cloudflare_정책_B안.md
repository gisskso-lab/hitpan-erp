# Cloudflare API 토큰 보관 정책 — B안 (사장님 결재 2026-06-01)

> **헌법**: #29 (인프라 사전 승인) 정합
> **결재**: 2026-06-01 사장님 "모두결재" → B안 (DPAPI 암호화) 박제 GO

---

## 1. 결재된 정책

### B안 (DPAPI + 본사 마스터 키)
- **저장 위치**: 본사 서버 환경변수에 **암호화된** Base64 문자열만
- **암호화 방식**:
  - Windows: DPAPI (LocalMachine scope, 본사 서버 한정)
  - 평문 토큰은 `dotnet user-secrets` 또는 본사 KMS 또는 환경변수
- **인스톨러 EXE에 절대 포함 금지** (헌법 #18 정합)

---

## 2. 절대 금지

| 금지 | 사유 |
|---|---|
| 평문 토큰을 git에 커밋 | 영구 노출 |
| 인스톨러 EXE에 포함 | 고객 PC 디컴파일로 노출 → 전체 인프라 장악 |
| 백오피스 응답에 노출 | reseller_admin이 가로챌 수 있음 |
| 클라이언트(Blazor WASM)에 전달 | 브라우저 메모리 탈취 |

---

## 3. 토큰 권한 범위 (Cloudflare 발급 시)

| 권한 | 필요? | 사유 |
|---|---|---|
| Zone:DNS:Edit | ✅ 필수 | `hitpan-{name}.kr` DNS 레코드 생성 |
| Zone:Zone:Read | ✅ 필수 | Zone ID 조회 |
| Account:Cloudflare Tunnel:Edit | ✅ 필수 | 터널 생성 + credentials 발급 |
| Account:Cloudflare Pages:Edit | ❌ 미사용 | |
| Account:Workers:Edit | ⚠ 선택 | 베타 단계 hitpan-prov.workers.dev 운영 |

**원칙**: 최소 권한. 사용 안 하는 권한은 발급 금지.

---

## 4. 토큰 회전 정책

- **수동 회전**: 분기 1회 (3개월)
- **사고 시 즉시 회전**: 누출 의심 시 30분 이내
- **회전 절차**:
  1. Cloudflare 대시보드에서 신규 토큰 발급
  2. 본사 서버 환경변수 갱신
  3. 서비스 재시작
  4. 이전 토큰 폐기

---

## 5. 본사 서버 환경변수 명세

```
CLOUDFLARE_API_TOKEN_ENC=<DPAPI 암호화된 Base64>
CLOUDFLARE_ACCOUNT_ID=<Account ID, 평문 OK>
CLOUDFLARE_ZONE_ID=<Zone ID for hitpan.kr, 평문 OK>
CLOUDFLARE_TUNNEL_DOMAIN_BASE=hitpan.kr   # 정식
# 베타: hitpan-prov.workers.dev
```

복호화는 본사 서버 부팅 시 1회. 메모리에만 보관.

---

## 6. 스켈레톤 구현 (작지#4 다음 박제)

- `ICloudflareProvisioningService` (인터페이스)
- `CloudflareProvisioningService` (DPAPI 복호 + Cloudflare API 호출)
- `ProvisioningController` (백오피스 endpoint)

→ 다음 작업지시서로 구현 단계 박제

---

## 7. 헌법 정합

- **헌법 #29**: 본사 서버 환경변수 변경 = 사장님 사전 결재 (변경 시마다)
- **헌법 #18**: 토큰은 본사에만, ERP 고객 PC에 절대 불가
- **헌법 #5**: 평문 저장 금지, DPAPI 암호화
- **헌법 #23**: 신규 코드 5중 검증 통과 후 머지

**박제 완료. 사장님 결재 B안 GO.**
