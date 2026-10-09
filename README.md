# App Almuerzo Web · 0.1.0

Migración de App_Almuerzo v7.36 a ASP.NET Core 10 MVC, EF Core y SQLite. La interfaz Vue conserva la estructura y los estilos del sistema de Apps Script. Los permisos y las reglas de negocio se verifican en el servidor.

Esta primera implementación permite probar los módulos con datos locales. **La integración real con AD requiere confirmar el contrato del endpoint**. No se ha conectado a AD, SMTP ni producción, y la paridad completa requiere una aceptación funcional con los responsables del sistema. El análisis detallado está en [docs/MIGRATION.md](docs/MIGRATION.md).

## Ejecutar y probar

Requisitos: .NET SDK 10 y Node.js 24. Desde la raíz del repositorio:

```powershell
npm ci
npm run build
dotnet restore Almuerzo.sln
dotnet build Almuerzo.sln --no-restore
dotnet test Almuerzo.sln --no-build
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/Almuerzo.Web --no-launch-profile --urls http://localhost:5062 -- --demo
```

Abrir `http://localhost:5062`. El modo demo usa una base independiente en `src/Almuerzo.Web/App_Data/demo`, un usuario sintético `demo.almuerzo@infotep.gob.do` y una contraseña aleatoria disponible **solo localmente** en `App_Data/demo/access.txt`. Nunca habilita usuarios de la base importada. El flag `--demo` se rechaza en producción. Los cambios a la plantilla, CSS o JavaScript requieren `npm run build`; editar `frontend/app.js`, `frontend/app.css` y `frontend/responsive.css`, no los archivos generados en `wwwroot/assets`. El script `port-legacy.mjs` fue usado para el bootstrap y protege los archivos existentes contra sobrescritura.

Para ejecutar con la base real, omitir `--demo` y configurar primero el proveedor institucional. El arranque aplica las migraciones y semillas idempotentes; no crea usuarios con contraseñas conocidas. Antes de una actualización de producción se debe respaldar la base.

## Importar el Excel de origen

El libro y sus datos son privados. No copiarlos al repositorio ni adjuntarlos a un PR. El importador lee las diez hojas, valida encabezados, conserva los registros de origen en un archivo lógico dentro de la base, redacta secretos de Config y reconcilia los conteos dentro de una transacción. Los JSON de preferencias y los históricos se preservan. Normaliza `USUARIO` a `USER`, códigos con ceros iniciales y fechas de Excel.

```powershell
./scripts/Import-Legacy.ps1 -WorkbookPath 'C:\Users\jtonos\Downloads\DB_Almuerzo.xlsx'
```

La base local predeterminada está en `src/Almuerzo.Web/App_Data/almuerzo.db`. Repetir el mismo archivo no vuelve a insertar datos: se reconoce por SHA-256. Un archivo diferente requiere una base nueva, sin usuarios, menú ni pedidos; no se fusionan históricos de forma silenciosa. Una referencia de departamento desconocida deja la cuenta sin departamento y sin acceso hasta su corrección administrativa. La importación fuerza modo de correo de prueba y elimina la URL antigua de Apps Script del uso operativo.

Para preparar una importación offline en el servidor, publicar el binario y ejecutar el mismo script con `-PublishedDirectory` y `-StoragePath`, usando una carpeta nueva fuera del directorio público. Detener la aplicación durante esa importación. El script no despliega, no envía datos ni configura credenciales.

## Identidad institucional y habilitación

El proveedor configurable es un adaptador provisional HTTPS/JSON con token Bearer. No asumir que este es el contrato de INFOTEP hasta verificar la URL, campos y respuesta. Se revisaron main y las ramas remotas disponibles de ExtraHub y no se encontró la integración indicada; ExtraHub usa autenticación local por cookies.

Configurar parámetros no secretos en el entorno del servidor. Para un contrato confirmado, los valores iniciales del adaptador son:

| Parámetro | Función / valor predeterminado |
|---|---|
| `Ad:ContractConfirmed` | `false`; habilitar solo después de verificar el contrato |
| `Ad:Endpoint` | URL HTTPS de autenticación |
| `Ad:UsernameField` / `Ad:PasswordField` | `username` / `password` en el cuerpo JSON |
| `Ad:SuccessField` | `success`, booleano |
| `Ad:EmailField` / `Ad:NameField` | `user.email` / `user.displayName`, rutas separadas por puntos |
| `Ad:Token` | secreto Bearer; nunca guardarlo en Git |

En desarrollo, usar User Secrets. [scripts/Set-AdToken.ps1](scripts/Set-AdToken.ps1) solicita el token oculto y lo escribe mediante stdin, sin incluirlo en la línea de comandos. En producción usar un gestor de secretos o variables de entorno (`Ad__Token`, `Ad__Endpoint`, etc.). La contraseña institucional se envía únicamente al endpoint configurado, no se persiste ni se registra.

La identidad AD no otorga acceso por sí sola: se requiere usuario ACTIVO, rol válido y departamento ACTIVO en esta base. Un colaborador nuevo puede solicitar acceso; se registra PENDIENTE, se encolan avisos a administradores generales y el administrador asigna rol y departamento. Las sesiones vuelven a comprobar el estado en cada petición. Un administrador departamental solo administra colaboradores USER de su departamento; la asignación de administradores corresponde al administrador general.

## Correo, reportes y procesos diarios

`Operations:Enabled` es `false` inicialmente. Al activarlo, un proceso cada 30 segundos genera el recordatorio y cierre diario según `America/Santo_Domingo`, con claves de ejecución que evitan duplicar la cola de cierre. La cola se confirma junto con cada cambio de negocio y procesa hasta cinco intentos por correo.

En Development, los mensajes se escriben en `App_Data/mail`; nunca se envían a direcciones reales. En producción configurar `Mail:Mode=Smtp`, `Host`, `Port` (587 por defecto), `From`, `Username` y el secreto `Password`. SMTP exige TLS. Mantener `TEST_EMAIL_MODE=TRUE` y definir un destino de prueba al validar. Quitar ese modo únicamente después de verificar destinatarios y permisos.

El cierre genera Excel y PDF consolidados y por departamento, y respaldo SQLite local. Conserva código, colaborador, departamento, platos en columnas por categoría y notas para cocina. El PDF distribuye categorías en grupos de cinco para mantener legibilidad. Los adjuntos y respaldos quedan fuera de `wwwroot`. El endpoint autenticado `/reports/daily/yyyy-MM-dd` permite descargar Excel, con scope departamental; agregar `?format=pdf` para PDF. El endpoint `/api/menu-day/yyyy-MM-dd` requiere `Authorization: Bearer ...` con el secreto `Integrations:MenuToken`; no acepta tokens en la URL. La plantilla y el sello externos de Google Drive no vienen incluidos en el repositorio original; su fidelidad visual exacta requiere proporcionar esos archivos.

## Seguridad y despliegue

Las reglas obligatorias están en [AGENTS.md](AGENTS.md). Incluyen autorización del servidor, scopes, CSRF, validación de menú, propiedad de pedidos, sesiones HttpOnly/SameSite, rate limiting, auditoría, CSP sin eval ni scripts inline libres, dependencias locales y prohibición de versionar datos/secretos. Estilos inline siguen permitidos para los componentes dinámicos heredados. Los mensajes de error no muestran stack traces al usuario.

Producción exige HTTPS y `AllowedHosts` con el dominio real. SQLite se usa en una única instancia con almacenamiento persistente; no colocar la base en una carpeta pública ni una unidad de red. Para escalar a múltiples instancias se necesita otro proveedor de base, un ejecutor de trabajos compartido y coordinación de la cola. El envío SMTP puede duplicarse si el proceso falla después de entregar el correo pero antes de confirmar el registro; la cola no promete entrega exactamente una vez.

Las nuevas claves de Data Protection se cifran con DPAPI en Windows bajo la cuenta del proceso. En otros sistemas, configurar `DataProtection:CertificatePath` y el secreto `CertificatePassword`; producción rechaza arrancar sin cifrado. Conservar claves, certificado y base de manera segura, con permisos solo para la cuenta de servicio y administradores autorizados. Los respaldos SQLite contienen datos personales y necesitan protección del volumen, retención y restauraciones verificadas.

```powershell
npm ci
npm run build
dotnet publish src/Almuerzo.Web -c Release -o artifacts/publish
```

Configurar `Storage__Path` fuera de la carpeta publicada, secretos, dominio y HTTPS antes de arrancar. Para IIS instalar el Hosting Bundle compatible y dar permisos a la cuenta del Application Pool. No publicar `App_Data`, libros, correos, respaldos, archivos de secretos ni credenciales demo. No hay publicación automática a GitHub ni al servidor.

Verificación disponible: `dotnet test Almuerzo.sln`, `npm audit` y `dotnet list Almuerzo.sln package --vulnerable --include-transitive`. Si el cliente TLS de npm falla por conectividad, `tools/audit-npm-registry.ps1` consulta el mismo registro oficial con el contenido del lockfile, sin desactivar validación de certificados. CI construye los recursos, ejecuta pruebas sintéticas y comprueba dependencias; no utiliza datos reales.
