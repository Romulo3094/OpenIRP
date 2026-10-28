# Iteración 23 — Bloqueo de seguridad y continuidad por ejecución

Fecha: 2026-10-27, America/Santiago. Desarrollo local en feature/paso26-identidad-organizaciones.

## Comportamiento

La retirada ordinaria se conserva. La retirada explícita por seguridad archiva una versión publicada,
retira su puntero vigente si corresponde y registra un aviso inmutable con security_hold_id, motivo,
revisión de playbook, actor y correlación. Puede registrar otro aviso sobre una versión ya archivada.
Cada aviso tiene un UUID nuevo. El de mayor revisión para esa versión es el bloqueo vigente.

El bloqueo es una condición operativa sobre la versión de las ejecuciones existentes, no una reescritura
masiva de sus tareas. Sus estados/instrucciones y el DAG permanecen intactos. Las transiciones,
asignaciones, aprobaciones, omisiones y finalización vuelven a comprobar la disponibilidad de la versión.
Mientras no haya continuidad autorizada para el bloqueo actual, se rechazan las operaciones que la requieren.
Puede seguir documentándose el incidente y sus evidencias conforme a los permisos existentes.
No se permite iniciar ejecuciones nuevas de la versión archivada, aunque otra ejecución tenga continuidad.

ORG_ADMIN decide justificadamente CONTINUE o CANCEL sobre una ejecución de su propia organización,
con el ID exacto del bloqueo vigente y ambas revisiones optimistas. PLATFORM_ADMIN no obtiene permisos
sobre ejecuciones privadas por administrar el catálogo público. La decisión requiere cuenta/membresía/
organización activas e incidente IN_RESPONSE. No cambia versiones ni reactiva su publicación.

CONTINUE permite aplicar los controles ordinarios de la ejecución archivada: no elimina responsables,
dependencias, aprobación de paso, evidencia exacta activa ni permisos. No acredita seguridad técnica automática.
CANCEL termina esa ejecución con motivo, conservando tareas e incidente. Se puede cancelar después de autorizar
continuidad, registrando otra decisión. La ruta genérica de cancelación exige usar la decisión con hold ID cuando existe aviso de seguridad.

Una retirada nueva invalida automáticamente las continuidades del aviso anterior para todas las ejecuciones
basadas en esa versión. No hace falta actualizar cada organización ni reutilizar autorizaciones antiguas.
Las decisiones previas se conservan. Una ejecución terminada no se reabre ni se invalida retrospectivamente;
la consulta de su bloqueo se limita a los avisos existentes hasta FinishedAt.

## Alcance del aviso

El aviso afecta únicamente al VersionId indicado. Retirar una versión anterior no retira otra publicación
vigente del mismo playbook. Los efectos sobre clones/versiones derivadas requieren una política de propagación
adicional; esta entrega no presupone que compartan automáticamente el bloqueo.
Las continuidades no expiran por tiempo en esta entrega: están vinculadas al aviso exacto, a la ejecución y
sus controles actuales; dejan de habilitar operación al aparecer otro aviso o cancelarse/terminarse la ejecución.

## API

| Método | Ruta | Uso |
|---|---|---|
| POST | /api/platform/playbooks/{playbook}/versions/{version}/security-withdrawals | Retirada/aviso de versión pública por PLATFORM_ADMIN. |
| POST | /api/organizations/{organization}/playbooks/{playbook}/versions/{version}/security-withdrawals | Retirada/aviso privado por ORG_ADMIN de la organización fuente. |
| GET | /api/organizations/{organization}/incidents/{incident}/executions/{execution}/security-hold | Snapshot del bloqueo aplicable y decisiones del mismo. |
| POST | /api/organizations/{organization}/incidents/{incident}/executions/{execution}/security-holds/{securityHold}/decision | CONTINUE/CANCEL administrativo para el ID vigente. |
| GET | /api/organizations/{organization}/incidents/{incident}/executions/{execution}/security-decisions | Historia paginada de todas las decisiones, incluidas las de avisos anteriores. |

Retirada: PlaybookVersionCommand con reason y expectedRevision del playbook; devuelve SecurityHoldItem.
Decisión: DecideSecurityHold con continue (booleano explícito), expectedExecutionRevision,
expectedIncidentRevision y reason; devuelve ExecutionItem. Motivos obligatorios hasta 2000 caracteres.
Las escrituras requieren CSRF. Los contratos están tipados en Swagger/OpenAPI Development.
Las lecturas conservan autorización organizacional y no-store. La historia acepta afterRevision>=0 y limit=1..100.
La consulta devuelve ContinuityAuthorized como condición de continuidad, no como permiso general para una transición.

```json
{
  "continue": true,
  "expectedExecutionRevision": 4,
  "expectedIncidentRevision": 7,
  "reason": "Continuidad excepcional revisada para este aviso y esta ejecución."
}
```

Usar revisiones reales de la respuesta vigente. Omitir continue o una revisión produce 400;
ID obsoleto, decisión duplicada o revisiones obsoletas producen conflicto. La autorización se relee después del gate compartido.
No se emplean los indicadores de autoaprobación de STEP_APPROVAL: esta es una decisión administrativa directa con motivo y auditoría.

## Persistencia y controles

Migración 20261027031839_ExecutionSecurityHolds: playbook_security_holds (8 columnas) y
execution_security_decisions (13 columnas), seis FK entre ambas, ámbitos compuestos y unicidad por
versión/revisión, ejecución/bloqueo/acción y ejecución/revisión. Ambas historias son append-only en EF y SQL.

Siete triggers nuevos, tres diferidos: guard de fila/TRUNCATE y validación por cada historia;
validación final de ejecución para impedir completar un bloqueo sin continuidad o cancelar sin decisión vigente.
La retirada exige fuente archivada, administrador elegible y auditoría atómica. La decisión exige ámbito,
bloqueo actual, administrador organizacional y eventos correlacionados con las revisiones.

app.execution_version_usable concentra la condición de versión publicada o continuidad del aviso vigente.
Siete funciones SQL previas se conservan salvo su comprobación de disponibilidad de versión; se mantienen
los guards de asignación, dependencias, aprobación, evidencia/FILE y revocación de excepciones.
El inicio continúa exigiendo versión publicada vigente. No se habilitan permisos globales ni registro público.
Los eventos de continuidad cambian el contexto de riesgo del incidente; una excepción de dependencia anterior
puede necesitar nueva evaluación por las reglas de contexto existentes.

Las escrituras comparten el gate transaccional PostgreSQL; las carreras se fuerzan observando dos escritores
en pg_locks antes de liberar el gate. GET usa RepeatableRead para evitar mezclar versión, aviso y decisión.
Down restaura las funciones anteriores en vacío y bloquea pérdida de avisos, decisiones o su historia de incidente.
Runtime recibe SELECT/INSERT sobre los registros nuevos; el aislamiento por RLS y la separación operativa completa siguen pendientes.

## Archivos

Nuevos: SecurityHoldEntities.cs, SecurityHoldContracts.cs, SecurityHoldMapping.cs,
PlaybookSecurityHoldService.cs, IncidentSecurityHoldService.cs, PlaybookSecurityControllers.cs,
SecurityHoldTests.cs y migración/Designer ExecutionSecurityHolds.
Integraciones en IPlaybookDraftService.cs, IExecutionService.cs, PlaybookDraftService.cs,
IncidentExecutionService.cs, ExecutionsController.cs, OpenIrpDbContext.cs, ExecutionMapping.cs,
IncidentMapping.cs, snapshot EF, ci/grant-runtime.sql y fixture TRUNCATE de PlaybookDraftPersistenceTests.cs.
README, planificación maestra y bitácora actualizados. Trabajo previo y frontend conservados.

## Verificaciones

- Release final con SDK .NET 10.0.403: cero errores/advertencias; EF sin diferencias pendientes de modelo.
- Primera selección enfocada completada: **17/17 en PostgreSQL 17**. Dos intentos anteriores de aplicar la migración temporal detectaron terminadores SQL y una expresión CASE, corregidos antes de completar esta selección; no afectaron la base habitual.
- Selección final de módulos relacionados: **312/312 en PostgreSQL 17 y 312/312 en PostgreSQL 18.3**, sin fallidas/omitidas: **32 casos nuevos** de seguridad y 280 existentes. Las 32 están incluidas en las 312. No se repitió la suite global de toda la API.
- [TRX PG17](../artifacts/security-related-pg17-cff5ff6c18264118aec6ba427f65efdb/paso26-pg17.trx), [TRX PG18.3](../artifacts/security-related-pg18-00b62d5d82774e10ad035991f44a692b/paso26.trx), [matriz y hashes](../artifacts/security-holds/validation.json).
- Cubre retirada pública/privada, bloqueo actual, continuidad y cancelación posterior, nueva retirada que invalida decisión antigua, roles/CSRF, scopes, revisión optimista, historia EF/SQL/TRUNCATE, rollback de fuente/ejecución/historiales, carreras forzadas, SQL sin eventos, runtime limitado, snapshots concurrentes y migración reversible en vacío/protegida con historia. STEP_APPROVAL/evidencia permanecen obligatorios tras autorizar continuidad.
- El primer lanzador nativo de PostgreSQL 18 se quedó detenido antes de las pruebas bajo redirección de salida. Se detuvieron solo el PowerShell y clúster propios identificados; se reinició sin esa redirección. La ejecución final es la del TRX enlazado. El servicio PostgreSQL de Windows no se detuvo.
- Respaldo anterior a migración 22 restaurado en PostgreSQL aislado, conteos e historial coincidentes. Dump artifacts/security-holds/openirp_dev-before-security-holds.dump; SHA256 **6FF919DC56211F8ABA78B72CA49C39C2358EE4251F62A27578D34635F807AF21**. [Restauración](../artifacts/security-holds/restore-verified.txt).
- Migración **20261027031839_ExecutionSecurityHolds** aplicada solo a openirp_dev local en 127.0.0.1:5433, propietario openirp, tras comprobar respaldo, conteos y hashes de las siete funciones previas. **22 migraciones**, datos anteriores conservados: ocho capacidades/tres categorías, identidad/negocio/historiales vacíos y ambas tablas nuevas vacías. [Conteos](../artifacts/security-holds/after-counts.json).
- Esquema real comprobado: 8+13 columnas, seis FK y siete triggers nuevos (tres diferidos); controles anteriores de aprobación/FILE/revocación conservados. [SQL final](../artifacts/security-holds/ExecutionSecurityHolds-final.sql), [verificador](../artifacts/security-holds/Verify-Schema.sql), [resultado](../artifacts/security-holds/schema-verified.txt).
- HTTPS posterior a migración: health/OpenAPI/Swagger 200 y **67 operaciones revisadas** con anónimos 401. Los cinco endpoints nuevos tienen contratos tipados. [Resultados](../artifacts/security-http-74e8f27e21c54091a9b36241af0a0c56/results.json). No es el total de endpoints; health es estático. Las operaciones autenticadas de negocio se probaron en bases temporales.
- MCP actualizado: 3369 nodos/10117 relaciones. Graphify AST/HTML: 2884 nodos/8002 relaciones/209 comunidades, sin IA externa.
- Procesos propios temporales de pruebas/restauración/HTTPS cerrados; PostgreSQL/Ollama habituales, frontend y trabajo previo preservados. Sin commit, push ni merge; HEAD sigue en 9cec6a075c918ca88bf02fdda6b8ad750c8cf4a3.


## Pendientes

Política de propagación a clones/versiones derivadas, alternativas operativas verificadas, revisión agregada
de riesgos al cierre, RESTRICTED, políticas de archivos e IA supervisada. La decisión humana de continuidad
no sustituye una evaluación técnica de la versión retirada.
