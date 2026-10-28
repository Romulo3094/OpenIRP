# Iteración 17 — Archivos privados de evidencia

Fecha: 2026-10-26. Amplía las evidencias TEXT/REFERENCE de la Iteración 16.

## Decisiones del usuario

Aprobadas expresamente para esta entrega:

- PDF, PNG, JPEG y TXT, con máximo de 10 MB por archivo (10 × 1024 × 1024 bytes).
- Conservar los archivos retirados en almacenamiento privado; impedir nuevas descargas y no realizar borrado físico automático.

RESTRICTED permanece bloqueado hasta definir permisos adicionales. No se habilitan registro público, permisos globales, omisiones de tareas ni IA.

## Funcionalidad

Carga multipart de un archivo por evidencia FILE, metadata inmutable, SHA-256, almacenamiento privado por UUID independiente del nombre original, descarga autenticada, historial de persistencia y recuperación administrativa.

Los archivos nunca se publican como directorio estático ni se identifican físicamente con un nombre proporcionado por el cliente. El DTO no devuelve ruta ni storageId. El detalle general de evidencia devuelve content=null para FILE: los bytes solo se obtienen en el endpoint de descarga.

Se comprueban extensión y MIME permitidos, cabeceras básicas (y terminación en PDF/JPEG), TXT UTF-8 sin controles binarios, tamaño real al copiar y SHA-256. Estos controles no equivalen a un escáner antimalware ni a un parser completo de documentos/imágenes. No se ejecuta ni se consulta el contenido. No se admiten ejecutables por el flujo de formatos permitido.

## Almacenamiento local

La API necesita EvidenceFiles:RootPath, por ejemplo EvidenceFiles__RootPath como variable de entorno, con un directorio absoluto local dedicado a OpenIRP y fuera de wwwroot/public. No usar una carpeta compartida para otros fines: el servicio restringe los permisos del directorio privado.

- Windows: ACL sin permisos heredados, para el usuario del proceso y LOCAL SYSTEM.
- Otros sistemas soportados por el filesystem: permisos de usuario sobre carpetas/archivos.
- Se rechazan rutas UNC y reparse points en el árbol utilizado.
- staging/{uuid}.pending y objects/{uuid}.bin; no existe endpoint para navegar esas rutas.
- La configuración ausente rechaza la carga con 503 file_storage_not_configured. No se crean directorios al generar/aplicar migraciones ni al arrancar sin operar archivos.

ci/Start-LocalApi.ps1 configura la raíz desde -EvidenceRoot, o la variable existente, o por defecto D:\Desarrollos\OpenIRP\.openirp-data\evidence. Restaura las variables al terminar. .openirp-data está excluido de Git y del grafo. Si se configura otra ubicación dentro de un repositorio, mantener su exclusión de versionado y extracción.

## Carga y estados

1. Autorización y validación de organización/incidente/tarea y revisión del incidente.
2. Copia acotada a staging; validación de formato y hash, fuera de la transacción de negocio.
3. Transacción que vuelve a comprobar permisos/tokens y registra evidencia ACTIVE, archivo PENDING, ambos historiales, evento de incidente y auditoría.
4. Movimiento sin sobrescritura a objects, en el mismo árbol de almacenamiento; lectura y hash de confirmación.
5. Otra transacción confirma AVAILABLE o registra FAILED/QUARANTINED con historial y auditoría.

Registrar incrementa la revisión del incidente una vez. Las transiciones de persistencia, recuperación, descarga y retirada no incrementan incidente/ejecución.

| Estado | Comportamiento |
|---|---|
| PENDING | Registro confirmado; persistencia aún sin confirmar. No descarga ni soporte de tarea. |
| AVAILABLE | Objeto confirmado y hash verificado. Exige además evidencia ACTIVE y autorización actual. |
| FAILED | Fallo recuperable de persistencia. No descarga ni soporte; recuperación ORG_ADMIN. |
| QUARANTINED | Acceso bloqueado por retirada, falta/integridad no verificable u otra condición insegura. Sin republicación por el endpoint de recuperación de esta entrega. |

REMOVED y la purga física no están habilitados. Una cuarentena no implica que se haya detectado malware: el motivo se devuelve como failureCode.

Una carga confirmada como metadata pero que termina FAILED/QUARANTINED devuelve 202 con su estado, no un éxito de descarga. La recuperación verifica los mismos bytes, tamaño y SHA; no sustituye el contenido registrado. Si objects ya existe y coincide, el movimiento/confirmación se puede repetir sin sobrescribirlo.

### Fallos y commits inciertos

Si la base rechaza de forma definitiva el registro, se elimina únicamente staging propio cuya ausencia en la base se comprueba. Una respuesta perdida o un commit de resultado incierto conserva staging: una consulta sin resultados en otra conexión no prueba que un commit en curso no pueda confirmarse.

Si metadata quedó PENDING y el proceso se interrumpió antes o después del movimiento, ORG_ADMIN puede recuperar usando el UUID de evidencia y su revisión de archivo. El objeto ya movido también se verifica de forma idempotente.

Los temporales desconocidos tras un crash previo al registro no tienen purga automática. Su conciliación operativa queda pendiente; no se borran archivos registrados/retirados para limpiar espacio.

## Descarga, integridad y retirada

Miembros activos autorizados, incluido VIEWER, pueden consultar y descargar según el ámbito actual. PLATFORM_ADMIN no obtiene acceso privado por su grant global. Solo ORG_ADMIN puede recuperar. Escribir sigue reservado a ORG_ADMIN/ANALYST.

La descarga consulta autorización/estado, abre el objeto y verifica tamaño/hash fuera de la transacción, y vuelve a comprobar permiso, estado y revisión antes de emitir la capacidad de lectura y su auditoría. Un fallo de integridad bloquea la descarga y registra cuarentena. Un fallo de auditoría tampoco permite devolver bytes.

Se devuelve como attachment, no-store, con X-Content-Type-Options: nosniff y CSP sandbox, sin procesamiento de rangos. Una descarga ya autorizada puede terminar si la retirada ocurre después; no se afirma poder revocar bytes ya entregados.

Retirar evidencia FILE confirma también la cuarentena del archivo, en la transacción de retirada. Nuevas descargas y finalizaciones quedan bloqueadas; se ocultan título, contenido, nombre original, tipo y SHA en sus DTO correspondientes. Los bytes y los identificadores históricos se conservan sin purga.

Una retirada durante publicación no puede terminar habilitando el archivo. El objeto puede quedar físicamente retenido, pero continúa en cuarentena y sin acceso operativo.

## Integración con tareas

completionEvidenceId conserva la identidad histórica. Para FILE, la evidencia debe estar ACTIVE y su archivo AVAILABLE. El servicio vuelve a comprobar físicamente tamaño/hash antes de completar; el guard SQL exige disponibilidad y ámbito exactos.

La comprobación local acotada a 10 MB en finalización ocurre bajo el gate de la operación. No hay llamadas de red ni análisis de IA en esa transacción. SQL no puede certificar por sí solo el estado de un filesystem.

Siguen aplicando responsable, dependencias, STEP_APPROVAL, estado de incidente/ejecución y versión utilizable. Un fallo/retirada no reescribe una tarea ya completada; conserva la referencia histórica.

## API

Prefijo: /api/organizations/{organization}/incidents/{incident}/evidence

| Método y sufijo | Función |
|---|---|
| POST /files | Carga multipart: metadata JSON y file. 201 si AVAILABLE; 202 si requiere atención. |
| GET /{evidence}/file | Metadata/estado/revisión del archivo, sin rutas internas. |
| GET /{evidence}/file/events?afterRevision=0&limit=50 | Historial paginado de persistencia. |
| POST /{evidence}/file/recovery | Recuperación ORG_ADMIN, con revisión y motivo. |
| GET /{evidence}/file/download | Descarga autenticada y verificada como attachment. |

Cookie autenticada y X-CSRF-TOKEN en escrituras. Historial: cursor >=0; limit 1..100 (50 por defecto).

metadata del multipart:

```json
{
  "classification": "Confidential",
  "title": "Resultado documentado",
  "executionId": null,
  "taskId": null,
  "expectedIncidentRevision": 3,
  "reason": "Registrar soporte del incidente"
}
```

classification y expectedIncidentRevision deben aparecer explícitamente. executionId y taskId son ambos null o UUID del mismo ámbito. El servidor obtiene actor, organización, incidente y tipo FILE; no acepta una ruta ni clave física del cliente.

El campo file lleva un nombre simple y MIME correspondiente: application/pdf, image/png, image/jpeg o text/plain. Para JPEG se admiten .jpg y .jpeg. La sobrecarga multipart está acotada a 64 KiB adicionales y metadata a 16384 caracteres; el tamaño del archivo también se comprueba al leer el stream.

Recuperación:

```json
{
  "expectedFileRevision": 2,
  "reason": "Reintentar la confirmación del archivo original"
}
```

La revisión del archivo es independiente de la de evidencia/incidente. Una recuperación disponible y verificada puede devolver el mismo estado sin incrementar revisión; tokens obsoletos o cuarentena se rechazan. No hay endpoint de reemplazo de bytes.

Errores relevantes: invalid_evidence_filename (400), unsupported_evidence_file_type / evidence_file_signature_mismatch (415), evidence_file_too_large (413), empty_evidence_file (400), file_storage_not_configured / file_storage_unavailable (503), stale_evidence_file_revision / evidence_file_not_available / evidence_file_quarantined (409), organization_admin_required (403).

El POST JSON general conserva el flujo TEXT/REFERENCE; una evidencia FILE se crea exclusivamente mediante carga multipart.

## PostgreSQL y permisos

Migración 20261026183812_PrivateEvidenceFiles:

- evidence_files: 13 columnas; un registro por evidencia, storageId único, metadata inmutable y revisión de persistencia.
- evidence_file_events: 11 columnas; historial append-only por revisión.
- Tres FK nuevas, tres checks nuevos y ampliación del check de tipos de evidencia.
- Siete triggers, incluidos tres diferidos: existencia/ámbito de archivo para FILE, cuarentena al retirar y coherencia del historial.
- Estados controlados, prohibición de eliminación/TRUNCATE y guard de tareas ampliado sin quitar aprobación ni ámbito.
- Down bloqueado si existen archivos, evidencia FILE o historial. Si está vacío, restaura el guard anterior y conserva TEXT/REFERENCE.

Transacciones cortas con gate (1869636978,26), READ COMMITTED y timeout de bloqueo de cinco segundos; lecturas compuestas REPEATABLE READ. La persistencia de disco no forma parte de la transacción PostgreSQL: se conserva el estado recuperable.

El rol SQL runtime recibe SELECT/INSERT y UPDATE solo de estado/revisión/fecha/código de fallo; no puede editar identidad, nombre, tamaño o hash ni borrar historial. RLS y separación operativa completa de credenciales continúan pendientes.

## Respaldo y operación

Una instalación con archivos reales necesita conservar **base de datos y directorio privado**, incluidos staging y objects, con procedimiento coordinado y verificación de hashes al restaurar. Un dump SQL no contiene los bytes del filesystem. Cambiar la raíz configurada requiere preservar y trasladar explícitamente los datos; no se buscan objetos en otras carpetas.

El respaldo de esta entrega corresponde a la base habitual anterior a la migración 18; allí no había evidencia FILE registrada. No se crean cuentas, archivos de ejemplo ni datos de negocio en esa base para las verificaciones. Las cargas reales de pruebas usan bases/directorios temporales propios.

## Archivos y verificación

Cambios principales: EvidenceFile.cs; IFileEvidenceService.cs; PrivateEvidenceStore.cs; IncidentFileEvidenceService.cs; EvidenceFileMapping.cs; EvidenceFilesController.cs; integración en IncidentService, EvidenceView/retirada, ejecución, DbContext y DI; migración/Designer/snapshot; grant-runtime.sql; Start-LocalApi.ps1; exclusiones de datos privados; EvidenceFileTests.cs y fixtures. Se preservaron AGENTS.md, configuración de Graphify, frontend y el trabajo local anterior.

- Compilación Release sin errores ni advertencias; modelo EF sin cambios pendientes.
- Primera cobertura: 143/143 enfocadas; ampliación con carreras y rol SQL limitado. El guard de archivo obligatorio funcionó; se corrigió la aserción para la excepción diferida y pasó el caso aislado.
- Regresión completa: **639/639 en PostgreSQL 17 y 639/639 en PostgreSQL 18.3**, cero fallidas/omitidas. Después del ajuste final para commits inciertos se repitieron **36/36 casos de archivos en cada motor**, incluidos los 35 anteriores y el nuevo caso de confirmación perdida. No sumar 639+36: existe solapamiento. TRX completos: artifacts/private-files-full-pg17-efbb24f1dbc84a40b08f240b64774c64/paso26-pg17.trx y artifacts/private-files-full-pg18-4070a5b5217d496bb5a72a680dab2371/paso26.trx. TRX finales: artifacts/private-files-final-pg17-732290fc9e9a4325840f004a116de253/paso26-pg17.trx y artifacts/private-files-final-pg18-884921537fed4b4fbd90e26721e6c83c/paso26.trx. Release sin errores/advertencias; EF sin diferencias.
- Aplicada únicamente 20261026183812_PrivateEvidenceFiles a openirp_dev (PostgreSQL 17.11, propietario openirp, 127.0.0.1:5433), tras respaldo completo restaurado/verificado y cotejo de conteos/historial. **18 migraciones**, datos previos conservados, ocho capacidades y tres categorías; tablas de negocio vacías, archivos/eventos nuevos vacíos. No se cargaron ejemplos ni se crearon cuentas. Evidencia/SQL/conteos/esquema: artifacts/private-evidence-files-dev-133ea5fec46d43a787de894c07bbb988/. Dump openirp_dev-before-private-evidence-files.dump, SHA256 **15493529627CDAEB35B35E4D20E0075CEBAE4A150F4C08812B4924243B66F3C1**. Este dump anterior a la migración no contiene binarios de filesystem; no había FILE registrados.
- HTTPS con conexión local configurada: health/OpenAPI 200 y **51 operaciones** comprobadas de playbooks/ejecuciones/aprobaciones/evidencias/archivos presentes y con anónimos 401. Evidencia artifacts/private-evidence-files-http-591d18f7b4ae4a1792534332093a85c6/. Flujos autenticados, multipart, CSRF, descarga e integridad probados en bases/directorios temporales; las llamadas anónimas no prueban negocio autenticado sobre la base habitual.
- MCP reindexado y Graphify AST actualizado según AGENTS.md: 2347 nodos, 6184 relaciones y 178 comunidades, con HTML local actualizado. Sin extracción semántica ni llamadas externas de IA. Resultados locales ignorados por Git y datos operativos excluidos.

No se habilitan purga, RESTRICTED, análisis antimalware, omisiones ni IA. Continúa la conciliación operativa de temporales desconocidos y la política completa de retención/revisión de cuarentena.

Instancias propias detenidas; servicios habituales y frontend preservados. Sin commit, push ni merge nuevos.
