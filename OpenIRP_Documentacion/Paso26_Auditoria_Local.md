# Paso 26 — auditoría administrativa básica

Implementación local del 22 de octubre de 2026 sobre `feature/paso26-identidad-organizaciones`. Continúa la [subfase de Identity](Paso26_Administracion_Local.md), preserva sus cambios y no modifica GitHub.

## Qué registra

`audit.audit_events` conserva actor (UUID cuando corresponde), tipo de actor, organización opcional, acción, tipo/UUID del recurso, resultado, fecha UTC, correlación y detalles mínimos JSONB.

Acciones cubiertas: `platform.bootstrap`, `organization.create`, `membership.add`, `membership.change`, `user.activate` y `user.deactivate`. El alta de organización representa la creación atómica del agregado con su cuenta y primer administrador. No se guardan correos, nombres, contraseñas, hashes, tokens, cookies ni cuerpos de peticiones. Los cambios de membresía guardan únicamente UUID del usuario, rol y estado resultantes.

Cada operación administrativa correcta y su evento `SUCCESS` se confirman en la misma transacción. Si la inserción del evento falla, se revierte la operación completa, incluso las filas ya insertadas por Identity antes del fallo. No se añadieron eventos retrospectivos para operaciones anteriores a esta migración.

Los rechazos de escrituras administrativas y de lectura de membresías/auditoría se registran mediante otra conexión/transacción, después del rollback. `403/404` se traducen a `DENIED`; otros errores, a `FAILURE`. Se guarda un código controlado, nunca el mensaje de excepción. Todos estos intentos quedan en el ámbito global, sin organización/recurso aportados por la petición: un intento cruzado no puede insertar eventos en el historial del tenant víctima.

La persistencia de un rechazo tiene límite de tres segundos. Si falla, se conserva la denegación original y se emite `AUDIT_REJECTION_NOT_PERSISTED` con correlación y acción. No se promete entrega duradera de rechazos durante una caída de PostgreSQL. Los eventos de login/logout, denegaciones anteriores a los servicios (401, CSRF, model binding), auditoría integral y una cola duradera de seguridad quedan fuera de esta subfase.

## Protección y consultas

- EF rechaza cambios y borrados de entidades de auditoría.
- Un trigger PostgreSQL por sentencia rechaza `UPDATE`, `DELETE` y `TRUNCATE` con SQLSTATE 42501, incluyendo SQL directo.
- Se revocaron privilegios públicos sobre tabla, secuencia y función del trigger.
- `ci/grant-runtime.sql` configura para una cuenta SQL dedicada solo SELECT/INSERT en la tabla de auditoría; no concede UPDATE/DELETE/TRUNCATE/DDL ni propiedad. El script rechaza roles propietarios, administrativos, con membresías o con INHERIT. Los permisos de negocio en `auth/app` siguen separados de la autorización por organización del backend.
- Un propietario/superusuario SQL conserva capacidad de alterar objetos o desactivar protecciones. No se afirma inmutabilidad criptográfica. RLS y separación operativa completa de credenciales siguen pendientes antes de producción.

| GET | Permiso | Datos devueltos |
|---|---|---|
| `/api/organizations/{organizationId}/audit?limit=50&before=123` | Cuenta activa, organización activa y membresía ORG_ADMIN activa del mismo tenant. | Solo eventos de esa organización. |
| `/api/audit/global?limit=50&before=123` | Cuenta activa y concesión PLATFORM_ADMIN vigente. | Solo eventos con `organization_id IS NULL`; ningún bypass a eventos organizacionales. |

La respuesta es `{ "items": [...], "nextBefore": 123 }`. `limit` admite 1 a 100; `before` debe ser positivo. Los eventos se ordenan por ID descendente. Para la página siguiente, usar `nextBefore`; null indica que no había otra página al consultar. No es una instantánea congelada para exportaciones concurrentes. `detailsJson` contiene el objeto mínimo serializado; no es el cuerpo original de la petición. Las respuestas llevan `Cache-Control: no-store`.

El servidor genera `X-Correlation-ID` por scope HTTP/CLI. Un encabezado enviado por el cliente no lo reemplaza. La correlación devuelta por una escritura coincide con su evento; los eventos globales de rechazo conservan esa misma correlación.

## Decisión física acotada

Se mantiene UUID para usuarios, organizaciones, membresías, actores, recursos y correlación. **Excepción documentada a la convención general de UUID de §14.1:** la PK interna de `AuditEvents` usa BIGINT GENERATED ALWAYS AS IDENTITY para proporcionar un cursor indexado estable sin depender del reloj. No es una clave de autorización. El índice `(organization_id, id)` sirve a la consulta por tenant y al ámbito global; existe otro por correlación. Impacto: los contratos de auditoría usan ID/cursor numéricos; el resto del modelo no cambia. La paginación sin duplicados y la protección por tenant están probadas. No se presupone un orden de commit entre transacciones diferentes ni se afirma que la secuencia carezca de huecos tras rollback.

Los UUID históricos de actores/recursos no tienen FK con eliminación en cascada: el registro debe sobrevivir a futuros cambios de ciclo de vida de esos objetos. El diseño de retención y anonimización deberá preservar esta propiedad.

## Migración y permisos

Nueva migración: `20261022175420_BasicAdministrativeAudit`. La migración inicial aprobada permanece intacta. Se aplicó en `openirp_dev` después de respaldar y restaurar la copia en otro contenedor PostgreSQL 17; usuarios, organizaciones, membresías y concesiones mantuvieron sus conteos (todos cero). No se creó ningún usuario de negocio ni rol SQL en el cluster habitual. La auditoría local comienza con cero eventos.

El helper `ci/Start-LocalApi.ps1` conserva la conexión de desarrollo previa del contenedor. **No convierte esa cuenta propietaria en una cuenta SQL limitada.** El script de permisos se probó con un LOGIN NOINHERIT efímero, sin privilegios administrativos, en bases desechables. Para una instalación operativa se debe crear una cuenta de ejecución distinta del migrador/propietario y configurar su secreto de conexión por separado.

El script de permisos se ejecuta como propietario sobre la base correcta, después de la migración. Ejemplo para un rol limitado ya creado por el operador, sin contraseñas en el archivo:

```sql
SET openirp.runtime_role = 'openirp_runtime';
\i ci/grant-runtime.sql
```

No ejecutarlo contra un rol propietario existente. Debe revisarse de nuevo al añadir tablas futuras. La reversión de la migración elimina la tabla de auditoría: solo se ensayó sobre bases vacías desechables; no usarla para deshacer cambios en una base con históricos que deban preservarse.

## Repetir las comprobaciones

Desde `D:\Desarrollos\OpenIRP`, con Docker activo:

```powershell
./ci/Test-IdentityDocker.ps1  # PostgreSQL 17, contenedor sin volúmenes existentes.
./ci/Test-IdentityLocal.ps1   # PostgreSQL 18 nativo, cluster nuevo en artifacts.
```

Ambos ejecutan la solución actual y las 37 pruebas; fallan si faltan herramientas/conexión o si un puerto está ocupado. Las bases de cada caso se crean con nombre aleatorio y se comprueba su nombre antes de migrar. El nuevo runner Docker restaura variables de entorno y elimina únicamente su contenedor etiquetado al terminar. La suite ya figura en el archivo CI local anterior; no se ejecutó CI remota.

## Evidencia de esta subfase

- Release: 0 errores, 0 advertencias. PostgreSQL 17.11: 37/37; PostgreSQL 18.3: 37/37; 0 fallidas y 0 omitidas. Se conservaron las 22 pruebas de Identity y se añadieron 15 casos de auditoría.
- Se verificaron rollback por fallo de auditoría, persistencia independiente del rechazo, fallo del canal de rechazos sin sustituir el 403 original, exclusión de secretos, concurrencia previa, paginación, correlación HTTP, aislamiento, mutaciones SQL/EF rechazadas y operaciones reales usando cuenta SQL limitada.
- La reversión/reaplicación de la migración en una base vacía desechable funcionó. EF no detecta diferencias pendientes de modelo.
- TRX PostgreSQL 17: `artifacts/paso26-pg17-b504fc4e9dea4f4d9a97fec03248c7e5/paso26-pg17.trx`.
- TRX PostgreSQL 18: `artifacts/paso26-f63100bfc31a4ee69f340d5112729b9d/paso26.trx`.
- Respaldo previo a la migración habitual: `artifacts/paso26-audit-dev-f32518942a594799ba871ad1c15cb8bc/openirp_dev-before-audit.dump`; SHA256 `201D7A60C1BE17F4D86B804ED6D1A0FBE1DF394AE08C54F4814E2ABECA59550E`. El directorio contiene el SQL incremental y los conteos antes/después; la restauración y esos conteos se comprobaron antes de migrar.
- HTTPS real mediante helper: health 200; auditoría global/organizacional anónima 401; correlación generada por servidor aunque el cliente envíe otra. Logs y resultado en `artifacts/paso26-audit-http-29aae799f01b4426b6bfdc31b218c92f`. Solo el cliente de esta comprobación aceptó el certificado de desarrollo; la API conserva cookies Secure.

## Archivos de esta subfase

| Archivo | Cambio |
|---|---|
| `apps/api/OpenIRP.Domain/Entities/AuditEvent.cs` | Evento histórico mínimo. |
| `apps/api/OpenIRP.Application/Audit/IAuditReader.cs` | Contratos, página y correlación de scope. |
| `apps/api/OpenIRP.Infrastructure/Audit/AuditRecorder.cs` | Construcción de éxitos y persistencia separada de rechazos. |
| `apps/api/OpenIRP.Infrastructure/Audit/AuditReader.cs` | Autorización, aislamiento y cursor. |
| `apps/api/OpenIRP.Infrastructure/Identity/IdentityAdministration.cs` | Auditoría de escrituras dentro de la transacción y de rechazos controlados. |
| `apps/api/OpenIRP.Infrastructure/Persistence/OpenIrpDbContext.cs` | Mapeo y protección EF. |
| `apps/api/OpenIRP.Infrastructure/Persistence/Migrations/20261022175420_BasicAdministrativeAudit.cs` y `.Designer.cs` | Nueva migración, tabla, índices, checks y trigger. |
| `apps/api/OpenIRP.Infrastructure/Persistence/Migrations/OpenIrpDbContextModelSnapshot.cs` | Modelo actualizado. |
| `apps/api/OpenIRP.Api/Program.cs` | Registro de servicios y correlación HTTP. |
| `apps/api/OpenIRP.Api/Controllers/AuditController.cs` | Endpoints GET protegidos. |
| `apps/api/OpenIRP.IntegrationTests/AdministrativeAuditTests.cs` | 15 casos nuevos sobre PostgreSQL real y HTTP. |
| `apps/api/OpenIRP.IntegrationTests/IdentityAdministrationTests.cs` | Clase parcial para compartir el fixture existente. |
| `apps/api/OpenIRP.IntegrationTests/OpenIRP.IntegrationTests.csproj` | Copia el script de permisos para ensayarlo directamente. |
| `ci/grant-runtime.sql` | Permisos para una cuenta dedicada existente. |
| `ci/Test-IdentityDocker.ps1` | Runner PostgreSQL 17 local reproducible. |
| `README.md`, documentación maestra, guía de Identity y esta guía | Estado, evidencias, decisión física y límites. |

No hubo commit, push, merge ni edición remota de GitHub. No se activaron registro público ni permisos de plataforma sobre tenants. La siguiente pieza del orden acordado es catálogos/capacidades/activos; el endurecimiento pendiente permanece explícito.

Referencias: [PostgreSQL: privilegios y propiedad](https://www.postgresql.org/docs/17/ddl-priv.html) y [CREATE TRIGGER](https://www.postgresql.org/docs/17/sql-createtrigger.html).
