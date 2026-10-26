# Iteración 12 — Revisión, publicación y vigencia de versiones

Fecha: 2026-10-23. Continúa el catálogo administrativo de la Iteración 11.

> Continuación: la [Iteración 13](Iteracion13_Clonacion_Playbooks.md) incorpora la clonación autorizada entre playbooks con procedencia externa. Las referencias a clonación pendiente en esta guía describen el alcance de la Iteración 12.

## Resultado y alcance

Se implementa DRAFT → IN_REVIEW → PUBLISHED, rechazo IN_REVIEW → DRAFT y retirada PUBLISHED → ARCHIVED. Solo PLATFORM_ADMIN administra/revisa publicaciones globales; solo ORG_ADMIN activo de la organización administra/revisa las privadas. No se amplían permisos administrativos de ANALYST/VIEWER ni acceso global a tenants.

Una versión publicada puede originar una nueva versión DRAFT del mismo playbook. Se numeran de forma creciente bajo el gate compartido; MAX(version_number) se utiliza exclusivamente para numeración. La vigencia siempre procede de current_published_version_id. Una única versión DRAFT o IN_REVIEW puede existir por playbook.

El nuevo borrador recibe UUID nuevos para versión, pasos, dependencias y requisitos; source_version_id conserva su procedencia dentro del mismo playbook, con FK compuesta. El contenido de la fuente publicada permanece intacto.

La clonación entre playbooks/ámbitos todavía está pendiente. Tampoco se crean ejecuciones, tareas, adjuntos, recomendaciones de IA ni bloqueos security_hold de ejecuciones. La retirada de esta entrega es administrativa; el tratamiento específico de retirada por seguridad se integrará al desarrollar ejecuciones. Las condiciones de cierre de incidentes relacionadas con esos módulos siguen pendientes.

## Revisión y decisión humana

Enviar a revisión exige al menos un paso y vuelve a validar todo el contenido, DAG y referencias activas de categoría/capacidades. Genera un reviewRequestId nuevo y registra solicitante, motivo, revisión del agregado y hash de contenido. La revisión congela la versión y todos sus nodos.

Rechazar conserva el motivo y la solicitud en el historial append-only y devuelve la versión a DRAFT. La próxima solicitud tiene otro UUID; no se puede reutilizar una decisión de una solicitud anterior.

Publicar exige expectedRevision vigente, solicitud actual IN_REVIEW y autorización actual del revisor. También se revalida la autorización del solicitante y la vigencia de categoría/capacidades: una desactivación durante la revisión no se ignora. Puede rechazarse una solicitud cuyo autor ya no está activo para permitir que otro administrador autorizado la corrija y reenvíe.

La autoaprobación de publicación exige selfApproval=true y selfApprovalJustification no vacía de hasta 2000 caracteres. Se registra explícitamente como excepción; no se presenta como revisión por dos personas. Otro administrador autorizado publica con selfApproval=false y justificación de autoaprobación null. Un rechazo siempre usa false/null. No existe publicación implícita al editar o crear.

Una publicación actualiza el puntero vigente y published_at en la misma transacción que la decisión y SUCCESS de auditoría. La publicación anterior sigue siendo histórica, aunque su estado continúe PUBLISHED; solo el puntero determina cuál está vigente.

## Contenido inmutable y consulta

Título y categoría se guardan también en cada versión. Esto impide que el título/instrucciones privados de un nuevo borrador se filtren a quienes consultan la publicación anterior. Objetivo, instrucciones, pasos, dependencias, requisitos, título y categoría no pueden editarse durante revisión ni después de publicar, incluida una versión ARCHIVED.

La retirada archiva la versión. Si era vigente, el puntero queda NULL sin sustitución automática. Retirar una publicación histórica no borra ni cambia otra vigente. Una versión archivada no se reactiva directamente; puede servir como fuente explícita de un nuevo borrador, que debe completar otra revisión/publicación.

Los administradores consultan cualquier versión por UUID, versiones numeradas y el historial de revisiones. GET administrativo del playbook devuelve su versión de número más alto, que puede ser un borrador o archivada; no equivale a consultar la vigente.

La consulta de publicaciones vigentes exige cuenta autenticada activa. Las globales están disponibles para esas cuentas; las privadas requieren organización y membresía activas, incluidos ANALYST/VIEWER. Un grant PLATFORM_ADMIN sin membresía no concede lectura privada. La consulta histórica general sigue siendo administrativa; el acceso histórico asociado a futuras ejecuciones se integrará con ese módulo.

Los listados de publicaciones solo devuelven título/categoría de la versión apuntada, nunca los del borrador. No se habilita acceso anónimo ni registro público.

## Endpoints

Se conservan los endpoints de la Iteración 11. Para administración, usar uno de estos prefijos:

- Global: /api/platform/playbooks/{playbook}
- Privado: /api/organizations/{organization}/playbooks/{playbook}

| Método y sufijo | Operación |
|---|---|
| GET /versions?afterNumber=0&limit=50 | Versiones ordenadas por número, máximo 100 por página |
| GET /versions/{version} | Contenido de esa versión, administración |
| POST /versions | Crear siguiente borrador desde una publicación del mismo playbook |
| POST /versions/{version}/review-requests | Enviar borrador a revisión, 201 |
| PUT /versions/{version}/review-requests/{reviewRequest} | Rechazar o publicar, 200 |
| POST /versions/{version}/withdrawals | Retirar publicación, 200 |
| GET /reviews?afterRevision=0&limit=50 | Historial administrativo, máximo 100 por página |

POST /versions devuelve 201. El límite/paginación no congela páginas futuras. afterNumber/afterRevision son cursores de avance; una página de tamaño limit puede tener continuación y se puede consultar otra página.

Rutas de consumo autenticado:

| Ruta GET | Alcance |
|---|---|
| /api/playbooks/public | Listado global vigente |
| /api/playbooks/public/{playbook}/current | Contenido global vigente |
| /api/organizations/{organization}/playbooks/published | Listado privado vigente |
| /api/organizations/{organization}/playbooks/{playbook}/current | Contenido privado vigente |

Listados de consumo usan skip/limit con los límites anteriores del catálogo (0..1000000 y 1..100) y nextSkip. Sin vigente devuelve lista vacía o 404 en consulta individual. Cookie y CSRF X-CSRF-TOKEN en toda escritura; no-store y límite HTTP administrativo de 1 MiB.

Ejemplos de cuerpos:

```json
{ "reason": "Contenido listo para revisión", "expectedRevision": 1 }
```

El mismo formato sirve para retirada, usando la revisión actual. Publicación con autoaprobación explícita:

```json
{
  "publish": true,
  "selfApproval": true,
  "selfApprovalJustification": "Excepción revisada por el único administrador disponible",
  "reason": "Se valida el procedimiento inicial",
  "expectedRevision": 2
}
```

Rechazo: publish=false, selfApproval=false, selfApprovalJustification=null y motivo. Nueva versión:

```json
{
  "sourceVersionId": "UUID-de-version-ya-publicada-del-mismo-playbook",
  "reason": "Preparar una mejora del procedimiento",
  "expectedRevision": 3
}
```

Las respuestas de contenido añaden reviewRequestId, sourceVersionId, publishedAt y playbook.currentPublishedVersionId. Son datos de lectura: el cuerpo del cliente no decide ámbito, IDs de publicación, estados ni puntero. Todas las mutaciones incrementan la revisión del agregado; un comando obsoleto devuelve 409.

## Persistencia, trazabilidad y concurrencia

Se mantiene READ COMMITTED con advisory gate (1869636978,26), timeout de bloqueo de cinco segundos y revalidación de autorización después del gate, compartido con identidad e inventario. Lecturas compuestas usan REPEATABLE READ. Publicación/retirada/nueva versión, revisión y auditoría confirman o revierten juntas; se exige DbContext limpio.

app.playbook_reviews registra SUBMITTED/PUBLISHED/REJECTED/WITHDRAWN con actor, UUID de solicitud cuando corresponde, motivo, revisión, marca/justificación de autoaprobación, fecha y SHA256 del contenido. Solo administradores autorizados leen motivos; auditoría administrativa conserva únicamente metadatos mínimos de IDs/revisión/número de versión.

El hash ordena pasos por código, enlaces por código de origen/destino y requisitos por código de paso/UUID de capacidad. Usa UTF-8 del JSON de PlaybookDraftContent, con nombres/orden de propiedades de ese contrato y enums numéricos del serializador .NET por defecto. No incluye UUID nuevos de nodos, actor, motivo, estado o revisión. El hash permite cotejar contenido revisado/publicado; no es una firma ni sustituye autorización.

EF y triggers SQL impiden modificar/eliminar el historial. Índices únicos impiden dos decisiones de la misma solicitud, duplicar evento de solicitud/acción o registrar dos eventos en una revisión del agregado.

Migración 20261024010838_PlaybookPublication:
- Añade puntero nullable al agregado y seis campos por versión: título/categoría, fuente, solicitud, solicitante y fecha publicada.
- Copia título/categoría de cada borrador anterior desde su agregado, sin publicar ni cambiar UUID/revisión/pasos. Los valores por defecto técnicos de la adición se eliminan tras la copia.
- Nueva tabla de revisiones (12 columnas). El módulo queda en seis tablas, 50 columnas, 17 FK, 14 checks y 14 guards SQL, verificados por metadatos.
- FK compuestas para puntero vigente y procedencia dentro del mismo playbook. Restricciones de estado/metadata y un solo borrador/revisión activo.
- Protecciones de contenido en las tres tablas de nodos, bloqueo de TRUNCATE y protección de estado/identidad de versiones. Los nodos solo se editan cuando su versión es DRAFT; se bloquea la fila de la versión al comprobarlo.
- Verificación diferida del puntero al confirmar: debe apuntar a una versión PUBLISHED del mismo agregado.
- Down rechaza revertir si hay playbooks o datos de ciclo de publicación. No elimina silenciosamente borradores ni historial.
- grant-runtime.sql permite solo los campos necesarios para el flujo; historial SELECT/INSERT, sin UPDATE/DELETE. Se prueba con LOGIN SQL sin propiedad ni permisos administrativos.

Las garantías de autorización y concurrencia de las operaciones soportadas incluyen el gate del servicio. No se proporciona una API de SQL arbitrario. Las credenciales del propietario pueden alterar objetos; RLS y separación operativa completa de credenciales siguen pendientes.

## Archivos

En apps/api:
- Domain/Entities/PlaybookEntities.cs y PlaybookReview.cs.
- Application/Playbooks/IPlaybookDraftService.cs y PlaybookPublicationContracts.cs.
- Infrastructure/Playbooks/PlaybookDraftService.cs y PlaybookPublicationService.cs.
- Infrastructure/Persistence/Configurations/PlaybookMapping.cs, OpenIrpDbContext.cs y migración/snapshot.
- Api/Controllers/PlaybookDraftControllers.cs y PlaybookPublicationControllers.cs.
- IntegrationTests/PlaybookPublicationTests.cs y PlaybookPublicationPersistenceTests.cs; adaptación de la prueba anterior de permisos/TRUNCATE a las nuevas relaciones SQL.

Además: ci/grant-runtime.sql, README, planificación maestra y nota histórica en la guía de Iteración 11.

## Verificación

- Compilación Release: cero errores y cero advertencias. EF: sin diferencias de modelo pendientes.
- Módulo de playbooks: **88/88 pruebas correctas** en PostgreSQL 17, sin fallidas/omitidas. Incluye los 44 casos anteriores de borradores y **44 casos nuevos de integración**. TRX: artifacts/publication-focused-pg17-8cd5554734dc46b29bf992b5546cb714/paso26-pg17.trx.
- Regresión completa PostgreSQL 17: **451/451 correctas**, cero fallidas/omitidas. TRX: artifacts/paso26-pg17-812843d29b124ec383cd8fb02c530b0f/paso26-pg17.trx.
- Regresión completa PostgreSQL 18.3 nativo aislado: **451/451 correctas**, cero fallidas/omitidas. TRX: artifacts/publication-full-pg18-15bef111c5634f22a84b68238234843c/paso26.trx.
- Los 44 nuevos están incluidos en los 451, y los 88 enfocados tampoco se suman otra vez. Cobertura: publicación global/privada, rechazo y UUID de solicitud nuevo, revisión congelada, fuentes/números nuevos, copia de nodos y aislamiento del borrador, puntero sin fallback, lectura por roles/contextos, autoaprobación, solicitante/revisor inactivos, referencias desactivadas/ciclos, carreras forzadas de decisiones/versiones, rollback por fallo de auditoría, SQL de contenido/historial/puntero/tenant, upgrade con borrador anterior, downgrade bloqueado, HTTP/CSRF y LOGIN SQL restringido.
- La primera ejecución aprobó los 44 nuevos y detectó una expectativa antigua de TRUNCATE: las nuevas FK impedían llegar al guard antes previsto. Se adaptó esa prueba para incluir todas las tablas relacionadas; la repetición aprobó los 88 casos.
- Migración aplicada exclusivamente a openirp_dev (PostgreSQL 17 habitual, 127.0.0.1:5433), después de restaurar un dump completo en otro contenedor sin puertos ni volúmenes. Los conteos y el historial restaurados coincidieron antes de aplicar; ninguna cantidad existente cambió.
- Se conservaron vacías las tablas de negocio/identidad/auditoría y playbooks, junto con ocho capacidades y tres categorías. No se fabricaron cuentas, publicaciones, revisores ni decisiones en la base habitual; la nueva tabla de revisiones está vacía y el historial contiene trece migraciones.
- Respaldo, SQL incremental, conteos y scripts: artifacts/playbook-publication-dev-35862eb4be274fa6ae6c4bbb3b3fed0d/. Dump openirp_dev-before-playbook-publication.dump, SHA256 **20455AFDA67098225FD765BFFD6248E0599940CD3EE77A96A4D627C10175D80C**.
- El SQL revisado permite exclusivamente la copia controlada de título/categoría al ampliar el esquema; no contiene eliminación de tablas/filas ni modificaciones de otros datos. Se cotejaron seis tablas, 50 columnas, 17 FK, 14 checks, 14 guards y ausencia de defaults de backfill residuales.
- HTTPS real: health y OpenAPI 200; 26 operaciones de playbooks presentes y todas las solicitudes anónimas 401. Evidencia: artifacts/playbook-publication-http-07f72cf2083147688362dcf2816ae141/. Flujos autenticados verificados en bases temporales.
- Contenedores/clústeres/proceso API propios detenidos. Sin listeners en 55432, 55433, 7113 ni 5195. PostgreSQL y Ollama habituales preservados.
- Todo permanece local, preservando las modificaciones anteriores, sin nuevo commit, push ni merge.

No se detectaron bloqueantes para continuar. Siguiente entrega: clonación autorizada entre playbooks/ámbitos; después, ejecución guiada y evidencias, con sus bloqueos de seguridad y condiciones de cierre.
