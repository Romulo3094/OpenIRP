# Iteración 33 — EXPLAIN_STEP con Ollama local sin thinking

Fecha: 2026-10-27, America/Santiago. Workspace D:\Desarrollos\OpenIRP; desarrollo local sobre feature/paso26-identidad-organizaciones.

**Estado: implementado y verificado. Base habitual actualizada de 26 a 27 migraciones con respaldo restaurado, ensayo de actualización y datos previos conservados. IA deshabilitada por defecto; activación explícita para uso local.**

## Alcance

Primera entrega funcional de IA: explicación de un paso autorizado. EXPLAIN_STEP no adapta ni ejecuta tareas, no crea borradores, no acepta riesgo ni toma decisiones de aprobación. ADAPT_STEP, SUMMARIZE_INCIDENT, PROPOSE_PLAYBOOK, aceptación/rechazo humano definitivo y conversión a PRIVATE quedan para entregas posteriores. La operación manual conserva sus controles y disponibilidad.

La petición del usuario exige IA local sin razonamiento. Se permite únicamente qwen3:4b-instruct, sin fallback a qwen3:4b ni modelos cloud. La etiqueta instalada todavía declara capacidad thinking: se fuerza think=false, se añade /no_think al mensaje y se rechazan thinking/reasoning_content no vacíos o bloques <think> en la respuesta. No se afirma que cambiar el nombre del modelo elimine esa capacidad.

## Contratos y permisos

Rutas bajo `/api/organizations/{organization}/incidents/{incident}/executions/{execution}/tasks/{task}`:

- GET assistance-context: instantánea mínima, revisiones y ContextToken, sin llamar al modelo.
- POST assistance: ExplainStep exige expectedIncidentRevision, expectedExecutionRevision, expectedContext y reason. Cookie autenticada y CSRF; devuelve AssistanceAssessmentItem.
- GET assistance-assessments: historial paginado, skip entre 0 y 1000000 y limit 1–100; no-store.

Cuenta/organización/membresía activas, sin bloqueo vigente; ORG_ADMIN local o ANALYST actualmente asignado al incidente o a la tarea exacta. VIEWER y PLATFORM_ADMIN sin membresía no acceden. La generación exige incidente IN_RESPONSE, ejecución IN_PROGRESS usable y tarea PENDING/BLOCKED/IN_PROGRESS. Los avisos propios/heredados impiden la inferencia si falta continuidad válida. El historial permite estados terminales, con autorización actual.

ExpectedContext es la huella risk-v1 del contexto de negocio y sus avisos/capacidades. No es autorización. Después de inferir se comprueban de nuevo permisos, ámbitos, estados, revisiones y huella bajo el gate transaccional; una revocación deniega guardar/devolver, y cambios de contexto generan STALE sin recomendación.

## Minimización e incertidumbre

Se envían código de categoría; CIA, alcance, impacto, criticidad y severidades declaradas (null permanece desconocido); código/fase/instrucciones y flags inmutables del paso elegido; requisitos de capacidad con disponibilidad efectiva/actividad; códigos y estados de predecesores. No se envían UUID de organización/incidente/usuarios/tareas, títulos o descripción del incidente, activos, notas/limitaciones libres, evidencias TEXT/REFERENCE/FILE, contenido RESTRICTED, archivos ni motivo de la solicitud.

Las instrucciones del paso son contenido autorizado pero no confiable; podrían contener datos introducidos por su autor. La minimización excluye campos, no garantiza detectar semánticamente todo dato sensible dentro del texto autorizado. No hay consulta de URLs del modelo, herramientas ni acciones sobre infraestructura externa. Los textos recibidos son datos no confiables para revisión y deben presentarse como texto, nunca ejecutarse o insertarse como HTML.

## Adaptador acotado

LocalAI.Enabled=false por defecto. BaseUrl por defecto http://127.0.0.1:11435; solo IP literal loopback y raíz sin usuario/query/fragment. Handler sin proxy, cookies ni redirecciones. El cliente verifica que el modelo permitido esté instalado y observa su digest en api/tags; no descarga modelos. El digest es metadata observada antes de la llamada, no una atestación del proceso ni un bloqueo frente a retag de un operador local.

api/chat recibe stream=false, think=false, temperatura 0, num_predict=384, num_ctx=4096 y esquema JSON con cuatro campos: stepCode, guidance, cautions, assumptions. Prompt openirp-explain-step-v1, esquema openirp-assistance-v1. El prompt exige respuesta breve y revisión humana. Un presupuesto conservador de bytes del contexto/prompt más reserva de salida rechaza entradas excesivas sin truncar el paso. Una instancia de API permite una inferencia simultánea, sin cola; otras reciben ai_busy. Tiempo por defecto 45 s, configurable de 1 a 90 s; cancelación del solicitante cancela la llamada.

Se limita a 64 KiB el envelope y 16 KiB el JSON de contenido; se rechazan nombres duplicados/extra, paso distinto, campos vacíos, tipos/límites incorrectos, herramientas, razonamiento y salidas truncadas/no terminadas. No se interpreta la validación estructural como corrección técnica.

## Evaluaciones y persistencia

AssistanceAssessment conserva tipo, contexto mínimo/hash, revisiones, esquema/prompt, parámetros, modelo/digest observado, duración, resultado y código de error fijo; para VALID conserva exclusivamente los cuatro campos normalizados y validados. Resultados: VALID, INVALID, UNAVAILABLE, STALE. Fallos/contexto obsoleto nunca incluyen recomendación. RequiresHumanReview=true, AuthorizesExecution=false, ManualOperationAvailable=true y OriginalResponseStored=false.

ContextJson se materializa como TEXT con comprobación JSON, tamaño y SHA-256 en PostgreSQL para conservar y reproducir los bytes UTF-8 exactos del contexto enviado, en lugar de la canonización del JSONB propuesto en el diseño lógico. InputHash identifica ese JSON; /no_think y el system prompt se reconstruyen por PromptVersion. La recomendación válida normalizada es un registro de negocio con acceso restringido; no se conserva el envelope, el original message.content, thinking ni cuerpos de error. ai_response_payloads y la retención cifrada de originales permanecen deshabilitados hasta definir claves, plazos y respaldos.

La inferencia ocurre fuera de transacciones/bloqueos de negocio. Solo el guardado final es transaccional: evaluación y auditoría ai.assistance.evaluated se confirman juntas. No cambia revisiones del incidente/ejecución, estado de tareas, flags, DAG o decisiones. Las evaluaciones informativas no alteran por sí mismas la huella de riesgo. IsCurrent exige resultado válido, contexto/revisiones coincidentes y tarea aún activa; no significa aceptación humana. Pueden coexistir varias evaluaciones del mismo contexto.

Migración 20261027235738_LocalAssistanceAssessments: tabla append-only, FK compuesta a tarea/ejecución/versión/organización/incidente, restricciones de contexto/hash/resultados, triggers de protección y comprobación diferida de contexto/actor/auditoría. EF bloquea cambios/borrados; SQL bloquea UPDATE/DELETE/TRUNCATE. Runtime recibe SELECT/INSERT. Downgrade rechazado si existen evaluaciones.

## Activación local

Con Docker/Ollama disponibles y el modelo ya instalado, en PowerShell desde D:\Desarrollos\OpenIRP:

```powershell
$env:LocalAI__Enabled = 'true'
./ci/Start-LocalApi.ps1
```

La variable afecta esa sesión y sus procesos hijos. Para volver al valor predeterminado, quitar LocalAI__Enabled de la sesión o establecerlo en false. No cambiar LocalAI:Model a qwen3:4b: se rechazará sin fallback. No hace falta habilitar thinking ni descargar otro modelo.

En Swagger: obtener assistance-context para una tarea activa autorizada, copiar IncidentRevision/ExecutionRevision/ContextToken a los tres campos expected del POST assistance y añadir reason. Las respuestas VALID siguen pendientes de revisión humana: no existe aún aceptación/rechazo definitivo de IA ni conversión de propuestas a borrador. El endpoint no ejecuta el paso.

## Verificación final y evidencias

- Release, SDK .NET 10.0.403: **cero errores y advertencias**. EF sin diferencias pendientes de modelo en ambos motores.
- Primera selección enfocada: 36/47; once casos detectaron serialización PascalCase en un campo de auditoría esperado como camelCase. Se corrigió la serialización sin relajar el guard. Enfocadas corregidas: **47/47 PostgreSQL 17**.
- Regresión previa a la política final de no-thinking: **255/255 PostgreSQL 17 y 255/255 PostgreSQL 18.3**. Después de la indicación del usuario se añadió /no_think, se prohibió el modelo base, se redujo la salida máxima a 384 tokens y se añadieron pruebas de thinking/reasoning_content/bloques de pensamiento.
- Regresión **final: 259/259 en PostgreSQL 17.11 y 259/259 en PostgreSQL 18.3**, cero fallidas/omitidas. Incluye **55 casos nuevos** de asistencia/transporte. Los 55 están incluidos en 259, no se suman. Selección relacionada: LocalAi, LocalAiTransport, Executions, EvidenceReadiness, OperationalReviews, PlaybookDrafts, SecurityHolds y ClosureRisk. No se ejecutó una nueva suite global completa en esta iteración.
- [Final PG17](../artifacts/ai-final-pg17-9925f808e1314c29b2102dfe9ffe921e/paso26-pg17.trx), [final PG18.3](../artifacts/ai-final-pg18-2843cc8f1a614c7ab4fd7e494a28067b/paso26.trx), [matriz, hashes y modo](../artifacts/ai-assistance/validation.json), [compilación](../artifacts/ai-assistance/build-nothink-final.log).
- Pruebas nuevas cubren minimización/RESTRICTED excluido, permisos/asignación/ámbito, requisitos obsoletos antes del modelo, cambios durante inferencia, revocación de acceso, auditoría atómica/rollback, historial inmutable/paginación/vigencia, runtime SQL limitado, cookies/CSRF/no-store, operación manual tras fallo, JSON inválido/extra/duplicado, paso distinto, herramientas, truncación/tamaños, destinos remotos o ambiguos, falta de modelo, configuración deshabilitada, modelo base/cloud prohibido, límites de contexto, timeout/cancelación, concurrencia sin cola y rechazo de razonamiento.
- Modelo local observado: **qwen3:4b-instruct**, digest **0edcdef34593eac1aa2be9c7d06c432dcf81945adca5eca2f27662c18f168ba0**, Ollama **0.40.6**, GPU NVIDIA RTX 3070. La etiqueta declara thinking: el modo sin razonamiento se fuerza por solicitud y se comprueba la ausencia de esa salida. No se cambió la definición ni se descargó otro modelo.
- Primera consulta estructurada sin /no_think agotó el límite de 90 s y quedó UNAVAILABLE, sin bloquear trabajo manual. Tras la corrección, la prueba real sintética completa aprobó: una medición de **13161 ms** y la confirmación con compilación final de **2506 ms**, resultado VALID sin reasoning. Son medidas puntuales del cliente (incluyen transporte/validación), no latencias garantizadas. [Real final: 1/1](../artifacts/ai-real-final-pg17-22a14428e389450dbfe4d877b115e621/paso26-pg17.trx). Ese caso también está incluido en la selección determinista; no se suma a 259. Usa únicamente datos sintéticos en base temporal eliminada, sin datos del usuario ni registro de texto original en artefactos.
- Respaldo anterior de openirp_dev: **9D87F751B742C69366F887F0A0AA2006FBD3FE390C04D917AB5D3CCF31FBA6FF**. Restauración completa aislada verificada (26 migraciones, todos los conteos y funciones); ensayo de esa restauración a 27 migraciones conservando todos los datos y todas las funciones anteriores. [Hash](../artifacts/ai-assistance/backup-sha256.json), [restauración](../artifacts/ai-assistance/restore-verified.txt), [ensayo](../artifacts/ai-assistance/upgrade-restore-verified.txt).
- Migración **20261027235738_LocalAssistanceAssessments** aplicada únicamente a openirp_dev, propietario openirp, **127.0.0.1:5433**, después de comprobar rama/HEAD, código/binarios, matriz, respaldo, SQL y que la base no cambió desde el respaldo. Estado posterior idéntico al ensayo restaurado: **27 migraciones**, ocho capacidades/tres categorías y todos los conteos previos conservados. assistance_assessments tiene cero filas en la base habitual; no se crearon usuarios ni datos de negocio allí. Esquema: 25 columnas, dos FK, tres triggers (uno diferido) y dos funciones nuevas; todas las funciones previas intactas. [SQL final](../artifacts/ai-assistance/LocalAssistance-final.sql), [esquema](../artifacts/ai-assistance/schema-verified.txt), [conservación](../artifacts/ai-assistance/dev-preserved.txt), [antes](../artifacts/ai-assistance/dev-before.json), [después](../artifacts/ai-assistance/dev-after.json).
- HTTPS posterior: health/OpenAPI/Swagger 200; las tres rutas nuevas y las rutas relacionadas comprobadas responden 401 anónimos. Contratos tipados y revisiones requeridas presentes en OpenAPI. No es un smoke de todos los endpoints; health es estático. Flujos autenticados/IA se verificaron en bases temporales. [Resultados](../artifacts/ai-assistance-http-61b6a4b1efb34e7fafbbf352deac27e5/results.json), [OpenAPI](../artifacts/ai-assistance-http-61b6a4b1efb34e7fafbbf352deac27e5/openapi.json).
- MCP actualizado: **4132 nodos/12706 relaciones**. Graphify AST/HTML: **3460 nodos/9743 relaciones/262 comunidades**, sin IA externa.
- Procesos propios temporales detenidos; PostgreSQL/Ollama habituales conservados. Sin commit/push/merge; HEAD sigue 9cec6a075c918ca88bf02fdda6b8ad750c8cf4a3. Frontend y cambios locales previos preservados. No repetir la migración ni ejecutar downgrade para reanudar sin inspeccionar el estado real.

## Archivos de esta entrega

[Inventario respecto del estado local preservado](../artifacts/ai-assistance/edited-files.json), [copia y hashes previos](../artifacts/ai-assistance/baseline-files.json). No se atribuyen a esta entrega cambios anteriores.

- Editado: [OpenIRP_Documentacion/Bitacora_Desarrollo_OpenIRP.md](../OpenIRP_Documentacion/Bitacora_Desarrollo_OpenIRP.md).
- Nuevo: [OpenIRP_Documentacion/Iteracion33_Asistencia_Local_Explicacion_Pasos.md](../OpenIRP_Documentacion/Iteracion33_Asistencia_Local_Explicacion_Pasos.md).
- Editado: [OpenIRP_Documentacion/OpenIRP_Planificacion_Integral.md](../OpenIRP_Documentacion/OpenIRP_Planificacion_Integral.md).
- Editado: [README.md](../README.md).
- Nuevo: [apps/api/OpenIRP.Api/Controllers/AssistanceController.cs](../apps/api/OpenIRP.Api/Controllers/AssistanceController.cs).
- Editado: [apps/api/OpenIRP.Api/Program.cs](../apps/api/OpenIRP.Api/Program.cs).
- Editado: [apps/api/OpenIRP.Api/appsettings.json](../apps/api/OpenIRP.Api/appsettings.json).
- Nuevo: [apps/api/OpenIRP.Application/Assistance/AssistanceContracts.cs](../apps/api/OpenIRP.Application/Assistance/AssistanceContracts.cs).
- Nuevo: [apps/api/OpenIRP.Domain/Entities/AssistanceAssessment.cs](../apps/api/OpenIRP.Domain/Entities/AssistanceAssessment.cs).
- Nuevo: [apps/api/OpenIRP.Infrastructure/Assistance/OllamaLocalAssistant.cs](../apps/api/OpenIRP.Infrastructure/Assistance/OllamaLocalAssistant.cs).
- Nuevo: [apps/api/OpenIRP.Infrastructure/Incidents/IncidentAssistanceService.cs](../apps/api/OpenIRP.Infrastructure/Incidents/IncidentAssistanceService.cs).
- Editado: [apps/api/OpenIRP.Infrastructure/Incidents/IncidentService.cs](../apps/api/OpenIRP.Infrastructure/Incidents/IncidentService.cs).
- Nuevo: [apps/api/OpenIRP.Infrastructure/Persistence/Configurations/AssistanceMapping.cs](../apps/api/OpenIRP.Infrastructure/Persistence/Configurations/AssistanceMapping.cs).
- Nuevo: [apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261027235738_LocalAssistanceAssessments.Designer.cs](../apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261027235738_LocalAssistanceAssessments.Designer.cs).
- Nuevo: [apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261027235738_LocalAssistanceAssessments.cs](../apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261027235738_LocalAssistanceAssessments.cs).
- Editado: [apps/api/OpenIRP.Infrastructure/Persistence/Migrations/OpenIrpDbContextModelSnapshot.cs](../apps/api/OpenIRP.Infrastructure/Persistence/Migrations/OpenIrpDbContextModelSnapshot.cs).
- Editado: [apps/api/OpenIRP.Infrastructure/Persistence/OpenIrpDbContext.cs](../apps/api/OpenIRP.Infrastructure/Persistence/OpenIrpDbContext.cs).
- Nuevo: [apps/api/OpenIRP.IntegrationTests/AssistanceTests.cs](../apps/api/OpenIRP.IntegrationTests/AssistanceTests.cs).
- Nuevo: [apps/api/OpenIRP.IntegrationTests/LocalAiTransportTests.cs](../apps/api/OpenIRP.IntegrationTests/LocalAiTransportTests.cs).
- Editado: [apps/api/OpenIRP.IntegrationTests/PlaybookDraftPersistenceTests.cs](../apps/api/OpenIRP.IntegrationTests/PlaybookDraftPersistenceTests.cs).
- Editado: [ci/grant-runtime.sql](../ci/grant-runtime.sql).

Fuentes del contrato de transporte: [api/chat de Ollama](https://docs.ollama.com/api/chat), [salidas estructuradas](https://docs.ollama.com/capabilities/structured-outputs). Los resultados de negocio y locales se acreditan con pruebas del repositorio, no con esas páginas.
