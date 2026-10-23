# Capacidades y limitaciones por incidente — desarrollo local

Subfase local posterior a activos afectados, en `feature/paso26-identidad-organizaciones`. Se conservan los cambios pendientes anteriores y las cinco migraciones existentes. Sin nuevo commit, push ni merge.

## Estado efectivo y herencia

`app.incident_capability_assessments` guarda una decisión vigente por organización, incidente y capacidad. El estado efectivo se calcula con esta precedencia:

1. Catálogo inactivo: `Unknown`, origen `InactiveCatalog`, conservando la declaración y evaluación históricas visibles.
2. Evaluación con availability no nulo: se aplica ese valor, origen `IncidentAssessment`.
3. Sin override: se hereda el perfil organizacional actual, origen `OrganizationProfile`.
4. Sin declaración organizacional: `Unknown`, origen `Undeclared`.

`availability: null` significa heredar. `availability: "Unknown"` es una evaluación explícita que prevalece incluso si la organización declara Available. No se borra la fila al restablecer herencia: se conserva su UUID y avanza la revisión del incidente. Las limitaciones son texto independiente del estado: puede heredarse disponibilidad y mantener una limitación específica. Una limitación vacía o de espacios se normaliza a null.

Los cambios del perfil general afectan en vivo a las capacidades heredadas; no modifican las excepciones explícitas ni reescriben snapshots históricos. Cambiar el perfil no incrementa la revisión del incidente: por eso las escrituras comprueban también las revisiones del perfil y del catálogo. La lectura no inserta decisiones ni eventos.

El listado incluye capacidades activas y referencias inactivas que tengan declaración organizacional o evaluación del incidente. No acepta nuevas evaluaciones explícitas sobre catálogo inactivo, pero permite restablecer herencia para retirar un override anterior sin borrar su historia. Al reactivar un catálogo, un override conservado vuelve a aplicarse; un override restablecido permanece heredado. El origen y la vigencia se exponen para evitar presentar una referencia inactiva como disponible.

## Permisos y precondiciones

ORG_ADMIN y ANALYST activos pueden evaluar o documentar limitaciones del incidente; VIEWER activo puede leer. Solo ORG_ADMIN conserva permiso para modificar el perfil general. PLATFORM_ADMIN no obtiene acceso organizacional sin membresía válida. Se validan organización activa, cuenta activa/no bloqueada y membresía/rol vigentes, igual que en el resto de incidentes.

Cada PUT exige explícitamente:

- `availability`: Available, Unavailable, Unknown o null para heredar.
- `expectedRevision`: revisión del incidente leída en la página.
- `expectedOrganizationCapabilityRevision`: revisión de la declaración general, cero si todavía no existe.
- `expectedCatalogRevision`: revisión de la entrada del catálogo.
- `reason`: motivo no vacío de hasta 2000 caracteres.

`limitations` es opcional, hasta 2000 caracteres. Una revisión del incidente obsoleta devuelve 409; cambios del perfil o del catálogo producen 409 `stale_capability_context`. Releer antes de decidir si reintentar. Las precondiciones no se actualizan automáticamente ni se ignoran aunque el estado efectivo aparente ser igual.

## Endpoints y ejemplos

Arranque: `./ci/Start-LocalApi.ps1`. HTTPS `https://localhost:7113`; OpenAPI `/openapi/v1.json` en desarrollo. Aplican login/cookies y renovación CSRF de [Identity](Paso26_Administracion_Local.md). PUT requiere `X-CSRF-TOKEN` válido.

| Método/ruta | Acción |
|---|---|
| GET `/api/organizations/{org}/incidents/{incident}/capabilities` | Estado efectivo, origen, perfil, limitaciones y revisiones. |
| PUT `/api/organizations/{org}/incidents/{incident}/capabilities/{capability}` | Crear/actualizar evaluación o restablecer herencia. |

Ejemplo para una capacidad sin declaración general, catálogo en revisión 1 e incidente en revisión 1:

```json
{
  "availability": "Unavailable",
  "limitations": "Los respaldos no están accesibles durante este incidente",
  "reason": "Comprobación realizada por el analista",
  "expectedRevision": 1,
  "expectedOrganizationCapabilityRevision": 0,
  "expectedCatalogRevision": 1
}
```

La respuesta es `{capability,incidentRevision}`. Restablecer herencia después:

```json
{
  "availability": null,
  "limitations": null,
  "reason": "Se vuelve a usar la declaración general",
  "expectedRevision": 2,
  "expectedOrganizationCapabilityRevision": 0,
  "expectedCatalogRevision": 1
}
```

El listado responde `{items,nextSkip,incidentRevision}`. Cada item incluye availability organizacional y notas, revisión del perfil/catálogo, override, disponibilidad efectiva, origen, limitaciones, assessmentId, assessedAtRevision y último motivo. Los nombres JSON concretos son `organizationAvailability`, `organizationNotes`, `organizationCapabilityRevision`, `overrideAvailability`, `effectiveAvailability`, `source` y los restantes en camelCase.

La página usa una transacción REPEATABLE READ, revalida autorización y obtiene la revisión del incidente y las dependencias desde una misma instantánea PostgreSQL. Diferentes páginas no constituyen una exportación congelada. skip admite 0–1.000.000 y limit 1–100, por defecto 50; orden por código de capacidad. Sin membresía/permiso: 403. Incidente ajeno bajo un tenant autorizado o capacidad inexistente: 404. JSON sin cualquiera de los cuatro campos explícitos availability/revisiones: 400. Las rutas privadas responden no-store.

## Historia, integridad y concurrencia

Las escrituras reutilizan la transacción/bloqueo de Identity, inventario e incidentes, con espera máxima de cinco segundos. Se releen permisos y las tres versiones después de adquirirlo. La decisión, la revisión del incidente, el evento del historial y el SUCCESS de auditoría se confirman juntos. Los fallos de cualquiera de los historiales revierten todo; los rechazos usan el canal global separado ya documentado.

Acciones: `CAPABILITY_ASSESSED` para override explícito, `CAPABILITY_INHERITED` para herencia. `incident_events.details.capability` captura UUID/código/nombre/vigencia/revisión del catálogo, disponibilidad/notas/revisión del perfil observado, override, disponibilidad efectiva, origen, limitaciones, UUID de evaluación y revisión. El motivo pertenece al evento y no se duplica en details. Se preservan snapshots anteriores cuando cambian el perfil, el catálogo o la evaluación.

La auditoría administrativa recibe `incident.capability_assessed`/`incident.capability_inherited`, revisión y UUID mínimos del incidente; no copia notas, limitaciones ni motivos privados. La tabla mutable conserva la decisión actual y el historial append-only conserva las anteriores. Esto no implementa todavía las instantáneas completas de una solicitud de IA: deberán capturar el contexto relevante al enviar cada solicitud, incluido el perfil vigente, sin confundirlo con el snapshot de una decisión anterior.

FK compuesta organización-incidente y FK de capacidad restrictivas, unicidad organización-incidente-capacidad e índices para ambos accesos. `assessed_at_revision > 1` es también token de concurrencia EF. Un trigger SQL protege la identidad de la decisión y exige avance de revisión; otro impide DELETE/TRUNCATE. Se restablece herencia mediante actualización auditada, conservando el marcador.

`ci/grant-runtime.sql` limita esta tabla a SELECT/INSERT y UPDATE solo de availability, limitations, reason y assessed_at_revision. Se verifica con un LOGIN temporal. SQL directo/futuros escritores deben respetar el protocolo transaccional para asegurar revisión del agregado y escritura de ambos historiales. El propietario/superusuario puede alterar objetos; no se afirma inmutabilidad criptográfica. RLS, coordinación granular y separación operativa de credenciales habituales continúan pendientes.

## Migración y archivos

Nueva migración `20261022204102_IncidentCapabilityAssessments`: crea la tabla y amplía las acciones permitidas del historial; no reescribe las cinco migraciones anteriores. El downgrade se rechaza si existen decisiones o eventos nuevos para evitar pérdida silenciosa de historia.

- `apps/api/OpenIRP.Domain/Entities/IncidentCapabilityAssessment.cs`: decisión vigente.
- `apps/api/OpenIRP.Application/Incidents/IncidentCapabilityContracts.cs`, `IIncidentService.cs`: contratos y precondiciones.
- `apps/api/OpenIRP.Infrastructure/Incidents/IncidentCapabilityService.cs`: herencia, cálculo efectivo, lectura coherente y escritura transaccional.
- `apps/api/OpenIRP.Infrastructure/Persistence/Configurations/IncidentCapabilityMapping.cs`, `IncidentMapping.cs`, `OpenIrpDbContext.cs`: modelo e integridad.
- Migración `20261022204102_IncidentCapabilityAssessments.cs`, diseñador y snapshot EF.
- `apps/api/OpenIRP.Api/Controllers/IncidentCapabilitiesController.cs`: rutas privadas.
- `apps/api/OpenIRP.IntegrationTests/IncidentCapabilityTests.cs`, `AdministrativeAuditTests.cs`: pruebas y ampliación del LOGIN SQL limitado.
- `ci/grant-runtime.sql`: permisos acotados.
- README, planificación maestra, guía anterior y esta guía.

## Validación

PostgreSQL 18.3 aislado: 156/156 pruebas correctas, cero fallidas/omitidas; Release sin errores/advertencias y EF sin diferencias de modelo. Son 29 casos nuevos sobre los 127 anteriores, más la ampliación de la prueba runtime. Incluye herencia/Unknown explícito, aislamiento/permisos, snapshots, precondiciones de perfil/catálogo, concurrencia forzada, rollback de ambos historiales, JSON/CSRF, Unicode máximo, integridad SQL, upgrade con historia y rechazo de downgrade destructivo.

TRX PG18: `artifacts/paso26-f59bbe65ee684c099c1125f150f5c4a4/paso26.trx`.

PostgreSQL 17.11: 156/156 pruebas correctas, cero fallidas/omitidas, Release sin errores/advertencias y EF sin diferencias pendientes. TRX: `artifacts/paso26-pg17-17fe2143645645549af752bfaffb85b6/paso26-pg17.trx`.

La migración se aplicó exclusivamente a `openirp_dev`, PostgreSQL 17 habitual en `127.0.0.1:5433`, después de restaurar un respaldo completo en otro contenedor sin puertos publicados ni volúmenes. La copia restaurada conservó los conteos e historial de migraciones. El helper volvió a cotejar los datos antes de migrar y verificó los conteos existentes después: identidades/organizaciones/membresías/concesiones/auditoría/activos/declaraciones/incidentes/eventos/asociaciones continúan en cero, ocho capacidades y tres categorías. La nueva tabla está vacía y ambos triggers están habilitados. No se inventaron cuentas reales ni se modificaron los roles SQL habituales.

Respaldo, SQL incremental y verificaciones: `artifacts/incident-capabilities-dev-f4287cbca0f4488aacc578eb05c56d5b/`. Dump `openirp_dev-before-incident-capabilities.dump`, SHA256 `560C39E26F6BB696D33CD253E4D4E90E85490829B164EC41550E42F4C39999B1`. Incluye before/after-counts, conteo de evaluaciones, restore-verified y triggers-verified.

HTTPS real mediante el helper: health 200, GET anónimo de capacidades del incidente 401, OpenAPI 200 con GET/PUT nuevos. Evidencia: `artifacts/incident-capabilities-http-1b6e121c13ea433d99e95a5cb78a29f8/`. Solo el cliente de comprobación aceptó el certificado local; se conserva HTTPS/cookies Secure en el servidor. El proceso propio se detuvo al terminar. Docker Desktop estaba detenido y fue iniciado para recuperar PostgreSQL 17; el primer intento de ese helper no ejecutó pruebas ni modificó la base habitual. Los helpers crean clusters/bases propios y sus datos sintéticos no se insertan en openirp_dev.

La evaluación CIA se incorporó posteriormente en [la Iteración 6](Iteracion6_Evaluacion_CIA.md). La fórmula de severidad, el ciclo de vida completo y las aprobaciones siguen pendientes de activación. Esta entrega no habilita registro público, bypass por rol global ni acciones remotas sobre dispositivos.
