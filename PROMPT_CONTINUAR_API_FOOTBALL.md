# Prompt para continuar las tandas de API-Football

Ultima actualizacion: `2026-07-19`, justo antes de apagar el equipo.

Copia y pega desde la siguiente linea en un chat nuevo de Codex:

---

Trabaja en el repositorio:

`/Users/alfonsogaeterodriguez/Desktop/corners-model-v1_`

Necesito continuar las tandas historicas de API-Football hacia atras, insertando y actualizando `dbo.MatchHistory` en Azure SQL con corners, datos extendidos y standings, igual que en las tandas anteriores.

## REANUDAR AQUI

- Mes actual: julio de 2025.
- Siguiente lote: `2025-07-01` a `2025-07-31`.
- Siguiente `competitionOffset`: `17`.
- Repetir primero `Liga Profesional Argentina` (leagueId 128): quedaron 20 de 45 fixtures por completar.
- No se ejecutaron nuevas tandas despues de este corte; este sigue siendo el punto exacto.
- Septiembre y agosto de 2025 ya quedaron completos; no volver a iniciarlos desde offset 0.
- La cuota anterior termino sin margen util (`DailyRemaining` entre 2 y 8). Consultar `/api/api-football/status` y continuar solo cuando se haya reiniciado.
- Despues de completar julio, seguir hacia atras con junio de 2025.

## Estado confirmado

- API local: `http://localhost:5070`
- Web local: `http://localhost:5130`
- La API key de API-Football NO esta guardada en este archivo. Pidemela o usa `API_FOOTBALL_KEY` si ya esta configurada en el entorno.
- La API fue reiniciada el `2026-07-19` sin `API_FOOTBALL_KEY`; por eso `/api/api-football/status` responde `500` con `API_FOOTBALL_KEY is not configured`. Configura la clave al levantarla en la proxima sesion y recien entonces consulta la cuota.
- La tanda mas reciente termino por el control preventivo de cuota (`StoppedByQuota: true`), sin HTTP `429` y sin errores del lote.
- Al cerrar, el ultimo header del lote reporto `DailyRemaining: 2` y `/status` reporto `8`; no queda cuota util para otra tanda.
- Antes de continuar, consulta `/api/api-football/status` y espera a que la cuota diaria se haya reiniciado.
- No reproceses 2026: ya esta cargado.

## Cobertura actual en MatchHistory

- `2025-12`: 1.364 partidos de API-Football.
- `2025-11`: 1.165 partidos de API-Football.
- `2025-10`: 1.774 partidos de API-Football.
- `2025-09`: completado desde el punto pendiente (offset 11 hasta el final).
- `2025-08`: completado desde offset 0 hasta el final.
- `2025-07`: parcial; se alcanzaron 18 competiciones desde offset 0 antes del corte de cuota.
- Octubre-diciembre tienen 4.303 fixtures unicos y 0 duplicados confirmados.
- Auditoria anterior a la normalizacion `v7`: 63.074 filas totales en `MatchHistory`, 15.360 con `ApiFootballFixtureId`, 1.525 equipos, 128 temporadas de liga, 5.926 snapshots de standings y 553 sync runs.
- La tanda de este documento agrego exactamente 3.141 filas nuevas a `MatchHistory` (59.933 -> 63.074).
- La normalizacion canonica `v7` deduplico partidos que representaban el mismo encuentro con nombres distintos. Al reanudar, vuelve a consultar el total actual de `MatchHistory`; no asumas que sigue siendo exactamente 63.074.

## Punto exacto de continuacion

1. Retoma julio de 2025 con `competitionOffset: 17`.
2. Ese offset repite `Liga Profesional Argentina` (leagueId 128): habia 45 fixtures disponibles y la cuota solo permitio procesar 25. `UpdateExisting: true` actualizara esos 25 y permitira completar los 20 restantes sin duplicarlos.
3. Cuando julio termine, continua hacia atras por meses completos: junio de 2025, mayo de 2025, abril de 2025, etc.
4. API-Football solo admite hasta 32 dias por descubrimiento, por lo que debes ejecutar un mes por solicitud.
5. Ejecuta los lotes reales, no `dryRun`.
6. No ejecutes meses en paralelo; respeta la cuota por minuto y conserva resultados mensuales verificables.

Payload inicial para julio:

```json
{
  "dateFrom": "2025-07-01",
  "dateTo": "2025-07-31",
  "competitionOffset": 17,
  "maxCompetitions": 500,
  "maxFixturesPerCompetition": 1000,
  "maxTotalFixtures": 7000,
  "minimumDailyRemaining": 0,
  "dryRun": false,
  "updateExisting": true,
  "syncStandings": true,
  "syncLineups": false,
  "seniorMenOnly": true
}
```

Endpoint:

`POST /api/api-football/bulk-sync`

Lee la clave interna local desde la configuracion del proyecto para enviar el header `X-Internal-Api-Key`; no la hardcodees en archivos nuevos.

## Manejo de offsets

- Si el lote termina normalmente por el limite de fixtures, el siguiente offset es `offset actual + ProcessedCompetitions`.
- Si termina con una fila `QuotaExceeded`, esa fila no fue procesada: el siguiente offset es `offset actual + ProcessedCompetitions - 1`.
- Si `StoppedByQuota` es true sin fila `QuotaExceeded`, revisa la ultima fila. Si `RequestedFixtures < AvailableFixtures`, vuelve a incluirla: `offset actual + ProcessedCompetitions - 1`.
- Tambien vuelve a incluir la ultima liga si el limite de 7.000 fixtures la dejo parcial (`RequestedFixtures < AvailableFixtures`). `UpdateExisting: true` evita duplicados.
- Verifica siempre los conteos directamente en Azure SQL despues de cada mes.

## Resultado de la ultima tanda

- Septiembre 2025, offset 11: 183 competiciones, 851 procesados, 673 insertados, 163 actualizados, 6.164 omitidos, 0 errores. Siguiente offset usado: 194.
- Septiembre 2025, offset 194: 402 competiciones, 891 procesados, 817 insertados, 39 actualizados, 5.995 omitidos, 0 errores. Septiembre quedo completo.
- Agosto 2025, offset 0: 128 competiciones, 1.227 procesados, 859 insertados, 263 actualizados, 5.878 omitidos, 0 errores.
- Agosto 2025, offset 127: 297 competiciones, 1.052 procesados, 660 insertados, 316 actualizados, 6.021 omitidos y 3 errores conocidos de Bundesliga. Siguiente offset usado: 423.
- Agosto 2025, offset 423: 96 competiciones, 15 procesados, 6 insertados, 7 actualizados, 429 omitidos, 0 errores. Agosto quedo completo.
- Julio 2025, offset 0: 18 competiciones, 207 procesados, 126 insertados, 32 actualizados, 1.203 omitidos, 0 errores y `StoppedByQuota: true`.
- Totales de los bulk-sync de esta tanda: 4.243 fixtures procesados, 3.141 insertados, 820 actualizados y 25.690 omitidos.

### Pendiente tecnico de Bundesliga

Un reintento directo de Bundesliga (leagueId 78, agosto 2025) actualizo 15 de 18 partidos, pero estos 3 siguen fallando porque el SP reporta duplicado y luego no encuentra la fila existente para actualizar:

- Fixture `1388318`: Borussia Dortmund vs Union Berlin, 2025-08-31.
- Fixture `1388310`: Borussia Monchengladbach vs Hamburger SV, 2025-08-24.
- Fixture `1388316`: Union Berlin vs VfB Stuttgart, 2025-08-23.

La normalizacion canonica `v7` ahora elimina duplicados logicos antes de reescribir nombres, por lo que este problema puede haber quedado corregido. Verifica primero las filas directamente en SQL; no gastes cuota reintentandolos hasta confirmar que siguen pendientes.

## Colas conocidas de meses ya cargados

Estas son competiciones de menor volumen que quedaron fuera de los topes de las pasadas anteriores. No pierdas estos puntos:

- Octubre de 2025: continuar desde offset `464`.
- Noviembre de 2025: continuar desde offset `180`.
- Diciembre de 2025: continuar desde offset `275`.

La prioridad actual es terminar julio y seguir hacia junio. Cuando haya cuota sobrante, vuelve a esas colas.

## Cambios de codigo ya realizados

- `MinimumDailyRemaining` acepta valores entre `0` y `2000` en `ApiFootballSyncService`.
- Existe `ApiFootballQuotaExceededException` para HTTP `429`.
- `bulk-sync` ahora debe detenerse en el primer `429`, marcar `StoppedByQuota` y evitar continuar generando errores por todas las ligas restantes.
- El proyecto API compilo correctamente, sin warnings ni errores, despues de estos cambios.
- Tambien se corrigio previamente el alias `Atlanta United` -> `Atlanta United FC`.
- Se aplico correctamente el catalogo canonico `2026-07-19-v7` sobre Azure SQL.
- Se agregaron alias para Racing de Montevideo, San Martin de San Juan, Midland, CA Atlanta, Gimnasia Jujuy, Central Norte y los clubes brasilenos detectados en Betano.
- Se endurecio `TeamNameMatcher` para no confundir sufijos regionales distintos; `Athletico PR` fue verificado contra diez filas de `Athletico Paranaense`, sin mezclarlo con `Atletico-MG`.
- De 11 falsos descartes revisados, 9 ya recuperan historial util. Solo siguen realmente incompletos `Tristan Suarez` (`0/0`) y `Liverpool Montevideo` (`0/0`). No crear alias `Liverpool Montevideo -> Liverpool`, porque mezclaria al club uruguayo con el ingles.
- La compilacion final de `CornersPredictionApi` termino con 0 warnings y 0 errores.

## Verificacion requerida

- Levanta API y web si estan apagadas.
- Levanta la API pasando `API_FOOTBALL_KEY` por variable de entorno; no guardes la clave en este archivo ni la hardcodees.
- Confirma API `200 OK` en `/health`.
- Confirma que la web responde y redirige al login si no hay sesion.
- Revisa la cuota antes de cada tanda.
- Reporta por mes: descubiertos, procesados, insertados, actualizados, omitidos, errores, offset final y cuota restante.
- Audita duplicados por `ApiFootballFixtureId`.
- Continua trabajando hacia atras hasta llegar al margen diario o recibir HTTP `429`.

---
