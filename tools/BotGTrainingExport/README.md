# Bot G training export

Internal, read-only exporter for `dbo.sp_GetBotG2026TrainingExport`. It writes an
immutable JSONL candidate universe plus a SHA-256 manifest. It never trains a
model, changes SQL, writes `active.json`, or prints a connection string.

The tool accepts the connection only through `BOT_G_SQL_CONNECTION_STRING`.
Use a read-only SQL principal where available. Dates require an explicit UTC
offset and `--as-of` makes the export reproducible.

```bash
BOT_G_SQL_CONNECTION_STRING='...' dotnet run \
  --project tools/BotGTrainingExport/BotGTrainingExport.csproj -- \
  --output /secure/path/bot-g-2026-08-31.jsonl \
  --as-of 2026-08-31T23:59:59Z \
  --date-from 2026-01-01T00:00:00Z \
  --date-to 2026-09-01T00:00:00Z
```

The export fails closed when a row lacks the exact v1.1 training contract,
per-market base-model lineage, Football Intelligence version/evidence fields,
or resolved outcome. Old v1.0 rows are rejected; they are never relabeled.
Existing output and manifest files are never overwritten.

Validate the tool without SQL:

```bash
dotnet run --project tools/BotGTrainingExport/BotGTrainingExport.csproj -- --self-test
```

Then run the Python preflight before any training:

```bash
python scripts/train_bot_g.py \
  --input /secure/path/bot-g-2026-08-31.jsonl \
  --preflight-only
```

No automated promotion exists. After a real walk-forward/final-test review,
promotion must be a separately reviewed operation: verify the JSONL SHA against
the manifest, inspect the immutable report, confirm `deployable=true`, confirm
the runtime/config versions match, copy the selected versioned artifact to
`models/bot-g/active.json` in a controlled deployment, restart the API, and only
then consider changing the independent database publication flag. This tool
does none of those steps.
