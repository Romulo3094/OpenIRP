# Iteración 29 — Alternativas operativas verificadas por personas

Fecha: 2026-10-27. Comparte el almacenamiento, autorización, transacción y contexto de la [revisión técnica](Iteracion28_Revision_Tecnica_Evidencias.md). Evidencias finales en [entrega previa a IA](Entrega_PreIA_Iteraciones28_32.md).

Kind=Alternative en POST `.../executions/{execution}/tasks/{task}/operational-reviews` registra la verificación de un procedimiento alternativo para la tarea. AlternativeProcedure no vacío (hasta 4000), Method y Assessment describen procedimiento, comprobación realizada, resultado, limitaciones y riesgos; EvidenceId identifica soporte ACTIVE de esa tarea/ejecución/versión exactas. Accepted es explícito. No basta con escribir una alternativa en un campo de texto del playbook.

La tarea puede estar pendiente, bloqueada, en curso, completada u omitida; revisar la alternativa no cambia su estado. El procedimiento es manual y no ejecuta acciones sobre infraestructura externa. La aplicación no afirma que una descripción libre demuestre equivalencia técnica: registra la verificación y responsabilidad humanas con soporte, contexto y decisión.

La última aceptación/rechazo de ALTERNATIVE sustituye la anterior para esa tarea. EVIDENCE y ALTERNATIVE son dimensiones independientes. Los cambios de capacidades o del contexto de ejecución/evidencias/seguridad invalidan conservadoramente el registro; se requiere revisión nueva. El historial conserva siempre la evaluación anterior.

## Uso con dependencias y capacidades

La consulta de capacidades existente identifica requisitos declarados y limitaciones. La persona puede documentar el procedimiento alternativo verificado frente a ese contexto y citar el ID de validación al justificar su decisión operativa. La alternativa no borra ni altera requisitos publicados.

Una alternativa aceptada **no satisface por sí sola una arista del DAG**. Cuando el predecesor está SKIPPED, se conserva la solicitud y decisión ORG_ADMIN de excepción independiente por vínculo de Iteración 20, su contexto/participantes y posible revocación de Iteración 22. No se permite reemplazar una dependencia pendiente/en curso con una alternativa ni heredar una excepción de otra arista. Tampoco sustituye aprobaciones de pasos, evidencia exigida, continuidad de seguridad ni aceptación de riesgo al cierre.

Se implementa el registro de validación humana de la alternativa, no un motor de equivalencia o recomendación automática. La recomendación mediante IA queda fuera de este tramo. El contrato devuelve AuthorizesExecution=false para que el consumidor no confunda validación técnica con autorización de una operación.
