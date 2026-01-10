using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Psicomy.Services.Billing.Infrastructure;

public class S3StorageService : IStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly StorageSettings _settings;
    private readonly ILogger<S3StorageService> _logger;

    public S3StorageService(IAmazonS3 s3Client, IOptions<StorageSettings> settings, ILogger<S3StorageService> logger)
    {
        _s3Client = s3Client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType, string folder)
    {
        var key = $"{folder}/{Guid.NewGuid()}/{fileName}";

        try
        {
            await EnsureBucketExistsAsync();

            var request = new PutObjectRequest
            {
                BucketName = _settings.BucketName,
                Key = key,
                InputStream = fileStream,
                ContentType = contentType
            };

            await _s3Client.PutObjectAsync(request);
            _logger.LogInformation("File uploaded to S3: {Key}", key);

            return key;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file to S3: {FileName}", fileName);
            throw;
        }
    }

    public async Task<Stream?> DownloadFileAsync(string storagePath)
    {
        try
        {
            var request = new GetObjectRequest
            {
                BucketName = _settings.BucketName,
                Key = storagePath
            };

            var response = await _s3Client.GetObjectAsync(request);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("File not found in S3: {StoragePath}", storagePath);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file from S3: {StoragePath}", storagePath);
            throw;
        }
    }

    public async Task DeleteFileAsync(string storagePath)
    {
        try
        {
            var request = new DeleteObjectRequest
            {
                BucketName = _settings.BucketName,
                Key = storagePath
            };

            await _s3Client.DeleteObjectAsync(request);
            _logger.LogInformation("File deleted from S3: {StoragePath}", storagePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file from S3: {StoragePath}", storagePath);
            throw;
        }
    }

    public Task<string> GetPresignedUrlAsync(string storagePath, int expirationMinutes = 30)
    {
        try
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = _settings.BucketName,
                Key = storagePath,
                Expires = DateTime.UtcNow.AddMinutes(expirationMinutes),
                Verb = HttpVerb.GET
            };

            var url = _s3Client.GetPreSignedURL(request);
            return Task.FromResult(url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate presigned URL for: {StoragePath}", storagePath);
            throw;
        }
    }

    private async Task EnsureBucketExistsAsync()
    {
        try
        {
            await _s3Client.PutBucketAsync(new PutBucketRequest
            {
                BucketName = _settings.BucketName,
                UseClientRegion = true
            });
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "BucketAlreadyOwnedByYou" || ex.ErrorCode == "BucketAlreadyExists")
        {
            // Bucket already exists, ignore
        }
    }
}
