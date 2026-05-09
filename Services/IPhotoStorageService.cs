namespace AzureQuotes.Api.Services;

public sealed record StoredPhoto(string Url, string StorageKey);

public interface IPhotoStorageService
{
    Task<StoredPhoto> SaveAsync(IFormFile photo, CancellationToken cancellationToken);
    Task DeleteAsync(string? storageKey, CancellationToken cancellationToken);
}
