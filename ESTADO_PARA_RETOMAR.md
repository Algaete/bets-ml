# Estado del proyecto para retomar

Actualizado el **14 de septiembre de 2026, aproximadamente 18:07 de Chile (UTC−03)**. Las secciones inferiores conservan el historial de sesiones anteriores.

## Reparación verificada del 14 de septiembre

- Ver `docs/reparacion-calibracion-2026-09-14.md`: la carga inicial C/F terminó y se verificó con una tanda real sin errores. C entrega 37.060 observaciones y F 37.356; ya no se sustituyen timeouts por listas vacías.
- Hay aprobaciones nuevas E/F de visita Over 0,5 en Bahia–Remo (`587532` / `587496`), con calibración disponible. Siguen `ProductionBlocked` por las métricas productivas anteriores; no se cambiaron umbrales. F Over falla brecha/Brier y F Under tiene 39/100 partidos.
- El job `cca14c03-fe82-4eef-bc4b-f81cc3cad6c9` continúa en segundo plano. Su primer lote terminó con 0 errores y el segundo preparó sus datos en 8,4 segundos. No dar por terminados los 16 lotes sin consultar su estado actual.
- API y robots en `http://localhost:5070`, web en `http://localhost:5130`. Log API actual: `/private/tmp/corners-api-calibration-final.log`. Comprobar procesos/puertos al volver; los identificadores de sesiones anteriores ya no son referencias válidas.
- La caché derivada, los triggers y el procedimiento de escritura ya están desplegados en Azure SQL. El avance queda persistido tras un apagado y la API prepara la caché de las evaluaciones nuevas al guardarlas.
- Se activó actualización asíncrona de estadísticas SQL con baja prioridad; quedó documentada y es reversible. No se cambió el nivel S0 ni el firewall.
- Pruebas: build limpio, 65 de motor/liquidación, 21 de rendimiento, 42 del plan productivo, equivalencia SQL de 85 observaciones en 12 mercados y escritura/invalidación real probada con rollback.

## Actualización: diagnóstico del 14 de septiembre, 15:48 de Chile

**Actualización posterior:** la reparación de calibración fue verificada; ver la sección superior y `docs/reparacion-calibracion-2026-09-14.md`. El siguiente diagnóstico describe el estado anterior a esa reparación.

- Ver `docs/diagnostico-productivo-2026-09-14.md` para evidencia y métricas actualizadas; tiene prioridad sobre los conteos antiguos de este documento.
- Azure SQL vuelve a responder; este diagnóstico no modificó el firewall.
- Para 14–21 de septiembre se encontraron 62 señales aprobadas de goles visita (38 Over, 24 Under), todas bloqueadas productivamente, y 0 publicadas.
- En C Over la versión actual tiene 23 partidos y yield 6,35%; la antigua tiene mejor evidencia, pero no es la configuración actual. F Over tiene 64 partidos y yield 9,27%, pero falla por poco calibración y Brier. Under necesita ≥100 y Green.
- Se confirmó otra vez el fallo de calibración: logs de timeout C/F y evidencia de E con `inputRows=0`; algunos candidatos se rechazan únicamente por esa falta de datos. El límite de 10 segundos del último commit no resolvió el problema.
- No se alteraron filtros ni código durante la revisión. Prioridad: resolver carga de calibración y diferenciar el fallo operativo de insuficiencia real antes de atribuir el cero productivo sólo a estadísticas.

## Actualización después del reinicio: 13 de septiembre, 20:37 de Chile

- El usuario volvió y pidió levantar API, web y robots para probar localmente.
- Se compiló la solución con .NET 8.0.410: 0 errores, 0 warnings.
- API levantada con perfil `http` en 5070; `/health` devuelve 200.
- Web levantada con perfil `http` en 5130; `/RobotPanel` redirige al login y la página de login devuelve 200.
- **Nuevo bloqueo de acceso:** Azure SQL devuelve error 40615 porque la IP actual `181.162.93.155` no está permitida. Los workers arrancan pero no pueden consultar la base. La salud HTTP de la API no implica acceso SQL.
- Azure CLI está instalada y tiene sesión activa. Servidor `mldataset-algat3`, grupo de recursos `Algat3`. Se revisaron las reglas: hay IP anteriores, falta la actual.
- Se preparó agregar la regla `LocalDev_Mac_2026-09-13` con inicio y fin `181.162.93.155`. **La revisión automática rechazó crearla por falta de autorización explícita para el cambio persistente de firewall; no se modificó Azure.**
- Pendiente inmediato: aprobación del usuario para esa regla de una sola IP; después comprobar SQL y la inicialización del robot, reiniciar la API si hace falta. Los problemas de calibración/rendimiento documentados abajo siguen siendo pendientes distintos.
- Sesiones de herramientas de este arranque: API `98301` (PID observado 8503), web `24372` (PID observado 8517). Son referencias temporales; comprobar procesos antes de reutilizarlas.
- No hubo cambios de código ni nuevo commit/push en este arranque.

## Resumen y prioridad

La API y la web están levantadas. Los cambios de código están publicados en GitHub. Se implementaron mejoras del panel, Bot Picks generales, orden por columna, liquidación manual, laboratorio de aprobadas y consultas SQL.

**El problema de la calibración histórica de E/F/H sigue pendiente.** Se limitó la espera y se redujeron consultas repetidas, pero las verificaciones reales siguieron devolviendo cero observaciones por timeout. Eso puede provocar `REJECTED_CALIBRATION_SAMPLE_LOW` aun cuando existe historial. No dar este punto por arreglado.

También hay bloqueos productivos independientes de ese fallo: existen candidatos aprobados por C/D que no pasan las reglas de publicación por mercado, lado y versión. No confundir aprobación del modelo con publicación productiva.

La ejecución que se estaba siguiendo **ya terminó**. No quedó en el lote 9/21 indicado en el mensaje anterior: al preparar este archivo se verificó `Completed`, **21/21 lotes**, **0 publicaciones** y **0 errores contabilizados**. Ese contador no incluye necesariamente fallos de calibración absorbidos como resultado vacío.

## Repositorio y respaldo

- Carpeta: `/Users/alfonsogaeterodriguez/Desktop/corners-model-v1_`.
- Remoto: `https://github.com/Algaete/bets-ml.git`.
- Rama: `main`.
- Último commit de código: `44b6863be1b014f7623d0a60b66651e36b1733ce` — `fix: keep bot runs fresh and responsive`.
- Commit anterior principal: `1b67348` — `feat: improve bot automation and general picks`.
- Antecedentes: `6acf2a9` — bots activos/diagnósticos; `a5ec9cb` — selección productiva.
- El push de `44b6863` se completó después de la autorización explícita del usuario. `main` y `origin/main` estaban sincronizadas y el árbol limpio antes de crear este archivo.
- Este archivo de continuidad es nuevo y se deja guardado localmente; su creación ocurre después del push anterior.
- `.env`, entornos Python y otros recursos locales están excluidos de Git. No asumir que un clon contiene toda la configuración o los modelos locales. No copiar secretos al documento ni al repositorio.

## Cómo levantar después del reinicio

Se usa **.NET SDK 8.0.410** (`global.json`), proyectos `net8.0`. Un diagnóstico temporal intentó `net9.0` y falló; no cambiar el framework para iniciar el proyecto.

Desde la carpeta del repositorio:

```bash
cd /Users/alfonsogaeterodriguez/Desktop/corners-model-v1_
dotnet build CornersPrediction.sln --no-restore
```

Si faltan dependencias restauradas, ejecutar `dotnet restore CornersPrediction.sln` y volver a compilar. En este Mac ya estaban restauradas.

En una terminal, API:

```bash
cd /Users/alfonsogaeterodriguez/Desktop/corners-model-v1_
dotnet run --no-build --project CornersPredictionApi/CornersPredictionApi.csproj --launch-profile http
```

En otra terminal, web:

```bash
cd /Users/alfonsogaeterodriguez/Desktop/corners-model-v1_
dotnet run --no-build --project CornersPrediction.Web/CornersPrediction.Web.csproj --launch-profile http
```

- API: `http://localhost:5070`.
- Swagger: `http://localhost:5070/swagger`.
- Web: `http://localhost:5130`.
- Panel: `http://localhost:5130/RobotPanel`.
- Procesos: `http://localhost:5130/BotAutomation`.
- Picks: `http://localhost:5130/BotPicks`.
- **La API del robot está integrada en la API de 5070 en este repositorio.** No fue necesario levantar otra instancia en 5005 para esta ejecución. No mezclar con proyectos antiguos de otras carpetas.

API y web cargan el `.env` local. La API conserva precedencia de las variables explícitas del proceso con `Env.NoClobber()`. La automatización usa Azure SQL; la API también inicializa su almacenamiento SQLite local. Los modelos usan recursos Python locales, incluidos `.venv-legacy` y los directorios de modelos configurados. Si falla inferencia, revisar esas rutas antes de reinstalar.

Comprobación básica:

```bash
curl -s -o /dev/null -w 'API HTTP %{http_code}\n' http://localhost:5070/health
curl -s -o /dev/null -w 'Web HTTP %{http_code}\n' http://localhost:5130/RobotPanel
```

Antes del apagado: API `200`, web `302` a login, comportamiento esperado sin sesión. Los identificadores de sesiones de herramientas y los PID antiguos no sirven después del reinicio.

## Automatización y última ejecución comprobada

Job seguido durante la reparación:

```text
RecommendationJobId: c386a9ac-b6f4-47e5-9a47-a758aa40566b
Nombre: Live automático 2026-09-12
Modo: Live
Rango: 2026-09-12 a 2026-09-19
Bots: A, C2026, D2026, E2026, F2026, G2026, H2026
Familias: CORNERS, GOALS, SHOTS, SOG
BatchSize: 10
Estado final: Completed
ProcessedBatches / TotalBatches: 21 / 21
NextBatchNumber: 22
SelectedMatches: 0
InsertedRows: 0
UpdatedRows: 0
SkippedMatches: 820
ErrorMatches: 0
AttemptCount: 0
LastRunId: 72c4e15c-da87-4cc6-9e94-4a1e7dfd970c
CompletedAtUtc: 2026-09-13T04:38:02
```

Los contadores de publicación no reflejan todas las evaluaciones del modelo. `SkippedMatches` tampoco debe presentarse como cantidad de partidos únicos sin revisar su agregación por bot/mercado.

El worker está habilitado. La recurrencia está configurada cada **360 minutos**, próximos **7 días**, lotes de **10**, máximo **3 intentos**; obtiene bots habilitados del mantenedor. Al volver a iniciar puede ejecutar nuevo trabajo automáticamente. No reencolar el job terminado sólo porque antes estaba en progreso: primero consultar los procesos actuales.

La recuperación de reservas locales distingue procesos que siguen vivos de procesos terminados. `LeaseMinutes=5`, heartbeat cada 15 segundos. Se comprobó recuperación tras varios reinicios de API.

## Lo implementado en Bot Picks y panel

En `1b67348` y cambios previos:

- Bot Picks generales tiene tabla ordenable por columna, filtros y paginación del servidor.
- El estado del modelo y el de publicación se exponen por separado. Un candidato aprobado puede estar bloqueado o en seguimiento productivo.
- Se agregó liquidación manual con identidad del operador y persistencia auditable para resultados no disponibles. La reconciliación automática protege las liquidaciones manuales.
- Se conserva la distinción entre cero real y estadística ausente. Sin dato oficial relevante o estado final, la liquidación automática queda pendiente.
- Se agregó Data Science Lab de aprobadas: muestra, señales independientes, liquidaciones, P/L, yield, acierto, calibración/Brier, curva diaria/acumulada y agrupaciones. Es análisis de aprobadas; no equivale al rendimiento de apuestas realmente publicadas.
- Carga del lab y diagnóstico separada de la tabla. En web, tabla y lab tienen límite de 60 segundos; diagnóstico, 15 segundos.
- El panel de ejecución usa trabajos persistentes y progreso por lotes. Hay información de errores por ejecución.
- Se quitó el veto basado únicamente en nombre de la casa de apuestas en la política productiva actual. Las reglas de frescura, instantánea de cuota, evidencia y límites siguen aplicándose.
- Se mantienen las cohortes históricas sin reescribir liquidaciones por cambios de política actual.

En `44b6863`:

- SQL de Bot Picks prepara claves e identidad en tablas temporales cubiertas por índices; evita una segunda lectura masiva de la tabla de auditoría con JSON grande para deduplicar.
- La vista general reduce evaluaciones repetidas del mismo bot/partido/mercado/lado/línea. Auditoría y análisis conservan su evidencia original.
- Consultas de disponibilidad/cuotas deduplican antes de resolver fixture e instantáneas. Cuando existe identificador del proveedor, se usa en la búsqueda de la instantánea.
- Lectura de scorecards mediante columnas compactas y caché de 30 minutos. Se retiró la lectura/interpretación masiva de `DecisionReason` de las selecciones; se usa el fallback de probabilidad almacenada y la evidencia científica aparte.
- Calibración comparte caché por bot fuente y día. Se preparan candidatos antes de leer columnas anchas y se conserva la caché persistente de probabilidades fuente por bloques.
- Actualización de cuotas durante jobs live cada 60 minutos, con exclusión de actualizaciones simultáneas y reutilización de cuota Pinnacle reciente tras reiniciar.
- Límite de edad de cuota robusta Pinnacle: 5400 segundos (90 minutos). El límite productivo consultado era 120 minutos. Son controles distintos.
- Pinnacle funcionó; Betano estaba deshabilitado en la configuración observada.
- Logs por candidato y excepciones repetidas de señales base de G pasan a `Debug`.

## Qué se comprobó sobre los picks

Durante la sesión se consultó **2026-09-13 a 2026-09-20**. Hubo 302, luego 306 y finalmente **320 filas aprobadas** en la vista general, mientras entraban evaluaciones nuevas. Se observaron goles totales, goles local/visita y córners totales/local/visita.

**Corrección de alcance:** ese rango comprende domingo y la semana siguiente; no es exclusivamente el fin de semana 12–13 de septiembre. Los números son mediciones de esas consultas, no un contador garantizado del estado final ni partidos únicos. En una consulta anterior del rango hubo 47 filas aprobadas de goles visita. La consulta de publicados devolvió 0 en ese momento.

Ejemplo confirmado de aprobación nueva:

- San Marcos de Arica vs Curicó Unido, goles visita Over 0,5, cuota 1,68.
- Evaluación C2026 `521391`: modelo aprobado; publicación bloqueada porque la cohorte controlada C/F no cumple conjuntamente muestra, yield mínimo 7%, calibración o Brier.
- Evaluación D2026 `521400`: modelo aprobado; bloqueada por muestra 19/100 y por no pertenecer a la prueba controlada C/F.
- Otro ejemplo fue Lille–Troyes, C2026 goles visita Over 0,5 a 1,75, probabilidad aproximada 0,693 y edge 0,147, también bloqueado productivamente.

Scorecards observados durante el diagnóstico, que deberán refrescarse al retomar:

- E2026 AwayTeamGoals agregado de 90 días: 110 observaciones, yield +10,3%, Brier del modelo 0,2363 frente a mercado 0,2407; figuraba Green agregado.
- Eso **no garantiza Green del segmento exacto por lado y versión**. Los segmentos exactos observados seguían Amber; la ventana corta también podía ser negativa.
- C Over de la cohorte actual: muestra 18, yield +15,17%, Brier 0,2219 vs mercado 0,2134, insuficiente y peor Brier.
- C Under: muestra 31, yield −5,23%, evidencia negativa.
- F Over: muestra 63, yield +7,87%, Brier 0,2371 vs 0,2341, peor que mercado.
- F Under: muestra 36, yield +6,36%, inferior al 7% y Brier peor.
- Otros bloqueos visibles: TotalGoals pausado por rendimiento, HomeTeamCorners pausado, familias en rojo, muestras por debajo de 100 y líneas asiáticas sin la evidencia requerida de cinco estados.

No relajar umbrales sólo para producir picks. Tampoco concluir que todos los rechazos son estadísticos: el fallo de carga de calibración está demostrado.

## Rendimiento medido y límites reales

- Disponibilidad de cuotas llegó a responder en aproximadamente 1,7 segundos.
- Bot Picks generales, con filtros 13–20 de septiembre: mediciones satisfactorias de 1,1; 2,2; 2,9; 6,2 y 11,3 segundos, según carga y consulta.
- Hubo consultas anteriores de 30/120 segundos que agotaron su espera mientras calibración saturaba I/O de Azure SQL.
- **Verificación final al redactar este archivo:** dos consultas concurrentes para el rango exacto 12–13 de septiembre, una de aprobadas y otra de publicadas, no completaron dentro del límite cliente de 25 segundos (`curl` exit 28, HTTP 000). No se obtuvieron conteos nuevos. API health y web seguían respondiendo. Esto no demuestra por sí solo un HTTP 500, pero confirma que la lentitud no está eliminada para todos los filtros/cargas.
- La ejecución terminó sin errores contabilizados, pero las pruebas reales de carga de calibración registraron cero observaciones por timeout.

Por tanto, describir el estado como **mejora parcial comprobada**, no como optimización completa del sistema.

## Pendiente principal: calibración E/F/H

Código actual en `SqlAutomationRepository.GetBotECalibrationHistoryAsync`:

1. Caché en memoria por bot fuente y fecha.
2. Carga SQL limitada por cancelación a **10 segundos**.
3. Si agota tiempo: devuelve lista vacía, log Warning breve y cachea vacío por **15 minutos**.
4. Carga exitosa: caché en memoria de **6 horas**.
5. En disco/base sólo existe la caché de probabilidades fuente por evaluación; no se implementó una caché persistente completa de observaciones listas para calibrar.

E y H usan fuente C2026; F usa F2026. Ante lista vacía los selectores calibrados pueden rechazar por `REJECTED_CALIBRATION_SAMPLE_LOW`. Acortar la espera favorece a los demás bots, pero no recupera la capacidad de E/F/H de evaluar con calibración.

Se probó aumentar temporalmente el límite a 60 segundos: ambas fuentes siguieron agotándolo y el lote esperó unos 125 segundos. Se volvió a 10 segundos. Hubo otro experimento de cachear vacío por seis horas, **descartado**; el código final usa 15 minutos para fallos.

Diagnóstico SQL observado:

- Aproximadamente 34.965 candidatos C indexados; una lectura tardó 49,86 segundos.
- Aproximadamente 26.167 candidatos C con resultado final enlazado.
- Tabla `AutomatedBotCalibrationProbabilityCache`: 45.374 filas; unas 22.539 asociadas a C.
- Esas cifras son una muestra del diagnóstico, no límites configurados.
- La presión de I/O de Azure SQL S0 y las búsquedas en filas anchas/JSON afectan también la tabla de picks.

**Se anunció durante la conversación la intención de persistir identidad/mercado/métricas junto con esa caché y eliminar el escaneo. No se llegó a implementar.** Al retomar, diseñar y probar esa solución o una alternativa que realmente cargue observaciones. No atribuir al commit ese trabajo pendiente.

Criterios para la corrección:

- No reemplazar probabilidad previa a calibración por `FinalProbability`.
- Conservar identidad oficial, prioridad del enlace de selección publicada y exclusión de identidades ambiguas.
- Respetar resultados corregidos, disponibilidad de cada estadística y corte temporal del modelo/partido.
- Un fixture aporta una observación independiente en el cálculo. No eliminar filas prematuramente: la elección depende de la probabilidad candidata.
- Diferenciar indisponibilidad de infraestructura de insuficiencia real de muestra en el diagnóstico.
- Revisar invalidación de cachés tras liquidación/corrección y verificar que optimizaciones no excluyan evidencia válida.
- Verificar con datos reales que C/F devuelvan observaciones y desaparezcan los rechazos causados sólo por el timeout.

Otros pendientes: G2026 no tiene `models/bot-g/active.json`; queda en shadow/abstención `ModelUnavailable`. Reducir sus logs no instala ni entrena ese modelo. H conserva su condición shadow y TotalCorners deshabilitado en el selector observado.

## Archivos importantes

- `CornersPredictionApi/RecommendationJobs/RecommendationJobWorker.cs`: progreso, recuperación, recurrencia y actualización de cuotas.
- `CornersPredictionApi/RecommendationJobs/RecommendationJobOptions.cs` y `appsettings.json`: opciones.
- `CornersPredictionApi/Robot/AutomatedCornersBot/SqlAutomationRepository.cs`: lectura de cuotas, cachés, calibración y persistencia.
- `CornersPredictionApi/Robot/AutomatedCornersBot/SqlAutomationRepository.Calibration.cs`: preparación SQL y bloques de probabilidad fuente.
- `CornersPredictionApi/sql/20260906_calibration_probability_cache.sql`: caché SQL actual e invalidación al actualizar/eliminar evaluación.
- `CornersPredictionApi/Robot/AutomatedCornersBot/AutomatedCornersSelectionService.cs`: evaluación por bot y publicación.
- `CornersPredictionApi/Robot/AutomatedCornersBot/BotGAutomationService.cs`: runtime de G.
- `CornersPrediction.Infrastructure/SqlServer/SqlServerAutomatedBotGeneralPicksRepository.cs` y `.Sorting.cs`: tabla y orden.
- `CornersPrediction.Infrastructure/SqlServer/SqlServerAutomatedBotGeneralPicksLabRepository.cs`: lab.
- `CornersPrediction.Infrastructure/SqlServer/SqlServerAutomatedBotPerformanceEvidenceRepository.cs`: evidencia científica.
- `CornersPrediction.Infrastructure/SqlServer/SqlServerAutomatedCornerSelectionsRepository.cs`: proyección compacta de selecciones/scorecards.
- `CornersPrediction.Application/AutomatedCorners/AutomatedBotPerformance.cs`: scorecards y criterios productivos.
- `CornersPrediction.Web/Services/BotPickProductionPlanner.cs`: plan productivo.
- `CornersPredictionApi/Controllers/AutomatedBotGeneralPicksController.cs`: tabla, lab, evidencia y liquidación manual.
- `CornersPrediction.Web/Controllers/BotPicksController.cs`, `Views/BotPicks/_Research.cshtml`, `wwwroot/js/bot-picks-research.js`: interfaz y carga.
- `CornersPredictionApi/SqlScripts/BotAutomationReadIndexes.sql`: índices de lectura.

Se aplicaron índices de instantáneas por SourceMatch, fixture próximo, lectura de selecciones y páginas de auditoría. No volver a lanzar a ciegas un índice grande de calibración: hubo intentos agotados a 600 segundos que se descartaron. Comprobar `sys.indexes`, migraciones y plan real antes de añadir/reconstruir índices.

Los proyectos temporales `tools/DbProbeTemp` fueron eliminados y no entraron al commit. `sqlcmd` y `gh` no estaban instalados; se usaron .NET/SqlClient y Git. No instalar herramientas sin necesidad para repetir el diagnóstico.

## Pruebas realizadas

Última compilación: `dotnet build CornersPrediction.sln --no-restore`, **0 errores y 0 warnings**.

Pruebas ejecutadas satisfactoriamente:

- `GeneralPicksInteraction.Tests`: combinaciones SQL de columnas/direcciones/filtros, rechazo de inyección, validación manual y resultados asiáticos.
- `RobotExecution.Tests`: recuperación de reservas sin liberar procesos vivos.
- `RobotExecution.Tests --sql`: 85 filas equivalentes de calibración en 12 mercados; tipos/JSON inválido y duplicado, ambigüedad de identidad y límites temporales; 9 bloques incrementales y 20 llamadas ordenadas en un viaje SQL.
- `BotPerformance.Tests`: reglas de scorecards y evidencia independiente de casa de apuestas.
- `BotPickProductionPlan.Tests`: **42 pruebas**.
- `BotPickSettlement.Tests`: **64 pruebas**, incluidos modelos y calibración.

Un test de liquidación falló por comparar texto SQL con saltos de línea concretos; se restauró el formato de las expresiones de la instantánea y volvió a pasar sin cambiar los asserts.

Comandos de referencia (usar `dotnet run --project ...` para compilar cada proyecto de prueba cuando cambie su código; algunos no están incluidos en la solución):

```bash
dotnet run --project tests/GeneralPicksInteraction.Tests/GeneralPicksInteraction.Tests.csproj
dotnet run --project tests/RobotExecution.Tests/RobotExecution.Tests.csproj
dotnet run --project tests/RobotExecution.Tests/RobotExecution.Tests.csproj -- --sql
dotnet run --project tests/BotPerformance.Tests/BotPerformance.Tests.csproj
dotnet run --project tests/BotPickProductionPlan.Tests/BotPickProductionPlan.Tests.csproj
dotnet run --project tests/BotPickSettlement.Tests/BotPickSettlement.Tests.csproj
```

La prueba SQL necesita conexión local configurada en `.env`; usa tablas temporales de prueba. Pasar pruebas funcionales pequeñas no garantiza latencia aceptable en el volumen real.

## Rutas útiles para continuar

Las rutas internas requieren `X-Internal-Api-Key` con el valor local configurado. No se guarda la clave en este documento.

```text
GET /health
GET /api/recommendation-jobs/c386a9ac-b6f4-47e5-9a47-a758aa40566b
GET /api/automated-corners/availability?batchSize=100
GET /api/automated-corners/general-picks?dateFrom=2026-09-13&dateTo=2026-09-20&page=1&pageSize=50&modelDecision=Approved&sortBy=EvaluationId&sortDirection=desc
GET /api/automated-corners/general-picks?dateFrom=2026-09-13&dateTo=2026-09-20&page=1&pageSize=50&publicationStatus=Published
GET /api/automated-corners/general-picks/lab
GET /api/automated-corners/general-picks/{evaluationId}/evidence
GET /api/automated-corners/performance/scorecards
PUT /api/automated-corners/general-picks/{recordId}/settlement
```

## Orden recomendado al retomar

1. Leer este archivo, comprobar `git status`, levantar API/web y validar salud y acceso a SQL/modelos.
2. Consultar jobs actuales; el job documentado ya está completado. Confirmar si la recurrencia creó otro.
3. Priorizar la recuperación efectiva de calibración E/F/H. No repetir indefinidamente cambios de timeout.
4. Reproducir la lentitud restante con rangos exactos y consultas concurrentes; revisar SQL e invalidación de cachés.
5. Reevaluar una tanda acotada con cuotas frescas y calibración disponible. Comparar razones antes/después por bot, mercado, lado y versión.
6. Volver a contar aprobadas y publicadas con fechas explícitas. Mostrar al usuario por qué cada etapa bloquea y no presentar datos históricos como estado actual.
7. Verificar interfaz, lab y liquidación manual en la web, además de las pruebas automatizadas relevantes.
8. Guardar cambios posteriores en Git y actualizar este documento con resultados y pendientes reales.

## Preferencias y alcance del usuario

- Quiere que los picks aprobados se vean en generales aunque el segmento esté pausado productivamente; el bloqueo explícito debe distinguirse.
- Quiere orden por columna, liquidación manual cuando falte resultado oficial y análisis de comportamiento de aprobadas.
- Quiere API, web y robot funcionando localmente después de reinicios, rendimiento utilizable y cambios respaldados en GitHub.
- Autorizó explícitamente el push de `44b6863` a `Algaete/bets-ml`, y se completó. No volver a pedir autorización para repetir ese envío ya realizado.
- Evitar afirmar que todo quedó arreglado: calibración y ciertos rangos de consultas siguen necesitando trabajo.

Prompt sugerido para la siguiente sesión:

> Lee ESTADO_PARA_RETOMAR.md, levanta API y web, y continúa por la calibración de E/F/H y la lentitud restante. Quiero comprobar qué aprobados nuevos hay y qué bloquea realmente la publicación productiva, sin confundir errores de infraestructura con falta de evidencia.
