using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace VoucherCampaigns.Infrastructure.Persistence;

/// <summary>
/// Converte colunas, chaves, índices e FKs para snake_case, o padrão do PostgreSQL.
/// Os nomes das tabelas são definidos em cada configuração.
/// </summary>
public static class SnakeCaseNaming
{
    public static void ApplySnakeCaseNames(this ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.GetColumnName(table)!));
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName()!));
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                foreignKey.SetConstraintName(ToSnakeCase(foreignKey.GetConstraintName()!));
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
            }
        }
    }

    public static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);

        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];

            if (!char.IsUpper(current))
            {
                builder.Append(current);
                continue;
            }

            // "_" antes de cada nova palavra: "NetworkId" -> "network_id", "PK_networks" -> "pk_networks".
            var startsWord = i > 0
                && name[i - 1] != '_'
                && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1])));

            if (startsWord)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }
}
