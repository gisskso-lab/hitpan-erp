using HitPan.Domain.Entities;
using HitPan.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HitPan.Infrastructure.Persistence.Configurations;

public sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.ToTable("items");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("item_id").HasMaxLength(36);
        builder.Ignore(e => e.ItemId);

        builder.Property(e => e.TenantId).HasColumnName("tenant_id").HasMaxLength(36).IsRequired();
        builder.Property(e => e.ItemCode).HasColumnName("item_code").HasMaxLength(30).IsRequired();
        builder.Property(e => e.ItemName).HasColumnName("item_name").HasMaxLength(100).IsRequired();
        // W3-1 (작업지시서 20260707작2, 휴면 enum 지뢰 읽기 안전장치): 출하 DDL·마이그 데이터의
        //   item_type 어휘(finished/semi_finished/raw/promo/assembly)가 enum 멤버명과 달라, 그런 행을
        //   EF 로 읽는 순간 materialize 폭발하는 휴면 지뢰였다(W1-1 users.role 과 동일 부류).
        //   쓰기는 기존과 동일(멤버명 ToString) — 읽기만 관용. 어휘 재설계는 별도 결재로 남김.
        builder.Property(e => e.ItemType).HasColumnName("item_type")
            .HasConversion(
                v => v.ToString(),
                v => ParseItemType(v))
            .IsRequired();
        builder.Property(e => e.CategoryId).HasColumnName("category_id").HasMaxLength(36);
        builder.Property(e => e.Unit).HasColumnName("unit").HasMaxLength(10).IsRequired();
        builder.Property(e => e.StdPrice).HasColumnName("std_price").HasColumnType("decimal(15,2)");
        builder.Property(e => e.CostPrice).HasColumnName("cost_price").HasColumnType("decimal(15,2)");
        builder.Property(e => e.SafeStock).HasColumnName("safe_stock").HasColumnType("decimal(15,3)");
        builder.Property(e => e.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(e => e.Memo).HasColumnName("memo").HasMaxLength(500);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(36);
        builder.Property(e => e.UpdatedBy).HasColumnName("updated_by").HasMaxLength(36);

        builder.HasIndex(e => new { e.TenantId, e.ItemCode }).IsUnique().HasDatabaseName("uq_tenant_code");
    }

    /// <summary>
    /// W3-1 (2026-07-12): items.item_type DB값 → ItemType 안전 변환 (UserConfiguration.ParseUserRole 패턴).
    /// ① 대/소문자 무관 Parse → ② 레거시 어휘 명시 맵(finished/promo→Product, semi_finished→Semi,
    /// raw→Material, assembly→Semi — assembly 의 어휘 재설계는 별도 결재) → ③ 최종 실패 시 Product 폴백.
    /// 폴백 시 경고로그는 정적 컨버터 컨텍스트라 불가(EmployeeConfiguration.ParseEmpType 과 동일 사유).
    /// </summary>
    private static ItemType ParseItemType(string value)
    {
        if (Enum.TryParse<ItemType>(value, ignoreCase: true, out var type))
            return type;

        return value.Trim().ToLowerInvariant() switch
        {
            "finished" or "promo" => ItemType.Product,
            "semi_finished" or "assembly" => ItemType.Semi,
            "raw" => ItemType.Material,
            _ => ItemType.Product
        };
    }
}
