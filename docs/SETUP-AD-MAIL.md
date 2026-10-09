# Habilitar AD, correo y pruebas · v0.1.1

## 1. Preparar el proyecto

Abrir PowerShell en `C:\Dev\AppsWeb\App_Almuerzo`. Requisitos: .NET SDK 10, Node.js 24 y conexión al servicio institucional (red/VPN según corresponda).

```powershell
Set-Location C:\Dev\AppsWeb\App_Almuerzo
npm ci
npm run build
dotnet restore Almuerzo.sln
dotnet build Almuerzo.sln -c Release --no-restore
dotnet test Almuerzo.sln -c Release --no-build
```

La base del Excel entregado ya está importada localmente: 329 usuarios, 21 departamentos, 2,282 platos y 10,647 pedidos. No volver a copiarla al repositorio ni a `wwwroot`. El arranque aplica las migraciones y añade los nuevos ajustes sin sobrescribir los existentes. Los favicon y sellos cargados se guardan en esa base privada.

Para importar en otra instalación vacía, con la aplicación detenida:

```powershell
./scripts/Import-Legacy.ps1 -WorkbookPath 'C:\ruta\DB_Almuerzo.xlsx' -StoragePath 'C:\AlmuerzoPrivate'
```

Repetir el mismo libro es idempotente; un libro diferente no se mezcla con una base operativa. Para ejecutar desde binarios publicados, añadir `-PublishedDirectory 'C:\ruta\publicada'`.

## 2. Configurar Active Directory

El proveedor implementa el contrato de [ExtraHub PR #15](https://github.com/JorgeTonos28/ExtraHub/pull/15), commit `bff85a6f22b8cc2d98004d6ceb8fcd29080ecf6d`, contrastado con el WSDL del servicio el 9 de octubre de 2026:

- `POST /ADService.asmx/Autenticacion`: formulario `Token`, `NombreUsuario`, `Password`; respuesta XML booleana.
- Solo si la autenticación es positiva: `POST /ADService.asmx/DatosUsuarioAD`, formulario `Token`, `NombreUsuario`; respuesta XML/DataSet con nombre y código.
- HTTPS con certificados válidos; no se siguen redirecciones. Hay un plazo total de 15 segundos para ambas consultas, un límite de respuesta de 1 MB y lectura XML sin DTD ni entidades externas.

Guardar el token con entrada oculta. Este script no lo pone en argumentos del proceso ni lo imprime:

```powershell
./scripts/Set-AdToken.ps1
dotnet user-secrets set 'Ad:Enabled' 'true' --project src/Almuerzo.Web
dotnet user-secrets set 'DevelopmentIdentity:Enabled' 'false' --project src/Almuerzo.Web
dotnet user-secrets set 'Operations:Enabled' 'true' --project src/Almuerzo.Web
dotnet user-secrets set 'Operations:ScheduledJobsEnabled' 'false' --project src/Almuerzo.Web
dotnet user-secrets set 'Mail:Mode' 'Pickup' --project src/Almuerzo.Web
```

El endpoint predeterminado ya es `https://ona-app-01.infotep.gob.do:8005/ADService.asmx`. Si TI indica otros nombres de columna, configurar `Ad:FullNameColumn` y `Ad:EmployeeCodeColumn`; los aliases habituales y los nombres XML con acentos están soportados. `Ad:DirectoryDomain` vale `INFOTEP`; modificarlo solo si TI confirma otro nombre NetBIOS.

Se admite `usuario`, `usuario@infotep.gob.do` o `INFOTEP\usuario`. Se consulta el servicio con el nombre de cuenta normalizado. Puesto que el contrato devuelve nombre y código, **el correo local se deriva como `usuario@infotep.gob.do`**. No se supone que el servicio entregue un correo verificado. Si una cuenta importada usa un alias diferente, la administración debe revisar su correspondencia antes de habilitarla. Un correo/código conflictivo bloquea el login; no se unen cuentas ni se transfieren privilegios automáticamente.

El código AD debe contener entre uno y cuatro dígitos: se normaliza a cuatro, conservando ceros. El nombre y el código vienen del directorio. No se pide una contraseña propia de Almuerzo ni se almacena la contraseña AD.

## 3. Iniciar con la base real importada

Si otro proceso ocupa el puerto 5062, cerrarlo con `Ctrl+C` en su terminal o utilizar otro puerto. La demostración preparada para esta entrega usa 5064 y una base separada. Ejecutar **sin `--demo`**:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/Almuerzo.Web -c Release --no-build --no-launch-profile --urls http://localhost:5062
```

Abrir [Almuerzo local](http://localhost:5062). Ingresar con la cuenta institucional y contraseña habitual. El modo Development permite HTTP únicamente para estas pruebas en localhost. La instalación publicada debe usar HTTPS.

User Secrets se cargan en Development. Los cambios a claves de host/AD/SMTP requieren reiniciar el proceso. No ejecutar `dotnet user-secrets list` en capturas, logs o chats: mostraría los secretos. Si se definieron antes variables `Storage__Path` o `ConnectionStrings__DefaultConnection`, revisar que apunten a la base deseada; la predeterminada es `src/Almuerzo.Web/App_Data/almuerzo.db`.

La identidad validada entra al sistema únicamente con usuario local `ACTIVO`, departamento `ACTIVO` y rol válido (`USER`, `ADMIN_DEP` o `ADMIN_GEN`). Para administrar, usar una cuenta importada que ya tenga `ADMIN_GEN`. Una instalación nueva debe importar primero la base con sus administradores; no existe un usuario administrador universal ni una contraseña inicial compartida.

La opción `--demo` sigue disponible para la demostración sintética en una base separada; activa su identidad local y desactiva operaciones. No sirve para comprobar AD o envío de correo real.

## 4. Comprobar solicitudes y permisos

1. Con un administrador general: abrir **Panel de Administración → Usuarios** y comprobar su departamento y estado; revisar los departamentos activos en **DEPTOS**.
2. Abrir otra sesión privada e ingresar con una cuenta AD autorizada para pruebas que no esté habilitada localmente. Debe mostrar **Acceso denegado**, nombre y código verificados.
3. Elegir un departamento sugerido y pulsar **Solicitar acceso**. La cuenta debe quedar `PENDIENTE`, sin departamento asignado y sin acceso a pedidos. Repetir la solicitud no genera otra mientras esté pendiente.
4. En la sesión del administrador, recargar **USUARIOS**, editar la cuenta pendiente, asignar departamento, rol y estado `ACTIVO`, y guardar. El correo de aprobación queda encolado.
5. Recargar la sesión del colaborador: ya debe entrar. Deshabilitarlo temporalmente y comprobar que la siguiente petición vuelve a denegar acceso.
6. Probar un `ADMIN_DEP`: únicamente debe ver y administrar los colaboradores/pedidos de su departamento. La administración general asigna los administradores departamentales.

Las cuentas ya importadas y habilitadas conservan su rol y departamento. Autenticarse en AD no concede ningún rol por sí solo. La fila 329 del origen quedó sin departamento por una referencia inexistente; necesita revisión administrativa.

## 5. Probar correo sin SMTP

Con los comandos del paso 2, el worker procesa la cola cada 30 segundos y guarda correos como `.eml` en `src/Almuerzo.Web/App_Data/mail`. No se ejecutan recordatorios ni cierres automáticos porque `ScheduledJobsEnabled=false`.

Después de solicitar acceso, esperar hasta 30 segundos y abrir los `.eml` con un cliente de correo, o inspeccionarlos localmente. Deben existir avisos a los administradores generales y una confirmación al solicitante. También reciben solicitudes los destinatarios de `ADMIN_EMAILS`. Esos destinatarios adicionales no adquieren acceso administrativo.

En **CONFIG → Editar**, mantener `TEST_EMAIL_MODE=TRUE`, escribir **un único correo** en `TEST_EMAIL_DEST` y guardar. El modo de prueba redirige cada mensaje a ese buzón; si se deja vacío con Pickup, los `.eml` conservan los destinatarios originales sin enviar nada. En SMTP, un destino de prueba vacío retiene los mensajes en la cola.

Para probar reportes: **CONFIG → Enviar correos de prueba**. Esta acción encola el cierre del próximo día laborable, con Excel y PDF consolidados y por departamento; no ejecuta respaldos ni mantenimiento. Todos los destinatarios de esa acción son el buzón de prueba. Revisar destinatario, cuerpo y adjuntos en los `.eml`. Un administrador departamental debe estar habilitado y asignado para generar su reporte. En el cierre diario normal, el consolidado se dirige a administradores generales, `ADMIN_EMAILS` y responsables, y los reportes por departamento a sus administradores.

## 6. Probar entrega real por SMTP

Obtener de TI el host, puerto TLS/STARTTLS, remitente autorizado y cuenta/contraseña SMTP. El código usa `SmtpClient` con STARTTLS (normalmente puerto 587). Un servidor que exige TLS implícito en 465 u OAuth sin contraseña requiere otro transporte; no basta con cambiar el puerto.

Primero confirmar en CONFIG que `TEST_EMAIL_MODE=TRUE` y que `TEST_EMAIL_DEST` es tu buzón de pruebas. Detener la aplicación. Sustituir los valores de ejemplo:

```powershell
dotnet user-secrets set 'Mail:Host' 'smtp.ejemplo.local' --project src/Almuerzo.Web
dotnet user-secrets set 'Mail:Port' '587' --project src/Almuerzo.Web
dotnet user-secrets set 'Mail:From' 'almuerzo@infotep.gob.do' --project src/Almuerzo.Web
dotnet user-secrets set 'Mail:Username' 'cuenta-smtp' --project src/Almuerzo.Web
./scripts/Set-MailPassword.ps1
dotnet user-secrets set 'Mail:Mode' 'Smtp' --project src/Almuerzo.Web
dotnet user-secrets set 'Mail:AllowSmtpInDevelopment' 'true' --project src/Almuerzo.Web
```

Reiniciar con el comando del paso 3 y volver a pulsar **Enviar correos de prueba**, o generar una nueva solicitud de acceso con una cuenta de prueba. Esperar el ciclo de la cola y comprobar el buzón y los adjuntos. Los mensajes que ya se escribieron como `.eml` constan como entregados localmente; cambiar a SMTP no los reenvía. Los mensajes aún pendientes sí se procesan y se redirigen al destino de prueba vigente.

En Development, SMTP explícito exige modo de prueba; no envía a la audiencia real al desactivar ese modo. Sin opt-in, SMTP se sustituye por Pickup. No se desactiva la validación TLS. Si el envío falla se reintenta hasta cinco veces; los logs muestran ID de correo, intento y tipo de error, sin credenciales ni direcciones. Las filas con cinco intentos necesitan revisión operativa; no se reintentan indefinidamente.

Volver a `Mail:Mode=Pickup` y `Mail:AllowSmtpInDevelopment=false` cuando termine la prueba si se quiere continuar trabajando sin envíos.

## 7. Configurar favicon y pie de página

Como administrador general: **Panel de Administración → CONFIG → Identidad visual y pie de página**.

1. Editar el texto (hasta 200 caracteres; vacío para ocultarlo).
2. Cargar un favicon PNG, máximo 512 KB y 512 × 512 px. El predeterminado reproduce el logo azul con cubiertos de la app.
3. Cargar el sello PNG, máximo 2 MB y 2400 × 1200 px. Preferir fondo transparente; usar PNG estático sin entrelazado.
4. Pulsar **Guardar identidad visual**. El favicon y el pie se actualizan en la sesión; las demás sesiones reciben la configuración al recargar. También aparece en login y acceso denegado.
5. **Usar el logo predeterminado** restaura el favicon; **Quitar el sello** elimina la imagen del pie.

La administración departamental no puede cambiar estos ajustes. Las imágenes se validan por estructura, CRC, dimensiones y datos raster acotados; se elimina metadata adicional. Se aceptan PNG para evitar contenido SVG activo de terceros. Las imágenes reemplazadas se eliminan y sus URLs dejan de funcionar. El texto se muestra como texto, sin interpretar HTML.

## 8. Habilitar producción y tareas diarias

Publicar la versión probada (`npm run build` y `dotnet publish src/Almuerzo.Web -c Release -o artifacts/publish`) y configurar en el servidor:

- `ASPNETCORE_ENVIRONMENT=Production`, dominio real en `AllowedHosts`, HTTPS e instalación .NET/Hosting Bundle compatible.
- `Storage__Path` fuera de la carpeta pública, con permisos para la cuenta de servicio; importar el Excel con la aplicación detenida en una base vacía. No copiar la base de la demo a producción.
- `Ad__Enabled=true`, `Ad__Token` mediante gestor de secretos, endpoint y columnas según TI. `DevelopmentIdentity__Enabled=false`.
- `Mail__Mode=Smtp`, host/puerto/remitente/usuario y `Mail__Password` mediante gestor de secretos. Primero probar con `TEST_EMAIL_MODE=TRUE` y el buzón único configurado desde el panel.
- Configurar `APP_URL` con HTTPS, `ADMIN_EMAILS`, responsables, administradores por departamento y `MAIL_SENDER_NAME` en CONFIG. Confirmar `HORA_RECORDATORIO`, `HORA_ENVIO`, `MINUTOS_PREV_CIERRE` y calendario.
- Activar `Operations__Enabled=true` para la cola y, cuando se hayan validado horario/destinatarios, `Operations__ScheduledJobsEnabled=true` para recordatorios, cierre y respaldo. Se usa `America/Santo_Domingo`; habilitar las tareas después de su hora puede ejecutarlas enseguida ese día.
- Cuando la prueba de recepción y destinatarios esté aceptada, cambiar `TEST_EMAIL_MODE=FALSE` desde CONFIG para enviar a los destinatarios reales. Este cambio afecta los mensajes pendientes en ese momento.

User Secrets no se carga en Production. El token AD y la contraseña SMTP de desarrollo no se publican. Proteger también las claves Data Protection y los respaldos; en Windows las claves nuevas usan DPAPI bajo la identidad del proceso. La app debe permanecer ejecutándose para procesar la cola y los horarios (en IIS, configurar AlwaysRunning/preload según la instalación).

No se ha enviado correo institucional ni autenticado una cuenta real durante el desarrollo: faltan el token, credenciales de prueba y configuración SMTP privados. El WSDL respondió HTTP 200 y la suite usa respuestas ASMX sintéticas, comprobación de permisos y entrega Pickup. La validación institucional completa se realiza siguiendo estos pasos.
