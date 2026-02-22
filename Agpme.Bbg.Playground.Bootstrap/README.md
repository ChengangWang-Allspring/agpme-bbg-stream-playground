# Agpme.Bbg.Playground.Bootstrap

A lightweight developer utility that prepares a **local PostgreSQL instance** for the  
**AGPME Bloomberg Stream Playground** by:

1. Starting a dedicated Postgres container via **Docker Compose**  
2. Waiting for the container to become healthy  
3. Applying all database **DDL scripts** from `db/DBScripts`  
4. Optionally synchronizing **metadata tables** from **DEV** using AWS Secrets Manager


## ⚙️ Prerequisites

- **Docker Desktop** or other Docker engine  
- .NET **8.0** SDK  
- Local AWS credentials profile (e.g., `dev`)  
- Shared project’s **AwsSecretHelper** works correctly in your environment

---

## 🧰 Configuration (`appsettings.json`)

Only **one configuration file** is used.

Key sections:

```jsonc
"LocalPlaygroundDb": {
  "ConnectionString": "Host=localhost;Port=6433;Database=agpme_playground;Username=postgres;Password=postgres"
},

"MetadataSync": {
  "Enabled": true,
  "TruncateBeforeCopy": true,
  "Tables": [
    "app_config.bbg_positions_inbound_cols_map",
  ]
},

"MetadataAwsSecrets": {
  "UseAwsSecret": true,
  "Arn": "arn:aws:secretsmanager:us-east-1:XXXXXXXXXXXX:secret:dev/agpme/api-XXXXXX",
  "KeyName": "AgpmeReadOnlyConnectionString",
  "Region": "us-east-1",
  "Profile": "dev"
}
```

Ensure:

- `Port` matches the Docker Compose host port (**6433**)
- Database matches Compose’s `POSTGRES_DB` (**agpme_playground**)
- `MetadataAwsSecrets` uses your correct Secret ARN & key name for `dev` profile

---

## ▶️ Commands

Run all commands from the **DevBootstrap** project folder:

### 📌 Bootstrap entire environment  
(Up + Schema apply + Metadata sync)

```bash
dotnet run -- bootstrap
```

### 📌 Start database only

```bash
dotnet run -- up
```

### 📌 Apply SQL scripts

```bash
dotnet run -- apply-sql
```

### 📌 Sync metadata tables from DEV

```bash
dotnet run -- sync-metadata
```

### 📌 Reset DB (down -v + up + apply-sql)

```bash
dotnet run -- reset
```

### 📌 Shutdown DB container

```bash
dotnet run -- down
```

---

## 🐳 Docker Compose

The included compose file starts an isolated Postgres instance:

- Host Port: **6433**
- DB Name: **agpme_playground**
- Data volume: **agpme_pg_playground_data** (project‑specific, safe)
- Health check: `pg_isready -U postgres -d agpme_playground || exit 1`

You can inspect the running container:

```bash
docker ps
docker logs agpme_pg_playground
```
Manuall launch docker compose
```bash
docker compose -f Docker/docker-compose.local.yml up -d
```
Manually stop docker 
```bash
docker compose -f Docker/docker-compose.local.yml down
```


---

## ⚠️ Important Safety Notes

- Do **NOT** reuse any generic Docker volume like `pgdata`
- The included compose uses a **unique** volume:  
  `agpme_pg_playground_data`
- Port `6433` prevents clash with system Postgres (5432)
- Metadata sync **WILL TRUNCATE** destination tables (safe for local dev)

---

## 🧹 Resetting the Local Database

To wipe the Docker volume and rebuild from scratch:

```bash
dotnet run -- reset
```

Equivalent to:

```bash
docker compose down -v
docker compose up -d
dotnet run -- apply-sql
```

---

## ❓ Troubleshooting

### “DB not ready” timeout
- Check:
  ```bash
  docker logs agpme_pg_playground
  ```
- Ensure port **6433** is not used by another service  
- Confirm healthcheck uses `agpme_playground`

### Secret resolution errors
- Verify AWS credentials (`aws sts get-caller-identity`)
- Ensure Secret ARN + KeyName exist
- Ensure Profile is present in `~/.aws/credentials`

### SQL script failures
- Look for the failing script in output  
- Validate the script against the ETL tests’ DDL

---

## 🏁 Summary

DevBootstrap gives you a **single command** to prepare a clean local Postgres instance  
identical to the ETL test environment, including schema and metadata.

Use:

```bash
dotnet run -- bootstrap
```

to begin development immediately with a fully structured database.
