# Postgres — provisionamento da role `hub`

Porta o mecanismo de `tesouro-direto-api/infra/postgres/` para o Hub.
`infra/postgres/sql/hub-role.sql` é a fonte única da verdade, IDEMPOTENTE,
e roda por dois caminhos.

## Caminho 1 — ambiente novo (initdb)

`infra/postgres/initdb/01-provision-hub.sh` é montado em
`/docker-entrypoint-initdb.d/` e roda automaticamente na primeira
inicialização de um volume `pgdata` **vazio**, invocando o SQL com
`HUB_APP_PASSWORD` do ambiente. `POSTGRES_DB: hub` no `docker-compose.yml`
faz o entrypoint oficial da imagem criar o database `hub` (dono inicial: a
role admin `postgres`) ANTES desse hook rodar; o script então transfere a
ownership para `hub`. Não exige nenhum passo manual: basta
`docker compose up` (ou `down -v && up`) num volume novo.

## Caminho 2 — ambiente já inicializado (a armadilha do pgdata persistente)

O hook de `/docker-entrypoint-initdb.d/` **só roda uma vez, na criação do
cluster**. Um volume `pgdata` que já existe **não executa o initdb de
novo** — trocar `docker-compose.yml` ou adicionar o hook não tem efeito
nenhum sobre um volume que já foi inicializado.

Para esses ambientes (por exemplo, `hub-precos-db-1` hoje, que já tem
histórico de migrations aplicado), rode o SQL manualmente como admin. A
senha entra por variável de AMBIENTE (`HUB_APP_PASSWORD`), nunca por `-v`
na linha de comando — evita a senha aparecer em `ps`/histórico de shell; o
SQL a lê com `\getenv`:

```bash
docker exec -e HUB_APP_PASSWORD='<senha-da-hub>' -e PGPASSWORD='<senha-do-admin>' hub-precos-db-1 \
  psql -v ON_ERROR_STOP=1 \
  -U postgres -d hub \
  -f /opt/hub/sql/hub-role.sql
```

Seguro reexecutar quantas vezes for preciso: o script inteiro (criação/
senha da role `hub`, ownership do database, REVOKE/GRANT de CONNECT) é
idempotente.

## Verificação rápida

```sql
SELECT rolname, rolsuper, rolcanlogin FROM pg_roles WHERE rolname = 'hub';
```

`rolsuper` deve ser `f`.

## Decisão de desenho

`docker-compose.yml` define `POSTGRES_DB: hub` (em vez de deixar o SQL
criar o database explicitamente com `CREATE DATABASE hub`), para que
`hub-role.sql` use `current_database()` em vez de hardcodar o nome do
database — mesmo padrão de `td-app-role.sql` no repo de referência.
`CREATE DATABASE` não roda dentro de bloco `DO`/transação, então criar o
database dentro do próprio SQL exigiria `\gexec`; deixar o entrypoint
oficial da imagem criar o database (via `POSTGRES_DB`) evita essa
complicação e mantém o SQL simples e portável.

## Não portado do molde (`tesouro-direto-api/infra/postgres/`)

O bloco de `REASSIGN`/troca de ownership de uma role legada (`app`, no
tesouro-direto) para a role nova, e os comentários sobre as tarefas
79-A/79-B daquele repo. O Hub é greenfield: não há role legada nem objeto
pré-existente para reassinar.

O bloco final de GRANTs explícitos em `public` (schema, tabelas,
sequences, functions, default privileges) também não foi portado: `hub` é
OWNER do próprio database — opção preferida da ADR-12
(`plataforma-docs/ARQUITETURA.md` §10), diferente do molde, onde `td_app`
só tem privilégios dentro de `tesouro_direto` sem ser dona dele. Dono de
database herda USAGE/CREATE em `public` via `pg_database_owner` (PG 15+)
sem GRANT — verificado por execução em `hub-precos-db-1`. Ver comentário
correspondente em `hub-role.sql` para os detalhes e a consequência caso
isso mude no futuro.
