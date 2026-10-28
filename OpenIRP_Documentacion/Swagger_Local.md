# Swagger UI local

Swagger UI está disponible únicamente en Development en https://localhost:7113/swagger.
Consume el documento existente /openapi/v1.json; no cambia el generador de contratos.
Los recursos de Swagger están incluidos en el paquete Swashbuckle.AspNetCore.SwaggerUI 10.3.1, sin CDN ni validador externo.

Desde la raíz del repositorio, con Docker PostgreSQL local iniciado:

```powershell
./ci/Start-LocalApi.ps1
```

Abrir https://localhost:7113/swagger y usar «Try it out» / «Execute».
Para iniciar sesión, ejecutar POST /api/auth/login con un usuario ya aprovisionado.
El navegador conserva la cookie HttpOnly; no se pega en «Authorize» ni se usa un bearer token.
Swagger obtiene automáticamente un token nuevo desde GET /api/auth/csrf antes de cada escritura,
incluidos login/logout, y lo envía en X-CSRF-TOKEN. No se omiten autenticación, permisos ni CSRF.
El interceptor solo permite solicitudes al mismo origen. No crear usuarios por registro público.

Las operaciones reales tienen efectos sobre la base configurada. Para pruebas de negocio usar
usuarios/datos locales apropiados. No hay nuevas migraciones, cuentas ni cambios de datos al habilitar Swagger.
En Production, /swagger y /openapi/v1.json no se publican.

Configuración: apps/api/OpenIRP.Api/Program.cs y OpenIRP.Api.csproj.
Referencia oficial: https://learn.microsoft.com/aspnet/core/fundamentals/openapi/using-openapi-documents?view=aspnetcore-10.0

## Verificación local — 2026-10-26

- Release: cero errores y advertencias.
- Development: interfaz, CSS, bundle, index.js y OpenAPI 200.
- Production: las mismas rutas 404.
- Ambos entornos: login sin CSRF 400 y organizaciones sin sesión 401.
- El index.js servido se ejecutó con mocks del navegador: ruta OpenAPI local, validador externo deshabilitado,
  cookies same-origin, token nuevo en cada escritura, rechazo de otro origen y de fallos/token CSRF ausente.
- Instancias HTTPS temporales detenidas; conexión ficticia sin acceso a PostgreSQL ni migraciones.
- Evidencias locales: artifacts/swagger-smoke-233e557a37c943bfbc1b3af75d1e63cf/results.json y swagger-config.js.
- No se probó login con una cuenta real ni se repitió la regresión de negocio para este cambio de interfaz.
