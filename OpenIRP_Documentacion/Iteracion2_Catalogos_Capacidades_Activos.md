# Iteración 2 — catálogos, capacidades y activos locales

Desarrollo local del 22 de octubre de 2026 en `feature/paso26-identidad-organizaciones`, sobre las subfases de [Identity](Paso26_Administracion_Local.md) y [auditoría](Paso26_Auditoria_Local.md). Se conservaron los cambios anteriores y las migraciones ya aplicadas. No hubo commit, push, merge ni edición de GitHub.

## Alcance implementado

Cuatro tablas nuevas: `app.capability_catalog`, `app.organization_capabilities`, `app.assets` y `app.incident_categories`. Todas tienen UUID como PK y una revisión positiva BIGINT para cambios de negocio. Activos y declaraciones incluyen organización obligatoria, FK restrictivas e identidad compuesta `(organization_id, id)` para futuras asociaciones por tenant. Las declaraciones tienen unicidad `(organization_id, capability_id)` e índice separado para la FK de capacidad.

La migración `20261022183846_CatalogsCapabilitiesAssets` incorpora ocho capacidades: IT_STAFF, SECURITY_SPECIALIST, EXTERNAL_SUPPORT, ENDPOINT_PROTECTION, SECURITY_LOGS, MFA, BACKUPS y DEVICE_ISOLATION. También crea PHISHING, ACCOUNT_COMPROMISE y RANSOMWARE. Son datos de referencia versionados por migración, sin cuentas de negocio, concesiones ni eventos operativos retrospectivos.

Una capacidad sin declaración se devuelve como `Unknown`, revisión 0 y notas null. Leer el perfil no inserta filas. Una declaración explícita comienza en revisión 1; actualizarla exige la revisión actual. Restablecerla a Unknown conserva su fila y aumenta la revisión para impedir que un cliente obsoleto vuelva a tratarla como ausente.

Los códigos de catálogo son únicos e inmutables desde la API. PLATFORM_ADMIN puede crear entradas y modificar nombre, categoría, descripción o vigencia. Desactivar una capacidad impide nuevas escrituras sobre sus declaraciones; las existentes siguen visibles con `isActive=false`, conservando su estado y notas. Las entradas inactivas sin declaración se omiten del perfil. Esto no afirma disponibilidad efectiva para un incidente: el consumidor futuro deberá considerar también la vigencia del catálogo.

Los activos tienen nombre, tipo, criticidad, estado, descripción, fecha UTC y revisión. La criticidad LOW/MEDIUM/HIGH no equivale a severidad de incidente. La retirada es lógica: el registro, su UUID y sus futuras referencias se conservan; es posible reactivarlo mediante una actualización autorizada. Nombres iguales están permitidos: la identidad del activo es su UUID, no su nombre.

## Permisos adoptados

| Operación | Permiso |
|---|---|
| Leer los dos catálogos, incluidas entradas inactivas si se solicitan | Cuenta autenticada y activa; son metadatos globales de referencia. |
| Crear/editar/desactivar entradas de catálogo | PLATFORM_ADMIN vigente y cuenta activa. |
| Leer perfil o inventario de una organización | Membresía activa en organización activa, incluyendo Analyst/Viewer. |
| Cambiar el perfil general | ORG_ADMIN del mismo tenant, conforme a la planificación. |
| Crear, editar, retirar o reactivar activos | ORG_ADMIN del mismo tenant. Política inicial conservadora; Analyst/Viewer solo leen el inventario. |

El permiso de plataforma no evita las comprobaciones de membresía para los datos organizacionales. La organización de una escritura procede de la ruta validada; no se acepta un cambio de propietario desde el cuerpo. Las búsquedas de activos combinan siempre organización e ID. Un miembro que solicita un UUID ajeno bajo su propia organización recibe 404; quien no tiene membresía en la organización solicitada recibe 403.

Las limitaciones específicas de incidente, los activos reportados todavía no inventariados y sus snapshots históricos se implementarán junto con incidentes. No se crearon tablas de asociaciones sin su agregado padre ni se habilitaron acciones remotas sobre dispositivos.

## Endpoints y contratos

Arrancar con `./ci/Start-LocalApi.ps1` desde la raíz, después de aprovisionar una identidad real con el helper `-Bootstrap` si corresponde. HTTPS: `https://localhost:7113`. Se conserva el flujo de cookies y CSRF de la guía Identity: obtener token antes del login y renovarlo después; enviar `X-CSRF-TOKEN` en POST/PUT/DELETE. OpenAPI de desarrollo: `/openapi/v1.json`.

| Método/ruta | Acción |
|---|---|
| GET `/api/catalogs/capabilities` | Capacidades activas del catálogo. |
| GET `/api/catalogs/incident-categories` | Categorías activas de incidente. |
| POST `/api/catalogs/{catalog}` | Alta de referencia por plataforma. |
| PUT `/api/catalogs/{catalog}/{id}` | Actualización de referencia por plataforma, con revisión. |
| GET `/api/organizations/{org}/capabilities` | Perfil con Unknown implícito para capacidades no declaradas. |
| PUT `/api/organizations/{org}/capabilities/{capabilityId}` | Crear/actualizar declaración organizacional. |
| GET `/api/organizations/{org}/assets` | Inventario, incluidos activos retirados. |
| GET `/api/organizations/{org}/assets/{id}` | Detalle dentro del mismo tenant. |
| POST `/api/organizations/{org}/assets` | Alta de activo. |
| PUT `/api/organizations/{org}/assets/{id}` | Edición o reactivación del activo. |
| DELETE `/api/organizations/{org}/assets/{id}?expectedRevision=1` | Retirada lógica, con revisión actual. |

`catalog` admite exactamente `capabilities` o `incident-categories`. Los listados aceptan `skip` entre 0 y 1.000.000, `limit` entre 1 y 100 (por defecto 50), y responden `{ "items": [...], "nextSkip": 50 }`. Usar nextSkip para continuar; null indica que no había otra página. Es paginación para inventarios pequeños, sin una instantánea congelada frente a cambios concurrentes. Catálogos/perfil se ordenan por código; activos, por UUID. El catálogo acepta `includeInactive=true`.

Ejemplo de alta de catálogo:

```json
{ "code": "VULNERABILITY_SCANNER", "name": "Escáner de vulnerabilidades", "category": "VISIBILITY", "description": null }
```

Actualización de catálogo: `{ "name": "Nombre", "category": "VISIBILITY", "description": null, "isActive": false, "expectedRevision": 1 }`. No hay edición de código ni borrado físico de referencias en la API.

Declarar una capacidad cuya revisión leída sea cero:

```json
{ "availability": "Available", "notes": "Respaldos revisados por el responsable", "expectedRevision": 0 }
```

Valores: `Unknown`, `Available`, `Unavailable`. Para borrar la declaración efectiva sin perder su revisión, enviar Unknown con notas null y la revisión actual. No se puede modificar una capacidad desactivada hasta que el catálogo vuelva a activarla.

Alta de activo:

```json
{ "name": "Servidor de archivos", "kind": "Server", "criticality": "High", "description": null }
```

Tipos: `Server`, `Workstation`, `CloudService`, `Account`, `Application`, `NetworkDevice`, `CriticalService`, `Other`. Criticidad: `Low`, `Medium`, `High`. Estados: `Active`, `Retired`. La actualización añade `status` y `expectedRevision` al resto de campos del activo. Tipo, criticidad, disponibilidad y precondiciones de escritura deben aparecer explícitamente en el JSON; no se asume una criticidad baja por omisión. El catálogo exige también isActive explícito al actualizar. Nombre máximo 200 caracteres; categoría/código 60; notas/descripción 2000. Los códigos usan ASCII mayúscula, dígitos y guion bajo, sin espacios ni saltos de línea.

Una revisión obsoleta produce 409; releer antes de decidir un nuevo intento. Los campos desconocidos no cambian IDs, códigos ni organización. La API guarda nombres/notas/descripciones en las tablas de negocio y no los copia a auditoría.

## Transacciones, integridad y auditoría

Las escrituras toman el mismo advisory lock transaccional de Identity (1869636978, 26), con READ COMMITTED y espera máxima de cinco segundos. La autorización se consulta después de adquirirlo, coordinando cambios del módulo con suspensiones y desactivaciones de cuenta. Las revisiones son también tokens de concurrencia EF. Esta primera implementación serializa escrituras administrativas de toda la base; no es una solución de escalabilidad por tenant.

Cada escritura correcta y su evento SUCCESS se confirman juntos. Un fallo de auditoría revierte activos, declaraciones y referencias nuevas. Acciones: `catalog.create`, `catalog.update`, `capability.set`, `asset.create`, `asset.update`, `asset.retire`. Los detalles contienen revisiones/estados/UUID mínimos, no texto libre. Rechazos controlados se registran por el canal separado de auditoría y solo en ámbito global, conservando sus límites de disponibilidad ya documentados.

Se ensayó una FK compuesta real contra `assets(organization_id,id)` en una tabla de prueba creada dentro de una transacción desechable: una asociación con otra organización falla, la correcta funciona y el rollback elimina toda la prueba. No se crearon asociaciones ficticias en la base habitual. Los checks y FK también rechazan datos inválidos fuera de los servicios; las escrituras SQL directas y servicios futuros deberán respetar revisión, permisos y protocolo transaccional.

## Evidencia ejecutada

- Release: 0 errores y 0 advertencias. 68/68 pruebas en PostgreSQL 17.11 y 68/68 en PostgreSQL 18.3, ninguna fallida ni omitida. Incluye las 37 pruebas previas y 31 casos nuevos de inventario/catálogos; la prueba de cuenta SQL limitada existente se amplió al módulo.
- Concurrencia forzada: ambos escritores esperan el mismo bloqueo antes de liberarlo. Solo una declaración inicial o actualización con la misma revisión prospera; la otra devuelve 409.
- Verificados permisos activos/suspendidos/revocados, ausencia de bypass por plataforma, claves compuestas, unicidad, Unknown implícito, desactivación de referencias, retirada lógica, metadatos de auditoría sin texto libre, rollback por fallo del sink, endpoints JSON/CSRF y precondiciones obligatorias.
- La reversión/reaplicación de la migración se probó solo en bases vacías desechables. EF confirmó que no hay cambios de modelo pendientes.
- PostgreSQL 17 TRX: `artifacts/paso26-pg17-e46bc813f16e4ffba6f784810a0e8e46/paso26-pg17.trx`.
- PostgreSQL 18 TRX: `artifacts/paso26-81c815f60bf0439a82899d62a4201944/paso26.trx`.
- Base habitual: se restauró el respaldo previo en otro contenedor PostgreSQL 17 y se cotejaron conteos antes de aplicar `20261022183846_CatalogsCapabilitiesAssets`. Los conteos previos de usuarios, organizaciones, membresías, concesiones y auditoría siguen en cero. Solo se agregaron las ocho capacidades y tres categorías de referencia; no se inventaron administradores ni datos privados.
- Respaldo, SQL incremental y conteos: `artifacts/inventory-dev-ffb7a367c40545598553e4e6b241a909/`. Archivo `openirp_dev-before-inventory.dump`, SHA256 `457A90BF6003D1524D3941D757225999770D6E5F2AEBA39A9920AB42B32151DA`.
- HTTPS real: health 200, cuatro rutas anónimas nuevas 401, OpenAPI 200 con las rutas nuevas. Logs/esquema/resultado: `artifacts/inventory-http-dbd3fffada1041abaf9ccde3037e18e4/`. El proceso de comprobación se detuvo. Solo ese cliente aceptó el certificado de desarrollo local; la API conserva HTTPS/cookies Secure.

Para repetir: `./ci/Test-IdentityDocker.ps1` (PostgreSQL 17 desechable) o `./ci/Test-IdentityLocal.ps1` (PostgreSQL nativo aislado). No usarlos para poblar la base habitual. El código CI local anterior incorpora automáticamente los nuevos casos al ejecutar el mismo proyecto; no se disparó CI remota.

## Archivos de esta subfase

| Archivo | Cambio |
|---|---|
| `apps/api/OpenIRP.Domain/Entities/InventoryEntities.cs` | Catálogos, declaración, activo y enumeraciones. |
| `apps/api/OpenIRP.Application/Inventory/IInventoryService.cs` | Contratos, revisiones, paginación y campos JSON obligatorios. |
| `apps/api/OpenIRP.Infrastructure/Inventory/InventoryService.cs` | Autorización, transacciones, perfil efectivo, inventario y auditoría. |
| `apps/api/OpenIRP.Infrastructure/Persistence/Configurations/InventoryMapping.cs` | Mapeo, checks, claves/índices y referencias iniciales deterministas. |
| `apps/api/OpenIRP.Infrastructure/Persistence/OpenIrpDbContext.cs` | Registro de entidades y configuración. |
| `apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261022183846_CatalogsCapabilitiesAssets.cs` y `.Designer.cs` | Migración nueva sin reescribir las anteriores. |
| `apps/api/OpenIRP.Infrastructure/Persistence/Migrations/OpenIrpDbContextModelSnapshot.cs` | Modelo actualizado. |
| `apps/api/OpenIRP.Api/Controllers/InventoryControllers.cs` | Endpoints protegidos. |
| `apps/api/OpenIRP.Api/Program.cs` | Registro del servicio de inventario. |
| `apps/api/OpenIRP.IntegrationTests/InventoryTests.cs` | 31 casos nuevos. |
| `apps/api/OpenIRP.IntegrationTests/AdministrativeAuditTests.cs` | Amplía la prueba runtime SQL al nuevo módulo. |
| `README.md`, documentación maestra y esta guía | Contratos, permisos, evidencias y pendientes. |

## Pendientes y siguiente módulo

La próxima implementación es incidentes: categorías ya disponibles, ciclo de vida, CIA, máximo histórico, aprobaciones, asociaciones de activos y limitaciones de capacidades con snapshots. La regla de severidad determinista debe definirse antes de implementarla; no se asumió una fórmula. RLS, separación operativa de credenciales SQL, auditoría integral/retención, MFA y otras medidas de endurecimiento siguen pendientes. El helper local conserva su conexión de desarrollo previa; no se cambiaron roles SQL de la base habitual ni se habilitó registro público.

Se aplicaron las recomendaciones de la skill `supabase-postgres-best-practices` para índices de FK/tenant y transacciones cortas. Referencias: [PostgreSQL: restricciones compuestas](https://www.postgresql.org/docs/17/ddl-constraints.html), [Microsoft: concurrencia EF](https://learn.microsoft.com/en-us/ef/core/saving/concurrency).
