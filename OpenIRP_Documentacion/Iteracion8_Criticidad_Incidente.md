# Criticidad evaluada del incidente — desarrollo local

> Esta guía registra el alcance de esa iteración. La [Iteración 9](Iteracion9_Severidad_Incidente.md) amplía después los contratos y activa la clasificación versionada.

Iteración 8, 23 de octubre de 2026, rama `feature/paso26-identidad-organizaciones`. Se preservan los cambios locales de la Iteración 7. El último commit publicado sigue siendo `0c70669`; esta subfase no realiza commit, push ni merge.

## Evaluación separada de los reportes

`assessedCriticality` representa una decisión explícita sobre la criticidad del contexto afectado: Low, Medium, High, NotApplicable o null. No modifica la criticidad general del inventario ni `incident_assets.reported_criticality`. Un reporte desconocido sigue desconocido aunque el analista evalúe después el incidente.

NotApplicable significa que el analista confirma que no corresponde una criticidad de activo; no equivale a Low. null significa que aún falta evaluar. No se asigna automáticamente el máximo de los reportes: se conserva como evidencia y se exige justificar cualquier decisión que lo reduzca. También puede evaluarse un incidente sin activos asociados, con motivo explícito.

`isCriticalityAssessmentComplete` solo indica que el valor es no nulo. No confirma la clasificación completa ni habilita cierre. CIA y contexto operacional permanecen independientes; severity y maximumSeverityReached todavía no existen como campos activos del contrato. La política futura sigue siendo Pending/severity=null si falta cualquier entrada requerida.

## Contrato HTTP

PUT `/api/organizations/{org}/incidents/{incident}/criticality`, autenticado y con `X-CSRF-TOKEN` vigente. Ejemplo:

```json
{
  "assessedCriticality": "High",
  "reevaluationJustification": null,
  "reason": "La función afectada es esencial para la operación de la organización",
  "expectedRevision": 3
}
```

El valor y expectedRevision deben aparecer explícitamente; omitirlos produce 400. null registra una evaluación aún pendiente y conserva el intento en la historia. El motivo es obligatorio, no blanco y de hasta 2000 caracteres. ReevaluationJustification es opcional salvo los casos siguientes, y también tiene un máximo de 2000 caracteres.

Se exige justificación adicional no blanca cuando:

- El valor Low/Medium reduce el máximo de criticidad conocido en los reportes originales.
- La nueva decisión reduce la última evaluación no nula registrada, incluso después de un reset o invalidación.
- Se declara NotApplicable existiendo cualquier asociación de activos, aunque sus criticidades reportadas sean desconocidas.

El motivo general no sustituye esta justificación. Se conserva la última decisión conocida del historial para impedir eludir el control mediante High → null → Low o agregando un activo entre ambas decisiones. Esta referencia no es el máximo histórico de severidad.

Detalle y listado muestran assessedCriticality, criticalityAssessedAtRevision e isCriticalityAssessmentComplete. La revisión indica el evento de la última evaluación explícita; puede existir con valor null. Los eventos exponen ambos campos históricos por snapshot. La revisión obsoleta devuelve 409; valor/justificación inválidos, 400. Campos extra como severity o status no permiten forzar clasificación o cierre.

## Cambios de activos e invalidación

Agregar un reporte, adjuntar un activo inventariado o vincular un reporte existente cambia el contexto observado. La misma transacción deja assessedCriticality y criticalityAssessedAtRevision en null. El evento de activos conserva ese estado pendiente y la decisión anterior permanece intacta en sus eventos.

Se exige una nueva evaluación sobre la revisión actual. Edición de título/descripción, asignación, CIA, capacidades y contexto operacional conservan la criticidad vigente en sus snapshots. Una edición ordinaria del inventario no reescribe los reportes de incidentes ni reevalúa automáticamente su criticidad.

## Evidencia reproducible y acotada

El evento CRITICALITY_ASSESSED incorpora `details.criticalityAssessment`:

- Valor, revisión de evaluación y referencia a la última decisión conocida: previousAssessmentValue/previousAssessmentRevision.
- source=IncidentAssetReports, cantidad de asociaciones y cantidad con criticidad reportada desconocida.
- highestReportedCriticality y assetContextRevision, la máxima revisión de reporte/vinculación observada, o cero sin asociaciones.
- sourceDigestVersion=1 y sourceDigestSha256.
- ReevaluationJustification normalizada, cuando corresponde. El motivo general está en el evento padre.

El servicio lee los identificadores y revisiones de las asociaciones ordenadas por UUID dentro de la transacción/bloqueo común. Alimenta incrementalmente SHA-256 con UTF-8, una línea por asociación, formato invariante:

`associationId:D|assetId:D|reportedCriticality|reportedAtRevision|linkedAtRevision\n`

Los nullable se representan vacíos; el enum usa su nombre del contrato v1. El separador final es LF. No se incluyen nombres, descripciones ni texto privado libre. El streaming mantiene memoria acotada y el resumen no crece con el número de asociaciones; evita superar el límite JSON del historial.

La huella identifica el conjunto de fuentes, no reemplaza la evidencia ni constituye una firma de auditoría. Los UUID y revisiones se recuperan del historial de activos del mismo incidente. Para reconstruir el contexto R, se incluyen asociaciones con reportedAtRevision <= R y se considera nulo el vínculo cuyo linkedAtRevision sea posterior a R. Los reportes originales y los eventos de vinculación conservan esas referencias. Las evaluaciones no consultan ni afirman haber utilizado la criticidad actual del inventario.

## Autorización y atomicidad

ORG_ADMIN y ANALYST activos de la misma organización pueden evaluar; VIEWER puede consultar. PLATFORM_ADMIN no obtiene acceso implícito al tenant. El servicio revalida cuenta activa/no bloqueada, organización y membresía después de adquirir el bloqueo compartido.

Evaluación, revisión, evento y SUCCESS de auditoría se confirman o revierten juntos. La invalidación por activos también es transaccional. Dos escritores con la misma revisión no pueden confirmar ambos ni usar fuentes de una revisión obsoleta. El motivo y la justificación permanecen en el historial organizacional; la auditoría administrativa recibe los metadatos mínimos existentes.

El trigger SQL y la comprobación EF append-only protegen los snapshots. Un propietario puede alterar objetos; RLS, separación operativa completa de credenciales y controles globales de auditoría siguen pendientes. SQL directo y futuros escritores deben respetar el mismo protocolo.

## Migración y archivos

`20261023200334_IncidentCriticalityAssessment` agrega cuatro columnas nullable sin DEFAULT: assessed_criticality y criticality_assessed_at_revision en incidents e incident_events. Dos checks validan catálogo y referencias de revisión; se amplía el check de acciones. No rellena datos históricos ni deshabilita triggers. Down rechaza valores, referencias o eventos de evaluación, aun cuando el estado actual haya vuelto a null.

Archivos en apps/api/:

- Domain/Entities/IncidentCriticality.cs e IncidentEntities.cs.
- Application/Incidents/IncidentCriticalityContracts.cs e IIncidentService.cs.
- Infrastructure/Incidents/IncidentCriticalityService.cs e IncidentService.cs.
- Infrastructure/Persistence/Configurations/IncidentMapping.cs, migración/designer y snapshot.
- Api/Controllers/IncidentCriticalityController.cs.
- IntegrationTests/IncidentCriticalityTests.cs y ampliación de AdministrativeAuditTests.cs.

También se actualizan esta guía, README, la propuesta de severidad y la planificación maestra.

## Verificación

- Compilación Release sin errores ni advertencias; EF sin diferencias de modelo.
- PostgreSQL 17: **259/259 correctas**, cero fallidas/omitidas. TRX: `artifacts/paso26-pg17-a39d69f630d240e4a66f75f945bf968d/paso26-pg17.trx`.
- PostgreSQL 18.3 nativo aislado: **259/259 correctas**, cero fallidas/omitidas. TRX: `artifacts/paso26-00d12df41413483eb2be6dfdd7ded010/paso26.trx`. EF sin diferencias; clúster temporal detenido. No se modificó el servicio PostgreSQL del sistema.
- Se agregaron 43 casos sobre las 216 pruebas anteriores y se amplió el LOGIN SQL limitado. Cubren valores/null/NotApplicable, conservación de reportes, reducciones justificadas y protección tras reset/invalidation, fuentes y huellas, snapshots posteriores, roles/contextos inactivos/tenants, validación/revisión, carreras forzadas, rollback de evaluación/invalidation si falla cualquiera de los historiales, checks SQL, upgrade/downgrade y HTTP/CSRF.
- La primera ejecución PostgreSQL 17 pasó 253/253. Después se incorporaron seis casos de reducción frente a la última decisión conocida y se repitió la batería completa sobre el código final; la evidencia final es 259/259.
- Migración aplicada únicamente a openirp_dev en PostgreSQL 17 habitual, 127.0.0.1:5433. Dump completo restaurado en otro contenedor sin puertos ni volúmenes y conteos/historial cotejados antes de aplicar. Se preservaron todas las tablas de negocio/identidad/auditoría vacías, ocho capacidades y tres categorías. No se fabricaron evaluaciones.
- Respaldo/SQL/conteos/restauración/esquema: `artifacts/incident-criticality-dev-ff919de8d006485ba3f289176b550d26/`. Dump `openirp_dev-before-incident-criticality.dump`, SHA256 `34BE59A865E43397EE9AA868DC488C073C9F385D4B6D1803E7A5E42B705B18C8`. Cuatro columnas nullable sin DEFAULT, dos checks validados, acción nueva y trigger del historial verificados.
- HTTPS real: health 200, OpenAPI 200 con PUT de criticidad y escritura anónima 401. Evidencia: `artifacts/incident-criticality-http-c2b015aeb71c4172882448816f84583f/`. El proceso propio se detuvo al terminar. Solo el cliente de comprobación aceptó el certificado local; la API conserva HTTPS/cookies Secure.
- Sin nuevo commit/push/merge. Los cambios quedan locales junto con la Iteración 7, posteriores al commit publicado 0c70669.

## Siguiente subfase

Validar los umbrales de la propuesta de severidad y el tratamiento de incidentes preexistentes antes de activar el motor. Después, implementar clasificación actual/máxima, transiciones y aprobaciones de cierre. Esta iteración registra las entradas y decisiones de criticidad; no presenta una matriz provisional como política ya validada.
