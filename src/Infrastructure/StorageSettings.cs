namespace Psicomy.Services.Billing.Infrastructure;

public class StorageSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string BucketName { get; set; } = "psicomy-billing";
    public string Region { get; set; } = "us-east-1";
    public bool UseSSL { get; set; } = false;
}
