# Bot G2026 — Goals Specialist / Market-Anchored Probability

## Objetivo e hipótesis

G2026 estima probabilidades calibradas para `TotalGoals`, `HomeTeamGoals` y
`AwayTeamGoals`. El mercado bilateral es el punto de partida, no un dato auxiliar:

```text
p_market = no_vig(odds_over, odds_under)
logit(p_raw) = logit(p_market) + f(features_as_of_prediction)
```

Si el meta-modelo no encuentra señal adicional, `f = 0` y la probabilidad vuelve
exactamente a `p_market`. La meta no es maximizar aciertos o picks, sino que la
probabilidad declarada se aproxime a la frecuencia observada y que el EV se calcule
con el settlement asiático real.

Identidad estable:

- BotKey: `G2026`
- Nombre: `Bot G Goals Specialist`
- Estrategia: `GOALS_MARKET_ANCHORED`
- Configuración runtime/trainer: `bot-g-goals-market-intelligence-1.1.0`
- Feature schema: `bot-g-goals-features-1.0.0`
- Training/export contract: `bot-g-training-export-1.1.0`
- Meta-model: `bot-g-market-meta-1.1.0`
- Stake: `1u`
- Estado inicial: `Enabled=true`, `PublishEnabled=false`, `ShadowMode=true`

G aplica Football Intelligence reproducible después de calibración y antes de
uncertainty/EV. Si no existe al menos un snapshot de equipo utilizable, se abstiene
con `FootballIntelligenceUnavailable`; ausencia de evidencia nunca se interpreta
como ajuste neutral favorable. G no utiliza Team Strength ni los motores C–F.

## Arquitectura

```text
CornerOddsSnapshots (quote bilateral inmutable)
        +
MatchHistory sólo as-of prediction
        +
goals_v1 + modelos 2026 temporalmente válidos
        |
        v
BotGFeatureBuilder -> no-vig estricto -> residual-logit artifact
        -> calibración jerárquica -> Football Intelligence as-of
        -> uncertainty/OOD
        -> distribución Win/HalfWin/Push/HalfLoss/Loss
        -> EV nominal/conservador -> abstention -> ranking
        |
        +--> AutomatedBotPickEvaluations (todos los candidatos)
        |
        `--> publicación productiva sólo con doble gate explícito
```

El runtime está aislado en `BotGAutomationService`. G se separa de las definiciones
normales antes de construir perfiles legacy; así no puede caer accidentalmente en
las reglas del Bot A. El servicio genera Over y Under para cada quote bilateral,
audita `Approved`, `Rejected` y `Abstain`, y rankea como máximo un candidato por
fixture. La publicación vive fuera del evaluador y exige simultáneamente:

1. `AutomatedBotDefinitions.PublishEnabled = 1`;
2. `strategyConfiguration.publishEnabled = true`;
3. `strategyConfiguration.shadowMode = false`;
4. ejecución no histórica y no `DryRun`;
5. decisión `Approved`;
6. guard adicional dentro del procedimiento SQL productivo.

Shadow no equivale a `DryRun`: shadow persiste auditoría y nunca publica; `DryRun`
no persiste.

El batching de G se hace por fixture completo, no por filas de odds: ninguna curva
de líneas puede quedar partida entre dos lotes. Esto mantiene idénticos el gate de
monotonicidad y el ranking aunque `BatchSize` sea menor que el número de quotes.

## Fuentes y linaje auditado

La auditoría encontró linajes distintos que no deben fusionarse ni relabelarse:

- El legacy activo `goals_v1` está en `newModelsML/goals_v1/` dentro del
  repositorio.
  Sus binarios funcionan, pero no incluyen dataset hash, rango temporal ni un script
  capaz de reproducir exactamente esos archivos. El cutoff operativo conservador
  registrado es `2026-06-11T16:36:16Z`; no debe reinterpretarse como linaje probado.
- La API usa tres bundles 2026 reales y distintos: TotalGoals
  `targettotalgoals-2026-08-09-trial-53`, HomeTeamGoals
  `targethomegoals-2026-08-09-trial-15` y AwayTeamGoals
  `targetawaygoals-2026-08-09-trial-48`. En cancha neutral Home/Away pueden incluir
  la firma ordenada de ambos componentes porque el runtime promedia inferencia
  normal e invertida. El snapshot guarda la firma exacta efectivamente usada.

El runtime registra ambos nombres/versiones, cutoffs, quote/snapshot, timestamps,
configuración y feature snapshot. Un artifact G ausente o inválido produce
`ModelUnavailable`: no existe fallback basado en reglas.

El loader compara de forma fail-closed configuración, training contract, schema,
mercado, firma exacta de ambos modelos base, cutoffs, Football Intelligence,
calibración, uncertainty, OOD y settlement. Snapshots v1.0 quedan como auditoría y
son rechazados por el export/trainer v1.1; no se reescribe su versión.

## Datos de entrenamiento

El contrato canónico está en `scripts/bot_g/input.schema.json`. Cada quote requiere
exactamente dos filas coherentes (Over y Under) y, como mínimo:

- `FixtureId`, mercado, línea, bookmaker y `QuoteId`;
- `OddsTimestampUtc`, `PredictionTimestampUtc` y kickoff UTC;
- odds decimales Over y Under del snapshot inmutable;
- señales legacy y 2026 producidas antes de la predicción;
- versión y `TrainedThroughUtc` de cada modelo base;
- features/contexto materializados as-of;
- `OutcomeAvailableUtc`, valor real del mercado y estado de settlement;
- versión del feature schema, configuración y training contract;
- versión, ajuste seleccionado, estados y cutoffs de Football Intelligence.

El loader rechaza fixtures repartidos entre folds, quotes unilaterales, timestamps
futuros, modelos entrenados hasta o después de la predicción, resultados conocidos
antes de predecir, duplicados y líneas/mercados no soportados.

### Datos reales que faltan hoy

No existe un dataset histórico suficiente para entrenar honestamente G:

- `PartidosProximosCuotas` sobrescribe la quote actual y no conserva todos los
  ticks históricos;
- no hay export histórico de snapshots bilaterales con timestamp para todo
  2018–2026;
- no hay predicciones OOF persistidas de `goals_v1`;
- no hay predicciones OOF simultáneas del linaje 2026 activo;
- `AutomatedBotPickEvaluations` previo a G contiene un universo elegido por el
  modelo base, no ambos lados neutrales de cada quote;
- faltan outcome-availability timestamps fiables para reconstruir algunas épocas.

Por eso esta entrega no crea `models/bot-g/active.json`, no declara métricas reales
y mantiene publicación cerrada. Sólo snapshots generados con el contrato v1.1 son
exportables; los anteriores no se completan por inferencia.

## Features

El feature builder usa únicamente información disponible antes de la predicción:

- ancla no-vig, odds, margen, edad de la quote y línea;
- señal legacy, señal 2026, promedio y desacuerdo;
- total 2026 directo versus home+away;
- contexto histórico y distancia modelo/contexto;
- ventanas 5/10/20 globales y por venue;
- media ponderada temporal, mediana, varianza, desviación, percentiles, IQR y MAD;
- calidad, faltantes e historial disponible;
- estadísticas de línea exacta/vecinas cuando existen as-of.

`BotGFeatures.ToNumericVector()` es el diccionario autoritativo del runtime. El
trainer sólo exporta coeficientes para nombres presentes en ese diccionario.

## Entrenamiento, OOF y backtest

El pipeline de `scripts/bot_g/` implementa:

- expanding-window por fixture, embargo y outcome lag;
- test final intacto y bloqueado salvo autorización explícita;
- cinco ablaciones sobre las mismas filas: mercado; mercado+legacy;
  mercado+2026; mercado+ambos; mercado+ambos+contexto;
- residual logistic con offset de mercado como único formato desplegable;
- CatBoost/XGBoost/LightGBM opcionales sólo como comparadores OOF;
- calibración Platt/Beta entrenada sólo con predicciones OOF;
- shrinkage global -> mercado -> lado -> bookmaker;
- ensemble bootstrap agrupado por fixture;
- OOD robusto con mediana/MAD y percentiles 1/99;
- backtest cronológico walk-forward que calibra sólo con outcomes anteriores;
- comparación pareada por `FixtureId/Bookmaker/MarketType/Selection/Line` con F
  cuando el input incluye `FPublished`; `FProbability`, `FEdge` y
  `FExpectedValue` habilitan además las diferencias compartidas sin imputaciones.
  El export SQL los enriquece sólo mediante la misma firma y un fixture oficial;
  si la predicción F no existe, sus métricas permanecen `NULL`.

Los artefactos registran dataset SHA-256, Git state, seed, paquetes, rangos
temporales, filas/fixtures, firma por mercado, contrato FI, features, parámetros y
límites de evidencia. Un dataset sintético queda `deployable=false`. La creación
automática de `active.json` está deshabilitada incluso para datos reales.

## Calibración, uncertainty y OOD

La probabilidad residual se calibra jerárquicamente. Cada perfil lleva
`EvidenceAvailableThroughUtc` y sólo es elegible si respeta el lag configurado.
La fiabilidad depende del tamaño efectivo; falta de evidencia suficiente produce
abstención, no una probabilidad optimista.

La uncertainty combina dispersión del ensemble y muestra efectiva para obtener
`ProbabilityLowerBound`, `ProbabilityUpperBound` y `ConservativeProbability`. El
edge conservador siempre se calcula contra el mismo `p_market` bilateral.

OOD calcula robust-z por feature. Si no existe referencia válida, la evaluación OOD
es `Unavailable` y el candidato se abstiene. No se trata la ausencia de evidencia
como score cero.

## Asian settlement y EV

El cálculo soporta `Win`, `HalfWin`, `Push`, `HalfLoss` y `Loss`. Para líneas enteras,
.25 y .75 se exige una distribución de cinco estados, específica de la línea y con
muestra efectiva suficiente. Para .5 la distribución binaria es suficiente. La
probabilidad final recalibra la masa positiva preservando las proporciones internas
del perfil antes de calcular:

```text
EV = sum(probabilidad_estado * retorno_neto_estado)
```

Sin perfil válido, una línea que lo requiere termina en
`SettlementDistributionUnavailable`; nunca se aplica `p * odds - 1` a una quarter
line como si fuera binaria.

## Abstention y ranking

Las abstenciones cubren, entre otras causas: snapshot/no-vig ausente, odds stale,
historia insuficiente, artifact/schema/cutoff inválido, calibración no fiable,
uncertainty alta, OOD, mala calidad, desacuerdo extremo y distribución asiática
ausente. El gate también rechaza toda curva de líneas cuya probabilidad no sea
monótona (Over debe bajar y Under subir al aumentar la línea). Candidatos
técnicamente válidos pero bajo thresholds se marcan `Rejected`.

Sólo los `Approved` entran al ranking. El score combina EV y edge conservadores,
reliability, calidad, uncertainty inversa y acuerdo contextual; los Approved que no
ganan el fixture quedan auditados como `LowerRankedCandidate`.

## Persistencia y SQL

`sql/20260819_bot_g2026.sql` es aditivo e idempotente. El inicializador ejecuta
primero `automated_corners_bot.sql` y después esta migración. Los cambios:

- agregan `PublishEnabled` sin alterar el estado de A–F;
- amplían el check de estrategias y siembran G sólo cuando no existe;
- amplían `AutomatedBotPickEvaluations` con campos G consultables;
- conservan `FeatureSnapshotJson` para payloads extensos;
- separan la firma productiva requerida
  (`BotKey|FixtureId|Bookmaker|MarketType|Selection|Line|ConfigurationVersion`)
  de la idempotencia de auditoría, que añade `OddsSnapshotId`; un nuevo snapshot
  inmutable nunca recicla decisión, métricas ni `RunId` de una quote anterior;
- agregan índices por bot/fixture/timestamp/mercado/lado/bookmaker/configuración/
  resultado y unicidad filtrada para fixture/publicación G;
- crean view y procedimientos de candidatos, detalle, scorecard, dataset temporal,
  settlement y auditoría;
- el export de entrenamiento exige contrato v1.1, los tres linajes reales y campos
  FI as-of; no usa `COALESCE` para inventar esas identidades;
- separan `FixtureIdentity` (hash canónico auditable, independiente del proveedor
  y estable cuando aparece un ID oficial) de `ApiFootballFixtureId` (sólo un ID
  oficial verificado, usado para settlement);
- protegen la tabla productiva con un gate SQL de `PublishEnabled` y un máximo de
  una publicación G por fixture. La selección productiva y el cambio de su audit
  candidate a `Published` ocurren dentro de la misma transacción y lock sobre
  `FixtureIdentity`; probabilidades, edge, EV, score y stake se copian desde el
  audit aprobado, no desde valores libres enviados por el caller.

No se reescriben picks históricos ni P/L anterior. La liquidación shadow sólo toma
un `FixtureId` oficial, partido `FT/AET/PEN`, goals disponibles y resultado no nulo;
los matches ambiguos siguen `Pending`.

## API y Web

Endpoints principales:

- `POST /api/bot-g2026/run`
- `POST /api/bot-g2026/settle`
- `GET /api/bot-g2026/candidates`
- `GET /api/bot-g2026/candidates/{candidateId}`
- `GET /api/bot-g2026/results`
- `GET /api/bot-g2026/scorecard`

La pantalla `/BotG2026` es independiente de Bot Picks productivos. Expone
probabilidades raw/calibrada/conservadora, no-vig, edges, EV, bounds, uncertainty,
reliability, OOD, decisión, razones, versiones, shadow/publicación y settlement.

## Operación

### Crear entorno y probar el pipeline offline

```bash
python -m pip install -r scripts/bot_g/requirements.lock.txt
python scripts/train_bot_g.py --self-test
python tests/test_bot_g_offline.py
```

Las dependencias de árboles comparadores son opcionales:

```bash
python -m pip install -r scripts/bot_g/requirements-optional.lock.txt
```

### Entrenar sin tocar el test final

Primero exportar con una credencial SQL de sólo lectura (la conexión nunca se pasa
por argumento ni se imprime) y ejecutar el preflight sin entrenar:

```bash
BOT_G_SQL_CONNECTION_STRING='...' dotnet run \
  --project tools/BotGTrainingExport/BotGTrainingExport.csproj -- \
  --output /secure/path/goals-candidates.jsonl \
  --as-of 2026-08-31T23:59:59Z

python scripts/train_bot_g.py \
  --input /secure/path/goals-candidates.jsonl \
  --preflight-only
```

El export es JSONL inmutable y agrega un manifest SHA-256. Un preflight `PASS`
autoriza sólo a entrenar; no habilita publicación ni promoción.

```bash
python scripts/train_bot_g.py \
  --input /absolute/path/goals-candidates.jsonl \
  --output-dir models/bot-g
```

Una única evaluación explícita del bloque final:

```bash
python scripts/train_bot_g.py \
  --input /absolute/path/goals-candidates.jsonl \
  --output-dir models/bot-g \
  --evaluate-final-test
```

`--activate` falla intencionalmente: v1.1 no escribe `active.json` de forma
automática. La promoción requiere revisión humana y despliegue manual separado.
Los outputs versionados (`artifact`, report, OOF y final-test) son inmutables: un
reentrenamiento exige incrementar `model_version` y `configuration_version`; el
pipeline se niega a sobrescribirlos.

### Backtest walk-forward

```bash
python scripts/backtest_bot_g.py \
  --input /absolute/path/goals-candidates.jsonl \
  --output-dir models/bot-g
```

### Compilar y probar .NET

```bash
dotnet build CornersPrediction.sln --no-restore
dotnet run --project tests/BotG2026.Tests/BotG2026.Tests.csproj
dotnet run --project tests/BotPickSettlement.Tests/BotPickSettlement.Tests.csproj
```

### Ejecutar en shadow

Aplicar el schema al iniciar la API, configurar la conexión existente y dejar:

```text
AutomatedBotDefinitions.G2026.IsEnabled = 1
AutomatedBotDefinitions.G2026.PublishEnabled = 0
strategyConfiguration.enabled = true
strategyConfiguration.publishEnabled = false
strategyConfiguration.shadowMode = true
BOT_G_ARTIFACT_PATH=../models/bot-g/active.json
```

Luego:

```bash
curl -X POST http://localhost:5175/api/bot-g2026/run \
  -H 'Content-Type: application/json' \
  -H 'X-Internal-Api-Key: <configured-key>' \
  -d '{"dateFrom":"2026-08-19","dateTo":"2026-08-26","dryRun":false}'
```

Sin `active.json`, la ejecución es útil como validación/collector pero cada candidato
queda abstain. No copie un modelo full-refit hacia atrás para evitar ese estado.

### Promoción posterior

La promoción nunca es automática por yield. El scorecard conceptual avanza por
`SHADOW`, `EXPERIMENTAL`, `MONITORING`, `CANDIDATE` y `PRODUCTION`, considerando
fixtures independientes, ventanas OOS, Brier/log loss contra mercado, calibración,
coverage, drawdown, EV observado y concentración de resultados. Para publicar se
debe instalar un artifact real aprobado y cambiar de forma atómica los dos gates:

El scorecard SQL usa fixtures **resueltos** y se limita deliberadamente a sugerir
hasta `MONITORING`: `CANDIDATE` y `PRODUCTION` requieren que el reporte offline
demuestre una segunda ventana walk-forward OOS independiente.

```text
AutomatedBotDefinitions.PublishEnabled = 1
strategyConfiguration.publishEnabled = true
strategyConfiguration.shadowMode = false
```

Antes de hacerlo, repetir pruebas, revisar scorecards/paired F y realizar una
aprobación operativa humana. No hay código de autopromoción.

## Limitaciones y extensiones

- El cutoff legacy actual es operativo, no linaje reproducible demostrado.
- El collector sólo empieza a producir un universo prospectivo correcto desde su
  despliegue; necesita outcomes resueltos antes de entrenar.
- CLV requiere snapshots de closing confiables; si no existen se reporta ausente.
- La inferencia histórica online está bloqueada; debe usarse el backtester as-of.
- Conformal prediction puede añadirse como otro proveedor de bounds sin cambiar el
  contrato de decisión.
- Un futuro mixture-of-experts (Bot H) debe esperar evidencia OOS de expertos
  diferenciados; no forma parte de G v1.
