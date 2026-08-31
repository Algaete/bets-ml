# Arquitectura

## Componentes

```mermaid
flowchart LR
    Job[RecommendationJobWorker] --> Pipeline[AutomatedCornersSelectionService]
    Pipeline --> Current[Selector actual]
    Current --> Candidate[(AutomatedBotPickEvaluations)]
    Current --> Published[(AutomatedCornerBetSelections)]
    Odds[(CornerOddsSnapshots)] --> Robust[Robust evaluation orchestrator]
    Candidate --> Robust
    History[(MatchHistory)] --> Residuals[Residual history query]
    Residuals --> Robust
    Intelligence[(Intelligence snapshots)] --> Robust
    Robust --> Core[Domain: consensus, bootstrap, EV, policy, exposure]
    Core --> Evaluation[(RobustEvaluations append-only)]
    Evaluation --> Api[Robust API]
    Api --> Web[Bot Picks detail]
    Evaluation --> Backtest[Walk-forward report]
```

El job continúa llamando al pipeline existente. La capa robusta se conecta allí para observar el mismo candidato y el mismo cutoff; no es un job paralelo que pueda competir con la publicación.

## Pipeline de una evaluación

```mermaid
flowchart TD
    A[Capturar fixture, mercado y AsOfUtc] --> B[Resolver snapshot exacto de cuota <= AsOfUtc]
    B --> C[Construir componentes point-in-time]
    C --> D[Consenso y reconciliación]
    D --> E[Cargar residuos una vez, sólo familia compatible]
    E --> F[Filtrar trained-through, prediction time y outcome lag]
    F --> G[Bootstrap determinista en memoria]
    G --> H[Settlement asiático 5 estados]
    H --> I[Fair odds, point/robust edge y EV]
    I --> J[Calibración y escenarios con estados explícitos]
    J --> K[Exposición y stake sin aumento]
    K --> L[Política versionada]
    L --> M[Append snapshot + componentes]
    M --> N{Mode}
    N -->|Shadow| O[Conservar decisión/stake actual]
    N -->|Enforce explícito| P[Sólo rechazar o reducir]
```

## Límites entre capas

- `Domain`: matemáticas puras, deterministas, sin SQL, reloj global ni red.
- `Application`: input/output estable, opciones, orquestación y contratos de repositorio.
- `Infrastructure`: Dapper/SQL Server, hashes canónicos, transacciones y consultas por lote.
- `API`: adapta candidatos reales, aplica timeout/cancellation y expone recursos.
- `Web`: carga el detalle robusto de forma lazy; el listado de Bot Picks no recibe payloads grandes.

## Persistencia

`dbo.AutomatedBotPickRobustEvaluations` guarda columnas consultables y JSON sólo para histogramas/evidencia variable. `dbo.AutomatedBotPickRobustComponents` conserva cada componente. `dbo.AutomatedBotRobustPolicies` conserva políticas versionadas. Las tablas existentes de candidatos, picks y odds se reutilizan.

Una evaluación repetida con idéntico input y versión devuelve la misma fila. Un cambio material crea una secuencia nueva, enlaza `SupersedesEvaluationId` y sólo cambia `IsCurrent` de la anterior de 1 a 0 dentro de la misma transacción.

## Reconciliación y fallback

Los pesos aprendidos sólo proceden de validación fuera de muestra y son proporcionales a `1 / max(error², epsilon)`, con cap por fuente. Si esa evidencia no existe, se usa un fallback explícito hacia Direct; contexto o escenarios sin validación nunca adquieren peso ocultamente.

## Integración con el sistema actual

En `Shadow`, `EffectiveDecision` y `EffectiveStake` siguen la decisión y stake actuales aunque `RobustDecision` sea Reject/ReduceStake/ManualReview. Esto permite medir desacuerdo sin cambiar resultados productivos. La liquidación sigue usando el motor canónico existente y la evaluación robusta jamás reescribe un settlement histórico.
