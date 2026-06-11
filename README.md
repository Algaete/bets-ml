# bets-ml

ASP.NET Core MVC/API application for football betting analytics, ML predictions, bankroll tracking, user administration, and upcoming match workflows.

## Local configuration

Copy `.env.example` to `.env` and fill the real values locally. Do not commit `.env`.

Key runtime areas:

- `CornersPrediction.Web`: MVC/Razor web app.
- `CornersPredictionApi`: backend API.
- `newModelsML`: active Python model artifacts and `active_models.json`.
- `docs/azure-container-apps.md`: Azure Container Apps deployment notes.

## Build

```bash
dotnet build CornersPrediction.sln
```
