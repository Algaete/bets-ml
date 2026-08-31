# Backtesting walk-forward

`tools/RobustPickBacktest` compara la decisión original del bot con la propuesta guardada por Robust Shadow. Es una herramienta offline: lee JSON/JSONL resuelto, no consulta SQL, no recalcula snapshots y no cambia picks ni liquidaciones.

## Protocolo temporal

Cada fold contiene tres ventanas disjuntas:

1. Train: `[validationStart − trainingWindow, validationStart)`.
2. Validation: `[validationStart, testStart)`.
3. Test: `[testStart, testEnd)`.

Train sólo admite resultados disponibles hasta `validationStart − embargo`. Validation sólo admite resultados disponibles hasta `testStart − embargo`. La disponibilidad es `OutcomeAvailableUtc` o, cuando el export no la conoce, `FixtureEndUtc + OutcomeAvailabilityLag`. Toda evaluación debe ser estrictamente prepartido.

Train aplica gates de soporte para el grid. Validation selecciona thresholds. La policy queda congelada antes de evaluar test; ningún resultado, probabilidad, CLV o métrica de test participa en ranking o desempates. El último fold se etiqueta `FinalHoldout`, y los anteriores `DevelopmentWalkForward`.

El reporte conserva por fold:

- límites de las tres ventanas;
- cutoffs de disponibilidad de train y validation;
- IDs incluidos;
- filas excluidas por outcome tardío;
- máximos `EvaluationAsOfUtc` y `OutcomeAvailableUtc` utilizados;
- resultado de la comprobación de integridad temporal.

## Poblaciones comparadas

- Baseline: decisión y stake originales.
- Robust Shadow: `Approve`, `Reject`, `ReduceStake` o `ManualReview`, usando el stake recomendado.
- Threshold grid: policy elegida exclusivamente en validation y aplicada después al test del fold.

El formato actual acepta únicamente evaluaciones resueltas. Por esa razón, dentro de cada estrategia `ResolvedPicks` coincide con `ApprovedPicks`. Pendientes deben liquidarse o excluirse del export; el tool no inventa resultados.

## Segmentación

El agregado OOS expone `Overall` y agrupaciones marginales por:

- bot;
- market family;
- exact market;
- scope Total/Home/Away;
- side Over/Under;
- liga;
- banda de odds;
- banda de línea;
- banda de calibration reliability;
- decil fijo de robustness.

Scope, side y liga se leen del snapshot exportado: no se deducen desde nombres de mercado. Los null entran al bucket `MISSING`. Odds, línea y reliability usan anchos configurables y bandas `[lower, upper)`; reliability incluye 1 en el último intervalo. Robustness usa D01–D10 sobre cortes fijos de 0,1, con 1 en D10. No se calculan cuantiles usando el test.

Cada dimensión particiona todo el agregado OOS. No se construye por defecto el producto cartesiano entre dimensiones porque produciría segmentos minúsculos y selection bias. El agregado del grid produce las mismas agrupaciones usando la policy congelada propia de cada fold.

## Métricas

Por estrategia se reportan:

- candidatos, picks aprobados/resueltos y Win/HalfWin/Push/HalfLoss/Loss;
- stake, P/L, yield, hit rate y odds promedio;
- maximum drawdown y longest losing streak;
- point edge, robust edge, point EV, robust EV y positive-EV stability promedios, con conteos;
- Brier, log loss, ECE y CRPS cuando existe CDF válida;
- CLV de odds, probabilidad y línea cuando existe;
- concentración HHI y máximo share de exposición.

Entre estrategias se reportan desacuerdos, baseline-only/robust-only, reducciones, rechazos, unidades eliminadas, pérdidas evitadas, victorias sacrificadas, delta de P/L/yield y delta de drawdown.

Fórmulas principales:

```text
Yield = ProfitLoss / TotalStake
HitRate = (Win + HalfWin) / (Win + HalfWin + HalfLoss + Loss)
HHI = sum((StakeExposureGroup / TotalStake)^2)
StakeReductionPercentage = TotalStakeReduction / BaselineApprovedStake
```

Push no entra al hit rate ni suma/corta la racha. HalfLoss/Loss incrementan la racha; HalfWin/Win la cortan. Drawdown sigue el orden de disponibilidad del resultado y agrega liquidaciones con la misma hora antes de mover equity.

Brier, log loss, ECE y CRPS se calculan sobre picks aprobados por la estrategia que tengan el target correspondiente. Push y resultados asiáticos parciales no reciben una etiqueta binaria artificial. ECE usa diez bins fijos. El clipping de log loss sólo evita `log(0)`.

La concentración usa `ExposureGroupKey`; si falta, usa fixture. Con stake cero HHI y máximo share son null.

## CRPS auditable

CRPS sólo se calcula cuando el export contiene `ObservedMarketValue` y una `BaselinePredictiveCdf` o `RobustPredictiveCdf`. El formato soportado es `DiscreteStepCdfV1`, una CDF escalonada continua por la derecha con:

- ID de distribución;
- versión/fuente opcional;
- `AsOfUtc` no posterior a la evaluación;
- IDs de evidencia;
- soporte estrictamente creciente;
- probabilidades acumuladas no decrecientes en `[0,1]`;
- masa final exactamente 1.

Para breakpoints consecutivos `[a,b)`:

```text
CRPS += (b - a) * (F(a) - I[ObservedMarketValue <= a])^2
```

Una CDF ausente produce CRPS null. Una CDF presente pero futura, incompleta o inválida hace fallar la entrada; nunca se transforma en evidencia neutral ni en score cero.

## Bootstrap agrupado

Los intervalos percentiles son pareados: baseline y robust reciben exactamente la misma muestra. Modos:

- `Fixture`: remuestrea fixtures completos.
- `Day`: remuestrea días completos.
- `Fixture-Day`: bootstrap jerárquico; remuestrea días y, dentro de cada día elegido, fixtures completos.

`Fixture-Day` no concatena IDs como una clave compuesta. Esa falsa implementación sería prácticamente fixture-only y no capturaría shocks de jornada. El reporte incluye cantidad de clusters de día, fixture y cluster primario.

La semilla se deriva mediante SHA-256 del hash de entrada, scope, modo, réplicas, confidence level y versión del algoritmo. Iguales bytes y opciones producen el mismo reporte. Los intervalos cubren P/L baseline/robust/delta, yields disponibles y drawdowns; son evidencia descriptiva, no autorización automática para Enforce.

## Selección multicriterio de thresholds

El grid permite variar robust edge/EV, positive-EV stability, scenario-side stability, normalized worst-case distance, consensus range, coherence gap y calibration reliability. Una policy debe cumplir mínimos configurables tanto en train como en validation.

Sólo con validation se calculan seis componentes:

- P/L: mayor es mejor;
- yield: mayor es mejor;
- drawdown: menor es mejor;
- volumen aprobado: mayor es mejor;
- calibración: `1 − ECE`, mayor es mejor;
- CLV de odds: mayor es mejor.

Cada componente se normaliza por min–max entre policies con muestra suficiente. Un componente constante vale 0,5. El score es:

```text
WeightedScore = sum(Weight_i * NormalizedComponent_i) / sum(Weight_i)
```

Los pesos deben ser no negativos y al menos uno positivo. Si una métrica con peso positivo no está disponible, aparece en `UnavailableComponents` y esa policy queda fuera de selección. Esto impide que ausencia de calibración o CLV resulte favorable. Los desempates usan P/L, yield, menor drawdown, volumen, ECE, CLV y finalmente `StableKey`, siempre con datos de validation.

Todas las policies se incluyen en el fold report junto con métricas separadas de train/validation y el desglose de objetivo. Si ninguna cumple muestra y cobertura, el fold queda sin policy; test nunca se usa como fallback.

## Uso y activación

Ejecutar la prueba determinista:

```bash
dotnet run --project tools/RobustPickBacktest/RobustPickBacktest.csproj -- --self-test
```

Ver opciones e input completo:

```bash
dotnet run --project tools/RobustPickBacktest/RobustPickBacktest.csproj -- --help
```

El backtest no activa Enforce. Una revisión humana debe exigir muestra global y por exact market, final holdout no materialmente peor, drawdown aceptable, calibración y CLV con cobertura suficiente, cero leakage y estabilidad razonable frente a pequeños cambios de thresholds. Un único pick ganado o perdido no valida el modelo.
