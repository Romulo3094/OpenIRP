# Iteración 13 — Clonación autorizada de playbooks

Fecha: 2026-10-23. Continúa el versionado y publicación de la Iteración 12.

> Continuación: la [Iteración 14](Iteracion14_Ejecuciones_Tareas.md) incorpora ejecuciones y tareas iniciales. Las referencias a ese trabajo como siguiente bloque describen el cierre de la Iteración 13.

## Comportamiento implementado

Un administrador puede copiar una versión publicada vigente a un playbook independiente, con versión inicial 1 DRAFT y revisión 1. Se copian exactamente el título/categoría de la versión, objetivo, instrucciones, pasos, posiciones/fases, flags, dependencias y requisitos de capacidades. Cada agregado, versión, paso, enlace y requisito recibe UUID nuevo.

La copia no recibe puntero vigente, fecha de publicación, solicitud de revisión, decisiones ni excepciones de autoaprobación de la fuente. Debe completar su propia revisión/publicación. La creación y todos los nodos confirman junto con SUCCESS de auditoría o se revierten juntos.

La fuente no cambia de contenido, revisión o decisiones por ser clonada. No se copian perfiles de disponibilidad de capacidades de organizaciones ni evaluaciones de incidentes.

## Matriz de permisos

| Fuente | Destino | Permiso |
|---|---|---|
| PUBLIC vigente | PRIVATE | ORG_ADMIN activo del destino |
| PUBLIC vigente | PUBLIC | PLATFORM_ADMIN activo |
| PRIVATE vigente | PRIVATE de la misma organización | ORG_ADMIN activo de esa organización |
| PRIVATE | PRIVATE de otra organización | Prohibido, incluso con administración en ambas |
| PRIVATE | PUBLIC | Prohibido, incluso con grant global y membresía privada |

El ámbito de destino procede de la ruta. No se aceptan permisos globales implícitos para tenants ni decisiones de ámbito del cuerpo. ANALYST/VIEWER pueden consultar publicaciones según el módulo anterior, pero no clonar ni consultar motivos administrativos de procedencia.

Antes de copiar se comprueban actor/cuenta/lockout, rol y membresía/organización del destino, fuente visible para ese destino, UUID exacto de versión vigente, estado PUBLISHED, published_at y revisión actual del agregado fuente. No se admiten DRAFT, IN_REVIEW, ARCHIVED ni publicaciones históricas que hayan dejado de ser vigentes.

La revisión observada es la del agregado fuente, disponible en su consulta de vigente. Incluso editar un borrador posterior de la fuente cambia ese token y obliga a refrescarlo. Se copia siempre la versión publicada indicada, no el título/instrucciones privados del borrador posterior.

Categoría y capacidades referenciadas deben existir y seguir activas en el catálogo al clonar. No se eliminan requisitos inactivos silenciosamente: se devuelve conflicto. La disponibilidad organizacional no se infiere de los requisitos.

## Endpoints y contratos

POST /api/platform/playbooks/clones crea PUBLIC, reservado a PLATFORM_ADMIN.

POST /api/organizations/{organization}/playbooks/clones crea PRIVATE, reservado a ORG_ADMIN del destino.

Ambos devuelven 201 con Location del nuevo agregado y su contenido DRAFT. Cookie autenticada, CSRF X-CSRF-TOKEN, no-store y límite administrativo de cuerpo de 1 MiB.

```json
{
  "sourcePlaybookId": "UUID-del-playbook-fuente",
  "sourceVersionId": "UUID-de-su-publicacion-vigente",
  "expectedSourceRevision": 3,
  "reason": "Adaptar el procedimiento para esta organización"
}
```

Los tres identificadores/token son obligatorios en JSON; reason no vacío hasta 2000 caracteres. Los UUID de destino y nodos son del servidor. El cuerpo no puede elegir organizationId, visibilidad, estado, aprobaciones o vigencia. Cada POST aceptado crea una copia independiente; no ofrece idempotencia automática de reintentos ni exige unicidad de títulos.

Revisión obsoleta o fuente no vigente: 409. Fuente ausente o privada fuera del ámbito permitido: 404 después de autorizar el destino. Sin autorización de destino: 403; anónimo: 401. No se confirma la existencia de contenido de otra organización.

La consulta administrativa de procedencia devuelve 200:

- GET /api/platform/playbooks/{playbook}/origin
- GET /api/organizations/{organization}/playbooks/{playbook}/origin

Devuelve playbookId, sourcePlaybookId, sourceVersionId, sourceRevision, reason, createdByUserId y createdAt. Para creación ordinaria, los cuatro campos de origen/motivo son null. Para una copia, el motivo queda conservado e inmutable. Solo los administradores autorizados acceden a este contrato; el motivo no aparece en consultas de publicaciones ni en auditoría mínima.

La respuesta de contenido añade al objeto playbook los campos sourceVersionId y sourceRevision. Conviven dos relaciones con nombres similares:

| Campo | Significado |
|---|---|
| playbook.sourceVersionId | Publicación externa que originó el playbook clonado; inmutable durante toda su vida |
| playbook.sourceRevision | Revisión del agregado fuente observada en esa creación |
| sourceVersionId del contenido de versión | Predecesora dentro del mismo playbook, introducida en Iteración 12 |
| sourcePlaybookId de /origin | Agregado al que pertenece la publicación externa, resuelto desde la FK |

Por eso la versión 1 del clon tiene sourceVersionId interno null; si crea su propia versión 2, el interno apunta a su versión 1 mientras playbook.sourceVersionId sigue apuntando a la publicación original.

## Independencia y límites

Editar la copia no modifica la fuente. Una retirada administrativa posterior de la fuente no borra ni cambia las copias existentes, su contenido o procedencia. La copia aún debe superar su propia revisión/publicación con los permisos y referencias vigentes de ese momento.

Esta entrega no interpreta retiradas por seguridad ni propaga bloqueos a descendientes o ejecuciones. El modelo security_hold y las autorizaciones de continuidad se incorporarán con ejecución/tareas. La procedencia conservada permite identificar la fuente para revisión posterior, sin afirmar que exista esa propagación.

No se modifica frontend, no se crean ejecuciones/tareas/evidencias/IA y no se publican copias automáticamente. El trabajo de apps/web encontrado en el workspace se preservó.

## Persistencia y concurrencia

La escritura reutiliza el advisory gate compartido (1869636978,26), READ COMMITTED, timeout de bloqueo de cinco segundos y revalidación de autorización después del bloqueo. Fuente/revisión/vigencia y referencias se verifican dentro de esa transacción. Una retirada o edición concurrente de la fuente produce una copia coherente anterior al cambio o un conflicto que exige refrescar.

Origin usa lectura compuesta REPEATABLE READ con autorización administrativa. Rechazos se auditan aparte en ámbito global, sin copiar identificadores no autorizados al log de otra organización.

SUCCESS registra destino, actor, UUID del nuevo agregado, revisión/versión y referencias mínimas de procedencia. No copia motivo ni instrucciones. El motivo se almacena en la procedencia administrativa privada del agregado.

Migración 20261024021251_PlaybookCloning:
- Añade tres columnas nullable sin DEFAULT en app.playbooks: source_version_id UUID, source_revision bigint y clone_reason varchar(2000).
- FK restrict hacia la versión fuente, índice de consulta y check que exige origen completo o campos todos null.
- Trigger de creación que verifica fuente vigente/publicada, revisión coincidente y ámbito público o misma organización. Bloquea filas de fuente/versión con FOR SHARE durante la comprobación para coordinar esos cambios.
- Trigger de actualización que impide cambiar/borrar procedencia o motivo, incluso después de retirar la fuente.
- Down se detiene si existe cualquier procedencia; no elimina silenciosamente esa relación. Puede retirar el módulo si solo hay playbooks ordinarios sin datos de clonación.
- No cambia tablas/filas anteriores ni la relación de predecesoras internas del versionado. El módulo queda con seis tablas, 53 columnas, 18 FK, 15 checks y 15 guards.

El LOGIN SQL restringido ya dispone de INSERT necesario para crear la copia; no recibe UPDATE de las columnas de procedencia. RLS y separación operativa completa de credenciales siguen pendientes. Los controles de rol del usuario final pertenecen al servicio; las credenciales del propietario pueden alterar objetos.

## Archivos de esta entrega

En apps/api:
- Domain/Entities/PlaybookEntities.cs.
- Application/Playbooks/PlaybookCloneContracts.cs e IPlaybookDraftService.cs.
- Infrastructure/Playbooks/PlaybookCloneService.cs; proyección de procedencia en PlaybookDraftService.cs y PlaybookPublicationService.cs.
- Infrastructure/Persistence/Configurations/PlaybookMapping.cs y migración/snapshot.
- Api/Controllers/PlaybookCloneControllers.cs.
- IntegrationTests/PlaybookCloneTests.cs y PlaybookClonePersistenceTests.cs.
- Adaptación de la prueba previa de downgrade en PlaybookDraftPersistenceTests.cs: una cadena de Down puede confirmar pasos anteriores antes de encontrar otro guard; se restaura el esquema actual antes de consultar con el modelo actual.

Además: README, planificación maestra y nota de avance en Iteración 12. No se modificó apps/web.

## Verificación

- Compilación Release: cero errores y cero advertencias. EF sin diferencias de modelo pendientes.
- Módulo de playbooks: **127/127 pruebas correctas** en PostgreSQL 17, sin fallidas ni omitidas. Incluye los 88 casos anteriores y **39 casos nuevos de integración** de clonación. TRX: artifacts/clone-focused-pg17-1dfdd3f7478641a0b5484998d022478a/paso26-pg17.trx.
- Regresión completa PostgreSQL 17: **490/490 correctas**, cero fallidas/omitidas. TRX: artifacts/paso26-pg17-542551579640484a91c8841a665acf9c/paso26-pg17.trx.
- Regresión completa PostgreSQL 18.3 nativo aislado: **490/490 correctas**, cero fallidas/omitidas. TRX: artifacts/cloning-full-pg18-b9b224974c134b53988a4f0481b6ce3e/paso26.trx.
- Los enfocados y los 39 nuevos ya están incluidos en los 490, no se suman otra vez. Cobertura: tres rutas de copia autorizadas, conservación íntegra y UUID nuevos, estados/flags/aprobaciones independientes, prohibición de exportación privada incluso con doble administración, roles/contextos inactivos, fuente no vigente/revisión obsoleta/borrador confidencial posterior, catálogo inactivo, motivos, carreras con retirada/edición, independencia posterior y versiones propias, fallos de auditoría/nodos, SQL de procedencia y ámbitos, upgrade/downgrade, HTTP/CSRF y LOGIN SQL restringido.
- La primera ejecución aprobó los 39 nuevos y detectó una prueba anterior que consultaba el modelo actual después de una cadena de Down parcialmente confirmada y bloqueada por otro guard. Se corrigió el fixture para volver a aplicar el esquema actual antes de consultar; la repetición aprobó los 127.
- Migración aplicada únicamente a openirp_dev (PostgreSQL 17 habitual, 127.0.0.1:5433), tras restaurar un dump completo en otro contenedor sin puertos ni volúmenes. Conteos e historial cotejados antes de aplicar; ninguna cantidad existente cambió.
- Se conservaron vacías las tablas de negocio/identidad/auditoría/playbooks, ocho capacidades y tres categorías. Los campos nuevos quedaron null y no se fabricaron cuentas, clones ni publicaciones en la base habitual. Historial con catorce migraciones.
- Respaldo, SQL incremental, conteos y scripts: artifacts/playbook-cloning-dev-64af77d210044d7bbbeb5639476bb86f/. Dump openirp_dev-before-playbook-cloning.dump, SHA256 **63541323C2C0FB04F241600A7C8D94D0029D22D3170AA4C5E30E78B01DD46845**.
- El SQL Up revisado no incluye DROP/TRUNCATE/DELETE/UPDATE de datos. Se verificaron seis tablas, 53 columnas, 18 FK, 15 checks, 15 guards, índice de fuente y tres columnas de procedencia nullable sin DEFAULT.
- HTTPS real: health/OpenAPI 200, 30 operaciones de playbooks presentes y accesos anónimos 401. Evidencia: artifacts/playbook-cloning-http-64324e209a4d45fa8464b9cc965f9940/. Los flujos autenticados se probaron en bases temporales.
- Instancias API/PostgreSQL propias detenidas; sin listeners en 55432, 55433, 7113 ni 5195. PostgreSQL/Ollama habituales y el trabajo de apps/web se preservaron.
- Cambios locales, sin nuevo commit, push ni merge.

No se detectaron bloqueantes para continuar. Siguiente bloque: ejecución guiada de una publicación vigente sobre un incidente, generación de tareas y dependencias; después evidencias, aprobaciones/excepciones y seguridad de continuidad/cierre.
