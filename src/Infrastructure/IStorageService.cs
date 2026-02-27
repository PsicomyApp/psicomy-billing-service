namespace Psicomy.Services.Billing.Infrastructure;

public interface IStorageService
{
    Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType, string folder);
    Task<Stream?> DownloadFileAsync(string storagePath);
    Task DeleteFileAsync(string storagePath);
    Task<string> GetPresignedUrlAsync(string storagePath, int expirationMinutes = 30);
}
