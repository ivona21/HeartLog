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

   The current app startup forces Kestrel to port `80`, so Swagger UI is available at:

   ```text
   http://localhost/index.html
   ```

   The raw OpenAPI JSON is available at:

   ```text
   http://localhost/swagger/v1/swagger.json
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
http://localhost/swagger/v1/swagger.json
```

Use `HEARTLOG_OPENAPI_URL` when the backend is running somewhere else:

```bash
HEARTLOG_OPENAPI_URL=http://example.local/swagger/v1/swagger.json bash scripts/export-openapi.sh
```

## Verify

The export script fails if:

- the backend is not reachable
- `/swagger/v1/swagger.json` returns a non-success status
- the response is not valid JSON

The generated file should change only when the public API contract changes.
