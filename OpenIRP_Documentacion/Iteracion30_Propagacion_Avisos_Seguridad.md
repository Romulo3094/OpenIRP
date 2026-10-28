# Iteración 30 — Avisos de seguridad en versiones derivadas

Fecha: 2026-10-27. Política aprobada por el usuario: propagación conservadora a todas las derivadas mediante procedencia, aviso genérico y decisión ORG_ADMIN independiente por ejecución. Evidencias en [entrega previa a IA](Entrega_PreIA_Iteraciones28_32.md).

## Resolución de procedencia

security_ancestors recorre recursivamente PlaybookVersion.SourceVersionId y Playbook.SourceVersionId. UNION elimina duplicados y evita recursión infinita ante ciclos históricos; el modelo ordinario conserva procedencia inmutable. Incluye la propia versión y alcanza clones de clones y nuevas versiones derivadas. La detección es dinámica: no depende de copiar alertas mediante un trabajo posterior ni modifica playbooks ajenos.

El aviso efectivo es el más reciente por OccurredAt, Revision e ID entre todos los ancestros. Una decisión para el aviso efectivo anterior caduca al llegar uno nuevo. La política es deliberadamente conservadora aunque una derivada haya cambiado mucho. No existe una operación implícita para ignorar la procedencia.

## Comportamiento y privacidad

- Las ejecuciones activas se bloquean hasta una decisión local de continuidad o cancelación para el aviso efectivo. Una decisión de otra ejecución no sirve.
- Se impiden nuevas ejecuciones de una versión con aviso propio o heredado, aunque conserve estado PUBLISHED. No se archivan ni reescriben automáticamente las copias ajenas; su estado editorial no equivale a autorización operacional.
- La continuidad no elimina evidencia, aprobaciones, omisiones, excepciones ni controles del DAG. SQL conserva los mismos bloqueos que la aplicación.
- El receptor ve IsInherited=true, el ID opaco del aviso y su fecha, la versión propia, revisión editorial de origen ocultada como 0, actor ocultado como Guid.Empty y una razón genérica. No recibe justificación privada, identidad del autor ni ID de la versión fuente por este contrato. Los identificadores opacos no otorgan acceso al origen.
- No se propaga la decisión de continuidad ni se ejecutan acciones externas. El rol de administrador de la fuente no concede permisos en la organización receptora.
- Las consultas históricas de ejecución mantienen el corte por FinishedAt. Las huellas de cierre/riesgo y revisión técnica consideran todos los avisos actuales de ancestros, incluso después de terminar la ejecución; una aceptación antigua puede perder vigencia sin cambiar la revisión del incidente.

No se habilita una publicación automática ni una limpieza de historia de alertas. La decisión local debe considerar el aviso heredado y las incertidumbres de no disponer del detalle privado.

## Persistencia y SQL

Migración 20261027193702_DerivedSecurityAlerts: la FK de ExecutionSecurityDecision apunta al ID del aviso, permitiendo un origen distinto de la versión ejecutada. El guard diferido exige que sea exactamente el aviso efectivo del árbol; no se puede apuntar a una alerta arbitraria. Se actualizan ejecución usable, decisiones, cancelación, huellas y bloqueo SQL de nuevos inicios.

No hay tablas de copias ni cambios en contenido privado de otras organizaciones. El downgrade solo admite contexto sin historia heredada afectada; rechaza perder decisiones o protección de clones con avisos existentes.
