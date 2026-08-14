# Bot F · Legacy ML calibrado

## Objetivo

Bot F permite comparar el stack ML anterior con Bot E sin cambiar el selector ni las reglas de calibración empírica. La única variable experimental principal es la fuente de predicción base:

- Bot E: artefactos `Models 2026`.
- Bot F: artefactos legacy desplegados antes del 19 de junio de 2026.

Bot F se identifica de extremo a extremo como `F2026` y usa la estrategia base `LEGACY_EMPIRICAL`.

## Artefactos base

| Familia | Versión legacy | Salidas |
|---|---|---|
| Córners | `legacy-corners-filtered-v1` | total, local, visita |
| Goles | `goals_v1` | total, local, visita |
| Tiros | `shots_v3_catboost` | total, local, visita |
| Tiros al arco | `sog_v1` | total, local, visita |

La configuración registra el bundle como `legacy-corners-v1+goals-v1+shots-v3+sog-v1`. El corte temporal conservador es `2026-06-11T16:36:16Z`, correspondiente al artefacto legacy más reciente del bundle. Ningún candidato de fecha igual o anterior puede evaluarse.

## Flujo

1. Obtiene cuotas históricas o próximas para los cuatro mercados.
2. Construye el contexto usando solo partidos anteriores al candidato.
3. Ejecuta los modelos legacy y adapta correctamente total/local/visita.
4. Calcula probabilidad base, contexto, no-vig, calidad, acuerdo, edge y EV.
5. Consulta evaluaciones anteriores de `F2026`, nunca de C o E.
6. Filtra evidencia por fecha y lag de disponibilidad de ocho horas.
7. Aplica el calibrador empírico jerárquico de E.
8. Persiste Approved, Rejected y PendingData; publica solo el mejor Approved por partido.
9. Liquida con MatchHistory usando Win, HalfWin, Push, HalfLoss o Loss.

## Aislamiento experimental

- `FeatureSchemaVersion`: `bot-f-legacy-features-1.0.0`.
- `ConfigurationVersion`: `bot-f-legacy-empirical-1.0.0`.
- `SourceBotKey` del calibrador: `F2026`.
- El esquema propio impide aplicar por accidente el meta-modelo de C/E entrenado sobre otra distribución.
- Las observaciones se deduplican por fixture dentro del calibrador.
- La evidencia futura se descarta aunque los lotes se ejecuten concurrentemente o fuera de orden.

## Ejecución web

En **Bots y procesos**, seleccionar `Bot F · Legacy ML calibrado`, los mercados deseados y el modo histórico o live. El proceso queda persistido, reanudable e idempotente. En **Bot Picks**, F tiene pestaña, tarjeta estadística, filtro de tabla, badge y liquidación por bot.

## Interpretación

F no debe considerarse ganador por una muestra pequeña. Comparar por familia, scope y lado usando al menos muestra resuelta, P/L, yield, drawdown, reliability y tier de evidencia. Las primeras fechas sirven para formar evidencia y pueden no publicar picks hasta superar los mínimos del calibrador.
