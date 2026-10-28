# Iteración 31 — Evidencias RESTRICTED

Fecha: 2026-10-27. Política aprobada: acceso exclusivo de ORG_ADMIN activo de la propia organización. Evidencias finales en [entrega previa a IA](Entrega_PreIA_Iteraciones28_32.md).

RESTRICTED se habilita para TEXT, REFERENCE y FILE. Registro, detalle, historial y archivos requieren ORG_ADMIN local, además de cuenta/membresía/organización activas. PLATFORM_ADMIN no obtiene acceso implícito; ANALYST y VIEWER no acceden al contenido ni a los metadatos propios de la evidencia restringida.

La lista filtra RESTRICTED antes de paginar cuando el lector no es administrador. GET directo, eventos, metadatos FILE y descarga responden 404 para ocultar el recurso al lector sin acceso. Registrar RESTRICTED como ANALYST responde 403; VIEWER conserva su prohibición general de escritura. La descarga vuelve a validar permisos y estado después de verificar bytes y antes de entregar el stream.

Al completar una tarea se exige también acceso a la evidencia seleccionada; conocer su UUID no permite a un analista usarla como sustituto. evidence-readiness devuelve AccessRestricted al lector sin acceso y oculta su referencia seleccionada y los metadatos de evidencia/archivo. No abre el contenido para decidir ese estado.

Los historiales de ejecución/incidente conservan sus identificadores opacos y referencias de trazabilidad; no se modifica ni borra historia para ocultarlos. La política protege el contenido y sus endpoints, no promete ocultar toda existencia de una evidencia ni borrar UUID ya presentes en registros de negocio. No introducir contenido restringido en motivos de eventos generales. Las revisiones técnicas operativas tienen lectura exclusiva de ORG_ADMIN.

Retirar conserva metadatos mínimos y almacenamiento privado; no hay purga ni reclasificación automática. Los controles físicos de FILE, formato y límite de 10 MB se conservan. Nadie recibe autorización adicional para ejecutar una tarea por leer RESTRICTED.

Migración 20261027193519_RestrictedEvidence amplía ck_evidence_content y agrega un trigger que exige creador ORG_ADMIN local elegible para insertar RESTRICTED. La lectura entre organizaciones sigue autorizada por aplicación: RLS y separación operacional completa de credenciales permanecen pendientes. No se presenta una cuenta SQL compartida o propietaria como frontera suficiente frente a acceso SQL directo. Downgrade rechazado si existe evidencia restringida retenida.
