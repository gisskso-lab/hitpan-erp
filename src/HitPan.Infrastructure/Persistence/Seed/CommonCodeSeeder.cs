using Dapper;
using System.Data;
using System.Data.Common;

namespace HitPan.Infrastructure.Persistence.Seed;

public sealed class CommonCodeSeeder
{
    private readonly IDbConnection _dbConnection;

    public CommonCodeSeeder(IDbConnection dbConnection)
    {
        _dbConnection = dbConnection;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var codeGroups = new Dictionary<string, string[]>
        {
            ["ITEM_TYPE"] = ["product", "material", "semi", "expense"],
            ["UNIT"] = ["EA", "KG", "M", "L", "BOX", "SET"],
            ["PAY_METHOD"] = ["cash", "card", "transfer", "credit"],
            ["PARTNER_TYPE"] = ["customer", "supplier", "both"],
            ["WH_TYPE"] = ["normal", "consign", "defect", "return"]
        };

        await EnsureOpenAsync(ct);
        foreach (var group in codeGroups)
        {
            for (var i = 0; i < group.Value.Length; i++)
            {
                var codeValue = group.Value[i];
                const string countSql = """
                    SELECT COUNT(*) FROM common_codes
                    WHERE tenant_id IS NULL
                      AND code_group = @CodeGroup
                      AND code_value = @CodeValue
                    """;

                var count = await _dbConnection.ExecuteScalarAsync<long>(
                    new CommandDefinition(
                        countSql,
                        new { CodeGroup = group.Key, CodeValue = codeValue },
                        cancellationToken: ct));

                if (count > 0)
                {
                    continue;
                }

                await _dbConnection.ExecuteAsync(
                    new CommandDefinition(
                        """
                        INSERT INTO common_codes
                          (code_id, tenant_id, code_group, code_value, code_label, sort_order, is_active)
                        VALUES (@CodeId, NULL, @CodeGroup, @CodeValue, @CodeLabel, @SortOrder, 1)
                        """,
                        new
                        {
                            CodeId = Guid.NewGuid().ToString(),
                            CodeGroup = group.Key,
                            CodeValue = codeValue,
                            CodeLabel = codeValue,
                            SortOrder = i
                        },
                        cancellationToken: ct));
            }
        }
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_dbConnection.State == ConnectionState.Open)
        {
            return;
        }

        if (_dbConnection is DbConnection dbConnection)
        {
            await dbConnection.OpenAsync(ct);
            return;
        }

        _dbConnection.Open();
    }
}
