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

Esta subfase conserva el estado REGISTERED: evaluación CIA, severidad, respuesta y cierre siguen pendientes de la regla determinista y sus controles. No se asigna severidad baja por omisión. El avance previo se publicó en `d7ced5f`; los cambios de incidentes permanecen locales, posteriores a ese commit.

### Activos afectados por incidente

La Iteración 4 permite reportar activos todavía no inventariados, adjuntar activos existentes y vincularlos después conservando nombre, tipo, contexto y criticidad históricos. Cada operación exige motivo y revisión del incidente, con permisos por organización, snapshots y auditoría transaccionales. La criticidad desconocida debe expresarse como null, sin asumir un nivel bajo.

La nueva migración está aplicada en la base local tras respaldo y restauración aislada verificados. Validación de esa subfase: **127/127 pruebas en PostgreSQL 17 y 127/127 en PostgreSQL 18**. Consulta [endpoints, archivos, evidencia y pendientes](OpenIRP_Documentacion/Iteracion4_Activos_Incidente.md). Los cambios nuevos de incidentes y asociaciones siguen locales, sin otro commit/push.

### Capacidades y limitaciones por incidente

La Iteración 5 permite heredar el perfil de la organización o evaluar disponibilidad específica del incidente, con motivo, limitaciones e historial. Unknown explícito es distinto de heredar. Se comprueban las revisiones del incidente, perfil y catálogo para rechazar decisiones basadas en contexto obsoleto. ANALYST puede documentar el incidente; el perfil general sigue reservado a ORG_ADMIN.

Migración local aplicada tras respaldo y restauración de prueba, conservando los datos previos. Validación de esa subfase: **156/156 pruebas en PostgreSQL 17 y 156/156 en PostgreSQL 18**, sin fallidas ni omitidas. Consulta [endpoints, archivos y evidencias](OpenIRP_Documentacion/Iteracion5_Capacidades_Incidente.md). Los cambios continúan locales sin nuevo commit/push; CIA, regla de severidad y ciclo de vida completo siguen pendientes.

### Evaluación de impacto CIA

La Iteración 6 permite evaluar confidencialidad, integridad y disponibilidad con None/Low/Medium/High o null explícito cuando falta investigar. Exige motivo y revisión, protege datos por organización y conserva CIA en el historial de todas las modificaciones posteriores. No deriva severidad ni autoriza cierre automáticamente.

Migración local aplicada tras respaldo/restauración verificados, conservando datos previos. Suite actual: **181/181 pruebas en PostgreSQL 17 y 181/181 en PostgreSQL 18**, sin fallidas ni omitidas. Consulta [archivos, endpoints y evidencias CIA](OpenIRP_Documentacion/Iteracion6_Evaluacion_CIA.md).

La [propuesta de severidad v1](OpenIRP_Documentacion/Propuesta_Regla_Severidad_v1.md) incluye una matriz comprobada en un prototipo matemático aislado, todavía sin activar en la API. La política de desconocidos está confirmada: Pending y severity=null hasta completar todas las entradas requeridas, sin severidad provisional. Los umbrales siguen sujetos a revisión. Los cambios de desarrollo continúan locales, sin nuevo commit/push.

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

## Aviso de seguridad

OpenIRP es una herramienta de apoyo a la respuesta a incidentes y se encuentra en desarrollo.

Los playbooks y las recomendaciones generadas deben ser evaluados por personal responsable antes de su aplicación.

El software se proporciona sin garantías de idoneidad para entornos productivos o situaciones de emergencia.

---

**OpenIRP — Respuesta a incidentes más accesible, estructurada y adaptable.**

*Proyecto académico de investigación y desarrollo en Ingeniería en Ciberseguridad.*
