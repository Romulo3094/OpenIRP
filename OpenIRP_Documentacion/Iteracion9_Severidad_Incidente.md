# Severidad actual y máximo histórico — desarrollo local

> Esta guía registra el alcance de la Iteración 9. La [Iteración 10](Iteracion10_Ciclo_Vida_Cierre.md) incorpora después transiciones, resolución y cierre controlado.

Iteración 9, 23 de octubre de 2026, rama `feature/paso26-identidad-organizaciones`. Se preservan las iteraciones 7 y 8 locales; el último commit publicado sigue siendo `0c70669`.

## Decisiones adoptadas por el usuario

El usuario aprobó en esta conversación la matriz v1 como regla inicial del MVP y el tratamiento de incidentes anteriores mediante una reevaluación explícita, iniciando el máximo desde el primer cálculo confirmado. Se mantiene la decisión previa: si falta cualquier entrada requerida, Pending y severity=null, sin severidad provisional.

Los umbrales son política inicial de OpenIRP, sujetos a evaluación con casos reales; no se afirma calibración empírica ni una fórmula normativa de NIST. La [matriz y sus escenarios](Propuesta_Regla_Severidad_v1.md) permanecen como referencia funcional.

## Regla y entradas

`openirp-severity-v1` considera las seis entradas: confidencialidad, integridad, disponibilidad, criticidad evaluada, alcance y afectación operacional. Las entradas se toman de la revisión vigente del incidente dentro de la transacción autorizada; ninguna procede de niveles suministrados por el cliente.

La regla verifica enums antes de evaluar. Cualquier entrada null devuelve Pending/INCOMPLETE_INPUTS y severity=null. Con todas completas, M es el mayor impacto CIA:

| Orden | Resultado | Condición |
|---|---|---|
| R1 | Critical | Operación CriticalInterruption, o M=High junto con criticidad High o alcance OrganizationWide. |
| R2 | High | M=High, operación MajorInterruption, o M>=Medium junto con criticidad High o alcance OrganizationWide. |
| R3 | Medium | M>=Medium, operación Degraded, o M>=Low junto con criticidad High o alcance Multiple/OrganizationWide. |
| R4 | Low | Resto de entradas completas. |

NotApplicable es una evaluación explícita de criticidad, no equivale a Low y no satisface el predicado High. Tres impactos None no descartan automáticamente un incidente. Pending no representa prioridad baja: la evidencia conocida sigue visible; esta entrega no crea una cola o alerta de revisión urgente.

## Activación por incidente y contrato

Incidentes nuevos comienzan con regla v1, estado de clasificación Pending, severity=null y máximo null. El registro inicial contiene el snapshot del cálculo pendiente. No se requiere un motivo adicional al registro para ese primer estado.

Los incidentes anteriores conservan sus campos nuevos en null y classificationStatus=NotEvaluated. Ni lecturas, ni edición de título/asignación/capacidades, ni reevaluación de otras entradas activan silenciosamente su primera clasificación. Requieren:

PUT `/api/organizations/{org}/incidents/{incident}/severity`

```json
{
  "reason": "Primera clasificación revisada con la evidencia vigente",
  "expectedRevision": 4
}
```

Este endpoint también permite recalcular una clasificación ya activa. Exige motivo no blanco de hasta 2000 caracteres y revisión explícita; omitir revisión produce 400 y una revisión obsoleta, 409. El servidor decide severidad, máximo y versión. Los campos extra severity, maximumSeverityReached, severityRuleVersion o status no pueden imponer valores.

La primera clasificación puede quedar Pending si faltan datos, con máximo null. Cuando produce un nivel, ese es el primer máximo confirmado; no se calculan ni atribuyen severidades a eventos antiguos. La historia previa permanece intacta.

Detalle/listado e historial exponen los campos aditivos:

- severity: Low/Medium/High/Critical o null.
- maximumSeverityReached: máximo de clasificaciones confirmadas o null si todavía no existe uno.
- severityRuleVersion y severityAssessedAtRevision.
- classificationStatus, derivado en el detalle/listado: NotEvaluated, Pending o Classified.

Las banderas de completitud CIA/contexto/criticidad conservan su alcance individual. El estado de negocio continúa REGISTERED; calcular severidad no inicia respuesta, no resuelve ni cierra el incidente.

## Recalcular y conservar historia

Una vez activada la regla del incidente, los cambios en CIA, contexto operacional o criticidad recalculan con la misma revisión. Reportar, adjuntar o vincular activos invalida primero la criticidad; la severidad pasa a Pending y conserva el máximo anterior. No se reutiliza una criticidad que quedó obsoleta.

Título, descripción, categoría, asignación y capacidades conservan la clasificación y la referencia a su último cálculo. Sus eventos copian los valores vigentes; no simulan un nuevo cálculo. Una reevaluación explícita sí avanza severityAssessedAtRevision aun si el resultado coincide.

`maximumSeverityReached` nunca disminuye ni se borra al volver a Pending. El servidor calcula el máximo y PostgreSQL bloquea también intentos de rebajarlo o llevarlo a null mediante UPDATE, con SQLSTATE 23514. Los checks validan niveles, coherencia actual/máximo y metadatos de versión/revisión. Un propietario puede alterar objetos; no se promete protección criptográfica frente a él.

Cada cálculo conserva `details.classification`: las seis entradas observadas, referencia a la evaluación de criticidad, versión, regla coincidente, resultado, máximo anterior y nuevo máximo, y revisión. Se integra con los detalles existentes de activos o criticidad sin reemplazarlos. Todos los eventos contienen además las columnas tipadas de clasificación vigentes; la auditoría administrativa mantiene sus metadatos mínimos sin copiar motivos privados.

No se implementan cambios automáticos de versión. Una versión desconocida se rechaza con 409; una futura regla requiere adopción, pruebas, migración y reevaluación explícita, conservando los eventos pasados.

## Permisos y atomicidad

ORG_ADMIN y ANALYST activos del tenant pueden recalcular. VIEWER consulta; PLATFORM_ADMIN no recibe acceso implícito a incidentes de organizaciones. Se revalidan cuenta activa/no bloqueada, organización y membresía bajo el bloqueo transaccional común.

Entradas, clasificación, máximo, revisión, evento y SUCCESS de auditoría se confirman o revierten juntos. Dos escritores con la misma revisión no pueden confirmar ambos ni mezclar entradas. El rollback conserva tanto el máximo previo como la situación NotEvaluated en una primera adopción fallida.

El endpoint utiliza cookies y CSRF existentes, sin registro público, permisos globales ni acciones remotas. El LOGIN SQL limitado se verifica también para la clasificación y la protección del máximo. RLS y separación operativa completa de credenciales siguen pendientes.

## Migración y archivos

`20261023202856_IncidentSeverityClassification` añade ocho columnas nullable sin DEFAULT en incidents e incident_events, dos checks y una acción de evento. Crea la función/trigger de máximo monotónico en incidents. No rellena evaluaciones antiguas, no actualiza eventos ni deshabilita su protección. Down se niega si borraría clasificaciones o metadatos, incluso Pending.

Código en apps/api/:

- Domain/Severity/SeverityRuleV1.cs y Domain/Entities/IncidentSeverity.cs.
- Application/Incidents/IncidentSeverityContracts.cs e IIncidentService.cs.
- Domain/Entities/IncidentEntities.cs.
- Infrastructure/Incidents/IncidentSeverityService.cs e IncidentService.cs.
- Infrastructure/Persistence/Configurations/IncidentMapping.cs, migración/designer y snapshot.
- Api/Controllers/IncidentSeverityController.cs.
- IntegrationTests/SeverityRuleV1Tests.cs, IncidentSeverityTests.cs, LegacyIncidentFixture.cs y ampliación del caso runtime.
- Pruebas de migraciones anteriores adaptadas a fixtures previos a la activación y comprobaciones HTTP actualizadas al contrato aditivo.

## Verificación

- Release sin errores ni advertencias; EF sin diferencias de modelo.
- PostgreSQL 17: **314/314 correctas**, cero fallidas/omitidas. TRX: `artifacts/paso26-pg17-4d303a84e27640e5a61e924988ed69ba/paso26-pg17.trx`.
- PostgreSQL 18.3 nativo aislado: **314/314 correctas**, cero fallidas/omitidas. TRX: `artifacts/paso26-5f1c386fc6f64613a31e23d44a7abb0c/paso26.trx`. EF sin diferencias y clúster temporal detenido. El servicio PostgreSQL del sistema no se modificó.
- Son 55 casos nuevos sobre los 259 anteriores: 20 del evaluador de dominio (incluyen las 3072 combinaciones y 12800 comparaciones de monotonicidad) y 35 de integración PostgreSQL. La suite suma 314; las combinaciones internas no se suman de nuevo al total. Los 20 casos del evaluador también pasaron aisladamente en `artifacts/severity-rule-v1/severity-rule-v1.trx`.
- La integración cubre los cuatro niveles, Pending, máximo no decreciente, invalidación por activos, conservación en otras modificaciones, adopción explícita sin reescribir historia, roles/contextos inactivos/tenants, revisiones, concurrencia forzada, rollback de clasificación/máximo y primera adopción, checks/trigger, upgrade/downgrade y HTTP/CSRF. La prueba del LOGIN SQL limitado también clasifica y verifica que no puede rebajar el máximo.
- Migración aplicada únicamente a openirp_dev, PostgreSQL 17 habitual en 127.0.0.1:5433. El respaldo completo se restauró en un contenedor separado sin puertos ni volúmenes; conteos e historial coincidieron antes de aplicar. Las tablas de negocio/identidad/auditoría siguen vacías, con ocho capacidades y tres categorías; ninguna clasificación fabricada.
- Respaldo/SQL/conteos/restauración/esquema: `artifacts/incident-severity-dev-40016ca50f244c529f225141e8a5c342/`. Dump `openirp_dev-before-incident-severity.dump`, SHA256 `7D8C72F3C542F13202C42E7A685BDA343F12884BA450057FD8139E24F3610FBB`. Ocho columnas nullable sin DEFAULT, dos checks validados, acción nueva y triggers del máximo/historial habilitados.
- HTTPS real: health 200, OpenAPI 200 con PUT de severidad y escritura anónima 401. Evidencia: `artifacts/incident-severity-http-5757098c6bce4ad09fcc4e8f5973bc11/`. El proceso propio se detuvo; solo el cliente de comprobación aceptó el certificado local, sin modificar HTTPS/cookies Secure de la API.
- Sin nuevo commit/push/merge; las iteraciones 7–9 continúan locales después del commit publicado 0c70669.

## Siguiente subfase

Transiciones de negocio, resolución, descarte/reapertura y aprobaciones de cierre vinculadas a revisión, usando el máximo histórico confirmado. Las reglas de autoaprobación excepcional y de invalidación deben conservar la justificación y la auditoría acordadas.
