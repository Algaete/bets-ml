# Football Intelligence: plan de implementación

## Objetivo actual

Agregar una capa prepartido auditable que recopile hechos estructurados de API-Football y noticias, genere snapshots sin data leakage y entregue una señal contextual a todos los bots activos: A, C, D, E, F, G y H. Bot B permanece retirado.

La garantía principal es:

> Si no existe un snapshot previo al partido con evidencia utilizable, la contribución de inteligencia es exactamente cero. La predicción, probabilidad, edge, EV y decisión quedan iguales a las que habría producido el bot sin esta capa.

Cada bot conserva su motor, calibración, umbrales y estado de publicación. G y H siguen siendo shadow; habilitar inteligencia no los autoriza a publicar. La variable contextual común es la inteligencia prepartido.

## Arquitectura existente

- `CornersPrediction.Domain`: entidades puras de dominio.
- `CornersPrediction.Application`: contratos, casos de uso y motores determinísticos. Aquí viven el selector C y los experimentos D/E.
- `CornersPrediction.Infrastructure`: repositorios SQL Server con Dapper e integraciones compartidas.
- `CornersPredictionApi`: composition root, API-Football, workers, endpoints y robot de picks.
- `CornersPrediction.Web`: MVC/Razor que consume la API interna.
- SQL Server se configura con `ConnectionStrings:DefaultConnection`; los repositorios existentes abren `SqlConnection` por operación y usan Dapper.
- `ApiFootballClient` ya centraliza autenticación, rate limiting, timeout y llamadas estructuradas.
- `AutomatedCornersSelectionService` construye las entradas de todos los bots y persiste evaluaciones/picks.
- Bot E (`MODELS_2026`) y Bot F (`LEGACY_EMPIRICAL`) comparten `BotEEmpiricalCalibrationCalculator` y difieren en la fuente del modelo base.

## Componentes que se reutilizan

- `ApiFootballClient`: se extenderá para fixture, injuries, squad, lineups y fixture players.
- `AutomatedCornersSelectionService`: punto único para resolver e inyectar el snapshot previo a las decisiones de todos los bots activos.
- `BotCPickDecisionEngine`: mantiene los cálculos base de C/D/E/F/H y aplica el ajuste contextual después de la calibración empírica.
- `Microsoft.Data.SqlClient` + Dapper: sin introducir otro ORM.
- `HtmlAgilityPack`: ya está referenciado por el proyecto API.
- Inicialización SQL idempotente ya usada en `Program.cs`.
- `BackgroundService`: patrón ya usado por `RecommendationJobWorker`.

## Componentes nuevos

### Dominio

- Enums para eventos, disponibilidad, certeza, fuente, resolución y decisión.
- Entidades de documento, hecho, snapshot y ejecución.

### Application

- Contratos de búsqueda, extracción de artículos, extracción semántica, resolución, consolidación y persistencia.
- Opciones validadas para ventanas, fuentes, confianza y pesos.
- `FootballIntelligenceAdjustmentCalculator`, puro y testeable.
- Regla neutral explícita para evidencia ausente, futura, vencida, no accionable o de baja confianza.

### Infrastructure

- Repositorios SQL Server/Dapper para documentos, hechos, snapshots, alias, fuentes y runs.
- Lecturas siempre limitadas por `CutoffAtUtc` y `FirstSeenAtUtc`.

### API

- Adaptador de API-Football.
- Proveedor de búsqueda desacoplado; si no hay credencial configurada, devuelve resultado vacío auditable.
- Extractor HTML por `HttpClient` + `HtmlAgilityPack`.
- Extractor semántico configurable; nunca produce picks.
- Orquestador por fixture, endpoints internos y worker.

## Integración de bots activos

1. Resolver `ApiFootballFixtureId` del candidato.
2. Leer el snapshot más reciente cuyo `CutoffAtUtc <= AsOfDateUtc`.
3. Verificar evidencia, frescura, confianza y ausencia de leakage.
4. Transformar impactos determinísticos del mercado en un ajuste acotado.
5. Si no pasa cualquier verificación, usar `Adjustment = 0` y conservar la salida base bit a bit en los campos numéricos.
6. Guardar en la auditoría de decisión: snapshot id, cutoff, cobertura, confianza, ajuste y código de neutralidad/aplicación.

A/C/D/E/F/G/H tienen `FootballIntelligence.Enabled=true`. A usa su ruta legacy; C/D/E/F/H comparten el selector; G aplica la señal en su ruta aislada antes de incertidumbre, EV y abstención. B conserva únicamente su historial y no se ejecuta.

## Persistencia versionada

Scripts:

- `CornersPredictionApi/SqlScripts/FootballIntelligence/001_CreateNewsTables.sql`
- `CornersPredictionApi/SqlScripts/FootballIntelligence/002_CreateIndexes.sql`
- `CornersPredictionApi/SqlScripts/FootballIntelligence/003_CreateSourceConfig.sql`
- `CornersPredictionApi/SqlScripts/FootballIntelligence/004_AddFactIdempotency.sql`

Las tablas serán idempotentes y conservarán documentos, extracciones versionadas, hechos contradictorios y snapshots históricos.

## Fases

1. Dominio, contratos, DTOs, opciones y ajuste neutral.
2. Tablas, índices y repositorios Dapper.
3. Extensión de API-Football y adaptadores de búsqueda/artículos.
4. Extracción semántica JSON, resolución y consolidación.
5. Snapshot builder, endpoints y worker.
6. Integración inicial E/F y posterior expansión controlada a A/C/D/G/H.
7. Tests unitarios e integración, build y documentación operativa.

## Pruebas obligatorias

- Sin snapshot -> ajuste cero.
- Snapshot sin documentos/hechos -> ajuste cero.
- Evidencia posterior al cutoff -> excluida y ajuste cero.
- Snapshot vencido o de baja confianza -> ajuste cero.
- Hechos informativos pero sin impacto medible -> ajuste cero.
- Snapshot válido -> ajuste acotado y trazable.
- Todos los bots activos usan la misma fórmula contextual y conservan sus motores propios.
- G/H conservan `PublishEnabled=false`.
- B permanece retirado y sin señal activa.
- Duplicados no aumentan corroboración.
- Una alineación oficial prevalece sin borrar hechos anteriores.

## Despliegue seguro

- El módulo nace deshabilitado globalmente.
- La búsqueda y el LLM requieren secretos por variables de entorno/Key Vault.
- La migración SQL se aplicará antes de habilitar el worker.
- Cada bot activo puede habilitar la señal individualmente desde su JSON de estrategia.
- La fuente estructurada API-Football funciona sin un buscador web. Las noticias externas requieren configurar un proveedor y su credencial; sin ella, esa parte queda neutral y auditable.
- No se hará backtest histórico con noticias recuperadas hoy: sin `FirstSeenAtUtc` histórico sería data leakage.
