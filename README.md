# OpenIRP

**Open Source Incident Response Playbook Platform**

*Plataforma web de código abierto para la gestión, recomendación y adaptación contextualizada de playbooks de respuesta a incidentes de ciberseguridad.*

**Estado:** En desarrollo | **Licencia propuesta:** GNU AGPLv3 | **Área:** Ciberseguridad / Incident Response

---

## Descripción

**OpenIRP** es un proyecto de investigación y desarrollo tecnológico que busca facilitar la respuesta a incidentes de ciberseguridad en pequeñas y medianas empresas (PYMES), especialmente aquellas que disponen de recursos técnicos, humanos y económicos limitados.

La plataforma está concebida para proporcionar procedimientos estructurados de respuesta a incidentes (*playbooks*), considerando las características de cada organización, sus capacidades tecnológicas y el tipo de incidente identificado.

OpenIRP contempla la integración de inteligencia artificial local para asistir en la contextualización de procedimientos previamente definidos, manteniendo la supervisión y validación humana de las recomendaciones generadas.

El proyecto se desarrolla bajo una filosofía **open source**, orientada a la accesibilidad, transparencia, colaboración y mejora continua.

## Problemática

Las PYMES pueden enfrentar dificultades al responder a incidentes de ciberseguridad debido a factores como:

- Recursos limitados para establecer equipos especializados.
- Ausencia de procedimientos documentados de respuesta a incidentes.
- Dificultad para adaptar marcos de buenas prácticas a organizaciones pequeñas.
- Dependencia de conocimientos técnicos especializados.
- Falta de herramientas accesibles para orientar la actuación ante incidentes.

Como consecuencia, una organización podría actuar de forma improvisada, retrasar la contención de una amenaza o ejecutar medidas que incrementen el impacto del incidente.

**OpenIRP propone abordar esta problemática mediante una plataforma de respuesta guiada**, utilizando playbooks estructurados y recomendaciones adaptadas a las capacidades de la organización.

## Objetivo general

Desarrollar y evaluar un prototipo de plataforma web de código abierto que permita recomendar y adaptar playbooks de respuesta a incidentes de ciberseguridad en PYMES, incorporando inteligencia artificial asistiva y validando su funcionamiento mediante escenarios simulados.

## Objetivos específicos

1. Analizar las necesidades de respuesta a incidentes de las PYMES y los criterios aplicables de estándares, metodologías y soluciones existentes.
2. Diseñar playbooks estructurados para incidentes de ciberseguridad, considerando los roles, recursos y capacidades de las organizaciones.
3. Implementar una plataforma web que permita gestionar información contextual de incidentes y recomendar playbooks adecuados.
4. Integrar inteligencia artificial local como mecanismo asistivo para contextualizar y adaptar los procedimientos de respuesta, manteniendo la supervisión humana.
5. Evaluar el funcionamiento y la pertinencia del prototipo mediante escenarios controlados de incidentes simulados.

## Funcionalidades previstas

Las siguientes capacidades forman parte de la propuesta de desarrollo. Su disponibilidad dependerá de la evolución del prototipo y de las versiones publicadas.

### Gestión de incidentes

- Registro de incidentes de ciberseguridad.
- Clasificación según tipo y características.
- Identificación de información contextual relevante.
- Consulta y seguimiento de incidentes registrados.

### Catálogo de playbooks

- Biblioteca de procedimientos estructurados.
- Clasificación por tipo de incidente.
- Identificación de requisitos, acciones y responsables.
- Consulta de procedimientos recomendados.

### Recomendación contextualizada

- Evaluación del incidente reportado.
- Consideración de las capacidades tecnológicas de la organización.
- Selección de procedimientos mediante criterios definidos.
- Presentación de recomendaciones justificadas.

### Inteligencia artificial asistiva

- Integración prevista con modelos de lenguaje ejecutados localmente mediante Ollama.
- Adaptación contextual de playbooks previamente definidos.
- Generación de explicaciones y recomendaciones complementarias.
- Validación de resultados antes de su aceptación.

**La inteligencia artificial no tendrá autoridad para ejecutar acciones críticas de manera autónoma.**

### Ejecución guiada

- Ejecución flexible de tareas con dependencias explícitas entre pasos, sin exigir un orden estrictamente secuencial.
- Identificación de responsables.
- Registro del avance de los procedimientos.
- Confirmación humana de las acciones realizadas.

## Escenarios iniciales de investigación

El prototipo priorizará escenarios de incidentes relevantes para organizaciones con recursos limitados.

| Incidente | Enfoque del playbook |
|---|---|
| Phishing | Identificación, evaluación, contención y recomendaciones de recuperación |
| Compromiso de cuentas | Evaluación del acceso no autorizado, contención y recuperación |
| Ransomware | Identificación de señales, aislamiento y coordinación de la respuesta |

Los escenarios serán representados mediante simulaciones controladas y no implican intervención sobre infraestructuras productivas.

## Arquitectura conceptual

La solución se concibe a partir de los siguientes componentes:

1. **Interfaz web:** interacción con usuarios, incidentes y playbooks.
2. **Servicios de aplicación:** gestión de la lógica funcional y reglas de negocio.
3. **Persistencia de datos:** almacenamiento de la información necesaria para el funcionamiento del prototipo.
4. **Motor de recomendación:** selección contextual de procedimientos.
5. **Servicio de IA local:** asistencia para adaptar y explicar procedimientos.
6. **Validación humana:** revisión y aceptación de las recomendaciones antes de su utilización.

La arquitectura técnica está especificada en [el documento maestro](OpenIRP_Documentacion/OpenIRP_Planificacion_Integral.md): solución .NET 10 en cuatro proyectos, PostgreSQL 17, contenedores Docker y Ollama local. Su implementación será incremental.

## Despliegue

OpenIRP contempla un despliegue basado en contenedores Docker, con el objetivo de facilitar su instalación y reproducibilidad en diferentes entornos.

Para las funcionalidades de inteligencia artificial se considera el uso de Ollama como servicio de inferencia local.

La base del entorno de desarrollo usa PostgreSQL 17 y Ollama mediante Docker Compose. Para preparar el entorno local, copia `infra/.env.example` a `infra/.env` y cambia `POSTGRES_PASSWORD`. Antes de ejecutar Compose, crea el volumen externo con `docker volume create openirp_ollama_data`; desde la carpeta `infra`, ejecuta `docker compose up -d`. Se requiere Docker con acceso a GPU NVIDIA para la configuración actual de Ollama (`gpus: all`). El modelo de IA debe descargarse por separado.

Para la API, configura la conexión sin subir secretos: `dotnet user-secrets set "ConnectionStrings:OpenIRP" "Host=127.0.0.1;Port=5433;Database=openirp_dev;Username=openirp;Password=TU_PASSWORD" --project apps/api/OpenIRP.Api`. Después ejecuta `dotnet run --project apps/api/OpenIRP.Api`. La configuración de desarrollo usa el puerto HTTP `5195` y HTTPS `7113` (consulta `launchSettings.json`). El endpoint `/api/health` responde de forma estática y todavía no comprueba la conectividad con PostgreSQL ni Ollama.

**Estado:** la infraestructura inicial y la capa Identity están en desarrollo. La migración `InitialIdentityOrganizations` está generada y fue aplicada y verificada sobre PostgreSQL 17 temporal en GitHub Actions. **La base local `openirp_dev` fue respaldada y migrada después de comprobar que estaba vacía; el respaldo se restauró con éxito en un contenedor separado.** El aprovisionamiento offline del primer administrador, el alta transaccional de organizaciones y la protección del último administrador ya están implementados localmente, con pruebas PostgreSQL y HTTP. Consulta [la guía del Paso 26](OpenIRP_Documentacion/Paso26_Administracion_Local.md) para repetirlas y preparar tu entorno persistente.

**Migración en la base habitual (revisar destino y respaldo primero):** además del secreto para arrancar la API, la factoría de EF Core necesita `ConnectionStrings__OpenIRP` como variable de entorno durante `dotnet ef database update`. En PowerShell configura esa variable solo en la sesión actual y usa:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.5
dotnet ef database update --project apps/api/OpenIRP.Infrastructure/OpenIRP.Infrastructure.csproj --startup-project apps/api/OpenIRP.Api/OpenIRP.Api.csproj --context OpenIrpDbContext
Remove-Item Env:\ConnectionStrings__OpenIRP
```

No apliques la migración a bases con datos existentes sin revisar antes el script y su respaldo; la conexión deberá apuntar al entorno de desarrollo deseado.

### Auditoría administrativa local

La subfase siguiente del Paso 26 incorpora eventos transaccionales, consultas de auditoría con permisos por organización y protección append-only. La migración `BasicAdministrativeAudit` ya fue aplicada a `openirp_dev` con respaldo/restauración comprobados y sin alterar los datos de negocio. La validación de esa subfase pasó 37/37 casos tanto en PostgreSQL 17 como en 18.

Consulta [la guía de auditoría](OpenIRP_Documentacion/Paso26_Auditoria_Local.md) para ver endpoints, permisos SQL, archivos y límites. Para repetir con Docker usa `./ci/Test-IdentityDocker.ps1`; para PostgreSQL nativo, `./ci/Test-IdentityLocal.ps1`. No se habilitó registro público ni acceso global a tenants.

### Catálogos, capacidades e inventario local

La Iteración 2 incorpora catálogos de capacidades/categorías, perfil organizacional con UNKNOWN implícito y activos con revisión y retirada lógica. Los permisos se comprueban por organización y las escrituras se auditan dentro de su transacción. La migración local se aplicó tras respaldo y restauración de prueba, conservando los datos previos.

La validación de esa subfase pasó 68/68 casos en PostgreSQL 17 y 68/68 en PostgreSQL 18. Consulta [contratos, permisos, archivos y evidencias](OpenIRP_Documentacion/Iteracion2_Catalogos_Capacidades_Activos.md). Puedes iniciar la API con `./ci/Start-LocalApi.ps1`; los nuevos endpoints aparecen en OpenAPI de desarrollo.

### Registro inicial de incidentes local

La Iteración 3 agrega registro, consulta, corrección y asignación de incidentes, con historial inmutable por revisión y auditoría transaccional. ORG_ADMIN/ANALYST activos pueden escribir en su organización; VIEWER lee. El permiso global no evita la membresía del tenant. Las revisiones obsoletas se rechazan con 409.

La migración `InitialIncidentRegister` está aplicada en `openirp_dev` tras respaldar y restaurar una copia aislada, conservando datos previos. La validación de esa subfase pasó **94/94 pruebas en PostgreSQL 17 y 94/94 en PostgreSQL 18**, sin omitidas. Consulta [rutas, contratos, archivos y evidencias](OpenIRP_Documentacion/Iteracion3_Registro_Incidentes.md).

El registro inicia en REGISTERED; las iteraciones posteriores incorporan evaluaciones, severidad y ciclo de vida con controles explícitos. No se asigna severidad baja por omisión. Las iteraciones 3–6 se publicaron en `0c70669`.

### Activos afectados por incidente

La Iteración 4 permite reportar activos todavía no inventariados, adjuntar activos existentes y vincularlos después conservando nombre, tipo, contexto y criticidad históricos. Cada operación exige motivo y revisión del incidente, con permisos por organización, snapshots y auditoría transaccionales. La criticidad desconocida debe expresarse como null, sin asumir un nivel bajo.

La nueva migración está aplicada en la base local tras respaldo y restauración aislada verificados. Validación de esa subfase: **127/127 pruebas en PostgreSQL 17 y 127/127 en PostgreSQL 18**. Consulta [endpoints, archivos, evidencia y pendientes](OpenIRP_Documentacion/Iteracion4_Activos_Incidente.md). Los cambios nuevos de incidentes y asociaciones siguen locales, sin otro commit/push.

### Capacidades y limitaciones por incidente

La Iteración 5 permite heredar el perfil de la organización o evaluar disponibilidad específica del incidente, con motivo, limitaciones e historial. Unknown explícito es distinto de heredar. Se comprueban las revisiones del incidente, perfil y catálogo para rechazar decisiones basadas en contexto obsoleto. ANALYST puede documentar el incidente; el perfil general sigue reservado a ORG_ADMIN.

Migración local aplicada tras respaldo y restauración de prueba, conservando los datos previos. Validación de esa subfase: **156/156 pruebas en PostgreSQL 17 y 156/156 en PostgreSQL 18**, sin fallidas ni omitidas. Consulta [endpoints, archivos y evidencias](OpenIRP_Documentacion/Iteracion5_Capacidades_Incidente.md). Esta iteración se publicó junto con CIA en `0c70669`; las iteraciones 9 y 10 incorporan después severidad y ciclo de vida con cierre manual controlado.

### Evaluación de impacto CIA

La Iteración 6 permite evaluar confidencialidad, integridad y disponibilidad con None/Low/Medium/High o null explícito cuando falta investigar. Exige motivo y revisión, protege datos por organización y conserva CIA en el historial de todas las modificaciones posteriores. La Iteración 9 integra estos valores en la regla versionada; CIA no autoriza cierre automáticamente.

Migración local aplicada tras respaldo/restauración verificados, conservando datos previos. Validación de esa subfase: **181/181 pruebas en PostgreSQL 17 y 181/181 en PostgreSQL 18**, sin fallidas ni omitidas. Consulta [archivos, endpoints y evidencias CIA](OpenIRP_Documentacion/Iteracion6_Evaluacion_CIA.md).

La [propuesta de severidad v1](OpenIRP_Documentacion/Propuesta_Regla_Severidad_v1.md) incluye una matriz comprobada en un prototipo matemático aislado, adoptada por el usuario e implementada en la Iteración 9. La política de desconocidos está confirmada: Pending y severity=null hasta completar todas las entradas requeridas, sin severidad provisional. Los umbrales se adoptaron para el MVP y siguen sujetos a evaluación con casos reales. Las iteraciones 3–6 y la decisión inicial sobre desconocidos se publicaron en el commit 0c70669; la adopción de la matriz corresponde a la Iteración 9 local. La siguiente subfase se desarrolla localmente.

La [Iteración 7: alcance y afectación operacional](OpenIRP_Documentacion/Iteracion7_Contexto_Incidente.md) incorpora PUT `/api/organizations/{org}/incidents/{incident}/context`, con entradas nullable explícitas, motivo, revisión, permisos por organización y snapshots en todos los eventos. La clasificación de severidad se incorpora posteriormente en la Iteración 9.
Validación de la Iteración 7: **216/216 pruebas en PostgreSQL 17 y 216/216 en PostgreSQL 18**, sin fallidas ni omitidas. Migración aplicada a la base local tras respaldo/restauración comprobados; esta nueva subfase permanece local, posterior al commit publicado `0c70669`.
### Criticidad evaluada del incidente

La [Iteración 8](OpenIRP_Documentacion/Iteracion8_Criticidad_Incidente.md) incorpora PUT `/api/organizations/{org}/incidents/{incident}/criticality`: decisión explícita independiente de los reportes originales, justificación de reducciones, evidencia versionada y snapshots. Cambiar los activos invalida la evaluación; un reset a null no permite eludir la justificación de una reducción posterior. El cálculo de severidad se incorpora posteriormente en la Iteración 9.

La compilación Release pasó sin errores ni advertencias y PostgreSQL 17 pasó **259/259 pruebas**, incluidas 43 nuevas. PostgreSQL 18 también pasó **259/259 pruebas**, sin fallidas ni omitidas. La migración local se aplicó tras respaldo/restauración comprobados y mantuvo los datos previos. Las iteraciones 7 y 8 siguen locales, posteriores a `0c70669`.
### Severidad actual y máximo histórico

La [Iteración 9](OpenIRP_Documentacion/Iteracion9_Severidad_Incidente.md) incorpora la matriz `openirp-severity-v1`, adoptada por el usuario para el MVP. Las entradas incompletas producen Pending/severity=null y conservan el máximo histórico. Los incidentes anteriores requieren una primera reevaluación explícita mediante PUT `/api/organizations/{org}/incidents/{incident}/severity`; no se recalcula la historia pasada.

Los cambios de entradas recalculan transaccionalmente para incidentes con regla activa. La API guarda entradas/regla/revisión y PostgreSQL impide rebajar el máximo. Las ediciones ordinarias conservan la clasificación; el ciclo de vida y cierre se incorporan en la Iteración 10. Los umbrales siguen sujetos a evaluación con casos reales.

Release sin errores ni advertencias y **314/314 pruebas correctas en PostgreSQL 17**, sin omitidas. PostgreSQL 18 también pasó **314/314 pruebas**, sin fallidas ni omitidas. Son 55 casos nuevos: 20 del evaluador y 35 de integración. Migración local aplicada tras respaldo/restauración verificados, conservando los datos. Las iteraciones 7–9 permanecen locales, sin nuevo commit/push.
### Ciclo de vida y cierre

La [Iteración 10](OpenIRP_Documentacion/Iteracion10_Ciclo_Vida_Cierre.md) incorpora evaluación, respuesta, resolución, descarte/reapertura y cierre manual. LOW/MEDIUM cierra el responsable asignado activo; HIGH/CRITICAL histórico exige decisión ORG_ADMIN. La aprobación cierra en la misma transacción y la autoaprobación queda identificada con justificación adicional. Todos los niveles requieren además aceptación vigente de riesgo por ORG_ADMIN conforme a la Iteración 26.

Toda modificación invalida una revisión pendiente y los estados terminales requieren reapertura antes de editar. Pending/NotEvaluated impide cerrar. La historia de resoluciones y decisiones se conserva.

**363/363 pruebas correctas en PostgreSQL 17 y 18**, sin fallidas ni omitidas; 49 casos nuevos. Compilación limpia y migración local tras respaldo/restauración verificados. Cambios de las iteraciones 7–10 locales, sin nuevo commit/push. El primer catálogo de borradores se incorpora en la Iteración 11; ejecución guiada, evidencias y endurecimiento operativo siguen pendientes.






### Catálogo de playbooks y borradores

La [Iteración 11](OpenIRP_Documentacion/Iteracion11_Catalogo_Borradores_Playbooks.md) incorpora catálogos administrativos PUBLIC/PRIVATE con primer borrador, pasos, dependencias sin ciclos y requisitos de capacidades. PLATFORM_ADMIN administra los borradores globales; ORG_ADMIN, los de su organización. Borradores públicos no significa acceso anónimo.

Las ediciones son transaccionales, auditadas y protegidas por revisión. PostgreSQL impide referencias de pasos entre versiones y cambios de identidad/propietario. Esta subfase solo admite versión 1 DRAFT; revisión, publicación inmutable, retirada y clonación siguen a continuación.

**407/407 pruebas correctas en PostgreSQL 17 y 18**, sin fallidas ni omitidas; 44 casos nuevos. Compilación limpia, EF sin diferencias y migración local aplicada después de respaldo/restauración verificados. Se conservaron los datos existentes. Las iteraciones 7–11 permanecen locales, sin nuevo commit/push.

### Publicación y vigencia de versiones

La [Iteración 12](OpenIRP_Documentacion/Iteracion12_Publicacion_Versiones_Playbooks.md) añade revisión, rechazo/publicación, autoaprobación explícita justificada, nuevas versiones desde publicaciones del mismo playbook y retirada. El contenido revisado/publicado queda protegido en PostgreSQL, incluidos pasos y requisitos.

La versión vigente procede de un puntero explícito. Retirarla deja el catálogo sin vigente hasta otra publicación; editar un borrador posterior conserva el contenido anterior para sus lectores. Usuarios activos consultan publicaciones globales y miembros activos las privadas de su organización.

**451/451 pruebas correctas en PostgreSQL 17 y 18**, sin fallidas ni omitidas; 44 casos nuevos de integración y 88 enfocados del módulo. Compilación limpia, EF sin diferencias, migración local tras respaldo/restauración verificados y datos existentes conservados. Las iteraciones 7–12 siguen locales, sin nuevo commit/push. Continúan clonación entre playbooks, ejecución guiada y evidencias.

### Clonación autorizada de playbooks

La [Iteración 13](OpenIRP_Documentacion/Iteracion13_Clonacion_Playbooks.md) permite copiar publicaciones vigentes a playbooks independientes en DRAFT, con nuevos UUID para todo el grafo y procedencia inmutable. Admite fuentes públicas y privadas de la misma organización; impide exportar contenido privado a otro tenant o al ámbito público.

No hereda aprobaciones ni publica automáticamente. Las referencias de catálogo deben seguir activas, y una edición o retirada concurrente de la fuente se resuelve bajo el gate compartido con comprobación de revisión.

**490/490 pruebas correctas en PostgreSQL 17 y 18**, sin fallidas ni omitidas; 39 casos nuevos y 127 enfocados de playbooks. Compilación limpia y EF sin diferencias. Migración local tras respaldo/restauración verificados; datos existentes conservados. Las iteraciones 7–13 siguen locales, sin nuevo commit/push. Siguiente bloque: ejecuciones, tareas y dependencias.

### Ejecuciones y tareas iniciales

La [Iteración 14](OpenIRP_Documentacion/Iteracion14_Ejecuciones_Tareas.md) permite iniciar una publicación vigente sobre un incidente en respuesta, generar tareas, asignarlas y avanzar según dependencias. Completar/cancelar queda auditado y no cierra el incidente automáticamente.

Una ejecución activa bloquea cambiar categoría o abandonar respuesta. Las versiones retiradas bloquean avance salvo continuidad autorizada para el aviso de seguridad vigente. Aprobaciones, evidencias y omisiones conservan sus controles específicos.

**531/531 pruebas correctas en PostgreSQL 17 y 18**, sin fallidas ni omitidas; 41 nuevas y 90 enfocadas de ejecución/ciclo de vida. Compilación limpia, EF sin diferencias y migración local tras respaldo/restauración verificados; datos conservados. Las iteraciones 7–14 siguen locales, sin nuevo commit/push. En esta entrega las evidencias, aprobaciones y excepciones seguían pendientes. La aprobación de pasos se incorpora en la Iteración 15.


### Aprobación de pasos de ejecución

La [Iteración 15](OpenIRP_Documentacion/Iteracion15_Aprobaciones_Pasos.md) permite al responsable solicitar autorización de pasos y a ORG_ADMIN aprobar/rechazar. La autoaprobación exige excepción explícita justificada. Reasignar invalida solicitudes/aprobaciones previas, incluso con la misma membresía; cada decisión queda en historia append-only.

La autorización no reemplaza dependencias ni evidencias, no permite omitir tareas y no resuelve/cierra incidentes. Se conservan los bloqueos por versión retirada. Hay tres endpoints nuevos con ámbito por organización/incidente/tarea y CSRF.

**565/565 pruebas correctas en PostgreSQL 17 y 18.3**, sin fallidas ni omitidas, incluidas 34 nuevas. Release sin errores/advertencias y EF sin diferencias. HTTPS: health/OpenAPI 200 y 41 operaciones anónimas 401. Instancias temporales detenidas.

**Base habitual actualizada (2026-10-25):** Docker recuperado; StepApprovals aplicada a openirp_dev después de respaldo/restauración completos verificados y cotejo de conteos/historial. Dieciséis migraciones; datos existentes conservados y tabla de aprobaciones vacía. La guía de Iteración 15 contiene la evidencia. Sin nuevo commit/push; frontend preservado.


### Evidencias de incidente y tarea

La [Iteración 16](OpenIRP_Documentacion/Iteracion16_Evidencias_Incidente_Tarea.md) incorpora TEXT y REFERENCE, registro/consulta/historial y retirada justificada por ORG_ADMIN. Los vínculos y contenido son inmutables; las retiradas ocultan título/contenido y conservan la trazabilidad.

Completar un paso RequiresEvidence exige seleccionar una evidencia activa de esa tarea exacta. La tarea conserva completionEvidenceId como soporte histórico incluso si la evidencia se retira después. Siguen aplicando STEP_APPROVAL, dependencias y permisos. FILE y RESTRICTED permanecen pendientes.

**604/604 pruebas correctas en PostgreSQL 17 y 18.3**, incluidas 39 nuevas; Release sin errores/advertencias y EF sin diferencias. Migración local aplicada tras respaldo/restauración verificados: 17 migraciones y datos existentes conservados. HTTPS: 46 operaciones presentes, anónimos 401. Sin nuevo commit/push; frontend preservado.


### Archivos privados de evidencia

La [Iteración 17](OpenIRP_Documentacion/Iteracion17_Archivos_Privados_Evidencia.md) añade carga multipart PDF/PNG/JPEG/TXT hasta 10 MB, SHA-256, descarga autenticada, historial y recuperación administrativa. Los archivos se conservan en un directorio privado; retirar bloquea nuevas descargas y no purga los bytes.

ci/Start-LocalApi.ps1 configura por defecto .openirp-data/evidence (ignorado por Git/grafos); admite -EvidenceRoot y la variable EvidenceFiles__RootPath. Usando otro arranque, configurar una raíz absoluta privada. La validación de formatos/hash no sustituye antivirus. RESTRICTED y purga siguen pendientes.

**639/639 de regresión completa en PostgreSQL 17 y 18.3; 36/36 finales de archivos por motor después del ajuste de commits inciertos.** Son conjuntos superpuestos, no se suman. Compilación limpia, EF sin diferencias; 18 migraciones locales tras respaldo/restauración verificados, datos conservados y sin archivos ficticios en la base habitual. HTTPS: 51 operaciones comprobadas, anónimos 401. Sin nuevo commit/push.

### Omisiones autorizadas de tareas

La [Iteración 18](OpenIRP_Documentacion/Iteracion18_Omisiones_Autorizadas.md) incorpora solicitud y decisión
SKIP_AUTHORIZATION con impacto, riesgo residual y contexto de dependencias. ORG_ADMIN revisa cada omisión;
la autoaprobación exige excepción justificada. La aprobación cambia la tarea a SKIPPED transaccionalmente.
Un paso omitido no satisface dependencias ni equivale a una acción completada. Finalizar la ejecución no cierra el incidente.

Cualquier cambio intermedio del incidente/ejecución invalida el contexto de la solicitud. Se conserva el historial.
Las tres rutas de solicitud, decisión y consulta aparecen también en [Swagger local](OpenIRP_Documentacion/Swagger_Local.md).
Las excepciones individuales y su revocación están implementadas. Siguen pendientes alternativas verificadas y suficiencia técnica; la evaluación y aceptación de riesgo de cierre está implementada en la Iteración 26; la continuidad por seguridad se describe en la Iteración 23.
Los cambios nuevos continúan locales.

### Cobertura declarada de capacidades

La [Iteración 19](OpenIRP_Documentacion/Iteracion19_Cobertura_Capacidades_Ejecucion.md) compara los requisitos
de ejecución/tarea con las capacidades efectivas actuales del incidente. Expone disponibilidad declarada,
brechas, desconocidos, referencias inactivas y contexto de revisión, conservando REQUIRED/RECOMMENDED y limitaciones.

Son dos consultas GET autorizadas, sin cambios de datos ni migraciones. RequiresHumanReview permanece true:
el resultado no otorga permisos ni sustituye evidencia, aprobaciones, dependencias o revisión técnica.
Los estados y requisitos históricos se conservan; el perfil y catálogo mostrados corresponden a la lectura actual.

### Excepciones individuales de dependencias

La [Iteración 20](OpenIRP_Documentacion/Iteracion20_Excepciones_Dependencias.md) permite solicitar y decidir
una excepción para cada predecesor omitido. Exige revisión ORG_ADMIN, impacto, riesgo residual y contexto vigente.
No modifica la versión ni marca al predecesor como completado; conserva aprobación de paso y evidencia.

Una autorización se invalida al reasignar o cambiar el contexto relevante. Las alternativas verificadas siguen pendientes. La continuidad por seguridad se documenta en la Iteración 23. Las tres rutas están en Swagger Development.

## Estado actual de dependencias

GET `.../executions/{execution}/tasks/{task}/dependency-readiness` muestra predecesores directos,
excepciones vigentes y metadatos de su última solicitud/decisión. Una aprobación histórica puede haber caducado.
La satisfacción de dependencias no sustituye los otros controles de avance.
[Contrato y alcance de Iteración 21](OpenIRP_Documentacion/Iteracion21_Vigencia_Dependencias.md).

## Revocación de excepciones

ORG_ADMIN puede revocar una aprobación de dependencia con motivo y revisiones vigentes. Si la tarea está en curso, queda BLOCKED en la misma transacción. El historial se conserva y una aprobación revocada no vuelve a habilitar su arista. GET dependency-readiness muestra la referencia de revocación.
[API y verificaciones de Iteración 22](OpenIRP_Documentacion/Iteracion22_Revocacion_Excepciones.md).

## Bloqueo de seguridad y continuidad

La retirada explícita por seguridad genera un aviso identificado. ORG_ADMIN puede autorizar continuidad o cancelar una ejecución con el ID vigente, motivo y revisiones. Otro aviso invalida autorizaciones anteriores; se conservan aprobación de paso, dependencias y evidencia.
[API y verificación de Iteración 23](OpenIRP_Documentacion/Iteracion23_Bloqueo_Seguridad_Continuidad.md).

## Alcance y limitaciones

OpenIRP se desarrolla como un proyecto académico de investigación tecnológica aplicada.

El alcance inicial contempla:

- Desarrollo de un prototipo funcional.
- Playbooks orientados a incidentes previamente seleccionados.
- Recomendación contextual de procedimientos.
- Asistencia mediante modelos de IA locales.
- Validación mediante escenarios simulados.

**Quedan fuera del alcance inicial:**

- Detección automática de amenazas en tiempo real.
- Sustitución de plataformas SIEM, EDR o SOAR.
- Ejecución autónoma de acciones sobre sistemas empresariales.
- Intervención directa en infraestructuras productivas.
- Entrenamiento de un modelo fundacional de inteligencia artificial.

El prototipo no sustituye la responsabilidad profesional ni la evaluación humana de decisiones críticas de ciberseguridad.

## Evaluación del proyecto

La evaluación de OpenIRP se realizará mediante escenarios controlados de respuesta a incidentes, con el propósito de analizar:

- Pertinencia de los playbooks recomendados.
- Adecuación de los procedimientos al contexto organizacional.
- Funcionamiento de la plataforma.
- Utilidad de la asistencia mediante inteligencia artificial.
- Claridad y aplicabilidad de las recomendaciones generadas.

Los resultados y las limitaciones identificadas serán documentados durante el proceso de investigación.

## Hoja de ruta

Las principales etapas previstas son:

- [ ] Análisis de requisitos y revisión de soluciones existentes.
- [ ] Diseño del catálogo inicial de playbooks.
- [ ] Definición de la arquitectura de la plataforma.
- [ ] Implementación de los componentes web.
- [ ] Desarrollo del mecanismo de recomendación contextual.
- [ ] Integración de inteligencia artificial local.
- [ ] Implementación de la ejecución guiada.
- [ ] Validación mediante escenarios simulados.
- [ ] Elaboración de documentación técnica.
- [ ] Publicación de una versión inicial reproducible.

*La lista corresponde a los hitos previstos, no a un registro certificado del avance actual.*

## Referencias y fundamentos

La investigación considera como referencias técnicas los siguientes marcos y estándares:

- [NIST — Computer Security Incident Handling Guide (SP 800-61)](https://csrc.nist.gov/pubs/sp/800/61/r2/final)
- [NIST Cybersecurity Framework](https://www.nist.gov/cyberframework)
- [MITRE ATT&CK](https://attack.mitre.org/)
- [CISA — Cybersecurity Resources](https://www.cisa.gov/resources-tools)
- [OASIS — CACAO Security Playbooks](https://www.oasis-open.org/committees/cacao/)

Estas referencias orientan el estudio y diseño; no implican que OpenIRP implemente completamente cada estándar.

## Contribuciones

OpenIRP tiene como propósito evolucionar hacia un proyecto colaborativo de código abierto.

Se consideran especialmente valiosas las contribuciones relacionadas con:

- Diseño y revisión de playbooks.
- Mejoras de seguridad y calidad del código.
- Documentación técnica.
- Pruebas y validación de escenarios.
- Accesibilidad y experiencia de usuario.
- Identificación de errores y propuestas de mejora.

Las contribuciones podrán canalizarse mediante *issues* y *pull requests*, de acuerdo con las políticas que se establezcan para el repositorio.

## Licencia

OpenIRP se propone distribuir bajo la **GNU Affero General Public License v3.0 (AGPL-3.0)**.

Esta licencia permite el uso, modificación y distribución del software, incluyendo fines comerciales, bajo las condiciones establecidas en su texto.

Consulta el archivo `LICENSE` para conocer los términos completos una vez incorporado al repositorio.

## Preparación para el cierre de incidentes

GET `/api/organizations/{organization}/incidents/{incident}/closure-readiness` reúne los controles actuales,
las acciones disponibles al lector y conteos de ejecuciones, tareas y evidencias en una instantánea consistente.
Es informativo: requiere revisión humana y no certifica riesgo residual ni integridad física de archivos.
[Contrato, límites y verificaciones de Iteración 24](OpenIRP_Documentacion/Iteracion24_Preparacion_Cierre.md).

La [Iteración 25](OpenIRP_Documentacion/Iteracion25_Contexto_Revision_Cierre.md) agrega closureContextToken y
expectedClosureContext opcional en las tres escrituras de cierre para detectar contexto desactualizado.
La [Iteración 26](OpenIRP_Documentacion/Iteracion26_Evaluacion_Riesgo_Cierre.md) exige evaluación y aceptación
vigente de ORG_ADMIN en TODOS los cierres. GET/POST `.../incidents/{incident}/closure-risk-reviews` consulta
historia y registra evaluación explícita; rechazo o contexto cambiado impiden cerrar. La aprobación adicional
por máximo histórico HIGH/CRITICAL se conserva. Historial inmutable y guards SQL; sin inventar evaluaciones
para cierres anteriores. La huella por sí sola no es aceptación de riesgo.

La [Iteración 27](OpenIRP_Documentacion/Iteracion27_Soporte_Evidencias_Ejecucion.md) añade GET
`.../incidents/{incident}/executions/{execution}/evidence-readiness`: soporte actual de evidencia seleccionada
por tarea, referencias retiradas y metadatos FILE no disponibles, en una instantánea con RiskContextToken.
Requiere revisión humana; no certifica integridad física ni suficiencia técnica y no modifica el cierre.

## Funciones manuales previas a IA

Las [iteraciones 28–32](OpenIRP_Documentacion/Entrega_PreIA_Iteraciones28_32.md) incorporan revisión técnica humana
de evidencias y alternativas por tarea, con método, soporte, decisión y contexto vigente. GET
`.../executions/{execution}/operational-reviews` consulta historia; POST
`.../executions/{execution}/tasks/{task}/operational-reviews` registra revisión ORG_ADMIN local.
Una alternativa validada conserva la autorización independiente de cada excepción de dependencia.

Los avisos de seguridad se heredan transitivamente por procedencia y exigen una decisión por ejecución;
el receptor recibe un aviso genérico. RESTRICTED se habilita exclusivamente para ORG_ADMIN local,
incluidos metadatos FILE y descarga; PLATFORM_ADMIN no obtiene acceso implícito.

POST `.../evidence/{evidence}/file/verification` verifica bytes administrativamente y deja auditoría;
no libera cuarentenas ni elimina archivos. `ci/Inspect-EvidenceStorage.ps1` diagnostica objetos/temporales
sin purga, con raíz explícita, límites y salida nueva. Ninguna de estas operaciones integra IA local,
ejecuta acciones externas ni certifica automáticamente suficiencia técnica o ausencia de malware.

## IA local: explicación supervisada de pasos

La [Iteración 33](OpenIRP_Documentacion/Iteracion33_Asistencia_Local_Explicacion_Pasos.md) incorpora GET
`.../tasks/{task}/assistance-context`, POST `.../tasks/{task}/assistance` y GET
`.../tasks/{task}/assistance-assessments`. Usa solo **qwen3:4b-instruct sin thinking**, con `think=false`
y `/no_think`, contexto mínimo autorizado y respuesta estructurada no confiable hasta revisión humana.
No ejecuta tareas, cambia controles ni guarda respuestas originales. Los fallos conservan el trabajo manual.

Está deshabilitada por defecto. Para probar con el modelo local ya instalado: establecer
`$env:LocalAI__Enabled='true'` en PowerShell y ejecutar `./ci/Start-LocalApi.ps1`.
La guía detalla permisos, precondiciones y límites; aceptar/rechazar definitivamente y convertir propuestas
siguen pendientes. La etiqueta base qwen3:4b no se usa como alternativa.

## Aviso de seguridad

OpenIRP es una herramienta de apoyo a la respuesta a incidentes y se encuentra en desarrollo.

Los playbooks y las recomendaciones generadas deben ser evaluados por personal responsable antes de su aplicación.

El software se proporciona sin garantías de idoneidad para entornos productivos o situaciones de emergencia.

---

**OpenIRP — Respuesta a incidentes más accesible, estructurada y adaptable.**

*Proyecto académico de investigación y desarrollo en Ingeniería en Ciberseguridad.*
