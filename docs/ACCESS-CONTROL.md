# Autoridad de datos y permisos · v0.2.1

El AD confirma identidad y organización. Almuerzo administra permisos y operación. Autenticarse en el directorio nunca concede un rol administrativo ni retira una suspensión local. La interfaz refleja los permisos; el servidor vuelve a verificarlos en cada petición.

## Matriz de responsabilidades

Todos los permisos operativos quedan limitados a la regional seleccionada por ADMIN_GEN, la regional propia de ADMIN_REG o el departamento propio de ADMIN_DEP. Un colaborador conserva sus pedidos, preferencias y valoraciones personales.

| Dato o acción | Autoridad / ADMIN_GEN | ADMIN_REG | ADMIN_DEP |
|---|---|---|---|
| Nombre, código de empleado y cuenta institucional | AD; solo consulta en formularios, incluso para ADMIN_GEN | Consulta | Consulta del departamento |
| Correo de la cuenta importada | Identificador conservado para vincular históricos; no se renombra en el panel | Consulta | Consulta del departamento |
| Regional y departamento verificados del colaborador | AD; corrección institucional, sin reasignación manual | Consulta | Consulta del departamento |
| Asignación de departamento mientras el contrato antiguo no lo devuelve | ADMIN_GEN, únicamente para cuentas sin organización verificada | No | No |
| Incorporación de cuentas | AD verificado o importación privada; sin creación manual de identidades | No | No |
| Habilitación general ACTIVO/PENDIENTE/INACTIVO | ADMIN_GEN | Consulta; no puede aprobar ni reactivar | Consulta |
| Roles y responsables ADMIN_DEP | ADMIN_GEN; responsables ya pertenecientes al departamento | Consulta | Consulta |
| Suspensión regional de USER/ADMIN_DEP | ADMIN_GEN o ADMIN_REG mediante operación específica | Suspender y retirar suspensión en su regional; sin afectar administradores generales/regionales | No |
| Catálogo de regionales y departamentos, mapeos AD y estados locales | ADMIN_GEN; metadatos verificados protegidos y códigos vinculados sin cambios improvisados | Consulta | Consulta del departamento |
| Nombre/código de un departamento ya verificado | Nombre sincronizado desde AD; código estable requiere reconciliación con TI | No | No |
| Platos, disponibilidad e importación semanal del menú | ADMIN_GEN | Sí, en su regional | No |
| Categorías, aliases y reglas de combinación/selección | ADMIN_GEN | Consulta para cargar menú | No |
| Calendario y suspensión de fechas | ADMIN_GEN | Sí, en su regional | No |
| Pedidos y cancelaciones administrativas | ADMIN_GEN | Sí, en su regional | Sí, en su departamento |
| Reportes y valoraciones de gestión | ADMIN_GEN | Consulta de su regional | Consulta de su departamento |
| Avisos publicados a colaboradores | ADMIN_GEN | Avisos de su regional | No |
| Proveedor, precios y períodos de valoración | ADMIN_GEN | Consulta | Consulta operativa |
| Horarios de cierre/recordatorio, margen de cierre y reglas generales | ADMIN_GEN | No | No |
| Destinatarios, modo/destino de prueba y envío manual de correos de prueba | ADMIN_GEN | No | No |
| Título, URL, remitente, favicon, footer y sello | ADMIN_GEN | No | No |
| Secretos AD/SendGrid/SQL y activación de procesos | TI, mediante configuración privada del servidor | No | No |

Los valores de configuración se conservan por regional para respetar cada contexto, pero su administración corresponde a ADMIN_GEN. No se concede a ADMIN_REG un editor genérico de claves. Su panel muestra AVISOS, menú, calendario, pedidos y consulta de departamentos, sin herramientas de identidad ni configuración institucional.

## Identidad y transición del endpoint

Nombre y código de empleado son de solo lectura para todos los administradores. Si un formulario intenta cambiarlos, se rechaza la operación; no se ignora el cambio silenciosamente. Se actualizan únicamente al autenticar un perfil verificado de AD. El correo histórico se conserva como identificador de la cuenta vinculada. Restablecer un vínculo sigue siendo una acción excepcional de ADMIN_GEN, auditada y con revocación de sesión; no permite editar la identidad por formulario.

Con el contrato actual, que todavía no devuelve organización, ADMIN_GEN puede asignar el departamento a cuentas sin organización verificada y aprobar solicitudes. Con `Ad:AutoProvisioningEnabled=true`, regional/departamento son exclusivamente los del perfil verificado. Una cuenta marcada `DirectoryManaged` conserva esa protección aunque la activación del proveedor se deshabilite temporalmente.

La sincronización institucional ocurre al iniciar sesión. Cada petición consulta los permisos, ámbito y bloqueos vigentes en la base local; no consulta continuamente el AD. Ante un traslado urgente se debe revocar/deshabilitar el acceso local y coordinar con TI una nueva autenticación y la habilitación correspondiente.

ADMIN_GEN prepara regionales y vincula los códigos departamentales existentes. Cuando un código ya está asociado a colaboradores verificados, no puede sustituirse desde el panel para forzar una reasignación. El nombre de un departamento existente se sincroniza desde el AD y se audita; colisiones con otro departamento se rechazan para reconciliación con TI. El nombre visible de una regional sigue siendo una etiqueta local administrada por ADMIN_GEN, pues el contrato preparado utiliza su código estable.

La autoridad del directorio queda marcada de forma persistente en regionales y departamentos. Que el último colaborador verificado sea trasladado no vuelve editables los metadatos. Las semillas reconocen las vinculaciones verificadas anteriores para conservar esta protección al actualizar.

Los departamentos verificados tampoco se eliminan desde el panel. ADMIN_GEN puede desactivarlos localmente; se conserva su identidad y su trazabilidad.

Asignar un responsable nunca cambia su departamento. Solo se admiten USER/ADMIN_DEP activos, sin suspensión y ya adscritos al departamento. Un traslado institucional retira ADMIN_DEP al cambiar de departamento y ADMIN_REG al cambiar de regional; los nuevos responsables se designan explícitamente.

## Dos controles de acceso independientes

El acceso efectivo exige simultáneamente:

1. Identidad AD válida y vínculo/código sin conflictos.
2. Estado general ACTIVO y rol válido en Almuerzo.
3. Regional y departamento activos y coincidentes.
4. Ausencia de suspensión regional (`RegionalBlocked=false`).

ADMIN_REG utiliza una operación limitada a `{email, blocked}`. No puede enviar nombre, departamento, código o rol en esa operación, bloquear a otro administrador regional/general, ni actuar fuera de su regional. Cambiar la suspensión genera auditoría explícita y revoca el acceso en la siguiente petición, incluyendo sesiones ya iniciadas.

Retirar una suspensión regional no cambia INACTIVO a ACTIVO ni aprueba una cuenta PENDIENTE. Habilitar el estado general tampoco retira una suspensión regional. ADMIN_GEN puede revisar ambos controles por separado. ADMIN_DEP no modifica identidades, roles ni estados de acceso.

Reautenticarse o publicar una solicitud de acceso manipulada no retira ninguno de los bloqueos. Una cuenta INACTIVO o suspendida no puede convertirse en PENDIENTE mediante ese formulario para beneficiarse después del alta automática AD. Los formularios de solicitud no se muestran en ese estado y la prohibición se comprueba también en el servidor.

Una transferencia **verificada** a otra regional retira únicamente la suspensión de la regional anterior, con auditoría. Una deshabilitación general se conserva. Dentro de la misma regional el AD no retira la suspensión.

## Históricos, comunicaciones y actualización

Suspender a un usuario no borra, cancela ni reescribe sus pedidos o valoraciones. Los pedidos existentes se revisan/cancelan mediante las operaciones habituales, preservando trazabilidad. Los responsables suspendidos dejan de recibir los resúmenes y se excluyen de los responsables efectivos y recordatorios. Los reportes históricos conservan el código del colaborador aunque haya sido trasladado de regional.

Las valoraciones identificadas para ADMIN_DEP no se autorizan solo por el nombre del departamento: las de comida se vinculan al ID del pedido histórico. Las valoraciones antiguas de proveedor no contienen ID departamental y requieren además pertenencia actual compatible; si el ámbito es ambiguo tras un cambio de nombre/adscripción, no se expone el comentario identificado al ADMIN_DEP. Los agregados comunitarios regionales permanecen disponibles.

La migración de ambos proveedores agrega `RegionalBlocked` con valor inicial false; los estados y permisos existentes no cambian. El arranque aplica la migración automáticamente. Los bloqueos existentes almacenados como INACTIVO se conservan como deshabilitaciones generales: no se reclasifican ni se retiran automáticamente.

La suite comprueba operaciones permitidas y rechazadas, manipulación de payloads, aislamiento regional, CSRF, protección de identidad incluso para ADMIN_GEN, responsables sin traslados implícitos, sesiones revocadas, persistencia de bloqueos frente al AD, solicitudes manipuladas, traslados verificados y conservación de históricos. Las pruebas de AD organizativo son simuladas hasta que TI entregue el contrato real.
