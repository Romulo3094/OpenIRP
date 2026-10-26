# Iteración 14 — Ejecuciones, tareas y dependencias iniciales

Fecha: 2026-10-24. Continúa los playbooks versionados de las iteraciones 11–13.

## Alcance

Se puede iniciar una publicación vigente sobre un incidente IN_RESPONSE, crear exactamente una tarea por paso, asignar responsables, avanzar/bloquear tareas según dependencias y completar o cancelar la ejecución explícitamente. Solo puede haber una ejecución IN_PROGRESS por incidente. Las ejecuciones terminadas conservan su historial y puede iniciarse otra si el incidente sigue en respuesta.

Cada ejecución queda ligada a una versión específica. Cambiar la publicación vigente no cambia sus tareas ni instrucciones y no interrumpe una ejecución basada en otra publicación que todavía mantiene estado PUBLISHED. No se ejecuta ninguna acción sobre infraestructura externa.

Una ejecución completada no resuelve ni cierra el incidente automáticamente. Mientras haya una activa se bloquea cambiar categoría o sacar al incidente de IN_RESPONSE, incluso para resolver, retroceder, descartar o cerrar. Tras completar/cancelar siguen aplicando las reglas manuales existentes de resolución, clasificación, responsable y cierre/revisión por máximo histórico.

## Permisos y condiciones de inicio

ORG_ADMIN y ANALYST activos de la organización pueden iniciar/gestionar ejecuciones; VIEWER activo solo consulta. PLATFORM_ADMIN no obtiene acceso a incidentes ni ejecuciones privados sin membresía y rol adecuados.

Iniciar exige actor/cuenta/lockout, membresía y organización activos; expectedIncidentRevision actual; playbook global o privado de esa misma organización; expectedPlaybookRevision actual; UUID explícito de la versión vigente PUBLISHED y categoría coincidente con la del incidente.

La categoría y las capacidades referenciadas deben seguir activas en el catálogo al iniciar. Los requisitos se exponen para consulta; no se convierten en una evaluación automática de suficiencia de disponibilidad. UNKNOWN/UNAVAILABLE organizacionales no se sustituyen por disponibilidad implícita. El análisis operativo de capacidades/excepciones se ampliará en entregas posteriores.

El primer estado es IN_PROGRESS, revisión de ejecución 1, con tareas PENDING sin asignar. Se copian identidades de instancia, no se alteran los pasos publicados. Instrucciones, flags y requisitos se consultan desde la versión inmutable.

## Estados de tareas y responsables

| Estado inicial | Destinos disponibles |
|---|---|
| PENDING | IN_PROGRESS, BLOCKED |
| BLOCKED | PENDING, IN_PROGRESS |
| IN_PROGRESS | BLOCKED, COMPLETED |
| COMPLETED | Ninguno |

Toda mutación requiere motivo no vacío hasta 2000 y tokens actualizados de ejecución e incidente. BLOCKED conserva su motivo en el historial; no existe una operación de bloqueo silencioso. Los flags y posiciones no sustituyen dependencias explícitas.

Antes de IN_PROGRESS o COMPLETED deben estar COMPLETED todos los predecesores de la versión. No se admite completar desde PENDING ni reabrir tareas completadas. Las tareas independientes pueden avanzar sin esperar a otras sin enlace; las escrituras concurrentes usan control optimista y pueden exigir refrescar los tokens.

Un escritor autorizado puede asignar/reasignar tareas no completadas a membresías activas ORG_ADMIN/ANALYST de la misma organización y cuentas activas sin lockout. Se puede dejar PENDING/BLOCKED sin responsable mediante null explícito; no una tarea IN_PROGRESS.

Solo el usuario que corresponde a la asignación actual puede cambiar el estado, con su rol/cuenta/membresía vigentes. ORG_ADMIN también debe estar asignado para ese avance; no hay override silencioso por rol.

> Estado histórico de Iteración 14. STEP_APPROVAL se incorpora posteriormente en [Iteración 15](Iteracion15_Aprobaciones_Pasos.md); evidencias y omisiones conservan sus barreras.

## Controles pendientes y límites conservadores

- RequiresApproval=true bloquea el inicio y la finalización del paso hasta incorporar STEP_APPROVAL. Ser ORG_ADMIN no omite ese requisito.
- RequiresEvidence=true permite iniciar, pero bloquea COMPLETED hasta integrar evidencias autorizadas.
- SKIPPED se reconoce como intención en el contrato, pero se rechaza con conflicto: no existe todavía el flujo de justificación/autorización de omisiones. Por ahora todas las tareas deben estar completadas, incluso las no obligatorias, para terminar la ejecución.
- No se implementan todavía aprobación de pasos, omisiones excepcionales, riesgo residual, evidencias ni autorizaciones de continuidad. El cierre continúa siendo manual y no se presenta esta entrega como ejecución completa de todos los procedimientos posibles.

Retirar administrativamente una versión (ARCHIVED) bloquea asignaciones, avances y finalización de las ejecuciones activas que la usan. Su consulta se conserva. Solo ORG_ADMIN puede cancelar una ejecución de versión retirada. En esta primera entrega no hay override de continuidad: debe cancelarse y, si procede, iniciarse una publicación vigente.

Todavía no existe la distinción de retirada por seguridad con security_hold_id y decisión de continuidad ligada al bloqueo actual. El bloqueo conservador por ARCHIVED no se presenta como implementación de ese flujo futuro.

Cancelación ordinaria exige motivo y escritor activo; queda terminal, preservando las tareas en sus últimos estados. Cancelar no equivale a completar el procedimiento ni satisface dependencias de otra ejecución. La posterior resolución del incidente sigue siendo una decisión humana y auditable.

## API

Prefijo: /api/organizations/{organization}/incidents/{incident}/executions

| Método y sufijo | Función |
|---|---|
| GET vacío?skip=0&limit=50 | Listado por incidente, nextSkip |
| POST vacío | Iniciar ejecución, 201 con Location |
| GET /{execution} | Ejecución, tareas, enlaces y requisitos |
| GET /{execution}/events?afterRevision=0&limit=50 | Historial de ejecución/tareas |
| PUT /{execution}/tasks/{task}/assignment | Asignar o quitar responsable |
| PUT /{execution}/tasks/{task}/state | Cambiar estado permitido |
| POST /{execution}/completion | Completar sin cerrar incidente |
| POST /{execution}/cancellation | Cancelar con motivo |

Cookie autenticada, no-store y X-CSRF-TOKEN en toda escritura. Listados limit 1..100, por defecto 50; skip 0..1000000; historial cursor afterRevision >= 0. Una página de historial de tamaño limit puede continuar con el último número devuelto; no congela páginas futuras.

Inicio:

```json
{
  "playbookId": "UUID-del-playbook",
  "versionId": "UUID-de-la-publicacion-vigente",
  "expectedPlaybookRevision": 3,
  "expectedIncidentRevision": 3,
  "reason": "Iniciar respuesta guiada"
}
```

Asignación:

```json
{
  "assignedMembershipId": "UUID-de-membresia-elegible",
  "expectedExecutionRevision": 1,
  "expectedIncidentRevision": 4,
  "reason": "Asignar responsable de la tarea"
}
```

assignedMembershipId debe aparecer explícitamente, incluso cuando sea null. El UUID es de membresía, no de usuario.

Estado:

```json
{
  "targetState": "InProgress",
  "expectedExecutionRevision": 2,
  "expectedIncidentRevision": 5,
  "reason": "Iniciar el paso con sus dependencias completas"
}
```

Completion/cancellation reciben expectedExecutionRevision, expectedIncidentRevision y reason. Ambas revisiones se actualizan en cada escritura aceptada; usar las devueltas en la respuesta, no asumir tokens estáticos de estos ejemplos.

El contenido devuelve versión/título/estado de fuente, estado/revisión de ejecución y revisión de incidente observada, fechas/motivo de finalización, tareas con instrucciones/flags/responsable, dependencias entre UUID de tareas y requisitos por versión/paso. Un taskId no reemplaza stepId: uno identifica la instancia y otro el paso de la publicación.

El ámbito y el actor se obtienen de ruta/autenticación. IDs de otro incidente, versión u organización no amplían permisos. Token obsoleto/estado inválido/dependencia incompleta/control pendiente: 409; autorización denegada: 403; recurso no encontrado dentro del ámbito autorizado: 404; anónimo: 401.

## Transacción e historial

Se reutiliza IncidentService e IExecutionService con el mismo DbContext, gate (1869636978,26), READ COMMITTED y timeout de bloqueo de cinco segundos. La autorización se revalida después del gate. Las lecturas compuestas usan REPEATABLE READ.

La escritura incrementa la revisión del incidente y de la ejecución; la tarea registra su última revisión de ejecución afectada. No se recalcula severidad por cambios puramente operativos. Se anexan snapshot de incidente, evento de ejecución y SUCCESS mínimo en una transacción.

Acciones: EXECUTION_STARTED, EXECUTION_TASK_ASSIGNED, EXECUTION_TASK_CHANGED, EXECUTION_COMPLETED y EXECUTION_CANCELLED. El historial de incidente conserva el contexto y un detalle con UUID/revisión de ejecución y tarea afectada; el historial específico conserva estado, responsable, actor, correlación, motivo y revisión de incidente. Los motivos operativos no se copian al log mínimo de auditoría.

El primer evento de inicio y el conjunto transaccional de tareas PENDING describen la creación. Un evento por revisión de ejecución permite reconstruir asignaciones/estados posteriores; los pasos de origen permanecen inmutables.

## PostgreSQL

Migración 20261024034804_InitialPlaybookExecutions:
- Tres tablas: app.playbook_executions (11 columnas), app.execution_tasks (9) y app.execution_task_events (16).
- FK compuestas atan organización/incidente/ejecución/versión y paso exactos; FK de responsables y actores. Unicidad de tarea por ejecución/paso y evento por ejecución/revisión.
- Índice único parcial impide más de una IN_PROGRESS por incidente.
- Estados/metadatos/revisiones comprobados; identidades y terminales protegidos, sin borrado físico ni TRUNCATE.
- Integridad diferida exige exactamente todas las tareas de la versión y no permite completar con tareas pendientes ni revisiones de tarea superiores a la ejecución.
- Triggers de tarea verifican transición, versión usable, estado del incidente, dependencias, responsable elegible y flags que no pueden omitirse.
- Trigger de incidente impide cambiar categoría o abandonar respuesta con ejecución activa.
- Historial append-only por EF/SQL. Down se bloquea si existe ejecución o historial operativo, incluido el de incidente.
- Se amplía el check de acciones del incidente sin reescribir sus eventos anteriores.

El conjunto de playbooks/ejecuciones queda en nueve tablas, 89 columnas, 28 FK y 23 checks. Los guards de ese módulo más el del incidente suman 24. grant-runtime.sql limita UPDATE al estado/revisión/finalización de ejecución y estado/responsable/revisión de tarea; eventos solo SELECT/INSERT.

El modelo de autorización de usuario final reside en el servicio y sus operaciones soportadas comparten gate. RLS y separación operativa completa de credenciales siguen pendientes; las credenciales del propietario pueden alterar objetos.

## Archivos de esta entrega

En apps/api:
- Domain/Entities/ExecutionEntities.cs.
- Application/Executions/IExecutionService.cs.
- Infrastructure/Incidents/IncidentExecutionService.cs e IncidentExecutionReadService.cs.
- Integración en IncidentService.cs para transacción, snapshots, revisiones y precondiciones de estado/categoría.
- Infrastructure/Persistence/Configurations/ExecutionMapping.cs, ampliación de IncidentMapping.cs, OpenIrpDbContext y migración/snapshot.
- Api/Controllers/ExecutionsController.cs y registro DI en Program.cs.
- IntegrationTests/ExecutionTests.cs y ExecutionPersistenceTests.cs; actualización del TRUNCATE de prueba anterior al incorporar nuevas FK.

Además: ci/grant-runtime.sql, README, planificación y guía. El frontend local existente se preserva.

## Verificación

- Compilación Release: cero errores y cero advertencias. EF sin diferencias de modelo pendientes.
- Pruebas operativas iniciales: **18/18 correctas** en PostgreSQL 17. Evidencia histórica: artifacts/execution-core-pg17-3254110e6ee44819b255c85092840604/paso26-pg17.trx.
- Verificación ampliada: **90/90 correctas**, cero fallidas/omitidas, con **41 casos nuevos de ejecución** y 49 del ciclo de vida anterior. TRX: artifacts/execution-focused-pg17-9cb0cdd9de90471c8eda42557bce4371/paso26-pg17.trx.
- Regresión completa PostgreSQL 17: **531/531 correctas**, sin fallidas ni omitidas. TRX: artifacts/paso26-pg17-9b8a23ee57e3431c84f3e089e0bb0b78/paso26-pg17.trx.
- Regresión completa PostgreSQL 18.3 nativo aislado: **531/531 correctas**, sin fallidas ni omitidas. TRX: artifacts/executions-full-pg18-beb64e18b3c446b784856daea7de7509/paso26.trx.
- Los 41 nuevos y los 90 enfocados están incluidos en los 531. Cobertura: inicio global/privado, conjunto exacto de tareas, historial/correlación, asignación y responsables actuales, dependencias/bloqueo/terminales, flags de aprobación/evidencia y omisión prohibida, roles/contextos inactivos, categoría/retirada/catálogo/revisiones, carreras forzadas, resolución/cierre posterior manual, inmovilidad de versión, retirada conservadora y cancelación administrativa, rollback de ambos agregados/los tres historiales, SQL de identidad/ámbitos/unicidad/integridad, upgrade/downgrade, HTTP/CSRF y LOGIN SQL limitado.
- Se ajustó el cliente HTTP de pruebas para deserializar enums de texto del contrato. La repetición aprobó los 90; no se alteró el contrato de la API.
- Migración aplicada únicamente a openirp_dev, PostgreSQL 17 habitual en 127.0.0.1:5433, tras restaurar un dump completo en otro contenedor sin puertos ni volúmenes y cotejar conteos/historial antes de aplicar.
- Se conservaron vacías las tablas de negocio/identidad/auditoría/playbooks y se mantuvieron ocho capacidades y tres categorías. Las tres tablas nuevas quedaron vacías. No se fabricaron cuentas, incidentes, ejecuciones o decisiones en la base habitual; quince migraciones aplicadas.
- Respaldo, SQL incremental, conteos, scripts y esquema: artifacts/playbook-executions-dev-1373b58baafe45279fe39aea8f7e4abd/. Dump openirp_dev-before-playbook-executions.dump, SHA256 **39BB768D5FFB38AF9BA4ED762E1E4F00518A01274C961430799D7ED945B8E120**.
- SQL Up sin eliminación ni actualización de datos existentes; amplía el check de acciones y crea objetos. Metadatos verificados: nueve tablas de módulo, 89 columnas, 28 FK, 23 checks, 24 guards incluyendo el incidente e integridad de tareas diferida.
- HTTPS real: health/OpenAPI 200, 38 operaciones de playbooks/ejecuciones presentes y anónimos 401. Evidencia: artifacts/playbook-executions-http-dfdd0be74d7143878f221fc197cfa04e/. Flujos autenticados verificados en bases temporales.
- Instancias API/PostgreSQL propias detenidas. Sin listeners en 55432, 55433, 7113 ni 5195. PostgreSQL/Ollama habituales y frontend local preservados.
- Trabajo local, sin nuevo commit, push ni merge.

No se detectaron bloqueantes para continuar. Permanecen pendientes evidencias, aprobaciones de pasos, autorizaciones de omisión y riesgo residual, continuidad con bloqueo de seguridad vigente e IA supervisada. Los controles aún no implementados permanecen bloqueados, sin sustituirlos por finalización automática.
