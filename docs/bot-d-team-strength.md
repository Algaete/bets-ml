# Bot D · Team Strength Gap

Bot D es un experimento aislado sobre el Pick Selector 2026 de Bot C. Conserva modelos base, estadística contextual, calibración, no-vig, edge, EV, calidad, acuerdo y todas las protecciones temporales. Su única diferencia controlada es una señal adicional de brecha de nivel entre los dos equipos.

## Qué calcula

La brecha combina tres señales normalizadas entre `-1` y `1`:

- `50% Elo temporal`: cada resultado actualiza al ganador y al perdedor; por ello una victoria sobre un rival fuerte vale indirectamente más y la fuerza se propaga por la red de partidos.
- `20% enfrentamientos directos`: resultado y diferencia de goles de los duelos entre los dos equipos, ponderados por recencia.
- `30% rivales comunes`: compara cómo rindió cada equipo frente a los mismos oponentes. Esto modela relaciones como `A venció a B` y `B rindió mejor o peor contra C` sin afirmar que la transitividad sea perfecta.

Solo se usan partidos con `MatchDateUtc < AsOfDateUtc`. Los resultados se deduplican y la evidencia reciente pesa más con `ResultDecayFactor=0.90`.

La señal combinada se reduce según la cobertura disponible:

```text
rawGap = weighted(EloSignal, DirectSignal, CommonOpponentSignal)
confidence = sampleCoverage × linkedOpponentCoverage
adjustedGap = clamp(rawGap × confidence, -1, 1)
```

El candidato queda rechazado si alguno de los equipos tiene menos de cuatro partidos o si la confianza final es inferior a `0.45`.

## Cómo afecta a cada mercado

La orientación se invierte correctamente según el alcance:

```text
Local:     marketSignal =  adjustedGap
Visitante: marketSignal = -adjustedGap
Total:     marketSignal = abs(adjustedGap)
```

Los pesos efectivos son `1.00` para mercados del local, `0.80` para visitante y `0.15` para totales. En fallback, el cambio de probabilidad está limitado a `±8 pp` antes de recalcular edge y EV. También desplaza moderadamente la predicción contextual en función de sigma. Esto evita tratar el gap como una probabilidad o dejar que sustituya al modelo del mercado.

Cuando exista un artefacto LogisticRegression con `FeatureSchemaVersion=bot-d-features-1.0.0`, se puede publicar en `models/bot-d-meta/active.json` o definir `BOT_D_META_MODEL_ARTIFACT_PATH`. El metamodelo recibirá las features de nivel directamente y no se aplicará además el ajuste explícito, evitando contar dos veces la misma señal. Bot C y Bot D mantienen rutas de artefacto independientes. Hoy Bot D opera como `RuleBasedFallback` explicable.

## Trazabilidad

Cada evaluación conserva en `DecisionReason.featureSnapshot.teamStrength`:

- cantidad de resultados recibidos y aceptados;
- muestras de local y visitante;
- Elo de ambos equipos, diferencia y señal Elo;
- cantidad y señal de enfrentamientos directos;
- cantidad y señal de rivales comunes;
- gap bruto, confianza y gap ajustado;
- peso del mercado, ajuste contextual y ajuste de probabilidad;
- configuración, versión, riesgos y razones de aprobación/rechazo.

El mantenedor muestra esta configuración, el flujo, las features, las reglas, las defensas contra data leakage y la política de persistencia/liquidación. Bot Picks identifica Bot D por separado en estadísticas, pero la tabla operativa continúa mostrando juntos todos los bots del mercado.

## Comparación histórica reproducible

Los modelos base declaran `TrainedThrough=2026-08-07`. Por integridad experimental, la comparación comienza el 8 de agosto de 2026; correr Bot D antes de esa fecha usaría modelos entrenados con información futura.

Con API local en ejecución:

```bash
CORNERS_INTERNAL_API_KEY='<internal-key>' node scripts/compare-bot-c-d-backtest.mjs --from=2026-08-08 --to=2026-08-13
```

El reporte separa C y D por mercado y alcance, y entrega picks, cobertura liquidada, accuracy, P/L, stake, yield, cuota, edge y EV. La comparación válida debe priorizar yield y calibración fuera de muestra; no se debe optimizar una versión nueva con unos pocos picks resueltos.
