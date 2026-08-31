# Robust Pick Evaluation Layer — plan de implementación

Estado: implementación completada y validada localmente. Este documento registra primero la arquitectura real encontrada y después la entrega adaptada a ella. La capa nace habilitada en modo `Shadow`; por diseño no modifica la publicación ni el stake efectivo del bot actual.

## Arquitectura encontrada

| Responsabilidad | Implementación real | Adaptación robusta |
|---|---|---|
| Dominio | `CornersPrediction.Domain` | Núcleo numérico puro y determinista en `RobustPickEvaluation` |
| Casos de uso | `CornersPrediction.Application` | Opciones, contratos de persistencia y orquestación |
| SQL/repositorios | `CornersPrediction.Infrastructure` y repositorios Dapper propios del robot en `CornersPredictionApi` | Repositorio Dapper append-only y consultas temporales por lote |
| API | `CornersPredictionApi` | Endpoints de detalle, historial, comparación, métricas y backfill seguro |
| Web MVC | `CornersPrediction.Web` | Secciones robustas dentro del modal de detalle de Bot Picks |
| Pipeline de picks | `AutomatedCornersSelectionService` | Evaluación posterior a la decisión actual y persistencia en Shadow |
| Picks publicados | `dbo.AutomatedCornerBetSelections` | Se reutiliza; no se cambia el resultado original en Shadow |
| Candidatos evaluados | `dbo.AutomatedBotPickEvaluations` | Se reutiliza. En bots selector 2026 contiene aprobados y rechazados; en legacy el histórico disponible es principalmente `SelectedPicksOnly` |
| Cuotas prepartido | `dbo.CornerOddsSnapshots` y referencias desde candidatos | Se reutiliza; no se crea otra tabla de odds |
| Definición por bot | `dbo.AutomatedBotDefinitions` y mantenedor existente | Política robusta versionada con override por bot/mercado sin reemplazar la definición actual |
| Resultados | `MatchHistory` + reconciliación API-Football | Fuente de actuals; disponibilidad conservadora con lag configurable |
| Settlement | `AutomatedBotPickSettlementCalculator` y adaptador asiático de Bot G | Se conserva como semántica de Win/HalfWin/Push/HalfLoss/Loss |
| Calibración/no-vig | Selectores C/D/E y servicios estrictos de Bot G | Se reutilizan evidencias cuando existen; missing metadata reduce reliability |
| Inteligencia | `FootballIntelligence` con snapshots y cutoff | Se mapea a estados no ambiguos; ausencia/fallo nunca se convierte en ajuste positivo |
| Jobs | workers de recomendaciones, Bot G e inteligencia | La evaluación se integra al pipeline y el backfill queda como operación separada e idempotente |

## Hallazgos de datos y leakage

- Los picks publicados guardan modelo, componentes Direct/Home/Away, contexto, línea, cuota, decisión y settlement.
- `AutomatedBotPickEvaluations` conserva todos los candidatos futuros de los selectores 2026, incluidos rechazos. Los registros legacy históricos no garantizan esa cobertura: sus residuos se etiquetan `SelectedPicksOnly` y bajan la confiabilidad.
- `CornerOddsSnapshots` conserva timestamp, línea y ambos lados cuando la fuente los entrega. La probabilidad no-vig sólo se considera disponible con Over y Under de la misma línea y snapshot.
- `ModelVersion` y `ModelTrainedThroughUtc` existen para los modelos 2026 y Bot G; los legacy pueden carecer de metadata completa. No se inventa el cutoff faltante.
- `CreatedAtUtc`/`AsOfUtc` de candidatos y snapshots permiten reconstrucción temporal. Un backfill sólo puede consumir evidencia existente a ese cutoff.
- Un resultado histórico es elegible sólo si la predicción y entrenamiento son anteriores al fixture y `OutcomeAvailableUtc <= EvaluationAsOfUtc`, aplicando `OutcomeAvailabilityLagHours` (8 por defecto, configurable).
- No se mezclan familias Goals, Corners, Shots y ShotsOnGoal. La carga SQL se hace una vez por evaluación/lote; ninguna simulación consulta SQL.

## Cálculos actuales localizados

- `AutomatedCornersSelectionService.EvaluateCandidate` y `EvaluateBotCCandidate`: lado, distancia, probabilidad, edge, EV, score, desacuerdo y razones.
- `AutomatedCornersSelectionService.PersistCandidateAsync`: stake por perfil/cap y publicación.
- `StrictMarketProbabilityService`: no-vig bilateral estricto.
- calibradores C/D/E/G: probabilidad calibrada, fallback y/o incertidumbre según bot.
- `AutomatedBotPickSettlementCalculator`: líneas enteras, medias y cuartos con factores reales.
- `BotPickProductionPlanner`: visualización histórica/productiva en Web; no es la fuente de decisión robusta.

## Entrega por etapas dentro de esta tarea

### 1. Núcleo y persistencia (obligatoria)

- Tipos de dominio, reason codes estables y estados de evidencia.
- Consenso, distancia conservadora, coherencia y golden test 22.13/11.96/11.53/23.71.
- Reconciliación con pesos validados fuera de muestra y fallback explícito.
- Bootstrap empírico ponderado, `EffectiveN`, MAD/MAE/RMSE, seed SHA-256 estable y cinco estados de settlement.
- EV asiático, fair probability/odds, escenarios externos y valores robustos.
- Reliability de calibración, política versionada, stake sin aumento y exposición conservadora.
- Migración SQL Server no destructiva para evaluaciones, componentes y políticas; reutilización de candidatos y odds existentes.

### 2. Integración Shadow, API y UI (obligatoria)

- Orquestar la evaluación con un snapshot de inputs auditable.
- Añadir una evaluación append-only por pick nuevo sin alterar publicación ni liquidación en `Shadow`.
- Exponer detalle actual, historial, comparación, métricas y previsualización/backfill.
- Mostrar predicciones, consenso, distribución, valor, calibración, evidencia, decisión, razones y versiones en el detalle de Bot Picks.
- Reemplazar textos ambiguos como “Neutral adjustment: No” por el estado real de evidencia.

### 3. Operación y validación (obligatoria)

- Backfill reanudable/idempotente con `DryRun` y filtros temporales.
- Backtest walk-forward baseline vs robust shadow, sin split aleatorio y con métricas por mercado.
- Pruebas unitarias e integración proporcional al mecanismo actual de tests.
- Documentación de arquitectura, leakage, configuración, backtesting y operaciones.
- Restore, build, tests, validación estática de SQL, golden test, dry-run pequeño y backtest pequeño.

## Decisiones de seguridad

1. `Shadow` es el default y su decisión efectiva siempre sigue al sistema actual.
2. `Enforce` requiere configuración explícita; no se activa en esta entrega ni se cambia una política productiva existente.
3. La primera versión jamás aumenta stake. Sólo puede recomendar mantener, reducir o rechazar.
4. Evaluaciones son append-only: una reevaluación crea secuencia nueva, marca la anterior no actual y la referencia como supersedida dentro de una transacción.
5. La clave idempotente incluye sujeto lógico, versión, cutoff e input hash; una repetición exacta no crea duplicado.
6. Missing, stale, unavailable e insufficient evidence permanecen diferenciados.
7. Lineup, fatiga y game-state sin datos históricos válidos quedan como providers/readiness desactivados; no reciben parámetros inventados.

## Limitaciones conocidas a medir en Shadow

- Selection bias en el residual histórico legacy.
- Metadata de calibración y entrenamiento incompleta en algunos modelos antiguos.
- Closing odds/CLV no siempre disponible; nunca se usa retrospectivamente para decidir.
- Correlaciones inicialmente gobernadas por reglas conservadoras; una correlación aprendida requiere muestra temporal suficiente.
- Un pick ganado o perdido no valida la política. La recomendación de activación dependerá de volumen resuelto por mercado, calibración, drawdown y test final fuera de muestra.

## Validación de entrega

- `dotnet restore CornersPrediction.sln`: correcto.
- `dotnet build CornersPrediction.sln -c Release --no-restore`: 0 warnings, 0 errores.
- Ocho suites .NET: 209/209 pruebas correctas.
- RobustPickBacktest: 8/8 self-tests correctos, incluido holdout final congelado y bootstrap fixture-day.
- Suite Python offline de Bot G: 14/14 correcta.
- Golden test 22.13/11.96/11.53/23.71: correcto.
- Backfill `DryRun`: probado sobre el contrato de repositorio; cero evaluaciones y cero appends.
- Migración: parseada con SQL Server ScriptDom y auditada como aditiva/append-only. No se ejecutó contra Azure SQL ni otra base productiva.
- Búsqueda acotada de secretos en los archivos de la capa: sin credenciales hardcodeadas.
