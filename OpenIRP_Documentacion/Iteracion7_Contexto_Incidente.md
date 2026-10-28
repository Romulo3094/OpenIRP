# Alcance y afectación operacional — desarrollo local

> Esta guía registra el alcance de esa iteración. La [Iteración 9](Iteracion9_Severidad_Incidente.md) amplía después los contratos y activa la clasificación versionada.

Iteración 7, 23 de octubre de 2026, rama `feature/paso26-identidad-organizaciones`. Las iteraciones 3–6 se publicaron en el commit `0c706696499ae3a935c69aee5cdf135d541f5488` después de pasar 181/181 pruebas PostgreSQL 17. Esta subfase se desarrolla localmente después de ese commit.

## Evaluación implementada

Se registran dos entradas independientes de CIA y de la criticidad del activo:

| Campo | Valor | Significado |
|---|---|---|
| scope | Single | Un recurso o ámbito acotado confirmado. |
| scope | Multiple | Varios recursos, usuarios o servicios afectados. |
| scope | OrganizationWide | Afectación transversal confirmada. |
| operationalImpact | None | Sin degradación operacional identificada. |
| operationalImpact | Degraded | Funciones degradadas pero sostenidas. |
| operationalImpact | MajorInterruption | Interrupción de una función principal con contingencia disponible. |
| operationalImpact | CriticalInterruption | Imposibilidad de sostener una función principal sin contingencia viable. |
| Cualquiera | null | Evidencia insuficiente; entrada todavía sin evaluar. |

Son categorías cualitativas documentadas por el analista; no se inventan minutos o porcentajes ni se cuentan asociaciones de inventario para deducir alcance. Una categoría de incidente, una dimensión CIA o una declaración de capacidades tampoco rellenan automáticamente estas entradas. El motivo conserva la justificación/evidencia de la evaluación.

`isContextAssessmentComplete` solo indica que ambas entradas son no nulas. No declara completa toda la evaluación de severidad: la criticidad evaluada sigue pendiente y el motor no está activado. La política confirmada por el usuario se mantiene: cualquier entrada requerida desconocida producirá Pending y severity=null cuando exista el motor, sin severidad provisional.

## Contrato HTTP

PUT `/api/organizations/{org}/incidents/{incident}/context`, autenticado, con cookie y `X-CSRF-TOKEN` vigentes. Arranque local: `./ci/Start-LocalApi.ps1`, HTTPS `https://localhost:7113`.

```json
{
  "scope": "Multiple",
  "operationalImpact": "Degraded",
  "reason": "Varios usuarios afectados; la función continúa mediante contingencia",
  "expectedRevision": 1
}
```

El PUT reemplaza ambas entradas: deben enviarse explícitamente, aun cuando su valor sea null. Omitir una entrada o expectedRevision devuelve 400; null vuelve a marcarla como pendiente con motivo. No se admiten enum fuera del catálogo. El motivo es obligatorio, no blanco y de hasta 2000 caracteres. Respuesta: IncidentItem con la nueva revisión y los valores vigentes; una revisión obsoleta produce 409.

Detalle/listado muestran `scope`, `operationalImpact` e `isContextAssessmentComplete`. GET events incluye ambas entradas históricas por revisión. Los nuevos campos son aditivos; CIA y su bandera conservan su significado. El cliente no puede imponer severidad, máximo histórico ni un estado de cierre enviándolos como campos extra. El estado sigue REGISTERED.

## Permisos, transacción e historia

ORG_ADMIN y ANALYST activos de la misma organización pueden evaluar; VIEWER activo puede leer. PLATFORM_ADMIN no obtiene acceso implícito al tenant. Se revalidan cuenta activa/no bloqueada, organización y membresía después del bloqueo PostgreSQL común a las escrituras administrativas.

La evaluación, revisión, evento `CONTEXT_ASSESSED` y auditoría SUCCESS `incident.context_assessed` se confirman en la misma transacción. No hay llamadas externas bajo el bloqueo. Dos escritores con la misma revisión no pueden confirmar ambos ni mezclar entradas. Un fallo del historial de negocio o de auditoría revierte ambos valores.

Los valores se copian al snapshot de todos los eventos posteriores, incluyendo CIA, edición, asignación, activos y capacidades. Reevaluar no modifica eventos anteriores. Actor, correlación, motivo y fecha quedan en el historial organizacional; la auditoría administrativa recibe solo los metadatos mínimos existentes, sin copiar el motivo privado.

Se mantienen los límites operativos existentes: el propietario SQL puede alterar objetos, RLS sigue pendiente y el helper habitual conserva sus credenciales locales. La prueba de LOGIN runtime limitado comprueba también esta operación y un evento posterior, manteniendo la denegación SQL de mutaciones del historial.

## Migración y archivos

Migración `20261023141918_IncidentContextAssessment`: cuatro columnas nullable sin DEFAULT en app.incidents y app.incident_events, dos checks para los valores permitidos y ampliación del check de acciones. No inventa evaluaciones de incidentes previos, no reescribe eventos ni desactiva triggers. Down se niega si existe cualquier valor o evento CONTEXT_ASSESSED, incluso si la evaluación actual volvió a null; exige recuperación planificada para conservar evidencia.

Archivos principales:

- Domain/Entities/IncidentContext.cs e IncidentEntities.cs.
- Application/Incidents/IncidentContextContracts.cs e IIncidentService.cs.
- Infrastructure/Incidents/IncidentContextService.cs e IncidentService.cs.
- Infrastructure/Persistence/Configurations/IncidentMapping.cs, migración/designer y snapshot.
- Api/Controllers/IncidentContextController.cs.
- IntegrationTests/IncidentContextTests.cs y ampliación de AdministrativeAuditTests.cs.
- Esta guía, README, propuesta de severidad y planificación maestra.

Las rutas de código son relativas a apps/api/. Los comandos de migración se ejecutan únicamente contra PostgreSQL local identificado y después de respaldo restaurado en un servidor aislado.

## Verificación

- Release sin errores ni advertencias y EF sin cambios de modelo pendientes.
- PostgreSQL 17: **216/216 correctas**, cero fallidas/omitidas. TRX: `artifacts/paso26-pg17-9ad09aae3b724ca19c9a115ef074a948/paso26-pg17.trx`.
- PostgreSQL 18.3 nativo aislado: **216/216 correctas**, sin fallidas/omitidas. TRX: `artifacts/paso26-f5ea5bc7ebe048cb91c31f10c55d1f90/paso26.trx`. Modelo sin cambios pendientes; clúster temporal detenido. No se modificó el servicio PostgreSQL del sistema.
- Se añadieron 35 casos sobre las 181 pruebas anteriores: 12 combinaciones completas, desconocidos/None/reset, conservación en otros módulos/CIA, roles/contexto inactivo/tenants, validación/revisiones, concurrencia forzada con pg_locks, rollback de ambos historiales, checks SQL, upgrade/downgrade y HTTP/CSRF. La prueba runtime SQL existente también se amplió.
- Migración aplicada solo a openirp_dev en 127.0.0.1:5433, tras restaurar el dump completo en un PostgreSQL separado sin puertos ni volúmenes. Conteos anteriores e historial conservados; cuatro columnas nullable sin DEFAULT y dos checks validados, trigger del historial habilitado. Las tablas de negocio/identidad/auditoría continúan vacías; ocho capacidades y tres categorías, cero evaluaciones fabricadas.
- Evidencia de respaldo/restauración/SQL/conteos/esquema: `artifacts/incident-context-dev-b97445be42bb4b18a93232ccf9d9116e/`. Dump `openirp_dev-before-incident-context.dump`, SHA256 `C84739AA53EE432127C9BBB779EE03C0F90C9DD6A70F7C85079C2DF1A42A20CA`.
- HTTPS real: health 200, OpenAPI 200 con la nueva ruta y PUT anónimo 401. `artifacts/incident-context-http-966ba763f3e5412089ab1c0c2ef0190b/`. Proceso propio detenido; solo el cliente de comprobación aceptó el certificado local.

Los cambios de esta iteración quedan locales, posteriores al commit publicado 0c70669. No se crearon cuentas reales, no se cambiaron roles SQL habituales ni se habilitaron registro público o permisos globales.

## Próxima subfase

Evaluación justificada y versionada de criticidad sin sobrescribir snapshots originales; después, validar los umbrales y la inicialización del máximo histórico antes de activar la clasificación determinista. El ciclo de vida completo y las aprobaciones de cierre continúan pendientes.
