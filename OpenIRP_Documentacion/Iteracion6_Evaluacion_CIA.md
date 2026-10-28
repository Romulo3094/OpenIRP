# Evaluación CIA — desarrollo local

> Esta guía registra el alcance de esa iteración. La [Iteración 9](Iteracion9_Severidad_Incidente.md) amplía después los contratos y activa la clasificación versionada.

Iteración 6, 23 de octubre de 2026, rama `feature/paso26-identidad-organizaciones`. Se conservan los cambios locales pendientes de las subfases anteriores. Esta entrega no crea un nuevo commit, push ni merge.

## Alcance implementado

Cada incidente conserva `confidentialityImpact`, `integrityImpact` y `availabilityImpact`. Valores por dimensión: `None`, `Low`, `Medium`, `High`, o null si todavía no se ha evaluado. None representa una evaluación explícita sin impacto identificado; null representa desconocimiento. No se sustituyen desconocidos por un nivel bajo.

`isImpactAssessmentComplete` se deriva de que las tres dimensiones sean no nulas. Tres None constituyen una evaluación completa de esas dimensiones, sin afirmar ausencia de incidente ni autorizar descarte/cierre. La evaluación puede ser parcial y puede revisarse o volver a null con motivo y revisión vigente.

La operación reemplaza explícitamente las tres dimensiones: no es un PATCH parcial. Para conservar un valor, reenviarlo junto con los demás. Omitir cualquiera produce 400; enviar null lo vuelve a marcar como sin evaluar. Los incidentes existentes y los eventos anteriores reciben null, sin inventar una evaluación retroactiva.

El estado del incidente continúa REGISTERED en esta subfase. CIA no produce una transición, una severidad ni una aprobación de cierre. La regla de severidad requiere completar sus entradas/política y se dejó una [propuesta concreta para revisar](Propuesta_Regla_Severidad_v1.md), sin motor activado.

## Permisos, revisiones e historia

ORG_ADMIN y ANALYST activos de la misma organización pueden evaluar CIA; VIEWER activo puede consultar valores e historial. PLATFORM_ADMIN no adquiere permiso organizacional implícito. Se comprueban cuenta activa/no bloqueada, organización activa y membresía/rol vigentes.

Cada PUT exige motivo no vacío de hasta 2000 caracteres y `expectedRevision` del incidente. Las escrituras usan la transacción/bloqueo compartido de Identity/inventario/incidentes, con autorización revalidada después de adquirirlo. Una revisión obsoleta devuelve 409. Dos evaluaciones simultáneas con la misma revisión no pueden confirmar ambas ni mezclar dimensiones.

El cambio de las tres dimensiones, su revisión, el evento `IMPACT_ASSESSED` y el SUCCESS de auditoría se confirman o revierten juntos. Un fallo de cualquiera de los historiales revierte los valores. El canal de rechazo conserva los límites ya documentados.

Los valores CIA se incorporan como columnas tipadas al snapshot de **todos** los eventos posteriores del incidente, incluyendo edición, asignación, activos y capacidades. No solo aparecen en IMPACT_ASSESSED: una modificación posterior conserva la evaluación vigente. Se exponen también en el detalle/listado actuales y en cada item del historial. Los eventos anteriores permanecen null y los snapshots históricos no se actualizan al reevaluar el incidente.

Auditoría administrativa: acción `incident.impact_assessed`, UUID/revisión mínimos del incidente; el motivo privado no se copia a auditoría global. Actor, instante UTC y correlación se conservan en el historial autorizado. El trigger SQL y la comprobación EF append-only protegen también las nuevas columnas del historial. Los checks SQL aceptan únicamente NONE/LOW/MEDIUM/HIGH o NULL.

La prueba de LOGIN SQL limitado existente se amplió para evaluar CIA y comprobar que un evento posterior conserva los valores. Las concesiones de tablas existentes cubren las columnas nuevas; no se cambiaron roles/credenciales del cluster habitual. SQL directo y futuros escritores siguen obligados a respetar el protocolo de revisión e historiales. RLS y separación operativa completa de credenciales siguen pendientes; un propietario puede alterar objetos, por lo que no se afirma inmutabilidad criptográfica.

## Endpoints y ejemplo

Arranque local: `./ci/Start-LocalApi.ps1`. HTTPS `https://localhost:7113`; OpenAPI de desarrollo `/openapi/v1.json`. Usar login/cookies y renovación CSRF de [Identity](Paso26_Administracion_Local.md). PUT requiere `X-CSRF-TOKEN` válido.

| Método/ruta | Uso |
|---|---|
| PUT `/api/organizations/{org}/incidents/{incident}/impact` | Evaluar o reevaluar las tres dimensiones con motivo/revisión. |
| GET `/api/organizations/{org}/incidents/{incident}` | Incluye CIA vigente y bandera de evaluación completa. |
| GET `/api/organizations/{org}/incidents` | Incluye CIA vigente en cada incidente del listado. |
| GET `/api/organizations/{org}/incidents/{incident}/events` | Incluye CIA histórica en cada snapshot. |

Ejemplo de evaluación parcial:

```json
{
  "confidentialityImpact": "None",
  "integrityImpact": null,
  "availabilityImpact": "High",
  "reason": "La confidencialidad y disponibilidad fueron investigadas; integridad pendiente",
  "expectedRevision": 1
}
```

Responde con IncidentItem, revisión incrementada e `isImpactAssessmentComplete=false`. Para una evaluación completa, enviar las tres dimensiones no nulas. Para volver a investigar una dimensión, enviar null explícito conservando las otras y aportando motivo.

Los cuatro campos de dimensiones/revisión deben aparecer en JSON. Enumeraciones fuera de rango, campos omitidos o motivo inválido producen 400. Un incidente ajeno bajo un tenant autorizado produce 404; ausencia de permiso/membresía produce 403. La ruta CIA responde no-store. Campos de cuerpo como severity, maximumSeverityReached, status u organizationId no forman parte del contrato y no cambian esos atributos del servidor.

## Migración y archivos

Nueva migración `20261023032311_IncidentImpactAssessment`: añade tres columnas nullable a incidents y tres al historial, checks de enumeración y acción nueva. Se conservaron las seis migraciones anteriores. El downgrade se rechaza si hay valores CIA o eventos IMPACT_ASSESSED, incluso cuando una reevaluación posterior volvió a null; no elimina silenciosamente la historia.

- `apps/api/OpenIRP.Domain/Entities/ImpactLevel.cs`, `IncidentEntities.cs`: enum y campos CIA del agregado/snapshot.
- `apps/api/OpenIRP.Application/Incidents/IncidentImpactContracts.cs`, `IIncidentService.cs`: contrato de escritura y respuestas ampliadas.
- `apps/api/OpenIRP.Infrastructure/Incidents/IncidentImpactService.cs`, `IncidentService.cs`: evaluación y conservación de CIA en el escritor común.
- `apps/api/OpenIRP.Infrastructure/Persistence/Configurations/IncidentMapping.cs`: conversiones, checks y acción.
- Migración `20261023032311_IncidentImpactAssessment.cs`, diseñador y snapshot EF.
- `apps/api/OpenIRP.Api/Controllers/IncidentImpactController.cs`: PUT protegido.
- `apps/api/OpenIRP.IntegrationTests/IncidentImpactTests.cs`, `AdministrativeAuditTests.cs`: casos CIA y ampliación runtime.
- Esta guía, propuesta de severidad, guía previa, README y planificación maestra.

## Verificación

- Release: cero errores y advertencias. EF sin diferencias de modelo pendientes.
- PostgreSQL 17.11: **181/181 correctas**, cero fallidas/omitidas. TRX: `artifacts/paso26-pg17-a875949ae95a4505b75285cf250b10ae/paso26-pg17.trx`.
- PostgreSQL 18.3 nativo aislado: **181/181 correctas**, cero fallidas/omitidas. TRX: `artifacts/paso26-20c37bc4411e44059ee03631cd303af9/paso26.trx`.
- Son 25 casos nuevos sobre los 156 previos y la ampliación del LOGIN runtime. Incluyen null/None, los cuatro niveles, evaluaciones parciales/reset, snapshots en otros módulos, permisos/tenants/contexto inactivo, revisiones, concurrencia forzada en pg_locks, rollback de ambos historiales, checks SQL, inmutabilidad, upgrade con eventos previos y downgrade que conserva historia. HTTP comprueba campos explícitos/CSRF y que el cliente no puede imponer severidad ni cierre.
- La migración se aplicó únicamente a `openirp_dev`, PostgreSQL 17 habitual en `127.0.0.1:5433`, tras restaurar el respaldo completo en otro contenedor sin puertos publicados ni volúmenes y cotejar conteos/historial. Se conservaron los conteos anteriores: identidades, organizaciones, membresías, concesiones, auditoría, activos, declaraciones, incidentes, eventos, asociaciones y evaluaciones de capacidades en cero; ocho capacidades y tres categorías. No se crearon cuentas reales ni se cambiaron roles SQL habituales.
- Se verificaron seis columnas CIA nullable sin DEFAULT, dos checks CIA validados y protección del historial habilitada. La migración no presupone None en datos previos y no ejecuta DML para inventar evaluaciones.
- Respaldo/SQL/conteos: `artifacts/incident-impact-dev-67c3b4aaae204b71a91d533979e20c02/`. Dump `openirp_dev-before-incident-impact.dump`, SHA256 `429B5325C5E4E35C3B55EBAC12B3756BBD17574D97C8F5FC86B814122F1A58B8`. Incluye restore-verified y schema-verified.
- HTTPS real: health 200, PUT CIA anónimo 401 y OpenAPI 200 con la ruta nueva. Evidencia: `artifacts/incident-impact-http-e4d4b0381211436eacdd57e9680535fd/`. Solo el cliente de comprobación aceptó el certificado local; la API conserva HTTPS/cookies Secure. El proceso propio se detuvo al terminar. Los clusters temporales se detuvieron y el PostgreSQL 18 del servicio del sistema no se modificó; los contenedores habituales PostgreSQL/Ollama siguen disponibles. La primera ejecución PostgreSQL 17 detectó un fallo en la prueba HTTP que leía dos veces el mismo stream; la prueba se corrigió para analizar un único cuerpo almacenado. No requirió cambiar el comportamiento de la aplicación.

La propuesta de severidad contiene una matriz y escenarios verificados en un prototipo matemático aislado: 3072 combinaciones completas, 12800 comparaciones de monotonicidad, seis entradas desconocidas y 14 escenarios. Evidencia: `artifacts/severity-proposal-fa6ccb7aec6040c0b0a11ccabf19e88c/`. Es una comprobación matemática de la propuesta, no validación empírica ni un motor activado en la API. El usuario confirmó el 23 de octubre de 2026 la política de desconocidos: Pending y severity=null hasta completar todas las entradas requeridas, sin severidad provisional. Esta decisión no activa el motor. CIA sí se integra y prueba sobre PostgreSQL real. El ciclo de vida completo, alcance/afectación operacional, criticidad reevaluada, severidad actual/máxima y aprobaciones de cierre continúan pendientes de las siguientes subfases.
