# Iteración 28 — Revisión técnica humana de evidencias

Fecha: 2026-10-27. Tramo previo a IA; verificación final y archivos editados en [entrega previa a IA](Entrega_PreIA_Iteraciones28_32.md).

## Operaciones

GET `.../incidents/{incident}/executions/{execution}/operational-reviews` devuelve OperationalReviewPage con ContextToken ops-v1, Items y NextRevision. Admite afterRevision>=0 y limit 1–100. POST `.../executions/{execution}/tasks/{task}/operational-reviews` registra una decisión explícita; Kind=Evidence identifica esta revisión.

Ambas operaciones exigen cuenta, organización y membresía activas y ORG_ADMIN local. GET es no-store; POST exige CSRF con autenticación cookie. PLATFORM_ADMIN sin membresía no obtiene acceso. El historial técnico puede contener información sensible y se conserva exclusivo de ORG_ADMIN, incluso para lectura.

Registrar exige EvidenceId de la evidencia ACTIVE seleccionada al completar esa tarea exacta; Method (hasta 2000), Assessment (hasta 4000), Reason (hasta 2000), Accepted explícito, SelfReview explícito, SelfReviewJustification si el actor es responsable del incidente o de la tarea, ExpectedIncidentRevision y ExpectedContext vigente. AlternativeProcedure debe ser null. Para aceptar FILE, la aplicación comprueba físicamente longitud/SHA-256 mediante el almacén privado; los guards SQL solo comprueban metadatos, nunca bytes.

La persona documenta cómo verificó pertinencia, alcance, resultado y limitaciones. Accepted no es un dictamen generado automáticamente. Rechazar es una decisión válida y sustituye una aceptación anterior para esa tarea/tipo. Un registro nuevo conserva los anteriores.

## Vigencia y transacción

ops-v1 se obtiene en SQL con canonización UTC y SHA-256. Reúne el contexto semántico del incidente, tareas, evidencias/metadatos FILE, capacidades, permisos y avisos de seguridad, incluidas fuentes derivadas. Excluye las propias revisiones operativas y su incremento de revisión; otra revisión no invalida la anterior por sí sola. Cambios materiales sí lo hacen, de manera conservadora para el incidente completo.

IsCurrent exige ser la última revisión de ese tipo para la tarea, contexto coincidente y revisor actualmente elegible. AcceptanceCurrent añade Accepted=true. Puede describir soporte histórico todavía coincidente después de cerrar; nunca autoriza ejecución, reapertura ni cierre. AuthorizesExecution=false y RequiresHumanReview=true.

La escritura incrementa la revisión del incidente; confirma revisión, evento OPERATIONAL_REVIEWED y auditoría juntos bajo el gate transaccional existente. Los guards SQL diferidos comprueban ámbitos, estado, rol, contexto y ambos historiales. UPDATE/DELETE/TRUNCATE se bloquean, también desde SQL; EF impide modificar/borrar el registro. La cuenta runtime recibe SELECT/INSERT, sin UPDATE/DELETE.

risk-v1 de cierre incluye los IDs de estas revisiones: una decisión técnica posterior invalida la aceptación previa de riesgo y exige reevaluarla. No se introdujo un requisito universal adicional de revisiones técnicas: todos los cierres conservan la aceptación ORG_ADMIN de Iteración 26 y HIGH/CRITICAL conserva su aprobación adicional. El informe de Iteración 27 mantiene TechnicalSufficiencyVerified=false; la nueva decisión humana no se convierte en certificación global.

Las decisiones se registran en incidentes no terminales; GET permite consultar los históricos. Una reapertura o una retirada posterior de evidencia puede invalidar vigencia sin reescribir la revisión anterior.

## Persistencia

Migración 20261027193143_OperationalReviews: app.operational_reviews, claves compuestas de ámbito, índices para historia/vigencia, restricciones de texto y declaración propia; tres triggers y dos funciones de protección, función operational_context y actualización de closure_risk_context. No se elimina historia en downgrade: se rechaza si ya hay revisiones.
