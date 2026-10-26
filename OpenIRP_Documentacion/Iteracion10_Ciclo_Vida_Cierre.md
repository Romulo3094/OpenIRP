# Ciclo de vida y cierre de incidentes — desarrollo local

Iteración 10, 23 de octubre de 2026, rama `feature/paso26-identidad-organizaciones`. Preserva las iteraciones 7–9 locales; el último commit publicado sigue siendo `0c70669`. Esta entrega no realiza commit, push ni merge.

## Transiciones explícitas

| Estado actual | Destinos permitidos |
|---|---|
| REGISTERED | UNDER_EVALUATION |
| UNDER_EVALUATION | IN_RESPONSE, DISCARDED |
| IN_RESPONSE | UNDER_EVALUATION, RESOLVED |
| RESOLVED | IN_RESPONSE, CLOSED con controles de cierre |
| CLOSED | UNDER_EVALUATION mediante reapertura |
| DISCARDED | UNDER_EVALUATION mediante reapertura |

No se permiten saltos ni transiciones al mismo estado. Cada operación exige motivo no blanco de hasta 2000 caracteres y expectedRevision explícita. Una revisión obsoleta produce 409; estado o campos inválidos, 400. ORG_ADMIN y ANALYST activos del tenant pueden operar; VIEWER consulta. PLATFORM_ADMIN necesita membresía y rol organizacional autorizado.

La transición a RESOLVED requiere resolution no blanca de hasta 4000 caracteres. La resolución se conserva al cerrar y queda en los snapshots. Volver a IN_RESPONSE o reabrir elimina la resolución vigente y preserva las anteriores en el historial. En otras transiciones no se acepta el campo resolution.

Se permite evaluar/responder/resolver con clasificación aún pendiente, pero no cerrar ni solicitar aprobación de cierre. El descarte es una decisión explícita motivada, conserva historia y máximo, y admite reapertura; no equivale a cierre ni rebaja severidad.

## Cierre según máximo histórico

El cierre requiere resolución documentada, clasificación vigente completa con regla v1, severity y maximumSeverityReached no nulos, y responsable asignado activo/no bloqueado con rol ANALYST u ORG_ADMIN.

- Máximo LOW/MEDIUM: el responsable asignado activo puede cerrar desde RESOLVED.
- Máximo HIGH/CRITICAL: el responsable asignado solicita revisión y un ORG_ADMIN activo la decide. Una severidad actual rebajada no elimina este requisito.
- Pending o NotEvaluated: cierre y solicitud de aprobación se rechazan.

Una aprobación válida confirma CLOSED y la decisión APPROVED en la misma transacción. No queda un permiso aprobado reutilizable entre revisión y cierre. El solicitante y la asignación se revalidan al aprobar, además del contexto activo del revisor.

Si el solicitante perdió su cuenta activa, quedó bloqueado o perdió su membresía/asignación, no puede aprobarse su solicitud. Los cambios de Identity no generan por sí solos una escritura en cada incidente: una solicitud puede seguir mostrándose Pending, pero la aprobación comprueba el contexto actual y se rechaza mientras no sea válido. Puede rechazarse o invalidarse al modificar el incidente.

## Solicitudes, decisiones e invalidación

La revisión vigente forma parte del agregado del incidente; cada evento conserva su snapshot inmutable. Esta materialización del diseño preliminar mantiene la revisión, el cierre y el historial en una sola transacción y permite consultar solicitudes anteriores mediante GET events.

closureReview contiene: id, requestedAtRevision, requestedByUserId, status, decidedByUserId, decisionRevision, selfApproval y selfApprovalJustification. Los instantes y motivos se consultan en los eventos de esas revisiones; no son fechas enviadas por el cliente. Los estados son Pending, Approved, Rejected e Invalidated.

La solicitud se vincula a su revisión exacta y solo existe una Pending vigente. Cualquier otra escritura del incidente, incluyendo cambios de título, asignación, CIA, contexto, criticidad, severidad, activos o capacidades, invalida la revisión pendiente. Se aplica también a escrituras que confirman el mismo valor y avanzan la revisión. El evento de la modificación registra el estado Invalidated, actor y revisión; el evento original conserva Pending.

Rechazar mantiene RESOLVED y conserva la resolución. Una solicitud posterior recibe otro UUID; el UUID anterior no puede decidir la nueva solicitud. Reabrir un CLOSED invalida la decisión anterior en el snapshot vigente y conserva la aprobación original en su evento, incluyendo su excepción si existió.

La revisión se refiere al agregado del incidente. Cambios del perfil general o catálogo no se proyectan automáticamente como modificaciones de todos los incidentes; la clasificación no usa esos valores dinámicos. Las precondiciones de ejecuciones, tareas y evidencias deberán integrarse cuando se implementen esos módulos; esta subfase implementa cierre manual con resolución, clasificación y revisión.

## Autoaprobación excepcional

Si quien aprueba es el solicitante asignado, debe ser ORG_ADMIN activo y enviar selfApproval=true con justificación adicional no blanca de hasta 2000 caracteres. El motivo general también es obligatorio. El servidor determina si es autoaprobación y rechaza declaraciones inconsistentes.

Una aprobación de otro administrador usa selfApproval=false y no adjunta justificación de autoaprobación. Un rechazo también usa false. La excepción queda identificada en los snapshots; no se presenta como doble control independiente. Motivo, resolución y justificación se conservan en el historial organizacional y no se copian a la auditoría administrativa global.

## Endpoints

Todos requieren autenticación, cookies y X-CSRF-TOKEN vigentes. Arranque: `./ci/Start-LocalApi.ps1`, HTTPS `https://localhost:7113`.

| Método/ruta | Uso |
|---|---|
| PUT `/api/organizations/{org}/incidents/{incident}/state` | Transición explícita; devuelve IncidentItem. |
| POST `/api/organizations/{org}/incidents/{incident}/closure-requests` | Solicitud HIGH/CRITICAL; 201 con el agregado actualizado. |
| PUT `/api/organizations/{org}/incidents/{incident}/closure-requests/{request}` | Decisión ORG_ADMIN; aprobación cierra atómicamente. |
| GET detalle/listado/events existentes | Resolución y revisión de cierre vigente o histórica. |

Ejemplo de resolución:

```json
{
  "targetState": "Resolved",
  "resolution": "Se recuperó el servicio y se verificaron las medidas aplicadas",
  "reason": "Resolución revisada con evidencia",
  "expectedRevision": 7
}
```

Solicitud:

```json
{ "reason": "Solicitar revisión del cierre", "expectedRevision": 8 }
```

Aprobación independiente:

```json
{
  "approve": true,
  "selfApproval": false,
  "selfApprovalJustification": null,
  "reason": "Resolución y contexto revisados",
  "expectedRevision": 9
}
```

approve, selfApproval y expectedRevision deben aparecer explícitamente. En solicitudes y transiciones también se exigen sus respectivas revisiones/campos requeridos. El cliente no puede imponer aprobaciones, máximo ni estado mediante campos extra de otros contratos.

## Estados terminales e integridad

CLOSED y DISCARDED admiten lectura, pero rechazan las escrituras ordinarias hasta una reapertura explícita a UNDER_EVALUATION. El control está en el escritor común y PostgreSQL también bloquea ediciones de registros terminales, saltos de estado y cierres HIGH/CRITICAL sin metadatos de aprobación.

Resolución, estado, revisión, revisión de cierre, snapshot y SUCCESS de auditoría confirman o revierten juntos. Las carreras contra otra aprobación o una modificación no pueden confirmar sobre la misma revisión ni aprobar una solicitud obsoleta.

La protección SQL de máximo histórico y el historial append-only permanecen habilitados. Los checks validan estados, resolución, coherencia de la solicitud/decisión, justificación de autoaprobación y referencias de revisión; las FK conservan referencias a solicitante/revisor. Un propietario puede alterar objetos; SQL directo y futuros escritores deben respetar el protocolo. RLS y separación operativa completa de credenciales siguen pendientes.

## Migración y archivos

`20261023220910_IncidentLifecycleClosure` añade 18 columnas nullable sin DEFAULT entre incidents e incident_events, cuatro checks de resolución/revisión, amplía los checks de estados/acciones, agrega FK de usuarios y unicidad del UUID de revisión vigente. Crea el trigger de ciclo de vida. Los registros anteriores permanecen REGISTERED sin inventar resolución ni aprobación.

Down se niega si perdería cualquier transición, resolución o solicitud/decisión histórica, aun después de descartar y reabrir. Las diez migraciones anteriores se conservan.

Código en apps/api/:

- Domain/Entities/IncidentWorkflow.cs e IncidentEntities.cs.
- Application/Incidents/IncidentWorkflowContracts.cs e IIncidentService.cs.
- Infrastructure/Incidents/IncidentWorkflowService.cs e IncidentService.cs.
- Infrastructure/Persistence/Configurations/IncidentMapping.cs, migración/designer y snapshot.
- Api/Controllers/IncidentWorkflowController.cs.
- IntegrationTests/IncidentWorkflowTests.cs, IncidentWorkflowRuleTests.cs y ampliación del LOGIN runtime.

También se actualizan esta guía, README y la planificación maestra.

## Verificación

- Release sin errores ni advertencias; EF sin diferencias de modelo.
- Prueba enfocada del módulo: **49/49 correctas** en PostgreSQL 17, cero fallidas/omitidas. TRX: `artifacts/workflow-focused-pg17-c7f680e9bf824e41b62de77756658567/paso26-pg17.trx`.
- Regresión completa PostgreSQL 17: **363/363 correctas**, cero fallidas/omitidas. TRX: `artifacts/paso26-pg17-e2d7321a98b64e10b2bfcf11654df4a9/paso26-pg17.trx`.
- Regresión completa PostgreSQL 18.3 nativo aislado: **363/363 correctas**, cero fallidas/omitidas. TRX: `artifacts/paso26-ad8a64f0e02d4433ba0aeacc135d8397/paso26.trx`. EF sin diferencias; clúster temporal detenido y servicio del sistema sin modificar.
- Son 49 casos nuevos sobre los 314 anteriores: dos de dominio (36 pares de estados, nueve transiciones permitidas) y 47 de integración. Los 49 enfocados ya están incluidos en los 363, no se suman otra vez. Se amplió también el LOGIN SQL limitado para asignar, transicionar, resolver, solicitar, autoaprobar justificadamente, rechazar edición terminal y reabrir.
- Cobertura: cierre LOW/MEDIUM asignado, máximo HIGH/CRITICAL aun con severidad actual baja, Pending/NotEvaluated, rechazo/reemplazo de solicitudes, autoaprobación y declaraciones inconsistentes, invalidación por modificaciones/reapertura, roles/contextos inactivos/solicitante reasignado, aislamiento de tenants/incidentes, transiciones y campos inválidos, bloqueo de todas las escrituras ordinarias terminales, carreras forzadas, rollback por fallos de ambos historiales, protecciones SQL, upgrade/downgrade e HTTP/CSRF.
- Migración aplicada solo a openirp_dev en PostgreSQL 17 habitual, 127.0.0.1:5433. Dump completo restaurado en otro contenedor sin puertos ni volúmenes; conteos e historial cotejados antes de aplicar. Se preservaron las tablas de negocio/identidad/auditoría vacías, ocho capacidades y tres categorías. No se fabricaron cuentas, resoluciones o decisiones.
- Respaldo/SQL/conteos/restauración/esquema: `artifacts/incident-workflow-dev-1b661a9a3b1345f5a980a76fdeb4da19/`. Dump `openirp_dev-before-incident-workflow.dump`, SHA256 `84E749E806012E412060F2B2302D121635A76503843CB8B556D9A6DD8015FDE2`. Dieciocho columnas nullable sin DEFAULT, cuatro checks nuevos y cuatro FK validados, identidad única de solicitud y triggers de ciclo/máximo/historial habilitados.
- HTTPS real: health 200, OpenAPI 200 con las tres rutas nuevas, PUT estado/POST solicitud/PUT decisión anónimos 401. Evidencia: `artifacts/incident-workflow-http-1feb3e491dd64658b2e3fa4e60a9bcf5/`. Proceso propio detenido; solo el cliente de comprobación aceptó el certificado local.
- Cambios locales, sin nuevo commit/push/merge. Los contenedores habituales PostgreSQL/Ollama se conservan y las instancias temporales se detuvieron.

## Siguiente bloque

Playbooks públicos/privados, versiones publicadas inmutables, pasos/dependencias y clonación. Después, ejecución guiada, evidencias e integración de sus condiciones de cierre, manteniendo los controles actuales.
