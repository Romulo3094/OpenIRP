# Iteración 27 — Soporte de evidencias por ejecución

Fecha: 2026-10-27. Desarrollo local sobre feature/paso26-identidad-organizaciones.

## Objetivo y contrato

GET `/api/organizations/{organization}/incidents/{incident}/executions/{execution}/evidence-readiness` permite revisar qué soporte conserva cada tarea en el momento de la consulta. Devuelve ExecutionEvidenceReadiness con tareas ordenadas por posición, código e ID; estado y revisiones del incidente y ejecución, versión y huella RiskContextToken. No modifica decisiones ni constituye una autorización de cierre.

El detalle TaskEvidenceSupport incluye TaskId, StepId, StepCode, Position, TaskStatus, IsRequired, RequiresEvidence, CompletionEvidenceId, tipo/revisión/estado de la evidencia y revisión/estado del archivo cuando corresponde. La consulta examina exclusivamente la referencia seleccionada al completar esa tarea exacta: las evidencias adjuntas sin seleccionar y las evidencias generales del incidente no sustituyen ese vínculo.

| SupportState | Significado |
|---|---|
| NotRequired | Sin referencia seleccionada y sin RequiresEvidence. No certifica la tarea ni las evidencias adjuntas. |
| AwaitingCompletion | RequiresEvidence y sin referencia, con tarea aún pendiente o en curso. Es esperable durante el trabajo. |
| SkippedNeedsReview | RequiresEvidence y tarea omitida sin referencia. La omisión requiere revisión; no equivale a completar. |
| MissingCompletionReference | Tarea completada que exige evidencia, pero carece de referencia seleccionada. |
| InvalidCompletionReference | Referencia incompatible con tarea/ejecución/versión/ámbito, estado de tarea o metadatos válidos. No toma prestados datos de otra evidencia. |
| Withdrawn | Evidencia seleccionada retirada. Conserva la tarea completada y su referencia histórica. |
| FileUnavailable | FILE activa cuyo registro de archivo no existe o no indica AVAILABLE. |
| MetadataAvailable | Evidencia seleccionada activa y coherente; si es FILE, su registro indica AVAILABLE. No comprueba bytes ni pertinencia técnica. |

RequiresHumanReview siempre es true. FileIntegrityVerified y TechnicalSufficiencyVerified siempre son false. Una alteración física sin cambio en los metadatos no se detecta aquí: la descarga/verificación de archivos conserva su flujo existente. Disponibilidad de metadatos, integridad física y suficiencia técnica son comprobaciones diferentes.

## Conteos y alcance

- EvidenceRequiredTaskCount cuenta RequiresEvidence, independientemente de IsRequired; ambos flags conservan su significado distinto.
- RequiredMetadataAvailableCount cuenta esas tareas con MetadataAvailable.
- CompletedRequiredEvidenceGapCount cuenta las completadas que exigen evidencia y no tienen soporte MetadataAvailable.
- CurrentReferencedEvidenceGapCount incluye toda referencia seleccionada sin soporte MetadataAvailable, incluso en tareas sin RequiresEvidence.
- SkippedRequiringEvidenceCount y AwaitingRequiredCompletionCount separan omisiones y trabajo pendiente.
- RequiredCoverage es NotRequired si no hay RequiresEvidence, MetadataAvailable si todas esas tareas tienen metadatos disponibles y NeedsReview en los demás casos. NotRequired puede coexistir con una referencia opcional retirada; no es un certificado.

Se admiten ejecuciones finalizadas/canceladas e incidentes cerrados para consulta histórica. El resultado describe el soporte **actual** de sus referencias, no reconstruye los metadatos existentes al completar la tarea. No reabre ni reescribe tareas, ejecuciones, evidencias, omisiones, aprobaciones o cierres. Tampoco modifica DAG, excepciones, vigencia de fuentes ni aceptación de riesgo.

## Consistencia, privacidad y autorización

Se reutiliza ExecutionRead con transacción RepeatableRead, permisos actuales de organización y controles de cuenta, bloqueo y suspensión. La misma instantánea cubre tareas, metadatos y RiskContextToken, que corresponde al ContextToken de RiskReview en closure-readiness. La huella no otorga permisos ni equivale a aceptación; closureContextToken tiene su contrato independiente.

El lote usa joins acotados por organización, incidente, ejecución y versión; el vínculo de evidencia exige tanto su ID como la tarea exacta. No hay consultas de evidencia por cada tarea. Se exigen entre una y cien tareas y se comprueba que cada una corresponda a un paso de la versión; inconsistencias producen 409 en vez de truncar silenciosamente. Recursos fuera del ámbito solicitado devuelven 404, sujetos primero a autorización.

El lote proyecta solo metadatos necesarios. No devuelve contenido, título, nombre original de archivo, ruta/storage ID ni SHA-256; tampoco abre archivos. El endpoint hereda autenticación y Cache-Control no-store; GET no exige token CSRF. PLATFORM_ADMIN sin membresía no obtiene acceso implícito. No añade roles, permisos SQL ni registro público. Las lecturas exitosas conservan la política de auditoría existente; los rechazos siguen el flujo común.

## Archivos de implementación

- apps/api/OpenIRP.Application/Executions/EvidenceReadinessContracts.cs: estados, detalle y resumen.
- apps/api/OpenIRP.Application/Executions/IExecutionService.cs: contrato de servicio.
- apps/api/OpenIRP.Infrastructure/Incidents/IncidentEvidenceReadinessService.cs: lectura consistente y clasificación.
- apps/api/OpenIRP.Api/Controllers/ExecutionsController.cs: GET tipado.
- apps/api/OpenIRP.IntegrationTests/EvidenceReadinessTests.cs: 31 casos nuevos.
- README.md, esta guía, planificación integral y bitácora: alcance y resultados.

## Límites y continuación

No hay migraciones nuevas ni cambios de escritura. La evaluación y aceptación ORG_ADMIN de la Iteración 26 sigue siendo obligatoria en todos los cierres, con aprobación adicional por máximo histórico HIGH/CRITICAL.

Quedan pendientes la suficiencia técnica verificada, alternativas operativas verificadas, propagación de alertas a derivadas, RESTRICTED, políticas operativas de archivos, IA supervisada, RLS/separación operativa de credenciales y validación integral del MVP. Este informe facilita la revisión humana y no completa esos módulos.

## Verificación y evidencias

- Compilación Release con .NET SDK 10.0.403: cero errores y advertencias. EF sin diferencias de modelo pendientes en ambos motores.
- Enfocadas: **31/31 en PostgreSQL 17**, sin fallidas/omitidas. [TRX](../artifacts/evidence-readiness-focused-pg17-d944200628334d708398458d5ca707cb/paso26-pg17.trx).
- Regresión relacionada final: **187/187 en PostgreSQL 17.11 y 187/187 en PostgreSQL 18.3**, sin fallidas/omitidas. Incluye las mismas **31 pruebas nuevas**; no se suman a 187. Filtro: Feature=EvidenceReadiness, Evidence, EvidenceFiles, ClosureRisk y Executions. No se ejecutó de nuevo la suite global completa en esta iteración.
- [TRX PG17](../artifacts/evidence-readiness-related-pg17-5a7b15e314974346ac0fe524b66a88c9/paso26-pg17.trx), [TRX PG18.3](../artifacts/evidence-readiness-related-pg18-b537067f85b44f6d8a881ac4b3e696e2/paso26.trx), [matriz, alcance y hashes](../artifacts/evidence-readiness/validation.json).
- Casos: referencias exactas TEXT/REFERENCE/FILE; evidencia sin seleccionar; retirada y cuarentena; archivo alterado/ausente sin lectura física en el informe; tareas omitidas y completadas; lectura de incidentes cerrados; ámbitos de organización/incidente/ejecución; permisos y cuenta limitada; HTTP autenticado/no-store/CSRF; cien tareas completas y ordenadas; lote único sin contenido privado; carrera real entre lectura y retirada con instantánea coherente y cambio posterior de huella.
- Cuatro escenarios de referencias históricas inválidas (ausente, general del incidente, otra tarea y otra ejecución) se preparan deshabilitando temporalmente triggers USER **solo en bases temporales propias de pruebas**, dentro de una transacción que los reactiva. Las FK se conservan. No se relajan guards ni permisos de producción.
- Base habitual openirp_dev en 127.0.0.1:5433: sin migraciones ni escrituras de aplicación. Comparación exacta de IDs de las 23 migraciones, conteos de todas las tablas app/auth/audit y definiciones de funciones app/auth. Ocho capacidades, tres categorías y tablas de identidad/negocio vacías como al inicio. [Antes](../artifacts/evidence-readiness/dev-before.json), [después](../artifacts/evidence-readiness/dev-after.json), [comprobación](../artifacts/evidence-readiness/dev-unchanged.txt). El respaldo restaurado de Iteración 26 sigue documentado en su guía; esta consulta no exige nueva migración.
- HTTPS local: health/OpenAPI/Swagger 200, nuevo GET 401 anónimo; contrato raíz, detalle y enums string presentes en OpenAPI. Health es estático; los flujos autenticados se comprobaron en bases temporales. No representa un smoke de todos los endpoints. [Resultados](../artifacts/evidence-readiness-http-99b462422a464c3e987e26137517e519/results.json), [OpenAPI](../artifacts/evidence-readiness-http-99b462422a464c3e987e26137517e519/openapi.json).
- MCP actualizado: 3684 nodos/11626 relaciones. Graphify AST/HTML: 3134 nodos/8895 relaciones/223 comunidades, sin IA externa.
- Procesos temporales propios detenidos; servicios habituales preservados. Sin commit, push, merge ni modificaciones remotas; HEAD permanece 9cec6a075c918ca88bf02fdda6b8ad750c8cf4a3. Se preservan frontend y cambios locales previos.
