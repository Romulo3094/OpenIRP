# Iteración 21 — Estado actual de dependencias de tarea

Fecha: 2026-10-26. Desarrollo local en feature/paso26-identidad-organizaciones.

## Resultado

La API permite consultar los predecesores directos de una tarea y distinguir su estado actual de la historia de excepciones.
Un registro APPROVED puede seguir en el historial después de caducar; su existencia no prueba que el vínculo esté satisfecho.
Esta consulta complementa las excepciones individuales de Iteración 20 y prepara su revisión operativa.

## Endpoint

GET /api/organizations/{organization}/incidents/{incident}/executions/{execution}/tasks/{task}/dependency-readiness

Requiere sesión y membresía activa de la misma organización; admite ORG_ADMIN, ANALYST y VIEWER.
PLATFORM_ADMIN no obtiene acceso por su grant global. Se revisan cuenta, lockout y organización.
Los identificadores deben pertenecer al mismo incidente/ejecución; una tarea ajena no revela historial.
GET no requiere CSRF; la respuesta usa Cache-Control: no-store. El contrato está tipado en Swagger/OpenAPI Development.

## Contrato y significado

TaskDependencyReadiness contiene ExecutionId, VersionId, VersionStatus, ExecutionStatus, ExecutionRevision,
IncidentRevision, IncidentStatus, TaskId, TaskStatus, AssignedMembershipId y Dependencies.
Cada vínculo incluye DependencyId, PredecessorTaskId, PredecessorCode, PredecessorStatus y Satisfaction:

| Satisfaction | Significado en el snapshot consultado |
|---|---|
| Unsatisfied | El predecesor no está completado y no hay excepción vigente aplicable. |
| PredecessorCompleted | El predecesor directo está COMPLETED. |
| ApprovedException | El predecesor está SKIPPED y el predicado SQL confirma una excepción individual vigente. |

LatestRequestId, LatestRequestRevision, LatestDecisionId y LatestDecisionAction identifican la solicitud más reciente
por vínculo y su decisión, si existe. Son metadatos históricos: una solicitud sin decisión no se presenta como una
solicitud necesariamente vigente; APPROVED tampoco equivale por sí solo a ApprovedException.
Los motivos, impacto, riesgo y contexto completo se consultan en el historial existente de exceptions.

DependencyCount y UnsatisfiedCount resumen los vínculos. AllDependenciesSatisfied es true cuando ninguno está sin satisfacer;
una tarea independiente devuelve Dependencies=[] y true aunque todavía esté sin responsable.
RequiresHumanReview es siempre true. El resultado evalúa dependencias, no la autorización para transicionar tareas:
aprobación de paso, evidencia exacta activa, responsable elegible, estado de tarea/incidente/ejecución y versión siguen controlándose al escribir.
No es una reconstrucción histórica de las condiciones que existían al completar una tarea.
Una ejecución cancelada/completada o una versión retirada puede conservar historial APPROVED con vínculo actualmente Unsatisfied.

## Implementación

ExecutionRead usa RepeatableRead para reunir autorización, ámbito, estados, revisiones y vigencia en un snapshot coherente.
Los vínculos se resuelven en una consulta SQL por lote, ordenados por posición del predecesor y UUID.
Un CTE materializado calcula el contexto una vez; cada arista SKIPPED reutiliza app.has_dependency_exception con cuatro argumentos,
el mismo predicado empleado por app.execution_dependencies_satisfied en las transiciones.
No hay consultas por vínculo desde la aplicación ni reglas de vigencia duplicadas en C#.
Una modificación concurrente posterior aparece en la siguiente lectura; las escrituras revalidan sus propios controles.

No se cambian tablas, funciones SQL, migraciones ni grants runtime. No se modifican DAG, estados, aprobaciones,
excepciones, severidad, auditoría de operaciones aceptadas ni revisiones por consultar. Los rechazos conservan la auditoría existente.
La base habitual permanece con 20 migraciones. Las pruebas integradas utilizan bases temporales.

## Archivos de esta entrega

- apps/api/OpenIRP.Application/Executions/DependencyReadinessContracts.cs: DTO y enum de satisfacción.
- apps/api/OpenIRP.Application/Executions/IExecutionService.cs: método de consulta.
- apps/api/OpenIRP.Infrastructure/Incidents/IncidentDependencyReadinessService.cs: snapshot y consulta por lote.
- apps/api/OpenIRP.Api/Controllers/ExecutionsController.cs: GET tipado.
- apps/api/OpenIRP.IntegrationTests/DependencyReadinessTests.cs: casos PostgreSQL/HTTP, aislamiento, cuenta limitada y concurrencia.
- README.md, planificación integral y bitácora: estado y referencias.

## Validación

- Release final con .NET SDK 10.0.403: cero errores y advertencias; EF sin diferencias pendientes de modelo.
- Selección final de módulos relacionados: **118/118 en PostgreSQL 17 y 118/118 en PostgreSQL 18.3**, sin fallidas/omitidas: **27 casos nuevos** de esta consulta y 91 existentes de excepciones/capacidades. Las 27 están incluidas en las 118; no se suman.
- Evidencias finales: [TRX PG17](../artifacts/dependency-readiness-pg17-7aa52980a9924adda338c57ed26b1036/paso26-pg17.trx), [TRX PG18.3](../artifacts/dependency-readiness-pg18-920c0ea56fe046c4a5e02f9a9d96b996/paso26.trx); [matriz y hashes](../artifacts/dependency-readiness/validation.json).
- Cubre estados de predecesores, solicitud/rechazo/aprobación, caducidad, STEP_APPROVAL independiente, múltiples aristas, aislamiento real entre organizaciones/ejecuciones, roles/lockout, HTTP/no-store, cuenta SQL limitada y cambio concurrente de capacidades entre lecturas.
- La primera ejecución obtuvo 100/118 por motor: 17 fallos por alias PascalCase incompatibles con el mapeo snake_case y uno por precondición de capacidad del fixture. Se corrigieron ambos. La segunda ejecución obtuvo 117/118 por motor: el fixture de rechazo enviaba indicadores de autoaprobación y fue corregido. Después se repitieron los 118 casos completos de la selección, con los resultados finales indicados. Los TRX iniciales se conservan en artifacts/dependency-readiness-pg17-ffba734ba0aa472893c45218544c247a y artifacts/dependency-readiness-pg18-bd9d309883794c5c8f4840a76873a252.
- No se repitió la regresión global de toda la API; sus resultados históricos y el ajuste de Iteración 20 permanecen documentados por separado.
- HTTPS final: health/OpenAPI/Swagger 200; **60 operaciones del conjunto revisado** responden 401 sin sesión. El GET nuevo y ExecutionDependencySatisfaction figuran en OpenAPI con enum textual. [Resultados](../artifacts/dependency-readiness-http-ca3b712512ec44eaad3e196932915983/results.json). No representa el total de endpoints y health es estático.
- Base habitual openirp_dev en 127.0.0.1:5433 sin cambios: los mismos IDs de 20 migraciones, ocho capacidades/tres categorías y conteos de identidad/negocio/historiales vacíos. Ninguna migración nueva ni datos de ejemplo. [Comparación](../artifacts/dependency-readiness/unchanged.txt), [antes](../artifacts/dependency-readiness/dev-before.json), [después](../artifacts/dependency-readiness/dev-after.json).
- MCP reindexado: 3110 nodos/9242 relaciones. Graphify AST/HTML: 2691 nodos/7363 relaciones/197 comunidades; sin IA externa.
- Procesos propios temporales de pruebas/HTTPS detenidos al cerrar; PostgreSQL/Ollama habituales preservados. Sin commit, push ni merge nuevos; HEAD sigue en 9cec6a075c918ca88bf02fdda6b8ad750c8cf4a3.


## Pendientes

Revocación dedicada de excepciones, alternativas verificadas, continuidad vinculada al security_hold_id vigente,
revisión agregada de riesgos al cierre, RESTRICTED, políticas operativas de archivos e IA supervisada.
La consulta no implementa ninguno de esos flujos ni cambia permisos de escritura existentes.
