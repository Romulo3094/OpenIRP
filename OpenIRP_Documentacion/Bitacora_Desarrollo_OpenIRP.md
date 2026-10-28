# Bitácora de desarrollo de OpenIRP

**Fecha de corte:** 2026-10-27, America/Santiago.
**Workspace:** D:\Desarrollos\OpenIRP.
**Rama:** feature/paso26-identidad-organizaciones.
**Estado:** desarrollo local verificado hasta Iteración 33: EXPLAIN_STEP con qwen3:4b-instruct sin thinking, contexto mínimo y evaluaciones auditables. Base habitual con 27 migraciones, respaldo restaurado y datos conservados. IA requiere activación explícita; revisión humana definitiva y propuestas siguen pendientes.

Este archivo conserva el estado del trabajo y permite retomarlo sin depender del chat. Resume entregas y decisiones; los contratos detallados y sus evidencias están en las guías enlazadas. Los resultados describen las verificaciones indicadas, no garantizan que servicios o procesos sigan ejecutándose después.

## 1. Punto de reanudación

La última entrega es **IA local EXPLAIN_STEP (Iteración 33)**: solo qwen3:4b-instruct, think=false y /no_think; sin alternativa al modelo base y con rechazo de thinking. Contexto mínimo por tarea, JSON acotado, revalidación tras inferir y evaluaciones inmutables, sin guardar respuesta original. [Contrato, activación y evidencias](Iteracion33_Asistencia_Local_Explicacion_Pasos.md). Final relacionado 259/259 por motor, incluidos 55 casos nuevos; consulta real sintética VALID en 2506 ms. No es una nueva ejecución global completa. Próximo: decisión humana definitiva por evaluación y posteriores tipos de asistencia/propuestas, sin ejecución automática.

La **evaluación y aceptación de riesgo en todos los cierres (Iteración 26)** continúa obligatoria. ORG_ADMIN registra evaluación y aceptación/rechazo con revisión y contexto; solo una aceptación vigente permite cerrar o solicitar/aprobar cierre. Historial inmutable, supersesión sin borrado y guards SQL. La solicitud de aprobación no invalida por sí misma la evaluación, pero cambios de contexto o reapertura sí requieren nueva evaluación. HIGH/CRITICAL mantiene su aprobación adicional. Base local: 27 migraciones, incluida LocalAssistanceAssessments después de las entregas previas a IA. Iteración 27: regresión relacionada 187/187 por motor, incluidas 31 nuevas; sin nueva suite global completa. Verificaciones de Iteración 26: 285/285 por motor; global inicial 957/958 y cobertura combinada de 958 IDs aprobados por motor, sin afirmar una ejecución global final de 958/958.

La cobertura declarada de capacidades (Iteración 19) permanece informativa: RequiresHumanReview=true. El usuario aprobó PDF/PNG/JPEG/TXT hasta 10 MB y conservar retiradas sin purga automática. Las iteraciones 28–32 incorporan verificación humana de alternativas/evidencias, propagación y diagnóstico de archivos sin purga. No calculan equivalencia técnica ni certificación automática. Leer guías 16–32 y §11 del plan maestro.
El código de API de las iteraciones 7–17 se publicó en 9cec6a075c918ca88bf02fdda6b8ad750c8cf4a3.
El avance de API de las iteraciones 18–33 y Swagger se incluye en la publicación de §26. El frontend continúa local y excluido hasta nueva indicación; no descartarlo ni sobrescribirlo.

## 2. Fuentes de referencia

- [Planificación integral — documento maestro](OpenIRP_Planificacion_Integral.md). Sus primeras secciones describen el diseño; las secciones de implementación y sus verificaciones distinguen qué está desarrollado.
- [Administración local y aprovisionamiento inicial](Paso26_Administracion_Local.md).
- [Auditoría local](Paso26_Auditoria_Local.md).
- [Regla de severidad v1](Propuesta_Regla_Severidad_v1.md).
- Guías por iteración en la tabla siguiente.
- Evidencias, TRX, SQL, conteos y respaldos bajo artifacts/, fuera del seguimiento Git habitual.

## 3. Avance funcional acumulado

| Entrega | Implementado | Referencia |
|---|---|---|
| Paso 26: identidad y organizaciones | Aprovisionamiento offline controlado del primer PLATFORM_ADMIN; organización y primer ORG_ADMIN en transacción; membresías, roles, estados y protección del último administrador activo. | [Administración](Paso26_Administracion_Local.md) |
| Auditoría administrativa | Auditoría de operaciones, correlación, consulta autorizada y protecciones append-only; confirmación conjunta de cambios e historial en los flujos soportados. | [Auditoría](Paso26_Auditoria_Local.md) |
| Iteración 2 | Catálogos, capacidades organizacionales, disponibilidad/limitaciones y activos; ámbitos y permisos por organización. | [Guía 2](Iteracion2_Catalogos_Capacidades_Activos.md) |
| Iteración 3 | Registro, consulta, edición, responsable, revisiones e historial de incidentes por organización. | [Guía 3](Iteracion3_Registro_Incidentes.md) |
| Iteración 4 | Activos afectados: reporte, asociación al inventario y vinculación posterior; snapshots históricos que no se reescriben al cambiar el inventario. | [Guía 4](Iteracion4_Activos_Incidente.md) |
| Iteración 5 | Evaluación de capacidades por incidente, limitaciones y herencia del perfil organizacional sin modificarlo. | [Guía 5](Iteracion5_Capacidades_Incidente.md) |
| Iteración 6 | Evaluación explícita de confidencialidad, integridad y disponibilidad (CIA); valores desconocidos conservados como null. | [Guía 6](Iteracion6_Evaluacion_CIA.md) |
| Iteración 7 | Alcance y afectación operacional del incidente, con evaluación e historial. | [Guía 7](Iteracion7_Contexto_Incidente.md) |
| Iteración 8 | Criticidad evaluada, justificación de reducciones e invalidación de la evaluación cuando cambia el contexto de activos. | [Guía 8](Iteracion8_Criticidad_Incidente.md) |
| Iteración 9 | Severidad determinista v1, Pending estricto, reevaluación explícita de casos anteriores y máximo histórico no decreciente. | [Guía 9](Iteracion9_Severidad_Incidente.md) |
| Iteración 10 | Evaluación, respuesta, resolución, cierre, descarte y reapertura; revisión administrativa de cierre por máximo histórico HIGH/CRITICAL. | [Guía 10](Iteracion10_Ciclo_Vida_Cierre.md) |
| Iteración 11 | Playbooks PUBLIC/PRIVATE y borradores estructurados: pasos, flags, requisitos de capacidades y dependencias acíclicas. | [Guía 11](Iteracion11_Catalogo_Borradores_Playbooks.md) |
| Iteración 12 | Revisión, rechazo/publicación, versiones inmutables, vigencia explícita, nuevas versiones y retirada administrativa. | [Guía 12](Iteracion12_Publicacion_Versiones_Playbooks.md) |
| Iteración 13 | Clonación controlada a borradores independientes, UUID nuevos, procedencia inmutable y protección de contenido privado entre organizaciones. | [Guía 13](Iteracion13_Clonacion_Playbooks.md) |
| Iteración 14 | Ejecuciones ligadas a una versión, tareas, responsables, estados, dependencias, finalización/cancelación e historial transaccional. | [Guía 14](Iteracion14_Ejecuciones_Tareas.md) |
| Iteración 15 | Solicitud y decisión de aprobación de pasos; excepción de autoaprobación, decisión única, invalidación por reasignación y controles SQL. | [Guía 15](Iteracion15_Aprobaciones_Pasos.md) |
| Iteración 16 | Evidencias TEXT/REFERENCE, consulta/retirada/historial y selección explícita de evidencia activa de tarea al completar. | [Guía 16](Iteracion16_Evidencias_Incidente_Tarea.md) |
| Iteración 17 | Archivos privados FILE, carga multipart y SHA-256; descarga autorizada, recuperación, estados e historial; conservación de retiradas sin purga. | [Guía 17](Iteracion17_Archivos_Privados_Evidencia.md) |
| Iteración 18 | Solicitud y decisión administrativa de omisiones, impacto/riesgo/contexto, SKIPPED terminal e historial atómico. | [Guía 18](Iteracion18_Omisiones_Autorizadas.md) |
| Iteración 19 | Cobertura declarada de requisitos/capacidades por ejecución o tarea, procedencia y revisiones; consulta informativa sin cambios de estado. | [Guía 19](Iteracion19_Cobertura_Capacidades_Ejecucion.md) |
| Iteración 20 | Excepción administrativa independiente por arista con predecesor omitido, contexto vigente, historial y controles SQL/aplicación. | [Guía 20](Iteracion20_Excepciones_Dependencias.md) |
| Iteración 21 | Consulta por tarea de dependencias satisfechas/excepciones vigentes, separadas del historial; snapshot, ámbito y permisos actuales. | [Guía 21](Iteracion21_Vigencia_Dependencias.md) |
| Iteración 22 | Revocación append-only por aprobación, bloqueo atómico de tarea en curso, nueva autorización independiente y metadatos de vigencia. | [Guía 22](Iteracion22_Revocacion_Excepciones.md) |
| Iteración 23 | Retirada de seguridad por versión, bloqueo vigente, continuidad/cancelación organizacional e historia inmutable. | [Guía 23](Iteracion23_Bloqueo_Seguridad_Continuidad.md) |
| Iteración 24 | Consulta informativa y consistente de preparación para el cierre, acciones según lector y agregados sin certificar riesgo residual ni archivos. | [Guía 24](Iteracion24_Preparacion_Cierre.md) |
| Iteración 25 | Huella de contexto en la misma instantánea, precondición opcional en cierre y token validado en historial. Política de evaluación ORG_ADMIN para todos los cierres aprobada, aún pendiente de aplicar. | [Guía 25](Iteracion25_Contexto_Revision_Cierre.md) |
| Iteración 26 | Evaluación y aceptación vigente ORG_ADMIN en todos los cierres, historial inmutable, contexto semántico y defensa SQL; preserva cierres históricos. | [Guía 26](Iteracion26_Evaluacion_Riesgo_Cierre.md) |
| Iteración 27 | Soporte actual de evidencias seleccionadas por tarea, brechas y metadatos FILE, snapshot con huella de riesgo; consulta informativa sin certificar bytes ni suficiencia técnica. | [Guía 27](Iteracion27_Soporte_Evidencias_Ejecucion.md) |
| Iteración 28 | Revisión técnica humana de evidencia seleccionada, contexto vigente, decisión e historial inmutable. | [Guía 28](Iteracion28_Revision_Tecnica_Evidencias.md) |
| Iteración 29 | Alternativa operativa con procedimiento, soporte y validación humana; no sustituye autorización por arista. | [Guía 29](Iteracion29_Alternativas_Operativas_Validadas.md) |
| Iteración 30 | Avisos transitivos por procedencia, genéricos al receptor, bloqueo y decisión independiente por ejecución. | [Guía 30](Iteracion30_Propagacion_Avisos_Seguridad.md) |
| Iteración 31 | RESTRICTED exclusivo de ORG_ADMIN local para TEXT/REFERENCE/FILE, con filtro y control al completar. | [Guía 31](Iteracion31_Evidencias_RESTRICTED.md) |
| Iteración 32 | Verificación administrativa auditable y diagnóstico acotado sin purga ni liberación automática de cuarentena. | [Guía 32](Iteracion32_Diagnostico_Archivos_Privados.md) |
| Iteración 33 | EXPLAIN_STEP local sin thinking; contexto mínimo, validación estricta, evaluación/auditoría atómicas y operación manual intacta. | [Guía 33](Iteracion33_Asistencia_Local_Explicacion_Pasos.md) |

La existencia de estos módulos no equivale a que la API del MVP esté terminada. Las partes pendientes se detallan más abajo.

### Frontend existente


No representa todavía la interfaz completa de administración, incidentes, playbooks, ejecuciones, aprobaciones, evidencias o IA. Los archivos de apps/web, su guía y ci/Start-LocalWeb.ps1 se preservaron durante el desarrollo de la API. No se repitieron sus pruebas ni su validación visual para crear esta bitácora; consultar sus resultados y límites en su propia guía.

## 4. Decisiones y reglas que deben conservarse

### Identidad, autorización y persistencia

- Sin registro público, cuentas de demostración automáticas ni permisos organizacionales implícitos por ser PLATFORM_ADMIN.
- PLATFORM_ADMIN es un grant separado; el acceso privado exige membresía y rol elegibles en la organización correspondiente.
- ORG_ADMIN, ANALYST y VIEWER conservan sus facultades específicas. Las cuentas, organizaciones, membresías y lockouts se comprueban en las operaciones.
- No dejar una organización sin administrador activo mediante las operaciones administrativas soportadas.
- Cookies de autenticación protegidas y CSRF en escrituras. La autorización del backend sigue siendo la autoridad, aunque el frontend oculte acciones.
- Escrituras soportadas bajo gate transaccional PostgreSQL (1869636978,26), revisión optimista y revalidación de permisos. Cambios e historiales se confirman o revierten juntos.
- Historiales protegidos contra modificaciones y borrado desde la aplicación y mediante los guards SQL correspondientes.
- RLS y separación operativa completa de credenciales siguen pendientes: no presentar la autorización actual como aislamiento completo frente a un propietario de PostgreSQL.
- La auditoría no se describe como criptográficamente inmutable frente a administradores privilegiados. El fallo de la auditoría de rechazo conserva la limitación operativa documentada; no existe una cola durable implementada.

### Severidad: decisiones expresas del usuario

1. Adoptar la **matriz v1 como regla inicial del MVP**, sujeta a evaluación con casos reales.
2. Si falta cualquier entrada requerida, mantener **Pending y severity=null**. No convertir desconocido en impacto nulo.
3. Para incidentes anteriores, realizar una primera reevaluación **explícita y auditable**; iniciar el máximo histórico desde ese cálculo.
4. No atribuir severidades retrospectivas a eventos antiguos.
5. Conservar el máximo histórico aunque la severidad actual disminuya.
6. CIA, alcance, afectación operacional y criticidad siguen siendo entradas explícitas. Las reducciones de criticidad requieren la justificación que corresponda.

La regla exacta y sus umbrales están en [Propuesta_Regla_Severidad_v1.md](Propuesta_Regla_Severidad_v1.md) y la guía de Iteración 9.

### Playbooks, ejecuciones y cierre

- Publicar requiere el flujo de revisión; clonar crea un borrador, sin heredar aprobación ni publicar automáticamente.
- Una ejecución queda ligada a una versión inmutable y crea exactamente una tarea por paso. Solo una ejecución IN_PROGRESS por incidente.
- Una publicación posterior no altera las instrucciones de una ejecución existente basada en otra versión aún PUBLISHED.
- Dependencias satisfechas por predecesores COMPLETED o una excepción individual vigente por vínculo hacia un SKIPPED autorizado. Omitir no equivale a completar; no hay excepción para predecesores pendientes/en curso.
- Solo el responsable asignado y actualmente elegible cambia el estado de su tarea.
- Una ejecución activa impide cambiar categoría o sacar al incidente de IN_RESPONSE.
- Finalizar o cancelar la ejecución no resuelve ni cierra automáticamente el incidente.
- El cierre manual actual exige aceptación vigente de riesgo por ORG_ADMIN en todos los niveles, clasificación completa, responsable elegible y revisión adicional por máximo histórico HIGH/CRITICAL. Pending/NotEvaluated no habilita cierre.
- Una versión ARCHIVED bloquea operación salvo continuidad administrativa para el aviso de seguridad vigente. La retirada ordinaria conserva el bloqueo; la de seguridad exige el ID actual incluso para cancelar.

### STEP_APPROVAL ya implementado

- Solicita el responsable activo; decide ORG_ADMIN de la misma organización.
- Para aprobar la solicitud propia se requiere excepción explícita y justificación adicional.
- La tarea debe estar PENDING/BLOCKED, con dependencias satisfechas (incluidas las excepciones individuales vigentes), versión utilizable y flag RequiresApproval.
- Cada solicitud admite una sola decisión definitiva; un rechazo permite solicitar nuevamente.
- Una operación de asignación posterior invalida solicitudes/aprobaciones anteriores, incluso si asigna la misma membresía.
- Antes de reasignar una tarea controlada IN_PROGRESS debe bloquearse con motivo.
- La aprobación sobrevive a bloqueo/reanudación sin reasignación; se vuelven a comprobar los controles operativos.
- STEP_APPROVAL es independiente de omisión, evidencia, aceptación de IA y cierre del incidente.

## 5. Pendientes para completar la API

El orden siguiente es una propuesta de continuación técnica, no una promesa de fechas ni una autorización para relajar reglas.

| Prioridad | Bloque | Trabajo pendiente / condición de terminación |
|---|---|---|
| 1 | IA local supervisada | EXPLAIN_STEP implementado (33). Pendientes aceptación/rechazo humano definitivo con decisión única por evaluación y contexto actual, ADAPT_STEP/SUMMARIZE_INCIDENT y evaluación empírica de pertinencia. Mantener modo sin thinking y operación manual. |
| 1 | Propuestas de IA a borradores | Propuesta pendiente, decisión humana y conversión autorizada a PRIVATE; publicación por el flujo ordinario sin degradar flags/dependencias. |
| 1 | Respuestas originales de IA | Definir retención, claves externas, cifrado, límites, acceso, expiración y respaldos antes de habilitar almacenamiento persistente real. |
| 2 | Endurecimiento y operación | RLS, separación operativa propietario/migrador/runtime, secretos, despliegue, auditoría operacional integral y recuperación reproducible. La cuenta runtime limitada y guards actuales no sustituyen ese trabajo. |
| 2 | Políticas productivas de archivos | Mantener conservación sin purga aprobada. Para ampliar: plazos/eliminación y respaldos, conciliación con intervención controlada, motor antimalware y liberación de cuarentenas bajo política explícita. Verificación/diagnóstico sin borrado ya implementados. |
| 2 | Validación final del MVP | Escenarios completos de extremo a extremo, evaluación empírica de severidad y pertinencia con casos reales/simulados y entrega reproducible. |

### Límites y decisiones vigentes

- RequiresEvidence exige seleccionar evidencia ACTIVE de la tarea exacta; leer RESTRICTED y usarla al completar exige ORG_ADMIN local. UUID y trazabilidad general no conceden acceso a su contenido.
- Una validación técnica humana no es certificación automática de suficiencia/equivalencia. Una alternativa no completa un predecesor ni sustituye excepción administrativa independiente por arista.
- Omisión, aprobación de paso, excepción/revocación, continuidad de seguridad y aceptación de riesgo son decisiones diferentes. Todos los cierres conservan aceptación vigente ORG_ADMIN; HIGH/CRITICAL mantiene aprobación adicional.
- Propagación conservadora por procedencia y aviso genérico aprobados e implementados; una decisión no se hereda entre ejecuciones.
- PDF/PNG/JPEG/TXT hasta 10 MB; retirados conservados sin purga. Diagnóstico no borra ni libera cuarentenas; no afirma antimalware.
- La Iteración 33 verifica EXPLAIN_STEP real sin thinking con datos sintéticos; no certifica todos los tipos ni corrección técnica. No se ejecutan acciones externas automáticamente.
- Pendientes decisiones de claves/plazos para respuestas originales de IA y políticas productivas posteriores de archivo/retención. RESTRICTED ya tiene política aprobada e implementación.

## 6. Entorno y base de datos: último estado verificado

| Elemento | Estado verificado |
|---|---|
| Solución | apps/api/OpenIRP.slnx: Domain, Application, Infrastructure, Api e IntegrationTests. |
| SDK utilizado | .NET 10.0.403; compilación Release sin errores ni advertencias en la última comprobación. |
| PostgreSQL habitual | Contenedor openirp-postgres; PostgreSQL 17.11, 127.0.0.1:5433; base openirp_dev, propietario openirp. |
| PostgreSQL de pruebas | Iteración 33: 259/259 por motor, incluidos 55 casos nuevos; selección relacionada, sin nueva suite global. Caso real sintético 1/1 con qwen3:4b-instruct sin thinking. Evidencias globales históricas conservadas. |
| Ollama habitual | Contenedor openirp-ollama, puerto local 11435 en el último cierre. |
| Migraciones habituales | 27; última: 20261027235738_LocalAssistanceAssessments. |
| Datos de referencia | Ocho capacidades y tres categorías de incidente. |
| Identidad y negocio | Tablas vacías en el último cierre: usuarios, organizaciones, membresías, auditoría, inventario, incidentes, playbooks, ejecuciones, aprobaciones, evidencias y sus historiales. |
| Procesos temporales | API de smoke y contenedores/clústeres propios de pruebas/restauración detenidos. |
| Puertos temporales | Sin listeners en 55432, 55433, 7113 y 5195 al cerrar la verificación; este dato es histórico, no una lectura permanente. |

No se fabricaron cuentas ni datos de negocio en la base habitual. Las pruebas autenticadas utilizaron bases temporales. Las pruebas manuales con usuarios reales requieren el aprovisionamiento administrativo explícito de la guía, sin credenciales predeterminadas.

### Migraciones aplicadas, en orden

1. 20261022130822_InitialIdentityOrganizations
2. 20261022175420_BasicAdministrativeAudit
3. 20261022183846_CatalogsCapabilitiesAssets
4. 20261022192153_InitialIncidentRegister
5. 20261022194422_IncidentAssetSnapshots
6. 20261022204102_IncidentCapabilityAssessments
7. 20261023032311_IncidentImpactAssessment
8. 20261023141918_IncidentContextAssessment
9. 20261023200334_IncidentCriticalityAssessment
10. 20261023202856_IncidentSeverityClassification
11. 20261023220910_IncidentLifecycleClosure
12. 20261023231051_PlaybookDraftCatalog
13. 20261024010838_PlaybookPublication
14. 20261024021251_PlaybookCloning
15. 20261024034804_InitialPlaybookExecutions
16. 20261024224015_StepApprovals
17. 20261026163753_IncidentTextReferenceEvidence
18. 20261026183812_PrivateEvidenceFiles
19. 20261026203604_SkipAuthorizations
20. 20261026224629_DependencyExceptions
21. 20261027024554_DependencyExceptionRevocations
22. 20261027031839_ExecutionSecurityHolds
23. 20261027145849_ClosureRiskReviews
24. 20261027193143_OperationalReviews
25. 20261027193519_RestrictedEvidence
26. 20261027193702_DerivedSecurityAlerts
27. 20261027235738_LocalAssistanceAssessments

No volver a tratar StepApprovals, IncidentTextReferenceEvidence, PrivateEvidenceFiles ni SkipAuthorizations ni DependencyExceptions ni DependencyExceptionRevocations como pendientes ni ejecutar downgrade para reanudar. Cualquier nueva migración debe revisar destino, historial y respaldo restaurado antes de aplicarse a la base habitual.

## 7. Verificaciones y evidencias principales

| Verificación | Resultado | Evidencia local |
|---|---|---|
| Selección final Iteración 23 — PG17 / PG18.3 | 312/312 por motor; 32 nuevas incluidas; sin fallidas/omitidas | [PG17](../artifacts/security-related-pg17-cff5ff6c18264118aec6ba427f65efdb/paso26-pg17.trx), [PG18.3](../artifacts/security-related-pg18-00b62d5d82774e10ad035991f44a692b/paso26.trx) |
| Migración 22 y conservación | Respaldo restaurado; datos anteriores conservados; tablas nuevas vacías | [Conteos](../artifacts/security-holds/after-counts.json) |
| HTTPS Iteración 23 | health/OpenAPI/Swagger 200; 67 operaciones revisadas con anónimos 401 | [Resultados](../artifacts/security-http-74e8f27e21c54091a9b36241af0a0c56/results.json) |
| Selección final Iteración 22 — PG17 / PG18.3 | 280/280 por motor; 43 nuevas incluidas, 237 existentes; sin fallidas/omitidas | [PG17](../artifacts/revocation-related-pg17-2a3f8b3c43a2477b8a0942ba0b897936/paso26-pg17.trx), [PG18.3](../artifacts/revocation-related-pg18-c358fb522f014cc796a44f3683e8104e/paso26.trx) |
| Migración 21, respaldo y conservación | Respaldo restaurado; datos previos idénticos; nueva tabla vacía | [Conteos](../artifacts/dependency-revocations/after-counts.json), [restauración](../artifacts/dependency-revocations/restore-verified.txt) |
| HTTPS Iteración 22 | health/OpenAPI/Swagger 200; 62 operaciones comprobadas con anónimos 401 | [Resultados](../artifacts/revocation-http-25d3cea699e542e4862424f6f7a35978/results.json) |
| Selección final Iteración 21 — PG17 / PG18.3 | 118/118 por motor; 27 nuevas incluidas y 91 existentes; sin fallidas/omitidas | [PG17](../artifacts/dependency-readiness-pg17-7aa52980a9924adda338c57ed26b1036/paso26-pg17.trx), [PG18.3](../artifacts/dependency-readiness-pg18-920c0ea56fe046c4a5e02f9a9d96b996/paso26.trx) |
| HTTPS Iteración 21 | health/OpenAPI/Swagger 200; 60 operaciones comprobadas con anónimos 401 | [Resultados](../artifacts/dependency-readiness-http-ca3b712512ec44eaad3e196932915983/results.json) |
| Datos locales Iteración 21 | Conteos y 20 IDs de migración idénticos; sin migraciones nuevas | [Comparación](../artifacts/dependency-readiness/unchanged.txt) |
| Global inicial Iteración 20 — PG17 / PG18.3 | 767/768 por motor; único fallo de fixture TRUNCATE corregido | [PG17](../artifacts/dependency-full-pg17-9ca443f12ca648b3a9694cf5a98e18c2/paso26-pg17.trx), [PG18.3](../artifacts/dependency-full-pg18-d1527556f5594b429a0d753d27af0929/paso26.trx) |
| Final enfocada Iteración 20 — PG17 / PG18.3 | 54/54 por motor: 51 de excepciones y tres de historial; sin fallidas/omitidas | [PG17](../artifacts/dependency-final-pg17-9755baca29034b7a838365203bc023dd/paso26-pg17.trx), [PG18.3](../artifacts/dependency-final-pg18-3b4765896f184c8a8e61184cd4aa02c4/paso26.trx) |
| Migración 20 y conservación | Conteos previos idénticos; nueva tabla vacía; respaldo restaurado | [Conteos](../artifacts/dependency-exceptions-dev-66f182a79e0848bf90c3ea7d70cff43b/after-counts.json), [restauración](../artifacts/dependency-exceptions-dev-66f182a79e0848bf90c3ea7d70cff43b/restore-verified.txt) |
| HTTPS Iteración 20 | health/OpenAPI/Swagger 200; 59 operaciones comprobadas con anónimos 401 | [Resultados](../artifacts/dependency-exceptions-http-1c9f40f709924ce48852cbbff564ec8b/results.json) |
| Regresión global histórica PG17 (Iteración 18) | 678/678, cero fallidas/omitidas | [TRX PG17](../artifacts/skip-full-pg17-f9235989e33e4cef92d0a4899da01781/paso26-pg17.trx) |
| Regresión global histórica PG18.3 (Iteración 18) | 678/678, cero fallidas/omitidas | [TRX PG18](../artifacts/skip-full-pg18-48cc773d90034ad6a04cf7430d4a3ba4/paso26.trx) |
| Bloque de omisiones | 38/38 enfocadas, incluidas en la regresión completa | [TRX enfocado](../artifacts/skip-focused-pg17-5b04e336c13e40a6b8e88fd8d4b0a6e8/paso26-pg17.trx) |
| Selección relacionada Iteración 19 — PG17 | 182/182, incluye 40 nuevas; no es la regresión global | [TRX PG17](../artifacts/readiness-relevant-pg17-f89282ee88b24bf38c387f06161512ea/paso26-pg17.trx) |
| Selección relacionada Iteración 19 — PG18.3 | 182/182, incluye 40 nuevas; no es la regresión global | [TRX PG18](../artifacts/readiness-relevant-pg18-81e1b877e37e427f962187f3dbba0981/paso26.trx) |
| Contexto local Iteración 19 | Conteos e IDs de 19 migraciones sin cambios; sin migración nueva | [Comparación](../artifacts/readiness-dev-checks-1bfa35949e7146e48e9cdcc95f0d35ed/unchanged.txt) |
| Compilación / modelo | Release limpio, EF sin diferencias pendientes | Guía 18 y artifacts |
| HTTPS local Iteración 19 | health/OpenAPI/Swagger 200; 56 operaciones revisadas con anónimos 401 y enums textuales | [Resultados](../artifacts/readiness-http-baed39f398034661942180f29e3ef412/results.json) |
| Esquema de omisiones | 20 columnas, 4 FK, 4 triggers nuevos (2 diferidos); controles anteriores conservados | [SQL verificado](../artifacts/skip-authorizations-dev-3a89b0ccac274e988e116758c987e029/schema-verified.txt) |
| Conservación de datos | Conteos anteriores idénticos; 19 migraciones y cero omisiones nuevas | [Conteos](../artifacts/skip-authorizations-dev-3a89b0ccac274e988e116758c987e029/after-counts.json) |
| Respaldo recuperable | Dump restaurado en PostgreSQL aislado; conteos/historial coincidentes | [Restauración](../artifacts/skip-authorizations-dev-3a89b0ccac274e988e116758c987e029/restore-verified.txt) |

En Iteración 20, la regresión global no se repitió después de corregir el fixture y añadir la comprobación SQL de organización activa. La matriz global más enfocada acredita 769 IDs distintos aprobados por motor; no es una ejecución global final de 769/769. Ver §16.

Las 38 enfocadas históricas se incluyen en las 678, no se suman. Las 56 operaciones HTTP de Iteración 19 son el conjunto revisado,
no el total de endpoints de la API. health es estático y no certifica PostgreSQL/Ollama.
Los flujos autenticados/CSRF se probaron en bases temporales; en openirp_dev solo se hicieron comprobaciones anónimas.

### Último respaldo previo a ExecutionSecurityHolds

- Archivo: artifacts/security-holds/openirp_dev-before-security-holds.dump.
- SHA256: 6FF919DC56211F8ABA78B72CA49C39C2358EE4251F62A27578D34635F807AF21.
- Estado previo a migración 22, restaurado en PostgreSQL aislado y cotejado. Sin FILE registrados; futuros respaldos con archivos reales deben incluir el directorio privado.
- SQL, verificación de esquema, matrices, conteos y scripts en artifacts/security-holds y las carpetas de resultados enlazadas.

Esta bitácora no copia código, TRX ni dump. artifacts queda fuera de Git; preservar sus archivos locales
y el código pendiente por separado. Las entradas de §10–19 mantienen la secuencia histórica.

## 8. Estado de Git y trabajo local por conservar

Último HEAD local comprobado:

- 9cec6a075c918ca88bf02fdda6b8ad750c8cf4a3.
- Mensaje: feat(api): completar evaluaciones, playbooks y evidencias privadas.
- Rama: feature/paso26-identidad-organizaciones, con seguimiento de origin de la misma rama.
- El commit/push de API de iteraciones 7–17 se verificó en la petición anterior.
- Swagger e iteraciones 18–23 quedan locales, junto con frontend/configuraciones/documentación mixta preservados.
- En esta continuación no se hizo commit, push, merge ni fetch. La captura de §11 es histórica.

Conservar especialmente:

- Contratos, entidades y servicios de contexto, criticidad, severidad y ciclo de vida.
- Módulo de playbooks: borradores, publicación, versiones, revisión, retirada y clonación.
- Ejecuciones, tareas y STEP_APPROVAL.
- Mapeos, migraciones, Designer y snapshot de EF; no guardar solamente los servicios y olvidar el esquema.
- Pruebas integradas, ajustes de fixtures, script de permisos runtime y documentación.
- Frontend, su guía y su script de arranque; se preservaron como trabajo local existente.

Las entradas cronológicas distinguen el commit/push previo de los cambios locales posteriores; conservar también los archivos sin seguimiento.

## 9. Secuencia para retomar el trabajo

1. Abrir D:\Desarrollos\OpenIRP y leer esta bitácora, el documento maestro y las guías de Iteración 16–19.
2. Revisar git status, rama y posibles cambios realizados después de esta captura; preservar todo el trabajo existente.
3. Para descubrimiento de código, preferir codebase-memory-mcp sobre búsquedas de archivos; proyecto del grafo: D-Desarrollos-OpenIRP. Reindexar si falta o está desactualizado.
4. Confirmar SDK, Docker, binding/base/propietario e historial actual de migraciones. No suponer que la base continúa vacía.
5. Continuar con alternativas verificadas, capacidades y continuidad por seguridad según el plan; conservar FILE privado, selección activa exacta y recuperación ya implementados. No habilitar RESTRICTED ni purga por inferencia.
6. Añadir verificaciones de aislamiento entre organizaciones/incidentes/tareas, permisos, historial, rollback y carreras según los cambios.
7. Ejecutar compilación y pruebas en bases temporales. Para modificar openirp_dev, comprobar y restaurar un respaldo antes de aplicar la migración nueva.
8. Actualizar plan maestro, guía de la nueva entrega y esta bitácora con resultados reales, rutas de evidencia y bloqueantes.
9. Commit/push únicamente cuando se solicite; no sobrescribir cambios ni hacer merge remoto por inferencia.

Comandos habituales desde la raíz del repositorio:

```powershell
git status --short --branch
dotnet --version
docker ps --format '{{.Names}} {{.Ports}}'

# Pruebas sobre instancias temporales propias:
./ci/Test-IdentityDocker.ps1
./ci/Test-IdentityLocal.ps1

# Arranque manual local, cuando corresponda:
./ci/Start-LocalApi.ps1
```

No ejecutar las pruebas integradas sobre la base habitual. El arranque de API no aplica migraciones ni crea cuentas automáticamente. Revisar los parámetros y prerrequisitos de las guías antes de usar los scripts.

## 10. Entradas cronológicas de cierre

| Fecha | Avance / incidencia | Resultado |
|---|---|---|
| 2026-10-22 a 2026-10-23 | Identidad, auditoría, inventario, incidentes y evaluación contextual/CIA/severidad; avances detallados en guías y plan maestro. | Base de los módulos actuales; parte del trabajo quedó en el commit 0c70669 y las entregas posteriores permanecieron locales. |
| 2026-10-23 a 2026-10-24 | Ciclo de vida, catálogo/versionado/publicación/clonación de playbooks, ejecuciones y tareas. | Entregas incrementales verificadas; límites pendientes documentados. |
| 2026-10-24 | Implementación de STEP_APPROVAL y regresión completa PostgreSQL 18.3. | 565/565; bloqueo del motor Docker impidió entonces PG17 y la migración habitual. |
| 2026-10-25 | Docker recuperado; PG17, respaldo/restauración, migración local StepApprovals, esquema y HTTPS. | 565/565 PG17, 16 migraciones, conteos conservados, procesos propios detenidos; bloqueo anterior resuelto. |
| 2026-10-25 | Creación de esta bitácora por solicitud del usuario. | Consolidación de avances, decisiones, evidencias, trabajo sin commit y pendientes. Sin cambios funcionales, pruebas adicionales ni commit/push por esta operación documental. |

Para próximas entradas registrar fecha, alcance, archivos relevantes, decisiones del usuario, migraciones/destino, pruebas y evidencias, respaldos, estado Git, bloqueantes y siguiente paso. Conservar el historial de decisiones; marcar expresamente cuándo un bloqueo queda resuelto.

## 11. Captura de estado Git al crear la bitácora

Captura obtenida antes de crear este archivo; no incluye la propia bitácora nueva. Es una referencia histórica, no una lista para descartar archivos ni una lectura permanente del repositorio.

```text
## feature/paso26-identidad-organizaciones...origin/feature/paso26-identidad-organizaciones
 M OpenIRP_Documentacion/Iteracion6_Evaluacion_CIA.md
 M OpenIRP_Documentacion/OpenIRP_Planificacion_Integral.md
 M OpenIRP_Documentacion/Propuesta_Regla_Severidad_v1.md
 M README.md
 M apps/api/OpenIRP.Api/Program.cs
 M apps/api/OpenIRP.Application/Incidents/IIncidentService.cs
 M apps/api/OpenIRP.Domain/Entities/IncidentEntities.cs
 M apps/api/OpenIRP.Infrastructure/Incidents/IncidentService.cs
 M apps/api/OpenIRP.Infrastructure/Persistence/Configurations/IncidentMapping.cs
 M apps/api/OpenIRP.Infrastructure/Persistence/Migrations/OpenIrpDbContextModelSnapshot.cs
 M apps/api/OpenIRP.Infrastructure/Persistence/OpenIrpDbContext.cs
 M apps/api/OpenIRP.IntegrationTests/AdministrativeAuditTests.cs
 M apps/api/OpenIRP.IntegrationTests/IncidentAssetTests.cs
 M apps/api/OpenIRP.IntegrationTests/IncidentCapabilityTests.cs
 M apps/api/OpenIRP.IntegrationTests/IncidentImpactTests.cs
 M ci/grant-runtime.sql
?? OpenIRP_Documentacion/Frontend_Local.md
?? OpenIRP_Documentacion/Iteracion10_Ciclo_Vida_Cierre.md
?? OpenIRP_Documentacion/Iteracion11_Catalogo_Borradores_Playbooks.md
?? OpenIRP_Documentacion/Iteracion12_Publicacion_Versiones_Playbooks.md
?? OpenIRP_Documentacion/Iteracion13_Clonacion_Playbooks.md
?? OpenIRP_Documentacion/Iteracion14_Ejecuciones_Tareas.md
?? OpenIRP_Documentacion/Iteracion15_Aprobaciones_Pasos.md
?? OpenIRP_Documentacion/Iteracion7_Contexto_Incidente.md
?? OpenIRP_Documentacion/Iteracion8_Criticidad_Incidente.md
?? OpenIRP_Documentacion/Iteracion9_Severidad_Incidente.md
?? apps/api/OpenIRP.Api/Controllers/ExecutionsController.cs
?? apps/api/OpenIRP.Api/Controllers/IncidentContextController.cs
?? apps/api/OpenIRP.Api/Controllers/IncidentCriticalityController.cs
?? apps/api/OpenIRP.Api/Controllers/IncidentSeverityController.cs
?? apps/api/OpenIRP.Api/Controllers/IncidentWorkflowController.cs
?? apps/api/OpenIRP.Api/Controllers/PlaybookCloneControllers.cs
?? apps/api/OpenIRP.Api/Controllers/PlaybookDraftControllers.cs
?? apps/api/OpenIRP.Api/Controllers/PlaybookPublicationControllers.cs
?? apps/api/OpenIRP.Application/Executions/
?? apps/api/OpenIRP.Application/Incidents/IncidentContextContracts.cs
?? apps/api/OpenIRP.Application/Incidents/IncidentCriticalityContracts.cs
?? apps/api/OpenIRP.Application/Incidents/IncidentSeverityContracts.cs
?? apps/api/OpenIRP.Application/Incidents/IncidentWorkflowContracts.cs
?? apps/api/OpenIRP.Application/Playbooks/
?? apps/api/OpenIRP.Domain/Entities/ExecutionEntities.cs
?? apps/api/OpenIRP.Domain/Entities/IncidentContext.cs
?? apps/api/OpenIRP.Domain/Entities/IncidentCriticality.cs
?? apps/api/OpenIRP.Domain/Entities/IncidentSeverity.cs
?? apps/api/OpenIRP.Domain/Entities/IncidentWorkflow.cs
?? apps/api/OpenIRP.Domain/Entities/PlaybookEntities.cs
?? apps/api/OpenIRP.Domain/Entities/PlaybookReview.cs
?? apps/api/OpenIRP.Domain/Entities/StepApproval.cs
?? apps/api/OpenIRP.Domain/Playbooks/
?? apps/api/OpenIRP.Domain/Severity/
?? apps/api/OpenIRP.Infrastructure/Incidents/IncidentContextService.cs
?? apps/api/OpenIRP.Infrastructure/Incidents/IncidentCriticalityService.cs
?? apps/api/OpenIRP.Infrastructure/Incidents/IncidentExecutionReadService.cs
?? apps/api/OpenIRP.Infrastructure/Incidents/IncidentExecutionService.cs
?? apps/api/OpenIRP.Infrastructure/Incidents/IncidentSeverityService.cs
?? apps/api/OpenIRP.Infrastructure/Incidents/IncidentStepApprovalService.cs
?? apps/api/OpenIRP.Infrastructure/Incidents/IncidentWorkflowService.cs
?? apps/api/OpenIRP.Infrastructure/Persistence/Configurations/ExecutionMapping.cs
?? apps/api/OpenIRP.Infrastructure/Persistence/Configurations/PlaybookMapping.cs
?? apps/api/OpenIRP.Infrastructure/Persistence/Configurations/StepApprovalMapping.cs
?? apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261023141918_IncidentContextAssessment.Designer.cs
?? apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261023141918_IncidentContextAssessment.cs
?? apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261023200334_IncidentCriticalityAssessment.Designer.cs
?? apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261023200334_IncidentCriticalityAssessment.cs
?? apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261023202856_IncidentSeverityClassification.Designer.cs
?? apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261023202856_IncidentSeverityClassification.cs
?? apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261023220910_IncidentLifecycleClosure.Designer.cs
?? apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261023220910_IncidentLifecycleClosure.cs
?? apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261023231051_PlaybookDraftCatalog.Designer.cs
?? apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261023231051_PlaybookDraftCatalog.cs
?? apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261024010838_PlaybookPublication.Designer.cs
?? apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261024010838_PlaybookPublication.cs
?? apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261024021251_PlaybookCloning.Designer.cs
?? apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261024021251_PlaybookCloning.cs
?? apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261024034804_InitialPlaybookExecutions.Designer.cs
?? apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261024034804_InitialPlaybookExecutions.cs
?? apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261024224015_StepApprovals.Designer.cs
?? apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261024224015_StepApprovals.cs
?? apps/api/OpenIRP.Infrastructure/Playbooks/
?? apps/api/OpenIRP.IntegrationTests/ExecutionPersistenceTests.cs
?? apps/api/OpenIRP.IntegrationTests/ExecutionTests.cs
?? apps/api/OpenIRP.IntegrationTests/IncidentContextTests.cs
?? apps/api/OpenIRP.IntegrationTests/IncidentCriticalityTests.cs
?? apps/api/OpenIRP.IntegrationTests/IncidentSeverityTests.cs
?? apps/api/OpenIRP.IntegrationTests/IncidentWorkflowRuleTests.cs
?? apps/api/OpenIRP.IntegrationTests/IncidentWorkflowTests.cs
?? apps/api/OpenIRP.IntegrationTests/LegacyIncidentFixture.cs
?? apps/api/OpenIRP.IntegrationTests/PlaybookClonePersistenceTests.cs
?? apps/api/OpenIRP.IntegrationTests/PlaybookCloneTests.cs
?? apps/api/OpenIRP.IntegrationTests/PlaybookDependencyGraphTests.cs
?? apps/api/OpenIRP.IntegrationTests/PlaybookDraftPersistenceTests.cs
?? apps/api/OpenIRP.IntegrationTests/PlaybookDraftTests.cs
?? apps/api/OpenIRP.IntegrationTests/PlaybookPublicationPersistenceTests.cs
?? apps/api/OpenIRP.IntegrationTests/PlaybookPublicationTests.cs
?? apps/api/OpenIRP.IntegrationTests/SeverityRuleV1Tests.cs
?? apps/api/OpenIRP.IntegrationTests/StepApprovalTests.cs
?? apps/web/
?? ci/Start-LocalWeb.ps1
```

## 12. Actualización 2026-10-26 — Iteración 16

Se incorporaron evidencias TEXT/REFERENCE de incidente/tarea y selección explícita de completionEvidenceId para finalizar tareas. [Guía completa y archivos editados](Iteracion16_Evidencias_Incidente_Tarea.md).

- Registro por ORG_ADMIN/ANALYST activos; consulta por miembros activos autorizados; retirada solo ORG_ADMIN. PLATFORM_ADMIN no obtiene acceso privado implícito.
- Contenido/ámbito inmutables. FILE y RESTRICTED continúan rechazados.
- Registrar actualiza la revisión/historia del incidente. Retirar usa la revisión propia de evidencia sin modificar incidente/ejecución, incluso después del cierre.
- La retirada oculta título/contenido en la API, sin purgar la base ni respaldos. Una tarea completada conserva el identificador del soporte histórico; no se reabre por una retirada posterior.
- La finalización exige evidencia activa de la misma tarea exacta cuando corresponde. Se conservan aprobación, responsable, dependencias y fuente utilizable; una evidencia general del incidente no sustituye el vínculo de tarea.
- **114/114 enfocadas**, incluidas 39 nuevas, y **604/604 completas en PostgreSQL 17 y 18.3**, cero fallidas/omitidas. Release sin errores/advertencias y EF sin diferencias.
- Respaldo completo restaurado/verificado antes de aplicar 20261026163753_IncidentTextReferenceEvidence a openirp_dev. Datos previos conservados, **17 migraciones**, tablas/referencias nuevas vacías.
- Esquema verificado: dos tablas de 17+10 columnas, siete FK nuevas, cinco checks nuevos y seis triggers, dos diferidos; selección nullable sin DEFAULT en tarea.
- Evidencias: artifacts/incident-evidence-dev-8e65fcff819d4d48a3494a1a36cf1e21/. Dump openirp_dev-before-incident-evidence.dump, SHA256 F329B7403FECCDB972C3D46841692B0435D4F98481188F8D00CF3C6351AD55A3.
- TRX: artifacts/evidence-full-pg17-d0080f5384db4b76be53563ebace8217/paso26-pg17.trx y artifacts/evidence-full-pg18-c254354372ca4bb79163d720bd582781/paso26.trx.
- HTTPS: health/OpenAPI 200 y 46 operaciones del conjunto comprobado con anónimos 401. Evidencia artifacts/incident-evidence-http-a250e6f8bf5c46578b40decbcb64b1dd/. Flujos autenticados/CSRF en bases temporales.
- Fuente y frontend anteriores preservados. Nuevos archivos principales: IncidentEvidence.cs, IEvidenceService.cs, IncidentEvidenceService.cs, EvidenceMapping.cs, EvidenceController.cs, EvidenceTests.cs y migración/Designer. Se ampliaron contratos/servicio de ejecución, DbContext/DI, permisos runtime, check de acciones y fixtures de downgrade/TRUNCATE.
- Sin commit/push/merge nuevos. HEAD sigue en 0c706696499ae3a935c69aee5cdf135d541f5488. La captura Git de §11 es histórica del 2026-10-25 y no enumera los archivos nuevos de esta entrega.

Siguiente alcance: ampliar evidencias a archivos privados con estados de almacenamiento/recuperación y políticas de acceso/retención; continúan omisiones/excepciones, continuidad por seguridad, cierre integrado e IA supervisada. No se detectaron bloqueantes de entorno al terminar esta entrega.

## 13. Actualización 2026-10-26 — Iteración 17

[Guía completa de archivos privados y archivos editados](Iteracion17_Archivos_Privados_Evidencia.md).

El usuario aprobó PDF/PNG/JPEG/TXT hasta 10 MB y conservación privada de retiradas sin borrado automático. Se implementaron carga multipart, SHA-256, metadata inmutable, descarga autorizada y recuperación ORG_ADMIN con estados PENDING/AVAILABLE/FAILED/QUARANTINED.

- Retirada bloquea nuevas lecturas y finalizaciones, conserva los bytes y las referencias históricas. Las lecturas ya autorizadas pueden terminar.
- Se valida extensión/MIME, cabeceras y UTF-8; no se presenta como antivirus ni parser completo.
- El resultado incierto de un commit conserva staging. Se probó la recuperación después de perder una confirmación ya comprometida.
- Directorio privado dedicado, ACL de usuario/SYSTEM en Windows; UUID físico independiente del nombre. .openirp-data excluido de Git y grafos; Startup local configura RootPath sin crear cuentas ni datos automáticamente.
- Regresión completa **639/639 por motor (PG17 y PG18.3)**. Tras el ajuste final de limpieza se repitieron **36/36 casos finales de archivos por motor**. Se solapan; no sumar sus totales ni describirlo como una única ejecución de 640 pruebas.
- TRX completos: artifacts/private-files-full-pg17-efbb24f1dbc84a40b08f240b64774c64/paso26-pg17.trx y artifacts/private-files-full-pg18-4070a5b5217d496bb5a72a680dab2371/paso26.trx.
- TRX finales: artifacts/private-files-final-pg17-732290fc9e9a4325840f004a116de253/paso26-pg17.trx y artifacts/private-files-final-pg18-884921537fed4b4fbd90e26721e6c83c/paso26.trx.
- Release sin errores/advertencias; EF sin diferencias. Respaldo completo restaurado/verificado antes de aplicar 20261026183812_PrivateEvidenceFiles a openirp_dev: **18 migraciones**, datos previos conservados; archivos/eventos nuevos vacíos, sin cuentas ni archivos ficticios.
- Evidencia/SQL/conteos/esquema: artifacts/private-evidence-files-dev-133ea5fec46d43a787de894c07bbb988/. Dump openirp_dev-before-private-evidence-files.dump, SHA256 15493529627CDAEB35B35E4D20E0075CEBAE4A150F4C08812B4924243B66F3C1. No había FILE registrados: un respaldo futuro con archivos reales debe incluir también el directorio privado.
- HTTPS: health/OpenAPI 200 y 51 operaciones comprobadas, con anónimos 401. Evidencia artifacts/private-evidence-files-http-591d18f7b4ae4a1792534332093a85c6/. Negocio autenticado/multipart/CSRF probado sobre bases y directorios temporales.
- Graphify AST/HTML local actualizado (2347 nodos, 6184 relaciones, 178 comunidades), sin llamadas de IA externas; MCP reindexado. Se preservaron AGENTS.md, configuraciones locales y frontend.
- Archivo principales nuevos: EvidenceFile.cs, IFileEvidenceService.cs, PrivateEvidenceStore.cs, IncidentFileEvidenceService.cs, EvidenceFileMapping.cs, EvidenceFilesController.cs, EvidenceFileTests.cs, migración/Designer. Integraciones en evidencia/ejecución/DI/DbContext, runtime grants, arranque y exclusiones.
- No se hizo commit, push ni merge; HEAD permanece 0c706696499ae3a935c69aee5cdf135d541f5488. Las capturas previas son históricas.

Siguen pendientes RESTRICTED, retención/purga física y conciliación de staging desconocido, revisión de cuarentena/antimalware, omisiones/excepciones, continuidad por seguridad, cierre integrado e IA supervisada.


## 14. Actualización 2026-10-26 — Iteración 18 y Swagger

[Guía de omisiones](Iteracion18_Omisiones_Autorizadas.md) y [Swagger local](Swagger_Local.md).
La API de iteraciones 7–17 quedó publicada en 9cec6a0; los cambios nuevos no se han publicado.
Se mantienen intactos frontend y configuraciones locales.

El responsable solicita con impacto/riesgo; ORG_ADMIN decide. Toda omisión de esta primera subfase requiere
administrador, incluso en pasos opcionales. La aprobación propia requiere excepción explícita justificada.
La solicitud se liga a revisiones de incidente/ejecución y dependencias calculadas por servidor; cualquier escritura
intermedia de esos agregados invalida la decisión pendiente.

Aprobar confirma SKIPPED e historias/auditoría conjuntamente. No se reutiliza STEP_APPROVAL, no se selecciona
evidencia de una acción no realizada, ni se satisfacen dependencias por omitir. Cada omisión posterior
requiere solicitud/decisión independiente. Ejecución terminada no equivale a incidente resuelto o cerrado.

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

Siguen pendientes alternativas/excepciones para ejecutar dependientes, capacidades/suficiencia,
continuidad por security_hold_id y cierre ampliado de riesgos. Se conservan los pendientes de RESTRICTED,
archivos/retención/antimalware e IA supervisada.


## 15. Actualización 2026-10-26 — Iteración 19

[Guía de cobertura declarada](Iteracion19_Cobertura_Capacidades_Ejecucion.md).
Dos GET comparan requisitos de la versión ligada a la ejecución con capacidades efectivas actuales del incidente.
La tarea incorpora requisitos globales y de su paso; no elimina referencias duplicadas entre ámbitos distintos.
Se conserva Unknown explícito, herencia, catálogo inactivo, REQUIRED/RECOMMENDED, notas, limitaciones y revisiones.

RequiresHumanReview es siempre true. El resultado no autoriza tareas ni sustituye dependencias, evidencia,
STEP_APPROVAL, SKIP_AUTHORIZATION o revisión técnica. Las capacidades mostradas en ejecuciones históricas son actuales;
no se atribuyen retrospectivamente a acciones antiguas. Lectura RepeatableRead y resolución en lote.

La configuración HTTP JSON se alinea con MVC para que Swagger describa enums textuales.
No hay cambios de esquema, migración nueva ni nuevos permisos SQL. Se conserva la base habitual en 19 migraciones.

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

Nuevos archivos: CapabilityRequirementEvaluation.cs, CapabilityReadinessContracts.cs,
IncidentCapabilityReadinessService.cs, CapabilityReadinessTests.cs y CapabilityRequirementEvaluationTests.cs.
Integraciones en interfaz/controlador de ejecuciones, Program.cs y personalización opcional del fixture ExecutionSeed.
Se preservaron Swagger/Iteración 18 y frontend; sin commit, push ni merge nuevos.

Continúan alternativas/excepciones de dependencias, validación de suficiencia técnica, continuidad por seguridad,
cierre de riesgos, RESTRICTED/operación de archivos e IA supervisada.


## 16. Actualización 2026-10-26 — Iteración 20

[Guía de excepciones individuales de dependencias](Iteracion20_Excepciones_Dependencias.md).

Responsable solicita; ORG_ADMIN decide por vínculo hacia un predecesor SKIPPED con autorización original.
Exige impacto/riesgo y contexto versionado del incidente/capacidades; aprobación propia requiere excepción explícita.
No modifica el DAG ni el estado SKIPPED. Aprobaciones/evidencias y decisiones por otros vínculos siguen siendo independientes.

Autorización pendiente exige revisiones inmediatas; la aprobada se revalida al avanzar y caduca por reasignación
o cambios de contexto relevante. El SQL comprueba organización activa, participantes y estado final diferido.
No implementa acciones alternativas ni continuidad sobre versiones retiradas.

- Compilación Release final: cero errores y advertencias. EF confirma que no hay diferencias pendientes de modelo.
- Regresión global inicial: **767/768 en PostgreSQL 17 y 767/768 en PostgreSQL 18.3**, sin omitidas. El único fallo fue SkipHistoryIsAppendOnlyInSqlAndEf(operation: "truncate"): la nueva FK adelantaba el error 0A000 al 42501 esperado. Se corrigió el fixture para truncar ambas tablas relacionadas y comprobar el guard append-only.
- Verificación final: **54/54 en cada motor**, sin fallidas/omitidas: **51 casos de excepciones** y tres variantes de historial de omisiones. Incluye organización activa en SQL, aislamiento, roles/CSRF, contexto vigente, rollback, concurrencia, múltiples dependencias, aprobación de paso, evidencia y permisos runtime.
- No se repitió toda la suite después del ajuste. Las ejecuciones global y enfocada acreditan 769 IDs de caso distintos aprobados por motor; no equivalen a una ejecución global final de 769/769. Se usan IDs TRX porque dos nombres de presentación truncados coinciden.
- TRX globales: [PG17](../artifacts/dependency-full-pg17-9ca443f12ca648b3a9694cf5a98e18c2/paso26-pg17.trx), [PG18.3](../artifacts/dependency-full-pg18-d1527556f5594b429a0d753d27af0929/paso26.trx). TRX finales: [PG17](../artifacts/dependency-final-pg17-9755baca29034b7a838365203bc023dd/paso26-pg17.trx), [PG18.3](../artifacts/dependency-final-pg18-3b4765896f184c8a8e61184cd4aa02c4/paso26.trx).
- Matrices verificables con hashes: [PG17](../artifacts/dependency-exceptions-dev-66f182a79e0848bf90c3ea7d70cff43b/validation-matrix-pg17.json), [PG18.3](../artifacts/dependency-exceptions-dev-66f182a79e0848bf90c3ea7d70cff43b/validation-matrix-pg18.json).
- Respaldo completo anterior a la migración 20 restaurado en PostgreSQL aislado, con conteos e historial coincidentes. SHA256: 22232280AE668ADD92F72B5ACE17F5C2A3C3F6E6320866D63EEB28C4255E9921. [Restauración](../artifacts/dependency-exceptions-dev-66f182a79e0848bf90c3ea7d70cff43b/restore-verified.txt).
- Migración **20261026224629_DependencyExceptions aplicada únicamente a openirp_dev local, 127.0.0.1:5433**. Se conservan los conteos previos: ocho capacidades, tres categorías, identidad/negocio/historiales vacíos; dependency_exceptions tiene cero filas. Historial de 20 migraciones. [Conteos](../artifacts/dependency-exceptions-dev-66f182a79e0848bf90c3ea7d70cff43b/after-counts.json).
- SQL incremental final revisado; esquema comprobado: 23 columnas, siete FK, cuatro triggers nuevos (dos diferidos). Conserva guards de STEP_APPROVAL, FILE y organización activa. [SQL final](../artifacts/dependency-exceptions-dev-66f182a79e0848bf90c3ea7d70cff43b/DependencyExceptions-final.sql), [comprobación](../artifacts/dependency-exceptions-dev-66f182a79e0848bf90c3ea7d70cff43b/Verify-DependencySchema.sql).
- HTTPS posterior a la migración: health/OpenAPI/Swagger 200; **59 operaciones del conjunto revisado** responden 401 sin sesión. Las tres rutas nuevas y sus contratos figuran en OpenAPI. No es el total de endpoints; health es estático. [Resultados](../artifacts/dependency-exceptions-http-1c9f40f709924ce48852cbbff564ec8b/results.json).
- Las pruebas autenticadas utilizaron bases temporales; no se crearon cuentas ni datos de negocio en la base habitual. Docker se reinició al detectarse detenido; PostgreSQL/Ollama habituales vuelven a estar disponibles. API y procesos propios de prueba se cerraron.
- Grafo MCP reindexado: 3030 nodos/8997 relaciones. Graphify AST/HTML: 2636 nodos/7179 relaciones/193 comunidades, sin IA externa.
- Sin commit, push, merge ni cambios remotos nuevos; HEAD permanece 9cec6a075c918ca88bf02fdda6b8ad750c8cf4a3.


Se preservaron frontend, Swagger y cambios anteriores. Nuevos contratos/entidad/mapeo/servicio/pruebas y
migración DependencyExceptions; se integran controladores, predicados de avance/STEP_APPROVAL, DbContext,
permisos runtime y fixtures de TRUNCATE.
Sin commit, push ni merge nuevos. Continúan continuidad por security_hold_id, alternativas verificadas,
revocación específica, cierre de riesgos, RESTRICTED/operación de archivos e IA supervisada.


## 17. Actualización 2026-10-26 — Iteración 21

[Guía de vigencia de dependencias](Iteracion21_Vigencia_Dependencias.md).

Consulta tipada por tarea: satisfacción actual separada de la última solicitud/decisión histórica.
Una aprobación caducada no satisface el vínculo. Lectura RepeatableRead, consulta por lote y predicado SQL compartido.
Sin cambios de escritura, esquema o migraciones.

- Release final con .NET SDK 10.0.403: cero errores y advertencias; EF sin diferencias pendientes de modelo.
- Selección final de módulos relacionados: **118/118 en PostgreSQL 17 y 118/118 en PostgreSQL 18.3**, sin fallidas/omitidas: **27 casos nuevos** de esta consulta y 91 existentes de excepciones/capacidades. Las 27 están incluidas en las 118; no se suman.
- Evidencias finales: [TRX PG17](../artifacts/dependency-readiness-pg17-7aa52980a9924adda338c57ed26b1036/paso26-pg17.trx), [TRX PG18.3](../artifacts/dependency-readiness-pg18-920c0ea56fe046c4a5e02f9a9d96b996/paso26.trx); [matriz y hashes](../artifacts/dependency-readiness/validation.json).
- Cubre estados de predecesores, solicitud/rechazo/aprobación, caducidad, STEP_APPROVAL independiente, múltiples aristas, aislamiento real entre organizaciones/ejecuciones, roles/lockout, HTTP/no-store, cuenta SQL limitada y cambio concurrente de capacidades entre lecturas.
- La primera ejecución obtuvo 100/118 por motor: 17 fallos por alias PascalCase incompatibles con el mapeo snake_case y uno por precondición de capacidad del fixture. Se corrigieron ambos. La segunda ejecución obtuvo 117/118 por motor: el fixture de rechazo enviaba indicadores de autoaprobación y fue corregido. Después se repitieron los 118 casos completos de la selección, con los resultados finales indicados. Los TRX iniciales se conservan en artifacts/dependency-readiness-pg17-ffba734ba0aa472893c45218544c247a y artifacts/dependency-readiness-pg18-bd9d309883794c5c8f4840a76873a252.
- No se repitió la regresión global de toda la API; sus resultados históricos y el ajuste de Iteración 20 permanecen documentados por separado.
- HTTPS final: health/OpenAPI/Swagger 200; **60 operaciones del conjunto revisado** responden 401 sin sesión. El GET nuevo y ExecutionDependencySatisfaction figuran en OpenAPI con enum textual. [Resultados](../artifacts/dependency-readiness-http-ca3b712512ec44eaad3e196932915983/results.json). No representa el total de endpoints y health es estático.
- Base habitual openirp_dev en 127.0.0.1:5433 sin cambios: los mismos IDs de 20 migraciones, ocho capacidades/tres categorías y conteos de identidad/negocio/historiales vacíos. Ninguna migración nueva ni datos de ejemplo. [Comparación](../artifacts/dependency-readiness/unchanged.txt), [antes](../artifacts/dependency-readiness/dev-before.json), [después](../artifacts/dependency-readiness/dev-after.json).
- MCP reindexado: 3110 nodos/9242 relaciones. Graphify AST/HTML: 2691 nodos/7363 relaciones/197 comunidades; sin IA externa.
- Procesos propios temporales de pruebas/HTTPS detenidos al cerrar; PostgreSQL/Ollama habituales preservados. Sin commit, push ni merge nuevos; HEAD sigue en 9cec6a075c918ca88bf02fdda6b8ad750c8cf4a3.

Archivos nuevos: DependencyReadinessContracts.cs, IncidentDependencyReadinessService.cs y DependencyReadinessTests.cs. Integraciones en IExecutionService.cs, ExecutionsController.cs, README y documentación maestra. Frontend y trabajo anterior preservados. Continúan revocación específica, alternativas, continuidad por seguridad y cierre de riesgos.


## 18. Actualización 2026-10-27 — Iteración 22

[Guía de revocación de excepciones](Iteracion22_Revocacion_Excepciones.md).

Revocación administrativa por aprobación concreta. Registra historia nueva y bloquea una tarea en curso atómicamente,
sin exigir elegibilidad del responsable para retirar autorización. Exige administrador actual, ámbito, motivo y revisiones.
No habilita continuidad sobre versiones retiradas ni reescribe estados terminales.

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

Archivos e integraciones detallados en la guía; nuevos entidad/mapeo/contrato/servicio/pruebas y migración DependencyExceptionRevocations. Fixtures de TRUNCATE adaptados a la FK nueva. Se preservaron trabajo previo, frontend y configuración. Continúan alternativas verificadas, continuidad por seguridad y cierre integrado de riesgos.


## 19. Actualización 2026-10-27 — Iteración 23

[Guía de bloqueo y continuidad](Iteracion23_Bloqueo_Seguridad_Continuidad.md).

Avisos fuente por versión con ID inmutable; continuidad/cancelación por ORG_ADMIN de cada ejecución.
El aviso nuevo invalida decisiones anteriores. Se preservan controles, versiones y estado histórico de tareas.

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

Archivos nuevos/integraciones detallados en la guía. Continúan alternativas, cierre agregado de riesgos, propagación a derivadas, RESTRICTED/archivos e IA supervisada.


## 20. Actualización 2026-10-27 — Iteración 24

[Guía y archivos](Iteracion24_Preparacion_Cierre.md).


- Release .NET SDK 10.0.403: cero errores y advertencias. EF no detecta diferencias pendientes de modelo.
- **146/146 en PostgreSQL 17 y 146/146 en PostgreSQL 18.3**, sin fallidas/omitidas: **24 casos nuevos** y 122 existentes de ciclo de vida, ejecuciones y bloqueos de seguridad. Las 24 están incluidas en las 146. No se ejecutó la suite global completa de la API.
- Cubre clasificación pendiente, máximo histórico, cierre directo/revisión, autoaprobación, invalidación, responsables y cuentas actuales, roles/aislamiento, agregados de tareas/evidencias, FILE fallidos, historia/revocación de excepciones, bloqueo vigente, runtime SQL limitado, HTTP/no-store y cancelación concurrente con instantánea consistente.
- Primera selección: 126/146 por motor, con 20 fallos por ordenar después de construir el DTO de agregación. Se movió el orden antes de la proyección SQL y se repitieron las 146 completas de cada motor. Los TRX iniciales permanecen en artifacts/closure-related-pg17-5503a8895167446e895cbe375602aa4e y artifacts/closure-related-pg18-5b7111f589fe4486bfafb1acfd84655d.
- [TRX PG17](../artifacts/closure-related-pg17-ecee0469546a46309dbb9b365df4aae7/paso26-pg17.trx), [TRX PG18.3](../artifacts/closure-related-pg18-6db491e5a5c746cf9f83c30a1b297b46/paso26.trx), [matriz con hashes](../artifacts/closure-readiness/validation.json).
- HTTPS Development: health/OpenAPI/Swagger 200; nuevo GET devuelve 401 anónimo y tiene contrato IncidentClosureReadiness tipado. HTTP autenticado y no-store probados en bases temporales. Health es estático; no acredita conectividad a servicios. [Resultados](../artifacts/closure-http-969b607caf2b40718dfc3ef38eaeb985/results.json), [OpenAPI](../artifacts/closure-http-969b607caf2b40718dfc3ef38eaeb985/openapi.json).
- Base habitual openirp_dev en 127.0.0.1:5433: mismos conteos de tablas app/auth y mismos 22 IDs de migración antes/después. Ocho capacidades, tres categorías, sin usuarios ni incidentes. No se aplicaron migraciones ni operaciones de negocio. [Comparación](../artifacts/closure-readiness/dev-unchanged.txt), [antes](../artifacts/closure-readiness/dev-before.json), [después](../artifacts/closure-readiness/dev-after.json). No fue necesario un respaldo nuevo: esta iteración no escribe datos ni cambia esquema en la base habitual; el respaldo restaurado de Iteración 23 sigue documentado.

Se preserva frontend, Swagger y WIP previo. Sin commit, push ni merge nuevos. Continúan integración de riesgos residuales, alternativas verificadas, propagación a derivadas, RESTRICTED, operación de archivos e IA supervisada.

Grafos finales: MCP 3449 nodos/10287 relaciones; Graphify AST/HTML 2947 nodos/8195 relaciones/221 comunidades, sin IA externa. Procesos propios de pruebas y API temporal detenidos; sin listeners propios 55432/55433/7113 al finalizar. PostgreSQL/Ollama habituales preservados. HEAD permanece 9cec6a075c918ca88bf02fdda6b8ad750c8cf4a3.


## 21. Actualización 2026-10-27 — Iteración 25

[Guía, contrato y archivos](Iteracion25_Contexto_Revision_Cierre.md).

Decisión del usuario: evaluación vigente de ORG_ADMIN obligatoria en TODOS los cierres. Esta entrega implementa la huella y precondición opcional; el registro y requisito obligatorio se implementarán en la siguiente subfase. No afirmar que están habilitados.


- .NET SDK 10.0.403, compilación Release sin errores ni advertencias. EF sin cambios pendientes de modelo.
- Selección enfocada inicial: **23/23 en PostgreSQL 17**. [TRX enfocado](../artifacts/closure-context-focused-pg17-4aff12a9793e4717bcb1fa7cf0203a84/paso26-pg17.trx).
- Regresión final relacionada: **169/169 en PostgreSQL 17 y 169/169 en PostgreSQL 18.3**, cero fallidas/omitidas: 23 nuevas incluidas y 146 existentes de preparación/ciclo de vida/cierre, ejecuciones y bloqueos de seguridad. No es la suite global completa de la API.
- [TRX PG17](../artifacts/closure-context-related-pg17-f27581c2e2004d55ab7079ead86c7800/paso26-pg17.trx), [TRX PG18.3](../artifacts/closure-context-related-pg18-6e0579bb13ea4e1387c82bb748208fe1/paso26.trx), [matriz con hashes](../artifacts/closure-context/validation.json).
- Casos nuevos: estabilidad entre lectores, formato, ámbito, ausencia de permiso implícito, escritura con token confirmado en historial; retirada antes de cierre/solicitud/aprobación/rechazo con revisión de incidente intacta; recuperación FILE; alertas posteriores a ejecución terminal; perfiles/catálogos/membresías/categoría; incidentes y organizaciones ajenas; instantánea concurrente y carreras reales con ambos escritores esperando el gate en pg_locks. Las pruebas existentes incluyen lecturas bajo cuenta SQL limitada y revalidación de cuentas/roles.
- HTTPS Development: health/OpenAPI/Swagger 200, GET anónimo 401; contrato de respuesta incluye closureContextToken y los tres contratos de escritura exponen expectedClosureContext opcional. HTTP autenticado y CSRF se prueban en bases temporales. Health es estático. [Resultados](../artifacts/closure-context-http-7ec630e3294940e7931d03f78ff1e6c6/results.json), [OpenAPI](../artifacts/closure-context-http-7ec630e3294940e7931d03f78ff1e6c6/openapi.json).
- openirp_dev en 127.0.0.1:5433 conserva todos los conteos de tablas app/auth/audit y los 22 IDs de migración. Ocho capacidades, tres categorías, sin usuarios ni incidentes. No se ejecutaron migraciones ni operaciones de negocio en ella. No se generó respaldo nuevo para esta entrega sin escrituras; el respaldo restaurado de Iteración 23 sigue documentado. [Antes](../artifacts/closure-context/dev-before.json), [después](../artifacts/closure-context/dev-after.json), [comparación](../artifacts/closure-context/dev-unchanged.txt).
- Grafos actualizados: MCP 3486 nodos/10504 relaciones; Graphify AST/HTML 2990 nodos/8359 relaciones/218 comunidades, sin IA externa.
- Procesos propios temporales de pruebas y API detenidos; servicios habituales preservados. Sin commit, push, merge ni cambios remotos. HEAD se conserva en 9cec6a075c918ca88bf02fdda6b8ad750c8cf4a3.

Se preservan frontend, Swagger, configuración y WIP previo. Continúan evaluación obligatoria aprobada, alternativas verificadas, RESTRICTED, políticas de archivos e IA supervisada.


## 22. Actualización 2026-10-27 — Iteración 26

[Guía, contrato y archivos](Iteracion26_Evaluacion_Riesgo_Cierre.md).

La política aprobada de evaluación ORG_ADMIN en todos los cierres está IMPLEMENTADA. No es solo una huella informativa. Mantiene independientes las decisiones de riesgo, aprobación de cierre, pasos, omisión y continuidad de ejecución.


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

Pendientes: alternativas verificadas y suficiencia técnica, propagación de avisos a derivadas, RESTRICTED, operación/retención de archivos e IA supervisada; RLS y despliegue antes de uso real multiempresa.


## 23. Iteración 27 — Soporte de evidencias por ejecución (2026-10-27)

[Contrato, límites y verificaciones](Iteracion27_Soporte_Evidencias_Ejecucion.md).

GET `.../incidents/{incident}/executions/{execution}/evidence-readiness` detalla el soporte actual de la evidencia seleccionada por cada tarea exacta, referencias retiradas, metadatos FILE no disponibles, omisiones y trabajo pendiente. Lectura RepeatableRead con RiskContextToken del mismo snapshot y lote acotado de hasta cien tareas. No devuelve contenido privado ni abre archivos; FileIntegrityVerified=false, TechnicalSufficiencyVerified=false y RequiresHumanReview=true. No cambia tareas, autorizaciones ni la política de aceptación ORG_ADMIN de todos los cierres.

- Release: cero errores/advertencias, EF sin diferencias pendientes.
- Enfocadas: **31/31 PostgreSQL 17**. Regresión relacionada final: **187/187 PostgreSQL 17.11 y 187/187 PostgreSQL 18.3**, sin fallidas/omitidas; las 31 nuevas están incluidas. No se repitió la suite global completa. [PG17](../artifacts/evidence-readiness-related-pg17-5a7b15e314974346ac0fe524b66a88c9/paso26-pg17.trx), [PG18.3](../artifacts/evidence-readiness-related-pg18-b537067f85b44f6d8a881ac4b3e696e2/paso26.trx), [matriz y hashes](../artifacts/evidence-readiness/validation.json).
- Cubre aislamiento, roles/cuentas y SQL limitado, exactitud del vínculo, retirada/cuarentena, lectura terminal, casos históricos inválidos en bases desechables, cien tareas sin truncar, privacidad del lote y retirada concurrente con contexto coherente.
- Base habitual local: **23 migraciones sin cambios**, conteos de todas las tablas app/auth/audit y funciones app/auth idénticos al inicio. Sin migración ni datos de negocio nuevos. [Conservación](../artifacts/evidence-readiness/dev-unchanged.txt), [antes](../artifacts/evidence-readiness/dev-before.json), [después](../artifacts/evidence-readiness/dev-after.json).
- HTTPS: health/OpenAPI/Swagger 200 y nuevo GET 401 anónimo; contrato tipado y enums string verificados. No es una prueba de todos los endpoints. [Resultados](../artifacts/evidence-readiness-http-99b462422a464c3e987e26137517e519/results.json).
- MCP: 3684 nodos/11626 relaciones; Graphify AST/HTML: 3134 nodos/8895 relaciones/223 comunidades, sin IA externa.
- Quedan suficiencia técnica y alternativas verificadas, propagación a derivadas, RESTRICTED, políticas operativas de archivos, IA supervisada, RLS y validación integral del MVP.
- Procesos propios temporales detenidos y servicios habituales conservados. Sin commit/push/merge; HEAD 9cec6a075c918ca88bf02fdda6b8ad750c8cf4a3. Cambios previos y frontend preservados.


## 24. Tramo previo a IA — Iteraciones 28–32 verificadas (2026-10-27)

El usuario autorizó avanzar hasta antes de IA local y confirmó las tres políticas descritas en la guía integrada. Las cinco entregas se implementaron y verificaron. Release sin errores/advertencias; EF sin diferencias pendientes. Global inicial: 1017/1019 por motor; dos fixtures corregidos. Final: 42/42 por motor, incluidos 35 casos nuevos; negocio byte-idéntico a las suites globales. Unión por IDs: 1023 casos distintos aprobados por motor, sin afirmar suite global final de 1023/1023.

Base habitual openirp_dev local actualizada de 23 a 26 migraciones tras respaldo restaurado, ensayo de actualización y comparación exacta antes/después; conteos previos e historial conservados. HTTPS y diagnóstico sin borrado verificados. [Resultados, contratos e inventario](Entrega_PreIA_Iteraciones28_32.md).

La siguiente etapa funcional es IA local supervisada; no se inició. Persisten los pendientes productivos de §5. Sin commit/push/merge; HEAD 9cec6a075c918ca88bf02fdda6b8ad750c8cf4a3. Frontend y trabajo local anterior preservados. Procesos propios temporales detenidos; PostgreSQL/Ollama habituales conservados.


## 25. Iteración 33 — EXPLAIN_STEP local sin thinking, verificado (2026-10-27)

El usuario ratificó usar IA sin razonamiento. Se implementaron las tres rutas de contexto/explicación/historial, con contexto mínimo, un modelo permitido, think=false, /no_think, validación/rechazo de reasoning y revalidación final de permisos/contexto. Evaluación y auditoría atómicas, inmutabilidad EF/SQL, ninguna ejecución/decisión de negocio automática y originales sin persistir.

Final relacionado: 259/259 PostgreSQL 17 y 259/259 PostgreSQL 18.3, incluidos 55 casos nuevos; no suite global nueva. Prueba real sintética 1/1 con digest observado, última duración 2506 ms; no latencia garantizada. Release limpio y EF sin diferencias. Migración 27 aplicada solo a openirp_dev local tras respaldo restaurado y ensayo; todos los conteos y funciones anteriores conservados, cero evaluaciones/usuarios nuevos en base habitual.

[Contrato, activación, resultados e inventario](Iteracion33_Asistencia_Local_Explicacion_Pasos.md). IA deshabilitada por defecto; habilitar LocalAI__Enabled=true para probar localmente. Pendientes revisión humana definitiva, otros tipos/propuestas/borradores y política de originales cifrados. Sin commit/push/merge, sin cambios de frontend. HEAD 9cec6a075c918ca88bf02fdda6b8ad750c8cf4a3. Procesos propios temporales cerrados; servicios habituales preservados.


## 26. Publicación del avance de API y exclusión de frontend (2026-10-27)

El usuario autorizó commit y push de todo el avance, excluyendo frontend hasta nuevo aviso. Se prepara la rama feature/paso26-identidad-organizaciones con la API y pruebas de las iteraciones 18–33, Swagger, migraciones hasta LocalAssistanceAssessments, herramientas operativas, AGENTS.md, exclusiones y documentación. Las referencias anteriores a trabajo local/sin push describen el estado histórico de cada entrega.

Quedan fuera apps/web/, ci/Start-LocalWeb.ps1, OpenIRP_Documentacion/Frontend_Local.md y la sección de frontend del README. Se conservan localmente sin sobrescribir sus archivos; tampoco se incluyen secretos, almacenamiento privado, respaldos, resultados generados o grafos locales ignorados. Las menciones históricas al frontend en la bitácora no publican su implementación.

Validación de API reutilizada: código idéntico al snapshot verificado de Iteración 33; 259/259 casos relacionados en PostgreSQL 17 y 18.3 (55 nuevos incluidos), caso real sintético sin thinking aprobado, Release sin errores/advertencias. No hay una nueva ejecución global completa. El commit se realiza después de comprobar alcance del índice, secretos y espacios; el resultado del push debe verificarse contra la referencia remota, sin force ni merge.
