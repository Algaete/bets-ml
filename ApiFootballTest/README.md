# API-Football MatchHistory probe

Isolated .NET 9 console test for measuring API-Football v3 coverage before implementing any database integration.

The probe does not insert data, does not reference the main solution and never writes the API key to disk or console output.

## Run

macOS/Linux:

```bash
export API_FOOTBALL_KEY="YOUR_API_KEY"
dotnet run --project ApiFootballTest/ApiFootballTest.csproj
```

PowerShell:

```powershell
$env:API_FOOTBALL_KEY="YOUR_API_KEY"
dotnet run --project ApiFootballTest/ApiFootballTest.csproj
```

Optional settings:

```bash
export API_FOOTBALL_DELAY_MS=6500
export API_FOOTBALL_SAMPLE_SIZE=10
```

`API_FOOTBALL_DELAY_MS` defaults to 6500 to remain below the free-plan rate limit observed during the probe. `API_FOOTBALL_SAMPLE_SIZE` accepts 1 through 10.
