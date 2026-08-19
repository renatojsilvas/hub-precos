-- hub-role.sql — provisiona a role `hub` (NOSUPERUSER) e fecha o database
-- `hub` para PUBLIC. Porta o mecanismo de
-- tesouro-direto-api/infra/postgres/sql/td-app-role.sql para o Hub
-- (greenfield: sem role legada, sem REASSIGN de ownership pré-existente).
--
-- IDEMPOTENTE de propósito: este arquivo roda por DOIS caminhos diferentes
-- e precisa produzir o mesmo estado final em qualquer um deles, quantas
-- vezes for executado:
--   1) `/docker-entrypoint-initdb.d/` — só em ambiente NOVO (volume pgdata
--      vazio); o script `infra/postgres/initdb/01-provision-hub.sh` o
--      invoca, com a senha chegando via variável de ambiente
--      HUB_APP_PASSWORD.
--   2) `docker exec ... psql -f` como admin, em ambiente JÁ inicializado —
--      porque o volume `pgdata` é persistente e o initdb NÃO roda de novo
--      sobre ele. Ver `infra/postgres/README.md`.
-- Por isso: `CREATE ROLE` vira `CREATE ou ALTER` conforme `pg_roles`, e o
-- REVOKE/GRANT de DATABASE e o ALTER DATABASE ... OWNER usam
-- `current_database()` (nunca hardcoded), garantindo que o mesmo arquivo
-- funcione qualquer que seja o nome do database ao qual o psql se conectou.
--
-- Database `hub` já existe quando este SQL roda: `POSTGRES_DB: hub` no
-- docker-compose.yml faz o entrypoint oficial da imagem criá-lo (dono
-- inicial: a role admin de bootstrap, `POSTGRES_USER`) ANTES de disparar
-- os hooks de `/docker-entrypoint-initdb.d/`. Este script transfere a
-- ownership para `hub` via `ALTER DATABASE ... OWNER TO hub` — preserva o
-- estado que o `infra/init-db.sql` antigo criava com
-- `CREATE DATABASE hub OWNER hub`, mas sem hardcodar o nome.
--
-- NÃO PORTADO do molde: o bloco de REASSIGN de ownership de uma role
-- legada (`app`, no tesouro-direto) para a role nova. O Hub é greenfield —
-- não existe role/objeto pré-existente para reassinar.
--
-- NÃO PORTADO do molde: o bloco final de GRANTs explícitos em `public`
-- (USAGE/CREATE no schema, SELECT/INSERT/... em ALL TABLES, sequences,
-- functions, ALTER DEFAULT PRIVILEGES). Diferença de fundo com o molde:
-- lá `td_app` é só titular de privilégios dentro do database
-- `tesouro_direto`, nunca dona dele. Aqui `hub` é OWNER do próprio
-- database (linha `ALTER DATABASE ... OWNER TO hub` acima) — a opção
-- PREFERIDA da ADR-12 (`../plataforma-docs/ARQUITETURA.md` §10: "owner
-- apenas do seu database (preferido; backup/restore independente) ou
-- schema"), enquanto o molde usa a segunda opção (owner de schema). A
-- partir do Postgres 15, o schema `public` de um database novo pertence
-- ao pseudo-role `pg_database_owner`, do qual o dono do database é membro
-- automático — `hub` herda USAGE/CREATE em `public` sem GRANT nenhum, e
-- objetos que ela cria já nascem dela (mesma razão do REASSIGN acima não
-- ter equivalente aqui). Verificado por execução no cluster real
-- (`hub-precos-db-1`): `has_schema_privilege('hub','public','CREATE'/
-- 'USAGE')` = true, `pg_has_role('hub','pg_database_owner','MEMBER')` =
-- true, e a migration `InitialCreate` do EF já criou
-- `__EFMigrationsHistory` em `public` conectada como `hub`, sem GRANT
-- algum. CONSEQUÊNCIA: se `hub` um dia deixar de ser dona do database
-- (ex.: migrar para o modelo de owner-de-schema), o bloco de GRANTs do
-- molde volta a ser necessário.

-- Senha via variável de AMBIENTE (HUB_APP_PASSWORD), nunca via argv do
-- psql — evita aparecer em `ps`/histórico de shell. `\getenv` deixa
-- `hub_app_password` INDEFINIDA se a env var não existir; o `\if` abaixo
-- converge esse caso e o de string vazia para o MESMO estado (variável
-- psql = ''), e o único ponto de falha real é o `RAISE EXCEPTION` dentro
-- do bloco DO logo adiante — com `ON_ERROR_STOP=1` isso faz o psql sair
-- != 0 e com mensagem clara.
--
-- Não usamos `\quit <código>` aqui: `\quit` do psql NÃO aceita argumento
-- de código de saída (a versão com argumento é ignorada com um warning e
-- o psql sai 0 mesmo assim) — usá-lo faria a guarda disparar, pular todo
-- o provisionamento, e o psql concluir com sucesso aparente sem ter
-- criado role nenhuma. Falha silenciosa clássica, evitada aqui de
-- propósito.
\getenv hub_app_password HUB_APP_PASSWORD
\if :{?hub_app_password}
\else
  \set hub_app_password ''
\endif

-- Publica a senha recebida numa GUC de sessão para o bloco DO ler.
-- Variáveis psql (`:'nome'`) NÃO são interpoladas dentro de blocos
-- dollar-quoted (`DO $$ ... $$`), então o valor precisa entrar por fora
-- do bloco — este é o jeito canônico de passar um valor de psql para
-- dentro de PL/pgSQL.
--
-- `\o /dev/null` .. `\o` suprime a SAÍDA deste SELECT: sem isso, o psql
-- ecoa o valor de retorno de `set_config` (a própria senha, em claro) no
-- stdout. No caminho do initdb, esse stdout vai para `docker logs`, que
-- qualquer coletor de observabilidade (Loki/Grafana, etc.) pode indexar —
-- senha de aplicação em log é inaceitável. Confirmado por execução (ver
-- verificação da tarefa) que, com a supressão, a senha não aparece em
-- nenhum lugar da saída do psql.
\o /dev/null
SELECT set_config('hub.provision_password', :'hub_app_password', false);
\o

DO $$
DECLARE
  v_password text := current_setting('hub.provision_password');
BEGIN
  IF v_password = '' THEN
    RAISE EXCEPTION 'HUB_APP_PASSWORD não foi definida ou está vazia — obrigatória para provisionar a role hub.';
  END IF;

  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'hub') THEN
    -- Converge o estado mesmo se `hub` tiver sido promovida a algo
    -- diferente de NOSUPERUSER por engano — rebaixa de volta.
    EXECUTE format(
      'ALTER ROLE hub WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD %L',
      v_password
    );
  ELSE
    EXECUTE format(
      'CREATE ROLE hub WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE PASSWORD %L',
      v_password
    );
  END IF;
END
$$;

-- Transfere a posse do database para `hub` e fecha para qualquer role
-- futura na instância: só quem recebe CONNECT explícito conecta.
-- `current_database()` evita hardcode do nome.
DO $$
BEGIN
  EXECUTE format('ALTER DATABASE %I OWNER TO hub', current_database());
  EXECUTE format('REVOKE CONNECT ON DATABASE %I FROM PUBLIC', current_database());
  EXECUTE format('GRANT CONNECT ON DATABASE %I TO hub', current_database());
END
$$;
