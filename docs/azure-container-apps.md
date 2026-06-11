# Azure Container Apps Deployment Notes

## Apps

Deploy two container apps:

- `corners-web`: public ingress, points to `CornersPrediction.Web/Dockerfile`.
- `corners-api`: internal ingress only, points to `CornersPredictionApi/Dockerfile`.

The Web app calls the API server-side using `BACKEND_API_BASE_URL`.

## Required Secrets / Environment Variables

Set these as Container Apps secrets where possible:

- `AZURE_AD_TENANT_ID`
- `AZURE_AD_CLIENT_ID`
- `AZURE_AD_CLIENT_SECRET`
- `AZURE_SQL_CONNECTION_STRING`
- `INTERNAL_API_KEY`

Set these as environment variables:

- Web:
  - `ASPNETCORE_ENVIRONMENT=Production`
  - `AZURE_AD_INSTANCE=https://login.microsoftonline.com/`
  - `AZURE_AD_CALLBACK_PATH=/signin-oidc`
  - `BACKEND_API_BASE_URL=http://<internal-api-fqdn-or-service>`
  - `BACKEND_API_INTERNAL_KEY=<secretref:INTERNAL_API_KEY>`
- API:
  - `ASPNETCORE_ENVIRONMENT=Production`
  - `AZURE_SQL_CONNECTION_STRING=<secretref:AZURE_SQL_CONNECTION_STRING>`
  - `INTERNAL_API_KEY=<secretref:INTERNAL_API_KEY>`
  - `SWAGGER_ENABLED=false`

## Azure SQL

The application reads the database connection from `AZURE_SQL_CONNECTION_STRING`
or `ConnectionStrings__DefaultConnection`.

Recommended managed identity connection string:

```text
Server=tcp:<server>.database.windows.net,1433;Initial Catalog=<database>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Authentication=Active Directory Managed Identity;
```

Then grant the API container app managed identity access in Azure SQL.

## Microsoft Entra Redirect URI

Configure the Web app redirect URI in Entra:

```text
https://<web-container-app-domain>/signin-oidc
```

## Security Defaults

- API requires `INTERNAL_API_KEY` in Production.
- API Swagger is disabled by default.
- Web authentication cookies are HTTPS-only in Production.
- Forwarded headers are enabled for Azure Container Apps reverse proxy.
- Betting data is isolated by platform user id via `X-User-Id` sent only from Web to API.
