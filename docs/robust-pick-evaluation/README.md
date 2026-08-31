# Robust Pick Evaluation

Esta capa responde una pregunta distinta a la del selector actual: no sólo “¿la estimación central supera el umbral?”, sino “¿el pick conserva valor cuando se consideran error histórico, desacuerdo entre predicciones, calibración, cuota, escenarios y exposición?”.

La versión inicial opera en `Shadow`. Calcula y guarda lo que habría decidido, pero la publicación, stake y liquidación originales siguen exactamente iguales. `Enforce` existe como modo explícito para una etapa posterior; no debe activarse hasta completar una validación walk-forward suficiente por mercado.

## Qué calcula

- componentes Direct, Home, Away, Home+Away, Context y Reconciled;
- distancia directa y worst-case según el lado del pick;
- rango de consenso y gap Direct vs Home+Away;
- distribución predictiva con bootstrap de residuos históricos point-in-time;
- probabilidades `Win`, `HalfWin`, `Push`, `HalfLoss` y `Loss` usando settlement asiático;
- probabilidad de mercado sin margen bilateral por método proporcional y power; si falta un lado queda explícitamente no disponible;
- fair odds, point edge/EV, robust edge/EV y estabilidad de EV positivo;
- confiabilidad de calibración y estados explícitos de evidencia;
- score de robustez y stake recomendado, que nunca aumenta en v1;
- límites de exposición por fixture, equipo y cluster correlacionado.

## Estados de evidencia

- `AppliedPositive`: evidencia válida favoreció el lado elegido.
- `AppliedNegative`: evidencia válida perjudicó el lado elegido.
- `ReviewedNeutral`: hubo evidencia suficiente y su impacto medido fue neutral.
- `InsufficientEvidence`: los datos existen, pero no alcanzan el mínimo fiable.
- `SourceUnavailable`: el proveedor falló o no estaba disponible.
- `SnapshotExpired`: el snapshot era demasiado antiguo para el cutoff.
- `NotApplicable`: el provider no corresponde a ese mercado o está deliberadamente desactivado.

`SourceUnavailable`, `SnapshotExpired` e `InsufficientEvidence` nunca se interpretan como un ajuste neutral favorable.

## Fórmulas principales

Para un pick Under, `WorstCasePrediction = max(predicciones utilizables)`; para Over se usa el mínimo. `WorstCaseDistance` conserva el signo favorable al lado elegido. La escala de error proviene del histórico temporalmente válido y toda normalización usa un epsilon explícito.

Con probabilidades asiáticas de cinco estados:

```text
EV = PWin × (odds - 1) + PHalfWin × (odds - 1) / 2
     - PHalfLoss / 2 - PLoss
FairOdds = 1 + (PHalfLoss / 2 + PLoss) / (PWin + PHalfWin / 2)
```

`PointEdge` compara la probabilidad puntual con la probabilidad de mercado conservadora. `RobustEdge` y `RobustEV` usan el cuantil adverso de escenarios; `PositiveEvStability` es la fracción ponderada de escenarios cuyo EV permanece positivo. Push no se trata como ganancia ni pérdida.

La confiabilidad de calibración combina tamaño efectivo, especificidad, recencia y error. Si el calibrador no trae límites, se deriva un intervalo Wilson determinista usando `EffectiveN`; el método, nivel de confianza y prior se persisten y muestran para auditoría. Un intervalo faltante o una muestra pequeña reduce confiabilidad, nunca la mejora.

## Reason codes

Los códigos se guardan como valores estables y la UI puede traducirlos sin perder auditoría:

- `ROBUST_EDGE_BELOW_MINIMUM`
- `ROBUST_EV_NOT_POSITIVE`
- `POSITIVE_EV_STABILITY_TOO_LOW`
- `CALIBRATION_RELIABILITY_TOO_LOW`
- `RESIDUAL_SAMPLE_TOO_SMALL`
- `WORST_CASE_DISTANCE_TOO_SMALL`
- `CONSENSUS_RANGE_TOO_LARGE`
- `COHERENCE_GAP_TOO_LARGE`
- `SIDE_DISAGREEMENT`
- `ODDS_TOO_OLD`
- `MARKET_PRICE_UNAVAILABLE`
- `NO_VIG_UNAVAILABLE`
- `LINEUP_SCENARIO_UNSTABLE`
- `DATA_QUALITY_TOO_LOW`
- `MODEL_TRAINED_AFTER_FIXTURE`
- `LOOKAHEAD_DATA_DETECTED`
- `EXPOSURE_LIMIT_EXCEEDED`
- `CORRELATED_EXPOSURE_LIMIT_EXCEEDED`
- `MARKET_AUTOMATION_NAME_MISMATCH`
- `INTELLIGENCE_SOURCE_UNAVAILABLE`
- `SNAPSHOT_EXPIRED`
- `ERROR_SCALE_UNAVAILABLE`
- `POINT_EDGE_BELOW_MINIMUM`
- `POINT_EV_BELOW_MINIMUM`
- `EVIDENCE_INSUFFICIENT`

## Documentos relacionados

- [Arquitectura](ARCHITECTURE.md)
- [Reglas contra data leakage](DATA_LEAKAGE_RULES.md)
- [Configuración](CONFIGURATION.md)
- [Backtesting](BACKTESTING.md)
- [Operaciones](OPERATIONS.md)
- [Plan y auditoría](IMPLEMENTATION_PLAN.md)

## Limitación principal

Los selectores 2026 y Bot G conservan candidatos rechazados y aprobados. Parte del histórico legacy sólo contiene picks seleccionados; sus residuos se marcan `SelectedPicksOnly`, reducen confiabilidad y pueden sufrir selection bias. No se inventan candidatos, timestamps, snapshots de cuota ni metadata de entrenamiento faltante.
