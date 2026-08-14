# Bot C · Pick Selector 2026

## Alcance implementado

Bot C ya no reutiliza el filtro simple de A/B. Los doce modelos 2026 siguen respondiendo qué valor estadístico es probable en el partido; `BotCPickDecisionEngine` decide si una línea y cuota concretas tienen respaldo suficiente para publicarse.

La solución real está en .NET 8, no .NET 10. Esta entrega conserva el framework del repositorio para no introducir una migración transversal ajena al selector.

## Mapeo con la arquitectura existente

| Concepto | Implementación real |
|---|---|
| Modelo base | `NewGenerationPredictionService` + artefactos CatBoost |
| Candidato | Una fila de `PartidosProximosCuotas` enriquecida con la predicción 2026 |
| Historial | `IMatchHistoryRepository` / `GetPredictionContextUseCase` / `MatchHistory` |
| Feature builder del selector | `BotCPickDecisionEngine` |
| Configuración | `BotCStrategyConfiguration`, persistida en `AutomatedBotDefinitions.StrategyConfigurationJson` |
| Catálogo explicable | `BotCStrategyCatalog`, visible en el mantenedor |
| Evaluaciones | `AutomatedBotPickEvaluations` |
| Pick publicado | `AutomatedCornerBetSelections` |
| Liquidación | `AutomatedBotPickSettlementUseCase` y reconciliación con `MatchHistory` |

## Flujo anterior

```text
Cuota -> modelo A/B o modelo 2026 -> distancia/edge/EV simples
      -> mejor línea -> AutomatedCornerBetSelections
```

## Flujo nuevo de Bot C

```text
Cuota + modelos 2026
  -> historial estrictamente anterior al partido
  -> ventanas 5/10/20, overall y venue
  -> estadística + recencia + shrinkage + tendencia + hit rate
  -> predicción contextual independiente
  -> probabilidad calibrada + no-vig + edge + EV
  -> calidad + acuerdo + vector versionado
  -> LogisticRegression activa o RuleBasedConfidenceScore de fallback
  -> Approved / Rejected / PendingData / Invalid
  -> AutomatedBotPickEvaluations (todos)
  -> AutomatedCornerBetSelections (solo mejor Approved)
  -> settlement idempotente desde MatchHistory
```

## Protección contra fuga temporal

La consulta SQL exige `MatchHistory.MatchDate < Candidate.MatchDate`. El motor vuelve a aplicar `observation.MatchDateUtc < AsOfDateUtc` antes de calcular cualquier feature. En el esquema actual `MatchHistory` guarda fecha y no kickoff UTC; por seguridad, todos los partidos del mismo día quedan excluidos del historial del candidato.

El snapshot conserva `AsOfDateUtc`, `OddsCapturedAtUtc`, cantidad de filas recibidas/aceptadas, `FeatureSchemaVersion` y `ConfigurationVersion`.

## Features v1

- Ventanas 5, 10 y 20 por equipo.
- Overall y local/visitante.
- Valores a favor, en contra y total del mercado.
- Promedio, media exponencial ponderada, mediana, desviación, varianza, min/max, P25/P75, IQR y MAD.
- Shrinkage configurable; si no existe baseline de liga, usa el baseline combinado y registra `LeagueBaselineUnavailable`.
- Predicción contextual local, visitante y total con peso venue dependiente de muestra.
- Márgenes a la línea y distancias normalizadas por volatilidad.
- Hit rate exacto y sensibilidad en línea -1, -0.5, exacta, +0.5 y +1.
- Tendencia últimos 5 contra los 5 anteriores.
- Acuerdo entre ML, contexto, mediana, tendencia, hit rate y mercado.
- Calidad por muestra, venue, frescura, completitud, mercado y consistencia.
- Probabilidad implícita, no-vig, overround, edge y EV.

## Política de probabilidad y meta-modelo

El runtime carga `models/bot-c-meta/active.json` sin reiniciar y exige que su `FeatureSchemaVersion` coincida exactamente con el feature builder. Si coincide, la decisión registra `DecisionEngineType=MetaModel`, usa `MetaProbability` como `FinalProbability` y recalcula edge/EV. Si no existe o no coincide, registra el riesgo concreto y usa `RuleBasedFallback` solamente cuando la configuración lo permite.

Todavía no existe un artefacto productivo entrenable: acabamos de empezar a guardar **todos** los candidatos y la mayoría no tiene settlement. No se activó un modelo sintético ni uno entrenado con los antiguos picks ya filtrados, porque eso introduciría selection bias/leakage.

`FinalProbability` es la probabilidad del modelo base después de calibración logística configurable. `RuleBasedConfidenceScore` combina las señales de selección, pero no se presenta ni persiste como una probabilidad real. Cuando exista un meta-modelo válido deberá respetar el mismo `FeatureSchemaVersion`; si no coincide, no debe activarse.

## Configuración

El mantenedor muestra y permite editar el JSON completo. Incluye:

- versiones de configuración y features;
- feature flag del selector y fallback;
- decay, shrinkage y pesos venue;
- mínimos de probabilidad, edge, EV, calidad, acuerdo, score e historial;
- rango de cuota y máxima divergencia contexto/ML;
- parámetros de calibración;
- perfiles de calibración con prioridad `MarketType:Selection`, `MarketType`, `*:Selection`, `*` y finalmente identidad global;
- siete pesos del score;
- seis pesos de calidad.

Los pesos de cada bloque deben sumar exactamente 1.0. El backend valida rangos y sumas antes de guardar. `marketThresholds` permite sobrescrituras parciales con prioridad `MarketType:Selection`, `MarketType`, `*:Selection`, `*` y finalmente los valores globales.

Ejemplo:

```json
{
  "marketThresholds": {
    "TotalCorners:Under": {
      "minimumFinalProbability": 0.58,
      "minimumFinalEdge": 0.04,
      "minimumFinalExpectedValue": 0.03,
      "minimumDataQualityScore": 0.75,
      "minimumHistoricalMatches": 6,
      "minimumOdds": 1.60,
      "maximumOdds": 2.30,
      "enabled": true
    }
  }
}
```

## Idempotencia y auditoría

Cada evaluación usa SHA-256 de bot, fila de cuota, mercado, línea, versión de modelo y versión de configuración. Reprocesar el mismo candidato actualiza su evaluación; no crea un duplicado. Una configuración o modelo nuevos generan una evaluación reproducible distinta.

La tabla de evaluaciones guarda aprobados, rechazados y pendientes. Una evaluación aprobada que pierde contra otra línea con mejor score se conserva como `Rejected` con `REJECTED_LOWER_RANKED_CANDIDATE`.

## Dataset, entrenamiento y activación

1. Exportar a CSV/JSONL el resultado de [`bot_c_meta_training_dataset.sql`](../sql/bot_c_meta_training_dataset.sql). La consulta incluye aprobados y rechazados y solo fixtures finales con la estadística API-Football confirmada.
2. Entrenar sin activar:

```bash
MPLCONFIGDIR=/tmp/matplotlib-cache .venv/bin/python scripts/train_bot_c_meta_model.py \
  --input /ruta/bot-c-candidates.csv \
  --output-dir models/bot-c-meta \
  --minimum-rows 200
```

3. Revisar `*.report.json`: Brier, log loss, ECE, AUC, yield, profit, drawdown, comparación temporal contra HistGradientBoosting y `recommendedBaseCalibrationProfiles`. Un perfil mercado/lado solo se propone con al menos 80 candidatos liquidados y ambas clases; para producción se recomienda esperar una muestra mayor y estable.
4. Repetir con `--activate` solo si el holdout intacto cumple los criterios acordados. La activación reemplaza `active.json` atómicamente y el runtime recarga el artefacto por fecha de modificación.

El pipeline ordena por fecha, genera predicciones out-of-fold mediante `TimeSeriesSplit`, ajusta Platt sobre predicciones fuera de fold y conserva el último 20 % como holdout temporal intacto. La v1 binaria elimina Push/Void del target; conserva Win/HalfWin como positivo y Loss/HalfLoss como negativo. El settlement productivo continúa soportando .0/.25/.5/.75.

Feature flags:

- `BotCMetaModel:Enabled=false`: no carga artefacto.
- `allowRuleBasedFallback=false`: si el artefacto falta o tiene schema incompatible, rechaza con código estructurado.
- `selectorEnabled=false` o `marketThresholds[...].enabled=false`: no publica picks en ese alcance.
- Variables de entorno equivalentes: `BOT_C_META_MODEL_ENABLED` y `BOT_C_META_MODEL_ARTIFACT_PATH`.

## Pruebas

Ejecutar:

```bash
dotnet run --project tests/BotPickSettlement.Tests/BotPickSettlement.Tests.csproj --no-restore
dotnet build CornersPredictionApi/CornersPredictionApi.csproj --no-restore
dotnet build CornersPrediction.Web/CornersPrediction.Web.csproj --no-restore
MPLCONFIGDIR=/tmp/matplotlib-cache .venv/bin/python scripts/train_bot_c_meta_model.py --self-test
```

La suite cubre estadística ponderada, mediana, varianza, desviación, percentiles, IQR, MAD, shrinkage, hit rate, prevención explícita de leakage, aprobación, `PendingData`, los doce mercados, reproducibilidad, configuración inválida, selección por meta-modelo, schema incompatible, thresholds por mercado y settlement asiático. El self-test entrena, calibra, valida, exporta y activa en un directorio temporal sin tocar el artefacto real.

## Riesgos y supuestos

- El modelo base 2026 entrega valor esperado y MAE; la probabilidad base se aproxima con una distribución normal y una calibración logística configurable. Debe recalibrarse con evidencia temporal por mercado/lado cuando haya volumen.
- Los artefactos base actuales fueron entrenados hasta 2026-08-07. El motor rechaza cualquier candidato con fecha igual o anterior para impedir que un backfill junio/julio se presente como prueba fuera de muestra. Para ese período hacen falta predicciones walk-forward u otros artefactos entrenados únicamente con datos anteriores a cada partido.
- `MatchHistory` no tiene kickoff UTC histórico con precisión uniforme. La consulta excluye el día completo para evitar fuga, a costa de perder algunos antecedentes legítimos del mismo día.
- El baseline de liga no siempre está disponible; en ese caso se usa un baseline peer local/visita y se registra el riesgo.
- Las features cruzadas aún no están enlazadas en una misma evaluación; se marca su ausencia y baja calidad sin inventar valores.
- Un meta-modelo no se debe activar hasta contar con suficientes candidatos liquidados de **todos** los estados previos. El pipeline y el runtime están listos; el artefacto productivo se mantiene ausente intencionalmente.

## Archivos principales de esta entrega

- `CornersPrediction.Application/Automation/BotC/BotCStrategy.cs`: configuración, thresholds, manifiesto y códigos.
- `CornersPrediction.Application/Automation/BotC/BotCPickDecisionEngine.cs`: features, decisión y snapshots.
- `CornersPrediction.Application/Automation/BotC/BotCMetaModel.cs`: contrato de inferencia.
- `CornersPredictionApi/Robot/AutomatedCornersBot/BotCMetaModelPredictor.cs`: carga segura e inferencia LogisticRegression.
- `CornersPredictionApi/Robot/AutomatedCornersBot/AutomatedCornersSelectionService.cs`: integración, ranking y publicación.
- `CornersPredictionApi/sql/automated_corners_bot.sql`: configuración y evaluaciones auditables.
- `sql/bot_c_meta_training_dataset.sql`: dataset de candidatos liquidados.
- `scripts/train_bot_c_meta_model.py`: walk-forward, calibración, comparación, reporte y artefacto.
- `CornersPrediction.Web/Views/BotAutomation/Index.cshtml`: mantenedor exhaustivo.
- `tests/BotPickSettlement.Tests/Program.cs`: pruebas unitarias y de integración de dominio.
