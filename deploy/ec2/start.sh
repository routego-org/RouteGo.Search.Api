#!/usr/bin/env bash
# Create the database (first time), build the image, apply migrations, start the API, then wait
# for /health.
# Usage (on the server): sudo bash ~/search/deploy/ec2/start.sh
set -euo pipefail

cd "$(dirname "$0")"
REGION=ap-south-1
SHARED_NETWORK=ec2_default
POSTGRES_CONTAINER=ec2-postgres-1
POSTGRES_SUPERUSER=notification_user
DB_SECRET=database/postgresql/route-go-search-api

if ! docker network inspect "$SHARED_NETWORK" >/dev/null 2>&1; then
  echo "ERROR: Docker network $SHARED_NETWORK not found. Is the Postgres container ($POSTGRES_CONTAINER) running?" >&2
  exit 1
fi

# The migrate container needs the database name, user and password; the instance role reads them
# from the same secret the API uses, so the password is never written to a file on this server.
echo ">> Reading the database settings from Secrets Manager"
secret_json="$(aws secretsmanager get-secret-value --secret-id "$DB_SECRET" --region "$REGION" \
  --query SecretString --output text)"
read_field() { python3 -c 'import json, sys; print(json.load(sys.stdin)[sys.argv[1]])' "$1" <<< "$secret_json"; }
SEARCH_DB_NAME="$(read_field database)"
SEARCH_DB_USER="$(read_field username)"
SEARCH_DB_PASSWORD="$(read_field password)"
export SEARCH_DB_NAME SEARCH_DB_USER SEARCH_DB_PASSWORD

echo ">> Creating the database login and database (skipped when they exist)"
docker exec -i -e SEARCH_DB_NAME -e SEARCH_DB_USER -e SEARCH_DB_PASSWORD "$POSTGRES_CONTAINER" \
  psql --no-psqlrc --quiet -U "$POSTGRES_SUPERUSER" -d postgres < create-database.sql

echo ">> Building image (first time takes 5-10 minutes)"
docker build -t routego/search:latest ../..

echo ">> Applying migrations and starting the API"
if ! docker compose up -d; then
  echo "ERROR: migrations or API start failed. Last logs:" >&2
  docker compose ps -a
  docker compose logs --tail 50 migrate search
  exit 1
fi

echo ">> Waiting for the API to be healthy"
for _ in $(seq 1 30); do
  if curl -fs http://localhost:5003/health >/dev/null 2>&1; then
    docker compose ps -a
    echo ">> DEPLOY OK — Search API is healthy on port 5003"
    exit 0
  fi
  sleep 2
done

echo "ERROR: Search API did not become healthy. Last logs:" >&2
docker compose ps -a
docker compose logs --tail 50 migrate search
exit 1
