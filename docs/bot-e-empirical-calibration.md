# Bot E · Calibración empírica walk-forward

## Objetivo

Bot E es un experimento de calibración sobre el Pick Selector 2026. No intenta predecir nuevamente cuántos goles, córners, tiros o tiros al arco habrá. Conserva la predicción puntual y las validaciones de Bot C, pero pregunta algo distinto antes de publicar una apuesta:

> Cuando Bot C asignó una probabilidad parecida a candidatos anteriores, ¿qué retorno asiático produjeron realmente y cuánto debemos confiar hoy en esa evidencia?

El problema que busca medir es la sobreconfianza. Un modelo puede predecir correctamente el orden relativo de dos oportunidades y aun así informar probabilidades demasiado altas. En ese caso el edge y el EV calculados desde esa probabilidad también quedan inflados. Bot E reduce ese riesgo mediante:

- resultados reales guardados localmente en `MatchHistory`;
- evaluaciones anteriores de Bot C, incluyendo `Approved` y `Rejected`;
- una ejecución walk-forward estricta;
- evidencia jerárquica por mercado y lado;
- deduplicación por fixture;
- retorno asiático completo, medio o push;
- ponderación por similitud, recencia y calidad;
- shrinkage hacia la probabilidad no-vig;
- una penalización conservadora por incertidumbre.

Bot E está implementado como `E2026`, admite `CORNERS`, `GOALS`, `SHOTS` y `SOG`, y publica como máximo la mejor oportunidad aprobada por partido y familia de mercado, igual que los otros selectores 2026.

## Diferencia entre Bot C, Bot D y Bot E

| Bot | Pregunta principal | Señal distintiva | Cambia el valor ML | Probabilidad final |
|---|---|---|---|---|
| Bot C | ¿La predicción ML, el contexto, la línea y la cuota justifican apostar? | Modelos 2026, historial, contexto, hit rate, calidad, edge y EV | No | Meta-probabilidad si existe artefacto compatible; en caso contrario, probabilidad base calibrada |
| Bot D | ¿La diferencia de nivel entre equipos refuerza o contradice el candidato? | Elo temporal, enfrentamientos directos y rivales comunes | No | Probabilidad de C con ajuste acotado por gap en fallback |
| Bot E | ¿La probabilidad previa fue confiable en situaciones comparables ya resueltas? | Retornos asiáticos observados, jerarquía, recencia, calidad y no-vig | No | Probabilidad equivalente empírica conservadora |

Bot D agrega una señal deportiva. Bot E no agrega una señal de juego: calibra la confianza económica en la señal existente.

Bot D y Bot E son experimentos aislados. La versión 1 rechaza una configuración que active simultáneamente `teamStrength.enabled=true` y `empiricalCalibration.enabled=true`, porque eso impediría atribuir el cambio de rendimiento a una sola hipótesis.

## Componentes y datos

| Responsabilidad | Implementación |
|---|---|
| Configuración y validación | `BotEEmpiricalCalibrationConfiguration` dentro de `BotCStrategyConfiguration` |
| Cálculo puro | `BotEEmpiricalCalibrationCalculator` |
| Decisión final | `BotCPickDecisionEngine` |
| Fuente de evaluaciones | `AutomatedBotPickEvaluations`, por defecto `BotKey=C2026` |
| Resultado real | `MatchHistory` |
| Carga por lote | `SqlAutomationRepository.GetBotECalibrationHistoryAsync` |
| Orquestación | `AutomatedCornersSelectionService` |
| Configuración persistida | `AutomatedBotDefinitions.StrategyConfigurationJson` |
| Auditoría del candidato | `FeatureSnapshotJson.empiricalCalibration` |
| Pick publicado | `AutomatedCornerBetSelections` |
| Liquidación | Flujo común desde `MatchHistory` |

La evidencia se carga una vez por fuente y lote hasta la fecha máxima del lote. El motor vuelve a filtrarla candidato por candidato; por lo tanto, cargar una fila en memoria no significa que pueda usarse para todos los partidos del lote.

## Cómo se obtiene una observación etiquetada

La fuente predeterminada es Bot C. Se consultan sus evaluaciones con:

- decisión `Approved` o `Rejected`;
- selección `Over` o `Under`;
- cuota válida;
- `FinalProbability`, probabilidad no-vig y calidad válidas;
- `BaseModelTrainedThroughUtc` persistido y partido estrictamente posterior a ese corte;
- fecha anterior al límite solicitado al repositorio.

No se usan únicamente picks publicados. Incluir rechazados reduce el sesgo de entrenar un calibrador solo con las decisiones que ya superaron los thresholds.

Cada evaluación se enlaza a un resultado local siguiendo esta prioridad:

1. `PublishedSelectionId -> AutomatedCornerBetSelections.MatchHistoryId`;
2. `ApiFootballFixtureId` directo;
3. equipos canónicos y fecha dentro de `±1` día.

El tercer mecanismo solo se acepta cuando el mejor enlace es único. Las coincidencias ambiguas quedan fuera de la evidencia.

Además, se exige:

- `FixtureStatus` en `FT`, `AET` o `PEN`;
- `ApiFootballFixtureId` local disponible;
- flag de disponibilidad confirmado para el mercado;
- estadística requerida distinta de `NULL`.

El valor real se mapea por `MarketType`:

- goles: local, visitante o suma;
- córners: local, visitante o suma;
- tiros: local, visitante o suma;
- tiros al arco: local, visitante o suma.

Un `NULL` nunca se transforma en cero.

## Flujo walk-forward exacto

Para un candidato con fecha `AsOfDateUtc`:

```text
Cuota + predicción 2026
  -> validaciones y protección TrainedThrough de Bot C
  -> probabilidad que usaría C antes de Bot E
  -> cargar evaluaciones C con resultado local confirmado
  -> conservar solo outcome.MatchDateUtc + lag < AsOfDateUtc
  -> filtrar lado y construir niveles global/familia/mercado
  -> una observación independiente por fixture
  -> ponderar retorno asiático por similitud, recencia y calidad
  -> shrinkage global hacia no-vig
  -> shrinkage familia hacia global
  -> shrinkage mercado exacto hacia familia
  -> límite conservador por error estándar
  -> probabilidad equivalente + edge + EV
  -> thresholds comunes y thresholds de calibración
  -> Approved / Rejected / PendingData / Invalid
```

La condición temporal es estricta:

```text
MatchDateUtc_observación + OutcomeAvailabilityLagHours < AsOfDateUtc_candidato
```

Con el valor actual de ocho horas:

- un partido ocurrido nueve horas antes puede aportar evidencia;
- uno ocurrido exactamente ocho horas antes no puede;
- el mismo partido candidato y cualquier partido posterior quedan excluidos.

El lag no pretende afirmar que todos los partidos duran ocho horas. Es un margen conservador que compensa que `MatchHistory` no conserva el timestamp histórico exacto en que cada resultado quedó disponible. También hace que la decisión sea independiente del orden en que se procesen los partidos del backfill.

## Jerarquía de evidencia

Primero se filtra por el mismo lado de la selección candidata, `Over` o `Under`. Después se forman tres niveles:

1. `GlobalSide`: todos los mercados con el mismo lado.
2. `MarketFamilyAndSide`: misma familia y lado; por ejemplo, todos los mercados de córners `Under`.
3. `ExactMarketAndSide`: mismo `MarketType` y lado; por ejemplo, `HomeTeamCorners:Under`.

El nivel global requiere al menos `MinimumObservations` fixtures independientes. Si no alcanza ese mínimo, el calibrador no está disponible y el candidato se rechaza con `REJECTED_CALIBRATION_SAMPLE_LOW`.

Si la familia alcanza `MinimumObservations`, su estimación se calcula usando la posterior global como prior. Si el mercado exacto alcanza `MinimumExactMarketObservations`, se calcula usando la posterior de familia —o global si la familia aún no estaba disponible— como prior.

No se mezclan directamente porcentajes de tres niveles. Cada nivel aplica shrinkage sobre el nivel más general ya calculado. Esto evita que doce observaciones exactas eliminen de golpe toda la información acumulada en la familia o en el mercado.

## Deduplicación por fixture

Un partido puede producir muchas evaluaciones:

- diferentes líneas;
- local, visitante y total;
- Pinnacle y Betano;
- candidatos aprobados y candidatos que perdieron frente a una línea con mayor score.

Esas filas no son resultados independientes. Antes de estimar cada nivel, Bot E agrupa por `FixtureId` y conserva una sola evaluación:

1. prioriza el `MarketType` exacto del candidato;
2. elige la probabilidad fuente más cercana a la probabilidad candidata;
3. desempata por `EvaluationId`.

Por eso `ExactMarketRows` puede ser muy superior a `ExactMarketFixtures`. Los thresholds, la muestra efectiva y la reliability se basan en fixtures deduplicados, no en la cantidad de líneas que ofreció una casa de apuestas.

El cálculo es determinista aunque las filas lleguen en distinto orden.

## Ponderación de observaciones

Para cada fixture seleccionado se calcula:

```text
distance_i   = (SourceProbability_i - CandidateSourceProbability) / ProbabilityBandwidth
similarity_i = exp(-0.5 × distance_i²)

recency_i = 0.5 ^ (AgeDays_i / RecencyHalfLifeDays)

quality_i = QualityWeightFloor
            + (1 - QualityWeightFloor) × DataQualityScore_i

weight_i = similarity_i × recency_i × quality_i
```

Consecuencias:

- una probabilidad histórica muy distinta aporta poco;
- una observación pierde la mitad de su peso cada `RecencyHalfLifeDays`;
- baja calidad reduce el peso, pero no por debajo del piso configurado;
- una fila válida puede quedar numéricamente fuera si su peso colapsa a casi cero.

La muestra efectiva usa Kish:

```text
n_eff = (Σ weight_i)² / Σ(weight_i²)
```

`n_eff` puede ser menor que la cantidad de fixtures cuando unos pocos concentran la mayor parte del peso.

## Retorno asiático exacto

Bot E reutiliza la misma calculadora que liquida los Bot Picks. No convierte `HalfWin` en una ganancia completa ni `HalfLoss` en una pérdida completa.

Una línea `.25` se divide en:

```text
Línea x.25 -> 0.5u en x.00 + 0.5u en x.50
```

Una línea `.75` se divide en:

```text
Línea x.75 -> 0.5u en x.50 + 0.5u en (x+1).00
```

Cada componente obtiene factor `+1`, `0` o `-1`. El factor final es el promedio de sus componentes.

Para una apuesta de 1u y cuota decimal `O`:

```text
si factor > 0: retorno_neto = (O - 1) × factor
si factor < 0: retorno_neto = factor
si factor = 0: retorno_neto = 0
```

Ejemplos con cuota `1.80`:

| Pick | Resultado real | Factor | Retorno neto |
|---|---:|---:|---:|
| Under 3.25 | 3 | +0.5 | +0.40u |
| Under 3.75 | 4 | -0.5 | -0.50u |
| Over 3.00 | 3 | 0 | 0u |
| Over 2.50 | 3 | +1 | +0.80u |

La media empírica ponderada es:

```text
WeightedAsianReturn = Σ(weight_i × return_i) / Σ(weight_i)
```

Para evaluar un candidato actual, todos los outcomes históricos se valorizan con la cuota actual del candidato. De esa forma la estimación responde a la pregunta económica correcta: qué retorno produciría hoy esa distribución de resultados al precio disponible hoy.

## No-vig, shrinkage y posterior

La probabilidad de mercado se obtiene quitando el margen cuando existen ambas cuotas:

```text
p_over_no_vig  = (1 / cuota_over)  / ((1 / cuota_over) + (1 / cuota_under))
p_under_no_vig = (1 / cuota_under) / ((1 / cuota_over) + (1 / cuota_under))
```

La configuración productiva exige no-vig. Si falta la cuota opuesta, Bot E no inventa una referencia.

La probabilidad no-vig se transforma en retorno prior a la cuota ofrecida:

```text
MarketAnchorExpectedValue = p_no_vig × cuota_candidata - 1
```

En cada nivel de la jerarquía:

```text
PosteriorExpectedValue =
    (Σweight × WeightedAsianReturn + PriorStrength × PriorExpectedValue)
    / (Σweight + PriorStrength)
```

Los strengths son distintos para global, familia y mercado exacto. Cuanto mayor sea el strength, mayor será la contracción hacia el nivel más general.

## Incertidumbre y probabilidad equivalente

Bot E calcula la varianza ponderada de los retornos y un error estándar:

```text
SE = sqrt(weightedReturnVariance / max(1, n_eff + PriorStrength))
```

Después usa un límite conservador:

```text
ConservativeExpectedValue =
    clamp(PosteriorExpectedValue - ConfidenceZScore × SE,
          -1,
          cuota - 1)
```

Finalmente lo expresa como probabilidad equivalente:

```text
ConservativeEquivalentProbability =
    (ConservativeExpectedValue + 1) / cuota
```

Esta probabilidad se define para que:

```text
ConservativeEquivalentProbability × cuota - 1
= ConservativeExpectedValue
```

En una línea `.5`, sin posibilidad de push, puede interpretarse como una probabilidad de acierto calibrada. En líneas enteras, `.25` o `.75`, no es literalmente `P(Win)`: resume en una sola escala económica los full wins, half wins, pushes, half losses y full losses.

Ejemplos anteriores a cuota `1.80`:

- retorno `+0.40` -> probabilidad equivalente `1.40 / 1.80 = 77.78%`;
- retorno `-0.50` -> probabilidad equivalente `0.50 / 1.80 = 27.78%`.

El edge y el EV final de Bot E son:

```text
FinalEdge = ConservativeEquivalentProbability - p_no_vig
FinalExpectedValue = ConservativeExpectedValue
```

No debe reemplazarse `FinalExpectedValue` por una liquidación binaria simplificada; hacerlo perdería la mitad ganada o perdida de las líneas asiáticas.

## Reliability y reglas de aprobación

La reliability combina muestra efectiva, el objetivo de evidencia y calidad promedio:

```text
reliability =
    n_eff / (n_eff + TargetEffectiveObservations)
    × AverageDataQuality
```

El candidato exige primero `n_eff >= MinimumEffectiveObservations` y luego debe superar
`MinimumReliability`. La varianza posterior incorpora también la incertidumbre del prior no-vig;
por eso una racha de resultados idénticos no puede declarar certeza con una muestra pequeña.
Además conserva las reglas efectivas del Pick Selector E:

- probabilidad final mínima `0.54`;
- edge final mínimo `0.025`;
- EV final mínimo `0.02`;
- calidad mínima `0.65`;
- acuerdo contextual mínimo `0.65`;
- score fallback mínimo `0.58` cuando no existe un metamodelo compatible;
- al menos seis partidos históricos por equipo;
- cuota entre `1.60` y `2.20`;
- calibrador disponible;
- muestra efectiva mínima de `8` fixtures;
- reliability mínima `0.15`.

Por eso un calibrador disponible no garantiza un pick. El candidato todavía puede fallar por edge, EV, calidad, contexto, cuota, historial o score.

## Configuración productiva inicial

```json
{
  "configurationVersion": "bot-e-empirical-calibration-1.0.2",
  "featureSchemaVersion": "bot-c-features-1.0.0",
  "empiricalCalibration": {
    "enabled": true,
    "version": "bot-e-empirical-calibration-1.0.2",
    "sourceBotKey": "C2026",
    "minimumObservations": 20,
    "minimumExactMarketObservations": 12,
    "minimumEffectiveObservations": 8,
    "targetEffectiveObservations": 80,
    "outcomeAvailabilityLagHours": 8,
    "probabilityBandwidth": 0.10,
    "globalPriorStrength": 40,
    "familyPriorStrength": 80,
    "exactMarketPriorStrength": 40,
    "recencyHalfLifeDays": 45,
    "qualityWeightFloor": 0.50,
    "minimumReliability": 0.15,
    "confidenceZScore": 0.50,
    "requireSameBaseModelVersion": false,
    "requireNoVigProbability": true
  }
}
```

| Campo | Función |
|---|---|
| `enabled` | Activa el calibrador. En C y D queda desactivado. |
| `version` | Versión auditable del algoritmo/configuración. |
| `sourceBotKey` | Bot cuyas evaluaciones etiquetadas alimentan el calibrador. |
| `minimumObservations` | Mínimo de fixtures globales; también habilita el nivel familia. |
| `minimumExactMarketObservations` | Mínimo de fixtures para activar mercado+lado. |
| `minimumEffectiveObservations` | Mínimo de muestra efectiva de Kish; si los pesos colapsan, el tier no puede aprobar. |
| `targetEffectiveObservations` | Objetivo que contrae directamente la reliability cuando todavía hay poca evidencia efectiva. |
| `outcomeAvailabilityLagHours` | Margen temporal exigido entre outcome y candidato. |
| `probabilityBandwidth` | Ancho del kernel de similitud de probabilidades. Menor valor hace la evidencia más local. |
| `globalPriorStrength` | Contracción del nivel global hacia no-vig. |
| `familyPriorStrength` | Contracción de familia hacia la posterior global. |
| `exactMarketPriorStrength` | Contracción del mercado exacto hacia la posterior anterior. |
| `recencyHalfLifeDays` | Días necesarios para reducir a la mitad el peso temporal. |
| `qualityWeightFloor` | Peso mínimo relativo aun con calidad cero. |
| `minimumReliability` | Mínimo para permitir aprobación. |
| `confidenceZScore` | Penalización aplicada al error estándar. |
| `requireSameBaseModelVersion` | Si es `true`, excluye observaciones de otra versión de modelo base. |
| `requireNoVigProbability` | Si es `true`, exige cuotas de ambos lados. |

`requireSameBaseModelVersion=false` aumenta cobertura, pero mezcla versiones que podrían tener distinta calibración. Debe activarse cuando haya volumen suficiente por versión.

Los campos rápidos de edge, EV, cuota y divergencia configurados en el mantenedor sobrescriben sus equivalentes del JSON efectivo, igual que en Bot C y D.

## Snapshot y trazabilidad

Cada evaluación guarda en `FeatureSnapshotJson.empiricalCalibration`:

- `enabled`, `version` y `sourceBot`;
- probabilidad previa a la calibración;
- filas recibidas y filas temporalmente aceptadas;
- filas y fixtures por nivel exacto, familia y global;
- fixtures finalmente seleccionados;
- `EvidenceTier`;
- `EffectiveSampleSize`;
- `WeightedAsianReturn`;
- probabilidad y EV ancla de mercado;
- EV posterior;
- error estándar;
- EV conservador;
- probabilidad equivalente conservadora;
- reliability;
- Brier de la probabilidad fuente y del mercado sobre la evidencia elegida;
- `EvidenceHash`;
- conteos `Win`, `HalfWin`, `Push`, `HalfLoss` y `Loss` presentes;
- riesgos de calibración.

`EvidenceHash` es SHA-256 de los `EvaluationId` seleccionados y permite comprobar que dos ejecuciones usaron las mismas filas. No es un checksum del contenido del resultado: si se corrige una estadística de `MatchHistory` manteniendo los mismos IDs, el hash puede permanecer igual aunque cambie el retorno calculado.

La decisión usa `DecisionEngineType=EmpiricalMarketCalibration` cuando la evidencia está disponible. Los códigos principales son:

- `APPROVED_EMPIRICAL_MARKET_CALIBRATION`;
- `REJECTED_CALIBRATION_SAMPLE_LOW`;
- `REJECTED_CALIBRATION_RELIABILITY_LOW`;
- riesgo `EmpiricalCalibrationUnavailable`;
- riesgo `CalibrationUsedBroaderEvidenceTier`;
- riesgo `LowEffectiveCalibrationSample`;
- riesgo `LowCalibrationReliability`.

Solo la mejor evaluación `Approved` se publica. Las demás permanecen auditables en `AutomatedBotPickEvaluations`.

## Cómo ejecutar Bot E desde la web

### Preparar la evidencia

Bot E depende de evaluaciones de la fuente `C2026` y de resultados locales. En una base nueva:

1. Sincronizar partidos y resultados hacia `MatchHistory`.
2. Ejecutar primero un backfill de Bot C para crear evaluaciones `Approved` y `Rejected`.
3. Liquidar o reconciliar resultados disponibles. El calibrador lee directamente `MatchHistory`; un pick C no necesita haber sido publicado, pero su evaluación sí debe existir.

### Crear la ejecución

1. Abrir **Bots y procesos**.
2. En **Mantenedor de bots**, comprobar que `E2026 · Bot E · Calibración empírica` esté habilitado.
3. Desplegar su detalle para revisar flujo, configuración efectiva, features, reglas y protecciones temporales.
4. Volver a **Procesos**.
5. Escribir un nombre identificable, por ejemplo `Backfill Bot E 08-13 agosto`.
6. Elegir fechas. Para el experimento actual, usar `Desde=2026-08-08`; no usar el acceso rápido del 19 de junio.
7. Elegir **Histórico: crea picks pendientes**.
8. Marcar únicamente **Bot E · Calibración empírica** si se quiere aislar el experimento.
9. Seleccionar `CORNERS`, `GOALS`, `SHOTS` y/o `SOG`.
10. Definir partidos por lote y reintentos.
11. Pulsar **Guardar y ejecutar**.

La tabla de ejecuciones muestra estado, avance, picks seleccionados, inserciones, actualizaciones y errores. Una ejecución con cero picks no implica necesariamente un error: puede significar que no había veinte fixtures previos, que la reliability fue baja o que la contracción eliminó el edge aparente.

### Revisar y liquidar

1. Abrir **Bot Picks**.
2. Elegir el mercado horizontal: Córners, Goles, Tiros o Tiros al arco.
3. Seleccionar la pestaña **Bot E · Calibración empírica** para ver su resumen financiero aislado.
4. La tabla operativa puede seguir mostrando todos los bots; la pestaña controla el resumen, no elimina la visibilidad necesaria para liquidar.
5. Pulsar **Sincronizar y liquidar todo** para consultar `MatchHistory` y resolver todos los bots y mercados disponibles.
6. Usar liquidación manual solo cuando el resultado local no existe o no está confirmado.

## Comparación C/D/E reproducible

Con API local activa:

```bash
CORNERS_INTERNAL_API_KEY='<internal-key>' \
node scripts/compare-bot-c-d-e-backtest.mjs \
  --from=2026-08-08 \
  --to=2026-08-13 \
  --e-configuration-version=bot-e-empirical-calibration-1.0.2
```

También se puede definir `CORNERS_API_URL`; el valor predeterminado es `http://localhost:5070`.
Los filtros `--c-configuration-version`, `--d-configuration-version` y
`--e-configuration-version` evitan mezclar iteraciones del mismo bot. El bloque
`byExperimentVersion` conserva además el desglose por configuración y `AutomationVersion`.

El reporte entrega:

- resumen global de C, D y E;
- desglose por familia;
- desglose por alcance local, visitante o total;
- comparaciones pareadas `C vs D`, `C vs E` y `D vs E`;
- cambios de lado y línea;
- picks idénticos;
- picks exclusivos de cada bot;
- cobertura de liquidación;
- P/L y yield con retorno asiático real;
- tier de evidencia, fixtures, `n_eff`, reliability y EV conservador de E.

## Cómo interpretar el experimento

No basta con comparar accuracy global. Bot E puede apostar mucho menos que C y escoger partidos diferentes.

La lectura recomendada es:

1. **Cobertura:** cuántos picks produjo y cuántos están liquidados.
2. **Pareados:** cómo rindieron C y E en el mismo partido y mercado.
3. **Selectividad:** rendimiento de `onlyBotE` y `onlyBotC`.
4. **Yield:** P/L dividido por stake realmente liquidado; respeta medias ganancias y pérdidas.
5. **Calibración:** revisar reliability, `n_eff` y tier antes de interpretar el resultado.
6. **Estabilidad:** repetir por fechas y mercados; no ajustar parámetros con un único bloque corto.

`Accuracy` excluye `Pending`, `Push` y `Void`. `HalfWin` y `HalfLoss` se reflejan correctamente en P/L/yield aunque el estado persistido sea `Won` o `Lost`. Por eso yield es más informativo que accuracy para líneas asiáticas.

Los Brier guardados en el snapshot describen la evidencia histórica seleccionada; no sustituyen un Brier walk-forward calculado sobre candidatos futuros. Para promover Bot E a estrategia preferida debe evaluarse en ventanas posteriores que no se usaron para ajustar parámetros.

## Limitaciones actuales

- Los modelos base fueron entrenados hasta `2026-08-07`. Una evaluación realmente fuera de muestra comienza el `2026-08-08`.
- Las evaluaciones históricas que no tengan `BaseModelTrainedThroughUtc`, o cuya fecha no sea posterior al corte, se excluyen de la evidencia aunque alguna versión antigua las hubiera marcado `Approved` o `Rejected`.
- Bot E no puede generar evidencia válida anterior al primer día fuera de muestra. En los primeros días del backfill es esperable que quede sin calibrador.
- La ventana disponible inicialmente es muy corta. Córners puede acumular antes; tiros y tiros al arco probablemente usarán tier familia o global durante más tiempo.
- `MatchHistory` no conserva el instante histórico exacto de disponibilidad del resultado. El lag fijo de ocho horas es una aproximación conservadora.
- La fuente incluye Approved y Rejected, pero solo evaluaciones donde Bot C pudo formar una selección válida; no representa todas las líneas teóricas posibles.
- `requireSameBaseModelVersion=false` mezcla versiones para ganar muestra.
- La calidad del enlace depende de fixture ID, selección ya vinculada o nombres canónicos. Un enlace ambiguo se descarta, reduciendo cobertura.
- Resultados insertados o corregidos tardíamente pueden aumentar o modificar la evidencia al repetir el backfill. El hash incluye el contenido completo de cada observación, por lo que una corrección crea una evaluación auditable distinta aunque conserve los mismos IDs.
- Bot E v1 calibra el lado elegido previamente por la predicción base; todavía no compara Over y Under empíricamente antes de escoger el lado.
- Una probabilidad equivalente en línea asiática no debe comunicarse como probabilidad literal de victoria.
- Muestras pequeñas pueden mostrar grandes variaciones de P/L. No deben optimizarse bandwidth, priors o thresholds mirando el mismo período usado para decidir que una variante “ganó”.

## Pruebas

La suite focalizada cubre:

- exclusión por fecha y lag estricto;
- una observación independiente por fixture pese a múltiples líneas;
- retorno exacto de `.25` y `.75`;
- rechazo por evidencia insuficiente;
- rechazo cuando muchas filas colapsan a una muestra efectiva insuficiente;
- reliability sensible al objetivo de evidencia;
- incertidumbre posterior no nula aun con outcomes idénticos;
- hash sensible a correcciones del resultado;
- determinismo frente al orden de entrada;
- ausencia de cambios en C y D cuando Bot E está deshabilitado;
- normalización `E -> E2026` en liquidación.

Ejecutar:

```bash
dotnet run --project tests/BotPickSettlement.Tests/BotPickSettlement.Tests.csproj --no-restore
dotnet build CornersPredictionApi/CornersPredictionApi.csproj --no-restore
dotnet build CornersPrediction.Web/CornersPrediction.Web.csproj --no-restore
```

## Criterio de promoción

Bot E debe mantenerse como experimento mientras la muestra sea reducida. Una promoción responsable requiere, como mínimo:

- varias ventanas temporales posteriores al 8 de agosto;
- resultados confirmados en los cuatro mercados que se quieran habilitar;
- comparación pareada contra C;
- yield estable y no explicado por dos o tres cuotas altas;
- reliability y `n_eff` suficientes por mercado;
- ausencia de degradación marcada en un alcance específico;
- parámetros congelados antes del período final de validación.

Que Bot E produzca menos picks o incluso ninguno al inicio puede ser el resultado correcto: su propósito es rechazar edges que todavía no cuentan con evidencia empírica suficiente.
