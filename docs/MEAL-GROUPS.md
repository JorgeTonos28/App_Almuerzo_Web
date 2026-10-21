# Grupos de almuerzo y estructura AD · v0.3.0

## Dos estructuras independientes

El catálogo Legacy `Departamentos` agrupaba colaboradores para gestionar, resumir y retirar comidas. No representaba exactamente el organigrama institucional. Convertirlo en departamento AD dividía esas agrupaciones y hacía incompatible la designación de sus responsables.

| Dato | Fuente | Quién lo modifica |
|---|---|---|
| Nombre, código, cargo y cuenta institucional | AD autenticado | Solo AD |
| Regional institucional y departamento AD | AD autenticado | Solo AD; TI reconcilia cambios de identidad |
| Alias público de regional | Almuerzo | ADMIN_GEN |
| Grupo de almuerzo, nombre y miembros | Almuerzo | ADMIN_GEN |
| Responsables y habilitación local | Almuerzo | ADMIN_GEN |

Cada persona pertenece a una regional y, cuando está habilitada, a un grupo de almuerzo de esa regional. Un grupo puede contener colaboradores de varios departamentos AD. La asignación del grupo no altera su departamento AD y nunca mezcla regionales. Los resúmenes, retiros, pedidos por encargo y permisos ADMIN_DEP se delimitan por el ID del grupo.

El nombre del grupo puede ser, por ejemplo, “Equipo de prueba”, aunque los departamentos AD de sus miembros tengan nombres distintos. El formulario muestra **Grupo de almuerzo** y **Departamento AD** por separado. Se retiró “Código AD anterior” del editor del grupo: ese campo era del contrato anterior por códigos, y NamesV1 solo aporta nombres.

## Incorporación y cambios

1. AD autentica y verifica el perfil de la misma cuenta.
2. Se registra regional/departamento institucional, con opt-in y sin inferir códigos ni fusionar nombres parecidos.
3. Una cuenta nueva se registra como USER/PENDIENTE sin grupo. Se encola un aviso a ADMIN_GEN una sola vez; trabajos/envíos conservan su configuración privada.
4. ADMIN_GEN revisa la regional, el departamento AD, asigna un grupo activo y habilita. El sistema encola la confirmación junto al cambio.
5. Los siguientes accesos conservan esa decisión. Reautenticarse no aprueba PENDIENTE, no reactiva INACTIVO y no retira una suspensión.

Las cuentas importadas conservan sus grupos y roles; no necesitan volver a incorporarse. En la misma regional, un cambio de departamento AD conserva la agrupación de almuerzos y sus responsables. ADMIN_GEN puede revisar y cambiar el grupo después. Un cambio a otra regional retira el grupo, ADMIN_DEP/ADMIN_REG y la suspensión de la regional anterior; se conserva INACTIVO y se requiere revisar/asignar el grupo de destino antes del acceso operativo.

## Responsables

ADMIN_GEN selecciona responsables activos del mismo **grupo**, sin suspensión. Su departamento AD puede ser diferente al de otros miembros. Un USER pasa a ADMIN_DEP; un ADMIN_GEN o ADMIN_REG conserva su rol y puede recibir además el resumen del grupo. Los responsables no se mueven de grupo al seleccionarlos. Para cambiarlo se usa Gestionar acceso, de forma expresa y auditada.

El responsable ADMIN_DEP gestiona los pedidos y consulta los históricos de su grupo. ADMIN_REG conserva la operación regional, sin editar grupos, membresías, responsables, identidades o aprobación de altas. Esas decisiones permanecen en ADMIN_GEN.

## Migración

`Departments` sigue representando grupos para preservar el contrato, claves e históricos de Legacy. Se añade `DirectoryDepartments` y `Users.DirectoryDepartmentId`. La migración copia las vinculaciones NamesV1 ya verificadas a ese catálogo; solo los usuarios verificados reciben ese vínculo inicial. No asigna por semejanza ni presume la identidad AD de quienes aún no autenticaron.

No se cambian IDs de grupos, afiliaciones, roles, pedidos ni preferencias. Los nombres de grupo sobrescritos anteriormente por AD pueden corregirse en GRUPOS DE ALMUERZO. Las migraciones de SQL Server y SQLite se aplican al iniciar. La transferencia de SQLite incluye el nuevo catálogo y conserva el origen.

El mapeo privado de **regionales** sigue siendo necesario para vincular ONA con `central` sin perder el contexto importado. Los antiguos mapeos `OrganizationMappings:*:Departments` ya no eligen grupos y no se consumen en NamesV1. No cargar las regionales de una captura como fuente verificada; su existencia y nombre se confirman al autenticar AD.

## Pruebas de aceptación

- Ingresar con dos colaboradores de departamentos AD distintos pero del mismo grupo: un responsable ve y gestiona ambos y recibe un resumen conjunto.
- Renombrar el grupo; verificar que el siguiente login no restaura el nombre AD.
- Seleccionar como responsable a un ADMIN_GEN del grupo; su rol permanece ADMIN_GEN y recibe el resumen de grupo.
- Ingresar con una cuenta nueva: queda pendiente; autenticarse de nuevo no la habilita ni duplica avisos.
- ADMIN_GEN asigna grupo y habilita: se permite el acceso, sin modificar la organización AD.
- Probar accesos de otro grupo/regional y manipulación de campos AD: se rechazan en el servidor.
- Probar traslado regional, cuentas INACTIVO y suspensiones, y comprobar que se conservan pedidos históricos.
