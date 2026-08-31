# Configuración

La configuración base vive bajo `RobustPickEvaluation` en la API. Una política SQL más específica puede sobrescribirla por bot, familia, mercado, scope, side, línea, cuota o liga, respetando `EffectiveFromUtc`.

```json
{
  "RobustPickEvaluation": {
    "Enabled": true,
    "Mode": "Shadow",
    "Version": "robust-pick-evaluation-1.0.0",
    "SimulationCount": 5000,
    "OuterScenarioCount": 500,
    "ProbabilityLowerQuantile": 0.10,
    "ProbabilityUpperQuantile": 0.90,
    "OutcomeAvailabilityLagHours": 8,
    "EvaluationTimeoutSeconds": 30,
    "MinimumReevaluationIntervalSeconds": 60,
    "ReevaluateOnOddsMovement": true,
    "SignificantOddsMovement": 0.02,
    "SignificantLineMovement": 0.25,
    "DefaultMaxOddsAgeSeconds": 1800,
    "MaxLineupOddsAgeSeconds": 300,
    "MaxOddsAgeSecondsBySource": {
      "Pinnacle": 900,
      "Betano": 1800
    },
    "Residuals": {
      "MinimumEffectiveN": 30,
      "TargetEffectiveN": 150,
      "RecencyHalfLifeDays": 90,
      "UseLineSimilarity": true,
      "UseOddsSimilarity": true,
      "AllowSelectedPicksOnly": true,
      "ErrorScaleEpsilon": 0.000001
    },
    "Policy": {
      "MinRobustEdge": 0.005,
      "MinRobustExpectedValue": 0.0,
      "MinPositiveEvStability": 0.75,
      "MinScenarioSideStability": 0.75,
      "MinNormalizedWorstCaseDistance": 0.25,
      "MaxNormalizedConsensusRange": 0.75,
      "MaxNormalizedCoherenceGap": 0.75,
      "MinCalibrationReliability": 0.50,
      "RequireSideAgreement": true
    },
    "Stake": {
      "AllowIncrease": false,
      "HighRobustnessThreshold": 0.90,
      "MediumRobustnessThreshold": 0.80,
      "MinimumRobustnessThreshold": 0.75
    },
    "Exposure": {
      "Enabled": true,
      "MaximumStakePerFixture": 1.5,
      "MaximumStakePerTeam": 1.0,
      "MaximumStakePerCorrelationCluster": 0.75,
      "MaximumRelatedPicksPerFixture": 3
    }
  }
}
```

## Modos

- `Disabled`: no calcula ni persiste evaluaciones nuevas.
- `Shadow`: calcula/persiste; decisión y stake efectivos siguen al bot actual.
- `Enforce`: puede rechazar o reducir un pick que el sistema actual aceptó. Nunca convierte un `NO BET` actual en `BET`.

Cambiar a `Enforce` debe hacerse con una política versionada y scope estrecho después de la validación descrita en [BACKTESTING.md](BACKTESTING.md). Para rollback, insertar una política nueva `Shadow`; no editar ni borrar la anterior.

## Distribución residual

- `SimulationCount`: draws internos. Más draws reducen ruido Monte Carlo; el seed determinista mantiene reproducibilidad.
- `MinimumEffectiveN`: sample efectivo mínimo para considerar suficiente el bucket.
- `TargetEffectiveN`: nivel donde muestra/reliability recibe score completo.
- `RecencyHalfLifeDays`: decaimiento exponencial por antigüedad.
- similitud de línea y cuota: pondera observaciones cercanas sin cruzar familia.
- `AllowSelectedPicksOnly`: si es `false`, un histórico legacy sin todos los candidatos no puede alimentar la distribución.

## Cuotas y calibración

- El no-vig sólo se calcula cuando Over y Under pertenecen al mismo snapshot y línea. Se calculan los métodos proporcional y power; para el lado elegido se conserva la estimación más exigente y se registra el método.
- `DefaultMaxOddsAgeSeconds` y el mapa por fuente determinan `Available`, `Stale`, `SourceUnavailable` o `SnapshotExpired`. Una cuota stale no se convierte en precio válido.
- Los límites de calibración existentes se respetan. Si sólo existe `EffectiveN`, se usa un intervalo Wilson determinista al 90 % por defecto; `PriorWeight`, `IntervalMethod` y `ConfidenceLevel` se conservan cuando la fuente los entrega.

## Política

Los thresholds se evalúan en orden y se acumulan todas las fallas. Un valor ausente requerido falla cerrado en la decisión robusta, pero Shadow sigue sin alterar producción. `RequireSideAgreement`, `RequireNoVig` y `RequireIntelligence` deben endurecerse sólo por política versionada.

`MinimumReevaluationIntervalSeconds` y los thresholds de cambio son guardrails del disparador de reevaluaciones. El pipeline actual emite evaluaciones al recibir una nueva evaluación fuente; la persistencia elimina repeticiones exactas mediante hash/idempotency y conserva como nueva secuencia los inputs realmente distintos.

## Stake

Aunque exista `AllowIncrease`, v1 recorta el multiplicador al rango `[0,1]`. La configuración queda registrada para trazabilidad, pero no autoriza aumentos. Los niveles iniciales producen 1.0, 0.75, 0.50 o 0 según el score; la exposición puede reducir todavía más.

## Overrides y precedencia

La política efectiva debe ser válida en `AsOfUtc`. Entre políticas compatibles prevalece la más específica (bot/mercado/scope/side/rangos/liga) y luego la más reciente por `EffectiveFromUtc`. Una política posterior nunca reescribe evaluaciones anteriores; una reevaluación crea otro snapshot con nueva secuencia.

## Variables de entorno

En despliegue se pueden mapear al menos:

- `ROBUST_PICK_EVALUATION_ENABLED`
- `ROBUST_PICK_EVALUATION_MODE`
- `ROBUST_PICK_EVALUATION_VERSION`
- `ROBUST_PICK_OUTCOME_AVAILABILITY_LAG_HOURS`
- `ROBUST_PICK_DEFAULT_MAX_ODDS_AGE_SECONDS`

Los secretos continúan en las variables/conexiones existentes. La configuración robusta no contiene API keys ni connection strings.
