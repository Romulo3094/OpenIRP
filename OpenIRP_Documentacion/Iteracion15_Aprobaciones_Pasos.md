# Iteración 15 — Aprobaciones de pasos (STEP_APPROVAL)

Fecha: 2026-10-24. Continúa las ejecuciones de la Iteración 14.

> Actualización posterior: [Iteración 16](Iteracion16_Evidencias_Incidente_Tarea.md) incorpora evidencias TEXT/REFERENCE y su selección al completar tareas. FILE y RESTRICTED siguen pendientes.

## Alcance

Se implementan solicitud, aprobación/rechazo y consulta del historial de autorización de pasos RequiresApproval. Una autorización válida permite iniciar y completar la tarea, manteniendo los demás controles. No inicia/completa tareas automáticamente.

STEP_APPROVAL no equivale a SKIP_AUTHORIZATION, aceptación de IA ni aprobación del cierre del incidente. Evidencias, omisiones excepcionales, riesgo residual y continuidad por retirada de seguridad siguen pendientes; sus barreras se conservan.

## Permisos y vigencia

- Solicita el usuario asignado actualmente, con cuenta/membresía activas y rol ORG_ADMIN o ANALYST en la organización. No basta ser administrador si la tarea está asignada a otra persona.
- Decide ORG_ADMIN activo de esa organización. El grant PLATFORM_ADMIN no concede acceso privado ni decisión sin membresía y rol de la organización.
- VIEWER activo consulta, junto con los escritores autorizados; no solicita ni decide.
- Se exige organización activa, incidente IN_RESPONSE, ejecución IN_PROGRESS y versión todavía PUBLISHED, aunque ya exista otra publicación vigente.
- La tarea debe estar PENDING o BLOCKED, asignada a un escritor elegible, con RequiresApproval y todos sus predecesores COMPLETED. No hay aprobación retrospectiva de tareas iniciadas/completadas.
- Cada solicitud/decisión exige motivo no vacío hasta 2000 y las revisiones actuales de ejecución e incidente.

Aprobar la solicitud propia exige selfApproval=true y selfApprovalJustification no vacía hasta 2000. La excepción queda en el historial. Aprobación por otro administrador: false y null. Rechazo: false y null, incluso al rechazar la solicitud propia.

Una solicitud admite una sola decisión definitiva. Pending o Approved no admiten otra solicitud para la misma asignación; Rejected permite una nueva solicitud con UUID distinto. El rechazo no omite, bloquea ni completa automáticamente la tarea.

Toda operación de asignación posterior invalida la solicitud/autorización anterior, incluso si se vuelve a asignar a la misma membresía. Cambiar a otro responsable y volver al original no recupera una aprobación antigua. Las solicitudes obsoletas se conservan y no pueden decidirse/reutilizarse.

Para reasignar una tarea controlada IN_PROGRESS primero hay que pasarla a BLOCKED con motivo. Luego se asigna, el responsable solicita nuevamente y el administrador decide sobre el nuevo UUID. La aplicación bloquea también una reasignación al mismo responsable mientras está IN_PROGRESS.

La autorización sobrevive a BLOCKED y su retorno a PENDING/IN_PROGRESS si no cambia la asignación. Se refiere al paso inmutable y su responsable, no a una única llamada HTTP. Las condiciones actuales de rol/cuenta, dependencias, versión y ejecución se vuelven a comprobar al avanzar.

No se incorpora revocación administrativa independiente de una decisión histórica; reasignar invalida su uso futuro. Una retirada de versión mantiene el bloqueo conservador existente y no puede eludirse con STEP_APPROVAL. No existe todavía autorización de continuidad por security_hold_id.

## API

Prefijo:

/api/organizations/{organization}/incidents/{incident}/executions/{execution}/tasks/{task}/approvals

| Método y sufijo | Función |
|---|---|
| GET vacío?afterRevision=0&limit=50 | Historial de solicitudes y decisiones |
| POST vacío | Solicitar aprobación, responde ExecutionItem actualizado |
| POST /{approvalRequest}/decision | Aprobar/rechazar, responde ExecutionItem actualizado |

Las escrituras requieren cookie autenticada y X-CSRF-TOKEN. Respuestas no-store. El actor y el ámbito proceden de autenticación/ruta.

Solicitud:

```json
{
  "expectedExecutionRevision": 2,
  "expectedIncidentRevision": 5,
  "reason": "Solicitar revisión antes de iniciar el paso"
}
```

Tras solicitar, consultar el historial: el id de la entrada action=REQUESTED es approvalRequest para la decisión. No usar el UUID de la entrada APPROVED/REJECTED. Las revisiones actualizadas se obtienen de ExecutionItem; no mantener los tokens de este ejemplo.

Decisión por otro administrador:

```json
{
  "approve": true,
  "selfApproval": false,
  "selfApprovalJustification": null,
  "expectedExecutionRevision": 3,
  "expectedIncidentRevision": 6,
  "reason": "Autorizar el procedimiento revisado"
}
```

Autoaprobación excepcional:

```json
{
  "approve": true,
  "selfApproval": true,
  "selfApprovalJustification": "Administrador único disponible; revisión explícita de alcance y dependencias",
  "expectedExecutionRevision": 3,
  "expectedIncidentRevision": 6,
  "reason": "Autorizar con excepción documentada"
}
```

approve, selfApproval y ambos tokens deben aparecer explícitamente. Rechazar usa approve=false, selfApproval=false y justificación adicional null; reason sigue siendo obligatorio.

Historial: id, requestId (null en REQUESTED), action, assignedMembershipId, executionRevision, actorUserId, correlationId, reason, selfApproval, selfApprovalJustification y occurredAt. Conserva el registro original y las decisiones posteriores, incluidas solicitudes invalidadas por reasignación. El historial de ejecución permite observar esas reasignaciones; una fila APPROVED histórica no prueba por sí sola autorización vigente.

Orden por executionRevision, cursor afterRevision >= 0; limit 1..100 (50 por defecto). Continuar desde la última revisión recibida. Los UUID de otra tarea/ejecución/incidente/organización no amplían acceso.

Errores relevantes:

| Código | HTTP | Condición |
|---|---|---|
| organization_admin_required | 403 | Decisor sin rol administrativo actual |
| active_assigned_task_writer_required | 409 | Responsable/solicitante no elegible |
| step_approval_not_required | 409 | Paso sin flag de aprobación |
| execution_dependencies_not_completed | 409 | Predecesores incompletos |
| step_approval_requires_unstarted_task | 409 | Tarea iniciada o terminal |
| step_approval_already_requested_or_approved | 409 | Solicitud/aprobación vigente |
| step_approval_request_obsolete | 409 | UUID ajeno/obsoleto o asignación cambiada |
| step_approval_already_decided | 409 | Decisión definitiva ya existente |
| invalid_step_self_approval | 400 | Excepción propia incompleta o improcedente |
| task_approval_required | 409 | Avance sin autorización vigente |
| block_task_before_reassignment | 409 | Reasignación de tarea controlada iniciada |
| stale_incident_revision / stale_execution_revision | 409 | Tokens obsoletos |

El orden de validación puede devolver antes otro conflicto o denegación aplicable. Un anónimo recibe 401; el cuerpo sin campos obligatorios o sin CSRF válido recibe 400.

## Transacciones y PostgreSQL

Migración 20261024224015_StepApprovals: app.step_approvals, 16 columnas, cuatro FK, tres checks e índices de ámbito/historial. La FK compuesta al request liga exactamente tenant, incidente, versión, ejecución, tarea y membresía; el trigger exige que el destino sea una solicitud REQUESTED vigente. Un índice único parcial permite una sola decisión por requestId.

Solicitudes y decisiones son filas independientes append-only: EF y SQL prohíben UPDATE/DELETE; SQL también TRUNCATE. No se generan aprobaciones para ejecuciones existentes. La migración conserva los datos anteriores y amplía los checks de acciones de incidente/ejecución.

Se reutiliza IncidentService/IExecutionService, gate transaccional (1869636978,26), READ COMMITTED y timeout de bloqueo de cinco segundos. Autorización y revisiones se comprueban dentro del gate. Cada operación aceptada incrementa incidente y ejecución y confirma atómicamente:

1. Registro de solicitud/decisión.
2. Evento STEP_APPROVAL_REQUESTED/APPROVED/REJECTED de ejecución.
3. Snapshot/evento de incidente con revisión y referencia a la tarea.
4. Auditoría SUCCESS mínima sin copiar motivos ni justificaciones privadas.

Un constraint trigger diferido exige el evento de ejecución correspondiente con mismo ámbito, revisión, actor, correlación y responsable. Si falla cualquiera de los destinos, se revierten todos los cambios y revisiones.

Los guards de inserción comprueban participantes actuales, estado, dependencias, rol de decisor y excepción propia. El guard de tareas exige autorización vigente antes de IN_PROGRESS/COMPLETED y mantiene el bloqueo de evidencia. Down se detiene si hay aprobaciones o historia STEP_APPROVAL; sin esos datos permite volver a la migración anterior, conservando las ejecuciones.

grant-runtime.sql concede únicamente SELECT/INSERT sobre step_approvals a un rol SQL dedicado. No añade privilegios globales de negocio. RLS y separación operativa completa de credenciales continúan pendientes; el propietario puede modificar objetos.

## Archivos de esta entrega

En apps/api:

- OpenIRP.Domain/Entities/StepApproval.cs.
- OpenIRP.Application/Executions/StepApprovalContracts.cs e IExecutionService.cs.
- OpenIRP.Infrastructure/Incidents/IncidentStepApprovalService.cs; integración en IncidentExecutionService.cs.
- OpenIRP.Infrastructure/Persistence/Configurations/StepApprovalMapping.cs; ampliación de ExecutionMapping.cs e IncidentMapping.cs.
- OpenIrpDbContext, migración StepApprovals, Designer y snapshot.
- OpenIRP.Api/Controllers/ExecutionsController.cs.
- OpenIRP.IntegrationTests/StepApprovalTests.cs y ajustes de fixtures/expectativas/TRUNCATE en pruebas de ejecución/playbooks.

Además: ci/grant-runtime.sql, README, planificación y esta guía. Se preservan los cambios del frontend y las iteraciones locales anteriores. Sin commit/push/merge nuevos.

## Verificación y entorno local

- Compilación Release sin errores ni advertencias.
- Primer conjunto: **67/67 correctas** en PostgreSQL 18.3 temporal, cero fallidas/omitidas; 26 nuevas y 41 de ejecuciones. TRX: artifacts/step-approvals-focused-pg18-89482db8b4f74a93bb44ef129c9b7816/paso26.trx. EF sin diferencias de modelo; clúster detenido.
- Se amplió el conjunto con ocho casos adicionales: evidencia independiente y bloqueo/reanudación, flag/dependencias de solicitud, decisiones SQL sin rol/excepción/historial y fallos del registro de aprobaciones.
- **565/565 pruebas correctas** en PostgreSQL 18.3 temporal, cero fallidas/omitidas, con **34 casos nuevos** de aprobación. TRX: artifacts/step-approvals-full-pg18-e1988186f4414b47aadde6e28261e0a8/paso26.trx. Incluye regresión anterior, carreras forzadas, invalidación por reasignación, roles/cuentas/lockout, retirada/cancelación, dependencias/evidencia, rollback en auditoría/eventos/aprobaciones, SQL manipulado, upgrade/downgrade, HTTP/CSRF y LOGIN SQL limitado. EF sin diferencias; clúster detenido.
- HTTPS real: health/OpenAPI 200 y 41 operaciones de playbooks/ejecuciones/aprobaciones presentes, todas con anónimos 401. Evidencia artifacts/step-approvals-http-96056fee0d014ec1b6268c586200c201/. Es una comprobación de rutas/autorización anónima sin uso de base de datos; los flujos autenticados se comprueban en bases temporales.
- SQL Up revisado sin sentencias de eliminación/actualización de datos anteriores: artifacts/step-approvals-review-b63630e4fa0443f89bdd8db77511ba16/StepApprovals.sql.
- Bloqueante inicial de Docker del 2026-10-24 resuelto el 2026-10-25. PostgreSQL 17 y la regularización local se completaron; ver el cierre de verificación al final.
- La migración, inicialmente pendiente, ya se aplicó a openirp_dev el 2026-10-25 tras verificar respaldo/restauración y la regresión PostgreSQL 17. Se conservaron los datos y hay dieciséis migraciones.
- Destino, propietario, binding, historial, respaldo restaurado, conteos y esquema local comprobados. La API conserva el arranque sin aplicar migraciones automáticamente.

Pruebas reproducibles desde el repositorio con PostgreSQL disponible:

```powershell
./ci/Test-IdentityLocal.ps1
# Con Docker operativo:
./ci/Test-IdentityDocker.ps1
```

Estos scripts crean bases/instancias temporales propias. No deben sustituirse por pruebas sobre la base habitual.

### Cierre de verificación pendiente — 2026-10-25

Docker recuperado. Se completó lo pendiente de la entrega inicial:

- **565/565 correctas en PostgreSQL 17.11**, cero fallidas/omitidas. TRX: artifacts/paso26-pg17-d94db28ebf874a2da0804545abd438cc/paso26-pg17.trx. Se mantienen las 565/565 ya verificadas en PostgreSQL 18.3; no hubo cambios de implementación en esta regularización.
- Release sin errores ni advertencias y EF sin diferencias de modelo. El contenedor de pruebas PostgreSQL 17 se detuvo al terminar.
- Respaldo completo de openirp_dev y restauración exitosa en otro PostgreSQL 17, sin puertos ni volúmenes publicados. Conteos e historial restaurados idénticos al origen; se volvió a comprobar que la base habitual no hubiera cambiado antes de migrarla.
- Dump: artifacts/step-approvals-dev-113a8c7cd3e84aa095895425dc33fc48/openirp_dev-before-step-approvals.dump. SHA256 **F0E013202CACB7D61EC3FA4C6C8F398F15265D6382E04730B3ADB7B4C3086CFA**.
- Aplicada únicamente 20261024224015_StepApprovals a openirp_dev, propietario openirp, PostgreSQL 17.11 en 127.0.0.1:5433. **Dieciséis migraciones aplicadas**.
- Conteos previos conservados: ocho capacidades y tres categorías; identidad, auditoría y tablas de negocio/playbooks/ejecuciones vacías. Nueva tabla step_approvals e historial STEP_APPROVAL vacíos. Sin cuentas ni datos de prueba fabricados en la base habitual.
- Esquema real comprobado: 16 columnas, cuatro FK, tres checks, tres triggers, integridad de historial diferida y guard de tarea que exige autorización. Evidencia de respaldo, restauración, SQL incremental, conteos, scripts y esquema: artifacts/step-approvals-dev-113a8c7cd3e84aa095895425dc33fc48/.
- HTTPS con conexión local configurada: health/OpenAPI 200 y 41 operaciones presentes, todas con anónimos 401. Evidencia: artifacts/step-approvals-local-http-944fc1021a1948e1bec5e36ce6b6cf69/. Estas llamadas anónimas no verifican un flujo de negocio autenticado sobre la base habitual; los flujos autenticados/CSRF quedaron cubiertos en las bases PostgreSQL 17 de pruebas.
- API temporal y contenedores propios de prueba/restauración detenidos. PostgreSQL/Ollama habituales y frontend preservados. Sin nuevo commit, push ni merge.

El bloqueo de Docker y la migración local pendiente de esta iteración quedan resueltos. Continúan los pendientes funcionales ya documentados: evidencias, omisiones/excepciones, continuidad por seguridad e IA supervisada.
