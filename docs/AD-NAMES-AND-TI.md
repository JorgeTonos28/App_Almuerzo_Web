# AD NamesV1 y entrega a TI · 0.2.4

## Qué está preparado

El contrato recibido de TI es ASMX HTTP POST sobre `https://ona-dis-2.infotep.gov.do/InfotepADWebservice/ADService.asmx`: primero `Autenticacion` (`Token`, `NombreUsuario`, `Password`) y, únicamente si autentica, `DatosUsuarioAD` (`Token`, `NombreUsuario`). El adaptador lee el DataTable/diffgram con `NombreCompleto`, `CodigoEmpleado`, `Cargo`, `Departamento` y `Regional`. Cargo puede estar vacío; no concede permisos. Nombre, código y organización son obligatorios y pertenecen a la misma fila verificada. Se mantienen HTTPS, límites de tiempo/tamaño, rechazo de XML externo/DTD y protección de credenciales.

El servidor de pruebas comunicado por TI responde actualmente en HTTP. Para homologación local existe una excepción explícita y temporal: `scripts\\Configure.cmd ad-http-test` solo puede activar `http://ona-dis-2.infotep.gov.do/...` en `Development`, con confirmación interactiva y un token/cuenta revocable de pruebas. Las credenciales viajan sin cifrar y pueden quedar expuestas en la red; no usar cuentas productivas ni incluir esa configuración en despliegues. Al terminar, ejecutar `scripts\\Configure.cmd ad` y solicitar a TI HTTPS con cadena confiable.

El nombre de AD se conserva como dato institucional. `Alias` es una etiqueta independiente opcional, editable solo por ADMIN_GEN desde Regionales y Departamentos. La aplicación, los correos de resumen y los reportes muestran alias o, si está vacío, el nombre institucional. Las pantallas de edición conservan el nombre original visible y protegido. Cambiar un alias no cambia cuentas, IDs, pertenencia, pedidos ni autorización; el siguiente login tampoco lo borra. Los textos históricos importados se conservan; no se reescriben los comentarios/etiquetas antiguas para aparentar una nueva adscripción.

Con el opt-in activo, el primer colaborador verificado crea una regional/departamento inexistentes, catálogos/configuración regionales y su cuenta USER, en la misma transacción. Las organizaciones inactivas no se reactivan; INACTIVO conserva el bloqueo general. Los responsables ADMIN_DEP y administradores regionales se designan explícitamente. Los traslados verificados conservan históricos, retiran ADMIN_DEP si cambia departamento y ADMIN_REG si cambia regional, y no convierten la suspensión regional en habilitación general.

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

El comando de catálogo aplica migraciones locales y termina sin abrir la web ni enviar correos. El archivo privado contiene IDs/nombres de regionales/departamentos, sin usuarios ni credenciales. El asistente `map` pide el nombre exacto que llega de AD y el ID interno existente; permite añadir varios departamentos. Para el catálogo inicial, la regional interna es `central`. Solicitar a TI los nombres AD de los departamentos existentes; no inventarlos. Repetir `map` para otras regionales y departamentos conforme se verifiquen. No se necesita enumerar organizaciones que nunca existieron en la app: esas se crearán al ingresar su primer usuario.

Los mapeos quedan en User Secrets, no en el repositorio. Son una decisión administrativa de reconciliación: verificar cada equivalencia, especialmente si el catálogo importado tiene departamentos homónimos. No pueden apuntar a un departamento de otra regional, sustituir vínculos incompatibles ni resolver duplicados silenciosamente. El nombre institucional se asigna del perfil autenticado, no del formulario de mapeo. Los nombres nuevos no adoptan automáticamente como alias el nombre antiguo: ADMIN_GEN elige el alias después.

En la instalación de TI deben configurarse los mismos mapeos contra los IDs del destino importado, mediante configuración privada. Ejemplo **sintético** de forma anidada en el archivo privado de producción:

```json
{
  "Ad": {
    "OrganizationMappings": [
      { "Name": "Regional AD de prueba", "Id": "central", "Departments": [
        { "Name": "Departamento AD de prueba", "Id": "ID_INTERNO_EXISTENTE" }
      ] }
    ]
  }
}
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

Probar la cuenta ADMIN_GEN importada, un usuario nuevo y los cuatro roles. Luego configurar alias en Administración → Regionales y DEPTOS, responsables del mismo departamento y administradores regionales. Verificar menú/calendario/reportes de cada ámbito. Probar INACTIVO, suspensión regional, perfil incompleto y organización inactiva. Detener con Ctrl+C antes de compilar/publicar. `--demo` sigue usando datos/identidad sintéticos y desactiva la provisión AD aunque esté activa en User Secrets.

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
