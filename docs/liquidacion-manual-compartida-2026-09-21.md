# Liquidación manual compartida de Bot Picks generales

## Uso

En Bot Picks generales, un administrador puede abrir **Liquidar partido** desde una señal. La ventana muestra el partido, fecha, liga, mercado, bots relacionados y cantidad de picks publicados. Se ingresa la estadística final de ese mercado y una fuente o motivo, y se guarda con **Liquidar para todos los bots**.

El dato es una estadística, no una orden de marcar todas las apuestas como ganadas o perdidas. Por ejemplo, «goles visita = 1» liquida todos los Over/Under de goles visita del partido, usando la línea y cuota de cada pick. Un mercado distinto, como goles local, goles totales o córners, necesita su propio dato. Un resultado desconocido permanece pendiente; no se convierte en cero ni en pérdida. El cero confirmado sí es válido.

Las señales fuera de la página o filtros actuales también comparten el resultado. Cada pick publicado conserva su stake. Se soportan líneas enteras y cuartos, incluidos Push, HalfWin y HalfLoss. La opción **Corregir resultado compartido** recalcula ese partido/mercado y conserva las entradas anteriores en la auditoría.

En **Productivo**, el campo de resultado y **Liquidar para todos** usan el mismo servicio. Tras guardar se recargan los bots y, al volver a Generales, también su tabla y laboratorio. Una fila ya liquidada muestra **Corregir para todos**: permite corregir el dato compartido y no significa que siga pendiente.

## Identidad, persistencia y protección

- Se utiliza el identificador de API-Football cuando existe. Si falta, el enlace exige coincidencia de fecha/hora, liga, local y visita; no se hace una coincidencia aproximada de nombres. Si aparecen identificadores contradictorios, se rechaza la operación.
- La operación se guarda en `GeneralBotFixtureManualSettlements`, fuera del histórico oficial y de las evaluaciones inmutables del modelo. Incluye actor autenticado, motivo, fecha y clave de reintento. La actualización de picks publicados es transaccional.
- Las evaluaciones posteriores del mismo partido/mercado leen el resultado compartido. Los picks publicados posteriormente lo reciben antes de la reconciliación automática. Los resultados manuales conservan la protección contra sobrescritura por el proveedor.
- Ambos endpoints públicos comparten siempre el resultado. Generales impone `ApplyToFixture=true` incluso si un cliente anterior omite el campo o envía `false`. Productivo conserva el cuerpo `{actualValue}` y su respuesta, pero delega en el servicio compartido. Los endpoints web mantienen Admin y protección CSRF para guardar. El repositorio individual se conserva para compatibilidad interna y lectura de su auditoría histórica.
- Tabla, laboratorio de aprobadas y planes publicados se recargan o invalidan al guardar. Las tablas de diagnósticos científicos que consumen exclusivamente evidencia oficial mantienen sus reglas de fuente; esta función no altera esas reglas ni el umbral productivo del 3%.

## Validación

- Prueba SQL real con tres bots ficticios: propagación de Over/Under y distintas líneas, ausencia de identificador en una señal, medias ganancias, stake propio y stake cero, consulta general y autor de la liquidación.
- Reintento idempotente, corrección a cero, auditoría original intacta, rechazo de partidos futuros/ambiguos y herencia en evaluaciones y picks publicados posteriores.
- Todas las filas y liquidaciones ficticias se revirtieron mediante una transacción; la migración e índice necesarios sí permanecen instalados.
- Pruebas JavaScript del flujo: vista previa antes de guardar, resultado cero, CSRF, reintento, recarga, mensajes escapados y bloqueo si no se puede verificar el partido.
- Suite de liquidación existente: 65 pruebas correctas. Proxy y permisos web: 10 pruebas correctas, incluidos clientes anteriores y actor/errores de Productivo. Regresiones del dashboard verifican que el repositorio publicado llama al servicio compartido.
- Regresión SQL adicional: la ruta Productivo liquida otros bots y señales no publicadas, también cuando ningún registro tiene ID de API-Football. Se comprueba el actor y el cálculo económico propio de F.
- Regresiones JS adicionales: liquidar C refresca F, elimina pendientes obsoletos y distingue corregir de liquidar; Generales y laboratorio recargan al regresar desde Productivo, descartando respuestas antiguas.
- Compilación sin errores ni advertencias. Vista previa HTTP real: 25 evaluaciones de cinco bots para Llaneros–Atlético Nacional, córners totales, en 2,44 segundos; fue una consulta, sin guardar resultados. API `/health` 200 y web 302 a login.
- No se pudo hacer inspección visual automatizada: el navegador de la sesión no estaba disponible. Se comprobaron el código real de la interfaz y sus interacciones mediante las pruebas JavaScript.
- La lectura general sin filtro de decisión respondió en 4,31 segundos. Una consulta de aprobadas F/GOALS del 18–20 de septiembre excedió 60 segundos durante la comprobación final; sigue pendiente revisar esa lentitud. No considerar resuelto el rendimiento de todas las combinaciones de filtros por haber validado la vista previa de liquidación.

La migración es `CornersPredictionApi/sql/20260921_fixture_manual_settlements.sql` y se registra en la inicialización habitual de la API. La búsqueda entre bots cuenta con un índice pequeño por identificador y mercado, para evitar escanear el histórico de evaluaciones.

Para repetir las pruebas SQL aisladas:

```sh
dotnet run --project tests/GeneralPicksInteraction.Tests/GeneralPicksInteraction.Tests.csproj -- --fixture-sql
```

Este modo aplica la migración idempotente, crea datos ficticios dentro de una transacción y revierte todas las escrituras de prueba.

## Incidente Sabadell–Real Oviedo

El 21 de septiembre se confirmó que Productivo todavía usaba `sp_ResolveAutomatedCornerBetSelection` para una sola selección, mientras Generales ya usaba el servicio compartido. El usuario había liquidado separadamente las selecciones 3840–3843 (F/C/D/E), goles visita Over 0,5 del partido del 20/09/2026 a las 09:00. No había ninguna entrada compartida. La ruta se corrigió y se compartió el valor **1**, confirmado expresamente por el usuario y presente en las cuatro liquidaciones originales, con una nueva entrada auditable de reconciliación. Las 32 evaluaciones del mismo partido/mercado heredan ese resultado; no se asignaron estadísticas a otros mercados. Las demás liquidaciones individuales antiguas no se migraron en masa.
