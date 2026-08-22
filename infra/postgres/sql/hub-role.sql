\getenv hub_db_name HUB_DB_NAME
\if :{?hub_db_name}
\else
  \set hub_db_name 'hub'
\endif
SELECT set_config('hub.provision_expected_db', :'hub_db_name', false);

DO $$
DECLARE
  v_expected_db text := current_setting('hub.provision_expected_db');
BEGIN
  IF current_database() <> v_expected_db THEN
    RAISE EXCEPTION 'hub-role.sql: current_database() = "%", esperado "%" (variável HUB_DB_NAME, default ''hub''). Abortando ANTES de qualquer alteração — confira o -d/--dbname do comando psql. A role hub é global ao cluster: rodar isto contra o database errado rotaciona a senha da role hub de verdade.',
      current_database(), v_expected_db;
  END IF;
END
$$;

\getenv hub_app_password HUB_APP_PASSWORD
\if :{?hub_app_password}
\else
  \set hub_app_password ''
\endif

\o /dev/null
SELECT set_config('hub.provision_password', :'hub_app_password', false);
\o

BEGIN;

DO $$
DECLARE
  v_password text := current_setting('hub.provision_password');
BEGIN
  IF v_password = '' THEN
    RAISE EXCEPTION 'HUB_APP_PASSWORD não foi definida ou está vazia — obrigatória para provisionar a role hub.';
  END IF;

  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'hub') THEN
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

DO $$
BEGIN
  EXECUTE format('ALTER DATABASE %I OWNER TO hub', current_database());
  EXECUTE format('REVOKE CONNECT ON DATABASE %I FROM PUBLIC', current_database());
  EXECUTE format('GRANT CONNECT ON DATABASE %I TO hub', current_database());
END
$$;

COMMIT;
