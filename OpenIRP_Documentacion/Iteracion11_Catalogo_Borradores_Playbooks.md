# Iteración 11 — Catálogo de playbooks y borradores estructurados

Fecha: 2026-10-23. Primera entrega del módulo de playbooks; continúa el cierre manual de la Iteración 10.

> Nota histórica: esta guía describe la primera entrega de borradores. La [Iteración 12](Iteracion12_Publicacion_Versiones_Playbooks.md) amplía el esquema y el contrato con revisión, publicación, versiones sucesivas, vigencia y retirada. Sus restricciones de solo versión 1 DRAFT se refieren a la entrega anterior.

## Alcance implementado

Catálogo administrativo PUBLIC global (organization_id NULL) y PRIVATE por organización. Se puede crear, listar, consultar y reemplazar transaccionalmente el contenido del primer borrador de cada playbook. Cada agregado tiene UUID y revisión; su versión inicial mantiene UUID y número 1, con estado DRAFT. El esquema rechaza otros números/estados en esta entrega: no existe todavía una versión ejecutable.

PUBLIC describe el ámbito del futuro catálogo publicado. Sus borradores no son anónimos ni visibles para cualquier cuenta. Solo PLATFORM_ADMIN activo administra/consulta los borradores globales. Solo ORG_ADMIN con membresía y organización activas administra/consulta los privados de su organización. ANALYST y VIEWER no acceden a borradores en esta entrega. Un grant global no sustituye membresía/rol privado.

No se habilitan registro público, publicación, clonación, nuevas versiones ni ejecuciones. La siguiente entrega incorporará revisión, publicación inmutable, puntero vigente, retirada y clonación con procedencia; después, ejecución/tareas/evidencias. Las precondiciones de cierre relacionadas con esos módulos siguen pendientes.

## Contrato HTTP

Todos los endpoints exigen cookie autenticada; POST y PUT requieren X-CSRF-TOKEN. Cache-Control no-store. Límite de cuerpo HTTP: 1 MiB.

| Operación | Catálogo global | Catálogo privado |
|---|---|---|
| Listar | GET /api/platform/playbooks | GET /api/organizations/{organization}/playbooks |
| Crear borrador | POST /api/platform/playbooks | POST /api/organizations/{organization}/playbooks |
| Consultar | GET /api/platform/playbooks/{playbook} | GET /api/organizations/{organization}/playbooks/{playbook} |
| Reemplazar borrador | PUT /api/platform/playbooks/{playbook}/draft | PUT /api/organizations/{organization}/playbooks/{playbook}/draft |

Creación devuelve 201 con Location; consulta/listado/edición, 200. Listado acepta skip (0..1000000) y limit (1..100, por defecto 50), orden por UUID y nextSkip; no congela páginas sucesivas. Las consultas de un agregado usan REPEATABLE READ para devolver una vista coherente del grafo y la autorización observada.

El ámbito procede de la ruta y autorización, no del cuerpo. Un UUID de otro playbook/ámbito devuelve 404 tras autorizar el ámbito solicitado; acceso a un ámbito no autorizado devuelve 403. Un usuario anónimo recibe 401.

Ejemplo de creación, usando las referencias iniciales de categoría/capacidad:

```json
{
  "content": {
    "title": "Respuesta inicial a phishing",
    "categoryId": "26000000-0000-4000-8100-000000000001",
    "objective": "Documentar y contener un correo sospechoso",
    "instructions": "Validar el contexto y coordinar las acciones manualmente.",
    "steps": [
      {
        "code": "ANALYZE",
        "position": 10,
        "phase": "Análisis",
        "instructions": "Preservar los antecedentes autorizados.",
        "isRequired": true,
        "requiresApproval": false,
        "requiresEvidence": true
      },
      {
        "code": "CONTAIN",
        "position": 20,
        "phase": "Contención",
        "instructions": "Solicitar autorización antes de contener.",
        "isRequired": true,
        "requiresApproval": true,
        "requiresEvidence": true
      }
    ],
    "dependencies": [
      { "predecessorCode": "ANALYZE", "successorCode": "CONTAIN" }
    ],
    "requirements": [
      {
        "capabilityId": "26000000-0000-4000-8000-000000000001",
        "stepCode": null,
        "level": "Required"
      }
    ]
  }
}
```

PUT usa content con la misma estructura completa, reason no vacío (máximo 2000) y expectedRevision obligatorio. Un cambio aceptado incrementa la revisión incluso si repite el contenido. Revisión obsoleta: 409. reason se valida como declaración explícita de edición; no se conserva como historial narrativo ni se copia a auditoría. Los borradores son mutables y no guardan snapshots históricos de contenido.

La respuesta contiene playbook (identidad, ámbito, categoría, título, revisión, fecha), versionId, versionNumber, status, objective, instructions y el grafo con UUID de pasos, dependencias y requisitos. Durante la edición se reemplazan todos los nodos del borrador con nuevos UUID; permanecen los UUID del playbook y de su versión. Ninguna ejecución puede referirse todavía a estos nodos.

## Validación estructural

- Título obligatorio hasta 200; objetivo obligatorio hasta 4000; instrucciones generales obligatorias hasta 12000 caracteres.
- Hasta 100 pasos, 500 dependencias y 300 requisitos. Se permite borrador sin pasos, como contenido aún incompleto; esto no implica que sea publicable.
- Código de paso: expresión [A-Z][A-Z0-9_]{0,59}, único dentro de la versión. Posición recomendada 1..10000, también única. La posición no determina dependencias.
- Fase es texto obligatorio hasta 60, sin imponer una taxonomía no aprobada. Instrucciones de paso obligatorias hasta 8000.
- IsRequired, RequiresApproval y RequiresEvidence deben aparecer explícitamente en JSON; no se asume false por omisión.
- Dependencias por código de predecesor/sucesor existentes, sin autoenlaces, duplicados ni ciclos, incluso en componentes desconectados. Se usa ordenación topológica iterativa, O(pasos + enlaces).
- Requisito de capacidad aplicable a toda la versión (stepCode null) o a un paso de esa misma versión. Nivel Required o Recommended explícito. No se duplica el mismo requisito por capacidad y ámbito.
- Categoría activa al crear o cambiar. Puede conservarse una categoría luego desactivada. Cada capacidad referenciada en una edición debe existir y estar activa; una referencia desactivada se puede consultar, pero debe retirarse para guardar una nueva edición.
- No se infieren capacidades disponibles, severidad, autorizaciones ni ejecución de acciones a partir de estos requisitos/flags.

## Transacciones, aislamiento y PostgreSQL

Las escrituras comparten el advisory lock (1869636978,26) con identidad, inventario e incidentes, READ COMMITTED y timeout de bloqueo de 5 segundos. La autorización se revalida después del bloqueo. Se exige DbContext limpio; se descarta seguimiento al terminar. El token de revisión se comprueba bajo el bloqueo y además es concurrency token de EF.

Eliminar el grafo anterior, insertar el nuevo, incrementar revisión y anexar SUCCESS a auditoría confirma o revierte en una sola transacción. Un fallo del log revierte también las eliminaciones del grafo. Los rechazos se registran separadamente en el ámbito global, sin copiar identificadores de tenant no autorizados ni contenido privado. SUCCESS registra actor/ámbito/identificador y metadatos de revisión/versión; no instrucciones ni motivos privados.

Migración 20261023231051_PlaybookDraftCatalog:
- Cinco tablas nuevas: playbooks, playbook_versions, playbook_steps, playbook_step_dependencies y playbook_capability_requirements.
- Índices por organización, categoría y creador; versión/número único; código y posición únicos por versión.
- FK compuestas (version_id, step_id) para ambos extremos de dependencias y para requisitos de paso. Impiden enlazar nodos de otro playbook, incluida otra organización.
- Índices únicos parciales distinguen requisitos globales de versión (step_id NULL) y requisitos por paso.
- Checks de revisión, contenido no vacío, códigos, posiciones, niveles, ausencia de autoenlaces y primer estado DRAFT. La validación completa de ciclos reside en el servicio/dominio y se serializa bajo el gate; las FK/checks SQL por sí solos no validan ciclos arbitrarios.
- Triggers impiden cambiar propietario/UUID/creador/fecha, editar el agregado sin avanzar revisión, cambiar identidad/número de versión y eliminar/truncar playbooks/versiones.
- Down rechaza eliminar el módulo si existen playbooks. Up crea objetos nuevos y conserva datos anteriores.
- grant-runtime.sql limita UPDATE a título/categoría/revisión del agregado y objetivo/instrucciones de versión. Los nodos del borrador admiten SELECT/INSERT/DELETE para su reemplazo; el runtime no recibe UPDATE sobre su identidad, ámbito o estado ni DELETE del agregado/versiones.

Las credenciales de propietario pueden alterar objetos; RLS y separación operativa completa de credenciales siguen pendientes. No se atribuye aislamiento SQL por usuario final a estas restricciones: la autorización de tenant se aplica en el servicio.

## Archivos de esta entrega

- Domain/Entities/PlaybookEntities.cs y Domain/Playbooks/PlaybookDependencyGraph.cs.
- Application/Playbooks/IPlaybookDraftService.cs.
- Infrastructure/Playbooks/PlaybookDraftService.cs.
- Infrastructure/Persistence/Configurations/PlaybookMapping.cs; integración en OpenIrpDbContext y snapshot/migración.
- Api/Controllers/PlaybookDraftControllers.cs y registro DI en Program.cs.
- IntegrationTests/PlaybookDraftTests.cs, PlaybookDraftPersistenceTests.cs y PlaybookDependencyGraphTests.cs.
- ci/grant-runtime.sql, README y planificación maestra.

Las rutas de código anteriores son relativas a apps/api salvo ci y documentación. Todo se trabajó en D:\Desarrollos\OpenIRP, preservando las iteraciones locales anteriores.

## Resultados verificables

- Release compiló con cero errores y cero advertencias; EF no detectó diferencias de modelo pendientes.
- Pruebas enfocadas: **44/44 correctas** en PostgreSQL 17, cero fallidas/omitidas. Son seis casos de dominio y 38 de integración. TRX: artifacts/playbook-focused-pg17-0995967ced0d45cbb7a0eda8c0daa2f9/paso26-pg17.trx.
- Regresión completa PostgreSQL 17: **407/407 correctas**, cero fallidas/omitidas. TRX: artifacts/paso26-pg17-0ec5d58e886542b5a6241dd41ca88e7e/paso26-pg17.trx.
- Regresión completa PostgreSQL 18.3 nativo aislado: **407/407 correctas**, cero fallidas/omitidas. TRX: artifacts/playbooks-full-pg18-0706134505774c7c81ff84998be0423f/paso26.trx. EF sin diferencias y clúster temporal detenido.
- Son 44 casos nuevos sobre los 363 anteriores; los enfocados están incluidos en los 407. Se probaron creación/edición global y privada, permisos y contexto vigente, aislamiento de ámbitos, DAG/duplicados/nulos/límites, paginación, catálogos inactivos, concurrencia forzada, rollback de creación/reemplazo por fallo de auditoría, upgrade/downgrade, SQL de identidad/referencias/unicidad, HTTP/CSRF y LOGIN SQL limitado.
- Se corrigió durante la validación la precisión del CreatedAt devuelto al crear para que coincida con microsegundos de PostgreSQL; el cliente de pruebas HTTP usa el convertidor de enums del contrato.
- Migración aplicada únicamente a openirp_dev, PostgreSQL 17 habitual en 127.0.0.1:5433, después de restaurar un dump completo en otro contenedor sin puertos ni volúmenes. Conteos e historial restaurados cotejados antes de aplicar; todas las cantidades existentes se conservaron: tablas de negocio/identidad/auditoría vacías, ocho capacidades y tres categorías. Cinco tablas nuevas vacías; historial con doce migraciones.
- Respaldo, SQL incremental, scripts, conteos y comprobaciones: artifacts/playbook-drafts-dev-4101114a8542483fadf53d24f8ec905b/. Dump openirp_dev-before-playbook-drafts.dump, SHA256 **21AEF2D19A7A9BE221C53B66CA9C6519664EFF045C009B4B03140E9A7E058045**.
- La inspección local confirmó cinco tablas, 31 columnas, 11 FK y ocho checks validados, cuatro guards habilitados e índices únicos parciales. El primer ensayo de restauración se detuvo antes de aplicar porque observó el servidor provisional de arranque; el ensayo definitivo esperó disponibilidad TCP, restauró y verificó la copia completa.
- HTTPS real: health y OpenAPI 200, seis rutas/ocho operaciones de playbooks presentes y ocho accesos anónimos 401. Evidencia: artifacts/playbook-drafts-http-a5578bb6dd08419889a79773a93040e7/. Los flujos autenticados se probaron con bases temporales, sin fabricar cuentas en openirp_dev.
- Los procesos propios API/PostgreSQL y contenedores temporales se detuvieron; PostgreSQL/Ollama habituales se conservaron. No hay listeners temporales en 55432, 55433, 7113 ni 5195.
- Se preservaron las modificaciones locales de las iteraciones anteriores. Esta entrega permanece local, sin nuevo commit, push ni merge.

Siguiente entrega: revisión y publicación de versiones con contenido inmutable, puntero vigente, retirada y clonación autorizada. No hay bloqueantes detectados para continuar; esos flujos y ejecución/evidencias siguen pendientes de implementación.
