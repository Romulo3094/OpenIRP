# Iteración 22 — Revocación explícita de excepciones de dependencia

Fechas de desarrollo: 2026-10-26–27 (America/Santiago). Rama feature/paso26-identidad-organizaciones.

## Resultado y reglas

ORG_ADMIN puede revocar una aprobación concreta de excepción de dependencia con motivo obligatorio y revisiones
optimistas de incidente/ejecución. La revocación se registra por separado; REQUESTED/APPROVED/REJECTED originales no se editan.
Una aprobación admite una sola revocación. Revocarla no equivale a rechazar retrospectivamente su solicitud.

La operación requiere organización activa, administrador/cuenta actuales elegibles, incidente IN_RESPONSE,
ejecución IN_PROGRESS y versión PUBLISHED. No autoriza continuidad sobre una versión retirada.
Solo se permite mientras la tarea está PENDING, BLOCKED o IN_PROGRESS. No reescribe tareas ni ejecuciones terminadas.
Si la tarea está IN_PROGRESS, la operación la deja BLOCKED en la misma transacción, conservando el responsable.
El motivo, registro de revocación, estado, revisiones, eventos de tarea/incidente y auditoría se confirman o revierten juntos.

Puede retirarse una aprobación ya caducada por contexto o reasignación, incluso si la tarea no tiene responsable o
su responsable ya no es elegible: la autorización que se exige al revocar es la del administrador actual.
La versión debe seguir utilizable. El bloqueo de una tarea en curso no exige una transición previa del responsable.

La revocación afecta al ID de aprobación indicado. No revoca otras aristas ni aprobaciones posteriores independientes;
si la tarea está en curso, se bloquea al ejecutar la revocación. La operación no altera instrucciones, DAG, flags,
SKIPPED del predecesor ni severidad. No resuelve ni cierra el incidente.
Una aprobación revocada nunca vuelve a habilitar su arista; para recuperar autorización se requiere otra solicitud y
revisión administrativa. Las aprobaciones de paso y evidencias siguen revalidándose por sus reglas independientes.

## API

Base: /api/organizations/{organization}/incidents/{incident}/executions/{execution}/tasks/{task}/dependencies/{predecessor}/exceptions/{exceptionApproval}

| Método | Ruta final | Contrato |
|---|---|---|
| POST | /revocation | ExecutionCommand; devuelve ExecutionItem con las revisiones actuales y el bloqueo, si corresponde. |
| GET | /revocation | DependencyRevocationItem del ID de aprobación; 404 si todavía no existe revocación. |

exceptionApproval es el ID del registro APPROVED, no el de REQUESTED ni el de la revocación.

```json
{
  "expectedExecutionRevision": 8,
  "expectedIncidentRevision": 11,
  "reason": "Se retira la autorización tras revisar el riesgo residual."
}
```

Revisiones ilustrativas: usar las obtenidas de la respuesta vigente. Motivo no vacío, hasta 2000 caracteres;
ambas revisiones son obligatorias. Escritura requiere sesión, CSRF y ORG_ADMIN de la organización correspondiente.
PLATFORM_ADMIN no obtiene acceso implícito. El administrador puede revocar su propia aprobación: es una reducción
de autorización, con motivo y auditoría; no utiliza los indicadores de autoaprobación del contrato de decisión.

GET mantiene el acceso de lectura organizacional de ORG_ADMIN/ANALYST/VIEWER elegibles y Cache-Control: no-store.
Se exige el ámbito exacto de organización/incidente/ejecución/tarea/predecesor/aprobación. La consulta histórica sigue
disponible para participantes autorizados aunque la tarea o ejecución hayan terminado después de la revocación.

DependencyRevocationItem contiene Id, ApprovalId, TaskId, PredecessorTaskId, ExecutionRevision, IncidentRevision,
ActorUserId, CorrelationId, Reason, BlockedTask y OccurredAt. Ambos endpoints están tipados en Swagger Development.

## Consulta de vigencia

GET dependency-readiness conserva LatestDecisionAction=APPROVED como dato histórico. Añade RevocationId,
RevokedAt y RevocationReason cuando la aprobación de la última solicitud fue revocada.
Satisfaction pasa a Unsatisfied para ese vínculo. Consultar solo APPROVED ya no basta para interpretar vigencia.
Si existe una solicitud/aprobación posterior, esos campos describen la posterior; la revocación anterior se consulta por su ID exacto.
Los historiales y las revocaciones se preservan y no se reconstruyen retroactivamente.

## Persistencia y concurrencia

Migración 20261027024554_DependencyExceptionRevocations: tabla app.dependency_exception_revocations con 18 columnas,
FK compuesta de diez campos al ámbito de la aprobación original y FK al actor. Índices únicos por ApprovalId y tarea/revisión.
Runtime recibe SELECT/INSERT en la tabla, sin UPDATE/DELETE. EF rechaza modificación/borrado.

Tres triggers nuevos: guard de fila append-only, guard de TRUNCATE y validación diferida de autorización/estado/historial.
El último exige aprobación real, administrador elegible, revisiones actuales, bloqueo final coherente y eventos correlacionados.
app.has_dependency_exception conserva sus controles anteriores y excluye las aprobaciones con revocación.
Los guards de tareas, STEP_APPROVAL y FILE se conservan; las transiciones y la consulta usan el predicado compartido.
No se añade una ruta paralela que pueda ejecutar una tarea omitiendo dependencias.

Las escrituras usan el gate transaccional compartido y comprueban de nuevo autorización/revisiones después de adquirirlo.
Las carreras se prueban manteniendo ambos escritores en espera, verificados en pg_locks antes de liberar el gate.
Down restaura el predicado anterior en vacío, pero rechaza eliminar revocaciones o su historial de incidente.
RLS y separación operativa completa de credenciales siguen pendientes; no se promete inmutabilidad frente a administradores privilegiados.

## Archivos

Nuevos: DependencyExceptionRevocation.cs, DependencyExceptionRevocationMapping.cs, DependencyRevocationContracts.cs,
IncidentDependencyRevocationService.cs, DependencyRevocationTests.cs y migración/Designer DependencyExceptionRevocations.
Integraciones: IExecutionService.cs, ExecutionsController.cs, DependencyReadinessContracts.cs,
IncidentDependencyReadinessService.cs, OpenIrpDbContext.cs, ExecutionMapping.cs, IncidentMapping.cs, snapshot EF,
ci/grant-runtime.sql y fixtures de TRUNCATE en DependencyExceptionTests.cs, SkipAuthorizationTests.cs y PlaybookDraftPersistenceTests.cs.
README, planificación maestra y bitácora actualizados. Frontend, configuración y trabajo anterior preservados.

## Verificación

- Release final con SDK .NET 10.0.403: cero errores/advertencias; EF sin diferencias de modelo pendientes.
- Primera comprobación enfocada: **38/38 en PostgreSQL 17**, incluidas carreras con ambos escritores esperando en pg_locks.
- Selección final ampliada: **280/280 en PostgreSQL 17 y 280/280 en PostgreSQL 18.3**, sin fallidas/omitidas. Incluye **43 casos nuevos** de revocación y 237 existentes: ejecuciones, STEP_APPROVAL, omisiones, excepciones, cobertura/vigencia y protección de definiciones/fixtures TRUNCATE.
- Las 43 están incluidas en las 280. No se repitió la suite global de toda la API. [TRX PG17](../artifacts/revocation-related-pg17-2a3f8b3c43a2477b8a0942ba0b897936/paso26-pg17.trx), [TRX PG18.3](../artifacts/revocation-related-pg18-c358fb522f014cc796a44f3683e8104e/paso26.trx), [matriz y hashes](../artifacts/dependency-revocations/validation.json).
- Cubre motivo/revisiones/CSRF, roles y cuentas actuales, organizaciones distintas, revocación de autorización caducada, responsable deshabilitado, aristas independientes, reautorización nueva, bloqueo atómico, rollback de ambas revisiones/estado/historiales, doble revocación y carrera contra completar, SQL sin eventos, append-only EF/SQL, cuenta runtime y downgrade protegido.
- Respaldo completo anterior a la migración 21 restaurado en PostgreSQL aislado sin puertos publicados, con conteos e historial idénticos. Dump: artifacts/dependency-revocations/openirp_dev-before-dependency-revocations.dump. SHA256: **37CF59FDCDC0F306291E8683DF7FB89A6453447D6CB94D0B398126A8221984BF**. [Restauración](../artifacts/dependency-revocations/restore-verified.txt).
- Migración **20261027024554_DependencyExceptionRevocations aplicada únicamente a openirp_dev local, 127.0.0.1:5433, propietario openirp**. Se comprobaron destino, conteos actuales y hash del respaldo antes de aplicar. **21 migraciones**; conteos previos conservados, ocho capacidades/tres categorías e identidad/negocio/historiales vacíos. Tabla nueva sin filas. [Conteos](../artifacts/dependency-revocations/after-counts.json).
- SQL incremental revisado; esquema real: 18 columnas, dos FK (una compuesta de diez campos), tres triggers nuevos, uno diferido. Predicado previo preservado salvo exclusión por ID de aprobación revocada; guards anteriores de tarea/STEP_APPROVAL/FILE conservados. [SQL](../artifacts/dependency-revocations/DependencyExceptionRevocations.sql), [comprobación](../artifacts/dependency-revocations/Verify-Schema.sql), [resultado](../artifacts/dependency-revocations/schema-verified.txt).
- HTTPS posterior a migración: health/OpenAPI/Swagger 200; **62 operaciones del conjunto revisado** responden 401 sin sesión. GET/POST de revocación figuran con contratos tipados. [Resultados](../artifacts/revocation-http-25d3cea699e542e4862424f6f7a35978/results.json). No es el total de endpoints; health es estático. Los flujos autenticados se probaron en bases temporales, sin fabricar cuentas ni datos en la base habitual.
- MCP actualizado: 3211 nodos/9669 relaciones. Graphify AST/HTML: 2768 nodos/7644 relaciones/202 comunidades, sin IA externa.
- Servidores propios de pruebas/restauración y API temporal detenidos; PostgreSQL/Ollama habituales y frontend preservados. Sin commit, push ni merge nuevos; HEAD continúa en 9cec6a075c918ca88bf02fdda6b8ad750c8cf4a3.


## Pendientes

Alternativas operativas verificadas, continuidad por el security_hold_id vigente, revisión agregada de riesgos al cierre,
RESTRICTED, políticas de archivos e IA supervisada. Esta entrega implementa revocación de excepciones individuales,
no revocación general de omisiones ni retroceso de estados terminales.
