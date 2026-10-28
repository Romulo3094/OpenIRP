# Iteración 32 — Verificación y diagnóstico sin purga

Fecha: 2026-10-27. Política aprobada: conservar retirados sin eliminación física ni purga automática; añadir verificación administrativa auditable y diagnóstico de objetos/temporales. Evidencias en [entrega previa a IA](Entrega_PreIA_Iteraciones28_32.md).

## Verificación de un archivo registrado

POST `.../incidents/{incident}/evidence/{evidence}/file/verification` recibe ExpectedFileRevision y Reason. Exige ORG_ADMIN local, permisos actuales y CSRF con cookie; no devuelve bytes. Comprueba el archivo privado mediante longitud y SHA-256 bajo el gate transaccional, y revalida la revisión antes de operar.

EvidenceFileVerification devuelve EvidenceId, Revision, State, PhysicalIntegrityVerified, DownloadAllowed, CheckedAt y FailureCode. PhysicalIntegrityVerified solo describe los bytes comprobados en ese instante; no garantiza su estado futuro, pertinencia ni ausencia de malware. MalwareScanned=false y TechnicalSufficiencyVerified=false. RetentionPolicy=RETAIN_NO_AUTOMATIC_PURGE.

Archivo ausente, hash/longitud incompatibles o ruta insegura producen estado QUARANTINED con el mecanismo e historial existentes. Los errores de acceso/almacenamiento operacional devuelven 503 y no fabrican una verificación aprobada. Una revisión obsoleta devuelve 409. Una verificación favorable no libera una cuarentena, no reactiva retiradas ni autoriza descargas si el estado no las permite. Puede inspeccionar un archivo retirado retenido sin volverlo accesible.

Se registran el resultado (evidence.file.verification_result), actor/correlación/motivo y la operación administrativa; cualquier cambio de metadatos FILE y su evento se confirma junto con la auditoría. Detectar cambios de metadatos puede invalidar la aceptación de riesgo; un diagnóstico favorable sin cambios de metadatos no equivale a otra aceptación de riesgo. No hay migración para este endpoint.

## Diagnóstico local de objetos y temporales

ci/Inspect-EvidenceStorage.ps1 es una herramienta offline del operador local. Requiere RootPath absoluto explícito y OutputPath absoluto nuevo fuera del almacén. Solo consulta el contenedor local openirp-postgres, openirp_dev en 127.0.0.1:5433; rechaza contexto Docker remoto. No es un endpoint global ni concede permisos de plataforma sobre datos de otras organizaciones.

Inspecciona únicamente objects y staging, sin recursión ni lectura de contenidos; detecta objetos/temporales registrados, no registrados, nombres inesperados, entradas inseguras y objetos AVAILABLE ausentes. Rechaza raíces/subdirectorios con reparse points, limita metadatos/entradas (10000 por defecto) y no emite éxito parcial si excede el límite. El informe contiene conteos; no exporta nombres originales, contenido, IDs de almacenamiento ni rutas de objetos. La salida usa CreateNew para impedir sobrescritura.

Ejemplo de operador, sustituyendo la raíz por la configuración privada real:

```powershell
./ci/Inspect-EvidenceStorage.ps1 -RootPath 'D:\AlmacenPrivado\OpenIRP' -OutputPath 'D:\Desarrollos\OpenIRP\artifacts\diagnostico-nuevo.json'
```

No borra, mueve, publica ni recupera objetos; un temporal sin registro no se considera automáticamente descartable. El diagnóstico refleja un instante y puede competir con cargas activas: revisar resultados en una ventana coordinada antes de cualquier acción manual. Sus conteos no certifican integridad ni antimalware. Las pruebas usan una raíz propia desechable conservada como evidencia, no la raíz de archivos del usuario.

Quedan deliberadamente fuera el borrado/purga, una política nueva de plazos y un motor antimalware o liberación de cuarentenas. La conservación vigente sigue aplicándose hasta aprobar políticas posteriores. Esto cumple el alcance de diagnóstico aprobado previo a IA; no se presenta como una política final de operación productiva.
