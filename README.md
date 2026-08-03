# ServerMetricsApi

A minimal C#/ASP.NET backend that exposes the existing `memory_log` and
`server_events` tables (from the AzerothCore server infrastructure project)
through a REST API.

## Endpoints

| Endpoint | Description |
|---|---|
| `GET /api/status` | Current snapshot (latest measurement + online/offline) |
| `GET /api/metrics?hours=24` | Time series for the last `hours` hours |
| `GET /api/events?days=7` | Events from the last `days` days |

## Setup

Follow these steps **in order** — creating the database user before the
first run avoids an "Access denied" error on startup.

### 1. Install the .NET SDK

This project targets **.NET 10**. On the server:
```bash
sudo apt-get update
sudo apt-get install -y dotnet-sdk-10.0
```

### 2. Copy the project to the server

Transfer the project folder to your VM (e.g. via `scp`), then move it to
wherever it should live permanently (e.g. `/root/backend` for local testing,
or `/opt/server-metrics-api` for the systemd deployment further below).

### 3. Create a read-only database user

Before touching the app config, create a MySQL user with `SELECT`-only
access to the two relevant tables. Replace `your-secure-password` with an
actual strong, random password — **not** a placeholder like `CHANGE_ME`,
since that would leave the database wide open to anyone who has seen this
README.

```sql
CREATE USER 'metrics_reader'@'localhost' IDENTIFIED BY 'your-secure-password';
GRANT SELECT ON acore_monitoring.memory_log TO 'metrics_reader'@'localhost';
GRANT SELECT ON acore_monitoring.server_events TO 'metrics_reader'@'localhost';
FLUSH PRIVILEGES;
```

Adjust `acore_monitoring` to match your actual database name if different.

You can verify the login works before moving on:
```bash
mysql -u metrics_reader -p -h localhost
```

### 4. Configure the connection string

Copy the example config and fill in your real password:
```bash
cp appsettings.json.example appsettings.json
nano appsettings.json
```
Make sure `Database=` matches the database name you granted access to in
step 3, and `Password=` matches the password you just set.

`appsettings.json` is listed in `.gitignore` and will never be committed —
only `appsettings.json.example` (with placeholder values) is meant to go
into the repository.

### 5. Restore and run

```bash
dotnet restore
dotnet run
```

You should see:
```
Now listening on: http://localhost:5000
```

### 6. Test it

In a separate terminal:
```bash
curl http://localhost:5000/api/status
curl "http://localhost:5000/api/metrics?hours=24"
curl "http://localhost:5000/api/events?days=7"
```

## Deploying as a systemd service

Once step 6 works, publish a release build and run it as a proper service
instead of a foreground `dotnet run`:

```bash
dotnet publish -c Release -o /opt/server-metrics-api
```

```ini
# /etc/systemd/system/server-metrics-api.service
[Unit]
Description=Server Metrics API
After=network.target mysql.service

[Service]
WorkingDirectory=/opt/server-metrics-api
ExecStart=/usr/bin/dotnet /opt/server-metrics-api/ServerMetricsApi.dll
Restart=on-failure
User=www-data
Environment=ASPNETCORE_URLS=http://localhost:5000

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now server-metrics-api
```

Make sure `appsettings.json` (with the real password) is also copied into
`/opt/server-metrics-api` — `dotnet publish` does not carry over a file
that's excluded via `.gitignore` if you're deploying straight from git;
copy it manually or manage it separately from version control.

Afterwards, expose port 5000 through your existing tunnel setup the same
way you did for the game server, so the API is reachable from outside the
VM.

## Troubleshooting notes from initial setup

- **`You must install or update .NET to run this application`**: the
  installed SDK/runtime version doesn't match the project's
  `TargetFramework` in `ServerMetricsApi.csproj`. Either install the
  matching runtime or update `TargetFramework` to the version you have
  installed.
- **`Access denied for user ... (using password: YES)`**: usually means
  the database user hasn't been created yet, or the password in
  `appsettings.json` doesn't match. See step 3–4 above.
- **`Access denied for user ... to database 'X'`**: the database name in
  `appsettings.json` doesn't match the database the user was actually
  granted access to. Double-check both against each other.
