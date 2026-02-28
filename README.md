# AGPME Bloomberg **Stream Playground** (C#/.NET 8)

A developer-friendly environment to **simulate Bloomberg-style streaming** of position data, **ingest** it via a subscriptions client, **persist** to a local Postgres, and **compare** the results against a target environment (UAT/PROD). The solution gives you an end‑to‑end loop to iterate on the realtime ingest path without touching production.

> Solution file: `agpme-bbg-stream-playground.sln`  
> Projects: Simulator API, Subscriptions API, Admin (Blazor), Bootstrap utility. 

---

## Why this exists — the problem it solves

- **Streaming is hard to test**: Production feeds are noisy, stateful, and often opaque. You need a safe loop to **replay sessions**, include **initial paint → heartbeats → intraday updates**, and examine what your ETL actually wrote. 
- **Deterministic playground**: This repo provides a **simulator** that streams JSON rows from a Postgres table (`app_data.bbg_positions_stream`) and a **client** that consumes the stream, persists to inbound, and calls the same **stored procedures** your ETL uses. Then, a **compare** step checks local results vs **target DB** using the shared comparison library. 
- **Tight feedback**: The Admin UI exposes **start/stop subscriptions**, **as‑of controls**, **metadata resync/edit**, and a **Compare** page that produces CSV artifacts for quick diffs. 

---

## Project structure

```
agpme-bbg-stream-playground.sln
│
├─ Agpme.Bbg.Playground.Simulator.Api/         # Stream server (minimal API)
├─ Agpme.Bbg.Playground.Subscriptions.Api/     # Subscriptions + Compare service (minimal API)
├─ Agpme.Bbg.Playground.Admin/                 # Blazor Server admin console (MudBlazor)
└─ Agpme.Bbg.Playground.Bootstrap/             # Bootstrapper (Docker PG, DB scripts, metadata sync)
```

**Key responsibilities**
- **Simulator API** (`/trading-solutions/positions/.../subscriptions`): Serves **initial paint**, then emits **`{}` heartbeats** and **intraday updates**; supports optional **partial chunking** to mimic real socket fragmentation. 
- **Subscriptions API**: Manages **subscription workers**, persists inbound (COPY), invokes **`app_data.bbg_upsert_positions_from_inbound`** for initial and intraday, exposes **client endpoints** for **health**, **settings**, **targets**, **admin reset**, **metadata resync/validate**, and **compare**. Swagger enabled in dev. 
- **Admin (Blazor)**: Friendly UI to **Start/Stop** for selected entities, set **as‑of**, view **DB health**, inspect and **edit metadata rows**, and run **Compare** (with field‑set options and CSV artifact links). Uses **MudBlazor**. 
- **Bootstrap**: Spins up a **local Postgres** via Docker Compose, **applies DB scripts**, and can **sync metadata** from an AWS‑backed source environment into the local DB. 

---

## High‑level architecture & data flow

1. **Simulator** reads prepared JSON from `app_data.bbg_positions_stream` and streams them (initial → `{}` markers → updates). 
2. **Subscriptions** connects to the simulator endpoint, buffers **initial** payload until first `{}` heartbeat, then **persists initial batch** and calls the **initial upsert**. Subsequent non‑empty JSONs go **one‑by‑one** through **intraday persist** + **intraday upsert**. 
3. **Admin** lets you inspect status, edit metadata, and **compare** local `bbg_positions` with **target** (UAT/PROD current or history). Compare uses the shared **`BbgCompareSession`** and writes CSVs under `artifacts/compare/...`. 

---

## Prerequisites

- **.NET 8 SDK** and **Docker Desktop** (for local Postgres). 
- AWS credentials/profile if you plan to **sync metadata** or query **target DB** through **Secrets Manager** (the AWS secret fetch is wired through the shared helper). 

---

## Getting started (dev)

## AWS Credentials Requirement

The Playground requires your AWS credentials file (typically `~/.aws/credentials`) to contain at least two profiles:

- **dev** — used for metadata sync and DEV source database access.
- **uat** — used for target database access when running compares or streaming against UAT.

These profiles must match the names referenced in `TargetAwsSecrets_*` and `MetadataAwsSecrets_*` inside `config/appsettings.json` so the Playground can resolve AWS Secrets Manager entries correctly. Ensure each profile includes valid `aws_access_key_id`, `aws_secret_access_key`, and (if applicable) `region`.

### 1) Bootstrap local Postgres and schema

From `Agpme.Bbg.Playground.Bootstrap`:
```bash
# run from project folder
dotnet run -- bootstrap         # up + apply-sql + optional metadata sync
# or granular:
dotnet run -- up                # docker compose up pg
dotnet run -- apply-sql         # apply DBScripts/*.sql
dotnet run -- sync-metadata     # copy metadata tables from DEV/UAT/PROD
```
This uses `Docker/docker-compose.local.yml`, waits for PG readiness, prints server identity, and applies all `DBScripts/*.sql`. Metadata sync pulls source CS from AWS secrets (`MetadataAwsSecrets_*`). 

### 2) Run the backends

- **Simulator API** (streams JSON): `Agpme.Bbg.Playground.Simulator.Api`
- **Subscriptions API** (workers + compare + swagger): `Agpme.Bbg.Playground.Subscriptions.Api`

Run both (F5/VS or `dotnet run`) — endpoints and options are read from `config/appsettings.json` linked into each project output. 

### 3) Admin console (optional but recommended)

Run `Agpme.Bbg.Playground.Admin` to access the Blazor UI:
- **Subscriptions**: start/stop, as‑of, health, metrics (initial/intraday counts, heartbeats).
- **Settings**: set global `as_of_date` for new sessions; **reset tables**; **resync metadata** from DEV. 
- **Metadata**: grid listing `app_config.bbg_positions_inbound_cols_map` with edit + validate (syntax probe) actions. 
- **Compare**: choose entity, **current vs history**, **One‑Step/Two‑Steps** compare, `UseAllFields`, tolerances; writes **CSV artifacts** if `Always` or `OnFailure`. 

---

## Configuration

All projects link a shared `config/appsettings.json` into their output. Notable settings:

- **Bootstrap**: 
  - `Docker:ComposeFile` → local docker compose path
  - `ConnectionString_Local` → `Host=localhost;Port=6433;...`
  - `DbScripts:Folder` → folder containing `*.sql`
  - `MetadataSync:*` → `Enabled`, `Tables`, `TruncateBeforeCopy`
  - `MetadataAwsSecrets_*` → `{Arn, KeyName, Region, Profile}` to fetch source CS  
  
- **Simulator API**:
  - `TargetEnvironment` + `TargetAwsSecrets_*` → to resolve the **target DB** CS once (lazy) and build a shared `NpgsqlDataSource` for streaming queries.  
  
- **Subscriptions API**:
  - `SimulatorApiServer` → base URL of stream server (e.g., `http://localhost:6066`)
  - `ConnectionString_Local` → local playground database
  - `SubscriptionTargets` → list of `{ entityType, entityName }` to start en masse  
  - Compare service reads **target** CS via `TargetAwsSecrets_*`; dev Swagger enabled.  
  
- **Admin**:
  - `SubscriptionApiServer` → base URL of Subscriptions API
  - `TargetEnvironment` → UI label (UAT/PROD) on the Compare page  
  

---

## Detailed usage

### Start a single subscription (API)
```http
POST /client/subscriptions/start
{ "entityType": "groups", "entityName": "MyPortfolio" }
```
Returns a `SubscriptionStatus` with metrics. Similar `stop`, `start-all`, `list`, and `status/{type}/{name}` endpoints exist. 

### Set the effective as‑of date
```http
POST /client/settings/as-of-date
{ "as_of_date": "2026-02-01" }   # null/empty = today
```
Used by the consumer when building the stream URI (`.../subscriptions?as_of_date=...`). 

### Reset local tables (dangerous)
```http
POST /client/admin/reset-positions
```
Truncates `app_data.bbg_positions_inbound` and `app_data.bbg_positions` (restart identity). 

### Resync or edit inbound metadata
- `GET /client/metadata/inbound-cols-map` → rows  
- `PUT /client/metadata/inbound-cols-map/{mapId}` → update row  
- `POST /client/metadata/inbound-cols-map/resync` → copy from DEV via AWS Secret  
- `POST /client/metadata/inbound-cols-map/validate` → **syntax‑only** probes for `transform_expr`/`update_set_expr`  
  All wired via the shared Postgres helper. 

### Compare local vs target (current/history)
```http
POST /client/compare/run
{
  "entityType":"groups",
  "entityName":"MyPortfolio",
  "asOfDate":"2026-02-01",
  "expectedSource":"current",      // or "history"
  "oneStepMode":true,
  "useAllFields":true,
  "stringCaseInsensitive":false,
  "numericTolerance":0.00001,
  "savePolicy":"OnFailure"         // Always | OnFailure | Never
}
```
Response reports success + counts and **writes CSVs** into `artifacts/compare/...` per the save policy. You can also specify **Phase1/Phase2/Excluded** fields for granular checks. 

---

## Implementation notes (highlights)

- **Streaming**
  - Endpoint: `POST /trading-solutions/positions/{entityType}/{entityName}/subscriptions?as_of_date=YYYY-MM-DD&chunk=true`  
  - Emits complete JSON objects with occasional **split‑chunk** writes to simulate partial buffers; client reassembles via a **brace‑depth** extractor. 
- **Persist & Upsert**
  - **Initial**: batch COPY of JSON‑mapped columns + `loader` fields (as‑of, entity, process, msg ids), then `CALL app_data.bbg_upsert_positions_from_inbound(...)`.  
  - **Intraday**: single‑row COPY + upsert per JSON.  
  - **Zero filters**: Initial filters out `POSITION_WITHOUT_PENDING`==0; intraday keeps zeros. 
- **Logging & Artifacts**
  - Per‑subscription logs under `Logs/subscriptions/{entityType}_{entityName}.log` (daily roll, 7 retained).  
  - Compare exports `phase1/expected.csv`, `phase1/actual.csv` (and phase2 if enabled). 

---

## Troubleshooting

- **Local DB offline**: Admin shows status; Subscriptions guard **start** if DB is not online. Ensure Docker is running; or run `dotnet run -- up` in Bootstrap. 
- **No EXPECTED rows** in compare: check target env, `as_of_date`, and whether you chose **current vs history** correctly. 
- **No ACTUAL rows**: you likely didn’t run a subscription for that `as_of_date`/entity yet. Start from Admin or `start` API. 
- **Metadata rejects expressions**: use Admin → **Metadata** → **Validate** to run a syntax probe (no data read). Fix invalid SQL or placeholders. 

---

## License
Internal / MIT‑style (match your organization’s policy).
