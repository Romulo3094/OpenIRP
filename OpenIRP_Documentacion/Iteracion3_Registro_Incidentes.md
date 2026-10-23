# Incidentes — registro inicial local

Subfase local posterior a catálogos/capacidades/activos. El avance anterior quedó publicado por autorización del usuario en `d7ced5f`, rama `feature/paso26-identidad-organizaciones`. Los cambios de esta subfase se conservan localmente, sin otro commit/push ni merge.

## Alcance

Registro, consulta, corrección y asignación de incidentes. Estado único habilitado: `REGISTERED`; no hay transición ni borrado HTTP. La fórmula de severidad permanece pendiente de especificación (§7.2 de la planificación): no se calcula severidad ni se presupone impacto bajo. CIA, máximo histórico, evaluaciones, respuesta, resolución y aprobaciones de cierre se implementarán en una subfase posterior. Las asociaciones y snapshots de activos se incorporaron posteriormente en [la Iteración 4](Iteracion4_Activos_Incidente.md); las excepciones de capacidades por incidente siguen pendientes.

Dos tablas: `app.incidents` y `app.incident_events`, con UUID. La primera conserva el estado actual y revisión positiva; la segunda guarda el snapshot de título, descripción, UUID de categoría y responsable en cada revisión, junto con actor, instante, motivo y correlación del servidor. Comparar revisiones consecutivas permite ver antes/después. Los nombres de categorías y cuentas siguen siendo referencias al catálogo/Identity; no se afirma una copia histórica de sus nombres.

Cada creación produce revisión 1 y evento REGISTERED; las correcciones y asignaciones incrementan la revisión y generan UPDATED/ASSIGNED. Correcciones y asignaciones requieren motivo de 1–2000 caracteres y `expectedRevision` explícito. No se omiten eventos para cambios que conservan los valores. No se copia texto libre a la auditoría administrativa global: el historial detallado pertenece al tenant.

La API permite títulos de hasta 200 caracteres y descripción de hasta 4000. El título se recorta y debe contener texto. La categoría debe existir y estar activa al registrar o reclasificar. Una categoría desactivada sigue visible por UUID en incidentes/historial y no impide corregir otros campos conservando esa categoría. No puede seleccionarse de nuevo después de reclasificar a otra.

## Permisos y asignación

ORG_ADMIN y ANALYST activos de la misma organización pueden registrar, corregir y asignar. VIEWER activo puede consultar. PLATFORM_ADMIN no adquiere acceso organizacional sin una membresía válida. Se comprueban cuenta activa/no bloqueada, organización activa, membresía activa y rol actual.

El responsable se identifica por `assignedMembershipId`, no por un identificador global de usuario: debe ser ORG_ADMIN/ANALYST activo del mismo tenant y tener cuenta activa/no bloqueada. `null` explícito desasigna. Una suspensión o cambio de rol posterior conserva la referencia histórica, pero no concede permisos al responsable anterior. El servicio de Identity sigue controlando las autorizaciones; no exige reasignar todos los incidentes para revocar un acceso.

Las claves compuestas `(organization_id, assigned_membership_id)` y `(organization_id, incident_id)` impiden referencias entre tenants incluso por SQL. Todas las relaciones son restrictivas. Las búsquedas combinan organización e ID; un UUID ajeno dentro de un tenant autorizado devuelve 404, y la falta de membresía devuelve 403.

## Endpoints

Arranque local: `./ci/Start-LocalApi.ps1`. HTTPS `https://localhost:7113`; OpenAPI de desarrollo `/openapi/v1.json`. Usar el flujo de login y CSRF de [Identity](Paso26_Administracion_Local.md); las escrituras requieren `X-CSRF-TOKEN` válido.

| Método/ruta | Acción |
|---|---|
| GET `/api/organizations/{org}/incidents` | Listado con skip/limit. |
| POST `/api/organizations/{org}/incidents` | Registro; 201 y Location al detalle. |
| GET `/api/organizations/{org}/incidents/{id}` | Detalle actual. |
| PUT `/api/organizations/{org}/incidents/{id}` | Corrección/reclasificación justificada. |
| PUT `/api/organizations/{org}/incidents/{id}/assignment` | Asignación/desasignación justificada. |
| GET `/api/organizations/{org}/incidents/{id}/events` | Historial paginado por revisión ascendente. |

Registro:

```json
{ "categoryId": "26000000-0000-4000-8100-000000000001", "title": "Correo sospechoso", "description": "Antecedentes reportados" }
```

Corrección:

```json
{ "categoryId": "26000000-0000-4000-8100-000000000001", "title": "Correo de suplantación", "description": "Contexto corregido", "reason": "Se verificó el remitente reportado", "expectedRevision": 1 }
```

Desasignación explícita:

```json
{ "assignedMembershipId": null, "reason": "Pendiente de nuevo turno", "expectedRevision": 2 }
```

El cuerpo no puede cambiar organización, estado ni IDs. Omitir `assignedMembershipId` o `expectedRevision` produce 400; una revisión obsoleta devuelve 409. Categoría/responsable inválidos producen 400, sin revelar información de otro tenant. Los recursos privados se sirven con Cache-Control no-store.

Listados: skip 0–1.000.000 y limit 1–100 (50 por defecto), orden UUID; `{items,nextSkip}`. Historial: afterRevision no negativo, limit 1–100, `{items,nextAfterRevision}`, cursor exclusivo. No se promete una instantánea congelada frente a escrituras concurrentes. El orden del historial es la revisión del agregado, sin depender del reloj.

## Transacciones e inmutabilidad

El mismo advisory lock transaccional que Identity e inventario serializa las escrituras, con READ COMMITTED y espera máxima de cinco segundos. Los permisos se releen después de adquirirlo. Dos clientes con la misma revisión no pueden confirmar ambos sus cambios. El incidente, su snapshot y el evento SUCCESS de auditoría se confirman o revierten juntos; fallar cualquiera de los dos historiales revierte el negocio. Los rechazos usan el canal global separado ya documentado, sin texto libre ni tenant no autorizado.

EF rechaza UPDATE/DELETE de IncidentEvent. Un trigger SQL rechaza UPDATE/DELETE/TRUNCATE con SQLSTATE 42501. `ci/grant-runtime.sql` otorga solo SELECT/INSERT en esta tabla al rol SQL limitado; la prueba usa un LOGIN temporal desechable. El rol propietario/superusuario puede alterar objetos, por lo que esto no representa inmutabilidad criptográfica ni protección frente al propietario. No se cambiaron roles SQL habituales y el helper de desarrollo sigue usando su credencial previa. RLS y separación operativa completa siguen pendientes.

Migración nueva: `20261022192153_InitialIncidentRegister`; las tres anteriores no se reescriben. La aplicación no migra ni crea identidades al iniciar.

## Archivos

- `apps/api/OpenIRP.Domain/Entities/IncidentEntities.cs`: agregado e historial.
- `apps/api/OpenIRP.Application/Incidents/IIncidentService.cs`: contratos, precondiciones y paginación.
- `apps/api/OpenIRP.Infrastructure/Incidents/IncidentService.cs`: permisos, asignación, transacciones y consultas.
- `apps/api/OpenIRP.Infrastructure/Persistence/Configurations/IncidentMapping.cs`: checks, claves e índices.
- `apps/api/OpenIRP.Infrastructure/Persistence/OpenIrpDbContext.cs`: mapeo e inmutabilidad EF.
- Migración `20261022192153_InitialIncidentRegister.cs`, su diseñador y snapshot EF.
- `apps/api/OpenIRP.Api/Controllers/IncidentsController.cs`, `Program.cs`: rutas y registro del servicio.
- `apps/api/OpenIRP.IntegrationTests/IncidentTests.cs`: pruebas PostgreSQL y HTTP.
- `AdministrativeAuditTests.cs`, `ci/grant-runtime.sql`: amplían el rol runtime y sus verificaciones.
- Esta guía, README y planificación maestra.

## Verificación

- Compilación Release: 0 errores y 0 advertencias; modelo EF sin cambios pendientes.
- PostgreSQL 17.11: 94/94 correctas, 0 fallidas/omitidas. TRX: `artifacts/paso26-pg17-d8b0f6e0d9a447668651bdec8ac6b656/paso26-pg17.trx`.
- PostgreSQL 18.3 nativo aislado: 94/94 correctas, 0 fallidas/omitidas. TRX: `artifacts/paso26-142262e01c0840d2b433d0ec4eab067e/paso26.trx`.
- Se agregan 26 casos a los 68 anteriores y se amplía la prueba del rol SQL runtime: registro/corrección/asignación y SELECT del historial funcionan con ese LOGIN limitado; UPDATE/DELETE/TRUNCATE/ALTER de historial fallan con 42501.
- Se verificaron snapshots y correlación, ausencia de texto libre en auditoría, permisos y contexto inactivos, ausencia de bypass global, referencias/FK entre tenants, catálogo inactivo, revisión obsoleta, paginación, JSON con asignación explícita, CSRF y ausencia de DELETE HTTP. Dos escritores se mantienen esperando en pg_locks antes de liberar el bloqueo; solo uno confirma la revisión. También se fuerza solapamiento entre registro y revocación de membresía.
- Fallos inyectados de auditoría o historial revierten el negocio y el otro historial; EF y SQL rechazan mutar eventos. Reversión/reaplicación de migraciones se verificó únicamente en bases vacías desechables mediante la prueba existente de roundtrip.
- Migración habitual aplicada exclusivamente a `openirp_dev`, `127.0.0.1:5433`, después de restaurar el respaldo completo en un contenedor PostgreSQL 17 sin puertos publicados ni volúmenes. Conteos e historial de la copia restaurada coincidieron con el original. Conteos previos conservados: identidades/organizaciones/membresías/concesiones/auditoría/activos/declaraciones en cero, ocho capacidades y tres categorías. Las tablas de incidentes e historial comienzan vacías. No se crearon cuentas reales ni se cambiaron roles SQL habituales.
- Respaldo y SQL incremental: `artifacts/incident-dev-7dd8764e670a42d9b22ab2e809b87ab9/`. Dump `openirp_dev-before-incidents.dump`; SHA256 `BE868CA51C305358F4A7C0F20FF6A399F24B06ACE8744F563BB320DCAF643A28`. Incluye before/after-counts, incident-counts y restore-verified.
- HTTPS real con el helper local: health 200, tres rutas de incidentes anónimas 401, OpenAPI 200 con rutas de registro/asignación/historial. Evidencia: `artifacts/incident-http-ba483fd3079f4029b717fe100f5fcfaf/`. Solo el cliente de comprobación aceptó el certificado de desarrollo; la API conserva HTTPS/cookies Secure. El proceso propio fue detenido.

Los helpers `./ci/Test-IdentityDocker.ps1` y `./ci/Test-IdentityLocal.ps1` crean sus propios clusters/bases y eliminan únicamente sus recursos. Los clusters y contenedores temporales de esta ejecución quedaron detenidos; los contenedores habituales de PostgreSQL y Ollama permanecen activos. La base PostgreSQL 18 del servicio del sistema no se modificó. Los artefactos están ignorados por Git.

La garantía de un snapshot por revisión corresponde a estos servicios y su protocolo transaccional; SQL directo y futuros escritores deben respetarlo. Las protecciones SQL del historial impiden mutación ordinaria, pero no obligan a cualquier escritor externo a registrar sus cambios de negocio. Esta subfase continúa usando el bloqueo global de Identity; la coordinación granular por tenant sigue pendiente de diseño y pruebas equivalentes.
