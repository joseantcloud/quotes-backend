using System.Diagnostics;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.ApplicationInsights;

namespace AzureQuotes.Api.Services;

public sealed class AzureBlobPhotoStorageService(IConfiguration configuration, TelemetryClient telemetry) : IPhotoStorageService
{
    public async Task<StoredPhoto> SaveAsync(IFormFile photo, CancellationToken cancellationToken)
    {
        LocalPhotoStorageService.ValidatePhoto(photo, configuration);

        var container = await GetContainerClientAsync(cancellationToken);
        var extension = Path.GetExtension(photo.FileName);
        var blobName = $"quotes/{DateTimeOffset.UtcNow:yyyy/MM/dd}/{Guid.NewGuid():N}{extension}";
        var blob = container.GetBlobClient(blobName);

        await using var stream = photo.OpenReadStream();
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        await blob.UploadAsync(stream, new BlobHttpHeaders { ContentType = photo.ContentType }, cancellationToken: cancellationToken);
        stopwatch.Stop();

        telemetry.TrackDependency(
            dependencyTypeName: "Azure Blob Storage",
            target: container.Uri.Host,
            dependencyName: "UploadBlob",
            data: blobName,
            startTime: startedAt,
            duration: stopwatch.Elapsed,
            resultCode: "0",
            success: true);

        telemetry.TrackEvent("photo.storage.azure.saved", new Dictionary<string, string>
        {
            ["blob_name"] = blobName,
            ["content_type"] = photo.ContentType,
            ["size_bytes"] = photo.Length.ToString()
        });

        var publicBaseUrl = configuration["AZURE_STORAGE_PUBLIC_BASE_URL"]?.TrimEnd('/');
        var url = string.IsNullOrWhiteSpace(publicBaseUrl) ? blob.Uri.ToString() : $"{publicBaseUrl}/{blobName}";

        return new StoredPhoto(url, $"azure:{blobName}");
    }

    public async Task DeleteAsync(string? storageKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || !storageKey.StartsWith("azure:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var blobName = storageKey["azure:".Length..];
        var container = await GetContainerClientAsync(cancellationToken);
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        await container.DeleteBlobIfExistsAsync(blobName, cancellationToken: cancellationToken);
        stopwatch.Stop();

        telemetry.TrackDependency(
            dependencyTypeName: "Azure Blob Storage",
            target: container.Uri.Host,
            dependencyName: "DeleteBlob",
            data: blobName,
            startTime: startedAt,
            duration: stopwatch.Elapsed,
            resultCode: "0",
            success: true);

        telemetry.TrackEvent("photo.storage.azure.deleted", new Dictionary<string, string>
        {
            ["blob_name"] = blobName
        });
    }

    private async Task<BlobContainerClient> GetContainerClientAsync(CancellationToken cancellationToken)
    {
        var containerName = configuration["AZURE_STORAGE_CONTAINER_NAME"] ?? "quote-photos";
        BlobContainerClient container;

        var connectionString = configuration["AZURE_STORAGE_CONNECTION_STRING"];
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            container = new BlobContainerClient(connectionString, containerName);
        }
        else
        {
            var accountUrl = configuration["AZURE_STORAGE_ACCOUNT_URL"];
            if (string.IsNullOrWhiteSpace(accountUrl))
            {
                throw new InvalidOperationException("Configure AZURE_STORAGE_CONNECTION_STRING or AZURE_STORAGE_ACCOUNT_URL.");
            }

            container = new BlobServiceClient(new Uri(accountUrl), new DefaultAzureCredential()).GetBlobContainerClient(containerName);
        }

        await container.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);
        return container;
    }
}
