using System.Security.Claims;
using System.Text;
using AzureQuotes.Api.Contracts;
using AzureQuotes.Api.Data;
using AzureQuotes.Api.Models;
using AzureQuotes.Api.Services;
using Azure.Identity;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

EnvFileLoader.Load();

var builder = WebApplication.CreateBuilder(args);

var appConfigConnectionString = builder.Configuration["AZURE_APP_CONFIG_CONNECTION_STRING"];
var appConfigEndpoint = builder.Configuration["AZURE_APP_CONFIG_ENDPOINT"];
if (!string.IsNullOrWhiteSpace(appConfigConnectionString))
{
    builder.Configuration.AddAzureAppConfiguration(options => options.Connect(appConfigConnectionString).Select("*"));
}
else if (!string.IsNullOrWhiteSpace(appConfigEndpoint))
{
    builder.Configuration.AddAzureAppConfiguration(options => options.Connect(new Uri(appConfigEndpoint), new DefaultAzureCredential()).Select("*"));
}

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

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
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
            return;
        }

        var origins = configuredOrigins
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddDbContext<QuotesDbContext>(options =>
{
    var sqlConnectionString = builder.Configuration["AZURE_SQL_CONNECTION_STRING"];
    if (!string.IsNullOrWhiteSpace(sqlConnectionString))
    {
        options.UseSqlServer(sqlConnectionString);
        return;
    }

    var databaseUrl = builder.Configuration["DATABASE_URL"] ?? "sqlite:///quotes_local.db";
    options.UseSqlite(ConvertToSqliteConnectionString(databaseUrl));
});

var jwtSecret = builder.Configuration["JWT_SECRET_KEY"] ?? "dev-jwt-secret-change-me-please-use-a-long-secret";
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
builder.Services.AddScoped<PasswordHasher<AppUser>>();
builder.Services.AddScoped<JwtService>();
builder.Services.AddSingleton<FeatureFlagService>();

var photoBackend = builder.Configuration["PHOTO_STORAGE_BACKEND"] ?? "local";
if (photoBackend.Equals("azure", StringComparison.OrdinalIgnoreCase) || photoBackend.Equals("azure_blob", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<IPhotoStorageService, AzureBlobPhotoStorageService>();
}
else
{
    builder.Services.AddScoped<IPhotoStorageService, LocalPhotoStorageService>();
}

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
    db.Database.EnsureCreated();
}

var uploadsPath = Path.Combine(app.Environment.ContentRootPath, app.Configuration["UPLOAD_FOLDER"] ?? "uploads");
Directory.CreateDirectory(uploadsPath);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads",
    ContentTypeProvider = new FileExtensionContentTypeProvider()
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
    docs = "/apidocs",
    openapi = "/v1/apispec.json"
}));

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTimeOffset.UtcNow
}));

app.MapGet("/apispec.json", () => Results.Redirect("/v1/apispec.json"));

app.MapGet("/api/features", (FeatureFlagService features) => Results.Ok(features.GetFeatures()));

app.MapPost("/api/auth/register", async (
    RegisterRequest request,
    QuotesDbContext db,
    PasswordHasher<AppUser> passwordHasher,
    JwtService jwt,
    CancellationToken cancellationToken) =>
{
    var email = NormalizeEmail(request.Email);
    if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
    {
        return Results.BadRequest(new { error = "Email invalido." });
    }

    if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
    {
        return Results.BadRequest(new { error = "La clave debe tener minimo 6 caracteres." });
    }

    var exists = await db.Users.AnyAsync(x => x.Email == email, cancellationToken);
    if (exists)
    {
        return Results.Conflict(new { error = "Este usuario ya existe." });
    }

    var user = new AppUser { Email = email };
    user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

    db.Users.Add(user);
    await db.SaveChangesAsync(cancellationToken);

    return Results.Ok(new AuthResponse(jwt.CreateToken(user), "Bearer", ToUserResponse(user)));
});

app.MapPost("/api/auth/login", async (
    LoginRequest request,
    QuotesDbContext db,
    PasswordHasher<AppUser> passwordHasher,
    JwtService jwt,
    CancellationToken cancellationToken) =>
{
    var email = NormalizeEmail(request.Email);
    var user = await db.Users.FirstOrDefaultAsync(x => x.Email == email, cancellationToken);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
    if (result == PasswordVerificationResult.Failed)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new AuthResponse(jwt.CreateToken(user), "Bearer", ToUserResponse(user)));
});

app.MapGet("/api/me", async (ClaimsPrincipal principal, QuotesDbContext db, CancellationToken cancellationToken) =>
{
    var userId = GetUserId(principal);
    var user = await db.Users.FindAsync([userId], cancellationToken);
    return user is null ? Results.NotFound(new { error = "Usuario no encontrado." }) : Results.Ok(ToUserResponse(user));
}).RequireAuthorization();

app.MapGet("/api/quotes", async (
    HttpRequest request,
    ClaimsPrincipal principal,
    QuotesDbContext db,
    FeatureFlagService featureFlags,
    CancellationToken cancellationToken) =>
{
    var scope = request.Query["scope"].ToString();
    var authenticated = principal.Identity?.IsAuthenticated == true;
    var userId = authenticated ? GetUserId(principal) : 0;

    IQueryable<Quote> query = db.Quotes.Include(x => x.User).Include(x => x.Likes);

    if (scope.Equals("mine", StringComparison.OrdinalIgnoreCase))
    {
        if (!authenticated)
        {
            return Results.Unauthorized();
        }

        query = query.Where(x => x.UserId == userId).OrderByDescending(x => x.CreatedAt);
        var mine = await query.ToListAsync(cancellationToken);
        return Results.Ok(mine.Select(x => ToQuoteResponse(x, userId)));
    }

    var features = featureFlags.GetFeatures();
    if (!features.PublicFeedEnabled)
    {
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

    return Results.Ok(randomized);
});

app.MapPost("/api/quotes", async (
    HttpRequest request,
    ClaimsPrincipal principal,
    QuotesDbContext db,
    IPhotoStorageService photoStorage,
    FeatureFlagService featureFlags,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var userId = GetUserId(principal);
    var form = await request.ReadFormAsync(cancellationToken);
    var content = form["content"].ToString().Trim();
    var isPublic = !bool.TryParse(form["is_public"], out var parsedIsPublic) || parsedIsPublic;
    var photo = form.Files["photo"];

    if (string.IsNullOrWhiteSpace(content))
    {
        return Results.BadRequest(new { error = "El pensamiento no puede estar vacio." });
    }

    StoredPhoto? storedPhoto = null;
    if (photo is not null && photo.Length > 0)
    {
        if (!featureFlags.GetFeatures().PhotoUploadEnabled)
        {
            logger.LogWarning("quote.photo_upload_rejected feature_disabled user_id={UserId}", userId);
            return Results.BadRequest(new { error = "La subida de fotos esta desactivada." });
        }

        try
        {
            storedPhoto = await photoStorage.SaveAsync(photo, cancellationToken);
            logger.LogInformation("quote.photo_uploaded user_id={UserId} url={Url}", userId, storedPhoto.Url);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "quote.photo_upload_rejected user_id={UserId}", userId);
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "quote.photo_upload_failed user_id={UserId}", userId);
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

    var created = await db.Quotes.Include(x => x.User).Include(x => x.Likes).FirstAsync(x => x.Id == quote.Id, cancellationToken);
    return Results.Created($"/api/quotes/{created.Id}", ToQuoteResponse(created, userId));
}).RequireAuthorization();

app.MapPut("/api/quotes/{quoteId:int}", async (
    int quoteId,
    HttpRequest request,
    ClaimsPrincipal principal,
    QuotesDbContext db,
    IPhotoStorageService photoStorage,
    FeatureFlagService featureFlags,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var userId = GetUserId(principal);
    var quote = await db.Quotes.Include(x => x.User).Include(x => x.Likes).FirstOrDefaultAsync(x => x.Id == quoteId, cancellationToken);
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
        return Results.BadRequest(new { error = "El pensamiento no puede estar vacio." });
    }

    quote.Content = content;
    quote.IsPublic = !bool.TryParse(form["is_public"], out var parsedIsPublic) || parsedIsPublic;
    quote.UpdatedAt = DateTime.UtcNow;

    var photo = form.Files["photo"];
    if (photo is not null && photo.Length > 0)
    {
        if (!featureFlags.GetFeatures().PhotoUploadEnabled)
        {
            logger.LogWarning("quote.photo_upload_rejected feature_disabled user_id={UserId} quote_id={QuoteId}", userId, quote.Id);
            return Results.BadRequest(new { error = "La subida de fotos esta desactivada." });
        }

        try
        {
            await photoStorage.DeleteAsync(quote.PhotoStorageKey, cancellationToken);
            var storedPhoto = await photoStorage.SaveAsync(photo, cancellationToken);
            quote.PhotoUrl = storedPhoto.Url;
            quote.PhotoStorageKey = storedPhoto.StorageKey;
            logger.LogInformation("quote.photo_uploaded user_id={UserId} quote_id={QuoteId} url={Url}", userId, quote.Id, storedPhoto.Url);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "quote.photo_upload_rejected user_id={UserId} quote_id={QuoteId}", userId, quote.Id);
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "quote.photo_upload_failed user_id={UserId} quote_id={QuoteId}", userId, quote.Id);
            return Results.Problem("No fue posible reemplazar la foto.");
        }
    }

    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(ToQuoteResponse(quote, userId));
}).RequireAuthorization();

app.MapDelete("/api/quotes/{quoteId:int}", async (
    int quoteId,
    ClaimsPrincipal principal,
    QuotesDbContext db,
    IPhotoStorageService photoStorage,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var userId = GetUserId(principal);
    var quote = await db.Quotes.FirstOrDefaultAsync(x => x.Id == quoteId, cancellationToken);
    if (quote is null)
    {
        return Results.NotFound(new { error = "Pensamiento no encontrado." });
    }

    if (quote.UserId != userId)
    {
        return Results.Forbid();
    }

    try
    {
        await photoStorage.DeleteAsync(quote.PhotoStorageKey, cancellationToken);
        logger.LogInformation("quote.photo_deleted user_id={UserId} quote_id={QuoteId}", userId, quote.Id);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "quote.photo_delete_failed user_id={UserId} quote_id={QuoteId}", userId, quote.Id);
    }

    db.Quotes.Remove(quote);
    await db.SaveChangesAsync(cancellationToken);
    logger.LogInformation("quote.deleted user_id={UserId} quote_id={QuoteId}", userId, quote.Id);
    return Results.NoContent();
}).RequireAuthorization();

app.MapPost("/api/quotes/{quoteId:int}/like", async (
    int quoteId,
    ClaimsPrincipal principal,
    QuotesDbContext db,
    CancellationToken cancellationToken) =>
{
    var userId = GetUserId(principal);
    var quote = await db.Quotes.Include(x => x.User).Include(x => x.Likes).FirstOrDefaultAsync(x => x.Id == quoteId, cancellationToken);
    if (quote is null)
    {
        return Results.NotFound(new { error = "Pensamiento no encontrado." });
    }

    var exists = quote.Likes.Any(x => x.UserId == userId);
    if (!exists)
    {
        quote.Likes.Add(new QuoteLike { QuoteId = quoteId, UserId = userId });
        await db.SaveChangesAsync(cancellationToken);
    }

    return Results.Ok(ToQuoteResponse(quote, userId));
}).RequireAuthorization();

app.MapDelete("/api/quotes/{quoteId:int}/like", async (
    int quoteId,
    ClaimsPrincipal principal,
    QuotesDbContext db,
    CancellationToken cancellationToken) =>
{
    var userId = GetUserId(principal);
    var like = await db.QuoteLikes.FirstOrDefaultAsync(x => x.QuoteId == quoteId && x.UserId == userId, cancellationToken);
    if (like is not null)
    {
        db.QuoteLikes.Remove(like);
        await db.SaveChangesAsync(cancellationToken);
    }

    var quote = await db.Quotes.Include(x => x.User).Include(x => x.Likes).FirstOrDefaultAsync(x => x.Id == quoteId, cancellationToken);
    return quote is null ? Results.NotFound(new { error = "Pensamiento no encontrado." }) : Results.Ok(ToQuoteResponse(quote, userId));
}).RequireAuthorization();

app.Run();

static string ConvertToSqliteConnectionString(string databaseUrl)
{
    const string prefix = "sqlite:///";
    if (databaseUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    {
        var fileName = databaseUrl[prefix.Length..];
        return $"Data Source={fileName}";
    }

    if (databaseUrl.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
    {
        return databaseUrl;
    }

    return "Data Source=quotes_local.db";
}

static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

static int GetUserId(ClaimsPrincipal principal)
{
    var rawUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
    return int.TryParse(rawUserId, out var userId) ? userId : throw new UnauthorizedAccessException("Token invalido.");
}

static UserResponse ToUserResponse(AppUser user) => new(user.Id, user.Email, user.CreatedAt);

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
        LikedByMe: currentUserId.HasValue && quote.Likes.Any(x => x.UserId == currentUserId.Value));
}
