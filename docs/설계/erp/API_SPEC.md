# 히트판 백오피스 — API 명세서
> 버전: 1.0 | 기준일: 2026-05-05 | 작성자: 백엔드매니저 + 보안매니저

---

## 1. 공통 규칙

### 1-1. Base URL

| 환경 | URL |
|------|-----|
| 개발 | `http://localhost:5257` |
| 운영 | `https://api.hitpan.app` |

### 1-2. 인증 헤더

```
Authorization: Bearer {access_token}
```

### 1-3. 공통 응답 형식

**성공**
```json
{
  "success": true,
  "data": { ... },
  "message": null
}
```

**실패**
```json
{
  "success": false,
  "data": null,
  "message": "에러 메시지",
  "errorCode": "INVALID_INPUT"
}
```

### 1-4. 페이지네이션 요청/응답

**요청 파라미터**
```
?page=1&size=20
```

**응답 형식**
```json
{
  "success": true,
  "data": {
    "items": [...],
    "totalCount": 147,
    "page": 1,
    "size": 20,
    "totalPages": 8
  }
}
```

### 1-5. 보안 원칙

- `tenant_id`는 JWT 클레임에서만 — 쿼리 파라미터로 받는 코드 금지 (헌법 #2)
- `reseller_id`는 reseller_admin JWT에서만 — API 파라미터 수신 즉시 반려
- platform_admin API: `/api/admin/*` 전체 `[Authorize(Policy = "PlatformAdmin")]`
- reseller API: `/api/reseller/*` 전체 `[Authorize(Policy = "ResellerAdmin")]`

---

## 2. 인증 API

### POST /api/auth/admin/login — 본사 관리자 로그인

**Request**
```json
{
  "email": "admin@hitpan.kr",
  "password": "Admin1234!"
}
```

**Response 200**
```json
{
  "success": true,
  "data": {
    "accessToken": "eyJ...",
    "refreshToken": "...",
    "expiresIn": 3600,
    "role": "super_admin",
    "adminName": "홍길동"
  }
}
```

**Response 401**
```json
{ "success": false, "message": "이메일 또는 비밀번호가 올바르지 않습니다", "errorCode": "UNAUTHORIZED" }
```

**Response 403**
```json
{ "success": false, "message": "비활성화된 계정입니다", "errorCode": "FORBIDDEN" }
```

---

### POST /api/auth/reseller/login — 대리점 계정 로그인

**Request**
```json
{
  "email": "lee@abc-reseller.com",
  "password": "Partner1234!"
}
```

**Response 200**
```json
{
  "success": true,
  "data": {
    "accessToken": "eyJ...",
    "refreshToken": "...",
    "expiresIn": 3600,
    "role": "reseller_admin",
    "resellerId": "550e8400-e29b-41d4-a716-446655440000",
    "resellerName": "ABC대리점",
    "accountName": "이담당"
  }
}
```

---

### POST /api/auth/refresh — 토큰 갱신

**Request**
```json
{ "refreshToken": "..." }
```

**Response 200**
```json
{ "success": true, "data": { "accessToken": "eyJ...", "expiresIn": 3600 } }
```

---

### POST /api/auth/logout — 로그아웃

**Headers**: Authorization required  
**Response 200**: `{ "success": true }`

---

## 3. 본사 — 고객사 API (/api/admin/tenants)

### GET /api/admin/tenants — 목록 조회

**Policy**: PlatformAdmin  
**Query Params**: `status`, `resellerId`, `planType`, `search`, `page`, `size`

**Response 200**
```json
{
  "success": true,
  "data": {
    "items": [
      {
        "tenantId": "...",
        "tenantCode": "T-001",
        "companyName": "(주)ABC물산",
        "bizNo": "123-45-67890",
        "resellerName": "ABC대리점",
        "status": "active",
        "planType": "pro",
        "trialEndsAt": null,
        "userCount": 8,
        "nextBillingAt": "2026-06-01",
        "createdAt": "2026-01-01T00:00:00"
      }
    ],
    "totalCount": 147,
    "page": 1,
    "size": 20,
    "totalPages": 8
  }
}
```

---

### GET /api/admin/tenants/{tenantId} — 상세 조회

**Policy**: PlatformAdmin

**Response 200**
```json
{
  "success": true,
  "data": {
    "tenantId": "...",
    "tenantCode": "T-001",
    "companyName": "(주)ABC물산",
    "bizNo": "123-45-67890",
    "ceoName": "홍길동",
    "tel": "02-1234-5678",
    "address": "서울 강남구 ...",
    "resellerName": "ABC대리점",
    "resellerId": "...",
    "status": "active",
    "subscription": {
      "planType": "pro",
      "baseUsers": 5,
      "extraUsers": 3,
      "baseFee": 59000,
      "extraFeePerUser": 10000,
      "billingCycle": "monthly",
      "startedAt": "2026-01-01",
      "nextBillingAt": "2026-06-01",
      "status": "active"
    },
    "invoices": [
      {
        "invoiceId": "...",
        "billingMonth": "2026-05",
        "totalAmount": 89000,
        "status": "paid",
        "paidAt": "2026-05-01T09:00:00"
      }
    ]
  }
}
```

---

### POST /api/admin/tenants — 신규 등록

**Policy**: PlatformAdmin (super_admin only)

**Request**
```json
{
  "companyName": "신규상사",
  "bizNo": "123-45-67890",
  "ceoName": "김대표",
  "tel": "02-9876-5432",
  "address": "경기도 수원시 ...",
  "resellerId": "550e8400-...",
  "planType": "basic",
  "billingCycle": "monthly",
  "extraUsers": 0,
  "startType": "trial"
}
```

**Response 201**
```json
{
  "success": true,
  "data": {
    "tenantId": "...",
    "tenantCode": "T-148",
    "tempPassword": "Hitpan2026!"
  }
}
```

---

### PATCH /api/admin/tenants/{tenantId}/status — 상태 변경

**Policy**: PlatformAdmin (super_admin, billing_admin)

**Request**
```json
{ "newStatus": "suspended", "reason": "결제 미납 3개월" }
```

**Response 200**: `{ "success": true }`

---

## 4. 본사 — 청구서 API (/api/admin/billing)

### GET /api/admin/billing/invoices — 청구서 목록

**Policy**: PlatformAdmin (super_admin, billing_admin)  
**Query Params**: `billingMonth`, `status`, `tenantId`, `page`, `size`

**Response 200**
```json
{
  "success": true,
  "data": {
    "items": [
      {
        "invoiceId": "...",
        "companyName": "(주)ABC물산",
        "billingMonth": "2026-05",
        "totalAmount": 89000,
        "status": "paid",
        "paidAt": "2026-05-01T09:00:00"
      }
    ],
    "totalCount": 147
  }
}
```

---

### POST /api/admin/billing/invoices/{invoiceId}/retry — 결제 재시도

**Policy**: PlatformAdmin (billing_admin, super_admin)  
**Response 200**: `{ "success": true, "data": { "newStatus": "paid" } }`

---

### POST /api/admin/billing/invoices/{invoiceId}/cancel — 청구서 취소

**Policy**: PlatformAdmin (super_admin)  
**Response 200**: `{ "success": true }`

---

## 5. 본사 — 대리점 API (/api/admin/resellers)

### GET /api/admin/resellers — 대리점 목록

**Policy**: PlatformAdmin (super_admin, billing_admin)  
**Query Params**: `status`, `search`, `page`, `size`

**Response 200**
```json
{
  "success": true,
  "data": {
    "items": [
      {
        "resellerId": "...",
        "resellerCode": "RS-001",
        "resellerName": "ABC대리점",
        "status": "active",
        "customerCount": 23,
        "monthlyRevenue": 3200000,
        "monthlyCommission": 640000,
        "joinDate": "2025-01-01"
      }
    ],
    "totalCount": 8
  }
}
```

---

### GET /api/admin/resellers/{resellerId} — 대리점 상세

**Policy**: PlatformAdmin

**Response 200**
```json
{
  "success": true,
  "data": {
    "resellerId": "...",
    "resellerCode": "RS-001",
    "resellerName": "ABC대리점",
    "bizNo": "123-45-67890",
    "ceoName": "김대표",
    "tel": "02-1234-5678",
    "address": "서울 강남구 ...",
    "bankName": "신한은행",
    "bankAccountMasked": "****-****-1234",
    "accountHolder": "ABC대리점",
    "contactPerson": "이담당",
    "contactPhone": "010-1234-5678",
    "contactEmail": "lee@abc-reseller.com",
    "joinDate": "2025-01-01",
    "status": "active",
    "customerCount": 23,
    "commissionPolicies": [...],
    "settlements": [...]
  }
}
```

---

### GET /api/admin/resellers/{resellerId}/bank-account — 계좌 전체 조회

**Policy**: PlatformAdmin (super_admin only) — 암호화 복호화 후 반환

**Response 200**
```json
{ "success": true, "data": { "bankAccount": "110-123-456789" } }
```

---

### POST /api/admin/resellers — 대리점 등록

**Policy**: PlatformAdmin (super_admin)

**Request**
```json
{
  "resellerName": "신규대리점",
  "bizNo": "987-65-43210",
  "ceoName": "박대표",
  "tel": "031-1234-5678",
  "address": "경기도 성남시 ...",
  "bankName": "국민은행",
  "bankAccount": "123-456-789012",
  "accountHolder": "신규대리점",
  "contactPerson": "최담당",
  "contactPhone": "010-9876-5432",
  "contactEmail": "choi@new-reseller.com",
  "joinDate": "2026-05-05"
}
```

**Response 201**
```json
{ "success": true, "data": { "resellerId": "...", "resellerCode": "RS-009" } }
```

---

### PUT /api/admin/resellers/{resellerId} — 대리점 정보 수정

**Policy**: PlatformAdmin (super_admin)  
**Request**: 수정 필드만 포함 (부분 업데이트)  
**Response 200**: `{ "success": true }`

---

### PATCH /api/admin/resellers/{resellerId}/status — 상태 변경

**Policy**: PlatformAdmin (super_admin)

**Request**
```json
{ "newStatus": "suspended", "reason": "계약 위반" }
```

**Response 200**: `{ "success": true }`

---

## 6. 본사 — 수수료 정책 API (/api/admin/resellers/{resellerId}/commissions)

### GET /api/admin/resellers/{resellerId}/commissions — 정책 목록

**Policy**: PlatformAdmin (super_admin, billing_admin)

**Response 200**
```json
{
  "success": true,
  "data": [
    {
      "commissionId": "...",
      "planCode": "pro",
      "rate": 22.00,
      "effectiveFrom": "2026-05-01",
      "effectiveTo": null,
      "isActive": true
    },
    {
      "commissionId": "...",
      "planCode": "pro",
      "rate": 20.00,
      "effectiveFrom": "2026-01-01",
      "effectiveTo": "2026-04-30",
      "isActive": false
    }
  ]
}
```

---

### POST /api/admin/resellers/{resellerId}/commissions — 정책 등록

**Policy**: PlatformAdmin (super_admin)

**Request**
```json
{
  "planCode": "pro",
  "rate": 25.00,
  "effectiveFrom": "2026-06-01"
}
```

**Response 201**
```json
{ "success": true, "data": { "commissionId": "..." } }
```

**Response 400** (rate 범위 오류)
```json
{ "success": false, "message": "수수료율은 0~100 사이여야 합니다", "errorCode": "INVALID_INPUT" }
```

**Response 409** (중복)
```json
{ "success": false, "message": "해당 기간·플랜의 정책이 이미 존재합니다", "errorCode": "CONFLICT" }
```

---

## 7. 본사 — 정산 API (/api/admin/settlements)

### GET /api/admin/settlements — 정산 목록

**Policy**: PlatformAdmin (super_admin, billing_admin)  
**Query Params**: `settlementMonth`, `status`, `resellerId`, `page`, `size`

**Response 200**
```json
{
  "success": true,
  "data": {
    "items": [
      {
        "settlementId": "...",
        "resellerId": "...",
        "resellerName": "ABC대리점",
        "settlementMonth": "2026-04",
        "activeCustomerCount": 23,
        "totalRevenue": 3200000.00,
        "totalCommission": 640000.00,
        "deductionAmount": 0.00,
        "paymentAmount": 640000.00,
        "status": "paid",
        "paymentDate": "2026-04-30"
      }
    ],
    "totalCount": 32,
    "monthlyTotal": {
      "totalRevenue": 15600000.00,
      "totalCommission": 2340000.00
    }
  }
}
```

---

### GET /api/admin/settlements/{settlementId} — 정산 상세

**Policy**: PlatformAdmin (super_admin, billing_admin)

**Response 200**
```json
{
  "success": true,
  "data": {
    "settlementId": "...",
    "resellerName": "ABC대리점",
    "settlementMonth": "2026-04",
    "activeCustomerCount": 23,
    "totalRevenue": 3200000.00,
    "totalCommission": 640000.00,
    "deductionAmount": 0.00,
    "paymentAmount": 640000.00,
    "status": "draft",
    "approvalDate": null,
    "approvedBy": null,
    "paymentDate": null,
    "memo": null
  }
}
```

---

### POST /api/admin/settlements/generate — 정산 생성

**Policy**: PlatformAdmin (billing_admin, super_admin)

**Request**
```json
{
  "settlementMonth": "2026-05",
  "resellerId": null
}
```
> `resellerId: null` → 전체 대리점 일괄 생성

**Response 201**
```json
{
  "success": true,
  "data": {
    "generatedCount": 8,
    "skippedCount": 0,
    "settlements": [...]
  }
}
```

**Response 409** (이미 존재)
```json
{ "success": false, "message": "2026-05 정산이 이미 존재합니다", "errorCode": "CONFLICT" }
```

---

### POST /api/admin/settlements/{settlementId}/approve — 정산 승인

**Policy**: PlatformAdmin (billing_admin, super_admin)

**Request**
```json
{ "memo": "검토 완료" }
```

**Response 200**: `{ "success": true }`

---

### POST /api/admin/settlements/{settlementId}/pay — 지급 처리

**Policy**: PlatformAdmin (billing_admin, super_admin)

**Request**
```json
{ "paymentDate": "2026-05-31", "memo": "계좌이체 완료" }
```

**Response 200**: `{ "success": true }`

---

### POST /api/admin/settlements/{settlementId}/cancel — 정산 취소

**Policy**: PlatformAdmin (super_admin only)

**Request**
```json
{ "reason": "오류 재생성 필요" }
```

**Response 200**: `{ "success": true }`

---

## 8. 본사 — 대리점 계정 API (/api/admin/resellers/{resellerId}/accounts)

### GET /api/admin/resellers/{resellerId}/accounts — 계정 목록

**Policy**: PlatformAdmin

**Response 200**
```json
{
  "success": true,
  "data": [
    {
      "accountId": "...",
      "accountName": "이담당",
      "email": "lee@abc-reseller.com",
      "role": "reseller_admin",
      "isActive": true,
      "lastLoginAt": "2026-05-05T09:12:00"
    }
  ]
}
```

---

### POST /api/admin/resellers/{resellerId}/accounts — 계정 생성

**Policy**: PlatformAdmin (super_admin)

**Request**
```json
{
  "email": "new@abc-reseller.com",
  "accountName": "신규담당",
  "role": "reseller_user",
  "phone": "010-0000-1111"
}
```

**Response 201**
```json
{ "success": true, "data": { "accountId": "...", "tempPassword": "Temp1234!" } }
```

---

### PATCH /api/admin/resellers/{resellerId}/accounts/{accountId}/toggle — 활성/비활성

**Policy**: PlatformAdmin (super_admin)  
**Response 200**: `{ "success": true, "data": { "isActive": false } }`

---

## 9. 대리점 파트너 API (/api/reseller/*)

> **보안**: 모든 엔드포인트에서 JWT의 `reseller_id` 클레임으로 자동 필터링
> reseller_admin이 타 대리점 데이터 요청 시 403 즉시 반환

### GET /api/reseller/dashboard — 대시보드 KPI

**Policy**: ResellerAdmin

**Response 200**
```json
{
  "success": true,
  "data": {
    "customerCount": 23,
    "newCustomersThisMonth": 2,
    "estimatedCommission": 640000.00,
    "lastMonthCommission": 580000.00,
    "lastMonthSettlementStatus": "paid",
    "customerStatusSummary": {
      "active": 20,
      "trial": 3,
      "suspended": 0
    },
    "monthlyCommissionTrend": [
      { "month": "2025-12", "commission": 460000.00 },
      { "month": "2026-01", "commission": 500000.00 },
      { "month": "2026-02", "commission": 540000.00 },
      { "month": "2026-03", "commission": 560000.00 },
      { "month": "2026-04", "commission": 580000.00 },
      { "month": "2026-05", "commission": 640000.00 }
    ]
  }
}
```

---

### GET /api/reseller/tenants — 내 담당 고객사 목록

**Policy**: ResellerAdmin  
**보안**: WHERE tenants.reseller_id = {JWT.reseller_id} 자동 적용  
**Query Params**: `status`, `search`, `page`, `size`

**Response 200** (tenants 항목: tenantId, companyName, status, planType, userCount, nextBillingAt)

---

### GET /api/reseller/tenants/{tenantId} — 내 담당 고객사 상세

**Policy**: ResellerAdmin  
**보안**: tenant.reseller_id ≠ JWT.reseller_id → 403

**Response 200** (기본정보 + 구독정보 + 청구이력, 결제수단 상세 제외)

---

### GET /api/reseller/commissions/policies — 내 수수료 정책

**Policy**: ResellerAdmin

**Response 200**
```json
{
  "success": true,
  "data": [
    { "planCode": "basic", "rate": 15.00, "effectiveFrom": "2026-01-01" },
    { "planCode": "pro", "rate": 22.00, "effectiveFrom": "2026-05-01" },
    { "planCode": "enterprise", "rate": 25.00, "effectiveFrom": "2026-01-01" }
  ]
}
```

---

### GET /api/reseller/commissions/settlements — 내 정산 이력

**Policy**: ResellerAdmin  
**Query Params**: `page`, `size`

**Response 200**
```json
{
  "success": true,
  "data": {
    "items": [
      {
        "settlementId": "...",
        "settlementMonth": "2026-04",
        "activeCustomerCount": 23,
        "totalRevenue": 3200000.00,
        "totalCommission": 640000.00,
        "paymentAmount": 640000.00,
        "status": "paid",
        "paymentDate": "2026-04-30"
      }
    ],
    "totalCount": 5
  }
}
```

---

### GET /api/reseller/profile — 내 프로필

**Policy**: ResellerAdmin (본인)

**Response 200**
```json
{
  "success": true,
  "data": {
    "accountId": "...",
    "accountName": "이담당",
    "email": "lee@abc-reseller.com",
    "phone": "010-1234-5678",
    "role": "reseller_admin",
    "resellerName": "ABC대리점",
    "lastLoginAt": "2026-05-05T09:12:00"
  }
}
```

---

### PUT /api/reseller/profile — 내 프로필 수정

**Policy**: ResellerAdmin (본인)

**Request**
```json
{ "accountName": "이담당2", "phone": "010-9999-8888" }
```

**Response 200**: `{ "success": true }`

---

### POST /api/reseller/profile/change-password — 비밀번호 변경

**Policy**: ResellerAdmin (본인)

**Request**
```json
{
  "currentPassword": "OldPass1234!",
  "newPassword": "NewPass5678!"
}
```

**Response 200**: `{ "success": true }`  
**Response 400**: `{ "success": false, "message": "현재 비밀번호가 올바르지 않습니다" }`

---

## 부록 A. Authorization Policy 정의

```csharp
// Program.cs 등록 필요

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PlatformAdmin", policy =>
        policy.RequireClaim("account_type", "platform_admin"));

    options.AddPolicy("PlatformAdminSuperOnly", policy =>
        policy.RequireClaim("account_type", "platform_admin")
              .RequireClaim("role", "super_admin"));

    options.AddPolicy("PlatformAdminBilling", policy =>
        policy.RequireClaim("account_type", "platform_admin")
              .RequireAssertion(ctx =>
                  ctx.User.HasClaim("role", "super_admin") ||
                  ctx.User.HasClaim("role", "billing_admin")));

    options.AddPolicy("ResellerAdmin", policy =>
        policy.RequireClaim("account_type", "reseller_admin"));
});
```

---

## 부록 B. JWT 클레임 구조

**platform_admin**
```json
{
  "sub": "{admin_id}",
  "account_type": "platform_admin",
  "role": "super_admin",
  "email": "admin@hitpan.kr",
  "exp": 1234567890
}
```

**reseller_admin**
```json
{
  "sub": "{account_id}",
  "account_type": "reseller_admin",
  "role": "reseller_admin",
  "reseller_id": "550e8400-...",
  "email": "lee@abc-reseller.com",
  "exp": 1234567890
}
```

---

## 부록 C. 컨트롤러 파일 목록

| 파일 | 경로 |
|------|------|
| AdminAuthController | `/Controllers/Admin/AdminAuthController.cs` |
| AdminTenantController | `/Controllers/Admin/AdminTenantController.cs` |
| AdminBillingController | `/Controllers/Admin/AdminBillingController.cs` |
| AdminResellerController | `/Controllers/Admin/AdminResellerController.cs` |
| AdminSettlementController | `/Controllers/Admin/AdminSettlementController.cs` |
| ResellerAuthController | `/Controllers/Reseller/ResellerAuthController.cs` |
| ResellerTenantController | `/Controllers/Reseller/ResellerTenantController.cs` |
| ResellerCommissionController | `/Controllers/Reseller/ResellerCommissionController.cs` |
| ResellerProfileController | `/Controllers/Reseller/ResellerProfileController.cs` |

---

*이 문서 기준으로 DB-39 DDL → 백엔드 구현 → 프론트 구현 순서로 진행*
