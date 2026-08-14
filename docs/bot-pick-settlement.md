# Liquidación automática de Bot Picks

## Flujo

```text
Bot A/B/C -> AutomatedCornerBetSelections (Pending)
API-Football -> MatchHistory (resultado + FixtureStatus)
ApiFootballSyncService -> IAutomatedBotPickSettlementUseCase
-> IAutomatedBotPickSettlementRepository (Dapper)
-> sp_ApplyAutomatedBotPickSettlements
-> Won / Lost / Push / Pending
```

No se creó una tabla paralela: `dbo.AutomatedCornerBetSelections` ya era la
persistencia real de todos los Bot Picks. Se amplió con las claves
`ApiFootballFixtureId` y `MatchHistoryId`, más campos genéricos de resultado y
auditoría.

## Fuente de verdad y matching

La fuente de verdad es `dbo.MatchHistory`. Los candidatos se obtienen por:

1. `MatchHistoryId` ya persistido.
2. `ApiFootballFixtureId` ya persistido.
3. Fallback para picks históricos por fecha UTC y nombres normalizados.

El fallback convierte la hora local de Santiago a su fecha UTC y tolera un día
de diferencia. Esto repara picks antiguos enlazados a una fila preliminar de
`MatchHistory` fechada localmente cuando API-Football insertó después la fila
final en fecha UTC. Un candidato `FT`, `AET` o `PEN` tiene prioridad sobre un
vínculo preliminar con `FixtureStatus = NULL`; solo se acepta un mejor candidato
único y sus claves estables se guardan inmediatamente en el pick.

Los alias de equipos se normalizan en la ingesta y en el catálogo central
`TeamNameAlias`; por ejemplo, `Cape Verde Islands` se unifica como `Cabo Verde`.

Los picks nuevos intentan guardar `ApiFootballFixtureId` desde
`dbo.PartidosProximos.ExternalFixtureId` al momento de ser generados.

## Reglas

- `FT`, `AET` y `PEN` se consideran estados finales explícitos.
- Un histórico con `FixtureStatus = NULL` no se liquida automáticamente, aunque
  tenga marcador y estadísticas. Permanece `Pending` hasta enlazarse con una
  fuente final verificable; una captura histórica puede ser parcial o antigua.
- `NS`, partidos en vivo, postponed, suspended y cancelled continúan `Pending`.
  `Void` se mantiene como decisión manual hasta definir una política de
  cancelaciones con suficiente certeza.
- Un valor estadístico `0` es válido.
- Un valor `NULL` significa que falta información y mantiene el pick `Pending`.
- Líneas `.25` y `.75` se dividen en dos líneas asiáticas; el factor puede ser
  `0.5` o `-0.5`.
- Una igualdad sobre línea entera produce `Push`, P/L `0`.
- El SP actualiza picks `Pending`, por lo que el proceso es idempotente y seguro
  ante ejecuciones concurrentes. También permite
  reconciliar exclusivamente liquidaciones automáticas cuando
  `MatchHistory.ApiFootballUpdatedAtUtc > SettledAtUtc`; exige el timestamp de
  liquidación esperado para no sobrescribir un cambio concurrente o manual.

## Mercados soportados

- `TotalGoals`, `HomeTeamGoals`, `AwayTeamGoals`
- `TotalCorners`, `HomeTeamCorners`, `AwayTeamCorners`
- `TotalShots`, `HomeTeamShots`, `AwayTeamShots`
- `TotalShotsOnGoal`, `HomeTeamShotsOnGoal`, `AwayTeamShotsOnGoal`

La selección se obtiene de `SelectedSide` (`Over`/`Under`) y la línea de
`LineValue`; no se interpreta una descripción libre del pick.

## Endpoint

```http
POST /api/automated-corners/settle
Content-Type: application/json

{
  "MatchDateTo": "2026-08-10",
  "DryRun": true,
  "MaxRows": 5000,
  "BotKey": "C2026",
  "MarketFamily": "corners"
}
```

`DryRun=true` calcula y explica cada decisión sin modificar datos. La respuesta
incluye revisados, liquidados, pendientes, Won/Lost/Push y la razón por fila.
`BotKey` acepta `A`, `B`, `C`/`C2026`, `Legacy` o la clave de un bot futuro;
`MarketFamily` acepta `corners`, `goals`, `shots` o `sog`. Ambos son opcionales
para conservar la liquidación global que se ejecuta tras sincronizar API-Football.
Los filtros se aplican antes de `MaxRows`, por lo que el botón de la web procesa
exclusivamente la pestaña de bot y el mercado visibles.

## Automatización

`ApiFootballSyncService` ejecuta el caso de uso una vez después de una
sincronización real que insertó o actualizó resultados. En `bulk-sync` no se
liquida por cada competición: se espera al fin del lote y se ejecuta una sola
vez. Un fallo de liquidación no revierte los resultados ya guardados; queda en
log y puede reintentarse mediante el endpoint.

Las estadísticas de un fixture se solicitan sin caché de proceso porque el
proveedor puede corregirlas después de marcarlo `FT`. Si una resincronización
cambia la fila fuente después de una liquidación automática, el mismo endpoint
la recalcula y deja la razón `Reconciliado por actualización de MatchHistory`.

## Auditoría

Campos principales:

- `SettlementActualValue` y `SettlementFactor`
- `SettlementReason` y `SettlementSource`
- `SettlementMatchStatus`
- `SettlementSnapshotJson`
- `LastSettlementCheckReason` y `LastSettlementCheckAtUtc`
- `SettledAtUtc`, `ProfitLoss`, `YieldPct`

Así se puede distinguir un pick perdido de uno que sigue pendiente porque el
partido no terminó, no existe en `MatchHistory`, el enlace es ambiguo o falta la
estadística del mercado.

## Pruebas

```bash
dotnet run --project tests/BotPickSettlement.Tests/BotPickSettlement.Tests.csproj
```

El ejecutable cubre valores cero, `NULL`, los 12 mercados/alcances, ejemplos de
goles/córners/tiros al arco, líneas enteras, `.25`, `.75`, estados de fixture e
idempotencia.
