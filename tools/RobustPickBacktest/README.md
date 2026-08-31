# RobustPickBacktest

Herramienta independiente para comparar la decisión original del bot con la decisión robusta guardada en `Shadow`. No consulta SQL, no recalcula predicciones y no modifica picks: consume un export resuelto JSON/JSONL y produce un reporte JSON reproducible.

## Ejecución

```bash
dotnet run --project tools/RobustPickBacktest/RobustPickBacktest.csproj -- \
  --input /ruta/evaluations.jsonl \
  --output /ruta/robust-backtest.json \
  --train-days 90 \
  --validation-days 30 \
  --test-days 30 \
  --step-days 30 \
  --outcome-lag-hours 8 \
  --embargo-hours 2 \
  --min-train 30 \
  --min-validation 15 \
  --bootstrap-replicates 2000 \
  --bootstrap-confidence 0.95 \
  --bootstrap-cluster fixture-day
```

Prueba interna determinista:

```bash
dotnet run --project tools/RobustPickBacktest/RobustPickBacktest.csproj -- --self-test
```

Use `--help` para ver todas las opciones. `step-days` debe ser mayor o igual que `test-days`; así una observación de test nunca aparece dos veces en el agregado.

Para comparar además un grid pequeño de políticas robustas, con gates de train y selección exclusivamente en validation:

```bash
dotnet run --project tools/RobustPickBacktest/RobustPickBacktest.csproj -- \
  --input /ruta/evaluations.jsonl \
  --output /ruta/robust-grid-backtest.json \
  --grid true \
  --grid-min-edge 0.005,0.01,0.02 \
  --grid-min-ev 0,0.01 \
  --grid-min-ev-stability 0.70,0.80 \
  --grid-min-scenario-stability 0.70,0.80 \
  --grid-min-worst-distance 0.20,0.30 \
  --grid-max-consensus-range 0.60,0.75 \
  --grid-max-coherence-gap 0.60,0.75 \
  --grid-min-calibration 0.50,0.65 \
  --grid-min-picks 30 \
  --grid-min-validation-picks 15 \
  --grid-weight-pl 1 \
  --grid-weight-yield 0.75 \
  --grid-weight-drawdown 1 \
  --grid-weight-volume 0.5 \
  --grid-weight-calibration 0.75 \
  --grid-weight-clv 0.5
```

El producto cartesiano está limitado por `--grid-max-combinations` (10.000 por defecto) para evitar una búsqueda accidentalmente enorme.

## Formato de entrada

Acepta:

- un arreglo JSON;
- un objeto `{ "evaluations": [...] }`;
- JSONL, una evaluación por línea.

Campos mínimos y opcionales:

```json
{
  "evaluationId": "9b395c4a-v3",
  "selectionKey": "automated-selection-2259",
  "fixtureId": 1623452,
  "evaluationAsOfUtc": "2026-08-21T05:03:15Z",
  "fixtureStartUtc": "2026-08-21T18:00:00Z",
  "fixtureEndUtc": "2026-08-21T20:00:00Z",
  "outcomeAvailableUtc": "2026-08-22T04:00:00Z",
  "botKey": "A",
  "marketFamily": "Corners",
  "marketType": "AwayTeamCorners",
  "scope": "Away",
  "side": "Over",
  "league": "Liga AUF Uruguaya",
  "lineValue": 3.5,
  "robustnessScore": 0.87,
  "exposureGroupKey": "fixture:1623452",
  "baselineApproved": true,
  "baselineStake": 1.0,
  "robustDecision": "ReduceStake",
  "robustRecommendedStake": 0.5,
  "odds": 2.06,
  "settlementFactor": 1.0,
  "baselineProbability": 0.6475,
  "robustProbability": 0.6010,
  "marketProbability": 0.4854,
  "binaryOutcome": 1,
  "closingOdds": 1.95,
  "closingNoVigProbability": 0.52,
  "clvLine": 0.0,
  "thresholdGridEligible": true,
  "thresholdGridStake": 0.5,
  "pointEdge": 0.1621,
  "robustEdge": 0.1156,
  "pointExpectedValue": 0.3339,
  "robustExpectedValue": 0.2381,
  "positiveEvStability": 0.91,
  "scenarioSideStability": 0.86,
  "normalizedWorstCaseDistance": 0.48,
  "normalizedConsensusRange": 0.32,
  "normalizedCoherenceGap": 0.21,
  "calibrationReliability": 0.78,
  "observedMarketValue": 13,
  "robustPredictiveCdf": {
    "distributionId": "selection-2259-robust-cdf-v3",
    "method": "DiscreteStepCdfV1",
    "asOfUtc": "2026-08-21T05:03:15Z",
    "sourceVersion": "residual-bootstrap-v3",
    "evidenceIds": ["residual-set:away-corners:2026-08-21"],
    "points": [
      { "value": 0, "cumulativeProbability": 0.01 },
      { "value": 4, "cumulativeProbability": 0.51 },
      { "value": 8, "cumulativeProbability": 0.91 },
      { "value": 14, "cumulativeProbability": 1.0 }
    ]
  }
}
```

`settlementFactor` admite `1`, `0.5`, `0`, `-0.5` y `-1`. El P/L se calcula con el mismo factor económico: los factores positivos ganan `stake × factor × (odds − 1)` y los negativos pierden `stake × factor`. Si el export ya conoce el retorno unitario exacto, puede enviar `unitProfitLoss`.

`robustRecommendedStake` es obligatorio para `Approve` y `ReduceStake`, y nunca puede superar `baselineStake`. El backtester falla ante ese dato ausente o ante un aumento: no lo sustituye por un stake favorable implícito.

`binaryOutcome` sólo admite `0` o `1` y se usa para Brier. Cuando falta, se infiere únicamente para Win completo (`1`) y Loss completo (`0`); Push, HalfWin y HalfLoss quedan fuera del Brier para no inventar una etiqueta binaria.

CLV puede enviarse como `clvOdds`, `clvProbability` y `clvLine`. Si falta `clvOdds`, se calcula `odds / closingOdds − 1`. Si falta `clvProbability`, se calcula `closingNoVigProbability − marketProbability`.

Los campos de policy desde `thresholdGridEligible` sólo son necesarios cuando se activa `--grid true`. Una fila marcada como elegible debe traer `thresholdGridStake`, robust edge/EV, ambas estabilidades, distancia, consensus range, coherence gap y calibration reliability; el proceso falla si falta alguno. `thresholdGridStake` tampoco puede superar `baselineStake`. Marcar `thresholdGridEligible: false` es un rechazo conservador: esa fila no pasa ninguna política del grid.

`scope`, `side`, `league`, `lineValue`, `calibrationReliability` y `robustnessScore` alimentan los reportes segmentados. El tool no deduce silenciosamente scope, side o liga desde `marketType`: un dato ausente queda en el bucket `MISSING`. Las bandas usan anchos configurables (`--odds-band-width`, `--line-band-width` y `--calibration-band-width`); los intervalos son `[lower, upper)`, salvo el extremo 1 de reliability. Robustness usa deciles fijos D01–D10, no cuantiles aprendidos con todo el histórico.

La CDF es completamente opcional. Para calcular CRPS se requiere `observedMarketValue` y una distribución baseline y/o robusta con `method: DiscreteStepCdfV1`. Los valores deben crecer estrictamente, la CDF no puede disminuir, debe permanecer en `[0,1]` y terminar exactamente en 1. `asOfUtc` no puede ser posterior a la evaluación. Una CDF incompleta, futura o no auditable invalida la entrada; su ausencia simplemente deja CRPS en `null`, nunca en cero.

## Regla temporal

Para cada fold:

1. Train ocupa `[validationStart − trainWindow, validationStart)` y sólo acepta outcomes disponibles antes de `validationStart − embargo`.
2. Validation ocupa `[validationStart, testStart)` y sólo acepta outcomes disponibles antes de `testStart − embargo`.
3. El test ocupa `[testStart, testEnd)` y no participa en gates, ranking ni desempates.
4. La disponibilidad efectiva es `outcomeAvailableUtc`; si no existe, se usa `fixtureEndUtc + outcome-lag-hours`.
5. Toda evaluación debe ser estrictamente anterior al kickoff.
6. El reporte conserva IDs, cutoffs y máximos `EvaluationAsOfUtc`/`OutcomeAvailableUtc` de train y validation para auditar leakage.

La comparación principal siempre evalúa la política robusta versionada y guardada en `robustDecision`/`robustRecommendedStake`. Si se activa el grid, train aplica el gate `grid-min-picks`; validation aplica su propio mínimo y selecciona la política. La ganadora se congela antes de tocar el test. El último fold se etiqueta `FinalHoldout`; los anteriores son `DevelopmentWalkForward`.

El score de validation normaliza por min–max, dentro del conjunto de políticas con muestra suficiente, seis componentes donde más alto siempre es mejor: P/L, yield, bajo drawdown, volumen, calidad de calibración (`1 − ECE`) y CLV. Luego aplica los pesos `grid-weight-*` y divide por la suma de pesos. Una población constante recibe 0,5; un componente ausente recibe 0, se registra en `UnavailableComponents` y, si su peso es positivo, la política queda inelegible. Los pesos y mínimos quedan incluidos en el reporte. Test jamás modifica el score.

Por defecto se conserva el último snapshot prepartido por `selectionKey`, evitando contar varias reevaluaciones append-only como apuestas distintas. Use `--latest-per-selection false` sólo para una auditoría explícita de snapshots.

## Métricas

Por fold y agregado:

- candidatos, picks aprobados/resueltos, stake, P/L, yield, cuota promedio y hit rate;
- maximum drawdown y racha perdedora más larga sobre el orden cronológico;
- Win, HalfWin, Push, HalfLoss y Loss;
- point/robust edge, point/robust EV y positive-EV stability promedio, cada uno con su observation count;
- concentración HHI, máximo share y cantidad de grupos de exposición;
- desacuerdos, rechazos robustos y reducciones de stake, con tasas y unidades reducidas;
- pérdidas evitadas y sus unidades, junto con victorias evitadas y su ganancia sacrificada;
- Brier, log loss, ECE, CRPS, probabilidad media, resultado medio y calibration gap;
- CLV promedio de cuota, probabilidad y línea cuando existe;
- diferencias robusto menos baseline.

El export de esta herramienta es resuelto, por lo que `resolvedPicks == approvedPicks`; no se simulan liquidaciones pendientes. El `hitRate` es `(Win + HalfWin) / (Win + HalfWin + HalfLoss + Loss)`; los push no entran al denominador. Una pérdida media cuenta como pérdida para la racha. Un push no suma ni corta la racha; cualquier resultado positivo sí la corta. Drawdown se ordena por disponibilidad del resultado y agrega P/L con la misma hora de liquidación antes de mover la curva.

Log loss recorta probabilidades sólo para estabilidad numérica, y ECE usa diez bins fijos de igual ancho. Brier, log loss, ECE y CRPS describen los picks aprobados por cada estrategia; así el objetivo de una policy cambia cuando cambia su selección. No se fabrican etiquetas binarias para resultados asiáticos parciales. CRPS integra exactamente la CDF escalonada continua por la derecha: `Σ (b−a) × (F(a)−I[y≤a])²` sobre todos los breakpoints de soporte y del valor observado.

La concentración usa `exposureGroupKey`; cuando falta, el fixture es el grupo conservador natural. Con shares de stake `sᵢ`, `exposureConcentrationHhi = Σsᵢ²` y `maximumExposureShare = max(sᵢ)`. Con stake cero las métricas quedan `null`.

`stakeReductionRate` cuenta picks baseline que robust conserva con menor stake dividido por todos los picks baseline aprobados. `robustRejectionRate` cuenta rechazos robustos sobre el mismo denominador. `stakeReductionPercentage` mide todas las unidades eliminadas —incluidos rechazos— respecto del stake baseline. Las pérdidas/victorias evitadas sólo cuentan apuestas baseline que la política robusta rechazó por completo.

## Reportes agrupados

`groups` contiene `Overall` y particiones marginales por bot, market family, exact market, scope Total/Home/Away, side Over/Under, liga, banda de cuota, banda de línea, banda de calibration reliability y decil de robustness. Cada dimensión particiona todas las filas OOS: los valores ausentes aparecen como `MISSING`, no se eliminan. Se evitan intersecciones cartesianas automáticas para no producir miles de segmentos con muestra minúscula. `thresholdGridAggregate.groups` repite la misma lectura usando la policy congelada de cada fold.

## Intervalos de confianza agrupados

El reporte incluye intervalos percentiles para P/L baseline, P/L robusto, delta de P/L, yields disponibles y drawdowns. El bootstrap conserva exactamente la misma muestra pareada para baseline y robust. Modos:

- `fixture`: remuestrea fixtures completos con todos sus picks;
- `day`: remuestrea jornadas completas;
- `fixture-day`: bootstrap jerárquico de dos etapas; primero días y después fixtures dentro de cada día seleccionado, arrastrando todos sus picks.

`fixture-day` no es una simple clave compuesta —que sería casi idéntica a fixture—. El reporte expone `dayClusterCount`, `fixtureClusterCount` y el cluster primario. `--bootstrap-replicates 0` desactiva intervalos.

La semilla se deriva con SHA-256 del hash de entrada, alcance del reporte y configuración del bootstrap. Por eso los intervalos son reproducibles entre ejecuciones con los mismos bytes y opciones. Son intervalos descriptivos del histórico agrupado; no convierten una muestra corta en evidencia productiva.

## Salida del grid walk-forward

Cada fold expone todas las políticas, su desempeño separado de train y validation, el desglose normalizado de los seis componentes, campos no disponibles, la política seleccionada y su desempeño de test. El agregado `thresholdGridAggregate` concatena únicamente decisiones fuera de muestra, usando en cada fila la política que su fold había congelado. También obtiene grupos y bootstrap propios. Si ninguna política cumple muestras y cobertura de métricas, el fold queda explícitamente sin policy; nunca se usa el test como fallback.

El reporte no contiene una hora de generación variable. `reportAsOfUtc` se deriva del último outcome disponible e `inputSha256` identifica exactamente los bytes de entrada; iguales datos y opciones producen el mismo JSON.
