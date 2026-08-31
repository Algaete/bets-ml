# Operaciones

## Arranque y migración

La API aplica el script idempotente `CornersPredictionApi/sql/20260829_robust_pick_evaluation.sql` mediante el inicializador SQL existente. Es aditivo: crea evaluaciones, componentes y políticas; no elimina ni renombra tablas actuales.

Antes de producción:

```bash
dotnet restore CornersPrediction.sln
dotnet build CornersPrediction.sln -c Release --no-restore
dotnet run --project tests/RobustPickEvaluation.Tests/RobustPickEvaluation.Tests.csproj -c Release
dotnet run --project tests/RobustPickEvaluation.Integration.Tests/RobustPickEvaluation.Integration.Tests.csproj -c Release
```

Valide el script en una base no productiva o mediante el pipeline SQL autorizado. No aplique manualmente un script desconocido sobre Azure SQL.

## Consultas API

Con la API local en `http://localhost:5070` y el header interno configurado:

```bash
curl -s -H "X-Internal-Api-Key: $INTERNAL_API_KEY" \
  http://localhost:5070/api/robust-pick-evaluations/123

curl -s -H "X-Internal-Api-Key: $INTERNAL_API_KEY" \
  http://localhost:5070/api/robust-pick-evaluations/123/history

curl -s -H "X-Internal-Api-Key: $INTERNAL_API_KEY" \
  http://localhost:5070/api/robust-pick-evaluations/123/comparison

curl -s -H "X-Internal-Api-Key: $INTERNAL_API_KEY" \
  "http://localhost:5070/api/robust-pick-evaluations/metrics?marketFamily=GOALS"
```

No escriba el valor real de la key en documentación, logs o commits.

## Backfill

Empiece siempre con `DryRun=true` y un rango pequeño. El comando acepta `FromUtc`, `ToUtc`, `BotKey`, `MarketFamily`, `MarketType`, `FixtureId`, `EvaluationVersion`, `DryRun` y `Force`.

```bash
curl -s -X POST \
  -H "X-Internal-Api-Key: $INTERNAL_API_KEY" \
  -H "Content-Type: application/json" \
  http://localhost:5070/api/robust-pick-evaluations/backfill \
  -d '{"fromUtc":"2026-08-20T00:00:00Z","toUtc":"2026-08-21T00:00:00Z","marketFamily":"GOALS","evaluationVersion":"robust-pick-evaluation-1.0.0","dryRun":true,"force":false}'
```

Un backfill persistente conserva AsOf original, no cambia pick/settlement y es idempotente. `Force` permite una secuencia nueva sólo cuando el input o versión cambian; no duplica un snapshot idéntico.

## Reevaluación

Una reevaluación se justifica por movimiento material de línea/cuota, snapshot nuevo de alineación/inteligencia o una versión/política nueva. El pipeline actual la dispara cuando existe una nueva evaluación fuente. Una repetición exacta (mismo sujeto, cutoff, versión e inputs) devuelve la fila existente; un input distinto crea otra secuencia y deja la anterior disponible en history.

`MinimumReevaluationIntervalSeconds` y `Significant*Movement` están validados como contrato del disparador, pero este repositorio no incluye un scheduler independiente que haga polling sólo para reevaluar picks ya publicados. Por eso no se afirma que esas opciones, por sí solas, creen una corrida: deben aplicarse en el productor del evento antes de invocar la capa. Esta limitación evita prometer una automatización que los datos/jobs actuales no ofrecen.

## Activar y volver a Shadow

1. Ejecute backfill y backtest walk-forward.
2. Revise métricas por mercado y volumen mínimo.
3. Inserte una política append-only `Enforce` con scope estrecho y versión nueva.
4. Monitoree desacuerdos, errores y exposición.
5. Para rollback, inserte una política posterior `Shadow`. Nunca edite o borre la política anterior.

## Observabilidad

Los logs estructurados incluyen evaluation/fixture/selection, bot, mercado, versión, modo, duración, EffectiveN, simulations, edge/EV, decisión y reason codes. No incluyen histogramas completos ni secretos.

Alertas operativas recomendadas:

- aumento de `robust_evaluations_failed_total`;
- `robust_data_leakage_rejected_total > 0`;
- cuotas stale o source unavailable por fuente;
- desacuerdo Shadow creciente por mercado;
- EffectiveN bajo durante varios lotes;
- duración de simulación o timeout anormal;
- ausencia de evaluaciones mientras siguen publicándose picks.

## Providers opcionales

Lineup, fatiga y game-state permanecen desactivados cuando no hay datos históricos suficientes. Un fallo opcional registra `SourceUnavailable` y no detiene el pipeline; tampoco produce un ajuste favorable. Revise `IntelligenceEvidenceStatus`, edades y counts en el detalle.

Closing odds/CLV se calculan sólo si existe un snapshot de cierre inmutable, posterior a la decisión y enlazado al mismo mercado. En los segmentos sin esa evidencia, CLV queda ausente y bloquea cualquier criterio de activación que lo requiera.

## Recuperación

- La migración es reejecutable.
- Un append exacto devuelve la fila existente y verifica su hash determinista.
- Una falla transaccional no deja dos filas `IsCurrent=1` para la misma clave lógica.
- No borre evaluaciones para “corregirlas”: emita una nueva versión/snapshot.
