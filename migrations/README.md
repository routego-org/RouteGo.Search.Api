# Migrations

Raw, versioned SQL for the RouteGo.Search.API database. The application never applies these
scripts; the CI/CD pipeline's DbUp step does. There is no runner project in this solution.

## Pipeline contract

The DbUp configuration lives in the pipeline, outside this repo. It must:

| Property | Requirement |
| --- | --- |
| Variables | Variable substitution **disabled** (DbUp's `$variable$` collides with PL/pgSQL `$$` bodies) |
| Transaction | **No wrapping transaction**; each script owns its `BEGIN`/`COMMIT` |
| Run-always | `R__*.sql` routed as **RunAlways**, in a run group after every `V####` script |
| Journal | Journal table at **`jobs.schema_versions`** |
| `jobs` schema | Ensured before the journal is created (`CREATE SCHEMA IF NOT EXISTS jobs;`) |
| Empty folder | A missing or empty `migrations/` **fails** the step |

How an operator triggers a run: not decided yet — the pipeline does not exist yet.

## Area → schema

| Spec area | Postgres schema |
| --- | --- |
| `data-model/trip-listings/` | `trip_listings` |
