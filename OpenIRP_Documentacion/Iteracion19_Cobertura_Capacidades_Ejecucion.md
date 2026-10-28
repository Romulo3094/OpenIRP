# Iteración 19 — Cobertura declarada de capacidades por ejecución/tarea

Fecha: 2026-10-26. Rama feature/paso26-identidad-organizaciones. Se preservan Swagger, Iteración 18 y frontend pendientes de commit.

## Alcance

Dos consultas comparan los requisitos inmutables de la versión ligada a una ejecución con las capacidades efectivas
actuales del incidente. La consulta de ejecución incluye todos los requisitos; la de tarea incluye los de versión
y los del paso exacto, conservando ambas filas si una misma capacidad aparece en ambos ámbitos.

No es una autorización para ejecutar ni una validación técnica de suficiencia. RequiresHumanReview siempre es true.
No inicia/omite/completa tareas, no selecciona evidencia, no autoriza dependencias y no cambia severidad.
Esta evaluación sirve de base para decisiones humanas y futuras alternativas; estas últimas siguen pendientes.

## Reglas y procedencia

Se reutiliza la precedencia vigente de Iteración 5:

1. Catálogo inactivo: estado efectivo Unknown y origen InactiveCatalog.
2. Override de incidente no nulo: prevalece, incluido Unknown explícito.
3. Sin override: perfil organizacional actual.
4. Sin perfil: Unknown/Undeclared, revisión organizacional cero, sin insertar filas.

Por cada referencia:

| Coverage | Significado |
|---|---|
| DeclaredAvailable | Catálogo activo y declaración efectiva Available |
| Unavailable | Catálogo activo y declaración efectiva Unavailable |
| Unknown | Catálogo activo con disponibilidad no conocida |
| InactiveCatalog | Referencia inactiva, independientemente de la declaración conservada |

El resumen RequiredCoverage solo agrega los requisitos REQUIRED:

- NoRequiredCapabilities: no hay requisitos obligatorios en el ámbito consultado.
- Gaps: al menos uno Unavailable o InactiveCatalog; se conservan además los contadores de Unknown.
- Unknown: sin brechas conocidas, pero falta conocer al menos uno.
- DeclaredAvailable: todos los REQUIRED tienen declaración Available en catálogo activo.

RECOMMENDED conserva sus referencias y contadores de brechas/desconocidos; no se convierte en REQUIRED ni modifica
el resumen obligatorio. Un ámbito sin requisitos tampoco implica permiso, ausencia de riesgos o aptitud técnica.

Las notas organizacionales y limitaciones específicas se devuelven sin interpretarlas como prueba de suficiencia.
HasNotesOrLimitations indica presencia de texto; no analiza su significado. Una declaración Available puede
tener limitaciones que necesitan revisión humana.

## Coherencia y vigencia

Toda la consulta, incluida autorización, revisión de incidente/ejecución, catálogo, perfil y overrides, usa el
snapshot RepeatableRead existente de ExecutionRead. Los datos se cargan en lotes, sin consultas por cada requisito.
Se conserva el límite publicado de 100 pasos y 300 requisitos; no se pagina ni trunca silenciosamente ese conjunto.

Las referencias siguen ligadas a VersionId original. La consulta sigue disponible sobre versiones ARCHIVED,
ejecuciones CANCELLED/COMPLETED y tareas SKIPPED, devolviendo esos estados explícitamente.
Las capacidades y nombres del catálogo son actuales para el snapshot de lectura: no representan qué había
disponible cuando ocurrió una tarea histórica y no reescriben eventos ni decisiones.

El perfil y catálogo pueden cambiar sin incrementar la revisión del incidente. Cada referencia incluye
CatalogRevision, OrganizationCapabilityRevision, AssessmentId y AssessedAtRevision. Una futura escritura
deberá revalidar su contexto: el resultado de este GET no es una aprobación reutilizable.

## API y permisos

Base: /api/organizations/{organization}/incidents/{incident}/executions/{execution}

| Método | Ruta | Ámbito |
|---|---|---|
| GET | /capability-readiness | Requisitos de toda la versión |
| GET | /tasks/{task}/capability-readiness | Requisitos de versión + paso exacto |

Exige sesión, cuenta no bloqueada y organización/membresía activas. ORG_ADMIN/ANALYST/VIEWER pueden consultar.
PLATFORM_ADMIN no tiene acceso organizacional implícito. No se mezclan perfiles ni overrides de otras organizaciones.
Objetos fuera del ámbito autorizado no se resuelven; respuesta no-store. GET no exige CSRF.

ExecutionCapabilityReadiness devuelve IDs/estados/revisiones de ejecución e incidente, TaskId/TaskStatus si corresponde,
RequiredCoverage, contadores, RequiresHumanReview y Requirements.
Cada ExecutionCapabilityRequirement identifica referencia/paso/tarea, Level, Coverage y el IncidentCapabilityItem
completo con su procedencia, revisiones y notas/limitaciones.

Disponible en Swagger Development. Se alinean las opciones HTTP JSON con el convertidor de enums MVC existente
para describir enums textuales en OpenAPI. Referencia:
[metadatos y serialización JSON de ASP.NET Core](https://learn.microsoft.com/aspnet/core/fundamentals/openapi/include-metadata?view=aspnetcore-10.0).
El formato de respuestas de los controladores y sus reglas de autorización se conservan.

## Archivos

Nuevos:
- Domain/Playbooks/CapabilityRequirementEvaluation.cs.
- Application/Executions/CapabilityReadinessContracts.cs.
- Infrastructure/Incidents/IncidentCapabilityReadinessService.cs.
- IntegrationTests/CapabilityReadinessTests.cs y CapabilityRequirementEvaluationTests.cs.

Integraciones: IExecutionService.cs, ExecutionsController.cs y Program.cs (esquemas JSON).
ExecutionTests.cs admite personalizar contenido del fixture mediante parámetro opcional; sus valores previos se conservan.
Documentación: esta guía, planificación maestra, README y bitácora.

No cambia DbContext, mapeos, migraciones ni permisos SQL. La base habitual continúa en las 19 migraciones verificadas
de la Iteración 18. No se ejecuta una migración ni se crean ejemplos/cuentas en la base habitual.

## Validación

- Release final: cero errores/advertencias; EF sin diferencias pendientes de modelo.
- Primera comprobación: 37/37 nuevas en PostgreSQL 17. Después se añadieron tres casos de aislamiento/límite/runtime.
- Selección final de módulos relacionados: **182/182 en PostgreSQL 17 y 182/182 en PostgreSQL 18.3**, cero fallidas/omitidas, incluyendo **40 casos nuevos** (33 integrados y siete del evaluador). Duraciones 7m23s y 7m25s. Cubre cobertura, capacidades de incidente, ejecuciones, STEP_APPROVAL y SKIP_AUTHORIZATION.
- TRX: artifacts/readiness-relevant-pg17-f89282ee88b24bf38c387f06161512ea/paso26-pg17.trx y artifacts/readiness-relevant-pg18-81e1b877e37e427f962187f3dbba0981/paso26.trx. Las 40 nuevas forman parte de las 182; no se suman.
- No se repitió la regresión global de 678 casos de Iteración 18: esos resultados siguen documentados como históricos. Esta entrega usa la selección descrita, más las verificaciones HTTPS del ajuste final de esquemas JSON.
- HTTPS final: health/OpenAPI/Swagger 200; **56 operaciones** del conjunto revisado con anónimos 401. Ambos GET nuevos figuran en OpenAPI y su respuesta es tipada. Enums de cobertura, nivel, disponibilidad y origen contienen valores textuales válidos. Evidencia: artifacts/readiness-http-baed39f398034661942180f29e3ef412/results.json, openapi.json y coverage-enum-schema.json.
- Development: Swagger/recursos/OpenAPI 200; Production: las mismas rutas 404. Login sin CSRF 400 y organizaciones sin sesión 401 en ambos entornos. Evidencia: artifacts/readiness-swagger-env-b7785337475348d681bbe8b29b99fba2/results.json. Instancias temporales detenidas.
- El esquema JSON final se comprueba conforme a los valores enum generados (OpenAPI puede omitir type cuando enum ya restringe a cadenas); se conserva la serialización MVC previa.
- Conteos e IDs de las **19 migraciones locales anteriores idénticos** antes/después: artifacts/readiness-dev-checks-1bfa35949e7146e48e9cdcc95f0d35ed/before-counts.json, after-counts.json y unchanged.txt. No se aplicó ninguna migración, se cargaron ejemplos ni se crearon cuentas en openirp_dev.
- MCP reindexado (2892 nodos/8320 relaciones); Graphify AST/HTML actualizado localmente (2530 nodos/6751 relaciones/187 comunidades), sin llamadas externas de IA.
- Servidores/clústeres de prueba y API temporal detenidos; Docker PostgreSQL/Ollama y frontend habituales preservados. Sin commit, push, merge ni cambios remotos nuevos; HEAD permanece 9cec6a075c918ca88bf02fdda6b8ad750c8cf4a3.

## Pendiente

Alternativas/excepciones de dependencias con revisión humana y contexto actualizado, continuidad por security_hold_id,
cierre ampliado con riesgo residual, RESTRICTED, controles operativos de archivos e IA supervisada.
La cobertura declarada no sustituye esos módulos.
