# Iteración 20 — Excepciones por vínculo de dependencia

Fecha: 2026-10-26. Rama feature/paso26-identidad-organizaciones. Se conserva el trabajo local anterior, Swagger y frontend.

## Alcance conservador

El responsable activo de una tarea PENDING o BLOCKED puede solicitar una excepción para un predecesor directo
que esté SKIPPED con su propia SKIP_AUTHORIZATION aprobada. ORG_ADMIN de la misma organización aprueba/rechaza.
La solicitud exige motivo, evaluación de impacto y riesgo residual (cada texto no vacío, máximo 2000 caracteres).

La decisión se refiere a un vínculo concreto de la versión de ejecución: tarea sucesora, tarea predecesora,
definición original de dependencia y autorización de omisión original. No concede un permiso global.
Cada vínculo no satisfecho requiere su propia decisión. Predecesores PENDING, IN_PROGRESS o BLOCKED no pueden
eludirse por este flujo; COMPLETED ya satisface su dependencia y no necesita excepción.

Aprobar no cambia estados de tareas, instrucciones, flags o dependencias publicadas. El predecesor sigue SKIPPED.
La operación permite que ese vínculo sea considerado autorizado en las validaciones posteriores de avance;
siguen vigentes todos los demás requisitos. No se crea una nueva versión ni se elimina la arista del DAG.

Este flujo implementa una excepción humana, no la validación de una acción alternativa. Las alternativas
con su soporte verificable siguen pendientes; no se simulan como tareas completadas.

## Operaciones y roles

- Solicita el responsable asignado actualmente elegible (ORG_ADMIN/ANALYST).
- Decide ORG_ADMIN activo de la organización; no hay acceso privado implícito de PLATFORM_ADMIN.
- Aprobar una solicitud propia exige SelfApproval=true y justificación adicional no vacía, máximo 2000.
  Para otros casos, SelfApproval=false y SelfApprovalJustification=null.
- Cada solicitud admite una sola decisión definitiva; un rechazo permite otra solicitud.
- Lectura para miembros activos elegibles, incluido VIEWER, con RepeatableRead y no-store.
- Escrituras con cookie, CSRF, gate PostgreSQL existente (1869636978,26), revisiones y revalidación de permisos.
- Incidente debe estar IN_RESPONSE, ejecución IN_PROGRESS y versión PUBLISHED.
- No hay nuevas cuentas, registro público, permisos globales ni acciones externas automáticas.

Confirmación atómica: autorización REQUESTED/APPROVED/REJECTED, evento de ejecución, evento de incidente y auditoría.
Cada operación incrementa revisiones de ejecución/incidente; no cambia changed_at_revision de la tarea.

## Contexto e invalidación

Una decisión pendiente exige las revisiones inmediatamente posteriores a su solicitud: cualquier escritura
intermedia del incidente/ejecución obliga a solicitar nuevamente. También se comprueba contexto de capacidades,
incluso si el cambio no incrementó la revisión del incidente.

Después de aprobar, la autorización sobrevive al avance ordinario, bloqueo/reanudación sin reasignación,
STEP_APPROVAL, registro de evidencia y decisiones sobre otros vínculos. No se vuelve a usar como autorización
de otro predecesor o tarea, ni como aprobación de paso, evidencia, omisión o cierre.

La aprobación deja de satisfacer el vínculo si:

- Se reasigna la tarea, incluso al mismo responsable.
- Cambia información de riesgo del incidente (incluidos evaluaciones, activos, responsable, título/descripción).
- Cambian referencias, perfil o evaluaciones de capacidades relevantes para la versión y el paso.
- Dejan de cumplirse estado, publicación, cuenta, organización o elegibilidad del responsable.

Se conserva todo el historial; no se sobrescriben decisiones. Si caduca durante IN_PROGRESS, el siguiente avance
o finalización se rechaza. El responsable puede bloquear y solicitar nueva revisión cuando la versión siga utilizable.
Una versión retirada sigue bloqueando operaciones: esto no sustituye la futura continuidad por security_hold_id.

El contexto usa formato dependency-exception-context-v1, revisión del último evento de riesgo del incidente,
huella de su contenido (sin revisión operativa/fecha de creación), catálogo/perfil/evaluación y sus revisiones.
Conserva disponibilidad/origen legibles de cada capacidad y huellas SHA-256 del contenido fuente.
No copia íntegramente las notas o limitaciones dentro de la autorización; las huellas sirven para comparar
precondiciones, no como firma ni como garantía frente a un propietario de PostgreSQL.
[Funciones SHA-256 de PostgreSQL](https://www.postgresql.org/docs/17/functions-binarystring.html).

Registrar evidencia y eventos de ejecución/aprobación/omisión/excepción no cuenta como cambio de riesgo por sí mismo.
Los cambios de datos efectivos sí se detectan mediante huella. Revertir datos a un valor anterior a través de
operaciones normales no revive una aprobación: las revisiones se conservaron/avanzaron.

No hay un endpoint dedicado de revocación en esta subfase. Reasignación/contexto invalidan, y cancelación/retirada
mantienen sus bloqueos operativos. Las acciones ya completadas con autorización válida conservan su historia;
un cambio posterior de contexto no las deshace ni reescribe.

## Controles de ejecución

El predicado común acepta un predecesor COMPLETED, o SKIPPED con una excepción vigente para esa arista y asignación.
Se usa tanto para iniciar/completar como para solicitar/decidir STEP_APPROVAL, en aplicación y SQL.

No sustituye:

- Elegibilidad y responsabilidad de quien cambia la tarea.
- STEP_APPROVAL independiente, con su excepción propia cuando corresponda.
- Evidencia activa de la tarea exacta y comprobación física de FILE.
- Autorización individual de cada dependencia no satisfecha.
- Publicación utilizable, revisiones, ciclo de vida o cierre manual del incidente.

Una tarea IN_PROGRESS controlada por una excepción aprobada debe bloquearse antes de reasignarse.
Un trigger diferido vuelve a comprobar dependencias al confirmar: no basta pasar el check previo y luego
insertar una marca de asignación que invalide la autorización dentro de la misma transacción.

Terminar una ejecución con tareas COMPLETED/SKIPPED autorizadas no resuelve ni cierra el incidente.

## API

Base: /api/organizations/{organization}/incidents/{incident}/executions/{execution}/tasks/{task}/dependencies/{predecessor}

Los identificadores task/predecessor corresponden a tareas de la misma ejecución, no a códigos de pasos.

| Método | Ruta | Acción |
|---|---|---|
| GET | /exceptions?afterRevision=0&limit=50 | Historia del vínculo; límite 1–100 |
| POST | /exceptions | Solicitud del responsable |
| POST | /exceptions/{exceptionRequest}/decision | Decisión administrativa |

Solicitud:

```json
{
  "impact": "Impacto de continuar sin la acción predecesora.",
  "residualRisk": "Riesgo residual documentado para revisión humana.",
  "expectedExecutionRevision": 6,
  "expectedIncidentRevision": 9,
  "reason": "Motivo de la excepción solicitada."
}
```

Decisión:

```json
{
  "approve": true,
  "selfApproval": false,
  "selfApprovalJustification": null,
  "expectedExecutionRevision": 7,
  "expectedIncidentRevision": 10,
  "reason": "Revisión del vínculo y su riesgo."
}
```

Revisiones ilustrativas; usar las de la respuesta real. Escritura devuelve ExecutionItem.
Historial devuelve DependencyExceptionItem con IDs de vínculo/predecesor/omisión, contexto, revisiones,
actor/correlación, impacto/riesgo y decisión. APPROVED histórico no implica vigencia actual ni permiso para cualquier acción.
Las rutas y sus contratos tipados aparecen en Swagger Development.

## Esquema y permisos SQL

Migración 20261026224629_DependencyExceptions: app.dependency_exceptions con 23 columnas, siete FK, tres checks,
unicidad de decisión request_id y de tarea/predecesor/revisión. Contexto jsonb acotado a 256 KiB.
Las FK compuestas conservan ámbito de organización/incidente/versión/ejecución para ambas tareas y la cadena de solicitud/decisión.

Historia append-only en EF y SQL. Cuatro triggers nuevos, dos diferidos, exigen ámbito real, original SKIP_AUTHORIZATION,
participantes actuales, contexto vigente y eventos correlacionados; protegen también el estado final de la tarea.
Se conservan los guards previos de STEP_APPROVAL, evidencia/FILE, identidad y terminalidad de tareas.
La resolución de contexto se calcula una vez por comprobación de dependencias, en SQL con CTE materializado.
No hay consultas de red ni IA dentro de la transacción.

Down bloquea pérdida de excepciones o su historia; en vacío restaura los predicados/guards anteriores.
Runtime recibe SELECT/INSERT en la tabla, sin edición/borrado. RLS y separación operativa completa siguen pendientes.

## Archivos

Nuevos: DependencyException.cs, DependencyExceptionContracts.cs, DependencyExceptionMapping.cs,
IncidentDependencyExceptionService.cs, DependencyExceptionTests.cs y migración/Designer DependencyExceptions.

Integración: IExecutionService.cs, ExecutionsController.cs, IncidentExecutionService.cs, IncidentStepApprovalService.cs,
OpenIrpDbContext.cs, IncidentMapping.cs, ExecutionMapping.cs, snapshot EF, ci/grant-runtime.sql
y fixtures de TRUNCATE en PlaybookDraftPersistenceTests.cs y SkipAuthorizationTests.cs.
Documentación maestra, README y bitácora actualizados; Swagger y frontend anteriores preservados.

## Validación

- Compilación Release final: cero errores y advertencias. EF confirma que no hay diferencias pendientes de modelo.
- Regresión global inicial: **767/768 en PostgreSQL 17 y 767/768 en PostgreSQL 18.3**, sin omitidas. El único fallo fue SkipHistoryIsAppendOnlyInSqlAndEf(operation: "truncate"): la nueva FK adelantaba el error 0A000 al 42501 esperado. Se corrigió el fixture para truncar ambas tablas relacionadas y comprobar el guard append-only.
- Verificación final: **54/54 en cada motor**, sin fallidas/omitidas: **51 casos de excepciones** y tres variantes de historial de omisiones. Incluye organización activa en SQL, aislamiento, roles/CSRF, contexto vigente, rollback, concurrencia, múltiples dependencias, aprobación de paso, evidencia y permisos runtime.
- No se repitió toda la suite después del ajuste. Las ejecuciones global y enfocada acreditan 769 IDs de caso distintos aprobados por motor; no equivalen a una ejecución global final de 769/769. Se usan IDs TRX porque dos nombres de presentación truncados coinciden.
- TRX globales: [PG17](../artifacts/dependency-full-pg17-9ca443f12ca648b3a9694cf5a98e18c2/paso26-pg17.trx), [PG18.3](../artifacts/dependency-full-pg18-d1527556f5594b429a0d753d27af0929/paso26.trx). TRX finales: [PG17](../artifacts/dependency-final-pg17-9755baca29034b7a838365203bc023dd/paso26-pg17.trx), [PG18.3](../artifacts/dependency-final-pg18-3b4765896f184c8a8e61184cd4aa02c4/paso26.trx).
- Matrices verificables con hashes: [PG17](../artifacts/dependency-exceptions-dev-66f182a79e0848bf90c3ea7d70cff43b/validation-matrix-pg17.json), [PG18.3](../artifacts/dependency-exceptions-dev-66f182a79e0848bf90c3ea7d70cff43b/validation-matrix-pg18.json).
- Respaldo completo anterior a la migración 20 restaurado en PostgreSQL aislado, con conteos e historial coincidentes. SHA256: 22232280AE668ADD92F72B5ACE17F5C2A3C3F6E6320866D63EEB28C4255E9921. [Restauración](../artifacts/dependency-exceptions-dev-66f182a79e0848bf90c3ea7d70cff43b/restore-verified.txt).
- Migración **20261026224629_DependencyExceptions aplicada únicamente a openirp_dev local, 127.0.0.1:5433**. Se conservan los conteos previos: ocho capacidades, tres categorías, identidad/negocio/historiales vacíos; dependency_exceptions tiene cero filas. Historial de 20 migraciones. [Conteos](../artifacts/dependency-exceptions-dev-66f182a79e0848bf90c3ea7d70cff43b/after-counts.json).
- SQL incremental final revisado; esquema comprobado: 23 columnas, siete FK, cuatro triggers nuevos (dos diferidos). Conserva guards de STEP_APPROVAL, FILE y organización activa. [SQL final](../artifacts/dependency-exceptions-dev-66f182a79e0848bf90c3ea7d70cff43b/DependencyExceptions-final.sql), [comprobación](../artifacts/dependency-exceptions-dev-66f182a79e0848bf90c3ea7d70cff43b/Verify-DependencySchema.sql).
- HTTPS posterior a la migración: health/OpenAPI/Swagger 200; **59 operaciones del conjunto revisado** responden 401 sin sesión. Las tres rutas nuevas y sus contratos figuran en OpenAPI. No es el total de endpoints; health es estático. [Resultados](../artifacts/dependency-exceptions-http-1c9f40f709924ce48852cbbff564ec8b/results.json).
- Las pruebas autenticadas utilizaron bases temporales; no se crearon cuentas ni datos de negocio en la base habitual. Docker se reinició al detectarse detenido; PostgreSQL/Ollama habituales vuelven a estar disponibles. API y procesos propios de prueba se cerraron.
- Grafo MCP reindexado: 3030 nodos/8997 relaciones. Graphify AST/HTML: 2636 nodos/7179 relaciones/193 comunidades, sin IA externa.
- Sin commit, push, merge ni cambios remotos nuevos; HEAD permanece 9cec6a075c918ca88bf02fdda6b8ad750c8cf4a3.


## Pendientes

Validación formal de alternativas, revocación dedicada de excepciones, continuidad por bloqueo de seguridad vigente,
cierre ampliado de riesgo residual, RESTRICTED, operación/retención/antimalware de archivos e IA supervisada.
