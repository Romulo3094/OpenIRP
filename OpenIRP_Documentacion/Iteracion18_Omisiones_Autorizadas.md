# Iteración 18 — Autorización de omisiones de tareas

Fecha: 2026-10-26. Rama feature/paso26-identidad-organizaciones. Desarrollo local posterior al commit API 9cec6a0; Swagger local y frontend preservados.

## Alcance

Se incorpora SKIP_AUTHORIZATION como operación distinta de STEP_APPROVAL. El responsable activo solicita omitir
una tarea PENDING o BLOCKED; ORG_ADMIN de esa organización aprueba o rechaza. Una tarea IN_PROGRESS debe
bloquearse primero, con motivo. En esta primera subfase se exige revisión administrativa tanto para pasos
obligatorios como opcionales: es una decisión conservadora de implementación, no una política nueva atribuida al usuario.

La solicitud exige motivo, evaluación de impacto y riesgo residual no vacíos, hasta 2000 caracteres cada uno.
El servidor guarda además una instantánea de predecesores y sucesores directos (UUID de tarea y estado),
y las revisiones del incidente y la ejecución. No se confía en una lista de dependencias enviada por el cliente.

Una aprobación pasa la tarea a SKIPPED en la misma transacción que la decisión, el historial de ejecución,
el evento del incidente y la auditoría. El rechazo conserva el estado anterior. Cada solicitud admite
una sola decisión definitiva; un rechazo permite otra solicitud.

## Contexto y concurrencia

- Escritura bajo el gate PostgreSQL existente (1869636978,26), revisión optimista y revalidación de cuenta,
  organización, rol, responsable, estado y publicación.
- La decisión exige que no haya ninguna escritura intermedia del incidente ni de la ejecución desde la solicitud.
  Esto incluye una reasignación al mismo usuario, cambios de otra tarea, evidencia registrada o cambios del incidente.
  Se devuelve skip_authorization_context_changed; el responsable debe solicitar de nuevo con evaluación actualizada.
- Las solicitudes antiguas se conservan como historia; no se eliminan al quedar obsoletas.
- Solicitar dos veces con el mismo contexto o decidir dos veces no sobrescribe la primera operación.
- Aprobar la solicitud propia exige SelfApproval=true y SelfApprovalJustification no vacío (máximo 2000).
  Rechazar, o decidir sobre otro solicitante, exige SelfApproval=false y justificación de autoaprobación null.
- PLATFORM_ADMIN no obtiene facultades privadas sin membresía organizacional elegible.
- Los lectores activos, incluido VIEWER, consultan el historial dentro de su organización.
- La respuesta de lectura usa RepeatableRead y paginación por afterRevision; no cambia revisiones.

## Dependencias, evidencias y cierre

SKIPPED es terminal e irreversible en esta entrega. Nunca equivale a COMPLETED y no satisface dependencias.
Los sucesores no pueden empezar, completar ni solicitar STEP_APPROVAL si falta un predecesor COMPLETED.
No se implementa todavía una alternativa validada o excepción de dependencia que permita ejecutar ese sucesor.

Una tarea dependiente también puede omitirse únicamente con su propia solicitud y decisión independientes;
no se propaga una autorización anterior. Esto no autoriza ejecutarla ni convierte la dependencia en satisfecha.
Las aprobaciones de omisión no se reutilizan como aprobaciones de paso.

Omitir no selecciona completionEvidenceId ni afirma que existe evidencia de una acción no realizada.
Las evidencias ya documentadas se conservan. Para ejecutar/completar normalmente siguen vigentes STEP_APPROVAL,
evidencia activa exacta, disponibilidad/integridad FILE, responsable y dependencias completas.

Una ejecución puede finalizar cuando todas sus tareas son COMPLETED o SKIPPED debidamente autorizadas.
La ejecución finalizada no resuelve ni cierra el incidente; conserva la respuesta y exige el ciclo de vida humano habitual.
La integración ampliada del cierre con revisión agregada de riesgo residual sigue pendiente. La instantánea y
los riesgos documentados no constituyen un cálculo automático de aceptabilidad ni sustituyen decisiones humanas.
La cancelación continúa siendo una operación independiente.

## Endpoints

Base: /api/organizations/{organization}/incidents/{incident}/executions/{execution}/tasks/{task}

| Método | Ruta | Operación |
|---|---|---|
| GET | /skip-authorizations?afterRevision=0&limit=50 | Historia por revisión, límite 1–100 |
| POST | /skip-authorizations | Solicitud del responsable |
| POST | /skip-authorizations/{authorizationRequest}/decision | Decisión ORG_ADMIN |

Cookies de autenticación y X-CSRF-TOKEN en escrituras. Swagger Development muestra estas rutas.

Solicitud:

```json
{
  "impact": "La comprobación pendiente se documenta para evaluación del administrador.",
  "residualRisk": "Riesgo que se conserva al omitir esta acción.",
  "expectedExecutionRevision": 2,
  "expectedIncidentRevision": 5,
  "reason": "Justificación de la omisión solicitada."
}
```

Decisión:

```json
{
  "approve": true,
  "selfApproval": false,
  "selfApprovalJustification": null,
  "expectedExecutionRevision": 3,
  "expectedIncidentRevision": 6,
  "reason": "Revisión administrativa documentada."
}
```

Las revisiones del ejemplo son ilustrativas; utilizar las recibidas en la respuesta real.
La respuesta de escritura es ExecutionItem, con nuevas revisiones de ejecución/incidente.
GET devuelve SkipAuthorizationItem: identidad/ámbito de solicitud, revisiones, actor/correlación,
motivo, impacto, riesgo, contexto de dependencias, excepción propia y fecha.
Las decisiones conservan el impacto/riesgo/contexto originales; cambiar la evaluación exige otra solicitud.

## Persistencia y permisos

Migración 20261026203604_SkipAuthorizations: tabla app.skip_authorizations con 20 columnas,
cuatro FK, tres checks e índices únicos para decisión de request_id y task_id/execution_revision.
El contexto es jsonb acotado a 64 KiB. Los vínculos compuestos impiden asociar otra organización/incidente/versión/tarea.

La aplicación y SQL impiden UPDATE/DELETE/TRUNCATE de autorizaciones. Los triggers diferidos exigen
ámbito y participantes actuales, solicitud/revisiones exactas, estado SKIPPED para aprobar, snapshot de dependencias
correcto y eventos correlacionados. Una tarea no puede pasar a SKIPPED sin una aprobación del mismo cambio.
La protección previa de tareas conserva identidad, transiciones, aprobación de paso y evidencia FILE.
El guard de integridad de ejecución admite omisiones autorizadas al finalizar, sin alterar las dependencias.

Down rechaza pérdida de autorizaciones, tareas omitidas o historial SKIP_AUTHORIZATION; en vacío restaura
las funciones y checks anteriores. No se usa EnsureCreated ni se modifica silenciosamente la base al arrancar.
Runtime recibe SELECT/INSERT en la tabla, sin UPDATE/DELETE. RLS y separación operativa completa siguen pendientes.

## Archivos editados

Nuevos: SkipAuthorization.cs, SkipAuthorizationContracts.cs, IncidentSkipAuthorizationService.cs,
SkipAuthorizationMapping.cs, SkipAuthorizationTests.cs y migración/Designer SkipAuthorizations.

Integraciones: IExecutionService.cs, ExecutionsController.cs, IncidentExecutionService.cs, OpenIrpDbContext.cs,
ExecutionMapping.cs, IncidentMapping.cs, OpenIrpDbContextModelSnapshot.cs, ci/grant-runtime.sql
y el fixture de TRUNCATE en PlaybookDraftPersistenceTests.cs. No se reemplazan los archivos del frontend.
Se preserva la configuración de Swagger del trabajo local anterior.

## Validación y estado

- **38/38 enfocadas** en PostgreSQL 17, incluidas concurrencia, roles, estado, permisos SQL runtime, historial, downgrade protegido, rollback y HTTP/CSRF. Evidencia: artifacts/skip-focused-pg17-5b04e336c13e40a6b8e88fd8d4b0a6e8/paso26-pg17.trx.
- **678/678 de regresión completa en PostgreSQL 17 y 678/678 en PostgreSQL 18.3**, cero fallidas/omitidas. Las 38 enfocadas son parte de las 678, no pruebas adicionales.
- TRX completos: artifacts/skip-full-pg17-f9235989e33e4cef92d0a4899da01781/paso26-pg17.trx y artifacts/skip-full-pg18-48cc773d90034ad6a04cf7430d4a3ba4/paso26.trx. Duraciones: 18m13s y 20m39s respectivamente.
- Release sin errores/advertencias; EF sin diferencias pendientes de modelo.
- Migración 20261026203604_SkipAuthorizations aplicada únicamente a openirp_dev, propietario openirp, PostgreSQL 17.11 en 127.0.0.1:5433, después de respaldo completo restaurado/verificado en otro contenedor sin puertos publicados y cotejo de conteos/historial. **19 migraciones**; datos previos conservados, ocho capacidades/tres categorías y cero autorizaciones nuevas. Sin cuentas ni ejemplos creados en la base habitual.
- Evidencia local, SQL incremental, conteos y esquema: artifacts/skip-authorizations-dev-3a89b0ccac274e988e116758c987e029/. Dump openirp_dev-before-skip-authorizations.dump, SHA256 **B1D3F189E5942FA2AD64EE4151B657A5D855B479C9E0BEB4D290D7F62BBF1330**. Incluye el estado anterior a la migración 19. No había FILE registrados; futuros respaldos con archivos reales requieren también el directorio privado.
- Esquema real verificado: 20 columnas, cuatro FK, cuatro triggers nuevos (dos diferidos), y conservación de controles STEP_APPROVAL/FILE.
- HTTPS posterior a migración: health/OpenAPI/Swagger 200 y **54 operaciones** del conjunto revisado presentes, anónimos 401. Evidencia: artifacts/skip-authorizations-http-82de0b8db74a487ba13ac4dfd44a4d97/results.json. Es un conjunto comprobado, no el total de endpoints de la API; el flujo autenticado de negocio/CSRF se probó en bases temporales.
- MCP reindexado; Graphify AST/HTML actualizado localmente (2438 nodos, 6506 relaciones, 184 comunidades), sin extracción semántica ni llamadas externas de IA.
- Servidores/clústeres propios de pruebas y API temporal detenidos; PostgreSQL/Ollama habituales y frontend preservados. No se hizo commit, push ni merge nuevos; HEAD sigue en 9cec6a075c918ca88bf02fdda6b8ad750c8cf4a3.

## Pendientes posteriores

Alternativas y excepciones específicas de dependencias, suficiencia contextual de capacidades,
continuidad por security_hold_id, cierre integrado con riesgo residual y asistencia IA supervisada.
RESTRICTED, conciliación de staging, revisión de cuarentena, antimalware y política de purga continúan pendientes.
La conservación privada sin purga automática de archivos retirados sigue vigente.
