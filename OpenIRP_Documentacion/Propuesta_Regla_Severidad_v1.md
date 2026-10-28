# Propuesta de regla determinista de severidad — OpenIRP v1

Estado: **matriz v1 adoptada por el usuario para el MVP el 23 de octubre de 2026**. Su implementación versionada, activación y pruebas se documentan en la [Iteración 9](Iteracion9_Severidad_Incidente.md). Complementa §7.2 de la planificación. Los umbrales siguen sujetos a evaluación con casos reales, sin afirmar calibración empírica.

## Referencia y alcance

FIPS 199 trata la categorización de información/sistemas según impacto potencial sobre confidencialidad, integridad y disponibilidad. Su vocabulario de impacto no proporciona por sí solo la matriz de cuatro severidades de incidentes de OpenIRP. Esta propuesta de umbrales es una decisión propia del proyecto y requiere validación mediante escenarios de las pymes. [NIST: FIPS 199](https://csrc.nist.gov/pubs/fips/199/final).

El glosario NIST distingue efectos limitados, serios y severos/catastróficos sobre operaciones, activos o personas. La correspondencia interna Low/Medium/High y el uso adicional de None pertenecen a los contratos de OpenIRP; no se afirma equivalencia normativa completa. [NIST: impacto potencial](https://csrc.nist.gov/glossary/term/potential_impact).

## Entradas de la clasificación v1

- CIA: tres dimensiones explícitas None/Low/Medium/High; null sigue significando sin evaluar.
- Criticidad de los activos afectados: Low/Medium/High, o NotApplicable cuando el analista confirma justificadamente que no corresponde una criticidad de activo. Desconocida permanece null. Debe conservarse la evidencia y los UUID/revisiones del contexto consultado.
- Alcance: Single (un recurso/ámbito acotado), Multiple (varios recursos/usuarios o servicios) y OrganizationWide (afectación transversal confirmada). null indica desconocido. Son categorías del alcance observado, no una cuenta automática de filas de inventario.
- Afectación operacional: None (sin degradación identificada), Degraded (funciones degradadas pero sostenidas), MajorInterruption (interrupción de una función principal con contingencia disponible), CriticalInterruption (imposibilidad de sostener una función principal sin contingencia viable). null indica desconocido. No se fijan minutos ni porcentajes sin datos de una pyme piloto.

CIA está implementado en la Iteración 6; alcance y afectación operacional se incorporan en la [Iteración 7](Iteracion7_Contexto_Incidente.md), con contratos, persistencia y snapshots. La [criticidad evaluada de la Iteración 8](Iteracion8_Criticidad_Incidente.md) incorpora una decisión explícita separada del reporte original, justificación de reducciones e invalidación al cambiar los activos. Las entradas ya tienen contratos y persistencia; el cálculo se incorpora en la Iteración 9. No deben inferirse automáticamente de la categoría PHISHING/RANSOMWARE ni del número de asociaciones de activos.

Los snapshots originales de incident_assets son inmutables: una criticidad reportada desconocida no puede resolverse sobrescribiendo ese reporte. La clasificación futura deberá registrar una evaluación de criticidad justificada y versionada aparte de la observación original. Tampoco debe usar un valor menor que una criticidad High conocida sin documentar y conservar esa reevaluación. Las dependencias deben corresponder a una revisión coherente del incidente y su contexto.

## Valores desconocidos y prioridad de revisión

Decisión del usuario confirmada el 23 de octubre de 2026: si falta cualquier entrada requerida, la clasificación queda Pending y `severity=null`; nunca se sustituye null por None/Low ni por cero. Esto indica falta de evaluación suficiente. Pending no equivale a baja prioridad: una interrupción crítica conocida o una dimensión High debe resaltarse para revisión inmediata aunque falten otros datos. No se habilitará cierre con evaluación pendiente.

Se adopta Pending estricto: incluso con evidencia conocida High/Critical, no se asignará una severidad provisional mientras falte alguna entrada requerida. Esta decisión cierra la elección de política para datos desconocidos; los umbrales se adoptaron como política inicial del MVP y siguen sujetos a evaluación con casos reales. La cola/alerta de revisión urgente también es futura y no se presenta como implementada.

## Matriz adoptada para entradas completas

Sea M el mayor impacto CIA observado; orden None < Low < Medium < High. Se evalúan estas reglas en orden y se toma la primera que corresponda:

| Orden | Resultado | Condición |
|---|---|---|
| 1 | Critical | Afectación operacional CriticalInterruption, o M=High junto con criticidad High o alcance OrganizationWide. |
| 2 | High | M=High, o afectación MajorInterruption, o M>=Medium junto con criticidad High o alcance OrganizationWide. |
| 3 | Medium | M>=Medium, o afectación Degraded, o M>=Low junto con criticidad High o alcance Multiple/OrganizationWide. |
| 4 | Low | Resto de entradas completas. |

NotApplicable no satisface los predicados de criticidad High; no se transforma en una criticidad Low. Si todas las dimensiones son None, afectación operacional None y no hay condiciones de escalamiento, Low es el mínimo de la escala de incidentes; no implica descarte automático ni confirma ausencia de incidente. La decisión de descarte pertenece al ciclo de vida y requiere justificación.

El mayor nivel CIA evita que sumar dimensiones sin impacto diluya una dimensión High. Criticidad y alcance escalan mediante reglas explícitas; no se utiliza una suma ponderada sin calibración. Para entradas conocidas, subir una entrada en los órdenes CIA/criticidad/alcance/operación no puede reducir la clasificación. Ese comportamiento se verificó en el prototipo matemático aislado descrito más abajo; deberá mantenerse al implementar el motor y sus contratos.

## Casos de aceptación de la matriz v1

| CIA C/I/A | Criticidad | Alcance | Operación | Resultado base esperado |
|---|---|---|---|---|
| None/None/None | NotApplicable | Single | None | Low; no descartar automáticamente. |
| Low/None/None | Low | Single | None | Low. |
| Low/None/None | Low | Multiple | None | Medium. |
| Medium/None/None | Low | Single | None | Medium. |
| Medium/None/None | High | Single | None | High. |
| High/None/None | Low | Single | None | High. |
| High/None/None | High | Single | None | Critical. |
| High/None/None | Low | OrganizationWide | None | Critical. |
| None/None/None | Low | Single | MajorInterruption | High. |
| None/None/None | Low | Single | CriticalInterruption | Critical. |
| null/None/High | High | Single | None | Pending; revisión urgente, sin asumir gravedad baja. |
| High/None/None | null | Single | None | Pending; resolver criticidad sin reescribir el reporte original. |
| None/None/None | Low | null | None | Pending. |
| None/None/None | Low | Single | null | Pending. |

Los 14 escenarios se ejecutaron contra un prototipo matemático aislado de esta propuesta y coincidieron con sus expectativas. Eso no constituye validación empírica de los umbrales ni validación de un motor de la API.

## Verificación matemática aislada

Se enumeraron las **3072 combinaciones completas** de tres dimensiones CIA (4×4×4), cuatro contextos de criticidad (NotApplicable/Low/Medium/High), tres alcances y cuatro afectaciones operacionales. Todas tuvieron resultado dentro de Low/Medium/High/Critical. Se comprobaron **12800 incrementos adyacentes** de entradas ordenadas sin ninguna disminución de severidad. NotApplicable es categórico y no se trata como escalón hacia Low en esas comparaciones de criticidad.

También pasaron seis casos con una entrada desconocida por vez, que producen Pending en la propuesta base, y los 14 escenarios de la tabla. El prototipo no se carga desde la aplicación, no escribe la base y no forma parte del cálculo activo del producto.

Evidencia local ignorada por Git: `artifacts/severity-proposal-fa6ccb7aec6040c0b0a11ccabf19e88c/Verify-SeverityProposal.ps1` y `verification.json`. El resultado deja `activatedInApi=false`. La suite de integración CIA se contabiliza por separado; estos casos no incrementan su total.

## Máximo histórico, versionado y cierre

La clasificación implementada en la Iteración 9 conserva versión de regla, entradas, resultado, revisión y motivo del evento. `maximum_severity_reached` solo aumentará a partir de clasificaciones efectivamente calculadas; no se borrará si la evaluación actual vuelve a Pending. Cambiar la versión de regla requerirá una reevaluación explícita, no reescribir eventos pasados.

Incidentes con evidencia CIA previa a la activación necesitarán revisión documentada: esa historia existe, pero no se afirmará que una severidad fue calculada por una regla que entonces no estaba activa. El usuario aprobó iniciar el máximo histórico de incidentes anteriores desde su primera clasificación explícita y confirmada, sin atribuir severidades a eventos antiguos. Las bases de negocio locales siguen vacías en la evidencia de esta iteración.

El cierre de un incidente que alcanzó High/Critical mantendrá la aprobación ORG_ADMIN vinculada a la revisión y sus reglas de invalidación. La evaluación CIA por sí sola no habilita cierre, no cambia estados ni permite al cliente escribir severity/maximumSeverityReached. La autoaprobación excepcional y justificada sigue siendo una decisión distinta.

## Revisión pendiente

La matriz v1 y la inicialización explícita del máximo fueron adoptadas por el usuario para el MVP. La Iteración 9 verifica el evaluador, sus fronteras/monotonicidad, revisiones, snapshots y máximo histórico. Quedan la evaluación empírica con casos reales y las transiciones/aprobaciones de cierre con sus reglas de invalidación. El resultado activatedInApi=false del prototipo aislado es evidencia histórica de aquel ensayo; no describe el estado posterior de la API.
