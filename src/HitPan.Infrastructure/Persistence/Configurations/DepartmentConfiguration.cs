using HitPan.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HitPan.Infrastructure.Persistence.Configurations;

public sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("departments");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("dept_id").HasMaxLength(36);
        builder.Ignore(e => e.DeptId);
        builder.Ignore(e => e.CreatedBy);
        builder.Ignore(e => e.UpdatedBy);

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.ParentDeptId).HasColumnName("parent_dept_id").HasMaxLength(36);
        builder.Property(e => e.DeptName).HasColumnName("dept_name").HasMaxLength(50).IsRequired();
        builder.Property(e => e.DeptCode).HasColumnName("dept_code").HasMaxLength(20);
        builder.Property(e => e.SortOrder).HasColumnName("sort_order").IsRequired();
        builder.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
    }
}
