# Premier League: resultados presentes, enlaces por nombre ausentes

## Evidencia del 21 de septiembre

Se consultó directamente API-Football con `GET /fixtures?league=39&season=2026&from=2026-09-18&to=2026-09-20`. Respondió HTTP 200, sin errores, con los 10 partidos de la jornada finalizados. Los diez también estaban en `MatchHistory`, con goles y córners disponibles. El problema comprobado no era falta de cobertura del proveedor.

| Nombres de los picks | Nombre en API-Football / MatchHistory | Fixture oficial |
| --- | --- | --- |
| Tottenham Hotspur–Aston Villa | Tottenham–Aston Villa | 1557416 |
| Newcastle United–Hull City | Newcastle–Hull City | 1557414 |
| Everton–Ipswich Town | Everton–Ipswich | 1557410 |
| Leeds United–Crystal Palace | Leeds–Crystal Palace | 1557412 |

Las ocho variantes permanecían diferentes al pasar por `fn_CanonicalTeamName`. Tampoco estaban en los alias explícitos del matcher C#. Los otros seis partidos sí aparecían enlazados y liquidados automáticamente.

También se confirmó un error de zona horaria en Generales: la evaluación 767311 de Leeds guardó `PredictionTimestampUtc=2026-09-20 12:42:10`, mientras `MatchDate=2026-09-20 10:00` era hora de Santiago (13:00 UTC). Se comparaban directamente ambas columnas y una predicción hecha 18 minutos antes se descartaba como posterior.

## Corrección

- Añadir exclusivamente las cuatro equivalencias verificadas al catálogo canónico y al matcher; se conservan los nombres largos operativos. No se elimina United/Town/Hotspur como regla general. Pruebas negativas para otros clubes, juveniles, femenino y reservas.
- Usar los alias SQL en el conjunto de partidos y fechas seleccionado por Generales. Página, orden por resultado y laboratorio comparten una resolución oficial; dos IDs de fixture distintos mantienen el caso como ambiguo.
- El laboratorio resuelve también evaluaciones que carecen de un ID almacenado. El ID obtenido se mantiene separado del registro original, sin reescribir la evidencia del modelo.
- Comparar la predicción con el inicio convertido desde Santiago a UTC; mantener la exclusión de predicciones posteriores al inicio y la procedencia/fecha del resultado oficial.
- El catálogo se instala con la inicialización existente. No se cambia `CatalogVersion` ni se ejecuta una normalización masiva del histórico.

Las liquidaciones manuales existentes mantienen su prioridad. Durante la revisión el usuario liquidó varios de estos picks: no reemplazarlas por un resultado automático ni atribuir esas operaciones a esta reparación.

## Validación y activación

- Compilación de solución y API sin advertencias ni errores; 66 pruebas del motor/liquidación correctas.
- `GeneralPicksInteraction.Tests --alias-sql`: página normal, orden global, laboratorio, alias, fixture ID exacto, rechazo de identidad ambigua o fecha distinta, predicción 18 minutos antes frente a posterior al inicio, prioridad manual e inmutabilidad. Datos ficticios y aliases de prueba revertidos íntegramente.
- La primera ejecución de esa prueba agotó 120 segundos al ordenar. Se eliminó un JOIN redundante contra el histórico de evaluaciones y hints que forzaban recorrer índices completos para recuperar seis IDs. La regresión completa pasó después en aproximadamente 20 segundos. Esto no demuestra que todos los filtros de aprobadas sean rápidos.
- API reiniciada; el catálogo SQL confirma las cuatro equivalencias. Log `/private/tmp/corners-api-20260921-premier-aliases.log`.
- Verificación HTTP real de Generales para E / córners local / 20 de septiembre: devolvió 83 filas. La evaluación **767306**, Leeds–Crystal Palace, ahora muestra **Under 5,5 → Win, actual 4, fuente ApiFootball, +0,85u**. Se resolvió por el resultado oficial, sin escribir un resultado manual ni cambiar el registro de evaluación.
- Al comprobar la reparación dirigida, los nueve picks publicados afectados ya estaban liquidados manualmente por el usuario. El motor oficial revisó 0 pendientes de ese grupo; no se aplicó ninguna liquidación automática sobre esas filas.

## Otros hallazgos para una tarea posterior

La auditoría de código identificó posibles limitaciones adicionales, que no explican los cuatro enlaces de Premier comprobados y no se modifican aquí: la reconciliación remota parte de selecciones publicadas pendientes; el importador histórico exige estadísticas completas y puede saltar una liga tras dos pruebas incompletas; y la sincronización exclusiva de goles puede sobrescribir flags de estadísticas que no consultó. Validar cada caso por separado antes de ampliar cambios. La lentitud ya documentada de ciertas consultas de aprobadas sigue siendo un pendiente independiente.
