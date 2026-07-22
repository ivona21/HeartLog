# OpenAPI Generation Workflow

The frontend contract is generated from the backend Swagger endpoint and committed as:

```text
openapi/heartlog.openapi.json
```

## Generate Locally

1. Start the backend:

   ```bash
   dotnet run --project HeartLog.Api --launch-profile http
   ```

2. In another terminal, export the OpenAPI spec:

   ```bash
   bash scripts/export-openapi.sh
   ```

3. Review the generated file:

   ```bash
   git diff -- openapi/heartlog.openapi.json
   ```

Commit `openapi/heartlog.openapi.json` when the API contract intentionally changes.

## Alternate Backend URL

By default, the export script reads:

```text
http://localhost:5048/swagger/v1/swagger.json
```

Use `HEARTLOG_OPENAPI_URL` when the backend is running somewhere else:

```bash
HEARTLOG_OPENAPI_URL=http://localhost/swagger/v1/swagger.json bash scripts/export-openapi.sh
```

## Verify

The export script fails if:

- the backend is not reachable
- `/swagger/v1/swagger.json` returns a non-success status
- the response is not valid JSON

Manual verification:

```bash
python3 -m json.tool openapi/heartlog.openapi.json >/dev/null
```

The generated file should change only when the public API contract changes.
