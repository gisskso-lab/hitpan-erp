using HitPan.Domain.Entities;
using HitPan.Domain.Enums;
using HitPan.Infrastructure.Security.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HitPan.Infrastructure.Persistence.Configurations;

public sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    private readonly EncryptedValueConverter _encryptedConverter;
    private readonly NullableEncryptedValueConverter _nullableEncryptedConverter;

    public EmployeeConfiguration(
        EncryptedValueConverter encryptedConverter,
        NullableEncryptedValueConverter nullableEncryptedConverter)
    {
        _encryptedConverter = encryptedConverter;
        _nullableEncryptedConverter = nullableEncryptedConverter;
    }

    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("employees");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("employee_id").HasMaxLength(36);
        builder.Ignore(e => e.EmployeeId);

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(36);
        builder.Property(e => e.EmpNo).HasColumnName("emp_no").HasMaxLength(20).IsRequired();
        builder.Property(e => e.EmpName).HasColumnName("emp_name").HasMaxLength(50).IsRequired();
        builder.Property(e => e.DeptId).HasColumnName("dept_id").HasMaxLength(36);
        builder.Property(e => e.Position).HasColumnName("position").HasMaxLength(30);
        builder.Property(e => e.JobTitle).HasColumnName("job_title").HasMaxLength(30);
        // 작3v3(2026-06-26): emp_type 소문자 정규화 + ignoreCase + 유령값 폴백 = 근본 면역.
        // 저장은 항상 소문자로 통일, 조회는 대/소문자 무관 Parse + Parse 실패(과거 박힌
        // 'fulltime'/'full_time' 등 enum 미정합 유령값) 시 Regular로 폴백 → 어떤 경로가
        // 무슨 값을 넣었어도/넣어도 employee materialize(=로그인 employee_id 클레임)가 안 깨짐.
        // (이전 HasConversion<string>()은 멤버명 'Regular'만 인식 → 유령값 로그인 Parse 폭발이 P0였음)
        // 검증팀장 반증4: 운영DB에 잔존할 수 있는 유령값 행은 코드만으로 못 치유 → 이 폴백이 그 갭까지 봉합.
        builder.Property(e => e.EmpType).HasColumnName("emp_type")
            .HasConversion(
                v => v.ToString().ToLowerInvariant(),
                v => ParseEmpType(v))
            .IsRequired();
        builder.Property(e => e.JoinDate).HasColumnName("join_date").IsRequired();
        builder.Property(e => e.ResignDate).HasColumnName("resign_date");
        builder.Property(e => e.BirthDate).HasColumnName("birth_date").HasMaxLength(200).HasConversion(_nullableEncryptedConverter);
        builder.Property(e => e.IdNoHash).HasColumnName("id_no_hash").HasMaxLength(256);
        builder.Property(e => e.Phone).HasColumnName("phone").HasMaxLength(20);
        builder.Property(e => e.Email).HasColumnName("email").HasMaxLength(100);
        builder.Property(e => e.BankName).HasColumnName("bank_name").HasMaxLength(30);
        builder.Property(e => e.BankAccount).HasColumnName("bank_account").HasMaxLength(200).HasConversion(_nullableEncryptedConverter);
        builder.Property(e => e.BaseSalary).HasColumnName("base_salary").HasMaxLength(200).HasConversion(_nullableEncryptedConverter);
        builder.Property(e => e.Role).HasColumnName("role").HasMaxLength(30).IsRequired();
        builder.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();

        // 작20260429 연차 관리 — DECIMAL(5,1) (소수점 0.5일 단위 가능)
        builder.Property(e => e.AnnualLeaveTotal).HasColumnName("annual_leave_total").HasColumnType("decimal(5,1)").IsRequired();
        builder.Property(e => e.AnnualLeaveUsed).HasColumnName("annual_leave_used").HasColumnType("decimal(5,1)").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(36);
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by").HasMaxLength(36);

        builder.HasIndex(e => new { e.TenantId, e.EmpNo }).IsUnique().HasDatabaseName("uq_tenant_empno");
    }

    /// <summary>
    /// 작3v3(2026-06-26): emp_type DB값 → EmployeeType 안전 변환.
    /// 대/소문자 무관(ignoreCase)으로 Parse하고, enum 미정합 유령값('fulltime'/'full_time'/빈값 등,
    /// 봉합 이전 잘못 박힌 행)이 오면 예외 대신 Regular로 폴백한다.
    /// 이유: Enum.Parse 예외는 employee materialize를 막아 로그인 employee_id 클레임을 비우는 P0였다.
    /// 폴백으로 과거 오염 행도 로그인은 살리되, 화면은 정규직(Regular)으로 보정 표시.
    /// </summary>
    private static EmployeeType ParseEmpType(string value)
        => Enum.TryParse<EmployeeType>(value, ignoreCase: true, out var t)
            ? t
            : EmployeeType.Regular;
}
