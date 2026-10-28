# Iteración 26 — Evaluación y aceptación de riesgo para todos los cierres

Fecha: 2026-10-27. Workspace D:\Desarrollos\OpenIRP.
Rama feature/paso26-identidad-organizaciones. Desarrollo local; sin cambios remotos.

## Política implementada

El usuario confirmó que TODOS los cierres, incluso LOW/MEDIUM y sin ejecución guiada, requieren
una evaluación vigente de ORG_ADMIN. Esta entrega exige además aceptación explícita del riesgo
residual declarado por esa persona. Un rechazo es una evaluación registrada, pero impide cerrar.
No hay registro público, cuentas de demostración ni permisos organizacionales implícitos de PLATFORM_ADMIN.

Se conservan los controles anteriores: clasificación completa, resolución, responsable asignado y
actualmente elegible; ausencia de ejecución activa; máximo histórico HIGH/CRITICAL requiere solicitud
y decisión administrativa de cierre. La evaluación de riesgo NO reemplaza esa decisión, STEP_APPROVAL,
omisiones, excepciones individuales ni continuidad por security_hold_id.

## Operaciones y contratos

- POST `/api/organizations/{organization}/incidents/{incident}/closure-risk-reviews` devuelve ClosureRiskReviewItem.
- GET misma ruta devuelve historial ordenado por IncidentRevision, con afterRevision>=0 y limit entre 1 y 100.
- GET closure-readiness incorpora RiskReview (ClosureRiskState), además de la huella y las propiedades existentes.

Registrar exige cuenta/membresía/organización activas, rol ORG_ADMIN local, incidente RESOLVED y
clasificación/resolución completas. Una ejecución activa sigue impidiendo ese estado y la escritura.
La lectura hereda permisos de consulta del incidente, incluido VIEWER, y no otorga facultades de cierre.
Todas las rutas están autenticadas y no-store; la escritura conserva cookie y CSRF.

ReviewClosureRisk exige:

- acceptResidualRisk: booleano explícito, true o false.
- assessment: evaluación humana no vacía, hasta 4000 caracteres; debe describir riesgos, excepciones,
  incertidumbres y mitigaciones consideradas. No se genera una conclusión automática del contenido.
- reason: motivo no vacío, hasta 2000 caracteres.
- selfReview: booleano explícito; true si el revisor es el responsable asignado al incidente.
- selfReviewJustification: obligatoria y no vacía para revisión propia, hasta 2000 caracteres; null en caso contrario.
- expectedIncidentRevision: revisión optimista obligatoria.
- expectedClosureContext: huella closure-v1 vigente obtenida de closure-readiness, obligatoria en este registro.

La declaración de revisión propia es independiente de la excepción de autoaprobación del cierre:
si la misma persona solicita y aprueba un cierre HIGH/CRITICAL, debe declarar y justificar ambas
operaciones por sus contratos separados. No se hereda una excepción de la otra.

El registro incrementa la revisión del incidente y confirma evaluación, evento de incidente y auditoría
juntos. Devuelve su ID, revisión, actor, correlación, ContextToken, decisión, evaluación, motivo,
declaración propia y fecha. Un fallo revierte todos los cambios de negocio. El rechazo de acceso
conserva el mecanismo de auditoría existente, sin prometer una cola durable.

## Dos huellas con propósitos distintos

closureContextToken (closure-v1 de Iteración 25) identifica la instantánea completa y su revisión.
Su comprobación sigue opcional en las tres escrituras de cierre por compatibilidad, pero omitirla
NO evita el requisito nuevo de aceptación vigente. En el POST de evaluación sí es obligatoria.

RiskReview.ContextToken (risk-v1) se calcula en PostgreSQL a partir del contexto de riesgo.
La misma función se utiliza para validar desde la API y desde los guards SQL. Canoniza JSONB y
fechas UTC y calcula SHA-256 nativo, sin extensiones ni acceso al sistema de archivos.

Incluye datos semánticos del incidente, última revisión de cambios relevantes, organización,
membresías y elegibilidad de cuentas, categoría, ejecuciones y tareas, versiones utilizadas y sus
avisos de seguridad, decisiones, aprobaciones, omisiones, excepciones/revocaciones, evidencias,
metadatos FILE, capacidades del incidente, perfiles y catálogos referenciados. Los historiales
append-only se identifican por IDs; las proyecciones mutables incluyen revisiones y estados.
Cada conjunto se agrega ordenadamente en SQL, con ámbitos explícitos, sin consulta por fila.

Excluye el número total de revisión y los campos propios de solicitud/decisión de cierre, así como
sus eventos y el evento de evaluación. Solicitar el cierre NO invalida por sí solo la evaluación.
Se mantiene el mismo riesgo al aprobar o rechazar una solicitud si no cambian los datos relevantes.
La transición a CLOSED conserva el vínculo; una reapertura cambia la revisión semántica y exige
una evaluación nueva antes de volver a cerrar.

La vigencia se recalcula contra el contexto actual; no modifica el registro histórico. Cambios de
membresías/perfiles de la propia organización invalidan conservadoramente su coincidencia. Los
cambios de evidencias, recuperación FILE y alertas pueden invalidarla SIN incrementar la revisión
del incidente. Se consideran alertas de versiones utilizadas aun después de terminar su ejecución.
No hay vencimiento por una duración fija ni propagación automática a clones ajenos.

## Estado y decisiones

- LatestReview es la última evaluación por revisión de incidente, incluso si fue rechazada o ya no coincide.
- IsCurrent exige incidente RESOLVED, contexto coincidente y revisor actualmente elegible como ORG_ADMIN.
- AcceptanceCurrent exige IsCurrent y AcceptResidualRisk=true.
- ResidualRiskEvaluated refleja IsCurrent: puede ser true con una evaluación rechazada, sin permitir cierre.
- RequiresHumanReview sigue true; FileIntegrityVerified sigue false.

La última evaluación reemplaza a las anteriores para decidir vigencia. No se busca una aceptación
más antigua para eludir un rechazo posterior. Una nueva aceptación crea otro ID y conserva todo el
historial. Una evaluación nueva invalida una solicitud de cierre pendiente y obliga a solicitar otra;
no altera una decisión de cierre ya confirmada, porque no se permite evaluar incidentes terminales.

Cierre directo, solicitud de cierre y aprobación comprueban aceptación vigente dentro del gate
transaccional PostgreSQL. Si falta, devuelven 409 current_closure_risk_acceptance_required y conservan
el estado. Rechazar una solicitud de cierre no exige aceptar riesgo para poder expresar el rechazo.
El evento confirmado de solicitud/cierre registra riskReviewId exacto. La auditoría de éxito conserva
metadatos/correlación, sin copiar el texto de la evaluación.

## Secuencia de uso

1. Terminar/cancelar la ejecución activa, completar clasificación, asignar responsable y resolver.
2. Consultar closure-readiness y revisar tareas, evidencia, omisiones, excepciones y contexto disponible.
3. ORG_ADMIN registra evaluación y aceptación/rechazo con revisión y closureContextToken actuales.
4. Refrescar closure-readiness: el registro incrementó la revisión total y su huella closure-v1.
5. Con aceptación vigente, el responsable cierra LOW/MEDIUM o solicita revisión HIGH/CRITICAL.
6. En HIGH/CRITICAL, ORG_ADMIN decide el cierre por el flujo existente; la aceptación de riesgo permanece
   vigente durante esa solicitud si no cambió el contexto. Declarar autoaprobación cuando corresponda.
7. Ante cambios relevantes, volver a evaluar; un token antiguo o una aceptación caducada no autorizan avanzar.

## Persistencia y defensa SQL

ClosureRiskReview tiene 13 columnas, FK al incidente con organización y al actor, índice único
(OrganizationId, IncidentId, IncidentRevision) e índice de actor. El historial es append-only en EF,
UPDATE/DELETE/TRUNCATE SQL. La cuenta runtime obtiene SELECT/INSERT, sin UPDATE/DELETE adicionales.

La migración ClosureRiskReviews amplía la lista de acciones de eventos y agrega seis funciones y
cinco triggers (dos diferidos). Comprueba contexto, ORG_ADMIN actual, declaración propia, evento y
auditoría correlacionados. Solicitar/cerrar exige aceptación vigente y evento con el mismo riskReviewId.
No se reemplazan funciones anteriores de tareas, STEP_APPROVAL, FILE, omisiones o dependencias.
El downgrade se permite sin esa historia y se rechaza si hay evaluaciones o vínculos de riesgo grabados.

Los cierres anteriores a la migración se conservan como historia, sin inventarles evaluaciones.
Reabrirlos y volver a cerrar exige la política nueva. No hay backfill de aceptaciones.

## Límites

Es una evaluación humana con control de vigencia, no un motor que certifique suficiencia técnica,
corrección de mitigaciones o ausencia de riesgo. Permite aceptar explícitamente incertidumbres;
no transforma capacidades desconocidas en disponibles, omisiones en completadas ni evidencia retirada en activa.
No ejecuta infraestructura externa ni usa IA para aprobar en nombre de una persona.

Las huellas son identificadores de coincidencia, no firmas ni credenciales. El registro conserva
evaluación, contexto, revisión y referencias auditables; no duplica todos los textos ni bytes revisados,
y no promete reconstrucción completa de cada contenido anterior si su módulo no conserva esa versión.
No se abren archivos para evaluar: cambios físicos sin cambios de metadatos no quedan certificados
por la huella. Se mantienen los verificadores de carga/descarga/finalización existentes.

RLS, aislamiento frente a propietario/superusuario, separación operativa de credenciales, RESTRICTED,
retención/purga y operación de archivos, alternativas verificadas e IA supervisada continúan pendientes.
Los guards SQL no equivalen a autorización por identidad humana de cada conexión SQL: la aplicación
sigue validando sesión/rol/ámbito, y un propietario puede alterar objetos privilegiados.

## Archivos

Nuevos: ClosureRiskReview.cs, ClosureRiskContracts.cs, ClosureRiskMapping.cs,
IncidentClosureRiskService.cs, ClosureRiskTests.cs y migración 20261027145849_ClosureRiskReviews
(con Designer y snapshot EF actualizado).

Integraciones: IIncidentService, IncidentWorkflowController, IncidentWorkflowService,
IncidentClosureReadinessService/Contracts, IncidentMapping, OpenIrpDbContext y ci/grant-runtime.sql.
Fixtures y expectativas de ciclo de vida, preparación/contexto, ejecución y evidencia adaptados a la
política nueva, conservando casos negativos. README, plan maestro y bitácora actualizados al finalizar.
Before-images de archivos previos conservadas en artifacts/closure-risk/before. Frontend preservado.

## Verificación final y aplicación local

- Compilación Release, SDK .NET 10.0.403: cero errores y advertencias. EF sin diferencias pendientes de modelo.
- Selección enfocada inicial: **34/34 en PostgreSQL 17**. [TRX](../artifacts/closure-risk-focused-pg17-1f6469af392b41c09880a64de19d7f04/paso26-pg17.trx).
- Primera selección ampliada: **281/284 en PostgreSQL 17**. Se corrigieron un fixture que comparaba precisión temporal en memoria con la de PostgreSQL y dos escenarios antiguos que no preparaban la aceptación exigida por la política nueva. [TRX inicial](../artifacts/closure-risk-related-pg17-247d9124852f4c86ac5aab059f7433c5/paso26-pg17.trx).
- Suite global: **957/958 en PostgreSQL 17 y 957/958 en PostgreSQL 18.3**, sin omitidas. Único fallo: RestrictedRuntimeCanWriteBusinessAndAppendAuditButCannotMutateHistory, cuyo escenario antiguo cerraba sin evaluación. Se actualizó para registrar la evaluación usando la MISMA cuenta SQL limitada y comprobar el incremento adicional de revisión/auditoría. [Global PG17](../artifacts/closure-risk-full-pg17-810ddd47927244c091d54318d8112df6/paso26-pg17.trx), [global PG18.3](../artifacts/closure-risk-full-pg18-3dc7b88452ca40859db92b838602bf9d/paso26.trx).
- Verificación final después de esa corrección: **285/285 en PostgreSQL 17 y 285/285 en PostgreSQL 18.3**, cero fallidas/omitidas. Incluye las **40 pruebas nuevas**, los módulos relacionados y el caso de cuenta limitada corregido. Las 40 están incluidas en las 285.
- [Final PG17](../artifacts/closure-risk-final-pg17-4f3c50021c504c83ab77d02696345cee/paso26-pg17.trx), [final PG18.3](../artifacts/closure-risk-final-pg18-6a07a226768a43249fec6ba1e93db109/paso26.trx), [matriz y hashes](../artifacts/closure-risk/validation.json).
- No se repitió la suite global completa después del último ajuste de fixture. Las DLL de negocio permanecen byte a byte idénticas a las de las ejecuciones globales. La unión por IDs TRX acredita **958 casos distintos aprobados por motor**, combinando global y final; NO equivale a una ejecución global final de 958/958. [Cobertura PG17](../artifacts/closure-risk/coverage-PG17.json), [cobertura PG18.3](../artifacts/closure-risk/coverage-PG18-3.json), [hashes de negocio](../artifacts/closure-risk/global-business-assemblies.json).
- Cubre aceptación/rechazo y supersesión, vigencia a través de solicitud/aprobación, cambios de contexto sin cambio de revisión, reapertura, cierres anteriores, rol/cuenta/ámbito, declaración propia, validación/CSRF, historial EF/SQL/TRUNCATE, rollback de todos los agregados, SQL sin aceptación/eventos y carreras reales. La cuenta runtime puede registrar evaluación y cerrar, con la misma huella de riesgo bajo timezone distinto, sin mutar historia.
- Respaldo completo anterior a migración 23 restaurado en PostgreSQL 17 aislado, sin puertos publicados. Coinciden todos los conteos app/auth/audit, los 22 IDs de migración previos y las definiciones de funciones. SHA256: **009041B427E5A3564E12F5C3D31838C328762F19D1A6A5A5BA3A17546DB2FA20**. [Restauración](../artifacts/closure-risk/restore-verified.txt), [respaldo y hash](../artifacts/closure-risk/backup-sha256.json).
- Migración **20261027145849_ClosureRiskReviews** aplicada únicamente a openirp_dev local, **127.0.0.1:5433**, propietario openirp, tras comprobar contexto Docker local, destino, respaldo, matriz y ausencia de cambios desde el respaldo. **23 migraciones**; ocho capacidades/tres categorías y conteos previos conservados. closure_risk_reviews tiene cero filas. No se crearon usuarios ni datos de negocio en la base habitual.
- Todas las funciones app/auth previas conservan sus definiciones. Esquema real: 13 columnas, dos FK y cinco triggers nuevos (dos diferidos), con seis funciones. [SQL incremental](../artifacts/closure-risk/ClosureRiskReviews.sql), [verificador](../artifacts/closure-risk/Verify-Schema.sql), [resultado](../artifacts/closure-risk/schema-verified.txt), [conservación](../artifacts/closure-risk/dev-preserved.txt), [antes](../artifacts/closure-risk/dev-before.json), [después](../artifacts/closure-risk/dev-after.json).
- HTTPS posterior: health/OpenAPI/Swagger 200; GET closure-readiness y GET/POST closure-risk-reviews responden 401 anónimos. Contratos tipados y decisiones/revisión/contexto obligatorios presentes en OpenAPI. No es el total de endpoints; health es estático. Los flujos autenticados se verificaron en bases temporales. [Resultados](../artifacts/closure-risk-http-ce7d972b22a646f6a7f7993b5a2b5bdc/results.json), [OpenAPI](../artifacts/closure-risk-http-ce7d972b22a646f6a7f7993b5a2b5bdc/openapi.json).
- MCP actualizado: 3596 nodos/11295 relaciones. Graphify AST/HTML: 3074 nodos/8666 relaciones/231 comunidades, sin IA externa.
- Sin commit, push, merge ni mutaciones remotas. HEAD permanece 9cec6a075c918ca88bf02fdda6b8ad750c8cf4a3. Se preservaron frontend y cambios previos.
