# Entrega previa a IA — Iteraciones 28–32

Fecha de trabajo: 2026-10-27, America/Santiago. Workspace D:\Desarrollos\OpenIRP; rama feature/paso26-identidad-organizaciones.

**Estado: entregas 28–32 implementadas y verificadas; base habitual actualizada a 26 migraciones, con respaldo restaurado y datos conservados. La siguiente etapa funcional es IA local y queda pendiente.**

## Alcance autorizado

El usuario pidió continuar hasta antes de la integración con IA local. Se conservan operación manual, límites de ámbito, revisión humana, prohibición de permisos globales implícitos y todos los cambios locales previos. No se habilita Ollama funcional, generación de propuestas de IA ni persistencia de respuestas originales. La siguiente etapa de IA queda fuera de esta entrega.

Políticas confirmadas en esta sesión:

1. RESTRICTED exclusivo de ORG_ADMIN activo local, sin bypass PLATFORM_ADMIN ni acceso ANALYST/VIEWER.
2. Propagación conservadora de avisos por procedencia a todas las derivadas, con aviso genérico y decisión local independiente por ejecución.
3. Conservar archivos retirados sin purga automática; diagnóstico administrativo sin borrado físico.

## Entregas

| Iteración | Función | Guía |
|---|---|---|
| 28 | Revisión técnica humana de evidencia seleccionada, aceptación/rechazo, método, contexto, autor e historial inmutable. | [Guía 28](Iteracion28_Revision_Tecnica_Evidencias.md) |
| 29 | Procedimiento alternativo con soporte y validación humana contextual; no sustituye autorización por arista. | [Guía 29](Iteracion29_Alternativas_Operativas_Validadas.md) |
| 30 | Avisos transitivos en clones/nuevas versiones; bloqueo SQL/aplicación e independencia por ejecución, sin exponer detalle privado de origen. | [Guía 30](Iteracion30_Propagacion_Avisos_Seguridad.md) |
| 31 | RESTRICTED en TEXT/REFERENCE/FILE; filtro antes de paginar, controles de lectura/descarga y de selección al completar. | [Guía 31](Iteracion31_Evidencias_RESTRICTED.md) |
| 32 | Verificación física administrativa auditable y diagnóstico local acotado de objetos/temporales, sin purga ni liberación automática. | [Guía 32](Iteracion32_Diagnostico_Archivos_Privados.md) |

La validación de alternativas es humana y documental, con evidencia y vigencia; no es equivalencia técnica calculada automáticamente. Todos los cierres siguen exigiendo aceptación vigente de riesgo ORG_ADMIN, y HIGH/CRITICAL conserva aprobación adicional. No se inventa un nuevo requisito universal de revisión técnica para todos los cierres.

## Migraciones preparadas

- 20261027193143_OperationalReviews: historia y contexto de revisiones técnicas/alternativas.
- 20261027193519_RestrictedEvidence: ampliación de clasificación y creador administrativo elegible.
- 20261027193702_DerivedSecurityAlerts: procedencia transitiva y decisiones para avisos heredados.

Se generan y prueban inicialmente en PostgreSQL desechable. El respaldo completo previo de openirp_dev tiene 23 migraciones y cero registros FILE. Fue restaurado en PostgreSQL 17 aislado, sin puertos publicados: coinciden IDs de migración, todos los conteos app/auth/audit y funciones app/auth. [Restauración](../artifacts/pre-ai/restore-verified.txt), [hash](../artifacts/pre-ai/backup-sha256.json), [SQL incremental](../artifacts/pre-ai/PreAi.sql).

## Verificación final

- Release con .NET SDK 10.0.403: **cero errores y advertencias**. EF sin diferencias pendientes de modelo en PostgreSQL 17 y 18.3.
- Suites completas iniciales: **1017/1019 en PostgreSQL 17.11 y 1017/1019 en PostgreSQL 18.3**, sin omitidas. Los dos fallos fueron fixtures: interceptor ligado al SQL anterior y lista de TRUNCATE sin la nueva tabla referenciada. Se corrigieron sin relajar los controles SQL ni cambiar la lógica de negocio.
- Selección final después de esas correcciones: **42/42 en cada motor**, sin fallidas/omitidas. Incluye **35 casos nuevos** de las cinco entregas, los métodos afectados completos y cuatro casos añadidos después de las suites iniciales (HTTP/CSRF, cuenta SQL limitada, selección RESTRICTED por analista y bloqueo SQL/independencia entre ejecuciones). Los 35 están incluidos en 42.
- Las DLL de Domain/Application/Infrastructure/Api son byte a byte idénticas a las usadas en ambas suites completas. No se repitió la suite global completa después de corregir los fixtures. La unión por **testId TRX acredita 1023 casos distintos aprobados por motor**, combinando inicial y final; NO representa una ejecución global final de 1023/1023. Se usan IDs porque dos nombres antiguos de casos se truncan al mismo texto visible.
- [Global PG17](../artifacts/pre-ai-full-pg17-72a8adc0f8634d2c97f7ebc69b2b8dd3/paso26-pg17.trx), [global PG18.3](../artifacts/pre-ai-full-pg18-c527bd5e6ffc452dac3058770d818832/paso26.trx).
- [Final PG17](../artifacts/pre-ai-final-pg17-d37ad51b9c484f77bc771f4fe513b304/paso26-pg17.trx), [final PG18.3](../artifacts/pre-ai-final-pg18-92f2073eae1a473fafd254e837a76263/paso26.trx), [matriz y hashes](../artifacts/pre-ai/validation.json), [cobertura PG17](../artifacts/pre-ai/coverage-PG17.json), [cobertura PG18.3](../artifacts/pre-ai/coverage-PG18-3.json), [DLL iniciales](../artifacts/pre-ai/full-business-assemblies.json), [DLL finales](../artifacts/pre-ai/final-business-assemblies.json).
- Cobertura nueva: revisión/alternativa aceptada y rechazada, supersesión sin borrado, contexto obsoleto, declaración propia, cambios de evidencia/asignación, roles/ámbito, rollback, carrera con una sola revisión ganadora; permisos de RESTRICTED para TEXT/REFERENCE/FILE, filtrado y redacción, selección prohibida por analista; verificación física, cuarentena/retirada sin purga; herencia de uno/dos niveles, aviso genérico, nuevo aviso que invalida continuidad, nuevos inicios bloqueados, SQL que impide avanzar e independencia entre ejecuciones; cuenta runtime sin privilegios para alterar historia.
- El diagnóstico offline conservó tres archivos propios: conteos correctos, límites y sobrescritura rechazados, hashes intactos. [Informe](../artifacts/pre-ai/storage-diagnosis.json), [verificación](../artifacts/pre-ai/storage-diagnosis-verified.json). La prueba no inspeccionó el almacén real del usuario ni afirma haber escaneado malware.
- Respaldo completo anterior: **2C7531C618AACF13656277DD5D2EE660CA9AC9A89B58762476250B6F2C043354**. Restauración aislada verificada con todas las tablas/migraciones/funciones anteriores; ensayo adicional de actualización de esa restauración de 23 a 26 migraciones, conservando conteos y las 23 IDs anteriores. Solo cambian cuatro funciones previas previstas. [Respaldo/hash](../artifacts/pre-ai/backup-sha256.json), [restauración](../artifacts/pre-ai/restore-verified.txt), [ensayo actualizado](../artifacts/pre-ai/upgrade-restore-verified.txt).
- Las tres migraciones se aplicaron únicamente a **openirp_dev**, propietario **openirp**, **127.0.0.1:5433**, tras comprobar contexto Docker local, binarios/código, matriz, hash del respaldo/SQL y ausencia de cambios desde el respaldo. El estado posterior coincide exactamente con el ensayo restaurado: IDs de migración, conteos de todas las tablas app/auth/audit y definiciones de funciones. **26 migraciones**, ocho capacidades/tres categorías; cero filas nuevas de identidad/negocio/revisiones. [SQL](../artifacts/pre-ai/PreAi.sql), [esquema](../artifacts/pre-ai/schema-verified.txt), [conservación](../artifacts/pre-ai/dev-preserved.txt), [antes](../artifacts/pre-ai/dev-before.json), [después](../artifacts/pre-ai/dev-after.json).
- HTTPS posterior: health/OpenAPI/Swagger 200; GET evidence-readiness, GET/POST operational-reviews y POST file/verification responden 401 anónimos. Contratos tipados, campos explícitos y AccessRestricted presentes. No es un smoke de todos los endpoints; health es estático. Los flujos autenticados se probaron en bases temporales. [Resultados](../artifacts/pre-ai-http-540b6a23e3674653b11291b27a3d38a2/results.json), [OpenAPI](../artifacts/pre-ai-http-540b6a23e3674653b11291b27a3d38a2/openapi.json).
- MCP: **3880 nodos/12119 relaciones**. Graphify AST/HTML: **3277 nodos/9291 relaciones/247 comunidades**, sin IA externa.

## Límites que continúan

IA local, propuestas y retención de respuestas originales no implementadas en este tramo. RLS, separación operativa de credenciales, secretos/despliegue, antimalware real, plazos/purga física y validación empírica de severidad siguen como trabajo posterior, con sus políticas. No confundir estas entregas con preparación productiva completa de la API.

No se ha hecho commit, push ni merge. Frontend y trabajo anterior preservados. La aplicación local ya está verificada. No repetir estas migraciones ni ejecutar downgrade para reanudar; inspeccionar primero estado real, historial y datos. Procesos propios temporales detenidos y servicios habituales conservados.

## Archivos editados en esta entrega

El inventario se compara contra el estado local preservado al comenzar, no contra HEAD; excluye cambios anteriores atribuidos al usuario. [Inventario verificable](../artifacts/pre-ai/edited-files.json), [copia y hashes previos](../artifacts/pre-ai/baseline-files.json).

- Editado: [OpenIRP_Documentacion/Bitacora_Desarrollo_OpenIRP.md](../OpenIRP_Documentacion/Bitacora_Desarrollo_OpenIRP.md).
- Nuevo: [OpenIRP_Documentacion/Entrega_PreIA_Iteraciones28_32.md](../OpenIRP_Documentacion/Entrega_PreIA_Iteraciones28_32.md).
- Nuevo: [OpenIRP_Documentacion/Iteracion28_Revision_Tecnica_Evidencias.md](../OpenIRP_Documentacion/Iteracion28_Revision_Tecnica_Evidencias.md).
- Nuevo: [OpenIRP_Documentacion/Iteracion29_Alternativas_Operativas_Validadas.md](../OpenIRP_Documentacion/Iteracion29_Alternativas_Operativas_Validadas.md).
- Nuevo: [OpenIRP_Documentacion/Iteracion30_Propagacion_Avisos_Seguridad.md](../OpenIRP_Documentacion/Iteracion30_Propagacion_Avisos_Seguridad.md).
- Nuevo: [OpenIRP_Documentacion/Iteracion31_Evidencias_RESTRICTED.md](../OpenIRP_Documentacion/Iteracion31_Evidencias_RESTRICTED.md).
- Nuevo: [OpenIRP_Documentacion/Iteracion32_Diagnostico_Archivos_Privados.md](../OpenIRP_Documentacion/Iteracion32_Diagnostico_Archivos_Privados.md).
- Editado: [OpenIRP_Documentacion/OpenIRP_Planificacion_Integral.md](../OpenIRP_Documentacion/OpenIRP_Planificacion_Integral.md).
- Editado: [README.md](../README.md).
- Editado: [apps/api/OpenIRP.Api/Controllers/EvidenceFilesController.cs](../apps/api/OpenIRP.Api/Controllers/EvidenceFilesController.cs).
- Editado: [apps/api/OpenIRP.Api/Controllers/ExecutionsController.cs](../apps/api/OpenIRP.Api/Controllers/ExecutionsController.cs).
- Editado: [apps/api/OpenIRP.Application/Executions/EvidenceReadinessContracts.cs](../apps/api/OpenIRP.Application/Executions/EvidenceReadinessContracts.cs).
- Editado: [apps/api/OpenIRP.Application/Executions/IExecutionService.cs](../apps/api/OpenIRP.Application/Executions/IExecutionService.cs).
- Nuevo: [apps/api/OpenIRP.Application/Executions/OperationalReviewContracts.cs](../apps/api/OpenIRP.Application/Executions/OperationalReviewContracts.cs).
- Editado: [apps/api/OpenIRP.Application/Executions/SecurityHoldContracts.cs](../apps/api/OpenIRP.Application/Executions/SecurityHoldContracts.cs).
- Editado: [apps/api/OpenIRP.Application/Incidents/IFileEvidenceService.cs](../apps/api/OpenIRP.Application/Incidents/IFileEvidenceService.cs).
- Nuevo: [apps/api/OpenIRP.Domain/Entities/OperationalReview.cs](../apps/api/OpenIRP.Domain/Entities/OperationalReview.cs).
- Editado: [apps/api/OpenIRP.Infrastructure/Incidents/IncidentClosureContextService.cs](../apps/api/OpenIRP.Infrastructure/Incidents/IncidentClosureContextService.cs).
- Editado: [apps/api/OpenIRP.Infrastructure/Incidents/IncidentClosureReadinessService.cs](../apps/api/OpenIRP.Infrastructure/Incidents/IncidentClosureReadinessService.cs).
- Editado: [apps/api/OpenIRP.Infrastructure/Incidents/IncidentEvidenceReadinessService.cs](../apps/api/OpenIRP.Infrastructure/Incidents/IncidentEvidenceReadinessService.cs).
- Editado: [apps/api/OpenIRP.Infrastructure/Incidents/IncidentEvidenceService.cs](../apps/api/OpenIRP.Infrastructure/Incidents/IncidentEvidenceService.cs).
- Editado: [apps/api/OpenIRP.Infrastructure/Incidents/IncidentExecutionService.cs](../apps/api/OpenIRP.Infrastructure/Incidents/IncidentExecutionService.cs).
- Editado: [apps/api/OpenIRP.Infrastructure/Incidents/IncidentFileEvidenceService.cs](../apps/api/OpenIRP.Infrastructure/Incidents/IncidentFileEvidenceService.cs).
- Nuevo: [apps/api/OpenIRP.Infrastructure/Incidents/IncidentFileVerificationService.cs](../apps/api/OpenIRP.Infrastructure/Incidents/IncidentFileVerificationService.cs).
- Nuevo: [apps/api/OpenIRP.Infrastructure/Incidents/IncidentOperationalReviewService.cs](../apps/api/OpenIRP.Infrastructure/Incidents/IncidentOperationalReviewService.cs).
- Editado: [apps/api/OpenIRP.Infrastructure/Incidents/IncidentSecurityHoldService.cs](../apps/api/OpenIRP.Infrastructure/Incidents/IncidentSecurityHoldService.cs).
- Editado: [apps/api/OpenIRP.Infrastructure/Persistence/Configurations/EvidenceMapping.cs](../apps/api/OpenIRP.Infrastructure/Persistence/Configurations/EvidenceMapping.cs).
- Editado: [apps/api/OpenIRP.Infrastructure/Persistence/Configurations/IncidentMapping.cs](../apps/api/OpenIRP.Infrastructure/Persistence/Configurations/IncidentMapping.cs).
- Nuevo: [apps/api/OpenIRP.Infrastructure/Persistence/Configurations/OperationalReviewMapping.cs](../apps/api/OpenIRP.Infrastructure/Persistence/Configurations/OperationalReviewMapping.cs).
- Editado: [apps/api/OpenIRP.Infrastructure/Persistence/Configurations/SecurityHoldMapping.cs](../apps/api/OpenIRP.Infrastructure/Persistence/Configurations/SecurityHoldMapping.cs).
- Nuevo: [apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261027193143_OperationalReviews.Designer.cs](../apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261027193143_OperationalReviews.Designer.cs).
- Nuevo: [apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261027193143_OperationalReviews.cs](../apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261027193143_OperationalReviews.cs).
- Nuevo: [apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261027193519_RestrictedEvidence.Designer.cs](../apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261027193519_RestrictedEvidence.Designer.cs).
- Nuevo: [apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261027193519_RestrictedEvidence.cs](../apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261027193519_RestrictedEvidence.cs).
- Nuevo: [apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261027193702_DerivedSecurityAlerts.Designer.cs](../apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261027193702_DerivedSecurityAlerts.Designer.cs).
- Nuevo: [apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261027193702_DerivedSecurityAlerts.cs](../apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261027193702_DerivedSecurityAlerts.cs).
- Editado: [apps/api/OpenIRP.Infrastructure/Persistence/Migrations/OpenIrpDbContextModelSnapshot.cs](../apps/api/OpenIRP.Infrastructure/Persistence/Migrations/OpenIrpDbContextModelSnapshot.cs).
- Editado: [apps/api/OpenIRP.Infrastructure/Persistence/OpenIrpDbContext.cs](../apps/api/OpenIRP.Infrastructure/Persistence/OpenIrpDbContext.cs).
- Editado: [apps/api/OpenIRP.IntegrationTests/EvidenceTests.cs](../apps/api/OpenIRP.IntegrationTests/EvidenceTests.cs).
- Nuevo: [apps/api/OpenIRP.IntegrationTests/OperationalReviewTests.cs](../apps/api/OpenIRP.IntegrationTests/OperationalReviewTests.cs).
- Editado: [apps/api/OpenIRP.IntegrationTests/PlaybookDraftPersistenceTests.cs](../apps/api/OpenIRP.IntegrationTests/PlaybookDraftPersistenceTests.cs).
- Nuevo: [apps/api/OpenIRP.IntegrationTests/PreAiSecurityAndFilesTests.cs](../apps/api/OpenIRP.IntegrationTests/PreAiSecurityAndFilesTests.cs).
- Editado: [apps/api/OpenIRP.IntegrationTests/SecurityHoldTests.cs](../apps/api/OpenIRP.IntegrationTests/SecurityHoldTests.cs).
- Nuevo: [ci/Inspect-EvidenceStorage.ps1](../ci/Inspect-EvidenceStorage.ps1).
- Editado: [ci/grant-runtime.sql](../ci/grant-runtime.sql).
