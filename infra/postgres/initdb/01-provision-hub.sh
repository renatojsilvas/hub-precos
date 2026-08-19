#!/usr/bin/env bash
# Hook do /docker-entrypoint-initdb.d/, executado pelo entrypoint oficial da
# imagem postgres SÓ na primeira inicialização de um volume `pgdata` vazio.
#
# O SQL de verdade (infra/postgres/sql/hub-role.sql) vive FORA deste
# diretório de propósito: o entrypoint do Postgres executa TODO arquivo
# `.sql` que encontrar em `/docker-entrypoint-initdb.d/` diretamente via
# `psql`, sem passar nenhuma variável — e aquele SQL exige a variável de
# ambiente HUB_APP_PASSWORD. Executado sozinho ali, falharia. Por isso
# `infra/postgres/sql/` é montado num caminho separado (`/opt/hub/sql`, ver
# docker-compose.yml) e este script — que roda com HUB_APP_PASSWORD já no
# ambiente do container — é o único arquivo deste diretório.
set -euo pipefail

if [ -z "${HUB_APP_PASSWORD:-}" ]; then
  echo "ERRO: HUB_APP_PASSWORD não definida (ou vazia) — obrigatória para provisionar a role hub." >&2
  exit 1
fi

# Não passamos a senha via `-v` (argv do psql): HUB_APP_PASSWORD já está no
# ambiente do processo (herdado do container), e o SQL a lê com `\getenv`.
# Isso evita a senha aparecer em `ps`/histórico de shell.
psql -v ON_ERROR_STOP=1 \
  --username "$POSTGRES_USER" \
  --dbname "$POSTGRES_DB" \
  -f /opt/hub/sql/hub-role.sql
