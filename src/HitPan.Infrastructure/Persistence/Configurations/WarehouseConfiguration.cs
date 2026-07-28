using HitPan.Domain.Entities;
using HitPan.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HitPan.Infrastructure.Persistence.Configurations;

public sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("warehouses");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("warehouse_id").HasMaxLength(36);
        builder.Ignore(e => e.WarehouseId);
        builder.Ignore(e => e.CreatedBy);
        builder.Ignore(e => e.UpdatedBy);

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.WhCode).HasColumnName("wh_code").HasMaxLength(20).IsRequired();
        builder.Property(e => e.WhName).HasColumnName("wh_name").HasMaxLength(50).IsRequired();
        // W3-2 (작업지시서 20260707작2, 휴면 enum 지뢰 읽기 안전장치): 마이그·수기 데이터의 wh_type
        //   한글 어휘(자사창고·3PL·위탁·불량·반품·기타)가 enum 멤버명과 달라 읽는 순간 materialize
        //   폭발하는 휴면 지뢰였다(W1-1 users.role 과 동일 부류). 쓰기는 기존과 동일 — 읽기만 관용.
        builder.Property(e => e.WhType).HasColumnName("wh_type")
            .HasConversion(
                v => v.ToString(),
                v => ParseWarehouseType(v))
            .IsRequired();
        builder.Property(e => e.Location).HasColumnName("location").HasMaxLength(100);
        builder.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.WhCode }).IsUnique().HasDatabaseName("uq_tenant_code");
    }

    /// <summary>
    /// W3-2 (2026-07-12): warehouses.wh_type DB값 → WarehouseType 안전 변환 (ParseUserRole 패턴).
    /// ① 대/소문자 무관 Parse → ② 한글 어휘 명시 맵(자사창고→Normal, 3PL/위탁→Consign, 불량→Defect,
    /// 반품→Return, 기타→Normal) → ③ 최종 실패 시 Normal 폴백.
    /// 폴백 시 경고로그는 정적 컨버터 컨텍스트라 불가(EmployeeConfiguration.ParseEmpType 과 동일 사유).
    /// </summary>
    private static WarehouseType ParseWarehouseType(string value)
    {
        if (Enum.TryParse<WarehouseType>(value, ignoreCase: true, out var type))
            return type;

        return value.Trim() switch
        {
            "자사창고" or "기타" => WarehouseType.Normal,
            "3PL" or "위탁" => WarehouseType.Consign,
            "불량" => WarehouseType.Defect,
            "반품" => WarehouseType.Return,
            _ => WarehouseType.Normal
        };
    }
}
