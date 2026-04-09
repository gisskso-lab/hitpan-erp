using HitPan.Domain.Entities;
using HitPan.Infrastructure.Security.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HitPan.Infrastructure.Persistence.Configurations;

public sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    private readonly EncryptedValueConverter _encryptedConverter;

    public EmployeeConfiguration(EncryptedValueConverter encryptedConverter)
    {
        _encryptedConverter = encryptedConverter;
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
        builder.Property(e => e.EmpType).HasColumnName("emp_type").HasConversion<string>().IsRequired();
        builder.Property(e => e.JoinDate).HasColumnName("join_date").IsRequired();
        builder.Property(e => e.ResignDate).HasColumnName("resign_date");
        builder.Property(e => e.BirthDate).HasColumnName("birth_date").HasMaxLength(200).HasConversion(_encryptedConverter);
        builder.Property(e => e.IdNoHash).HasColumnName("id_no_hash").HasMaxLength(256);
        builder.Property(e => e.Phone).HasColumnName("phone").HasMaxLength(20);
        builder.Property(e => e.Email).HasColumnName("email").HasMaxLength(100);
        builder.Property(e => e.BankName).HasColumnName("bank_name").HasMaxLength(30);
        builder.Property(e => e.BankAccount).HasColumnName("bank_account").HasMaxLength(200).HasConversion(_encryptedConverter);
        builder.Property(e => e.BaseSalary).HasColumnName("base_salary").HasMaxLength(200).HasConversion(_encryptedConverter);
        builder.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(36);
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by").HasMaxLength(36);

        builder.HasIndex(e => new { e.TenantId, e.EmpNo }).IsUnique().HasDatabaseName("uq_tenant_empno");
    }
}
