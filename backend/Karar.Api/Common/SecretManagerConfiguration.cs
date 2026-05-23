using Google.Cloud.SecretManager.V1;
using Microsoft.Extensions.Configuration;

namespace Karar.Api.Common;

public static class SecretManagerConfiguration
{
    public static IConfigurationBuilder AddGcpSecrets(this IConfigurationBuilder builder, string projectId)
    {
        if (string.IsNullOrEmpty(projectId)) return builder;

        try
        {
            var client = SecretManagerServiceClient.Create();
            var secrets = client.ListSecrets(new ListSecretsRequest
            {
                Parent = $"projects/{projectId}"
            });

            var secretData = new Dictionary<string, string?>();

            foreach (var secret in secrets)
            {
                var secretName = secret.SecretName.SecretId;
                // Convert Secret Manager name format (e.g. ConnectionStrings--Postgres)
                // to ASP.NET Core format (ConnectionStrings:Postgres)
                var configKey = secretName.Replace("--", ":");

                try
                {
                    var version = client.AccessSecretVersion(new AccessSecretVersionRequest
                    {
                        SecretVersionName = new SecretVersionName(projectId, secretName, "latest")
                    });

                    secretData[configKey] = version.Payload.Data.ToStringUtf8();
                }
                catch
                {
                    // Ignore secrets we can't access or that don't have a 'latest' version
                }
            }

            builder.AddInMemoryCollection(secretData);
        }
        catch
        {
            // Silently fail if GCP credentials are not available (e.g. local dev)
        }

        return builder;
    }
}
