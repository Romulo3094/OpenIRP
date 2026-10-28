# Iteración 24 — Preparación para el cierre del incidente

Fecha: 2026-10-27. Desarrollo local sobre feature/paso26-identidad-organizaciones.

## Alcance entregado

GET `/api/organizations/{organization}/incidents/{incident}/closure-readiness` devuelve
`IncidentClosureReadiness`, una consulta informativa del estado persistido y de los controles actuales de cierre.
No ejecuta decisiones, no crea solicitudes y no modifica incidentes, tareas, archivos ni auditoría de éxito.
Los accesos rechazados conservan la auditoría habitual. Usa cookie autenticada, ámbito organizacional,
cuenta/membresía/organización actuales y `Cache-Control: no-store`; GET no exige CSRF.
PLATFORM_ADMIN requiere membresía local para leer; Viewer puede consultar pero no adquiere permisos de cierre.

## Contrato

- Identifica incidente, organización, revisión y estado de la instantánea.
- Clasificación completa reutiliza el mismo predicado del cierre existente: CIA, contexto, criticidad,
  severidad, máximo histórico y versión de regla v1 completos. Desconocidos siguen pendientes.
- Expone existencia de resolución y elegibilidad actual del responsable asignado.
- `Blockers` enumera impedimentos comunes: incidente no resuelto, resolución ausente,
  clasificación pendiente, responsable no elegible, ejecución activa o revisión agotada.
  La lista no incluye los permisos particulares del lector ni es una evaluación de riesgo residual.
- `CanCloseDirectly`, `CanRequestClosure`, `CanApproveClosure` proyectan la posibilidad de realizar
  esas acciones por el lector según los controles actuales. Las escrituras siempre revalidan
  permisos y revisiones; una lectura no reserva autorización. Una aprobación propia todavía exige
  declaración y justificación explícitas (`SelfApprovalDeclarationRequired`).
- La revisión pendiente solo es actual si corresponde a la revisión vigente del incidente resuelto.
  Una revisión invalidada/rechazada permanece histórica. HIGH/CRITICAL en el máximo histórico exige
  revisión administrativa aunque la severidad actual haya bajado.
- Agrupa TODAS las ejecuciones, tareas y evidencias del incidente por estado, ordenadas por estado.
  Son conteos SQL, sin truncar listas ni consultar cada tarea por separado. Tareas pendientes de una
  ejecución cancelada siguen visibles; no se presentan como una ejecución activa.
- `ActiveUnavailableFiles` cuenta FILE activos sin metadatos AVAILABLE. No abre archivos ni calcula SHA256.
- `HistoricalDependencyApprovals` y `DependencyRevocations` son conteos históricos separados:
  una aprobación registrada no equivale a autorización vigente. La vigencia por arista sigue en
  dependency-readiness; esta consulta no resta conteos para inventar una autorización efectiva.
- Identifica la ejecución activa y su revisión, el último security_hold_id de su versión y continuidad
  para ese ID exacto. Un aviso nuevo invalida la continuidad anterior. Incluso autorizada, la ejecución
  activa impide resolver/cerrar según los controles existentes.

## Consistencia y límites

La lectura usa la transacción RepeatableRead existente de consultas de ejecución. Autorización,
incidente y agregados comparten instantánea, incluso ante una cancelación concurrente. Los cambios
posteriores se reflejan al consultar de nuevo. La elegibilidad temporal se evalúa al leer.

`RequiresHumanReview=true`, `ResidualRiskEvaluated=false`, `FileIntegrityVerified=false`.
No exige tener una ejecución ni evidencia para el cierre manual, pues esa regla no existe hoy.
Tampoco considera el conteo cero de evidencias o excepciones como garantía de seguridad.
No certifica suficiencia técnica, cobertura operacional ni aceptación de riesgos, y no sustituye el
análisis humano del contenido. Los conteos no devuelven contenido de evidencia ni rutas privadas.
El bloqueo de seguridad mostrado corresponde a la ejecución ACTIVA; para decisiones históricas o
avisos posteriores a una ejecución terminal se deben consultar sus endpoints específicos.

Sigue pendiente la política integrada de riesgos residuales para el cierre, alternativas verificadas,
propagación a clones, permisos RESTRICTED, retención/purga y operación de archivos, e IA supervisada.

## Archivos de esta iteración

Nuevos:

- apps/api/OpenIRP.Application/Incidents/IncidentClosureReadinessContracts.cs
- apps/api/OpenIRP.Infrastructure/Incidents/IncidentClosureReadinessService.cs
- apps/api/OpenIRP.IntegrationTests/IncidentClosureReadinessTests.cs
- OpenIRP_Documentacion/Iteracion24_Preparacion_Cierre.md

Integraciones:

- apps/api/OpenIRP.Application/Incidents/IIncidentService.cs
- apps/api/OpenIRP.Api/Controllers/IncidentWorkflowController.cs
- apps/api/OpenIRP.Infrastructure/Incidents/IncidentWorkflowService.cs (extrae el predicado compartido sin cambiar la regla)
- README.md, planificación integral y bitácora.

Sin cambios de modelo, migraciones nuevas, permisos SQL nuevos ni modificaciones al frontend.
Se conserva el trabajo local previo. No se hace commit, push ni merge.

## Verificación final

- Release .NET SDK 10.0.403: cero errores y advertencias. EF no detecta diferencias pendientes de modelo.
- **146/146 en PostgreSQL 17 y 146/146 en PostgreSQL 18.3**, sin fallidas/omitidas: **24 casos nuevos** y 122 existentes de ciclo de vida, ejecuciones y bloqueos de seguridad. Las 24 están incluidas en las 146. No se ejecutó la suite global completa de la API.
- Cubre clasificación pendiente, máximo histórico, cierre directo/revisión, autoaprobación, invalidación, responsables y cuentas actuales, roles/aislamiento, agregados de tareas/evidencias, FILE fallidos, historia/revocación de excepciones, bloqueo vigente, runtime SQL limitado, HTTP/no-store y cancelación concurrente con instantánea consistente.
- Primera selección: 126/146 por motor, con 20 fallos por ordenar después de construir el DTO de agregación. Se movió el orden antes de la proyección SQL y se repitieron las 146 completas de cada motor. Los TRX iniciales permanecen en artifacts/closure-related-pg17-5503a8895167446e895cbe375602aa4e y artifacts/closure-related-pg18-5b7111f589fe4486bfafb1acfd84655d.
- [TRX PG17](../artifacts/closure-related-pg17-ecee0469546a46309dbb9b365df4aae7/paso26-pg17.trx), [TRX PG18.3](../artifacts/closure-related-pg18-6db491e5a5c746cf9f83c30a1b297b46/paso26.trx), [matriz con hashes](../artifacts/closure-readiness/validation.json).
- HTTPS Development: health/OpenAPI/Swagger 200; nuevo GET devuelve 401 anónimo y tiene contrato IncidentClosureReadiness tipado. HTTP autenticado y no-store probados en bases temporales. Health es estático; no acredita conectividad a servicios. [Resultados](../artifacts/closure-http-969b607caf2b40718dfc3ef38eaeb985/results.json), [OpenAPI](../artifacts/closure-http-969b607caf2b40718dfc3ef38eaeb985/openapi.json).
- Base habitual openirp_dev en 127.0.0.1:5433: mismos conteos de tablas app/auth y mismos 22 IDs de migración antes/después. Ocho capacidades, tres categorías, sin usuarios ni incidentes. No se aplicaron migraciones ni operaciones de negocio. [Comparación](../artifacts/closure-readiness/dev-unchanged.txt), [antes](../artifacts/closure-readiness/dev-before.json), [después](../artifacts/closure-readiness/dev-after.json). No fue necesario un respaldo nuevo: esta iteración no escribe datos ni cambia esquema en la base habitual; el respaldo restaurado de Iteración 23 sigue documentado.

Grafos actualizados: MCP 3449 nodos/10287 relaciones; Graphify AST/HTML 2947 nodos/8195 relaciones/221 comunidades, sin IA externa. Procesos propios temporales cerrados; Docker PostgreSQL/Ollama habituales preservados.
