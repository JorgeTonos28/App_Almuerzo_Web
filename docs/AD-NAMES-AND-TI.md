# AD NamesV1 y entrega a TI · 0.3.0

## Qué está preparado

El contrato recibido de TI es ASMX HTTP POST sobre `https://ona-dis-2.infotep.gov.do/InfotepADWebservice/ADService.asmx`: primero `Autenticacion` (`Token`, `NombreUsuario`, `Password`) y, únicamente si autentica, `DatosUsuarioAD` (`Token`, `NombreUsuario`). El adaptador lee el DataTable/diffgram con `NombreCompleto`, `CodigoEmpleado`, `Cargo`, `Departamento` y `Regional`. Cargo puede estar vacío; no concede permisos. Nombre, código y organización son obligatorios y pertenecen a la misma fila verificada. Se mantienen HTTPS, límites de tiempo/tamaño, rechazo de XML externo/DTD y protección de credenciales.

El servidor de pruebas comunicado por TI responde actualmente en HTTP. Para homologación local existe una excepción explícita y temporal: `scripts\\Configure.cmd ad-http-test` solo puede activar `http://ona-dis-2.infotep.gov.do/...` en `Development`, con confirmación interactiva y un token/cuenta revocable de pruebas. Las credenciales viajan sin cifrar y pueden quedar expuestas en la red; no usar cuentas productivas ni incluir esa configuración en despliegues. Al terminar, ejecutar `scripts\\Configure.cmd ad` y solicitar a TI HTTPS con cadena confiable.

AD y gestión de almuerzos usan catálogos separados. `DirectoryDepartments` conserva el nombre y clave institucional por regional; nunca se edita desde el panel. La tabla Legacy `Departments` conserva los **grupos de almuerzo**, con IDs, miembros y pedidos históricos. ADMIN_GEN cambia su nombre visible y responsables, y asigna colaboradores aunque sus departamentos AD sean diferentes. La regional conserva su alias independiente.

Las cuentas nuevas quedan PENDIENTE sin grupo hasta que ADMIN_GEN asigne uno y apruebe el acceso; su primer registro encola un aviso, sin repetirlo en cada login. Las importadas conservan grupo y rol al verificar AD en la misma regional. Cambiar de departamento AD no divide un grupo. Un traslado regional retira grupo y roles ADMIN_DEP/ADMIN_REG, mantiene los históricos y exige nueva asignación. INACTIVO y suspensiones no se retiran por reautenticación. Ver [modelo de grupos](MEAL-GROUPS.md).

**Límite del contrato:** no hay IDs estables de regional/departamento. Se usa SHA-256 del nombre normalizado (Unicode compuesto, mayúsculas y espacios compactados), preservando acentos y puntuación; el departamento se identifica dentro de su regional. No se interpreta el prefijo numérico del departamento como identificador. Un cambio sustancial de nombre llega como otra organización, sin fusión automática. Antes de renombrar organizaciones en AD, TI debe coordinar una reconciliación auditada o ampliar el endpoint con IDs estables. Un alias puede cambiar libremente sin afectar ese vínculo.

## 1. Mantener las pruebas locales

Las credenciales SQL entregadas permiten conectar a una base, si red/permisos lo permiten. No conceden acceso al sistema de archivos, IIS, escritorio remoto o publicación del servidor. No es necesario mover el entorno de desarrollo para continuar trabajando: se conserva LocalDB local y TI instala cada paquete nuevo en su servidor.

El token nuevo y la contraseña SQL real no se recibieron en el chat (`***` no es una contraseña utilizable). No se han reemplazado los secretos existentes ni conectado a la base institucional. No publicar esos valores en Git, capturas, argumentos de comandos o este chat.

La consulta HTTPS del WSDL desde el equipo de desarrollo falló con `SEC_E_UNTRUSTED_ROOT`. TI debe instalar en el almacén de confianza del equipo y de la cuenta de servicio la CA/intermedias correspondientes, y comprobar SAN/hostname/vigencia/cadena del certificado. No usar `curl -k`, callbacks que acepten certificados ni `TrustServerCertificate=true`. El formato se implementó contra la documentación/XML suministrados; autenticar una cuenta real sigue pendiente.

## 2. Vincular los catálogos importados antes de activar la sincronización

Este paso evita que el primer acceso traslade una cuenta importada a una regional vacía y deje sus históricos en otra. No se emparejan datos antiguos por alias ni por semejanza del nombre. El sistema rechaza el primer traslado de un catálogo importado no vinculado hasta resolver el mapeo.

Desde **CMD**, con la app detenida:

```cmd
cd /d C:\Dev\AppsWeb\App_Almuerzo
dotnet restore Almuerzo.sln
dotnet build Almuerzo.sln -c Release --no-restore
if not exist private mkdir private
set ASPNETCORE_ENVIRONMENT=Development
dotnet run --project src\Almuerzo.Web -c Release --no-build --no-launch-profile -- --directory-catalog C:\Dev\AppsWeb\App_Almuerzo\private\directory-catalog.json
notepad private\directory-catalog.json
scripts\Configure.cmd map
```

El comando de catálogo aplica migraciones y termina sin abrir la web ni enviar correos. El archivo privado contiene las regionales, los grupos de almuerzo y el catálogo AD por separado. El asistente `map` solo vincula el nombre exacto de la **regional** AD con su ID interno existente; para ONA la regional Legacy es `central`. No hay que mapear cada departamento AD a un grupo: esa mezcla era incorrecta. Los antiguos elementos `Departments` de los mapeos privados ya no se consumen en NamesV1 y se pueden retirar en la configuración privada.

Las regionales nuevas se registran cuando autentica el primer usuario, con opt-in; los nombres de directorios o capturas sirven para verificar con TI, no se cargan como organizaciones confirmadas. El sistema rechaza una primera vinculación de cuenta importada a una regional diferente y no vinculada.

Configuración privada equivalente para TI, con datos **sintéticos**:

```json
{ "Ad": { "OrganizationMappings": [ { "Name": "Regional AD de prueba", "Id": "central" } ] } }
```

No trasladar sin revisión el catálogo completo de desarrollo a producción; se usa la base actualizada de la fecha de despliegue. El primer ADMIN_GEN procede de la importación/autorización institucional existente, nunca de un cargo ni de un primer registro automático.

## 3. Introducir el token AD desde CMD

```cmd
scripts\Configure.cmd ad
```

El asistente usa entrada oculta y JSON por stdin de `dotnet user-secrets set`: no imprime el token ni lo pasa en argumentos. Actualiza únicamente claves AD/identidad de desarrollo y conserva la conexión SQL local, correos y mapeos existentes. User Secrets es almacenamiento local del usuario, no un vault cifrado de producción; proteger el perfil de Windows. Esc cancela la entrada.

Se configuran:

| Clave | Valor |
|---|---|
| `Ad:Endpoint` | Nuevo endpoint ASMX indicado arriba |
| `Ad:OrganizationContract` | `NamesV1` |
| `Ad:EmployeeCodeColumn` | `CodigoEmpleado` |
| `Ad:Enabled` | `true` |
| `Ad:AutoProvisioningEnabled` | `true` |
| `Ad:AutoCreateOrganizationEnabled` | `true` |
| `Ad:Token` | Entrada privada de TI |
| `DevelopmentIdentity:Enabled` | `false` |

Los valores versionados conservan AD/provisión/creación desactivados hasta configurar el opt-in. El adaptador `Codes` anterior se conserva para compatibilidad explícita, sin crear regionales desconocidas. Sus columnas de códigos no se usan en NamesV1.

Para probar localmente cuando estén resueltos certificado, token y mapeos:

```cmd
set ASPNETCORE_ENVIRONMENT=Development
dotnet run --project src\Almuerzo.Web -c Release --no-build --no-launch-profile --urls http://localhost:5062
```

Probar la cuenta ADMIN_GEN importada, un usuario nuevo y los cuatro roles. Luego configurar el alias regional y los nombres en GRUPOS DE ALMUERZO. En USUARIOS, asignar grupos y habilitar pendientes; en cada grupo, designar responsables del mismo grupo (pueden tener departamentos AD distintos). Verificar menú/calendario/reportes de cada ámbito. Probar INACTIVO, suspensión regional, perfil incompleto y organización inactiva. Detener con Ctrl+C antes de compilar/publicar. `--demo` sigue usando datos/identidad sintéticos y desactiva la provisión AD aunque esté activa en User Secrets.

## 4. Preparar la configuración separada para TI

Datos recibidos: servidor SQL `ona-dis-2.infotep.gov.do`, base `AlmuerzoBD`, usuario `almuerzo`. El hostname no es una dirección HTTP para SQL. TI debe confirmar instancia/puerto, acceso de red/VPN/firewall desde el host de la aplicación, certificado SQL válido y permisos de esa cuenta. HTTPS para AD y SQL/TDS son servicios distintos aunque compartan nombre de servidor.

```cmd
scripts\Configure.cmd ti
```

Si el acceso informa que el mapeo no existe en la base local, consulta el ID real del catálogo importado y vuelve a ejecutar `scripts\Configure.cmd map`. Cuando el nombre AD ya tenga otro ID guardado, el asistente exige escribir `RECONCILE_MAPPING` antes de reemplazarlo; esto evita corregir un vínculo de organización por accidente.

El asistente pide contraseña SQL, token AD y hostname HTTPS de la web; genera `private\ti\appsettings.Production.json` con acceso restringido al usuario actual. Usa un constructor de cadena de conexión para manejar contraseñas con caracteres especiales y fija `Encrypt=true;TrustServerCertificate=false;Persist Security Info=false`. No cambia User Secrets/local DB, no conecta al servidor y no sobrescribe un archivo privado anterior.

El archivo contiene secretos en texto: no versionarlo, incorporarlo al ZIP del código ni enviarlo por un canal público. TI debe trasladar los valores mediante su vault/variables de entorno o instalar el JSON privado junto al binario con ACL solo de la cuenta de servicio y administradores autorizados. Nunca en wwwroot. Ajustar `Storage:Path` y sus permisos, agregar `Ad:OrganizationMappings` y verificar el hostname de `AllowedHosts`. El usuario que ejecuta el asistente no es necesariamente la cuenta de IIS: TI debe asignar las ACL finales. Tras cargar los valores en el mecanismo definitivo, retirar copias temporales conforme a la política de TI.

El archivo nace con Pickup y procesos desactivados. SendGrid requiere API key Mail Send, remitente verificado y prueba controlada: seguir [la sección de correos](DEPLOYMENT-NATIONAL.md#7-habilitar-sendgrid-y-probar-correos). Esos datos no están contenidos en las credenciales SQL ni en el token AD.

## 5. Lo que ejecuta TI

1. Recibir el paquete publicado, pedir la **base actualizada inmediatamente antes del despliegue**, respaldar y programar el corte. El código/paquete no lleva datos reales.
2. Disponer del directorio de publicación, .NET 10 Hosting Bundle, cuenta/Application Pool IIS, DNS/binding HTTPS, almacenamiento privado y cadenas de confianza AD y SQL.
3. Instalar secretos/configuración privada en el contexto de la cuenta de servicio. La cuenta SQL debe poder aplicar las migraciones iniciales; coordinar privilegios y actualizaciones posteriores. Confirmar acceso sin desactivar certificados.
4. Importar una única fuente actualizada a SQL vacío mediante el procedimiento de [despliegue nacional](DEPLOYMENT-NATIONAL.md). No iniciar la web primero sobre un destino de transferencia vacío. Conservar origen, IDs y conteos.
5. Aplicar los mapeos de catálogos importados, iniciar una sola instancia y comprobar AD real, alias, bloqueos, traslados y aislamiento regional. El arranque aplica las migraciones de esta versión automáticamente.
6. Probar SendGrid únicamente con un buzón de prueba; verificar recepción y adjuntos. HTTP 202 solo confirma aceptación. Activar audiencia real/trabajos diarios cuando termine la homologación.
7. Cada actualización requiere publicar y reinstalar el paquete ASP.NET. Tu copia local puede seguir usando LocalDB y pruebas aisladas durante ese proceso.

No se ha comprobado autenticación AD real, conexión a AlmuerzoBD ni recepción real de SendGrid sin sus credenciales y confianza de certificados. Las pruebas automatizadas usan perfiles sintéticos, transporte simulado y bases temporales.
