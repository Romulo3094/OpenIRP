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

La base del entorno de desarrollo usa PostgreSQL 17 y Ollama mediante Docker Compose. Para preparar el entorno local, copia `infra/.env.example` a `infra/.env` y cambia `POSTGRES_PASSWORD`. Antes de ejecutar Compose, crea el volumen externo con `docker volume create openirp_ollama_data`; desde la carpeta `infra`, ejecuta `docker compose up -d`. Se requiere Docker con acceso a GPU NVIDIA para la configuración actual de Ollama (`gpus: all`). El modelo de IA debe descargarse por separado.\n\nPara la API, configura la conexión sin subir secretos: `dotnet user-secrets set "ConnectionStrings:OpenIRP" "Host=127.0.0.1;Port=5433;Database=openirp_dev;Username=openirp;Password=TU_PASSWORD" --project apps/api/OpenIRP.Api`. Después ejecuta `dotnet run --project apps/api/OpenIRP.Api`. La configuración de desarrollo usa el puerto HTTP `5195` y HTTPS `7113` (consulta `launchSettings.json`). El endpoint `/api/health` responde de forma estática y todavía no comprueba la conectividad con PostgreSQL ni Ollama.\n\n**Estado:** la infraestructura inicial y la capa Identity están en desarrollo. La primera migración y las pruebas transaccionales de las organizaciones siguen pendientes.

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
