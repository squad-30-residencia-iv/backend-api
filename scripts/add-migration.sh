#!/bin/sh

# Gera uma migração do EF Core nos dois conjuntos (SQLite e PostgreSQL).
# Uso: sh scripts/add-migration.sh <NomeDaMigracao>

set -e

if [ -z "$1" ]; then
  echo "Uso: sh scripts/add-migration.sh <NomeDaMigracao>"
  exit 1
fi

cd "$(dirname "$0")/.."

dotnet tool restore

for PROVIDER in Sqlite Postgres; do
  # A geração não conecta ao banco; a connection string só precisa ser válida para o provider.
  case "$PROVIDER" in
    Sqlite) CONNECTION="Data Source=design-time.db" ;;
    Postgres) CONNECTION="Host=localhost;Database=design-time" ;;
  esac

  echo "⚙️ Gerando migração '$1' para $PROVIDER..."
  Database__Provider="$PROVIDER" \
  Database__ConnectionString="$CONNECTION" \
  dotnet ef migrations add "$1" \
    --project "src/VoucherCampaigns.Infrastructure.Migrations.$PROVIDER" \
    --startup-project src/VoucherCampaigns.Api \
    --output-dir Migrations
done

echo "✅ Migração '$1' gerada para SQLite e PostgreSQL."
