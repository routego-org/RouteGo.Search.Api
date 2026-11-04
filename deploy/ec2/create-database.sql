-- Creates the Search login and database on the shared Postgres container, once. Re-running it only
-- re-sets the password from the secret. Run by start.sh as the Postgres superuser, with the values
-- in SEARCH_DB_USER / SEARCH_DB_NAME / SEARCH_DB_PASSWORD (read from Secrets Manager).
\set ON_ERROR_STOP on
\getenv db_user SEARCH_DB_USER
\getenv db_name SEARCH_DB_NAME
\getenv db_password SEARCH_DB_PASSWORD

SELECT format('CREATE ROLE %I LOGIN', :'db_user')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'db_user') \gexec

SELECT format('ALTER ROLE %I WITH LOGIN PASSWORD %L', :'db_user', :'db_password') \gexec

-- The service user owns its database, so its migrations can create schemas and pgcrypto.
SELECT format('CREATE DATABASE %I OWNER %I', :'db_name', :'db_user')
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = :'db_name') \gexec
