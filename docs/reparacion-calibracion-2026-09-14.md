# Reparación de la carga de calibración

Autorizada por el usuario después del diagnóstico en `diagnostico-productivo-2026-09-14.md`.

## Problema confirmado

La carga vencía a los 10 segundos y guardaba una lista vacía durante 15 minutos. E/F/H recibían esa lista como si no existiera evidencia y podían rechazar por `REJECTED_CALIBRATION_SAMPLE_LOW`.

Una medición de la consulta anterior sobre Azure SQL tardó 111 segundos sólo en reunir 59.130 identificadores. La preparación restante venció a los 180 segundos. El plan recorría el índice general de auditoría y luego buscaba campos en las filas grandes con JSON.

## Cambios

- Nueva proyección persistente `AutomatedBotCalibrationEvaluationCache`: guarda sólo metadatos de evaluaciones, sin JSON ni resultados oficiales. Se prepara en transacciones de 250 registros; conserva los bloques completados después de una interrupción.
- La búsqueda de pendientes usa `BotDecisionDate`, que cubre sus condiciones. Las siguientes cargas consultan los metadatos ya preparados.
- Un trigger prepara los metadatos al insertar una evaluación, los actualiza al corregirla y los elimina al borrarla. Incluye partidos futuros. Los enlaces de selección y resultados de MatchHistory se resuelven en cada carga; no se congelan liquidaciones corregibles en la nueva caché.
- Se conserva la caché existente de probabilidades fuente y su resolución estricta de JSON. No se sustituyen por `FinalProbability` ni se reducen artificialmente las observaciones por partido antes del calibrador.
- La probabilidad fuente de cada evaluación nueva se resuelve con el mismo parser .NET y se guarda dentro de la transacción de escritura. Se respeta la precisión de la probabilidad base persistida. El reintento idempotente repone la caché después de los triggers; una corrección externa la invalida.
- El historial antiguo se lee directamente en bloques de 100, sin copiar el JSON completo a tempdb. Sólo persisten los metadatos y las probabilidades compactas; una interrupción conserva los bloques terminados.
- El índice de metadatos cubre los campos consultados y usa compresión de página. La conversión de fecha local a UTC se realiza una vez por hora de inicio distinta, conservando las reglas temporales.
- Sólo las cargas completas y exitosas se guardan en memoria. Los candidatos futuros comparten la evidencia disponible del día; el calculador conserva su corte temporal individual y el desfase de disponibilidad del resultado. Caducidad en memoria: 30 minutos.
- Un fallo al cargar historial deja las evaluaciones en `PendingData`, con `PENDING_CALIBRATION_HISTORY_UNAVAILABLE`, tanto en decisión como en publicación. Una muestra realmente insuficiente sigue siendo un rechazo estadístico.
- Se elimina la etiqueta de calibración aprobada cuando el calibrador no está disponible.
- No cambian umbrales productivos, configuraciones de bots, versiones, stakes ni reglas de liquidación.

## Verificaciones realizadas

- Solución compilada sin errores ni advertencias.
- 65 pruebas de liquidación/motor, incluida la nueva distinción entre fallo operativo y falta real de muestra.
- SQL en tablas temporales: 85 observaciones idénticas en 12 mercados frente a la consulta de referencia, incluyendo probabilidades numéricas/texto/null, JSON malformado, prioridad de enlaces, ambigüedad y corte del entrenamiento.
- Preparación en varios bloques, reutilización de caché e invalidación selectiva verificadas. La comparación SQL y la prueba incremental se repitieron después de optimizar las fechas.
- Escritura real mediante el procedimiento y triggers desplegados: siete casos de JSON/probabilidad, precisión decimal, reintento idempotente, corrección de fixture y eliminación. Todas las escrituras de prueba se revirtieron y se verificó que no quedaran registros de auditoría.
- Pruebas de rendimiento/scorecards y 42 pruebas del plan productivo aprobadas.
- API reiniciada en 5070 con la corrección; health 200. Web 5130 responde y redirige al login sin sesión.

## Verificación con datos reales

La tabla, su índice cubierto, los triggers y el procedimiento actualizado ya están desplegados. Se prepararon los metadatos de 61.552 evaluaciones C y 62.887 F. Con los filtros de resultados oficiales quedan 37.060 observaciones candidatas C y 37.356 F, antes de excluir probabilidades fuente inválidas.

A las 17:28 de Chile, se habían preparado probabilidades de 31.139 candidatas C y 30.635 F. La carga inicial seguía leyendo auditoría antigua; las esperas SQL observadas son `PAGEIOLATCH_SH` (lecturas de disco). No se modificó el nivel de servicio de Azure ni las reglas de firewall.

La API se reinició aproximadamente a las 17:30 de Chile con la versión final. El job `cca14c03-fe82-4eef-bc4b-f81cc3cad6c9` retomó su lote y refrescó 1.494 cuotas de 100 partidos. A las 17:52 terminó C: 37.060 observaciones válidas de 601 partidos, incluidas 4.408 observaciones de goles visita en 521 partidos. Su recarga desde SQL, en un proceso nuevo, devolvió las mismas 37.060 observaciones en 40,5 segundos. Dentro de cada API se reutiliza la lista en memoria durante 30 minutos.

A las 17:57 terminó F en la API: 37.356 observaciones válidas. La preparación inicial fue costosa en S0 y requirió varias horas entre metadatos y JSON antiguo; no debe presentarse el tiempo de recarga como si fuera el de esa migración inicial. Los bloques quedan persistidos. La API volvió a cargar C desde SQL en 36,16 segundos.

### Reevaluación confirmada

- Primer lote: RunId `308e2a7c-9061-47f0-9550-338fb0dd26c0`, 10 partidos/mercado, **0 errores**, 0 selecciones publicadas. Terminó aproximadamente a las 18:03 de Chile. Las evaluaciones de investigación sí se guardaron.
- F `587496`: Bahia–Remo, goles visita **Over 0,5**, cuota 1,61, **Approved** por modelo, `ProductionBlocked`. Evidencia `isAvailable=true`, `inputRows=37356`, 451 partidos exactos, tamaño efectivo 431,56, sin flags de calibración. Probabilidad final 0,703798 y EV final 0,133115.
- E `587532`: mismo partido y línea, **Approved**, `ProductionBlocked`. Evidencia `isAvailable=true`, `inputRows=37060`, 446 partidos exactos, tamaño efectivo 427,66, sin flags de calibración.
- F `587497` y E `587533`, visita Under 1,5 del mismo partido: calibración aprobada; rechazos por edge/EV, contexto, rango de cuota y puntuación. No aparece `REJECTED_CALIBRATION_SAMPLE_LOW` en esos nuevos casos.
- Las filas que carecen de historial del equipo, por ejemplo F `587337`, quedan `PendingData` también en publicación. Es un motivo distinto de la carga de calibración ya reparada.
- El segundo lote comenzó con RunId `139c9548-d112-46e0-bf34-096513ba19ec`; cargó ambas fuentes desde memoria y llegó a la primera evaluación en **8,4 segundos**, incluyendo la preparación restante del lote. No es el tiempo de ejecutar sus diez partidos.
- El job completo continúa en segundo plano (16 lotes al comenzar esta ejecución). No se afirma que se hayan reevaluado ya todos los partidos de la semana.

### Productivo sigue usando los requisitos anteriores

Scorecards usados por el primer lote, corte `2026-09-14T20:32:26.553505Z`: F Over tiene 64 partidos y yield 9,27%, pero brecha **5,009375 puntos** frente al máximo de 5, y delta Brier **+0,0004083** frente al máximo de 0. F Under tiene **39/100** partidos y estado Amber. E Over tiene **58/100** y no participa en el ensayo reducido C/F. C Over tiene 23 partidos, yield 6,35%, brecha 7,46 puntos y Brier peor que mercado.

La nueva aprobación de E/F demuestra la reparación del rechazo técnico. No implica habilitación productiva automática, y no se redujeron límites para publicar señales.

### Estadísticas SQL y tabla de Generales

Se observó `SELECT INTO (STATMAN)` al consultar Generales: el plan esperaba una actualización síncrona de estadísticas. Una consulta F cancelada a los 15 segundos registró el error de cancelación de esa prueba; no fue un error del bot. La consulta posterior completó en 17,02 segundos.

Se aplicó explícitamente `sql/20260914_async_statistics.sql`: `AUTO_UPDATE_STATISTICS` sigue activo; `AUTO_UPDATE_STATISTICS_ASYNC` y `ASYNC_STATS_UPDATE_WAIT_AT_LOW_PRIORITY` pasaron de OFF a ON. El script es operativo y no forma parte del arranque de la aplicación. Incluye reversión. Este ajuste evita esperar la actualización de estadísticas y da baja prioridad a su publicación de metadatos, según [Microsoft Learn](https://learn.microsoft.com/en-us/sql/relational-databases/statistics/statistics?view=sql-server-ver17).

La consulta posterior F con otra clave HTTP (pageSize 21, sin respuesta en caché de aplicación) devolvió 200 en **3,35 segundos**. E había respondido en 1,99 segundos; las evidencias individuales en 0,18–0,20 segundos. Son mediciones operativas, no un benchmark aislado: también influyen las cachés de SQL y la carga concurrente.

Logs actuales: `/private/tmp/calibration-prepare-c-sequential.log` y `/private/tmp/corners-api-calibration-final.log`. La web continúa en 5130; la API y robots en 5070. El progreso persistente está en Azure SQL y no depende de los archivos temporales. No publicar conclusiones productivas nuevas sin consultar el resultado de la ejecución y sus evidencias.
