# Activos afectados por incidente — snapshots históricos locales

Subfase posterior al registro inicial de incidentes, realizada localmente en `feature/paso26-identidad-organizaciones`. Se preservan los cambios anteriores pendientes y las cuatro migraciones existentes. Esta entrega no hace commit, push ni merge.

## Comportamiento

`app.incident_assets` relaciona incidentes con activos afectados. Un incidente puede incluir varios activos; un activo del inventario puede aparecer en varios incidentes. Dentro del mismo incidente solo puede existir una asociación vinculada por UUID de activo. Los reportes aún sin identificar pueden tener nombres iguales: no se deduce identidad física a partir del nombre.

Hay tres operaciones:

1. Reportar un activo todavía no inventariado: guarda nombre, tipo, contexto y criticidad reportados, con `assetId=null`.
2. Adjuntar un activo activo del inventario: copia su nombre, tipo y criticidad actuales como snapshot y guarda su UUID. El contexto lo aporta el analista.
3. Vincular después un reporte con un activo activo del mismo tenant: completa únicamente UUID y revisión de vinculación. Conserva nombre, tipo, contexto y criticidad reportados aunque el inventario tenga otros valores.

La criticidad reportada acepta `Low`, `Medium`, `High` o `null` explícito si todavía no se conoce. No se interpreta null como baja ni se calcula severidad de incidente. El tipo se exige explícito; `Other` permite una clasificación general. La criticidad del activo es distinta de la severidad del incidente.

Los campos originales no se editan ni eliminan desde la API. La vinculación se admite una sola vez; una asociación vinculada no puede reemplazarse ni desasignarse por esta API. La corrección administrativa de una vinculación equivocada queda pendiente de un flujo explícito y auditable que conserve su historia. Un cambio posterior de nombre/tipo/criticidad o la retirada del activo del inventario no modifica los snapshots previos. No se admiten nuevas asociaciones ni primeras vinculaciones hacia activos retirados.

Nombre máximo 200 caracteres, contexto opcional máximo 2000; el nombre se recorta y debe contener texto. Cada escritura exige motivo no vacío de hasta 2000 caracteres y `expectedRevision` del **incidente**. La revisión no pertenece a la fila del activo. Las respuestas de escritura contienen `{asset,incidentRevision}`; las revisiones `reportedAtRevision` y `linkedAtRevision` permiten identificar cuándo se reportó y cuándo se vinculó.

## Permisos, integridad y transacciones

ORG_ADMIN y ANALYST activos pueden reportar, adjuntar y vincular en su organización; VIEWER activo puede leer. Un ANALYST no necesita permiso de escritura del inventario para adjuntar un activo ya existente. PLATFORM_ADMIN no obtiene acceso a datos del tenant sin membresía válida. Cuenta bloqueada/inactiva, membresía no vigente y organización no activa siguen rechazándose mediante el mismo servicio de incidentes.

Las búsquedas combinan organización, incidente y asociación. Una asociación no puede reutilizarse bajo otro incidente ni siquiera dentro del mismo tenant. Las FK compuestas `(organization_id, incident_id)` y `(organization_id, asset_id)` rechazan mezclas de tenants también por SQL. Todas las FK son restrictivas. Hay un índice para cada FK y un índice único parcial de incidente-activo cuando el UUID no es null, además del índice que soporta el listado por organización/incidente/UUID.

Se reutiliza la transacción y el bloqueo de Identity/inventario/incidentes: se releen permisos después de adquirirlo y se incrementa la revisión del incidente. El reporte/vínculo, su snapshot en `incident_events` y el SUCCESS de auditoría se confirman juntos; cualquier fallo revierte todo. Ediciones del incidente, cambios de membresía y retirada de inventario coordinan con ese mismo bloqueo. Sigue siendo un bloqueo global con espera máxima de cinco segundos, pendiente de una solución granular con garantías equivalentes.

Acciones del historial: `ASSET_REPORTED`, `ASSET_ATTACHED`, `ASSET_LINKED`. La nueva propiedad `details.asset` contiene el snapshot histórico de esa asociación en esa revisión; los eventos previos conservan sus datos y reciben `details={}`. El evento de reporte anterior mantiene `assetId=null` incluso después de una vinculación. Los valores de enum dentro de details se serializan como texto.

La auditoría administrativa usa `incident.asset_reported`, `incident.asset_attached`, `incident.asset_linked`, con revisión y UUID mínimos. No copia el nombre, contexto ni motivo privados. El historial detallado se consulta con autorización del tenant. Los rechazos conservan el canal global y los límites de disponibilidad ya documentados.

Un trigger SQL impide UPDATE de los campos originales, DELETE y TRUNCATE. Solo permite la primera vinculación desde un UUID nulo, sin cambiar el snapshot. El rol SQL runtime limitado recibe SELECT/INSERT y UPDATE solo de las columnas `asset_id` y `linked_at_revision`; se verifica con un LOGIN desechable. El propietario/superusuario puede alterar objetos: no se afirma inmutabilidad criptográfica ni protección frente a ese actor. Las garantías de revisión y escritura de ambos historiales pertenecen al protocolo de los servicios; SQL directo/futuros escritores deben respetarlo. RLS y separación operativa de credenciales habituales continúan pendientes.

## Endpoints

Arrancar con `./ci/Start-LocalApi.ps1`; HTTPS `https://localhost:7113`, OpenAPI de desarrollo `/openapi/v1.json`. Aplican las cookies y el flujo CSRF de [Identity](Paso26_Administracion_Local.md). Las escrituras requieren `X-CSRF-TOKEN` válido.

Todas las rutas parten de `/api/organizations/{org}/incidents/{incident}/assets`:

| Método/sufijo | Operación |
|---|---|
| GET raíz | Listado privado con skip/limit. |
| POST `/reported` | Reporte aún no inventariado; 201. |
| POST `/from-inventory` | Adjuntar activo existente y copiar snapshot; 201. |
| PUT `/{association}/link` | Primera vinculación justificada; 200. |

Reporte sin criticidad conocida:

```json
{ "reportedName": "Estación reportada", "reportedType": "Workstation", "reportedContext": "Equipo observado por el analista", "reportedCriticality": null, "reason": "Registro inicial de afectación", "expectedRevision": 1 }
```

Adjuntar desde inventario:

```json
{ "assetId": "UUID_DEL_ACTIVO", "reportedContext": "Servicio afectado", "reason": "Identidad confirmada", "expectedRevision": 2 }
```

Primera vinculación del reporte:

```json
{ "assetId": "UUID_DEL_ACTIVO", "reason": "Se verificó el identificador del equipo", "expectedRevision": 3 }
```

No incluir assetId en `/reported`: ese contrato crea siempre un reporte sin inventario; los campos extra no cambian IDs, organización ni snapshots del servidor. Omitir tipo, criticidad o precondición del reporte produce 400, por lo que desconocido debe expresarse como null. Criticidad/tipo numéricos fuera de enum se rechazan. Para adjuntar/vincular se exige UUID explícito.

Revisión obsoleta, duplicado de activo dentro del incidente, vinculación ya realizada o activo retirado: 409. UUID de activo ajeno/inexistente o asociación de otro incidente: 404 dentro de un tenant autorizado. Falta de membresía/permiso: 403. Los listados usan `{items,nextSkip}`, skip 0–1.000.000 y limit 1–100 (50 por defecto), orden por UUID. No constituyen una exportación congelada frente a cambios concurrentes. Las rutas privadas responden no-store.

## Migración y archivos

Migración nueva `20261022194422_IncidentAssetSnapshots`: crea `incident_assets`, extiende acciones y añade `details_json` JSONB a `incident_events`, con objeto obligatorio de hasta 32768 bytes. No reescribe las cuatro migraciones anteriores ni actualiza los eventos históricos mediante DML. La reversión se rechaza si existen asociaciones o eventos del nuevo módulo; no elimina silenciosamente reportes para permitir un downgrade. La recuperación de una base con historia exige un plan de preservación/restauración, no un rollback ordinario.

- `apps/api/OpenIRP.Domain/Entities/IncidentAsset.cs`: asociación y snapshot original.
- `apps/api/OpenIRP.Domain/Entities/IncidentEntities.cs`: detalles de historial.
- `apps/api/OpenIRP.Application/Incidents/IncidentAssetContracts.cs`, `IIncidentService.cs`: contratos, revisiones y detalles JSON.
- `apps/api/OpenIRP.Infrastructure/Incidents/IncidentAssetService.cs`, `IncidentService.cs`: operaciones y transacción reutilizada.
- `apps/api/OpenIRP.Infrastructure/Persistence/Configurations/IncidentAssetMapping.cs`, `IncidentMapping.cs`, `OpenIrpDbContext.cs`: integridad, índices y mapeo.
- Migración `20261022194422_IncidentAssetSnapshots.cs`, diseñador y snapshot EF.
- `apps/api/OpenIRP.Api/Controllers/IncidentAssetsController.cs`: rutas protegidas.
- `apps/api/OpenIRP.IntegrationTests/IncidentAssetTests.cs`, `AdministrativeAuditTests.cs`: pruebas y ampliación runtime SQL.
- `ci/grant-runtime.sql`: permisos limitados para la tabla nueva.
- README, planificación maestra y esta guía.

## Verificación

PostgreSQL 17: 127/127 pruebas correctas, sin fallidas ni omitidas; 33 casos nuevos además de los 94 anteriores. Compilación Release sin errores/advertencias, modelo EF sin diferencias pendientes. Incluye snapshots, criticidad desconocida, FK compuestas reales, permisos, JSON/CSRF, concurrencia forzada en pg_locks, retirada frente a asociación, fallo inyectado de ambos historiales para las tres operaciones, protección SQL del reporte y pruebas con rol runtime limitado. También se probó el upgrade conservando eventos ya existentes y el rechazo del downgrade con historial del módulo.

TRX PG17: `artifacts/paso26-pg17-b421678a926b49bea6df7e92026dde92/paso26-pg17.trx`.

PostgreSQL 18.3 nativo aislado: 127/127 correctas, 0 fallidas/omitidas; modelo EF sin diferencias. TRX: `artifacts/paso26-f79ee6740b564729b9ce523a9caf4f9a/paso26.trx`. Los helpers de prueba crean sus propios recursos desechables; los datos sintéticos no se insertan en la base habitual.

La migración se aplicó exclusivamente a `openirp_dev`, PostgreSQL 17 habitual en `127.0.0.1:5433`, después de restaurar el respaldo completo en otro contenedor sin puertos publicados ni volúmenes y cotejar sus conteos/historial. No se cambiaron credenciales ni roles SQL habituales. Se conservaron usuarios, organizaciones, membresías, concesiones, auditoría, activos, declaraciones, incidentes e historial de incidentes (todos en cero). Se conservan ocho capacidades y tres categorías; la tabla nueva comienza vacía y ambos triggers están habilitados.

Respaldo, SQL incremental, conteos y verificación: `artifacts/incident-assets-dev-e60cbe94cd45401ba9f6e9f46d80ad5f/`. Dump `openirp_dev-before-incident-assets.dump`, SHA256 `960D7A8A5ADC5A44E2048D6567056BA51CE025FD4E27CD71BA6815F938E78A6C`.

La comprobación final del helper produjo una falsa alarma porque conservaba los nombres incidents/incident_events al leer el resultado incident_assets. Se corrigió esa comprobación y se verificaron los conteos, las cinco migraciones y los triggers mediante consultas independientes de solo lectura, sin repetir la migración. Evidencia: `post-migration-verified.txt` y `live-verified-counts.json`. El respaldo/restauración y la comparación de los datos previos ya habían concluido correctamente antes del error del chequeo final.

HTTPS real mediante el helper: health 200, GET anónimo del listado de asociaciones 401, OpenAPI 200 con las cuatro rutas nuevas. Evidencia: `artifacts/incident-assets-http-f2ef50acdf3f40ee9ce359391aa143d8/`. Solo el cliente de comprobación aceptó el certificado de desarrollo local; la API conserva HTTPS/cookies Secure. El proceso propio se detuvo al terminar. Los recursos temporales de PostgreSQL 17/18 quedaron detenidos y el servicio PostgreSQL 18 del sistema no se modificó. Los contenedores habituales PostgreSQL/Ollama siguen disponibles; los respaldos y resultados están ignorados por Git.

Las capacidades y limitaciones por incidente se implementaron después en [la Iteración 5](Iteracion5_Capacidades_Incidente.md). CIA, fórmula determinista de severidad y ciclo de vida completo/aprobaciones siguen pendientes. Esta entrega no habilita registro público, permisos globales de tenant ni acciones remotas sobre los activos. Se aplicaron las recomendaciones de `supabase-postgres-best-practices` para índices de FK y unicidad parcial.
