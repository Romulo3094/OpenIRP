# Iteración 16 — Evidencias TEXT/REFERENCE de incidente y tarea

Fecha: 2026-10-26. Continúa ejecuciones y STEP_APPROVAL de las iteraciones 14–15.

> Actualización posterior: [Iteración 17](Iteracion17_Archivos_Privados_Evidencia.md) incorpora FILE por carga multipart, con los formatos/límite y conservación aprobados. RESTRICTED y purga continúan pendientes.

## Alcance implementado

Registro, consulta, listado, retirada e historial de evidencias TEXT y REFERENCE con clasificación INTERNAL o CONFIDENTIAL. Toda evidencia pertenece a una organización y un incidente; opcionalmente queda ligada desde su creación a una tarea exacta de una ejecución de ese incidente.

El contenido, tipo, clasificación, título y vínculos originales son inmutables. Corregir o cambiar una asociación requiere crear otra evidencia y, si corresponde, retirar la anterior con motivo. No hay PATCH de contenido ni reasignación silenciosa.

La finalización de tarea admite completionEvidenceId explícito. Si el paso RequiresEvidence, es obligatorio seleccionar una evidencia ACTIVE vinculada a esa misma tarea, ejecución, versión, incidente y organización. Una evidencia general del incidente no satisface automáticamente cualquier tarea.

## Límites conservados

- FILE no está habilitado: file_evidence_not_available (409). No existe carga/descarga de adjuntos en esta entrega.
- RESTRICTED se rechaza con restricted_evidence_policy_pending (409), hasta definir permisos adicionales.
- INTERNAL/CONFIDENTIAL se consultan por miembros activos autorizados, incluidos VIEWER. CONFIDENTIAL es una clasificación; esta entrega no añade un ACL diferente para ese valor.
- SKIPPED sigue rechazado; no se incorpora omisión ni excepción de dependencias.
- STEP_APPROVAL sigue siendo independiente y obligatorio si el paso lo exige.
- Las versiones ARCHIVED siguen bloqueando avance/finalización. Se permite documentar evidencia en una tarea no completada de una ejecución activa aunque su fuente se haya retirado, sin autorizar continuidad.
- No se habilita IA ni se ejecuta ninguna acción sobre infraestructura externa.

Siguen pendientes archivos privados, estados de almacenamiento y recuperación, políticas de retención/eliminación y permisos de RESTRICTED. PDF/PNG/JPEG/TXT y 10 MB siguen siendo propuestas del plan para una entrega futura, no controles ya habilitados.

## Permisos, datos y ciclo de vida

ORG_ADMIN y ANALYST activos pueden registrar evidencias. VIEWER activo consulta; no registra ni retira. Solo ORG_ADMIN activo de la organización puede retirar. PLATFORM_ADMIN no obtiene acceso privado ni decisión sin la membresía/rol correspondientes. Se comprueban organización, membresía, cuenta y lockout.

Título: 1..200 caracteres. TEXT: contenido no vacío hasta 16000. REFERENCE: URI absoluta HTTPS hasta 2048, sin credenciales ni caracteres de control; se normaliza el esquema. El servidor almacena la referencia y **no la consulta ni descarga**.

executionId y taskId deben ser ambos null o ambos UUID. El servidor deriva versionId de la tarea y comprueba el ámbito completo. Para registrar evidencia de tarea, el incidente debe estar IN_RESPONSE, la ejecución IN_PROGRESS y la tarea no completada. No se añade soporte retrospectivo a tareas completadas. El registro general conserva las reglas existentes de edición del incidente; los estados terminales requieren reapertura antes de registrar.

Estados ACTIVE, revisión 1, y WITHDRAWN, revisión 2, terminal. Cada operación exige motivo no vacío hasta 2000.

### Registro y revisión del incidente

Registrar exige expectedIncidentRevision actual. Incrementa la revisión del incidente, genera su evento EVIDENCE_REGISTERED e invalida una revisión de cierre pendiente conforme a la regla general de mutaciones. No modifica la revisión de ejecución. Tras registrar, refrescar los tokens de incidente antes de avanzar una tarea; la respuesta devuelve incidentRevision.

El texto de la evidencia no se interpreta para inferir severidad ni disponibilidad. El evento del incidente contiene la referencia/metadata de la evidencia, no su contenido.

### Retirada y hechos históricos

Retirar exige expectedEvidenceRevision, con motivo. Es una operación del agregado de evidencia: incrementa su revisión y confirma su historial y auditoría, **sin modificar la revisión ni el estado del incidente o la ejecución**.

Se permite retirar después de completar la tarea o cerrar/descartar el incidente. No reabre tareas, no reescribe su finalización y no invalida una decisión de cierre ya consumada. La tarea conserva completionEvidenceId como referencia histórica del soporte válido seleccionado al completar.

Tras retirada, GET y listado devuelven title=null y content=null, incluso para ORG_ADMIN. Conservan identidad, ámbito, clasificación, estado y metadata de creación/retirada; los eventos no contienen una copia del payload original. El listado omite content también para evidencias activas; el detalle autorizado permite consultarlo mientras ACTIVE.

La retirada restringe el acceso por estos endpoints. No elimina físicamente el contenido almacenado ni las copias previas/respaldos. El tratamiento físico y de retención requiere la política pendiente; no se presenta esta subfase como borrado o purga.

### Concurrencia con finalización

Registro y operaciones de ejecución usan el gate compartido y tokens actuales. Retirada también usa el gate y una revisión propia. SQL bloquea la fila de evidencia al validar la finalización.

Si la retirada gana, la finalización con esa evidencia se rechaza. Si la finalización gana, puede retirarse después: ambas operaciones son válidas en ese orden y la tarea conserva su referencia histórica. Una aprobación no evita comprobar evidencia activa al completar.

## API de evidencia

Prefijo: /api/organizations/{organization}/incidents/{incident}/evidence

| Método y sufijo | Función |
|---|---|
| GET vacío?skip=0&limit=50 | Listado sin contenido, incluye metadata de retiradas |
| POST vacío | Registrar, 201 con Location y EvidenceResult |
| GET /{evidence} | Detalle autorizado; título/contenido ocultos si retirado |
| GET /{evidence}/events?afterRevision=0&limit=50 | Historial de evidencia |
| POST /{evidence}/withdrawal | Retirar con motivo y revisión propia |

Cookie autenticada, X-CSRF-TOKEN en escrituras y respuestas no-store. skip 0..1000000, limit 1..100 (50 por defecto); afterRevision >= 0. IDs de otro ámbito no amplían permisos.

Registro TEXT de tarea:

```json
{
  "kind": "Text",
  "classification": "Confidential",
  "title": "Resultado de la comprobación",
  "content": "Registro explícito del resultado observado",
  "executionId": "UUID-de-ejecucion",
  "taskId": "UUID-de-tarea",
  "expectedIncidentRevision": 8,
  "reason": "Documentar el soporte del paso"
}
```

Para evidencia general usar ambos vínculos null. Para referencia: kind=Reference y content con URI HTTPS sin credenciales. kind, classification y expectedIncidentRevision deben aparecer explícitamente.

EvidenceResult contiene evidence (metadata/contenido según estado) e incidentRevision. El servidor obtiene actor, organización e incidente de autenticación/ruta, no del cuerpo.

Retirada:

```json
{
  "expectedEvidenceRevision": 1,
  "reason": "Restringir el acceso al contenido retirado"
}
```

La retirada devuelve EvidenceResult con revisión de evidencia 2 y el incidentRevision observado, sin incrementarlo. Token anterior: stale_evidence_revision (409); repetir con revisión 2: evidence_already_withdrawn (409).

## Finalización de tarea

En el endpoint ya existente PUT executions/{execution}/tasks/{task}/state:

```json
{
  "targetState": "Completed",
  "expectedExecutionRevision": 4,
  "expectedIncidentRevision": 9,
  "reason": "Completar con soporte identificado",
  "completionEvidenceId": "UUID-de-evidencia-activa-de-esa-tarea"
}
```

completionEvidenceId es nullable/opcional en el contrato para conservar llamadas anteriores de pasos sin RequiresEvidence. Si se proporciona, solo se admite con targetState=Completed y debe corresponder a evidencia activa de esa tarea exacta, incluso si el paso no exige evidencia. No puede agregarse después de completar.

Errores: task_evidence_required (409) si falta para un paso obligatorio; active_task_evidence_required (409) si la seleccionada no es elegible; evidence_only_on_completion (400) si se proporciona en otro estado. Los DTO de tareas incluyen completionEvidenceId. Los eventos del incidente asociados a la operación de ejecución incluyen el identificador seleccionado.

Antes de completar siguen aplicando responsable actual, cuenta/rol, estado de ejecución/incidente, versión publicada, dependencias y STEP_APPROVAL.

## Persistencia e integridad

Migración 20261026163753_IncidentTextReferenceEvidence:

- app.incident_evidence: 17 columnas, contenido/ámbito inmutables y retirada terminal.
- app.evidence_events: 10 columnas, historial append-only.
- execution_tasks.completion_evidence_id nullable, sin DEFAULT ni backfill.
- Siete FK nuevas en conjunto: ámbito de incidente/tarea y actores, historial y referencia de finalización.
- Cinco checks nuevos entre las tablas de evidencia y la tarea. La FK de evidencia a tarea conserva los cinco componentes; la referencia de tarea a evidencia se complementa con el guard de ámbito exacto.
- Seis triggers nuevos, incluidos dos de integridad histórica diferida. SQL rechaza modificación/borrado de historial y eliminación física/TRUNCATE de evidencias.
- Guard de tarea conserva aprobación/dependencias y exige evidencia activa de la tarea al completar, con bloqueo de lectura de su fila.
- Down se bloquea con evidencia, eventos, referencias o historia EVIDENCE_REGISTERED. Si está vacío, restaura el guard anterior y conserva tareas previas.
- Se amplía el check de acciones del incidente sin reescribir su historial.

Registrar confirma contenido, evento de evidencia, snapshot de incidente y auditoría SUCCESS en una transacción. Retirar confirma metadata, evento de evidencia y auditoría SUCCESS en otra transacción, para permitir restringir contenido de incidentes terminales sin alterar sus decisiones.

Se usa READ COMMITTED, gate (1869636978,26) y timeout de bloqueo de cinco segundos; lecturas compuestas REPEATABLE READ. Un fallo de persistencia revierte los cambios y revisiones de la operación.

grant-runtime.sql concede a un rol dedicado SELECT/INSERT sobre evidencia/eventos y solo UPDATE de los campos de retirada y selección de evidencia en tarea. No concede modificación de contenido ni borrado. RLS y separación operativa completa de credenciales siguen pendientes; los guards no vuelven inmutable el esquema frente al propietario.

## Archivos de esta entrega

En apps/api:

- Domain/Entities/IncidentEvidence.cs y nuevo campo de ExecutionTask en ExecutionEntities.cs.
- Application/Incidents/IEvidenceService.cs y ampliación compatible de Executions/IExecutionService.cs.
- Infrastructure/Incidents/IncidentEvidenceService.cs, integración de interfaz en IncidentService.cs y selección/DTO/historial en IncidentExecutionService.cs.
- Infrastructure/Persistence/Configurations/EvidenceMapping.cs; ampliación de IncidentMapping.cs y OpenIrpDbContext.
- Migración, Designer y snapshot.
- Api/Controllers/EvidenceController.cs y DI en Program.cs.
- IntegrationTests/EvidenceTests.cs, ajustes de downgrade de pruebas de ejecución/aprobación y TRUNCATE de pruebas de playbooks.

Además: ci/grant-runtime.sql, README, planificación, bitácora y esta guía. Frontend local y trabajo anterior preservados.

## Verificación

- Release sin errores ni advertencias; EF sin diferencias de modelo.
- **114/114 pruebas enfocadas correctas en PostgreSQL 17**, con 39 nuevas de evidencia y 75 anteriores de ejecuciones/aprobaciones. TRX: artifacts/evidence-focused-pg17-190427402c124c4bbc527279f85a5e19/paso26-pg17.trx.
- Cobertura nueva: TEXT/REFERENCE, redacción de retiradas, tarea exacta/soporte general, controles combinados con STEP_APPROVAL, permisos y contextos inactivos, validación de entradas/políticas pendientes, ámbitos, historial/correlación, rollback, SQL, carreras, cierre previo, upgrade/downgrade, HTTP/CSRF y LOGIN SQL limitado.
- **604/604 correctas tanto en PostgreSQL 17 como en PostgreSQL 18.3**, sin fallidas/omitidas. TRX: artifacts/evidence-full-pg17-d0080f5384db4b76be53563ebace8217/paso26-pg17.trx y artifacts/evidence-full-pg18-c254354372ca4bb79163d720bd582781/paso26.trx. Las 39 nuevas y las 114 enfocadas están incluidas en 604.
- Migración aplicada solo a openirp_dev (17.11, 127.0.0.1:5433, propietario openirp) después de respaldo completo restaurado/verificado y cotejo de conteos/historial. Diecisiete migraciones, datos previos conservados; ocho capacidades y tres categorías, tablas de negocio vacías y nuevos registros/referencias vacíos. Evidencia/SQL/conteos/esquema: artifacts/incident-evidence-dev-8e65fcff819d4d48a3494a1a36cf1e21/. Dump openirp_dev-before-incident-evidence.dump, SHA256 F329B7403FECCDB972C3D46841692B0435D4F98481188F8D00CF3C6351AD55A3.
- HTTPS con conexión local configurada: health/OpenAPI 200 y 46 operaciones de playbooks/ejecuciones/aprobaciones/evidencias presentes, anónimos 401. Evidencia artifacts/incident-evidence-http-a250e6f8bf5c46578b40decbcb64b1dd/. Estas llamadas no prueban negocios autenticados sobre la base habitual; esos flujos se prueban en bases temporales. Instancias propias detenidas; servicios/frontend habituales preservados. Sin nuevo commit/push/merge.

No confundir finalización histórica con acceso actual al contenido: una evidencia puede retirarse después sin convertir una tarea completada en pendiente ni otorgar acceso al payload retirado.
