using System.Security.Claims;
using System.Diagnostics;
using System.Text;
using Azure.Identity;
using AzureQuotes.Api.Contracts;
using AzureQuotes.Api.Data;
using AzureQuotes.Api.Models;
using AzureQuotes.Api.Services;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.AppConfiguration.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

EnvFileLoader.Load();

var builder = WebApplication.CreateBuilder(args);

var appConfigConnectionString = builder.Configuration["AZURE_APP_CONFIG_CONNECTION_STRING"];
var appConfigEndpoint = builder.Configuration["AZURE_APP_CONFIG_ENDPOINT"];

if (!string.IsNullOrWhiteSpace(appConfigConnectionString)
    && appConfigConnectionString.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
    && appConfigConnectionString.Contains(";Id=", StringComparison.OrdinalIgnoreCase)
    && !appConfigConnectionString.StartsWith("Endpoint=", StringComparison.OrdinalIgnoreCase))
{
    appConfigConnectionString = $"Endpoint={appConfigConnectionString}";
}

var featureFlagRefreshSeconds = int.TryParse(
    builder.Configuration["FEATURE_REFRESH_SECONDS"],
    out var parsedRefreshSeconds)
    ? Math.Max(parsedRefreshSeconds, 1)
    : 10;

try
{
    if (!string.IsNullOrWhiteSpace(appConfigConnectionString))
    {
        builder.Configuration.AddAzureAppConfiguration(options =>
        {
            options.Connect(appConfigConnectionString)
                .Select("*")
                .UseFeatureFlags(featureFlags =>
                {
                    featureFlags.SetRefreshInterval(TimeSpan.FromSeconds(featureFlagRefreshSeconds));
                });
        });
    }
    else if (!string.IsNullOrWhiteSpace(appConfigEndpoint))
    {
        builder.Configuration.AddAzureAppConfiguration(options =>
        {
            options.Connect(new Uri(appConfigEndpoint), new DefaultAzureCredential())
                .Select("*")
                .UseFeatureFlags(featureFlags =>
                {
                    featureFlags.SetRefreshInterval(TimeSpan.FromSeconds(featureFlagRefreshSeconds));
                });
        });
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Azure App Configuration was not loaded: {ex.Message}");
}

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddApplicationInsightsTelemetry(options =>
{
    options.EnableAdaptiveSampling = false;
    options.EnableQuickPulseMetricStream = true;
});

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Azure Quotes API",
        Version = "v1",
        Description = "API educativa para Azure DevOps de Cero a Experto"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Pega solamente el JWT. No escribas la palabra Bearer.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        var configuredOrigins = builder.Configuration["FRONTEND_BASE_URL"];

        if (string.IsNullOrWhiteSpace(configuredOrigins))
        {
            policy.AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod();

            return;
        }

        var origins = configuredOrigins
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        policy.WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddDbContext<QuotesDbContext>(options =>
{
    var sqlConnectionString = builder.Configuration["AZURE_SQL_CONNECTION_STRING"];

    if (string.IsNullOrWhiteSpace(sqlConnectionString))
    {
        throw new InvalidOperationException(
            "AZURE_SQL_CONNECTION_STRING is required. This application uses Azure SQL only.");
    }

    options.UseSqlServer(
        sqlConnectionString,
        sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null);
        });
});

var jwtSecret = builder.Configuration["JWT_SECRET_KEY"];

if (string.IsNullOrWhiteSpace(jwtSecret))
{
    throw new InvalidOperationException("JWT_SECRET_KEY is required.");
}

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "azure-quotes-api",

            ValidateAudience = true,
            ValidAudience = "azure-quotes-client",

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddAzureAppConfiguration();

builder.Services.AddScoped<PasswordHasher<AppUser>>();
builder.Services.AddScoped<JwtService>();
builder.Services.AddSingleton<FeatureFlagService>();

var photoBackend = builder.Configuration["PHOTO_STORAGE_BACKEND"] ?? "local";

if (photoBackend.Equals("azure", StringComparison.OrdinalIgnoreCase)
    || photoBackend.Equals("azure_blob", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<IPhotoStorageService, AzureBlobPhotoStorageService>();
}
else
{
    builder.Services.AddScoped<IPhotoStorageService, LocalPhotoStorageService>();
}

var app = builder.Build();

app.Logger.LogInformation("Application configured to use Azure SQL only.");
app.Logger.LogInformation("Automatic database initialization is disabled during startup.");

var uploadsFolder =
    app.Configuration["UPLOAD_FOLDER"]
    ?? app.Configuration["UPLOADS_PATH"]
    ?? "uploads";

var uploadsPath = Path.IsPathRooted(uploadsFolder)
    ? uploadsFolder
    : Path.Combine(app.Environment.ContentRootPath, uploadsFolder);

Directory.CreateDirectory(uploadsPath);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads",
    ContentTypeProvider = new FileExtensionContentTypeProvider()
});

app.UseAzureAppConfiguration();

app.Use(async (context, next) =>
{
    var telemetry = context.RequestServices.GetRequiredService<TelemetryClient>();
    var stopwatch = Stopwatch.StartNew();
    var requestPath = context.Request.Path.HasValue ? context.Request.Path.Value! : "/";

    telemetry.TrackTrace(
        "http.request.start",
        new Dictionary<string, string>
        {
            ["method"] = context.Request.Method,
            ["path"] = requestPath,
            ["trace_id"] = Activity.Current?.TraceId.ToString() ?? string.Empty,
            ["span_id"] = Activity.Current?.SpanId.ToString() ?? string.Empty
        });

    app.Logger.LogInformation(
        "http.request.start method={Method} path={Path} trace_id={TraceId}",
        context.Request.Method,
        requestPath,
        Activity.Current?.TraceId.ToString());

    try
    {
        await next();
        stopwatch.Stop();

        telemetry.TrackTrace(
            "http.request.end",
            new Dictionary<string, string>
            {
                ["method"] = context.Request.Method,
                ["path"] = requestPath,
                ["status_code"] = context.Response.StatusCode.ToString(),
                ["elapsed_ms"] = stopwatch.ElapsedMilliseconds.ToString(),
                ["trace_id"] = Activity.Current?.TraceId.ToString() ?? string.Empty
            });

        app.Logger.LogInformation(
            "http.request.end method={Method} path={Path} status={StatusCode} elapsed_ms={ElapsedMs}",
            context.Request.Method,
            requestPath,
            context.Response.StatusCode,
            stopwatch.ElapsedMilliseconds);
    }
    catch (Exception ex)
    {
        stopwatch.Stop();

        telemetry.TrackException(ex, new Dictionary<string, string>
        {
            ["method"] = context.Request.Method,
            ["path"] = requestPath,
            ["elapsed_ms"] = stopwatch.ElapsedMilliseconds.ToString(),
            ["trace_id"] = Activity.Current?.TraceId.ToString() ?? string.Empty
        });

        app.Logger.LogError(
            ex,
            "http.request.failed method={Method} path={Path} elapsed_ms={ElapsedMs}",
            context.Request.Method,
            requestPath,
            stopwatch.ElapsedMilliseconds);

        throw;
    }
});

app.UseSwagger(options =>
{
    options.RouteTemplate = "{documentName}/apispec.json";
});

app.UseSwaggerUI(options =>
{
    options.RoutePrefix = "apidocs";
    options.SwaggerEndpoint("/v1/apispec.json", "Azure Quotes API v1");
});

app.UseCors("frontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    app = "Azure Quotes API",
    status = "running",
    database = "Azure SQL",
    docs = "/apidocs",
    openapi = "/apispec.json"
}));

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    database = "Azure SQL",
    timestamp = DateTime.UtcNow
}));

app.MapGet("/health/db", async (
    QuotesDbContext db,
    TelemetryClient telemetry,
    CancellationToken cancellationToken) =>
{
    telemetry.TrackEvent("health.db.check.start");

    try
    {
        var canConnect = await db.Database.CanConnectAsync(cancellationToken);

        if (!canConnect)
        {
            telemetry.TrackEvent("health.db.check.failed", new Dictionary<string, string>
            {
                ["result"] = "cannot_connect"
            });

            return Results.Problem(
                title: "Database connection failed",
                detail: "The application could not connect to Azure SQL.");
        }

        telemetry.TrackEvent("health.db.check.succeeded", new Dictionary<string, string>
        {
            ["result"] = "connected"
        });

        return Results.Ok(new
        {
            status = "database_available",
            provider = "Azure SQL",
            timestamp = DateTime.UtcNow
        });
    }
    catch (Exception ex)
    {
        telemetry.TrackException(ex, new Dictionary<string, string>
        {
            ["operation"] = "health.db.check"
        });

        return Results.Problem(
            title: "Database connection error",
            detail: ex.Message);
    }
});

app.MapPost("/api/admin/database/ensure-created", async (
    HttpRequest request,
    QuotesDbContext db,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    var expectedKey = configuration["ADMIN_SETUP_KEY"];
    var providedKey = request.Headers["X-Setup-Key"].ToString();

    if (string.IsNullOrWhiteSpace(expectedKey))
    {
        return Results.Problem(
            title: "ADMIN_SETUP_KEY is not configured",
            detail: "Configure ADMIN_SETUP_KEY in .env locally or App Settings in Azure.");
    }

    if (!string.Equals(expectedKey, providedKey, StringComparison.Ordinal))
    {
        return Results.Unauthorized();
    }

    await db.Database.EnsureCreatedAsync(cancellationToken);

    return Results.Ok(new
    {
        status = "database_initialized",
        provider = "Azure SQL",
        timestamp = DateTime.UtcNow
    });
});

app.MapGet("/apispec.json", () => Results.Redirect("/v1/apispec.json"));

app.MapGet("/api/features", (FeatureFlagService features) =>
{
    var current = features.GetFeatures();

    app.Logger.LogInformation(
        "features.loaded public_feed_enabled={PublicFeedEnabled} photo_upload_enabled={PhotoUploadEnabled} maintenance_mode_enabled={MaintenanceModeEnabled}",
        current.PublicFeedEnabled,
        current.PhotoUploadEnabled,
        current.MaintenanceModeEnabled);

    return Results.Ok(current);
});

app.MapPost("/api/auth/register", async (
    RegisterRequest request,
    QuotesDbContext db,
    PasswordHasher<AppUser> passwordHasher,
    JwtService jwt,
    TelemetryClient telemetry,
    CancellationToken cancellationToken) =>
{
    var email = NormalizeEmail(request.Email);

    if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
    {
        telemetry.TrackEvent("auth.register.rejected", new Dictionary<string, string>
        {
            ["reason"] = "invalid_email"
        });

        return Results.BadRequest(new { error = "Email invalido." });
    }

    if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
    {
        telemetry.TrackEvent("auth.register.rejected", new Dictionary<string, string>
        {
            ["reason"] = "weak_password"
        });

        return Results.BadRequest(new { error = "La clave debe tener minimo 6 caracteres." });
    }

    var exists = await db.Users.AnyAsync(x => x.Email == email, cancellationToken);

    if (exists)
    {
        telemetry.TrackEvent("auth.register.rejected", new Dictionary<string, string>
        {
            ["reason"] = "already_exists"
        });

        return Results.Conflict(new { error = "Este usuario ya existe." });
    }

    var user = new AppUser
    {
        Email = email
    };

    user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

    db.Users.Add(user);
    await db.SaveChangesAsync(cancellationToken);

    telemetry.TrackEvent("auth.register.succeeded", new Dictionary<string, string>
    {
        ["user_id"] = user.Id.ToString(),
        ["email_domain"] = email.Split('@').Last()
    });

    app.Logger.LogInformation(
        "auth.register.succeeded user_id={UserId} email_domain={EmailDomain}",
        user.Id,
        email.Split('@').Last());

    return Results.Ok(new AuthResponse(
        jwt.CreateToken(user),
        "Bearer",
        ToUserResponse(user)));
});

app.MapPost("/api/auth/login", async (
    LoginRequest request,
    QuotesDbContext db,
    PasswordHasher<AppUser> passwordHasher,
    JwtService jwt,
    TelemetryClient telemetry,
    CancellationToken cancellationToken) =>
{
    var email = NormalizeEmail(request.Email);

    var user = await db.Users
        .FirstOrDefaultAsync(x => x.Email == email, cancellationToken);

    if (user is null)
    {
        telemetry.TrackEvent("auth.login.failed", new Dictionary<string, string>
        {
            ["reason"] = "user_not_found"
        });

        return Results.Unauthorized();
    }

    var result = passwordHasher.VerifyHashedPassword(
        user,
        user.PasswordHash,
        request.Password);

    if (result == PasswordVerificationResult.Failed)
    {
        telemetry.TrackEvent("auth.login.failed", new Dictionary<string, string>
        {
            ["reason"] = "bad_password",
            ["user_id"] = user.Id.ToString()
        });

        return Results.Unauthorized();
    }

    telemetry.TrackEvent("auth.login.succeeded", new Dictionary<string, string>
    {
        ["user_id"] = user.Id.ToString()
    });

    app.Logger.LogInformation("auth.login.succeeded user_id={UserId}", user.Id);

    return Results.Ok(new AuthResponse(
        jwt.CreateToken(user),
        "Bearer",
        ToUserResponse(user)));
});

app.MapGet("/api/me", async (
    ClaimsPrincipal principal,
    QuotesDbContext db,
    TelemetryClient telemetry,
    CancellationToken cancellationToken) =>
{
    var userId = GetUserId(principal);

    var user = await db.Users.FindAsync([userId], cancellationToken);

    telemetry.TrackEvent("user.profile.loaded", new Dictionary<string, string>
    {
        ["user_id"] = userId.ToString(),
        ["found"] = (user is not null).ToString()
    });

    return user is null
        ? Results.NotFound(new { error = "Usuario no encontrado." })
        : Results.Ok(ToUserResponse(user));
}).RequireAuthorization();

app.MapGet("/api/quotes", async (
    HttpRequest request,
    ClaimsPrincipal principal,
    QuotesDbContext db,
    FeatureFlagService featureFlags,
    TelemetryClient telemetry,
    CancellationToken cancellationToken) =>
{
    var scope = request.Query["scope"].ToString();

    var authenticated = principal.Identity?.IsAuthenticated == true;
    var userId = authenticated ? GetUserId(principal) : 0;

    IQueryable<Quote> query = db.Quotes
        .Include(x => x.User)
        .Include(x => x.Likes);

    if (scope.Equals("mine", StringComparison.OrdinalIgnoreCase))
    {
        if (!authenticated)
        {
            telemetry.TrackEvent("quotes.mine.rejected", new Dictionary<string, string>
            {
                ["reason"] = "unauthorized"
            });

            return Results.Unauthorized();
        }

        var mine = await query
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        telemetry.TrackEvent("quotes.mine.loaded", new Dictionary<string, string>
        {
            ["user_id"] = userId.ToString(),
            ["count"] = mine.Count.ToString()
        });

        return Results.Ok(mine.Select(x => ToQuoteResponse(x, userId)));
    }

    var features = featureFlags.GetFeatures();

    telemetry.TrackEvent("quotes.feed.features_checked", new Dictionary<string, string>
    {
        ["public_feed_enabled"] = features.PublicFeedEnabled.ToString(),
        ["maintenance_mode_enabled"] = features.MaintenanceModeEnabled.ToString()
    });

    if (!features.PublicFeedEnabled)
    {
        telemetry.TrackEvent("quotes.feed_blocked", new Dictionary<string, string>
        {
            ["reason"] = "feature_disabled"
        });

        return Results.Ok(Array.Empty<QuoteResponse>());
    }

    var publicQuotes = await query
        .Where(x => x.IsPublic)
        .OrderByDescending(x => x.CreatedAt)
        .Take(100)
        .ToListAsync(cancellationToken);

    var randomized = publicQuotes
        .OrderBy(_ => Random.Shared.Next())
        .Take(50)
        .Select(x => ToQuoteResponse(x, authenticated ? userId : null));

    telemetry.TrackEvent("quotes.feed.loaded", new Dictionary<string, string>
    {
        ["count"] = publicQuotes.Count.ToString(),
        ["returned"] = randomized.Count().ToString()
    });

    return Results.Ok(randomized);
});

app.MapPost("/api/quotes", async (
    HttpRequest request,
    ClaimsPrincipal principal,
    QuotesDbContext db,
    IPhotoStorageService photoStorage,
    FeatureFlagService featureFlags,
    TelemetryClient telemetry,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var userId = GetUserId(principal);

    var form = await request.ReadFormAsync(cancellationToken);

    var content = form["content"].ToString().Trim();

    var isPublic = !bool.TryParse(form["is_public"], out var parsedIsPublic)
        || parsedIsPublic;

    var photo = form.Files["photo"];

    if (string.IsNullOrWhiteSpace(content))
    {
        telemetry.TrackEvent("quote.create.rejected", new Dictionary<string, string>
        {
            ["reason"] = "empty_content"
        });

        return Results.BadRequest(new { error = "El pensamiento no puede estar vacio." });
    }

    StoredPhoto? storedPhoto = null;

    if (photo is not null && photo.Length > 0)
    {
        if (!featureFlags.GetFeatures().PhotoUploadEnabled)
        {
            logger.LogWarning(
                "quote.photo_upload_rejected feature_disabled user_id={UserId}",
                userId);

            telemetry.TrackEvent("quote.photo_upload_blocked", new Dictionary<string, string>
            {
                ["user_id"] = userId.ToString(),
                ["reason"] = "feature_disabled"
            });

            return Results.BadRequest(new { error = "La subida de fotos esta desactivada." });
        }

        try
        {
            storedPhoto = await photoStorage.SaveAsync(photo, cancellationToken);

            logger.LogInformation(
                "quote.photo_uploaded user_id={UserId} url={Url}",
                userId,
                storedPhoto.Url);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(
                ex,
                "quote.photo_upload_rejected user_id={UserId}",
                userId);

            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "quote.photo_upload_failed user_id={UserId}",
                userId);

            return Results.Problem("No fue posible subir la foto.");
        }
    }

    var quote = new Quote
    {
        Content = content,
        IsPublic = isPublic,
        UserId = userId,
        PhotoUrl = storedPhoto?.Url,
        PhotoStorageKey = storedPhoto?.StorageKey
    };

    db.Quotes.Add(quote);
    await db.SaveChangesAsync(cancellationToken);

    telemetry.TrackEvent("quote.created", new Dictionary<string, string>
    {
        ["user_id"] = userId.ToString(),
        ["quote_id"] = quote.Id.ToString(),
        ["has_photo"] = (storedPhoto is not null).ToString(),
        ["is_public"] = quote.IsPublic.ToString()
    });

    var created = await db.Quotes
        .Include(x => x.User)
        .Include(x => x.Likes)
        .FirstAsync(x => x.Id == quote.Id, cancellationToken);

    return Results.Created(
        $"/api/quotes/{created.Id}",
        ToQuoteResponse(created, userId));
}).RequireAuthorization();

app.MapPut("/api/quotes/{quoteId:int}", async (
    int quoteId,
    HttpRequest request,
    ClaimsPrincipal principal,
    QuotesDbContext db,
    IPhotoStorageService photoStorage,
    FeatureFlagService featureFlags,
    TelemetryClient telemetry,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var userId = GetUserId(principal);

    var quote = await db.Quotes
        .Include(x => x.User)
        .Include(x => x.Likes)
        .FirstOrDefaultAsync(x => x.Id == quoteId, cancellationToken);

    if (quote is null)
    {
        return Results.NotFound(new { error = "Pensamiento no encontrado." });
    }

    if (quote.UserId != userId)
    {
        return Results.Forbid();
    }

    var form = await request.ReadFormAsync(cancellationToken);

    var content = form["content"].ToString().Trim();

    if (string.IsNullOrWhiteSpace(content))
    {
        telemetry.TrackEvent("quote.update.rejected", new Dictionary<string, string>
        {
            ["reason"] = "empty_content",
            ["quote_id"] = quoteId.ToString()
        });

        return Results.BadRequest(new { error = "El pensamiento no puede estar vacio." });
    }

    quote.Content = content;

    quote.IsPublic = !bool.TryParse(form["is_public"], out var parsedIsPublic)
        || parsedIsPublic;

    quote.UpdatedAt = DateTime.UtcNow;

    var photo = form.Files["photo"];

    if (photo is not null && photo.Length > 0)
    {
        if (!featureFlags.GetFeatures().PhotoUploadEnabled)
        {
            logger.LogWarning(
                "quote.photo_upload_rejected feature_disabled user_id={UserId} quote_id={QuoteId}",
                userId,
                quote.Id);

            telemetry.TrackEvent("quote.photo_upload_blocked", new Dictionary<string, string>
            {
                ["user_id"] = userId.ToString(),
                ["quote_id"] = quote.Id.ToString(),
                ["reason"] = "feature_disabled"
            });

            return Results.BadRequest(new { error = "La subida de fotos esta desactivada." });
        }

        try
        {
            await photoStorage.DeleteAsync(quote.PhotoStorageKey, cancellationToken);

            var storedPhoto = await photoStorage.SaveAsync(photo, cancellationToken);

            quote.PhotoUrl = storedPhoto.Url;
            quote.PhotoStorageKey = storedPhoto.StorageKey;

            logger.LogInformation(
                "quote.photo_uploaded user_id={UserId} quote_id={QuoteId} url={Url}",
                userId,
                quote.Id,
                storedPhoto.Url);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(
                ex,
                "quote.photo_upload_rejected user_id={UserId} quote_id={QuoteId}",
                userId,
                quote.Id);

            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "quote.photo_upload_failed user_id={UserId} quote_id={QuoteId}",
                userId,
                quote.Id);

            return Results.Problem("No fue posible reemplazar la foto.");
        }
    }

    await db.SaveChangesAsync(cancellationToken);

    telemetry.TrackEvent("quote.updated", new Dictionary<string, string>
    {
        ["user_id"] = userId.ToString(),
        ["quote_id"] = quote.Id.ToString(),
        ["has_photo"] = (quote.PhotoUrl is not null).ToString(),
        ["is_public"] = quote.IsPublic.ToString()
    });

    return Results.Ok(ToQuoteResponse(quote, userId));
}).RequireAuthorization();

app.MapDelete("/api/quotes/{quoteId:int}", async (
    int quoteId,
    ClaimsPrincipal principal,
    QuotesDbContext db,
    TelemetryClient telemetry,
    IPhotoStorageService photoStorage,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var userId = GetUserId(principal);

    var quote = await db.Quotes
        .FirstOrDefaultAsync(x => x.Id == quoteId, cancellationToken);

    if (quote is null)
    {
        telemetry.TrackEvent("quote.delete.rejected", new Dictionary<string, string>
        {
            ["reason"] = "not_found",
            ["quote_id"] = quoteId.ToString()
        });

        return Results.NotFound(new { error = "Pensamiento no encontrado." });
    }

    if (quote.UserId != userId)
    {
        return Results.Forbid();
    }

    try
    {
        await photoStorage.DeleteAsync(quote.PhotoStorageKey, cancellationToken);

        logger.LogInformation(
            "quote.photo_deleted user_id={UserId} quote_id={QuoteId}",
            userId,
            quote.Id);
    }
    catch (Exception ex)
    {
        logger.LogError(
            ex,
            "quote.photo_delete_failed user_id={UserId} quote_id={QuoteId}",
            userId,
            quote.Id);
    }

    db.Quotes.Remove(quote);
    await db.SaveChangesAsync(cancellationToken);

    telemetry.TrackEvent("quote.deleted", new Dictionary<string, string>
    {
        ["user_id"] = userId.ToString(),
        ["quote_id"] = quote.Id.ToString()
    });

    logger.LogInformation(
        "quote.deleted user_id={UserId} quote_id={QuoteId}",
        userId,
        quote.Id);

    return Results.NoContent();
}).RequireAuthorization();

app.MapPost("/api/quotes/{quoteId:int}/like", async (
    int quoteId,
    ClaimsPrincipal principal,
    QuotesDbContext db,
    TelemetryClient telemetry,
    CancellationToken cancellationToken) =>
{
    var userId = GetUserId(principal);

    var quote = await db.Quotes
        .Include(x => x.User)
        .Include(x => x.Likes)
        .FirstOrDefaultAsync(x => x.Id == quoteId, cancellationToken);

    if (quote is null)
    {
        telemetry.TrackEvent("quote.like.rejected", new Dictionary<string, string>
        {
            ["reason"] = "not_found",
            ["quote_id"] = quoteId.ToString()
        });

        return Results.NotFound(new { error = "Pensamiento no encontrado." });
    }

    var exists = quote.Likes.Any(x => x.UserId == userId);

    if (!exists)
    {
        quote.Likes.Add(new QuoteLike
        {
            QuoteId = quoteId,
            UserId = userId
        });

        await db.SaveChangesAsync(cancellationToken);

        telemetry.TrackEvent("quote.like.added", new Dictionary<string, string>
        {
            ["user_id"] = userId.ToString(),
            ["quote_id"] = quoteId.ToString()
        });
    }

    return Results.Ok(ToQuoteResponse(quote, userId));
}).RequireAuthorization();

app.MapDelete("/api/quotes/{quoteId:int}/like", async (
    int quoteId,
    ClaimsPrincipal principal,
    QuotesDbContext db,
    TelemetryClient telemetry,
    CancellationToken cancellationToken) =>
{
    var userId = GetUserId(principal);

    var like = await db.QuoteLikes
        .FirstOrDefaultAsync(
            x => x.QuoteId == quoteId && x.UserId == userId,
            cancellationToken);

    if (like is not null)
    {
        db.QuoteLikes.Remove(like);
        await db.SaveChangesAsync(cancellationToken);

        telemetry.TrackEvent("quote.like.removed", new Dictionary<string, string>
        {
            ["user_id"] = userId.ToString(),
            ["quote_id"] = quoteId.ToString()
        });
    }

    var quote = await db.Quotes
        .Include(x => x.User)
        .Include(x => x.Likes)
        .FirstOrDefaultAsync(x => x.Id == quoteId, cancellationToken);

    return quote is null
        ? Results.NotFound(new { error = "Pensamiento no encontrado." })
        : Results.Ok(ToQuoteResponse(quote, userId));
}).RequireAuthorization();

app.Run();

static string NormalizeEmail(string email)
{
    return email.Trim().ToLowerInvariant();
}

static int GetUserId(ClaimsPrincipal principal)
{
    var rawUserId =
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.FindFirstValue("sub");

    return int.TryParse(rawUserId, out var userId)
        ? userId
        : throw new UnauthorizedAccessException("Token invalido.");
}

static UserResponse ToUserResponse(AppUser user)
{
    return new UserResponse(
        user.Id,
        user.Email,
        user.CreatedAt);
}

static QuoteResponse ToQuoteResponse(Quote quote, int? currentUserId)
{
    return new QuoteResponse(
        QuoteId: quote.Id,
        Content: quote.Content,
        IsPublic: quote.IsPublic,
        PhotoUrl: quote.PhotoUrl,
        CreatedAt: quote.CreatedAt,
        UpdatedAt: quote.UpdatedAt,
        OwnerEmail: quote.User.Email,
        LikesCount: quote.Likes.Count,
        LikedByMe: currentUserId.HasValue
            && quote.Likes.Any(x => x.UserId == currentUserId.Value));
}