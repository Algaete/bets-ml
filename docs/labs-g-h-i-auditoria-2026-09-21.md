# Laboratorios G, H e I: evidencia útil y límites

Auditoría iniciada el 21 de septiembre de 2026 (Chile), 22 de septiembre UTC. Los conteos son una fotografía de una base que sigue recibiendo observaciones. No se entrenaron modelos, alteraron umbrales ni habilitó publicación.

## Qué sirve de cada laboratorio

| Laboratorio | Qué representa | Qué permite estudiar |
| --- | --- | --- |
| G | Colector prospectivo de goles; falta el artefacto entrenado activo | Datos anteriores al partido para contrastar mercado, modelos base y contexto, después de validar linaje y resultados |
| H | Challenger de calibración para córners | Probabilidades y economía virtual de la primera aprobación por partido/configuración, comparadas con el mercado en las mismas observaciones |
| I | Señales de movimiento de cuotas | Si un movimiento observado antes del partido anticipa resultados; su puntuación no es una probabilidad calibrada |

Ninguno debe evaluarse contando snapshots repetidos como apuestas independientes. Tampoco se deben sumar las ventanas de 7/30/90 días: se superponen. H usa fecha del partido; I usa fecha de predicción. Las versiones se analizan por separado.

## G: mucho volumen no equivale a un modelo entrenable

- Runtime HTTP confirma `Available=false`, `State=Missing`; no existe artefacto activo G. Las probabilidades que conserva la ruta de abstención son el ancla de mercado, no una predicción propia calibrada.
- Metadatos del índice filtrado: aproximadamente 244.000 observaciones. Una consulta posterior encontró 131.195 pendientes sin ID oficial y 13.356 pendientes con ID: alrededor del 91% de los pendientes carece del enlace que exige el worker G. Los conteos proceden de lecturas en instantes distintos y no constituyen un corte transaccional global.
- Esperar resultados no resuelve por sí solo ese enlace. El arreglo de identidad de Generales no reparaba automáticamente el worker de G.
- El universo disponible todavía debe pasar el export/preflight de `tools/BotGTrainingExport` y `scripts/train_bot_g.py --preflight-only`: contrato v1.1, dos lados de la cuota, linaje de modelos, Football Intelligence anterior a la predicción y resultado conocido después. No se ejecutó un entrenamiento ni se declaró válido todo el histórico por su volumen.
- Siguiente trabajo de datos: reconciliar IDs de G con la misma disciplina de alias/fecha/ambigüedad, sin inventar features históricas; exportar y cuantificar cuántos partidos cumplen realmente el contrato.
- La consulta acotada del **15 al 30 de septiembre** respondió en 33,185 s: **69.201 observaciones, 370 partidos, 123 con algún resultado, 27.617 observaciones liquidadas y cero aprobaciones**; todas las observaciones se abstienen y no hay probabilidades calibradas propias comparables. Ésta sí es una fotografía del mismo resultado SQL. La web abre por defecto con la última semana más los próximos partidos, muestra el rango y permite ampliarlo. No se presenta esa muestra como todo el histórico ni como un dataset ya aprobado para entrenar.

## H: la cobertura cambia la interpretación

Se encontraron 27.473 capturas, 58 registros aprobados y sólo 11 primeras aprobaciones por partido/configuración: 1 de v1.0 y 10 de v1.1. Estos son denominadores diferentes.

Antes de reparar los enlaces, v1.1 mostraba 5 resultados y 5 `Unmatched`. Los cinco resueltos sumaban 3 victorias, 2 derrotas, +0,315u sobre 2,5u de stake virtual (+12,6%). Error cuadrático modelo 0,26334 frente a mercado 0,25791: diferencia +0,00543, ligeramente desfavorable al modelo. No es una evaluación completa ni una muestra suficiente para decidir una recalibración.

Los otros cinco sí existían finalizados con córners en MatchHistory: Motherwell–Dundee United, Manchester City–Coventry, Bayern–Bodø/Glimt, Bolton–West Ham y Nottingham Forest–Coventry. El problema era el enlace de nombres. La versión v1.0 tiene una observación pendiente de estadísticas, que no debe mezclarse con v1.1.

Con los alias y los identificadores de competición verificados, la lectura corregida recupera **10/10 resultados de v1.1: 6 victorias, 4 derrotas, +0,585u sobre 5u, yield +11,7%**. El error pareado pasa a 0,24554 frente a 0,25081 del mercado (diferencia −0,00527, diez pares). Esta es la comparación completa de esas diez primeras aprobaciones; sigue siendo una muestra muy pequeña. Sirve para seguir observando H, no para ajustar umbrales y declarar validado el ajuste sobre esos mismos partidos.

El explorador de umbrales conserva un corte 70/30 que se calcula después de filtrar. Cambiar los umbrales también cambia esa partición: sus resultados se etiquetan como exploratorios y no como una validación independiente. Para evaluar una modificación hace falta congelar configuración y corte temporal antes de observar los resultados futuros.

Las diez aprobaciones miden la política de selección de H. Para estudiar una calibración hay que incluir también las evaluaciones rechazadas con probabilidades y resultados válidos, agrupar las capturas repetidas y separar fechas de ajuste y de evaluación. Calibrar sólo con las aprobadas introduciría selección. Las 27.000 capturas no son 27.000 partidos independientes ni se ha certificado todavía cuántas sirven para ese conjunto de calibración.

## I: cobertura oficial y movimientos de mercado

La consulta devolvió 2.456 observaciones aprobadas en total. Su primera página de 1.000 (predicciones del 15 al 22 de septiembre UTC) abarca 176 identidades de partido: 422 filas resueltas, 475 sin ID oficial, 17 sin coincidencia y 86 pendientes. Hay múltiples snapshots por partido; no son 422 apuestas independientes y esa página no permite calcular el rendimiento global.

La antigua lectura dejaba permanentemente sin resultado las observaciones capturadas sin ID. La nueva resolución conserva el ID auditado y expone por separado el ID recuperado: ID explícito primero; en su ausencia, alias explícitos de ambos equipos, competición y fecha. Para los nombres calificados de Inglaterra/Premier League, Inglaterra/Championship, Escocia/Premiership y UEFA/Champions League se exige el ID oficial 39, 40, 179 y 2 respectivamente. No se convierte «Premier League» en un alias global inglés. Una coincidencia ambigua queda sin economía. Se comprueban kickoff UTC auditado y disponibilidad temporal del resultado; tampoco se revela un valor real conocido después del corte solicitado. MatchHistory conserva fecha sin hora: no se inventa un kickoff a medianoche a partir de ese campo.

No existe una métrica de comparación contra la cuota de cierre (CLV) ni una probabilidad calibrada de I. Su `SignalScore` mide movimiento. Un retorno virtual positivo sirve para formular una hipótesis y comprobarla después; no basta para afirmar una ventaja estable.

La verificación final de 30 días devuelve **12.191 observaciones, 857 partidos evaluados, 483 primeras aprobaciones y 248 resueltas**. Las resueltas suman 127 victorias, 121 derrotas y **−17,76u / 248u = −7,16%**. Quedan 224 primeras aprobaciones sin enlace utilizable y 11 futuras; la cobertura resuelta es 51,35%. La pérdida describe esa parte observada, no un resultado completo de las 483.

| Segmento de I, 30 días | Resueltas | P/L virtual | Yield |
| --- | ---: | ---: | ---: |
| Goles totales | 163 | −13,36u | −8,20% |
| Córners totales | 85 | −4,40u | −5,18% |

En siete días hay 88 resueltas y −5,27%. La ventana de 90 días coincide con la de 30 porque aún no hay una historia independiente adicional; no son dos validaciones diferentes. Prioridad: recuperar resultados faltantes y estudiar la señal como variable auxiliar, antes de intentar convertirla en una probabilidad o cambiar umbrales para mejorar retrospectivamente el rendimiento.

## Cambios de lectura y presentación

- Resumen interpretativo en cada lab: muestra útil, resultados disponibles, conclusión y siguiente paso; los detalles quedan debajo.
- G separa colector/modelo; excluye el ancla de mercado sin calibración de las métricas del modelo. Brier/log-loss usan pares comparables de líneas binarias .5 con Win/Loss. El volumen deja de sugerir una promoción.
- H compara el error del modelo y del mercado sobre las mismas filas. Mantiene la etiqueta de resultado equivalente asiático donde corresponde, evitando llamarlo probabilidad binaria de acierto.
- I muestra primeras aprobaciones por partido/versión, partidos resueltos distintos, cobertura y causas de resultados ausentes. Los nuevos denominadores son anulables para no inventar ceros al conectar contra un backend anterior.
- Se diferencian explícitamente fallos de consulta, ausencia de datos y rendimiento cero.

## Rendimiento y verificación

Durante el diagnóstico Azure SQL permanecía al 99–100% de lectura de datos, con CPU considerablemente menor. Status de H/I y scorecards agotaban sus límites; eso no probaba que estuvieran vacíos.

H e I pasan a reconciliar sólo primeras aprobaciones para sus scorecards, manteniendo los contadores y medias de todo el universo. I incorpora un índice estrecho sin JSON; G usa su índice de scorecard existente. H/I comparten consultas simultáneas y conservan resultados correctos en caché durante dos minutos, con su fecha original visible; nunca cachean errores. No se cambió la capacidad ni el coste de Azure.

Medición final HTTP, con el proceso de bots activo: **I 17,731 s** frente a timeout anterior de 90 s; **H 63,433 s**, todavía lento en la primera lectura. Ambos respondieron en alrededor de 1 ms al repetir desde caché. En H se alineó la espera del bloque asíncrono a 90 s para que la web pudiera recibir el resultado de esa primera lectura; esto no se presenta como una mejora del tiempo SQL. El histórico amplio de G todavía agotó 120 s: sigue pendiente su rendimiento, además de la reconciliación de sus IDs. No dar por resuelta toda la lentitud del proyecto.

La regresión SQL de identidad I usa un esquema aislado, 17 casos y rollback completo: ID exacto, alias, competición por ID, ambigüedad, duplicados, medianoche UTC/local, resultado futuro, predicción tardía, estadísticas ausentes y auditoría sin cambios. Compara también todas las columnas del scorecard optimizado con el cálculo mediante la función de auditoría en cinco escenarios de versiones, ventanas, empates y cortes históricos. Se ejecuta con `dotnet run --project tests/BotILabResolution.Tests -- --sql`.

Las referencias metodológicas de [calibración de probabilidades](https://scikit-learn.org/1.8/modules/calibration.html) y [validación temporal](https://sklearn.org/stable/modules/cross_validation.html) explican por qué Brier no mide exclusivamente calibración y por qué los datos futuros deben reservarse para validar ajustes. Ninguna cifra de rendimiento de este informe procede de esas referencias: son observaciones del proyecto.
