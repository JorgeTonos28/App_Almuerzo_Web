# Autoridad de datos y permisos · v0.3.0

AD confirma identidad y estructura institucional. Almuerzo administra acceso y **grupos de almuerzo**, que son las agrupaciones operativas de Legacy. Un grupo puede reunir colaboradores de varios departamentos AD de una misma regional. Véase [modelo y migración](MEAL-GROUPS.md).

## Matriz de responsabilidades

Todos los permisos se comprueban en el servidor en cada petición. ADMIN_GEN selecciona una regional; ADMIN_REG opera en la propia; ADMIN_DEP gestiona el grupo de almuerzo asignado.

| Dato o acción | ADMIN_GEN / autoridad | ADMIN_REG | ADMIN_DEP |
|---|---|---|---|
| Nombre, código, cargo y cuenta institucional | Solo AD; consulta incluso para ADMIN_GEN | Consulta | Consulta de su grupo |
| Correo importado | Se conserva para históricos; no se renombra en el panel | Consulta | Consulta de su grupo |
| Regional y departamento institucional AD | Solo AD verificado; cambios de identidad requieren TI | Consulta | Consulta de su grupo |
| Alias visible de regional | ADMIN_GEN; no se usa para autenticar o vincular | Consulta | Consulta |
| Grupos de almuerzo, nombres, estados y membresías | ADMIN_GEN | Consulta | Consulta del grupo propio |
| Responsables de grupo ADMIN_DEP | ADMIN_GEN; activos y pertenecientes al grupo | Consulta | Consulta |
| Alta PENDIENTE, habilitación general y roles | ADMIN_GEN; nunca automáticos desde AD | No aprueba ni reactiva | No |
| Suspensión regional de USER/ADMIN_DEP | ADMIN_GEN o ADMIN_REG, operación limitada | Sí en su regional, sin afectar pares/general | No |
| Menú e importación semanal | ADMIN_GEN | Sí en su regional | No |
| Calendario | ADMIN_GEN | Sí en su regional | No |
| Pedidos y cancelaciones administrativas | ADMIN_GEN | Sí en su regional | Sí en su grupo |
| Reportes y valoraciones identificadas | ADMIN_GEN | De su regional | De su grupo |
| Avisos a colaboradores | ADMIN_GEN | Sí en su regional | No |
| Proveedor, precio y períodos | ADMIN_GEN | Consulta | Consulta operativa |
| Categorías, reglas, horarios, configuración, correos y branding | ADMIN_GEN | No | No |
| Secretos y procesos AD/SQL/SendGrid | TI mediante configuración privada | No | No |

Asignar un responsable no cambia su grupo ni el departamento AD. USER pasa a ADMIN_DEP; ADMIN_GEN y ADMIN_REG conservan sus roles y pueden recibir también el resumen de su grupo cuando se seleccionan explícitamente. Los suspendidos/inactivos no reciben resúmenes ni ejercen el ámbito operativo.

## AD e incorporación

El contrato vigente NamesV1 devuelve nombres, no IDs organizativos estables. Se usa una clave del nombre normalizado por regional, sin interpretar prefijos, usar aliases ni fusionar por semejanza. Nombres nuevos se registran solo con opt-in a partir del perfil autenticado; los de una captura no constituyen un catálogo verificado.

Las regionales importadas requieren mapeo privado explícito para preservar su ID. Los grupos Legacy ya no se mapean a departamentos AD: ambos catálogos están separados. Los elementos privados antiguos `OrganizationMappings:*:Departments` no se consumen en NamesV1. La tabla `Departments` sigue almacenando grupos para conservar IDs, pedidos y contrato de interfaz; `DirectoryDepartments` contiene los departamentos AD.

Una cuenta nueva autenticada queda USER/PENDIENTE y sin grupo. ADMIN_GEN revisa el perfil, asigna un grupo activo de su regional y aprueba. Se encolan aviso inicial y confirmación de acceso en sus respectivas transacciones; repetir el login no genera avisos duplicados. Ningún cargo concede privilegios.

Las cuentas importadas conservan grupo, roles e históricos al verificar AD en la misma regional. Un cambio de departamento AD dentro de esa regional no divide el grupo ni retira ADMIN_DEP. Si la regional cambia, se retira el grupo y los roles ADMIN_DEP/ADMIN_REG, se exige nueva asignación y aprobación y se conserva el historial anterior. La deshabilitación general INACTIVO prevalece. El contrato anterior Codes queda como compatibilidad explícita con sus reglas previas; no se utiliza para el modelo vigente.

## Controles de acceso

Se exige identidad AD válida y vínculo/código sin conflictos; estado ACTIVO; regional, grupo y departamento AD vinculado activos y consistentes; rol válido y ausencia de suspensión regional. Una cuenta Legacy aún no autenticada no recibe una identidad AD presunta por pertenecer a un grupo.

Cada petición vuelve a consultar estado, ámbito y rol en la base; la sesión no mantiene privilegios antiguos. La consulta de AD ocurre durante el login. Un traslado urgente exige revocar/deshabilitar localmente y coordinar la nueva autenticación con TI.

Nombre/código/cargo/organización AD son de solo lectura: el servidor rechaza su manipulación en formularios. Restablecer una cuenta AD vinculada sigue siendo excepcional, exclusivo de ADMIN_GEN, auditado y con revocación de sesión; no cambia la identidad institucional manualmente.

La suspensión regional es independiente de ACTIVO/PENDIENTE/INACTIVO. ADMIN_REG solo envía `{email, blocked}`, no edita otros campos, no bloquea a ADMIN_GEN/ADMIN_REG ni opera fuera de su regional. Retirar suspensión no aprueba pendientes ni reactiva INACTIVO. Login y solicitud de acceso tampoco retiran bloqueos. Un traslado regional verificado retira únicamente la suspensión de la regional anterior con auditoría.

## Históricos y comunicaciones

Renombrar un grupo no cambia su ID ni los pedidos. Reasignar un colaborador no traslada sus pedidos históricos: ADMIN_DEP solo ve/cancela los pedidos almacenados para su grupo, también al usar pedidos por encargo. Los resúmenes agrupan por ese ID, reuniendo personas de departamentos AD distintos. Los códigos de empleado se conservan en reportes de regionales anteriores.

La exposición de valoraciones identificadas se limita mediante el ID de pedido histórico; los votos Legacy de proveedor sin ese ID exigen además pertenencia actual compatible. Una etiqueta repetida no autoriza datos ajenos. Los agregados comunitarios regionales siguen disponibles.

Las migraciones de ambos proveedores separan los datos NamesV1 ya verificados, sin cambiar miembros, roles, grupos, pedidos o preferencias. Las pruebas cubren incorporación pendiente, asignación, agrupación de varios departamentos, permisos negativos, responsables ADMIN_GEN, cambios AD, traslados regionales, históricos, login JSON con CSRF y actualización de una base previa.
