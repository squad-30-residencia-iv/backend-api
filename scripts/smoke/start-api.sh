#!/bin/sh

# Sobe a API de um checkout (worktree ou o próprio repositório) num SQLite novo, em segundo plano,
# e espera o /health responder. Imprime a pasta da execução, com api.log, api.pid, api.db e porta.
# Uso: sh scripts/smoke/start-api.sh [<pasta-do-projeto>] [<porta>]

set -e

PROJECT="${1:-$(cd "$(dirname "$0")/../.." && pwd)}"
PORT="${2:-5091}"
PROJECT="$(cd "$PROJECT" && pwd)"
RUN_DIR="${SMOKE_RUN_DIR:-${TMPDIR:-${TEMP:-/tmp}}/smoke-api-$PORT}"

rm -rf "$RUN_DIR"
mkdir -p "$RUN_DIR"

# Caminho nativo para o SQLite: no Git Bash, /tmp não existe para o .NET.
DB_PATH="$RUN_DIR/api.db"
case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*) DB_PATH="$(cygpath -w "$DB_PATH")" ;;
esac

cd "$PROJECT"

ASPNETCORE_ENVIRONMENT=Development \
Database__Provider=Sqlite \
Database__ConnectionString="Data Source=$DB_PATH" \
Database__SeedDemoData=true \
nohup dotnet run --project src/VoucherCampaigns.Api --no-launch-profile --urls "http://localhost:$PORT" \
  > "$RUN_DIR/api.log" 2>&1 &

echo $! > "$RUN_DIR/api.pid"
echo "$PORT" > "$RUN_DIR/api.port"

for _ in $(seq 1 90); do
  if curl -s -o /dev/null "http://localhost:$PORT/health"; then
    echo "API no ar em http://localhost:$PORT (pasta: $RUN_DIR)"
    exit 0
  fi
  if ! kill -0 "$(cat "$RUN_DIR/api.pid")" 2>/dev/null; then
    echo "A API encerrou antes de responder. Últimas linhas de $RUN_DIR/api.log:" >&2
    tail -40 "$RUN_DIR/api.log" >&2
    exit 1
  fi
  sleep 1
done

echo "A API não respondeu em 90s. Últimas linhas de $RUN_DIR/api.log:" >&2
tail -40 "$RUN_DIR/api.log" >&2
exit 1
