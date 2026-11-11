#!/bin/sh

# Encerra a API iniciada por start-api.sh (o processo e os filhos).
# Uso: sh scripts/smoke/stop-api.sh <pasta-da-execução>

RUN_DIR="${1:?Informe a pasta da execução impressa pelo start-api.sh}"

if [ ! -f "$RUN_DIR/api.pid" ]; then
  echo "Sem api.pid em $RUN_DIR; nada a encerrar."
  exit 0
fi

PID="$(cat "$RUN_DIR/api.pid")"

case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*)
    # O pid do Git Bash não é o do Windows: encerra pela porta, que é o que a API ocupa.
    PORT="$(cat "$RUN_DIR/api.port" 2>/dev/null)"
    for WINPID in $(netstat -ano 2>/dev/null | awk -v p=":$PORT" '$2 ~ p"$" && $4 == "LISTENING" { print $5 }' | sort -u); do
      taskkill //PID "$WINPID" //T //F > /dev/null 2>&1 || true
    done
    kill "$PID" 2>/dev/null || true
    ;;
  *)
    pkill -TERM -P "$PID" 2>/dev/null || true
    kill "$PID" 2>/dev/null || true
    ;;
esac

echo "API encerrada (pasta $RUN_DIR mantida para leitura do api.log)."
