# Arquitectura y acceso a datos del proyecto

## Estado real verificado

La solución principal está compilada para `net8.0` en sus cinco proyectos. No se cambió el framework. El repositorio usa Dapper `2.1.66`, `Microsoft.Data.SqlClient` `5.2.2`, ASP.NET Core MVC/API y, para una base local auxiliar, EF Core SQLite `8.0.11`.

La configuración actual no usa Key Vault ni una clase `SqlConexion`. API y Web cargan `.env`; la API traduce `AZURE_SQL_CONNECTION_STRING` o `SQL_CONNECTION_STRING` a `ConnectionStrings:DefaultConnection`. Los repositorios abren una `SqlConnection` por operación. Esa conexión se devuelve al pool al ejecutar `DisposeAsync`; no significa una conexión física nueva en cada método.

Hay dos persistencias:

- `DefaultConnection`: Azure SQL, con históricos, cuotas, usuarios, apuestas y picks.
- `CornersDatabase`: SQLite local usado por el contexto EF `CornersPredictionDbContext`.

## Jobs de recomendaciones reutilizables

Las corridas largas ya no necesitan mantener un request HTTP abierto. El módulo de jobs sigue este flujo:

```text
POST /api/recommendation-jobs
            │ 202 Accepted + jobId
            v
IRecommendationJobsUseCase
            v
IRecommendationJobRepository
            v
SP de cola/progreso → dbo.AutomatedRecommendationJobs
            ^
            │ claim con lease
RecommendationJobWorker
            v
AutomatedCornersSelectionService → bots solicitados → mercados solicitados → picks Pending
```

Propiedades importantes:

- El job guarda rango, modo, `BotKeys`, `MarketFamilies`, tamaño y siguiente lote.
- Un lease evita que dos instancias procesen el mismo lote al mismo tiempo.
- Cada lote confirma sus contadores en SQL; un reinicio continúa desde `NextBatchNumber`.
- Un error transitorio reintenta con backoff; al superar `MaxAttempts` queda `Failed` con `LastError`.
- Solicitudes activas idénticas reutilizan el mismo job mediante un hash idempotente.
- `HistoricalBackfill` reconstruye recomendaciones antiguas; `Live` respeta el corte de partidos futuros.
- La recurrencia configurable en `RecommendationJobs:Recurring` ejecuta Bot C cada seis horas sobre los próximos siete días.

Endpoints:

```http
POST   /api/recommendation-jobs
GET    /api/recommendation-jobs?take=50
GET    /api/recommendation-jobs/{jobId}
DELETE /api/recommendation-jobs/{jobId}
GET    /api/recommendation-jobs/capabilities
```

Desde la Web, un administrador entra en **Bots y procesos** (`/BotAutomation`). Allí puede crear el job,
elegir rango/modo/bots/mercados, cancelarlo y seguir `ProcessedBatches`, picks, inserts, updates y errores.
La pantalla consulta el estado cada ocho segundos; cerrar el navegador no detiene el worker.

Ejemplo para cualquier rango y las cuatro familias actuales:

```bash
curl -X POST http://localhost:5070/api/recommendation-jobs \
  -H 'Content-Type: application/json' \
  -H 'X-Internal-Api-Key: ...' \
  --data '{
    "name":"Bot C histórico",
    "dateFrom":"2026-06-19",
    "dateTo":"2026-08-10",
    "mode":"HistoricalBackfill",
    "botKeys":["C2026"],
    "marketFamilies":["CORNERS","GOALS","SHOTS","SOG"],
    "batchSize":25
  }'
```

## Mantenedor de bots

El catálogo vive en `dbo.AutomatedBotDefinitions` y no es solo metadata: el motor lee la definición al
comenzar cada lote y aplica sus mercados y overrides. Los SP son:

- `dbo.sp_GetAutomatedBotDefinitions`
- `dbo.sp_UpsertAutomatedBotDefinition`
- `dbo.sp_DeleteAutomatedBotDefinition`

Endpoints:

```http
GET    /api/recommendation-bots
GET    /api/recommendation-bots/{botKey}
POST   /api/recommendation-bots
PUT    /api/recommendation-bots/{botKey}
DELETE /api/recommendation-bots/{botKey}
```

Una definición configura `BaseStrategy`, estado, familias de mercado, edge/EV mínimos, distancia a línea,
diferencia máxima con el contexto, desacuerdo entre modelos, cuota mínima, lift sobre probabilidad implícita
y multiplicador de stake. Un valor nulo hereda el default de la estrategia base.

Las bases ejecutables actuales son `LEGACY_A`, `LEGACY_B` y `MODELS_2026`. Los bots integrados se pueden editar
o deshabilitar, pero no eliminar. Un bot personalizado se puede crear, clonar y eliminar; sus picks históricos
no se borran. Para introducir una fuente o algoritmo nuevo (lesiones, clima, noticias, etc.) primero se implementa
una nueva estrategia de código y luego se expone como opción del catálogo.

### Alineación con la forma de trabajo indicada

| Patrón indicado | Estado en este proyecto |
|---|---|
| Contratos en una librería central | Alineado: interfaces, comandos y casos de uso viven en `Application`; reglas puras en `Domain`. |
| Repositorios Dapper separados | Alineado: implementaciones SQL viven en `Infrastructure/SqlServer`. |
| MVC y API consumen por DI | Alineado: ambos usan interfaces/clientes registrados en `Program.cs`/extensiones de DI. |
| SP como contrato SQL | Alineado en selecciones, catálogo de bots y jobs; algunos módulos legacy de `Robot` aún tienen SQL directo. |
| Conexión centralizada estilo `SqlConexion` | Parcial: cada repositorio recibe `DefaultConnection`; conviene introducir `ISqlConnectionFactory` gradualmente. |
| Key Vault como proveedor de secretos | No implementado: hoy se usa `.env`/variables de entorno. Puede agregarse sin cambiar repositorios. |
| Procesos largos depurables y reanudables | Alineado con `AutomatedRecommendationJobs`, estados, leases, reintentos y `LastError`. |

Para un bot futuro basado en lesiones, ranking, clima, consenso de casas u otros factores, el job y sus SP no cambian. Se agrega una nueva estrategia al motor, se incorpora a `RecommendationBotBaseStrategies` y el usuario crea sus variantes desde el mantenedor. El siguiente paso de evolución recomendado es extraer la evaluación actual a implementaciones de `IRecommendationStrategy`, dejando `AutomatedCornersSelectionService` únicamente como orquestador.

## 1. Mapa de arquitectura

```text
CornersPrediction.sln
├── CornersPrediction.Domain
│   ├── Admin, Betting, MatchHistory, Predictions, Teams
│   └── Entidades, value objects y reglas sin dependencia de infraestructura
├── CornersPrediction.Application
│   ├── Abstractions/Persistence
│   ├── Admin, AutomatedCorners, Automation, Betting
│   ├── MatchHistory, Predictions, Teams, UpcomingMatches
│   └── Contratos, DTO, comandos y casos de uso
├── CornersPrediction.Infrastructure
│   ├── SqlServer       → repositorios Dapper
│   ├── Persistence     → DbContext/SQLite y repositorios auxiliares
│   ├── Python          → ejecución de modelos antiguos
│   ├── Automation      → orquestación del pipeline
│   └── Options         → configuración tipada
├── CornersPredictionApi
│   ├── Controllers     → endpoints HTTP
│   ├── ApiFootball     → cliente, sincronización y repositorio API-Football
│   ├── NewGenerationMl → carga, features e inferencia de los 12 modelos 2026
│   ├── Robot           → scrapers y Bot A/B/C
│   ├── SqlScripts/sql  → bootstrap, SP e índices
│   └── Program.cs      → composición, seguridad y configuración
└── CornersPrediction.Web
    ├── Controllers/Views/Models → MVC/Razor
    ├── Clients                  → clientes HTTP tipados hacia la API
    ├── Services                 → usuario actual y autenticación
    └── Program.cs               → Azure AD, autorización y DI del frontend
```

### Dependencias entre capas

```text
Web MVC ──HTTP──> API Controller
                    │
                    v
             Application Use Case
                    │
                    v
          Application Repository Interface
                    ^
                    │ DI
          Infrastructure Dapper Repository
                    │
                    v
        Stored Procedure / SQL parametrizado
                    │
                    v
                 Azure SQL
```

`Domain` no referencia otros proyectos. `Application` referencia `Domain`. `Infrastructure` referencia `Application` y `Domain`. La API referencia `Application` e `Infrastructure`. La Web no referencia esos proyectos: consume la API por HTTP y mantiene sus propios viewmodels.

### Flujo real de Bot Picks

1. `Views/BotPicks/Index.cshtml` solicita `/BotPicks/Selections`.
2. `BotPicksController.Selections` recibe y valida los filtros de la vista.
3. `AutomatedCornersApiClient` llama `GET /api/automated-corners/selections` con la clave interna.
4. `AutomatedCornersController.GetSelections` crea `AutomatedCornerSelectionsFilterRequest`.
5. `IGetAutomatedCornerSelectionsUseCase` aplica la regla de aplicación.
6. El caso de uso depende de `IAutomatedCornerSelectionsRepository`.
7. DI resuelve `SqlServerAutomatedCornerSelectionsRepository`.
8. El repositorio construye `DynamicParameters` y ejecuta `dbo.sp_GetAutomatedCornerBetSelections`.
9. Dapper mapea las columnas a `AutomatedCornerSelectionDto`.
10. El resultado vuelve API → cliente MVC → JSON → tabla Razor/JavaScript.

### Responsabilidad por aplicación

#### `CornersPrediction.Web`

- Presentación, Razor, navegación, filtros y validación amigable.
- Autenticación Microsoft/Azure AD y autorización de la UI.
- No debe conocer SQL ni ejecutar SP.
- Sus clientes tipados encapsulan URL, serialización y encabezados hacia la API.

#### `CornersPredictionApi`

- Contrato HTTP, validación de transporte y códigos de respuesta.
- Composición de casos de uso, robots, scrapers, API-Football e inferencia ML.
- Aplica la clave interna y expone Swagger/health.
- Debe evitar lógica SQL en controllers. Hoy algunos módulos históricos de `Robot` todavía mezclan orquestación y acceso a datos; son candidatos a migrar gradualmente a `Application`/`Infrastructure`.

#### `CornersPrediction.Application` y `Domain`

- Equivalen a la parte reutilizable de `CoreMain` del ejemplo: contratos y reglas.
- `Application` define interfaces de repositorio, casos de uso, DTO y comandos.
- `Domain` conserva entidades y reglas que no dependen de ASP.NET, Dapper ni SQL Server.

#### `CornersPrediction.Infrastructure`

- Implementa interfaces de `Application` con Dapper, SQL Server, SQLite, Python y HTTP.
- Es el lugar preferido para nuevas implementaciones de repositorio.
- La API registra estas implementaciones con `AddInfrastructure()`.

## 2. Guía de acceso a datos con Dapper

### `QueryAsync<T>`

Úsalo cuando el SP puede devolver cero, una o muchas filas. Ejemplo real simplificado de Bot Picks:

```csharp
var command = new CommandDefinition(
    "dbo.sp_GetAutomatedCornerBetSelections",
    parameters,
    commandType: CommandType.StoredProcedure,
    commandTimeout: 300,
    cancellationToken: cancellationToken);

var rows = await connection.QueryAsync<AutomatedCornerSelectionDto>(command);
return rows.ToArray();
```

Materializa con `ToArray()` antes de cerrar la conexión. El alias de cada columna debe coincidir con la propiedad C#.

### `QueryFirstOrDefaultAsync<T>`

Úsalo si cero filas es válido y, por diseño, solo importa la primera. Si la identidad exige unicidad, en este proyecto es más seguro `QuerySingleOrDefaultAsync<T>` porque detecta duplicados:

```csharp
var item = await connection.QuerySingleOrDefaultAsync<BettingRecord>(command);
return item; // null significa no encontrado
```

No uses `FirstOrDefault` para ocultar accidentalmente dos filas que deberían ser únicas.

### `ExecuteAsync`

Úsalo para `INSERT`, `UPDATE`, `DELETE`, `MERGE` o un SP que no devuelve un resultset. `ExecuteAsync` devuelve filas afectadas por el comando, pero varios SP de este proyecto usan un parámetro `OUTPUT` explícito:

```csharp
var parameters = new DynamicParameters();
parameters.Add("Id", id, DbType.Int64);
parameters.Add("RowsAffected", dbType: DbType.Int32,
    direction: ParameterDirection.Output);

await connection.ExecuteAsync(new CommandDefinition(
    "dbo.sp_DeleteMatchHistory",
    parameters,
    commandType: CommandType.StoredProcedure,
    cancellationToken: cancellationToken));

return parameters.Get<int>("RowsAffected");
```

### `ExecuteScalarAsync<T>`

Úsalo para una sola celda: `COUNT`, `EXISTS`, un id con `SCOPE_IDENTITY()` o una versión. Ejemplo real del bootstrap API-Football:

```csharp
var schemaExists = await connection.ExecuteScalarAsync<int>(
    new CommandDefinition(
        "SELECT CASE WHEN OBJECT_ID(N'dbo.ApiFootballTeams', N'U') IS NOT NULL THEN 1 ELSE 0 END;",
        cancellationToken: cancellationToken));
```

Si el SP ya utiliza `@InsertedId OUTPUT`, mantén un solo patrón por módulo; no mezcles retorno, resultset y output para el mismo dato.

### `DynamicParameters`

Declara nombre, valor, tipo, tamaño y dirección. Esto evita inferencias peligrosas, especialmente en `decimal`, `nvarchar` y outputs:

```csharp
var parameters = new DynamicParameters();
parameters.Add("League", request.League, DbType.String, size: 200);
parameters.Add("MatchDate", request.MatchDate, DbType.Date);
parameters.Add("Odds", request.Odds, DbType.Decimal,
    precision: 10, scale: 2);
parameters.Add("InsertedId", dbType: DbType.Int64,
    direction: ParameterDirection.Output);
```

En Dapper, un valor C# `null` se envía como SQL `NULL`. Cuando se usa `SqlParameter` manual, este repositorio emplea `(object?)value ?? DBNull.Value`.

### SP y cancelación

El patrón recomendado es siempre `CommandDefinition`, `CommandType.StoredProcedure`, timeout explícito para operaciones largas y `CancellationToken`:

```csharp
var command = new CommandDefinition(
    "dbo.sp_MiOperacion",
    parameters,
    commandType: CommandType.StoredProcedure,
    commandTimeout: 60,
    cancellationToken: cancellationToken);
```

### Excepciones SQL con contexto

La API ya reconoce errores de negocio lanzados con `THROW 50018` para duplicados. Al envolver, conserva `SqlException` como `InnerException`, el número SQL y el contexto no sensible:

```csharp
try
{
    await connection.ExecuteAsync(command);
}
catch (SqlException ex) when (ex.Number == 50018)
{
    throw new InvalidOperationException(
        $"El partido ya existe. SourceMatchId={request.SourceMatchId}, " +
        $"League={request.League}, Date={request.MatchDate:yyyy-MM-dd}.",
        ex);
}
catch (SqlException ex)
{
    throw new InvalidOperationException(
        $"Falló dbo.sp_MiOperacion (SQL {ex.Number}) para el partido {request.SourceMatchId}.",
        ex);
}
```

No incluyas connection strings, tokens ni payloads completos en el mensaje. Registra la excepción en el controller/orquestador una sola vez para evitar logs duplicados.

## 3. Patrones exactos para stored procedures

### Lectura: filtros → SP → DTO

```csharp
public async Task<IReadOnlyList<AutomatedCornerSelectionDto>> GetSelectionsAsync(
    AutomatedCornerSelectionsFilterRequest filters,
    CancellationToken cancellationToken)
{
    await using var connection = new SqlConnection(_connectionString);
    var parameters = new DynamicParameters();
    parameters.Add("DateFrom", filters.DateFrom, DbType.Date);
    parameters.Add("DateTo", filters.DateTo, DbType.Date);
    parameters.Add("Status", filters.Status, DbType.String, size: 20);
    parameters.Add("OnlyPending", filters.OnlyPending, DbType.Boolean);

    var rows = await connection.QueryAsync<AutomatedCornerSelectionDto>(
        new CommandDefinition(
            "dbo.sp_GetAutomatedCornerBetSelections",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

    return rows.ToArray();
}
```

El SP debe proyectar nombres compatibles:

```sql
SELECT
    s.AutomatedCornerBetSelectionId,
    MatchDay = CAST(s.MatchDate AS date),
    Recommendation = CONCAT(s.SelectedSide, N' ', s.LineValue),
    s.Status
FROM dbo.AutomatedCornerBetSelections AS s
WHERE (@DateFrom IS NULL OR s.MatchDate >= @DateFrom)
  AND (@OnlyPending = 0 OR s.Status = N'Pending');
```

### Escritura: insert/update

Patrón real de `SqlServerMatchHistoryRepository.AddAsync`:

```csharp
parameters.Add("League", item.League, DbType.String, size: 100);
parameters.Add("MatchDate", item.MatchDate.ToDateTime(TimeOnly.MinValue), DbType.Date);
parameters.Add("InsertedId", dbType: DbType.Int64,
    direction: ParameterDirection.Output);

await connection.ExecuteAsync(new CommandDefinition(
    "dbo.sp_InsertMatchHistory",
    parameters,
    commandType: CommandType.StoredProcedure,
    cancellationToken: cancellationToken));

item.Id = checked((int)parameters.Get<long>("InsertedId"));
```

SP mínimo correspondiente:

```sql
CREATE OR ALTER PROCEDURE dbo.sp_InsertMatchHistory
    @League nvarchar(100),
    @MatchDate date,
    @InsertedId bigint OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.MatchHistory (League, MatchDate)
    VALUES (@League, @MatchDate);
    SET @InsertedId = CONVERT(bigint, SCOPE_IDENTITY());
END;
```

### Retorno escalar

Para un log o auditoría que debe devolver el id directamente:

```sql
CREATE OR ALTER PROCEDURE dbo.sp_InsertErrorLog
    @Operation nvarchar(100),
    @Message nvarchar(2000)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.ErrorLog(Operation, Message) VALUES (@Operation, @Message);
    SELECT CONVERT(int, SCOPE_IDENTITY());
END;
```

```csharp
var logId = await connection.ExecuteScalarAsync<int>(
    "dbo.sp_InsertErrorLog",
    new { Operation = operation, Message = message },
    commandType: CommandType.StoredProcedure);
```

### TVP

No existe hoy ningún uso de `AsTableValuedParameter`, `SqlDbType.Structured` ni `DataTable` en la solución. El bulk de históricos actual usa JSON (`dbo.sp_BulkInsertMatchHistoryJson`). Por tanto, el siguiente ejemplo es el patrón recomendado si se agrega un TVP; no se presenta como código existente:

```sql
CREATE TYPE dbo.MatchHistoryRowType AS TABLE
(
    RowNumber int NOT NULL,
    MatchDate date NOT NULL,
    HomeTeam nvarchar(150) NOT NULL,
    AwayTeam nvarchar(150) NOT NULL,
    HomeCorners int NULL,
    AwayCorners int NULL
);
GO
CREATE OR ALTER PROCEDURE dbo.sp_BulkInsertMatchHistory
    @Rows dbo.MatchHistoryRowType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT dbo.MatchHistory(MatchDate, HomeTeam, AwayTeam, HomeCorners, AwayCorners)
    SELECT MatchDate, HomeTeam, AwayTeam, HomeCorners, AwayCorners FROM @Rows;
END;
```

```csharp
var table = new DataTable();
table.Columns.Add("RowNumber", typeof(int));
table.Columns.Add("MatchDate", typeof(DateTime));
table.Columns.Add("HomeTeam", typeof(string));
table.Columns.Add("AwayTeam", typeof(string));
table.Columns.Add("HomeCorners", typeof(int));
table.Columns.Add("AwayCorners", typeof(int));

table.Rows.Add(1, matchDate, home, away,
    homeCorners is null ? DBNull.Value : homeCorners,
    awayCorners is null ? DBNull.Value : awayCorners);

var parameters = new DynamicParameters();
parameters.Add("Rows", table.AsTableValuedParameter("dbo.MatchHistoryRowType"));
await connection.ExecuteAsync(
    "dbo.sp_BulkInsertMatchHistory",
    parameters,
    commandType: CommandType.StoredProcedure);
```

Puntos críticos del TVP:

- El orden de columnas del `DataTable` debe ser exactamente el del type SQL.
- `int`, `bigint`, `decimal`, `date` y longitudes de strings deben ser compatibles.
- Una celda SQL nula es `DBNull.Value`, no un objeto `null` dentro de `DataRow`.
- El parámetro debe llevar el nombre completo del type: `dbo.MatchHistoryRowType`.
- Los TVP son `READONLY`; la salida se devuelve en otro resultset o parámetro.

## 4. Dependencias e inyección

`CornersPredictionApi/Program.cs` llama:

```csharp
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
```

`AddApplication()` registra casos de uso. `AddInfrastructure()` conecta contratos con implementaciones:

```csharp
services.AddScoped<IMatchHistoryRepository, SqlServerMatchHistoryRepository>();
services.AddScoped<IBettingRepository, SqlServerBettingRepository>();
services.AddScoped<IAutomatedCornerSelectionsRepository,
    SqlServerAutomatedCornerSelectionsRepository>();
```

Cuando un caso de uso pide `IAutomatedCornerSelectionsRepository`, el contenedor crea un `SqlServerAutomatedCornerSelectionsRepository` dentro del scope HTTP.

### `Scoped`

- Una instancia por request HTTP/scope.
- Adecuado para casos de uso, repositorios y servicios con estado de una operación.
- Es el lifecycle preferido para repositorios, aunque cada método abra su conexión.

### `Singleton`

- Una instancia durante toda la vida del proceso.
- Solo para componentes thread-safe, configuración inmutable, catálogos/caches coordinados o workers persistentes.
- En este proyecto, paquetes/model runner 2026, políticas y algunos coordinadores son singleton.
- No debe capturar un servicio scoped. `SqlConnection` tampoco debe guardarse en un singleton.

### Riesgos al romper el contrato

- Cambiar firma solo en interfaz o implementación impide compilar.
- Cambiar nulabilidad/tipos sin actualizar SP produce mapping incorrecto o conversiones SQL.
- Renombrar una columna sin alias deja la propiedad DTO con su valor por defecto.
- Registrar dos implementaciones del mismo contrato puede resolver la última silenciosamente.
- Inyectar scoped dentro de singleton produce error de validación o estado compartido inseguro.
- Crear acceso SQL directamente en controllers duplica reglas y hace difícil probar/debuggear.

### Mejora de orden recomendada

El equivalente futuro de `SqlConexion` debería ser un factory pequeño, stateless y singleton, por ejemplo `ISqlConnectionFactory.CreateConnection()`. Hoy cada repositorio lee `DefaultConnection` directamente y algunos módulos de `Robot` repiten esa lógica. Conviene migrarlos por módulo, no hacer una reescritura masiva mientras el bot está operativo. Key Vault también puede añadirse como proveedor de configuración; los repositorios no deberían saber si el secreto vino de `.env`, Key Vault o App Service.

## 5. Checklist para agregar un nuevo SP

### SQL

1. Definir contrato: parámetros, nulabilidad, tamaños, outputs y columnas de salida.
2. Crear el script idempotente con `CREATE OR ALTER PROCEDURE`.
3. Añadir `SET NOCOUNT ON` y esquema `dbo` explícito.
4. Probar con valores normales, nulos, límites, inexistentes y duplicados.
5. Revisar plan/índices y hacer la operación transaccional si modifica varias tablas.

### Domain/Application

6. Crear o actualizar entidad/DTO/comando sin tipos de ASP.NET ni Dapper.
7. Añadir el método a la interfaz de repositorio.
8. Añadir/actualizar el caso de uso y sus validaciones.

### Infrastructure

9. Implementar el método con `await using SqlConnection`, `DynamicParameters` y `CommandDefinition`.
10. Declarar `DbType`, tamaño, precision/scale y outputs.
11. Mapear nombres SQL a propiedades C# con aliases explícitos.
12. Envolver `SqlException` solo cuando agrega contexto útil.

### API/Web

13. Registrar la implementación en DI si es un repositorio nuevo.
14. Exponer endpoint con request DTO, códigos `200/201/204/400/404/409/500` coherentes.
15. Adaptar el cliente HTTP MVC y después controller/view.
16. No exponer entidades SQL directamente a la vista si el contrato de UI difiere.

### Validación mínima

- Strings requeridos con `IsNullOrWhiteSpace`, longitudes y valores permitidos.
- Fechas coherentes (`DateTo >= DateFrom`) y números no negativos.
- IDs mayores que cero y manejo explícito de no encontrado.
- Usuario/rol/ownership antes de consultar o mutar.
- CancellationToken propagado hasta Dapper.
- Prueba de integración de SP y smoke test del endpoint.
- `dotnet build CornersPrediction.sln --no-restore` antes de entregar.

## 6. Troubleshooting específico

### “No inserta un campo”

Revisa en este orden:

1. El request JSON realmente trae el valor.
2. El DTO tiene setter/init público y tipo compatible.
3. El repository añade el parámetro con el mismo nombre del SP.
4. El SP lo usa en el `INSERT/UPDATE`; no basta declararlo.
5. Un trigger/default no lo reemplaza.
6. No existe un SP antiguo desplegado en otro catálogo.

```sql
SELECT DB_NAME() AS DatabaseName, @@SERVERNAME AS ServerName;
SELECT name, TYPE_NAME(user_type_id) AS SqlType, max_length,
       precision, scale, is_output
FROM sys.parameters
WHERE object_id = OBJECT_ID(N'dbo.sp_MiOperacion')
ORDER BY parameter_id;

EXEC sys.sp_helptext N'dbo.sp_MiOperacion';
SELECT TOP (20) * FROM dbo.MiTabla ORDER BY Id DESC;
```

### “Falla binding del body JSON”

- Verifica `Content-Type: application/json`.
- JSON válido, sin coma final, fecha ISO y decimales con punto.
- `[FromBody]` solo una vez; no mezclar body con query esperando binding automático.
- La API actual es case-insensitive y conserva nombres de propiedad C# en sus respuestas.
- Revisa el cuerpo de `ProblemDetails` y logs del endpoint.

```bash
curl -i -X POST http://localhost:5070/api/mi-endpoint \
  -H 'Content-Type: application/json' \
  -H 'X-Internal-Api-Key: ...' \
  --data '{"matchDate":"2026-08-10","homeTeam":"A","awayTeam":"B"}'
```

### “El SP funciona en SQL pero falla desde app”

- Confirma servidor, base y usuario efectivo de la app.
- Comprueba permisos `EXECUTE`, timeout y tipos/tamaños.
- Compara todos los parámetros, incluido `DBNull` y outputs.
- Si SQL usa sesión/configuración especial, hazla explícita en el SP.
- Asegura que la app no está llamando otra versión del SP.

```sql
SELECT DB_NAME(), SUSER_SNAME(), ORIGINAL_LOGIN(), APP_NAME();
SELECT HAS_PERMS_BY_NAME(N'dbo.sp_MiOperacion', N'OBJECT', N'EXECUTE') AS CanExecute;
EXEC sys.sp_describe_first_result_set
    @tsql = N'EXEC dbo.sp_MiOperacion @Id = 1';
```

Desde la solución:

```bash
dotnet build CornersPrediction.sln --no-restore --disable-build-servers
curl -i http://localhost:5070/health
```

### “TVP mismatch de columnas”

Compara type SQL y `DataTable` por ordinal, no solo por nombre:

```sql
SELECT c.column_id, c.name, TYPE_NAME(c.user_type_id) AS SqlType,
       c.max_length, c.precision, c.scale, c.is_nullable
FROM sys.table_types AS tt
JOIN sys.columns AS c ON c.object_id = tt.type_table_object_id
WHERE SCHEMA_NAME(tt.schema_id) = N'dbo'
  AND tt.name = N'MatchHistoryRowType'
ORDER BY c.column_id;
```

Errores típicos: `DateOnly` no convertido a `DateTime`, `long` enviado a `int`, orden distinto, decimal sin escala, string demasiado largo o `null` en vez de `DBNull.Value`.

### Diagnóstico de Bot C histórico

```sql
SELECT
    CAST(MatchDate AS date) AS MatchDay,
    MarketType,
    COUNT(*) AS PendingPicks
FROM dbo.AutomatedCornerBetSelections
WHERE AutomationVersion LIKE N'%C2026%'
  AND MatchDate >= '20260619'
  AND Status = N'Pending'
GROUP BY CAST(MatchDate AS date), MarketType
ORDER BY MatchDay, MarketType;
```

La reconstrucción se puede reanudar con:

```bash
node scripts/run-bot-c-historical-backfill.mjs \
  --from=2026-06-19 --to=2026-08-10 \
  --start-batch=1 --batch-size=25 --concurrency=3
```

El upsert identifica la versión de automatización y el partido/mercado. Conserva un estado ya liquidado; los inserts nuevos nacen `Pending`.

## Cómo trabaja esta arquitectura en 10 líneas

1. Razor muestra la interfaz y nunca habla con SQL.
2. Un controller MVC convierte la acción del usuario en una llamada HTTP tipada.
3. La API valida transporte, seguridad y el request.
4. El controller delega la operación al caso de uso de `Application`.
5. El caso de uso aplica reglas y depende de una interfaz.
6. DI resuelve esa interfaz con un repositorio de `Infrastructure`.
7. El repositorio abre una conexión SQL corta desde el pool.
8. Dapper envía parámetros tipados a un SP y mapea su resultado a DTO.
9. La respuesta regresa API → Web → View, sin filtrar detalles de persistencia.
10. Logs, IDs de corrida, CancellationToken y scripts idempotentes permiten depurar cada salto.
