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

# NORMALIZAÇÃO — não é frescura, é a causa raiz de um deploy quebrado (2026-08-20).
# O segredo chegava ao provisionamento por `docker exec -e`, que preserva o valor
# byte a byte, e à aplicação pelo `.env` do compose, que é lido POR LINHA e portanto
# descarta um `\n`/`\r` final. Bastava o segredo ter sido colado com quebra de linha
# para a role nascer com um byte a mais do que a aplicação envia: `28P01 password
# authentication failed`, com os dois lados "parecendo" iguais em qualquer comparação
# feita a partir do `.env`. Normalizar aqui faz os dois caminhos convergirem na origem.
HUB_APP_PASSWORD="$(printf '%s' "$HUB_APP_PASSWORD" | tr -d '\r\n')"
[ -n "$HUB_APP_PASSWORD" ] || { echo "ERRO: HUB_APP_PASSWORD ficou vazia após remover quebras de linha." >&2; exit 1; }

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

echo "==> 3/3 verificando que a credencial provisionada REALMENTE autentica"
# Por que este passo existe: até 2026-08-20 o script terminava aqui, reportando
# sucesso sem nunca ter testado a credencial que acabou de criar. Quando o valor
# divergiu (ver NORMALIZAÇÃO acima), o provisionamento passou verde e o defeito só
# apareceu minutos depois, como crash-loop da aplicação num container à parte — longe
# da causa e caro de diagnosticar. Provisionar sem verificar é afirmar sem evidência.
#
# O teste tem que passar PELA REDE, e não por `docker exec` no próprio Postgres: o
# `pg_hba.conf` da imagem oficial tem `host all all 127.0.0.1/32 trust`, então um
# teste por loopback autentica QUALQUER senha e daria um falso positivo — exatamente
# o tipo de verificação que parece proteger e não protege.
PG_NETWORK="$(docker inspect -f '{{range $k, $v := .NetworkSettings.Networks}}{{$k}} {{end}}' "$PG_CONTAINER" | awk '{print $1}')"
PG_IMAGE="$(docker inspect -f '{{.Config.Image}}' "$PG_CONTAINER")"

[ -n "$PG_NETWORK" ] || { echo "ERRO: não descobri a rede docker de '$PG_CONTAINER'." >&2; exit 1; }

if docker run --rm --network "$PG_NETWORK" \
     -e PGPASSWORD="$HUB_APP_PASSWORD" \
     "$PG_IMAGE" \
     psql -h "$PG_CONTAINER" -U hub -d "$HUB_DB_NAME" -tAc 'select 1' >/dev/null 2>&1; then
  echo "    credencial confere (autenticou como 'hub' em '$HUB_DB_NAME' via rede '$PG_NETWORK')"
else
  echo "ERRO: a role 'hub' foi provisionada mas NÃO autentica com a senha fornecida." >&2
  echo "      Rede testada: '$PG_NETWORK'. Isso normalmente significa que o valor de" >&2
  echo "      HUB_APP_PASSWORD difere entre quem provisiona e quem conecta." >&2
  exit 1
fi

echo "==> pronto: database '$HUB_DB_NAME' e role 'hub' provisionados e verificados em '$PG_CONTAINER'"
