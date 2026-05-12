# Azure DevOps Quotes Course

Proyecto práctico de Azure DevOps de cero a experto basado en una aplicación realista de citas, pensamientos y fotos. El objetivo es enseñar un flujo completo de desarrollo, despliegue, operación y troubleshooting con backend, frontend, Azure SQL, Azure App Service, Azure Blob Storage, Azure App Configuration, Application Insights y Azure DevOps.

Este documento aplica a los dos componentes del repositorio:

- `quotes-backend`: ASP.NET Core Minimal API en .NET 10.
- `quotes-frontend`: React + Vite + Node.js para servir el build en Azure App Service.

## Visión General

La solución está pensada para mostrar un ciclo de vida real de una aplicación moderna en Azure:

1. Desarrollo local.
2. Construcción de backend y frontend.
3. Configuración mediante variables de entorno y variable groups.
4. Publicación en Azure App Service Linux.
5. Creación del schema de base de datos de forma controlada.
6. Observabilidad con logs, Application Insights y trazas.
7. Configuración runtime del frontend y feature flags.
8. Diagnóstico con endpoints de salud y errores reproducibles.

La separación en tres pipelines independientes no es accidental:

- El backend se despliega por su cuenta porque contiene la API, la autenticación, la lógica de negocio y el acceso a datos.
- El schema se crea aparte para evitar que un despliegue de código tenga efectos destructivos o dependencias ocultas sobre la base de datos.
- El frontend se despliega de forma independiente porque es estático, tiene su propia cadena de empaquetado y solo necesita conocer la URL de la API en runtime.

## Arquitectura

```text
Usuario
  -> Azure App Service Linux (Frontend: React + Node server.mjs)
      -> /config.js inyecta API_BASE_URL en runtime
      -> llama al backend

Usuario
  -> Azure App Service Linux (Backend: ASP.NET Core Minimal API)
      -> JWT Authentication
      -> EF Core
      -> Azure SQL
      -> Azure Blob Storage para fotos
      -> Azure App Configuration opcional para configuración del frontend
      -> Application Insights y logs de App Service

Azure DevOps Pipelines
  -> despliegue backend
  -> creación de schema
  -> despliegue frontend
```

### Servicios de Azure utilizados

- Azure App Service Linux para backend y frontend.
- Azure SQL como única base de datos.
- Azure Blob Storage para fotos.
- Azure App Configuration opcional para configuración del frontend.
- Application Insights para telemetría, trazas y mapa de dependencias.
- Azure DevOps para pipelines, variable groups y operación del curso.

## Estructura Del Repositorio

```text
quotes-backend/
  Program.cs
  AzureQuotes.Api.csproj
  Contracts/
  Data/
  Models/
  Services/
  azure-pipelines-backend.yml
  azure-pipelines-create-schema.yml

quotes-frontend/
  package.json
  package-lock.json
  server.mjs
  index.html
  src/
  azure-pipelines-frontend.yml
```

## Backend

### Stack técnico

- ASP.NET Core Minimal API.
- .NET 10.
- Entity Framework Core.
- Azure SQL.
- JWT Authentication.
- Swagger / OpenAPI.
- Azure Blob Storage para fotos.
- Azure App Configuration opcional para feature flags.
- Application Insights y logging estructurado.
- Despliegue en Azure App Service Linux.

### Qué resuelve

- Registro de usuarios.
- Inicio de sesión y generación de JWT.
- Creación, edición, eliminación y reacción de quotes.
- Feed público y feed privado del usuario autenticado.
- Subida de fotos a Blob Storage.
- Health checks para operación y troubleshooting.
- Creación controlada del schema de Azure SQL con un endpoint administrativo.

## Frontend

### Stack técnico

- React.
- Vite.
- JavaScript.
- CSS.
- Node.js con `server.mjs` para servir el build en Azure App Service.

### Qué resuelve

- Login y registro.
- Interacción con el backend.
- Carga del feed público.
- Carga de pensamientos propios.
- Likes y subida de fotos.
- Inyección en runtime de `API_BASE_URL` y feature flags desde `/config.js`.

El frontend es la fuente de verdad para los feature flags de interfaz y comportamiento visible. El backend ya no expone ni evalúa flags.

## Endpoints Del Backend

### Públicos

| Método | Ruta | Uso |
|---|---|---|
| GET | `/` | Estado básico de la API |
| GET | `/health` | Salud general |
| GET | `/health/db` | Verificación de conexión a Azure SQL |
| GET | `/apispec.json` | Redirección al OpenAPI |
| POST | `/api/auth/register` | Registro de usuario |
| POST | `/api/auth/login` | Inicio de sesión |
| GET | `/api/quotes?scope=feed` | Feed público |

### Protegidos

| Método | Ruta | Uso |
|---|---|---|
| GET | `/api/me` | Perfil del usuario autenticado |
| GET | `/api/quotes?scope=mine` | Quotes propios |
| POST | `/api/quotes` | Crear quote |
| PUT | `/api/quotes/{quoteId}` | Editar quote |
| DELETE | `/api/quotes/{quoteId}` | Borrar quote |
| POST | `/api/quotes/{quoteId}/like` | Dar like |
| DELETE | `/api/quotes/{quoteId}/like` | Quitar like |

### Administrativo

| Método | Ruta | Uso |
|---|---|---|
| POST | `/api/admin/database/ensure-created` | Crea el schema con `EnsureCreatedAsync()` usando `X-Setup-Key` |

## Base De Datos

Este curso usa Azure SQL también en local. No usa SQLite.

### Tablas esperadas

- `dbo.Users`
- `dbo.Quotes`
- `dbo.QuoteLikes`

### Nota importante sobre schema

El proyecto no está planteado alrededor de migraciones EF Core para este flujo de curso. En su lugar, el schema se crea de manera controlada con el pipeline manual de schema o con el endpoint administrativo protegido.

## Variables De Entorno Del Backend

### Variables principales

| Variable | Uso |
|---|---|
| `DOTNET_ENVIRONMENT=Production` | Entorno de ejecución |
| `ASPNETCORE_URLS=http://0.0.0.0:8080` | Binding del contenedor |
| `WEBSITES_PORT=8080` | Puerto esperado por App Service |
| `WEBSITES_CONTAINER_START_TIME_LIMIT=600` | Tiempo de arranque |
| `WEBSITE_WARMUP_PATH=/health` | Health path |
| `ENVIRONMENT=production` | Marca de entorno |
| `FRONTEND_BASE_URL=https://<frontend-app>.azurewebsites.net,https://<backend-app>.azurewebsites.net` | CORS |
| `BACKEND_BASE_URL=https://<backend-app>.azurewebsites.net` | URL pública del backend |
| `JWT_SECRET_KEY=<clave larga>` | Firma JWT |
| `ADMIN_SETUP_KEY=<clave secreta>` | Protege el endpoint de schema |
| `AZURE_SQL_CONNECTION_STRING=<connection string>` | Azure SQL |
| `PHOTO_STORAGE_BACKEND=azure` | Backend de imágenes |
| `AZURE_STORAGE_CONNECTION_STRING=<connection string>` | Azure Blob Storage |
| `AZURE_STORAGE_CONTAINER_NAME=quote-photos` | Contenedor |
| `MAX_PHOTO_MB=4` | Límite de fotos |
| `LOG_LEVEL=Information` | Nivel de logs |
| `ENABLE_ORYX_BUILD=false` | Evita build en Azure |
| `SCM_DO_BUILD_DURING_DEPLOYMENT=false` | Evita build remoto |

### Variables recomendadas para observabilidad

- `APPLICATIONINSIGHTS_CONNECTION_STRING`.
- `APPINSIGHTS_INSTRUMENTATIONKEY` si todavía usas el modelo clásico.

## Variables De Entorno Del Frontend

| Variable | Uso |
|---|---|
| `API_BASE_URL=https://<backend-app>.azurewebsites.net` | URL de la API |
| `WEBSITES_PORT=8080` | Puerto del contenedor |
| `ENABLE_ORYX_BUILD=false` | Evita build en Azure |
| `SCM_DO_BUILD_DURING_DEPLOYMENT=false` | Evita build remoto |

Los feature flags de UI se documentan en [quotes-frontend/README.md](../quotes-frontend/README.md).

## Azure DevOps Variable Groups

### `vars-backend`

| Variable | Uso |
|---|---|
| `azureServiceConnection` | Service connection de Azure |
| `resourceGroupName` | Resource group |
| `webAppName` | App Service del backend |
| `backendBaseUrl` | URL pública del backend |
| `frontendBaseUrl` | URLs permitidas en CORS |
| `environmentName` | Nombre del entorno |
| `photoStorageBackend` | `azure` o `local` |
| `storageContainerName` | Contenedor de Blob Storage |
| `maxPhotoMb` | Tamaño máximo |
| `logLevel` | Nivel de logs |
| `JWT_SECRET_KEY` | JWT |
| `ADMIN_SETUP_KEY` | Endpoint de schema |
| `AZURE_SQL_CONNECTION_STRING` | Azure SQL |
| `AZURE_STORAGE_CONNECTION_STRING` | Blob Storage |
| `AZURE_APP_CONFIG_CONNECTION_STRING` | Opcional para App Configuration |

### `vars-frontend`

| Variable | Uso |
|---|---|
| `azureServiceConnection` | Service connection de Azure |
| `resourceGroupName` | Resource group |
| `webAppName` | App Service del frontend |
| `environmentName` | Nombre del entorno |
| `apiBaseUrl` | URL de la API |
| `featurePublicFeedEnabled` | Flag del feed para la UI |
| `featurePhotoUploadEnabled` | Flag de fotos para la UI |
| `featureMaintenanceModeEnabled` | Flag de mantenimiento para la UI |
| `nodeVersion` | Versión de Node.js |

## Pipelines

### 1. `azure-pipelines-backend.yml`

Este pipeline:

1. Restaura dependencias.
2. Compila el backend.
3. Publica el output.
4. Empaqueta en ZIP.
5. Despliega en Azure App Service Linux.
6. Configura App Settings.
7. Reinicia el App Service.
8. Valida `GET /health`.
9. Opcionalmente valida `GET /health/db`.

### 2. `azure-pipelines-create-schema.yml`

Este pipeline es manual y no despliega código.

1. Valida que el backend responda en `GET /health`.
2. Valida conexión a Azure SQL con `GET /health/db`.
3. Llama `POST /api/admin/database/ensure-created`.
4. Envía `X-Setup-Key` usando `ADMIN_SETUP_KEY`.
5. Crea las tablas `Users`, `Quotes` y `QuoteLikes`.
6. Valida `GET /api/quotes?scope=feed`.

### 3. `azure-pipelines-frontend.yml`

Este pipeline:

1. Instala Node.js 20.
2. Ejecuta `npm ci` o `npm install`.
3. Ejecuta `npm run build`.
4. Empaqueta `dist`, `package.json`, `package-lock.json` y `server.mjs`.
5. Despliega en Azure App Service Linux.
6. Configura `API_BASE_URL`.
7. Reinicia el App Service frontend.

## Creación de Recursos en Azure desde Cero

Esta sección es **para principiantes** que nunca han usado Azure. Se explica cada concepto antes de usarlo.

### Conceptos Previos Explicados

#### ¿Qué es un Connection String?

Un **connection string** es una texto que contiene la información necesaria para conectarse a una base de datos. Es como una "dirección + contraseña" combinada en un solo formato.

**Ejemplo:**
```
Server=tcp:myserver.database.windows.net,1433;Initial Catalog=mydb;Persist Security Info=False;User ID=admin;Password=P@ssw0rd123;Encrypt=True;Connection Timeout=30;
```

En este ejemplo:
- `myserver.database.windows.net` = dónde está la base de datos
- `mydb` = nombre de la base de datos
- `admin` = usuario
- `P@ssw0rd123` = contraseña

#### ¿Qué es Application Insights?

**Application Insights** es el "guardabosques" de tu aplicación. Recoge:
- Qué hace tu aplicación (requests HTTP)
- Cuántos errores hay
- Qué tan rápido responde
- Dónde están los problemas

### Paso 1: Crear Grupo De Recursos

Un **grupo de recursos** es una carpeta en Azure donde pones todos tus recursos (base de datos, app service, storage, etc.).

1. Ve a https://portal.azure.com
2. Click en "Resource groups" (o busca "Resource groups" arriba)
3. Click en "+ Create"
4. Rellena:
   - **Resource group name**: `rg-livedomain-prod` (o un nombre que prefieras)
   - **Region**: `East US` o la más cercana a ti
5. Click en "Review + create" → "Create"
6. Espera a que diga "Deployment succeeded"

### Paso 2: Crear Azure SQL Database

La base de datos donde se guardan usuarios, quotes y likes.

#### 2.1 Crear SQL Server

1. En el portal, busca "SQL servers" y click
2. Click en "+ Create"
3. Rellena:
   - **Resource group**: selecciona `rg-livedomain-prod`
   - **Server name**: `sql-livedomain-prod` (debe ser único globalmente)
   - **Location**: misma región que el resource group (ej. East US)
   - **Administrator login**: `sqladmin` (usuario de administrador)
   - **Password**: genera una contraseña fuerte, ej: `P@ssw0rd123!Azure2024` (guárdala en un lugar seguro)
   - Confirma la contraseña

4. Click en "Review + create" → "Create"
5. Espera a que se complete

#### 2.2 Crear Azure SQL Database

1. Cuando termine, click en "Go to resource"
2. En el lado izquierdo, busca "Databases" o click en "+ New database"
3. Rellena:
   - **Database name**: `quotes-db`
   - **Compute + storage**: "Basic" está bien para desarrollo (es más barato)
4. Click en "Create"
5. Espera a que se complete

#### 2.3 Abrir Firewall Para Desarrollo Local

Para que tu computadora local se conecte:

1. En SQL Server, ve a "Firewalls and virtual networks"
2. Click en "+ Add your client IP address"
3. Verás tu IP agregada automáticamente
4. Click en "Save"

#### 2.4 Obtener Connection String

1. Ve a SQL Database ("quotes-db")
2. Click en "Connection strings"
3. Copia la que dice "ADO.NET" (no la ODBC ni JDBC)
4. Reemplaza:
   - `{your_username}` → `sqladmin`
   - `{your_password}` → la contraseña que creaste (ej: `P@ssw0rd123!Azure2024`)

Debe verse así:
```
Server=tcp:sql-livedomain-prod.database.windows.net,1433;Initial Catalog=quotes-db;Persist Security Info=False;User ID=sqladmin;Password=P@ssw0rd123!Azure2024;Encrypt=True;Connection Timeout=30;
```

**Guárdalo**, lo necesitarás después.

### Paso 3: Crear Azure Storage Account (Blob Storage)

Donde se guardan las fotos.

1. Busca "Storage accounts" en el portal
2. Click en "+ Create"
3. Rellena:
   - **Resource group**: `rg-livedomain-prod`
   - **Storage account name**: `salivedomain` (solo letras, números, y debe ser único)
   - **Region**: misma región que los otros
   - **Performance**: "Standard"
   - **Redundancy**: "Locally-redundant storage (LRS)" está bien
4. Click en "Review + create" → "Create"

#### 3.1 Crear Contenedor

1. Cuando termine, click en "Go to resource"
2. A la izquierda, click en "Containers"
3. Click en "+ Container"
4. Rellena:
   - **Name**: `photos` (en minúsculas)
   - **Public access level**: "Private"
5. Click en "Create"

#### 3.2 Obtener Connection String

1. A la izquierda, click en "Access keys"
2. Bajo "Storage account name", copia "Storage account name"
3. Bajo "Key 1", copia la **"Connection string"** (larga, empieza con `DefaultEndpointsProtocol=...`)

**Guárdalo**.

### Paso 4: Crear Azure App Configuration

Donde se guardan opciones de configuración runtime para el frontend.

1. Busca "App Configuration" en el portal
2. Click en "+ Create"
3. Rellena:
   - **Resource group**: `rg-livedomain-prod`
   - **Name**: `appcfg-livedomain-prod`
   - **Region**: misma región
4. Click en "Review + create" → "Create"

#### 4.1 Crear Connection String De App Configuration

1. Cuando termine, click en "Go to resource"
2. A la izquierda, click en "Access keys"
3. Bajo "Primary key", copia "Connection String"

**Guárdalo**.

#### 4.2 Agregar Feature Flags Del Frontend (Opcional)

Si vas a usar App Configuration para el frontend, define aquí las claves que consume `quotes-frontend/server.mjs`.
Si todavía no lo necesitas, puedes saltarte este paso y usar variables de entorno del App Service del frontend.

### Paso 5: Crear Application Insights

Para ver logs, errores y performance.

1. Busca "Application Insights" en el portal
2. Click en "+ Create"
3. Rellena:
   - **Name**: `appins-livedomain-prod`
   - **Resource group**: `rg-livedomain-prod`
   - **Region**: misma región
   - **Resource Mode**: "Workspace-based" está bien
4. Click en "Review + create" → "Create"

#### 5.1 Obtener Instrumentation Key

1. Cuando termine, click en "Go to resource"
2. A la izquierda, click en "Overview"
3. Copia el valor de "Instrumentation Key" (es un GUID, ej: `12345678-1234-1234-1234-123456789012`)

**Guárdalo**.

### Paso 6: Crear Azure App Service (Backend)

Donde va a vivir la API.

1. Busca "App Services" en el portal
2. Click en "+ Create"
3. Click en "Web App"
4. Rellena:
   - **Resource group**: `rg-livedomain-prod`
   - **Name**: `app-backend-livedomain-prod` (debe ser único, será tu URL)
   - **Runtime stack**: ".NET 10"
   - **Operating System**: "Linux"
   - **Region**: misma región
   - **App Service Plan**: Click en "Create new"
     - **Name**: `plan-livedomain-prod`
     - **Sku and size**: Click en "Change size" → "Dev/Test" → "B1" (el más barato)
5. Click en "Review + create" → "Create"

#### 6.1 Obtener Nombre de Host

1. Cuando termine, click en "Go to resource"
2. En "Overview", copia el valor de "Default domain" (ej: `app-backend-livedomain-prod.azurewebsites.net`)

**Guárdalo**.

### Paso 7: Crear Azure App Service (Frontend)

Donde va a vivir React.

1. Busca "App Services" en el portal
2. Click en "+ Create"
3. Click en "Web App"
4. Rellena:
   - **Resource group**: `rg-livedomain-prod`
   - **Name**: `app-frontend-livedomain-prod` (debe ser único)
   - **Runtime stack**: "Node 20 LTS"
   - **Operating System**: "Linux"
   - **Region**: misma región
   - **App Service Plan**: Selecciona el que acabas de crear `plan-livedomain-prod` (o crea uno nuevo igual)
5. Click en "Review + create" → "Create"

#### 7.1 Obtener Nombre de Host

1. Cuando termine, click en "Go to resource"
2. En "Overview", copia el valor de "Default domain" (ej: `app-frontend-livedomain-prod.azurewebsites.net`)

**Guárdalo**.

### Paso 8: Configurar Variables De Entorno En Azure DevOps

Ahora le decimos a Azure DevOps dónde desplegar y qué configuración usar.

#### 8.1 Crear Variable Group Para Backend

1. Ve a tu proyecto en Azure DevOps: https://dev.azure.com/TuOrganizacion/TuProyecto
2. A la izquierda, click en "Pipelines"
3. Click en "Library"
4. Click en "+ Variable group"
5. Rellena:
   - **Name**: `vars-backend`
6. Agrega estas variables (click en "+ Add"):

| Variable | Valor | Explicación |
|----------|-------|-------------|
| `RESOURCE_GROUP` | `rg-livedomain-prod` | Grupo de recursos que creaste |
| `APP_SERVICE_NAME` | `app-backend-livedomain-prod` | Nombre del App Service backend |
| `AZURE_SQL_CONNECTION_STRING` | (pegar la que copiaste en 2.4) | Connection string de SQL |
| `JWT_SECRET_KEY` | `TuClaveSecretaLargaDeMas32Caracteres123!` | Clave para firmar tokens JWT (mínimo 32 caracteres) |
| `ADMIN_SETUP_KEY` | `TuClaveAdminMuySegura123!` | Clave para el endpoint `/api/admin/database/ensure-created` |
| `FRONTEND_BASE_URL` | `https://app-frontend-livedomain-prod.azurewebsites.net` | URL del frontend (sin slash al final) |
| `BACKEND_BASE_URL` | `https://app-backend-livedomain-prod.azurewebsites.net` | URL del backend (sin slash al final) |
| `STORAGE_CONNECTION_STRING` | (pegar la que copiaste en 3.2) | Connection string de Blob Storage |
| `PHOTO_STORAGE_BACKEND` | `azure` | Usar Azure Blob Storage |
| `APPINSIGHTS_INSTRUMENTATION_KEY` | (pegar la que copiaste en 5.1) | Instrumentation Key de Application Insights |
| `AZURE_APPCONFIG_CONNECTION_STRING` | (pegar la que copiaste en 4.1) | Connection string de App Configuration (opcional) |

7. Click en "Save"

#### 8.2 Crear Variable Group Para Frontend

1. Click en "+ Variable group"
2. Rellena:
   - **Name**: `vars-frontend`
3. Agrega estas variables:

| Variable | Valor | Explicación |
|----------|-------|-------------|
| `RESOURCE_GROUP` | `rg-livedomain-prod` | Grupo de recursos |
| `APP_SERVICE_NAME` | `app-frontend-livedomain-prod` | Nombre del App Service frontend |
| `API_BASE_URL` | `https://app-backend-livedomain-prod.azurewebsites.net` | URL del backend (sin slash) |

4. Click en "Save"

#### 8.3 Conectar Variable Groups a Pipelines

Para que las pipelines accedan a estas variables:

1. Ve a tu pipeline (ej: `azure-pipelines-backend.yml`)
2. Click en "Edit"
3. A la derecha, bajo "Variables", click en "Variable groups"
4. Click en "Link variable group"
5. Selecciona `vars-backend`
6. Click en "Link"

Repite para `azure-pipelines-frontend.yml` con `vars-frontend`.

### Resumen De Lo Que Creaste

| Recurso | Nombre | Propósito |
|---------|--------|----------|
| Resource Group | `rg-livedomain-prod` | Carpeta virtual que contiene todo |
| SQL Server | `sql-livedomain-prod` | Servidor de base de datos |
| SQL Database | `quotes-db` | Base de datos en el servidor |
| Storage Account | `salivedomain` | Almacenamiento de fotos |
| Container | `photos` | Carpeta dentro del storage |
| App Configuration | `appcfg-livedomain-prod` | Feature flags y configuración |
| Application Insights | `appins-livedomain-prod` | Logs y telemetría |
| App Service Backend | `app-backend-livedomain-prod` | Servidor de la API |
| App Service Frontend | `app-frontend-livedomain-prod` | Servidor de la web |

### Variables De Entorno Locales

Para correr localmente, crea un archivo `.env` en la carpeta `quotes-backend`:

```env
# Base de datos
AZURE_SQL_CONNECTION_STRING=Server=tcp:sql-livedomain-prod.database.windows.net,1433;Initial Catalog=quotes-db;Persist Security Info=False;User ID=sqladmin;Password=P@ssw0rd123!Azure2024;Encrypt=True;Connection Timeout=30;

# Autenticación
JWT_SECRET_KEY=TuClaveSecretaLargaDeMas32Caracteres123!
ADMIN_SETUP_KEY=TuClaveAdminMuySegura123!

# URLs
FRONTEND_BASE_URL=http://localhost:5173
BACKEND_BASE_URL=http://localhost:5000

# Storage
STORAGE_CONNECTION_STRING=DefaultEndpointsProtocol=https;AccountName=salivedomain;AccountKey=TuKeyLargaAqui==;EndpointSuffix=core.windows.net
PHOTO_STORAGE_BACKEND=local

# Application Insights (opcional localmente)
APPINSIGHTS_INSTRUMENTATION_KEY=12345678-1234-1234-1234-123456789012

# App Configuration (opcional)
AZURE_APPCONFIG_CONNECTION_STRING=Endpoint=https://appcfg-livedomain-prod.azureconfig.io;Id=...;Secret=...
```

### Troubleshooting De Creación De Recursos

| Error | Causa | Solución |
|-------|-------|----------|
| "Name already exists" | El nombre que elegiste ya existe en Azure | Usa un nombre diferente, más único |
| "Quota exceeded" | Has alcanzado el límite de tu suscripción | Pide que aumenten la cuota o elimina recursos antiguos |
| "Access denied" | No tienes permisos en Azure | Pide que te den rol "Contributor" o superior |
| No puedo conectarme a SQL | El firewall está bloqueando | Ve a SQL Server → Firewalls → Agrega tu IP |
| Storage connection string es incorrecta | Copiaste mal o está expirada | Ve a Access keys y copia nuevamente |

## Orden Recomendado De Despliegue

1. Crear o validar recursos base: App Service, Azure SQL, Storage, App Configuration, App Insights.
2. Desplegar backend.
3. Ejecutar pipeline de schema.
4. Validar backend con `/health` y `/health/db`.
5. Desplegar frontend.
6. Probar login, feed, create quote, like y upload de fotos.
7. Revisar logs y Application Insights.

## Ejecución Local

### Backend local

El backend local también usa Azure SQL. No uses SQLite.

#### `.env` mínimo

```env
AZURE_SQL_CONNECTION_STRING=<connection string de Azure SQL>
JWT_SECRET_KEY=<clave larga>
ADMIN_SETUP_KEY=<clave secreta>
FRONTEND_BASE_URL=http://localhost:5173
BACKEND_BASE_URL=http://localhost:5000
PHOTO_STORAGE_BACKEND=local
```

#### Comandos

```bash
dotnet restore
dotnet run
```

#### Pruebas locales

- `http://localhost:5000/health`
- `http://localhost:5000/health/db`
- `http://localhost:5000/apidocs`

### Frontend local

#### `.env` mínimo

```env
VITE_BACKEND_URL=http://localhost:5000
```

#### Comandos

```bash
npm install
npm run dev
```

#### Abrir

- `http://localhost:5173`

## Testing Con Swagger

El backend expone OpenAPI en Swagger UI, ideal para probar la API durante desarrollo y troubleshooting.

### Acceso a Swagger

**Local:**
- `http://localhost:5000/apidocs`

**En Azure:**
- `https://<backend-app>.azurewebsites.net/apidocs`

### Flujo De Testing: Registro, Login Y Endpoints Protegidos

El proceso típico para probar endpoints protegidos es:

1. **Registrar un usuario** con `POST /api/auth/register`.
2. **Iniciar sesión** con `POST /api/auth/login` para obtener un JWT.
3. **Copiar el token** en el campo de autorización global de Swagger.
4. **Probar endpoints protegidos** que requieren autenticación.

### Paso 1: Registro (Público)

1. En Swagger, abre `POST /api/auth/register`.
2. Click en "Try it out".
3. En el body, ingresa:

```json
{
  "email": "estudiante@example.com",
  "password": "password123"
}
```

4. Click en "Execute".
5. Deberías recibir un status `200 OK` con un token JWT:

```json
{
  "access_token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "token_type": "Bearer",
  "user": {
    "id": 1,
    "email": "estudiante@example.com",
    "createdAt": "2024-05-11T12:00:00Z"
  }
}
```

### Paso 2: Login (Público)

Alternativamente, si el usuario ya existe, usa login:

1. En Swagger, abre `POST /api/auth/login`.
2. Click en "Try it out".
3. En el body, ingresa:

```json
{
  "email": "estudiante@example.com",
  "password": "password123"
}
```

4. Click en "Execute".
5. Copia el `access_token` del response.

### Paso 3: Configurar Bearer Token En Swagger

1. En la parte superior derecha de Swagger UI, haz click en el botón `Authorize` (icono de candado).
2. En el cuadro de diálogo, selecciona "Bearer Token" si está disponible, o pega manualmente:

```
Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

3. O simplemente pega el token sin "Bearer ", Swagger lo agregará automáticamente:

```
eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

4. Click en "Authorize" y luego "Close".

### Paso 4: Probar Endpoints Protegidos

Ahora que tienes el token configurado, puedes probar endpoints protegidos:

#### GET /api/me (Perfil del usuario autenticado)

1. Abre `GET /api/me` en Swagger.
2. Click en "Try it out".
3. Click en "Execute".
4. Deberías recibir tu perfil:

```json
{
  "id": 1,
  "email": "estudiante@example.com",
  "createdAt": "2024-05-11T12:00:00Z"
}
```

#### GET /api/quotes?scope=mine (Tus quotes)

1. Abre `GET /api/quotes` en Swagger.
2. En parámetros, configura `scope=mine`.
3. Click en "Execute".
4. Si no tienes quotes, recibirás un array vacío `[]`.

#### POST /api/quotes (Crear un quote)

1. Abre `POST /api/quotes` en Swagger.
2. Click en "Try it out".
3. En el body (form-data), ingresa:
   - `content`: "Mi primer pensamiento con Azure DevOps"
   - `is_public`: `true` (checkbox)
   - `photo`: (opcional, sube una imagen JPG/PNG)

4. Click en "Execute".
5. Deberías recibir tu quote creado con ID.

### Estructura Del JWT

El JWT generado tiene esta estructura:

**Header:**
```json
{
  "alg": "HS256",
  "typ": "JWT"
}
```

**Payload:**
```json
{
  "sub": "1",
  "email": "estudiante@example.com",
  "iat": 1715425200,
  "exp": 1715511600,
  "iss": "azure-quotes-api",
  "aud": "azure-quotes-client"
}
```

**Notas sobre el JWT:**
- `sub`: ID del usuario.
- `email`: Email del usuario.
- `iat`: Tiempo de emisión (Unix timestamp).
- `exp`: Tiempo de expiración (Unix timestamp, aprox. 1 día).
- `iss`: Emisor (debe coincidir con lo configurado en `Program.cs`).
- `aud`: Audiencia (debe coincidir con lo configurado en `Program.cs`).

### Testing Con cURL

Si prefieres usar línea de comandos, aquí está el flujo completo:

#### 1. Registrar usuario

```bash
curl -X POST "http://localhost:5000/api/auth/register" \
  -H "Content-Type: application/json" \
  -d '{
    "email": "test@example.com",
    "password": "password123"
  }'
```

Response:
```json
{
  "access_token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "token_type": "Bearer",
  "user": { "id": 1, "email": "test@example.com", "createdAt": "..." }
}
```

#### 2. Usar el token en endpoints protegidos

```bash
# Reemplaza YOUR_TOKEN con el access_token del response anterior
TOKEN="eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."

curl -X GET "http://localhost:5000/api/me" \
  -H "Authorization: Bearer $TOKEN"
```

Response:
```json
{
  "id": 1,
  "email": "test@example.com",
  "createdAt": "2024-05-11T12:00:00Z"
}
```

#### 3. Crear un quote

```bash
TOKEN="eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."

curl -X POST "http://localhost:5000/api/quotes" \
  -H "Authorization: Bearer $TOKEN" \
  -F "content=Mi pensamiento con cURL" \
  -F "is_public=true"
```

#### 4. Obtener feed público (sin token)

```bash
curl -X GET "http://localhost:5000/api/quotes?scope=feed"
```

#### 5. Obtener tus quotes (con token)

```bash
TOKEN="eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."

curl -X GET "http://localhost:5000/api/quotes?scope=mine" \
  -H "Authorization: Bearer $TOKEN"
```

### Errores Comunes En Testing

| Error | Causa | Solución |
|---|---|---|
| `401 Unauthorized` | Token no está incluido o es inválido | Verifica que el token esté en el header `Authorization: Bearer <token>` |
| `IDX10653 HS256 requires key size of at least 128 bits` | `JWT_SECRET_KEY` es demasiado corta | Configura una clave de mínimo 32 caracteres |
| `403 Forbidden` | Permisos insuficientes | Verifica que el usuario sea el propietario del recurso |
| `404 Not Found` | El quote no existe | Verifica que el `quoteId` sea válido |
| `400 Bad Request` | Body malformado o campo faltante | Revisa que el JSON sea válido |

### Inspeccionando El JWT

Puedes decodificar un JWT sin verificar la firma en `jwt.io` para inspeccionar su contenido. **Advertencia:** Esto es solo para debugging local, nunca confíes en tokens sin verificación de firma.

1. Ve a `https://jwt.io`.
2. Pega tu token en el campo "Encoded".
3. Verás el Header y Payload decodificados en el lado derecho.

### Notas De Seguridad Para Testing

- **Nunca** guardes tokens en el navegador sin protección (aunque Swagger UI lo hace localmente por conveniencia).
- **Nunca** incluyas tokens en logs o mensajes de error en producción.
- Los tokens expiran después de ~24 horas; deberás volver a hacer login.
- Si cambias `JWT_SECRET_KEY`, todos los tokens existentes serán inválidos.

## Consultas SQL Útiles

```sql
SELECT COUNT(*) AS UsersCount FROM dbo.Users;
SELECT COUNT(*) AS QuotesCount FROM dbo.Quotes;
SELECT COUNT(*) AS LikesCount FROM dbo.QuoteLikes;

SELECT TOP 20 * FROM dbo.Quotes ORDER BY CreatedAt DESC;
SELECT TOP 20 * FROM dbo.Users ORDER BY CreatedAt DESC;
```

## Comandos Útiles De Validación

```bash
dotnet build .\quotes-backend\AzureQuotes.Api.csproj
dotnet run --project .\quotes-backend\AzureQuotes.Api.csproj

npm install
npm run build
npm run dev
```

## Buenas Prácticas De Seguridad

- No subas secretos al repositorio.
- Usa variable groups y App Service App Settings para las claves.
- Usa una `JWT_SECRET_KEY` larga y aleatoria.
- Protege `ADMIN_SETUP_KEY` como secreto real.
- Limita CORS a dominios reales del frontend.
- Usa `PHOTO_STORAGE_BACKEND=azure` en producción.
- Revisa Application Insights y logs ante cualquier cambio de flag o despliegue.

## Observabilidad Y Application Map

El backend está preparado para generar suficiente señal operativa para el demo:

- Requests HTTP con trazas estructuradas.
- Eventos de negocio para auth, quotes, likes y health.
- Dependencias explícitas para Blob Storage.
- Telemetría de Azure SQL a través de EF Core y Application Insights.

Con esto, Application Insights puede mostrar el flujo entre App Service, Azure SQL y Azure Blob Storage en Application Map, útil para triage de aplicaciones distribuidas.

## Troubleshooting

| Error | Causa | Solución |
|---|---|---|
| `No files matched the search pattern` | El pipeline no encuentra el `.csproj` | Usa `**/AzureQuotes.Api.csproj` o revisa la ruta del repo |
| `Container path not found: quotes-frontend` | El frontend no está en la carpeta esperada | Usa `$(Build.SourcesDirectory)` como directorio frontend |
| `Cannot find package 'express'` | `server.mjs` esperaba Express pero Azure no tenía `node_modules` | Mantén `server.mjs` sin dependencias externas o permite `npm install` en Azure si decides usar Express |
| `Invalid object name 'Users'` | Azure SQL no tiene schema creado | Ejecuta el pipeline `create-schema` o el endpoint `ensure-created` |
| `ADMIN_SETUP_KEY is not configured` | La variable existe en el group pero no en App Service | Agrega `ADMIN_SETUP_KEY` en `AzureAppServiceSettings` del pipeline backend |
| `CORS blocked: No Access-Control-Allow-Origin header` | `FRONTEND_BASE_URL` no incluye el dominio real | Configura `FRONTEND_BASE_URL` con el frontend real sin slash final |
| `IDX10653 HS256 requires key size of at least 128 bits` | `JWT_SECRET_KEY` es demasiado corta | Usa una clave de mínimo 32 caracteres recomendados |
| `Azure App Configuration was not loaded: Invalid connection string format` | `AZURE_APP_CONFIG_CONNECTION_STRING` está mal formada | Corrígela o elimínala si no se usa todavía |

## Flujo Recomendado Para El Tutorial

1. Explicar arquitectura general.
2. Configurar Azure SQL, Storage, App Service e identidad.
3. Desplegar backend y validar health checks.
4. Crear schema con el pipeline manual.
5. Desplegar frontend y validar conexión a la API.
6. Crear usuario, iniciar sesión y generar JWT.
7. Probar quote, like y subida de fotos.
8. Activar o desactivar feature flags en Azure App Configuration.
9. Revisar Application Insights, logs y Application Map.
10. Resolver errores reales usando la sección de troubleshooting.

## Notas Didácticas Para El Curso

- Backend, schema y frontend van separados para enseñar control operacional real.
- Azure SQL es la base única en local y producción para evitar derivas entre entornos.
- `EnsureCreatedAsync()` se usa como mecanismo pedagógico para mostrar creación controlada del schema.
- `server.mjs` expone `/config.js` para resolver `API_BASE_URL` en runtime sin rearmar el frontend.
- Feature flags y observabilidad están pensados para demostrar cambio operacional sin redeploy.

---

Si quieres, el siguiente paso natural es adaptar este README a una versión más corta para portada del curso y dejar una versión larga como documentación técnica interna.

## Modernización: Contenerización y despliegue en Azure Container Apps (ACR → Container Apps)

Esta sección describe pasos prácticos para convertir el backend en una imagen Docker, subirla a Azure Container Registry (ACR) y desplegarla en Azure Container Apps. Incluye un `Dockerfile` de ejemplo y comandos `az` para ejecutar en tu cuenta.

Requisitos previos
- Azure CLI instalada y autenticada (`az login`).
- Extensiones necesarias: `az extension add --name containerapp --upgrade` y `az extension add --name acr` (si no están instaladas).
- Permisos para crear recursos en el `resource group` o una service principal con permiso para ACR/Container Apps.

1) Crear ACR (Azure Container Registry)

```bash
az acr create --resource-group rg-livedomain-prod --name acrlivedomainprod --sku Standard --location eastus
az acr login --name acrlivedomainprod
```

2) Dockerfile (ubicado en la raíz del repositorio `quotes-backend/Dockerfile`)

El repositorio incluye un `Dockerfile` multi-stage optimizado para .NET 10. Ejemplo:

```dockerfile
# build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# copy csproj and restore
COPY *.sln ./
COPY AzureQuotes.Api/*.csproj ./AzureQuotes.Api/
RUN dotnet restore

# copy everything and publish
COPY . .
WORKDIR /src/AzureQuotes.Api
RUN dotnet publish -c Release -o /app/publish /p:PublishTrimmed=true

# runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80
COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "AzureQuotes.Api.dll"]
```

3) Construir y etiquetar la imagen

```bash
az acr login --name acrlivedomainprod
ACR_NAME=acrlivedomainprod
IMAGE_NAME=${ACR_NAME}.azurecr.io/azurequotes-api:latest
docker build -f Dockerfile -t $IMAGE_NAME .
docker push $IMAGE_NAME
```

4) Crear un Environment para Azure Container Apps (si no existe)

```bash
az containerapp env create --name aca-env-livedomain --resource-group rg-livedomain-prod --location eastus
```

5) Crear la Container App apuntando a la imagen en ACR

Si tu ACR requiere autenticación, usa `--registry-server`, `--registry-username` y `--registry-password` o un service principal/managed identity.

```bash
az containerapp create \
  --name azurequotes-api \
  --resource-group rg-livedomain-prod \
  --environment aca-env-livedomain \
  --image acrlivedomainprod.azurecr.io/azurequotes-api:latest \
  --ingress 'external' --target-port 80 \
  --cpu 0.5 --memory 1.0Gi \
  --registry-server acrlivedomainprod.azurecr.io \
  --registry-username <acr-username> \
  --registry-password <acr-password> \
  --env-vars "ASPNETCORE_ENVIRONMENT=Production" "WEBSITES_PORT=80" "JWT_SECRET_KEY=<your-jwt-secret>" "AZURE_SQL_CONNECTION_STRING=<conn-string>" "STORAGE_CONNECTION_STRING=<storage-conn>"
```

6) Hacer deploy automatizado en CI (snippet ejemplo para Azure DevOps)

```yaml
# task: Docker@2
- task: Docker@2
  displayName: Build and push image
  inputs:
    command: buildAndPush
    repository: acrlivedomainprod.azurecr.io/azurequotes-api
    dockerfile: Dockerfile
    tags: latest
    containerRegistry: 'ACR-Service-Connection'

# Luego, desploy a Container Apps usando AzureCLI@2
- task: AzureCLI@2
  displayName: Deploy Container App
  inputs:
    azureSubscription: 'ACR-Service-Connection'
    scriptType: bash
    scriptLocation: inlineScript
    inlineScript: |
      az containerapp revision set-mode --name azurequotes-api --resource-group $(RESOURCE_GROUP) --mode single
      az containerapp update --name azurequotes-api --resource-group $(RESOURCE_GROUP) --set properties.template.containers[0].image=acrlivedomainprod.azurecr.io/azurequotes-api:$(Build.BuildId)
```

7) Variables y secretos

Guarda secretos sensibles (JWT keys, connection strings) como **secrets** en tu pipeline o en Key Vault y pásalos como `--env-vars` o `--secrets` para Container Apps. No subas nunca valores sensibles al repositorio.

8) Mapeo de `appSettings` y variables de entorno

Si tienes un JSON de `appSettings` (por ejemplo los valores que usas en App Service), conviértelos en variables de entorno para Container App. Ejemplo de mapeo:

- `API_BASE_URL` → `API_BASE_URL`
- `PUBLIC_FEED` → `featurePublicFeedEnabled` (o maneja el mapeo en `server.mjs`)
- `PHOTO_UPLOAD` → `featurePhotoUploadEnabled`
- `MaintenanceMode` → `featureMaintenanceModeEnabled`

9) Validar

- `az containerapp show --name azurequotes-api --resource-group rg-livedomain-prod`
- Accede a `https://<containerapp-hostname>` y valida `/health`.

Notas finales
- Recomendamos usar ACR Tasks o pipeline CI para builds reproducibles.
- Usa `--secrets` y Key Vault para no exponer valores sensibles.
- Para escalado y observabilidad, conecta Application Insights y revisa logs.
