
# Stream Playground Server — Quick Test Guide

This project exposes a streaming endpoint that returns Bloomberg position data using **chunked JSON**.

Because the response is an infinite / long‑running stream, **`curl`** is the recommended test client (Postman does not reliably show partial chunks).

---

## 🚀 Running the Server

From the C# solution root:

```bash
dotnet run --project Agpme.Bbg.Playground.Simulator.Api
```

By default it listens on:

```
http://localhost:6066
```

(or whatever port is configured in `launchSettings.json`)

---

## 📡 Streaming API Test (Recommended)

Use `curl` with **no output buffering**:

```bash
curl -N -X POST "http://localhost:6066/trading-solutions/positions/accounts/TEST5/subscriptions?as_of_date=2026-02-20&chunk=true"
```

```bash
curl -N -X POST "http://localhost:6066/trading-solutions/positions/groups/FIAACCT/subscriptions?as_of_date=2026-02-20&chunk=true"
```

- `-N` disables curl’s buffering, letting chunks appear as soon as the server flushes them.
- The server will send:
  - Initial paint rows (0 or more JSON objects)
  - Then `{}` heartbeats or real-time update chunks

You should see output like:

```
{}
{"streamOrder":123,"json":"..."}
{}
{"streamOrder":124,"json":"..."}
```

---

## 🛠 Verbose Mode (Debug Streaming)

```bash
curl -N -v -X POST "http://localhost:6066/trading-solutions/positions/accounts/TEST5/subscriptions?as_of_date=2026-02-20&chunk=true"
```

Shows headers, connection details, and flush timing.

---
