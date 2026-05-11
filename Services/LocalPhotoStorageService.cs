using System.Diagnostics;
using Microsoft.ApplicationInsights;

namespace AzureQuotes.Api.Services;

public sealed class LocalPhotoStorageService(IConfiguration configuration, IWebHostEnvironment environment, TelemetryClient telemetry) : IPhotoStorageService
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif"
    };

    public async Task<StoredPhoto> SaveAsync(IFormFile photo, CancellationToken cancellationToken)
    {
        ValidatePhoto(photo, configuration);

        var uploadFolder = configuration["UPLOAD_FOLDER"] ?? "uploads";
        var absoluteFolder = Path.Combine(environment.ContentRootPath, uploadFolder);
        Directory.CreateDirectory(absoluteFolder);

        var extension = Path.GetExtension(photo.FileName);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var absolutePath = Path.Combine(absoluteFolder, fileName);

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        await using var fileStream = File.Create(absolutePath);
        await photo.CopyToAsync(fileStream, cancellationToken);
        stopwatch.Stop();

        telemetry.TrackDependency(
            dependencyTypeName: "Local File System",
            target: absoluteFolder,
            dependencyName: "WriteFile",
            data: fileName,
            startTime: startedAt,
            duration: stopwatch.Elapsed,
            resultCode: "0",
            success: true);

        telemetry.TrackEvent("photo.storage.local.saved", new Dictionary<string, string>
        {
            ["file_name"] = fileName,
            ["size_bytes"] = photo.Length.ToString(),
            ["content_type"] = photo.ContentType
        });

        var backendBaseUrl = (configuration["BACKEND_BASE_URL"] ?? "http://localhost:5000").TrimEnd('/');
        var url = $"{backendBaseUrl}/{uploadFolder}/{fileName}";
        return new StoredPhoto(url, $"local:{uploadFolder}/{fileName}");
    }

    public Task DeleteAsync(string? storageKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || !storageKey.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        var relativePath = storageKey["local:".Length..].Replace('/', Path.DirectorySeparatorChar);
        var absolutePath = Path.Combine(environment.ContentRootPath, relativePath);
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }

        stopwatch.Stop();

        telemetry.TrackDependency(
            dependencyTypeName: "Local File System",
            target: Path.GetDirectoryName(absolutePath) ?? environment.ContentRootPath,
            dependencyName: "DeleteFile",
            data: absolutePath,
            startTime: startedAt,
            duration: stopwatch.Elapsed,
            resultCode: "0",
            success: true);

        return Task.CompletedTask;
    }

    public static void ValidatePhoto(IFormFile photo, IConfiguration configuration)
    {
        if (photo.Length <= 0)
        {
            throw new InvalidOperationException("La foto esta vacia.");
        }

        if (!AllowedContentTypes.Contains(photo.ContentType))
        {
            throw new InvalidOperationException("Tipo de archivo no permitido. Usa jpg, png, webp o gif.");
        }

        var maxMb = int.TryParse(configuration["MAX_PHOTO_MB"], out var parsed) ? parsed : 4;
        var maxBytes = maxMb * 1024L * 1024L;
        if (photo.Length > maxBytes)
        {
            throw new InvalidOperationException($"La foto supera el limite de {maxMb} MB.");
        }
    }
}
