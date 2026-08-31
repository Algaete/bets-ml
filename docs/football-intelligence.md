# Inteligencia pre-partido para todos los bots activos

## Resultado funcional

Los bots activos A, C, D, E, F, G y H comparten una capa contextual pre-partido. Cada motor conserva su modelo y aplica la misma señal después de su probabilidad base/calibrada y antes de volver a comprobar edge, EV y aprobación. B permanece retirado y sus picks históricos solo muestran la evidencia como referencia.

La regla de seguridad es estricta:

- sin `ApiFootballFixtureId`, no se busca snapshot y el ajuste es `0`;
- sin snapshot, el ajuste es `0`;
- sin hechos accionables, fuentes suficientes o confianza mínima, el ajuste es `0`;
- un snapshot vencido o posterior al cutoff produce ajuste `0`;
- un hecho válido pero sin impacto medible produce ajuste `0`;
- la ausencia de inteligencia nunca aprueba ni rechaza un pick por sí misma.

El ajuste válido queda limitado por defecto a `±4` puntos porcentuales. El motor conserva en `FeatureSnapshotJson` la probabilidad anterior, el ajuste, los ids de snapshot, los estados de evidencia y las razones.

## Fuentes y procesamiento

1. API-Football aporta fixture, plantel, lesiones/sanciones y alineación cuando existe.
2. Un proveedor opcional de búsqueda obtiene noticias públicas. El adaptador incluido usa Brave Search.
3. El extractor HTML elimina navegación, scripts y contenido no relevante; limita tamaño y rechaza hosts locales/privados.
4. El extractor determinístico acepta solo frases explícitas asociadas a jugadores conocidos. Por ejemplo, “ruled out” es baja confirmada, pero “returned to training” no se convierte automáticamente en disponible.
5. La resolución cruza equipo y jugador con el fixture y plantel de API-Football.
6. La consolidación conserva contradicciones, escoge un estado vigente y da prioridad final a una alineación oficial.
7. Se construye un snapshot por equipo y cutoff.

Los documentos se versionan por URL canónica + hash de contenido + equipo. Una actualización no reescribe la evidencia que existía en un cutoff anterior.

## Persistencia

Scripts idempotentes:

- `CornersPredictionApi/SqlScripts/FootballIntelligence/001_CreateNewsTables.sql`
- `CornersPredictionApi/SqlScripts/FootballIntelligence/002_CreateIndexes.sql`
- `CornersPredictionApi/SqlScripts/FootballIntelligence/003_CreateSourceConfig.sql`
- `CornersPredictionApi/SqlScripts/FootballIntelligence/004_AddFactIdempotency.sql`

Las tablas principales son:

- `FootballNewsDocument`
- `FootballNewsFact`
- `FootballNewsFactResolution`
- `MatchTeamIntelligenceSnapshot`
- `MatchIntelligenceRun`
- `FootballTeamAlias` y `FootballPlayerAlias`
- `FootballSourceConfiguration`
- tablas preparadas para importancia de jugador y validación histórica de fuentes

El inicializador ejecuta los cuatro scripts en forma idempotente. `FactHash` evita que una reejecución vuelva a insertar el mismo hecho del mismo documento y versión de extracción. En producción, la migración debe aplicarse antes de habilitar el worker.

## Configuración

Variables principales:

```dotenv
API_FOOTBALL_KEY=
FOOTBALL_INTELLIGENCE_ENABLED=false
FOOTBALL_INTELLIGENCE_WORKER_ENABLED=false
FOOTBALL_INTELLIGENCE_WORKER_POLL_MINUTES=5
FOOTBALL_NEWS_SEARCH_PROVIDER=None
BRAVE_SEARCH_API_KEY=
FOOTBALL_NEWS_OPENAI_ENABLED=false
OPENAI_API_KEY=
FOOTBALL_NEWS_OPENAI_MODEL=
```

Para usar Brave:

```dotenv
FOOTBALL_NEWS_SEARCH_PROVIDER=Brave
BRAVE_SEARCH_API_KEY=<secreto>
```

Sin Brave, el módulo puede trabajar solo con evidencia estructurada de API-Football. Si tampoco hay evidencia estructurada accionable, todos los bots permanecen neutrales.

La sección `FootballIntelligence` de `appsettings.json` configura horizonte, concurrencia, cutoffs T-72h/T-24h/T-6h/T-90m/T-40m/T-10m, límites de consultas y artículos, recencia y pesos.

`INewsFactExtractor` elige el extractor determinístico `RuleBasedNewsFactExtractor` cuando OpenAI está deshabilitado. Para extracción semántica estructurada, activa `FOOTBALL_NEWS_OPENAI_ENABLED=true` y configura `OPENAI_API_KEY` y `FOOTBALL_NEWS_OPENAI_MODEL`. `OpenAiNewsFactExtractor` usa JSON Schema estricto, DTOs fuertes, timeout y reintentos acotados para 429, 5xx, timeout o salida estructurada inválida. El modelo y la versión del prompt quedan auditados; una falla de un artículo no detiene el resto del fixture.

## Endpoints internos

Ejecutar o refrescar un fixture antes del kickoff:

```http
POST /api/intelligence/fixtures/{fixtureId}/run
Content-Type: application/json

{
  "cutoffUtc": "2026-08-18T18:00:00Z",
  "forceRefresh": false
}
```

Consultar el último resultado conocido antes de un cutoff:

```http
GET /api/intelligence/fixtures/{fixtureId}?cutoffUtc=2026-08-18T18:00:00Z
GET /api/intelligence/fixtures/{fixtureId}/latest?cutoffUtc=2026-08-18T18:00:00Z
```

Auditoría:

```http
GET /api/intelligence/fixtures/{fixtureId}/snapshots
GET /api/intelligence/fixtures/{fixtureId}/documents?cutoffUtc=...
GET /api/intelligence/fixtures/{fixtureId}/facts?cutoffUtc=...
```

El endpoint de documentos devuelve metadatos y longitud, no el cuerpo completo de la noticia.

## Worker

`UpcomingFixtureIntelligenceWorker` lee próximos partidos API-Football desde `PartidosProximos`, detecta el siguiente cutoff vencido y crea un snapshot. Está deshabilitado por defecto.

Para habilitarlo, primero debe existir la migración SQL y luego se configuran:

```dotenv
FOOTBALL_INTELLIGENCE_ENABLED=true
FOOTBALL_INTELLIGENCE_WORKER_ENABLED=true
```

Cada fixture se procesa una vez por ventana alcanzada. La concurrencia está limitada y un fallo de un partido no detiene los demás.

## Data leakage y backtest

- Todo documento exige `FirstSeenAtUtc <= CutoffAtUtc`.
- Si existe fecha de publicación, también exige `PublishedAtUtc <= CutoffAtUtc`.
- El motor ignora snapshots con cutoff posterior al partido.
- Una ejecución manual no acepta cutoffs futuros.
- Un backtest histórico no hace búsquedas web actuales ni incorpora lesiones actuales.
- No se debe atribuir a T-24h una noticia recuperada por primera vez hoy.

Por esta razón, la señal empieza a producir una comparación real a medida que el worker acumula snapshots prospectivos. Para fechas antiguas sin evidencia guardada, los picks se muestran como referencia y no se reescriben.

## Pruebas

El runner `tests/BotPickSettlement.Tests` cubre:

- neutralidad exacta sin snapshot, sin hechos, baja confianza, vencimiento y cutoff futuro;
- ajuste válido, acotado y con dirección Over/Under;
- invariancia numérica de E y F sin evidencia;
- frases explícitas y negaciones;
- consultas multilingües balanceadas;
- deduplicación por jugador y precedencia de alineación oficial;
- toda la suite previa de liquidación y bots activos.
