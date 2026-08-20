#!/usr/bin/env bash
# Provisiona o database e a role do Hub num Postgres COMPARTILHADO já existente
# (o caminho da VPS), em vez do container próprio do docker-compose.yml local.
#
# POR QUE ESTE SCRIPT EXISTE, e por que `hub-role.sql` não basta sozinho:
# o `hub-role.sql` provisiona a ROLE e assume que o database já existe — localmente
# quem o cria é `POSTGRES_DB: hub` no docker-compose.yml, executado pelo entrypoint
# da imagem antes dos hooks de initdb. Num cluster que já está de pé e é
# compartilhado (§12 do plano: td_api, hub, custodia e operacoes no mesmo Postgres),
# não há entrypoint nenhum para rodar — o database precisa ser criado por fora, e é
# o que o passo 1 abaixo faz.
#
# `CREATE DATABASE` não roda dentro de transação nem de bloco DO, então não dá para
# embutir no `hub-role.sql` (que é atômico de propósito, com BEGIN/COMMIT). Daí a
# separação em dois passos, com `\gexec` no primeiro — a forma canônica de executar
# DDL gerado condicionalmente no psql.
#
# IDEMPOTENTE: seguro reexecutar. O passo 1 não recria um database existente; o
# passo 2 converge a role (create-ou-ALTER) e reaplica ownership/REVOKE/GRANT.
#
# Uso (na VPS, a partir da raiz do repo):
#   HUB_APP_PASSWORD='...' ./infra/postgres/provision-remote.sh
#
# Variáveis:
#   HUB_APP_PASSWORD  (obrigatória) senha da role `hub`
#   PG_CONTAINER      (default: tesouro-direto-db) container do Postgres compartilhado
#   PG_ADMIN_USER     (default: postgres) role admin de bootstrap do cluster
#   HUB_DB_NAME       (default: hub) nome do database do Hub
set -euo pipefail

: "${HUB_APP_PASSWORD:?HUB_APP_PASSWORD é obrigatória (senha da role hub)}"

PG_CONTAINER="${PG_CONTAINER:-tesouro-direto-db}"
PG_ADMIN_USER="${PG_ADMIN_USER:-postgres}"
HUB_DB_NAME="${HUB_DB_NAME:-hub}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROLE_SQL="$SCRIPT_DIR/sql/hub-role.sql"

[ -f "$ROLE_SQL" ] || { echo "ERRO: não achei $ROLE_SQL" >&2; exit 1; }

if ! docker ps --format '{{.Names}}' | grep -qx "$PG_CONTAINER"; then
  echo "ERRO: container '$PG_CONTAINER' não está rodando. Containers ativos:" >&2
  docker ps --format '  {{.Names}}' >&2
  exit 1
fi

echo "==> 1/2 database '$HUB_DB_NAME' (cria se não existir)"
# A senha NÃO passa por aqui: este passo é só DDL de database, e mandar a senha
# junto ampliaria sem motivo a superfície onde ela aparece.
docker exec -i "$PG_CONTAINER" \
  psql -v ON_ERROR_STOP=1 -U "$PG_ADMIN_USER" -d postgres -v db="$HUB_DB_NAME" <<'SQL'
SELECT format('CREATE DATABASE %I', :'db')
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = :'db');
\gexec
SQL

echo "==> 2/2 role 'hub' e permissões (hub-role.sql, idempotente)"
# `-e` põe as variáveis no ambiente do processo psql DENTRO do container, que é de
# onde o `\getenv` do hub-role.sql as lê. Nunca via argv (`-v`), para a senha não
# aparecer em `ps` nem no histórico de shell.
docker exec -i \
  -e HUB_APP_PASSWORD="$HUB_APP_PASSWORD" \
  -e HUB_DB_NAME="$HUB_DB_NAME" \
  "$PG_CONTAINER" \
  psql -v ON_ERROR_STOP=1 -U "$PG_ADMIN_USER" -d "$HUB_DB_NAME" -f - < "$ROLE_SQL"

echo "==> pronto: database '$HUB_DB_NAME' e role 'hub' provisionados em '$PG_CONTAINER'"
