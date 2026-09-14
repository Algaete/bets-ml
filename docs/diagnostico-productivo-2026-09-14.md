# Diagnóstico de ausencia de goles visita en productivo

Consulta del 14 de septiembre de 2026, aproximadamente 15:48 de Chile. Código local: `44b6863`, sin modificaciones de código durante este diagnóstico. Rango consultado: **14–21 de septiembre**, no sólo un fin de semana.

## Resultado

La consulta general de `AwayTeamGoals`, filtrada por modelo `Approved`, devolvió **62 filas**: C2026 19 Over y 12 Under; D2026 19 Over y 12 Under. Todas tenían `ProductionBlocked`. Son señales por bot/partido/línea; no 62 partidos distintos. La consulta de publicados del mismo mercado/rango devolvió 0.

El acceso a Azure SQL funciona ahora. El bloqueo de IP documentado el día anterior ya no explica esta ejecución; este diagnóstico no modificó ninguna regla de firewall.

Los bots A/C/D/E/F tienen `IsEnabled=true` y `PublishEnabled=true` en el mantenedor. G/H son shadow. No hay desactivación nueva de publicación de E/F en el mantenedor.

## Bloqueos productivos observados

La política usa **30 días, bot, mercado, lado y versión exactos**. La casa de apuestas no es un veto en la política actual. Los umbrales no cambiaron entre `1b67348` y `44b6863`.

| Segmento actual | Partidos independientes | Yield | Brecha de calibración | Brier modelo / mercado | Impedimento |
| --- | ---: | ---: | ---: | --- | --- |
| C Over, versión `7DC137EB` | 23 | +6,35% | 7,46 puntos porcentuales | 0,242426 / 0,228802 | Requiere ≥30 partidos, yield ≥7%, brecha ≤5 puntos y Brier ≤mercado |
| F Over, versión `54881EB1` | 64 | +9,27% | 5,009375 puntos porcentuales | 0,234979 / 0,234571 | Muestra/yield suficientes, pero falla brecha y Brier |
| C Under, versión `7DC137EB` | 43 | −10,84% | 23,68 puntos porcentuales | 0,312131 / 0,248739 | Requiere ≥100 partidos y Green; actualmente Red |
| D Under, versión `1B461E1D` | 36 | −13,67% | 25,03 puntos porcentuales | — | Requiere ≥100 y Green; actualmente Red |
| E Over, versión `39702656` | 58 | +15,43% | 0,88 puntos porcentuales | delta Brier −0,013877 | Requiere ≥100 y Green; no pertenece al ensayo reducido C/F |
| E Under, versión `39702656` | 40 | +1,23% | 9,06 puntos porcentuales | delta Brier +0,008119 | No alcanza ≥100 ni Green |
| F Under, versión `54881EB1` | 39 | +3,00% | 7,66 puntos porcentuales | 0,248181 / 0,241203 | No alcanza ≥100 ni Green |

La prueba controlada de 0,5u admite únicamente C/F **Over**. Under no está prohibido en general, pero necesita su segmento Green de 100 partidos. Esta distinción ya existía antes del último commit.

La versión antigua de C Over, `AutomatedCornersBotV1.0-C2026`, tiene 36 partidos, yield +16,61%, brecha 2,51 puntos y Brier mejor que mercado. Cumpliría los requisitos estadísticos del ensayo para esa versión, pero **no es la versión actual**. No se puede asumir que su evidencia valida automáticamente la configuración actual `AutomatedCornersBotV1.0-7DC137EB-C2026`. El hash se genera a partir de la configuración serializada del selector.

Ejemplos aprobados recientes que luego se bloquean:

- Águilas Doradas–Deportivo Pereira, visita Over 0,5 a 1,86: C evaluación `585805`, bloqueada por cohorte; D `585816`, muestra 24/100.
- Real Sociedad II–Mallorca, visita Under 1,5 a 1,83: C `586621`, muestra 43/100; D `586633`, muestra 36/100.

En las 62 aprobaciones había también seis filas bloqueadas por edad de cuota y cuatro por inexistencia de scorecard. Algunos motivos son los guardados cuando se evaluó la fila y no necesariamente reflejan el scorecard más reciente. La vista filtrada no sustituye una auditoría por RunId.

## Fallo técnico confirmado: calibración vacía

En logs del job `87e50db5-12e3-4d13-87a0-f4702c08d5b0`, fuentes F2026 y C2026 vuelven a registrar `Calibration history timed out` y `Observations=0`, incluidos lotes 12 y 13. Los siguientes reutilizan el resultado vacío.

La evidencia persistida de E2026 `586645`, Real Sociedad II–Mallorca Under 1,5, contiene:

```json
{
  "enabled": true,
  "sourceBot": "C2026",
  "result": {
    "isAvailable": false,
    "inputRows": 0,
    "globalFixtures": 0,
    "riskFlags": ["InsufficientCalibrationHistory"]
  }
}
```

El único código de rechazo de ese candidato es `REJECTED_CALIBRATION_SAMPLE_LOW`. F2026 `586609`, mismo partido/línea, también tiene ese único código de rechazo. Eso demuestra que no se deben presentar todos los descartes como falta de valor del partido. Tampoco permite afirmar que se aprobarían con calibración real: recalibrar puede cambiar probabilidad y EV.

En la página de 100 señales más recientes de goles visita, 42 evaluaciones rechazadas de E/F incluían muestra insuficiente de calibración; dos tenían ese rechazo como único incumplimiento. Ochenta señales de esa página también incumplían el rango de cuota del selector. Son conteos de una página, no tasas sobre todo el historial.

El último commit redujo el presupuesto de carga de cinco minutos a **10 segundos**, cacheó el resultado vacío de fallo durante **15 minutos** y conservó resultados exitosos durante seis horas. Aunque mejora la espera del resto, **no arregla la consulta y contribuye a rechazos operativos de E/F/H**. No confundirlo con un nuevo umbral estadístico o considerarlo solución completa.

## Conclusión y siguiente reparación necesaria

Hay aprobaciones Over/Under; las bloquean requisitos productivos existentes. Además persiste el fallo técnico de calibración. Volver a ejecutar los mismos partidos no aumenta el número de resultados independientes necesario para promoción.

La siguiente reparación debe conseguir que carguen observaciones históricas válidas sin saturar SQL, y distinguir infraestructura no disponible de muestra realmente insuficiente. Después se deben reevaluar señales con cuotas frescas y medir el resultado real. No se cambiaron filtros, límites, modelos, stakes ni datos de liquidación durante esta revisión.

Fuentes locales: `AutomatedBotPerformance.cs`, `BotPickProductionPlanner.cs`, `SqlAutomationRepository.cs`, evidencia individual del API, catálogo de bots, logs y scorecards cuyo `DateToUtc` era `2026-09-14T18:29:38.821971Z`.
