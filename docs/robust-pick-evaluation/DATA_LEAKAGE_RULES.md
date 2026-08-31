# Reglas contra data leakage

Estas reglas son invariantes, no recomendaciones opcionales.

## Cutoff único

Cada evaluación tiene un `AsOfUtc` UTC inmutable. Todo input debe satisfacer su propia regla temporal:

- predicción: `PredictionAsOfUtc <= AsOfUtc` y anterior al inicio del fixture;
- modelo: `ModelTrainedThroughUtc < FixtureStartUtc`;
- cuota: `QuoteTimestampUtc <= AsOfUtc`, misma fuente, fixture, mercado y línea;
- inteligencia/alineación: snapshot/cutoff `<= AsOfUtc`;
- resultado histórico: `OutcomeAvailableUtc <= AsOfUtc`;
- cuando sólo se conoce el fin del fixture, se exige `FixtureEndUtc + OutcomeAvailabilityLag <= AsOfUtc`.

El lag por defecto es 8 horas y vive en configuración. No está hardcodeado dentro del dominio.

## Separación de mercados

Un residuo de Goals nunca entra en Corners, Shots ni ShotsOnGoal. Dentro de una familia se aplica esta jerarquía, deteniéndose en el primer nivel que alcanza el `EffectiveN` mínimo:

1. mercado + side + liga + banda de línea;
2. mercado + side + liga;
3. mercado + side;
4. familia + scope Total/Home/Away + side;
5. familia + scope;
6. familia.

“Global” en calibración significa global dentro de la misma familia, nunca entre familias.

## Disponibilidad del resultado

La fecha nominal del partido no prueba que el resultado estaba disponible. Se usa el timestamp de actualización de MatchHistory/API-Football cuando existe; en su ausencia se aplica el lag conservador. Un resultado posterior al cutoff se excluye aunque hoy sea conocido.

## Backfill

- conserva el `AsOfUtc` original;
- no usa el reloj actual para elegir snapshots históricos;
- no reconstruye closing odds como si fueran prepartido;
- no trata una predicción generada hoy para un partido antiguo como evidencia out-of-sample;
- no inventa `ModelTrainedThroughUtc`;
- registra missing metadata y continúa en Shadow con warnings;
- no cambia decisión, stake ni settlement originales.

## Selection bias

`AllCandidates` significa que la fuente preservó aprobados y rechazados antes del resultado. `SelectedPicksOnly` significa que sólo existen picks que atravesaron el gate histórico. Esta segunda fuente puede sesgar residuales y calibración: se permite sólo por configuración, se marca en la evaluación y reduce confiabilidad.

## Tests obligatorios de temporalidad

- observación futura excluida;
- modelo entrenado después del fixture excluido;
- lag respetado en el borde;
- cuota posterior al cutoff rechazada;
- seed y resultado iguales para el mismo snapshot;
- backfill repetido idempotente;
- ninguna consulta usa `SYSUTCDATETIME()` como cutoff de una evaluación histórica.

## Auditoría manual de una evaluación

Revise `AsOfUtc`, `QuoteTimestampUtc`, `ModelTrainedThroughUtc`, `SourceOddsSnapshotId`, versiones, input hash, source scope y warning codes. Una fila con `LOOKAHEAD_DATA_DETECTED` no debe ser evidencia para elegir thresholds ni habilitar Enforce.
