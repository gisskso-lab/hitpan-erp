using HitPan.Application.DTOs.Tenant;
using HitPan.Application.Interfaces;
using HitPan.Domain.Entities;
using HitPan.Domain.Enums;

namespace HitPan.Application.Services;

public class TenantService : ITenantService
{
    private readonly IUnitOfWork _unitOfWork;

    public TenantService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CreateTenantResponse> CreateAsync(CreateTenantRequest request, CancellationToken ct = default)
    {
        var tenants = _unitOfWork.Repository<Tenant>();
        var users = _unitOfWork.Repository<User>();
        var subscriptions = _unitOfWork.Repository<Subscription>();

        var existing = await tenants.GetAllAsync();
        if (existing.Count > 0)
        {
            throw new InvalidOperationException("이미 등록된 회사가 있습니다");
        }

        var now = DateTime.UtcNow;
        var tenantId = Guid.NewGuid().ToString();
        var tenantCode = $"HP-{1:000000}";

        var tenant = new Tenant
        {
            Id = tenantId,
            TenantId = tenantId,
            TenantCode = tenantCode,
            CompanyName = request.CompanyName,
            BizNo = request.BizNo,
            CeoName = request.CeoName,
            Tel = request.Tel,
            Address = request.Address,
            Status = TenantStatus.Trial,
            TrialEndsAt = now.AddDays(30),
            DbHost = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost",
            DbName = Environment.GetEnvironmentVariable("DB_NAME") ?? string.Empty,
            LicenseKeyHash = Guid.NewGuid().ToString("N"),
            ResellerTier = 0
        };

        var admin = new User
        {
            Id = Guid.NewGuid().ToString(),
            UserId = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            Email = request.AdminEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.AdminPassword, workFactor: 12),
            UserName = "관리자",
            Role = UserRole.TenantAdmin,
            IsActive = true
        };

        var subscription = new Subscription
        {
            Id = Guid.NewGuid().ToString(),
            SubscriptionId = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            PlanType = PlanType.Basic,
            BaseUsers = 5,
            ExtraUsers = 0,
            BaseFee = 0,
            ExtraFeePerUser = 10000,
            BillingCycle = "monthly",
            StartedAt = now,
            NextBillingAt = now.AddMonths(1),
            Status = SubscriptionStatus.Active
        };

        await tenants.AddAsync(tenant);
        await users.AddAsync(admin);
        await subscriptions.AddAsync(subscription);
        await _unitOfWork.SaveChangesAsync(ct);

        return new CreateTenantResponse
        {
            TenantId = tenantId,
            TenantCode = tenantCode,
            Message = "등록 완료. 30일 무료 체험이 시작됩니다."
        };
    }
}
