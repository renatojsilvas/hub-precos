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

### Atomicidade (o script não deixa estado meio-aplicado)

`hub-role.sql` roda a criação/senha da role e a transferência de
ownership + REVOKE/GRANT de CONNECT dentro de um único `BEGIN`/`COMMIT`.
Se o processo cair (kill/OOM/queda de rede) no meio da execução, NADA é
aplicado — nem a role `hub` fica criada. A garantia está no `.sql`, não
numa flag do comando acima: não é preciso (nem é preciso lembrar de)
passar `--single-transaction`/`-1` ao `psql`.

### `-d`/`--dbname` errado NÃO rotaciona a senha da `hub` em silêncio

A role `hub` é global ao cluster (roles não pertencem a um database). O
`hub-role.sql` guarda o database esperado: aborta com `RAISE EXCEPTION`
(sem alterar nada) se `current_database()` não bater com a variável de
ambiente `HUB_DB_NAME` (default `'hub'`). Rodando contra `hub`, não é
preciso definir nada. Rodando contra outro database (ex.: um futuro
`hub_e2e`), passe `-e HUB_DB_NAME=hub_e2e` além de `-d hub_e2e` — sem
isso, o comando aborta em vez de rotacionar a senha da role `hub` de
verdade e abrir o database errado para `hub`.

## Verificação rápida

```sql
SELECT rolname, rolsuper, rolcanlogin FROM pg_roles WHERE rolname = 'hub';
```

`rolsuper` deve ser `f`.

## Risco residual conhecido: senha em claro fora do psql/`docker logs`

`hub-role.sql` (o `\o /dev/null` .. `\o` ao redor do `set_config` da
senha) protege a saída do **comando `psql`** — a senha não aparece no
stdout/stderr daquele processo, e portanto não aparece em `docker logs`.
Isso é tudo que a supressão promete; não é uma garantia geral sobre todo
lugar onde a senha circula.

**O que NÃO está protegido**, porque é inerente a qualquer segredo
passado por variável de ambiente no `docker-compose.yml` (não é uma
regressão desta correção, e não viola PADROES.md §6 — nada disso vai
para o git):

- `docker inspect hub-precos-db-1 --format '{{json .Config.Env}}'` —
  mostra `HUB_APP_PASSWORD` em claro, porque a env var fica associada ao
  processo do container enquanto ele existir.
- `docker compose config` — expande `${HUB_APP_PASSWORD}` do `.env` e
  imprime o valor em claro; é trivial vazar isso colando a saída num
  ticket/PR/CI sem perceber.

Trate a saída desses dois comandos como segredo (não cole em lugar
compartilhado) da mesma forma que trataria o `.env`.

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
