# Paso 26: Identity y administración local

Continuación posterior: [auditoría administrativa básica](Paso26_Auditoria_Local.md), con nueva migración y suite de 37 pruebas. Las evidencias de 22 pruebas de esta guía corresponden a la subfase inicial.

Implementación local del 22 de octubre de 2026, sobre `feature/paso26-identidad-organizaciones`, desde `f53c99a`. No se modificó GitHub ni se reescribió la migración `20261022130822_InitialIdentityOrganizations`.

## Comportamiento implementado

- El primer `PLATFORM_ADMIN` se crea mediante un comando offline explícito. Requiere `OPENIRP_BOOTSTRAP_ENABLED=true`, correo y contraseña; el arranque normal nunca lo aprovisiona. No existe ruta HTTP de bootstrap ni registro público.
- El bootstrap se cierra si existe cualquier concesión histórica, incluso revocada. No promueve cuentas existentes, no restablece contraseñas y no reactiva privilegios. Una segunda ejecución responde con error y código de salida 1.
- Crear una organización requiere usuario activo y concesión vigente de plataforma. Organización, cuenta nueva y primera membresía `OrgAdmin` se confirman en una transacción. Un fallo de contraseña, correo duplicado o slug duplicado revierte todo.
- `PLATFORM_ADMIN` no implica acceso a organizaciones: cada acceso requiere membresía activa y organización activa. Las operaciones administrativas del tenant requieren `OrgAdmin`; los identificadores de membresías siempre se buscan junto con su organización.
- Se rechaza revocar, suspender o degradar al último `OrgAdmin` con cuenta activa. La desactivación global de una cuenta comprueba todas sus organizaciones en la misma transacción. No hay endpoints de borrado de cuentas, organizaciones ni concesión de roles globales.
- Cada cambio de membresía recibe `expectedUpdatedAt` (null al crear). Si ya cambió, devuelve 409. Los timestamps usan precisión de microsegundos compatible con PostgreSQL.
- Todas las escrituras de administración comparten `pg_advisory_xact_lock(1869636978, 26)`, con transacción READ COMMITTED y espera máxima de 5 segundos. La autorización se vuelve a consultar después del bloqueo. Un conflicto/bloqueo devuelve 409; el cliente debe releer y decidir si reintenta.

**Alcance del bloqueo:** esta primera subfase serializa las escrituras administrativas de toda la base, incluyendo las desactivaciones que afectan a varios tenants. Es una decisión conservadora con coste de concurrencia; evolucionar a bloqueos por organización exige conservar la coordinación con cambios globales de cuenta. Las lecturas no toman ese bloqueo.

**Límite de la garantía:** los servicios y endpoints mantienen la regla del administrador; no es un trigger de base. SQL directo, scripts o futuros servicios que escriban estas tablas deben respetar el mismo protocolo. RLS, roles SQL operativos restringidos, auditoría integral, recuperación de cuenta, MFA, cambio obligatorio de contraseña inicial y verificación de correo mediante envío siguen pendientes del endurecimiento. Esta subfase no certifica uso productivo multiempresa.

## Pruebas reproducibles sin tocar la base habitual

Desde la raíz del repositorio, en PowerShell:

```powershell
./ci/Test-IdentityLocal.ps1
```

Requiere .NET 10, dotnet-ef 10.0.5 y binarios PostgreSQL locales. El valor predeterminado es `C:\Program Files\PostgreSQL\18\bin`; para otros binarios usar `-PostgresBin`. El puerto predeterminado es 55432 y puede cambiarse con `-Port`. Un puerto ocupado se rechaza sin detener su servidor.

El script crea un cluster nuevo en `artifacts/paso26-<id>`, vinculado exclusivamente a 127.0.0.1, con contraseña aleatoria y SCRAM. Nunca se conecta a los puertos habituales 5432/5433. Compila Release, genera el SQL idempotente, aplica la migración a `openirp_test_schema`, ejecuta las restricciones SQL previas dentro de ROLLBACK, guarda un dump de esa base recién creada y verifica el comando offline. Luego ejecuta la suite y comprueba que el modelo no tiene cambios pendientes. Detiene únicamente su propio cluster y restaura las variables de entorno al terminar.

Cada prueba crea una base `openirp_test_<uuid>`, comprueba el nombre real de conexión antes de migrar y la elimina al terminar. El runner xUnit exige `OPENIRP_TEST_POSTGRES` con un servidor loopback desechable y permisos CREATEDB; no omite pruebas si falta configuración. No uses una cuenta operativa para este runner. El archivo CI local incorpora la misma suite sobre su servicio PostgreSQL 17 efímero.

Los directorios `artifacts` están ignorados por Git y conservan TRX, SQL, dump y logs; el cluster queda detenido. No contienen credenciales de la base habitual. La contraseña temporal se elimina del entorno y su archivo inicial se borra; las cuentas de pruebas usan datos sintéticos.

## Preparar una base de desarrollo persistente

La base habitual `openirp_dev` fue inspeccionada vacía, respaldada y migrada el 22 de octubre de 2026. El respaldo previo se restauró con éxito en otro contenedor PostgreSQL 17. No se creó un administrador real ni datos de negocio. Antes de usarla, comprobar host, puerto, nombre, propietario y contenido; obtener y verificar un respaldo si ya tiene datos. PostgreSQL 18 del sistema y PostgreSQL 17 de Compose son instancias distintas. No reutilizar automáticamente sus credenciales ni sus bases.

Configurar `ConnectionStrings:OpenIRP` con user-secrets para la API y `ConnectionStrings__OpenIRP` en la sesión de migración. La factoría EF no lee user-secrets. Revisar primero el script generado y después ejecutar sobre el destino local comprobado:

```powershell
dotnet ef migrations script --idempotent --project apps/api/OpenIRP.Infrastructure --startup-project apps/api/OpenIRP.Api --output ./artifacts/migrations-reviewed.sql
dotnet ef database update --project apps/api/OpenIRP.Infrastructure --startup-project apps/api/OpenIRP.Api
```

Estos comandos no crean respaldos de la base habitual. El script de pruebas sí crea su propio destino nuevo y no necesita respaldar datos existentes.

## Aprovisionar el primer administrador en el destino local verificado

La aplicación ya debe tener su migración aplicada y la conexión local explícita en `ConnectionStrings__OpenIRP`. El operador debe verificar fuera de banda el correo/identidad del destinatario: las cuentas creadas administrativamente se guardan con `EmailConfirmed=true`; no se simula ningún envío de correo.

Leer la contraseña con prompt evita dejarla escrita en el historial. Debe tener 12 a 128 caracteres y cumplir los requisitos Identity de mayúscula, minúscula, número y símbolo. No guardar estos valores en archivos versionados:

```powershell
try {
    $env:OPENIRP_BOOTSTRAP_ENABLED = 'true'
    $env:OPENIRP_BOOTSTRAP_EMAIL = Read-Host 'Correo verificado del administrador'
    $securePassword = Read-Host 'Contraseña inicial' -AsSecureString
    $env:OPENIRP_BOOTSTRAP_PASSWORD = [System.Net.NetworkCredential]::new('', $securePassword).Password
    dotnet run --project apps/api/OpenIRP.Api --no-launch-profile -- --bootstrap-platform-admin
    if ($LASTEXITCODE -ne 0) { throw 'Aprovisionamiento rechazado; revisar el código de error.' }
} finally {
    Remove-Item Env:OPENIRP_BOOTSTRAP_ENABLED, Env:OPENIRP_BOOTSTRAP_EMAIL, Env:OPENIRP_BOOTSTRAP_PASSWORD -ErrorAction SilentlyContinue
    if ($securePassword) { $securePassword.Dispose() }
}
```

Este comando no abre servidor HTTP ni aplica migraciones. No se creó un administrador real del usuario: solamente se aprovisionaron cuentas sintéticas dentro de las bases desechables.

## Probar los endpoints

Para la instalación Docker local inspeccionada, ejecutar primero `./ci/Start-LocalApi.ps1 -Bootstrap` en una terminal interactiva para introducir correo y contraseña mediante prompt. Luego ejecutar `./ci/Start-LocalApi.ps1` para iniciar HTTPS. El helper obtiene las credenciales del contenedor local en memoria, comprueba que corresponde a `openirp_dev` en 127.0.0.1:5433, no imprime secretos, no los guarda y restaura el entorno al terminar. No ejecuta migraciones. Requiere el certificado de desarrollo local confiable.

Si prefieres configurar user-secrets por separado, ejecutar `dotnet run --project apps/api/OpenIRP.Api --launch-profile https`, con el certificado de desarrollo local confiable. La URL es `https://localhost:7113`. Las cookies son Secure, HttpOnly, SameSite Strict, con duración fija de 30 minutos; usar HTTPS. No se habilitó CORS.

1. GET `/api/auth/csrf`, conservando cookies y el `token` de respuesta.
2. POST `/api/auth/login`, JSON `{ "email": "correo-verificado", "password": "contraseña" }`, con encabezado `X-CSRF-TOKEN`.
3. GET `/api/auth/csrf` otra vez después del login para obtener un token vinculado a la identidad autenticada. Conservar cookies y usar ese token en POST/PATCH/logout.
4. El administrador de plataforma puede POST `/api/organizations` con el contrato siguiente. El primer administrador inicia sesión por separado para operar dentro de su organización.

```json
{
  "name": "Organización de prueba",
  "slug": "organizacion-prueba",
  "countryCode": "CL",
  "firstAdministrator": {
    "email": "admin-organizacion@example.test",
    "password": "Sustituir-Por-Secreto-Unico!2026"
  }
}
```

| Ruta | Autorización / efecto |
|---|---|
| GET `/api/organizations` | Solo organizaciones activas con membresía propia activa. |
| POST `/api/organizations` | Plataforma vigente; devuelve 201 y organización, nunca contraseña/hash. |
| GET `/api/organizations/{id}/memberships` | ORG_ADMIN del mismo tenant. |
| POST `/api/organizations/{id}/memberships` | ORG_ADMIN; `{ "userId": "uuid-conocido", "role": "OrgAdmin" }` para cuenta activa existente. |
| PATCH `/api/organizations/{id}/memberships/{membershipId}` | ORG_ADMIN; `{ "role": "Viewer", "status": "Active", "expectedUpdatedAt": null }`. Usar la revisión devuelta en el siguiente cambio. |
| PATCH `/api/users/{id}/status` | Plataforma vigente; `{ "isActive": false }`; verifica todos los tenants. |
| POST `/api/auth/logout` | Sesión autenticada y CSRF. |

Roles JSON: `OrgAdmin`, `Analyst`, `Viewer`. Estados: `Active`, `Suspended`, `Revoked`. La base conserva `ORG_ADMIN` y sus valores anteriores. Un usuario multi-organización puede agregarse explícitamente por su UUID conocido; el alta inicial de otra organización exige una cuenta nueva para evitar promover o tomar cuentas existentes inadvertidamente.

Cada petición autenticada vuelve a comprobar cuenta activa, bloqueo temporal y security stamp. Cinco intentos de contraseña fallidos bloquean durante 15 minutos. La pérdida de una membresía o de una concesión de plataforma se verifica en la siguiente operación. La desactivación de cuenta cambia el stamp e invalida sus sesiones, incluso después de reactivarla.

## Inventario de archivos

- `apps/api/OpenIRP.Application/Identity/IIdentityAdministration.cs`: contratos y errores de negocio.
- `apps/api/OpenIRP.Infrastructure/Identity/IdentityAdministration.cs`: aprovisionamiento, transacciones, permisos y concurrencia.
- `apps/api/OpenIRP.Api/Program.cs`: DI, comando offline, cookies, CSRF y comprobación de sesiones.
- `apps/api/OpenIRP.Api/Controllers/IdentityAdministrationControllers.cs`: autenticación, organizaciones, membresías y estado de cuenta.
- `apps/api/OpenIRP.IntegrationTests/OpenIRP.IntegrationTests.csproj` y `IdentityAdministrationTests.cs`: runner y pruebas con PostgreSQL real y HTTP TestServer.
- `apps/api/OpenIRP.slnx`: incorpora el proyecto de pruebas.
- `ci/Test-IdentityLocal.ps1`: validación Windows en cluster nuevo.
- `ci/Start-LocalApi.ps1`: conexión temporal a Docker local, bootstrap interactivo y arranque HTTPS.
- `ci/verify-identity-schema.sql`: admite `foreign_key_violation` y `restrict_violation` al rechazar el borrado de un usuario referenciado; ambas conservan el rechazo previsto.
- `.github/workflows/postgres-identity-integration.yml`: agrega variables y ejecución de la suite; solo editado localmente, sin disparar CI remota.
- `README.md`, `OpenIRP_Documentacion/OpenIRP_Planificacion_Integral.md` y este documento: estado y procedimiento.

## Referencias de implementación

- [PostgreSQL: bloqueos explícitos y advisory locks](https://www.postgresql.org/docs/18/explicit-locking.html): el protocolo advisory debe ser respetado por los escritores.
- [Microsoft: prevención CSRF en ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0): protección por token para sesiones con cookies.
- [Npgsql: códigos de error PostgreSQL](https://www.npgsql.org/doc/api/Npgsql.PostgresErrorCodes.html): códigos de restricciones y conflictos.

## Resultados de esta ejecución

- Release: 0 errores, 0 advertencias. PostgreSQL 18 nativo: 22/22; PostgreSQL 17.11 Docker: 22/22; ninguna prueba omitida.
- La migración aprobada no cambió y EF no detecta diferencias de modelo. En la base habitual ya aplicada, el historial registra `20261022130822_InitialIdentityOrganizations` / 10.0.5; usuarios, organizaciones y administradores siguen en cero.
- Respaldo previo de `openirp_dev`: `artifacts/paso26-dev-migration-8a6c4702a55c4e62acce7d8282a997fa/openirp_dev-before-paso26.dump`. Restauración comprobada en otro contenedor antes de migrar. SHA256: `55ABB09C9FD46E842D87990B13981711D2CBA356CE78211664058B162A717172`.
- TRX: `artifacts/paso26-f858a3dc5f2c4a00a2513efe8329985b/paso26.trx` y `artifacts/paso26-pg17-ff41cf6a313549f999e0a8f3cfcf2141/paso26-pg17.trx`.
- API real iniciada con el helper: HTTPS health=200, CSRF=200/token, organizaciones anónimas=401; luego detenida. Los logs están en `artifacts/paso26-http-smoke`. La comprobación aceptó el certificado de desarrollo solo en el cliente de prueba; el helper utiliza HTTPS normal.
- Docker Desktop y los contenedores locales existentes quedaron activos; las instancias temporales de validación quedaron detenidas/eliminadas. No hubo commit, push, merge ni ejecución de CI remota.
