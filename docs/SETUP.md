Practical steps to get this API running, from a fresh clone to a systemd
service on the VM. For the reasoning behind the code structure, see
[ARCHITECTURE.md](ARCHITECTURE.md) and [DATA-ACCESS.md](DATA-ACCESS.md).

Follow these steps **in order** — creating the database user before the
first run avoids an "Access denied" error on startup, which is the mistake
this order is built to prevent.

## 1. Install the .NET SDK

This project targets **.NET 10**. On the server:
```bash
sudo apt-get update
sudo apt-get install -y dotnet-sdk-10.0
```

## 2. Get the project onto the server

Clone the repository (or transfer the project folder via `scp`), then place
it wherever it should live — e.g. `/root/backend` for local testing, or
`/opt/server-metrics-api` for the systemd deployment further below.

## 3. Create a read-only database user

Before touching the app config, create a MySQL user with `SELECT`-only
access to the two relevant tables. Replace `your-secure-password` with an
actual strong, random password — **not** a placeholder, since a placeholder
committed to this guide would leave the database open to anyone who's read
it.

```sql
CREATE USER 'metrics_reader'@'localhost' IDENTIFIED BY 'your-secure-password';
GRANT SELECT ON acore_monitoring.memory_log TO 'metrics_reader'@'localhost';
GRANT SELECT ON acore_monitoring.server_events TO 'metrics_reader'@'localhost';
FLUSH PRIVILEGES;
```

Adjust `acore_monitoring` to match your actual database name if different.

Verify the login works before moving on:
```bash
mysql -u metrics_reader -p -h localhost
```

### Optional: table and user for the Python analysis tool

If the separate Python analysis tool is also part of your setup, it writes
precomputed metrics into an additional table, read by this API's
`/api/insights` endpoint:

```sql
CREATE TABLE analysis_results (
    id INT AUTO_INCREMENT PRIMARY KEY,
    metric_name VARCHAR(100) NOT NULL,
    value DECIMAL(12,4),
    unit VARCHAR(20),
    computed_at DATETIME NOT NULL,
    details JSON
);
CREATE INDEX idx_metric_computed ON analysis_results (metric_name, computed_at);

CREATE USER 'analysis_writer'@'localhost' IDENTIFIED BY 'another-secure-password';
GRANT SELECT ON acore_monitoring.memory_log TO 'analysis_writer'@'localhost';
GRANT SELECT ON acore_monitoring.server_events TO 'analysis_writer'@'localhost';
GRANT SELECT, INSERT ON acore_monitoring.analysis_results TO 'analysis_writer'@'localhost';
FLUSH PRIVILEGES;
```

If you haven't built the Python tool yet, either run the `CREATE TABLE`
statement anyway (an empty table is fine — `/api/insights` will correctly
return an empty list) or skip this and don't call `/api/insights` yet —
querying a table that doesn't exist throws a database error, not an empty
result.

## 4. Configure the connection string

Copy the example config and fill in your real password:
```bash
cp config/appsettings.json.example config/appsettings.json
nano config/appsettings.json
```
Make sure `Database=` matches the database name granted in step 3, and
`Password=` matches the password just set.

`config/appsettings.json` is listed in `.gitignore` and will never be
committed — only `config/appsettings.json.example` (with placeholder
values) is meant to go into the repository.

## 5. Restore and run

```bash
dotnet restore
dotnet run
```

You should see:
```
Now listening on: http://localhost:5000
```

## 6. Test it

In a separate terminal:
```bash
curl http://localhost:5000/api/status
curl "http://localhost:5000/api/metrics?hours=24"
curl "http://localhost:5000/api/events?days=7"
curl http://localhost:5000/api/insights
```

## 7. Deploying as a systemd service

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

`dotnet publish` does not carry over `config/appsettings.json` if it's
excluded via `.gitignore` and you're deploying straight from git — copy it
manually into `/opt/server-metrics-api/config/`, or manage it separately
from version control.

Afterwards, expose port 5000 through your existing tunnel setup, the same
way as for the game server, so the API is reachable from outside the VM.

## Troubleshooting notes from initial setup

These are real issues encountered while first setting this project up —
kept here in case they resurface.

- **`You must install or update .NET to run this application`**: the
  installed SDK/runtime version doesn't match the project's
  `TargetFramework` in `ServerMetricsApi.csproj`. Either install the
  matching runtime or update `TargetFramework` to the version actually
  installed.
- **`Access denied for user ... (using password: YES)`**: usually means
  the database user hasn't been created yet, or the password in
  `config/appsettings.json` doesn't match. See steps 3–4.
- **`Access denied for user ... to database 'X'`**: the database name in
  `config/appsettings.json` doesn't match the database the user was
  actually granted access to. Double-check both against each other.
- **`Cannot implicitly convert type 'int' to 'short'`**: a `dynamic` Dapper
  query encountered a database column whose actual type didn't match the
  target C# property. See the "Dynamic (untyped) queries" section in
  [DATA-ACCESS.md](DATA-ACCESS.md) for the full explanation and the fix
  that was applied.
