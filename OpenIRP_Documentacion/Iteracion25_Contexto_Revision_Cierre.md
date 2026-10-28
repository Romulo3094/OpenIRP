# Iteración 25 — Contexto exacto para revisar el cierre

Fecha: 2026-10-27. Workspace D:\Desarrollos\OpenIRP; rama feature/paso26-identidad-organizaciones.

## Decisión del usuario

El 2026-10-27 el usuario confirmó: **todos los cierres exigirán una evaluación vigente de ORG_ADMIN**,
no solo los incidentes con omisiones, excepciones, ejecuciones canceladas o alertas de seguridad.
Esta política queda aprobada para la siguiente subfase. Todavía NO se aplica como requisito de cierre:
esta entrega identifica/comprueba el contexto y preserva compatibilidad con los clientes existentes.
No presentar la huella como evaluación, aprobación o aceptación de riesgo residual.

## Comportamiento implementado

GET `/api/organizations/{organization}/incidents/{incident}/closure-readiness` incorpora
`closureContextToken`, con formato `closure-v1:` seguido de 64 dígitos hexadecimales SHA-256 en minúsculas.
La huella y el informe se calculan en la MISMA instantánea RepeatableRead; no existe una segunda lectura
independiente que mezcle revisiones.

Las tres escrituras de cierre admiten `expectedClosureContext` opcional:

- PUT `/api/organizations/{organization}/incidents/{incident}/state`, exclusivamente para TargetState=Closed.
- POST `/api/organizations/{organization}/incidents/{incident}/closure-requests`.
- PUT `/api/organizations/{organization}/incidents/{incident}/closure-requests/{closureRequest}` (aprobación o rechazo).

Si se envía, el servidor valida el formato y vuelve a calcular la huella dentro de la transacción y del
gate compartido de escritura. Devuelve 400 invalid_closure_context si el formato no es válido y
409 stale_closure_context si cambió el contexto. En otras transiciones, rechaza el campo con
400 unexpected_closure_context. La revisión optimista ExpectedRevision sigue siendo obligatoria.
El rechazo revierte cualquier cambio de incidente/historia de negocio y mantiene la auditoría habitual.

El token validado se conserva como closureContextToken en Details del evento de incidente confirmado;
las transiciones conservan también workflow.from/to. No se agrega contenido privado a la auditoría.
Un token nunca concede permisos: se mantienen cuenta, membresía, organización, responsable y revisión
administrativa por máximo histórico, excepciones de autoaprobación, CSRF y controles de ejecución.

Si el campo se omite o es null se conserva el flujo anterior. Esta compatibilidad NO satisface aún
la política aprobada de evaluación obligatoria. El requisito deberá habilitarse junto con el registro
real de la evaluación, su vigencia y su vínculo auditable al cierre.

## Qué integra la huella v1

Proyecciones de metadatos ordenadas por ID, con secciones fijas, alimentadas por streaming a SHA-256.
No se cargan historiales completos en listas ni hay consultas por cada fila; se realiza una consulta
por sección. JSON escapa los saltos de línea, y los delimitadores identifican filas y secciones vacías.
La versión del protocolo debe cambiar si se modifican las proyecciones o su serialización.

Incluye ID/organización/revisión del incidente; estado y marca de actualización de la organización;
membresías y elegibilidad actual de sus cuentas; categoría/revisión; ejecuciones, tareas y evidencia
de finalización; estados de las versiones utilizadas; avisos de seguridad de esas versiones;
IDs de decisiones de continuidad, aprobaciones de paso, omisiones, excepciones y revocaciones;
evidencias/revisión/estado/ámbito/clasificación y metadatos FILE (estado, revisión, tamaño y SHA-256
registrado); evaluaciones de capacidades del incidente, perfiles de la organización y revisiones
actuales de los catálogos referenciados por esos perfiles/evaluaciones/requisitos ejecutados.
Los IDs de historiales identifican su contenido append-only sin incorporar textos privados a la huella.

Una retirada de evidencia, recuperación FILE, alerta sobre versión ejecutada o cambio de perfil puede
invalidar el token SIN modificar la revisión del incidente. También invalida cambios de membresías de
la misma organización de forma conservadora. Incidentes distintos y datos de otras organizaciones
no forman parte de la huella, salvo fuentes compartidas efectivamente referenciadas.

Las alertas sobre versiones ya ejecutadas se incluyen incluso si la ejecución terminó. Esto exige
refrescar la revisión del cierre; no reescribe decisiones ni convierte la ejecución terminal en activa.
No se incluyen lecturas/auditoría, sesiones de login ni cambios de inventario que no modifican el snapshot
histórico del activo afectado. Los campos sensibles de identidad y el contenido/título de evidencias no
se serializan para construir la huella.

## Límites y continuidad

Es un identificador de concurrencia, no una firma ni credencial. Protege a clientes que envían la
precondición en las operaciones soportadas; no impone esta condición a SQL directo ni a propietarios
privilegiados. El gate coordina escritores de la aplicación. No se agrega RLS ni separación de credenciales.
El cierre que gana una carrera puede confirmarse antes de una retirada posterior; esta conserva las
reglas ya aprobadas de historia y retención, sin reescribir un cierre confirmado.

No se abren archivos ni se verifica su integridad física al generar la huella: FileIntegrityVerified=false.
El hash almacenado no detecta por sí mismo cambios de bytes en disco. ResidualRiskEvaluated=false y
RequiresHumanReview=true permanecen correctos. Una huella no demuestra suficiencia técnica ni cobertura.

Siguiente subfase: registro de evaluación explícita por ORG_ADMIN, aceptación/rechazo, vigencia ante
cambios y requisito obligatorio en TODOS los cierres conforme a la decisión del usuario. Se deberán
modelar las transiciones del propio flujo sin invalidar indebidamente una evaluación solo por solicitar
la aprobación de cierre. Continúan alternativas verificadas, RESTRICTED, operación de archivos e IA.

## Archivos

Nuevo servicio: apps/api/OpenIRP.Infrastructure/Incidents/IncidentClosureContextService.cs.
Pruebas: apps/api/OpenIRP.IntegrationTests/IncidentClosureContextTests.cs.
Integraciones: IncidentWorkflowContracts.cs, IncidentClosureReadinessContracts.cs,
IncidentWorkflowService.cs e IncidentClosureReadinessService.cs; README, bitácora y plan maestro.
Sin migración ni cambios de modelo, permisos SQL o frontend. Los archivos previos de integración y
documentación se respaldaron como before-images locales en artifacts/closure-context/before.

## Verificación final

- .NET SDK 10.0.403, compilación Release sin errores ni advertencias. EF sin cambios pendientes de modelo.
- Selección enfocada inicial: **23/23 en PostgreSQL 17**. [TRX enfocado](../artifacts/closure-context-focused-pg17-4aff12a9793e4717bcb1fa7cf0203a84/paso26-pg17.trx).
- Regresión final relacionada: **169/169 en PostgreSQL 17 y 169/169 en PostgreSQL 18.3**, cero fallidas/omitidas: 23 nuevas incluidas y 146 existentes de preparación/ciclo de vida/cierre, ejecuciones y bloqueos de seguridad. No es la suite global completa de la API.
- [TRX PG17](../artifacts/closure-context-related-pg17-f27581c2e2004d55ab7079ead86c7800/paso26-pg17.trx), [TRX PG18.3](../artifacts/closure-context-related-pg18-6e0579bb13ea4e1387c82bb748208fe1/paso26.trx), [matriz con hashes](../artifacts/closure-context/validation.json).
- Casos nuevos: estabilidad entre lectores, formato, ámbito, ausencia de permiso implícito, escritura con token confirmado en historial; retirada antes de cierre/solicitud/aprobación/rechazo con revisión de incidente intacta; recuperación FILE; alertas posteriores a ejecución terminal; perfiles/catálogos/membresías/categoría; incidentes y organizaciones ajenas; instantánea concurrente y carreras reales con ambos escritores esperando el gate en pg_locks. Las pruebas existentes incluyen lecturas bajo cuenta SQL limitada y revalidación de cuentas/roles.
- HTTPS Development: health/OpenAPI/Swagger 200, GET anónimo 401; contrato de respuesta incluye closureContextToken y los tres contratos de escritura exponen expectedClosureContext opcional. HTTP autenticado y CSRF se prueban en bases temporales. Health es estático. [Resultados](../artifacts/closure-context-http-7ec630e3294940e7931d03f78ff1e6c6/results.json), [OpenAPI](../artifacts/closure-context-http-7ec630e3294940e7931d03f78ff1e6c6/openapi.json).
- openirp_dev en 127.0.0.1:5433 conserva todos los conteos de tablas app/auth/audit y los 22 IDs de migración. Ocho capacidades, tres categorías, sin usuarios ni incidentes. No se ejecutaron migraciones ni operaciones de negocio en ella. No se generó respaldo nuevo para esta entrega sin escrituras; el respaldo restaurado de Iteración 23 sigue documentado. [Antes](../artifacts/closure-context/dev-before.json), [después](../artifacts/closure-context/dev-after.json), [comparación](../artifacts/closure-context/dev-unchanged.txt).
- Grafos actualizados: MCP 3486 nodos/10504 relaciones; Graphify AST/HTML 2990 nodos/8359 relaciones/218 comunidades, sin IA externa.
- Procesos propios temporales de pruebas y API detenidos; servicios habituales preservados. Sin commit, push, merge ni cambios remotos. HEAD se conserva en 9cec6a075c918ca88bf02fdda6b8ad750c8cf4a3.
