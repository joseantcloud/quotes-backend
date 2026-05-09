using AzureQuotes.Api.Contracts;

namespace AzureQuotes.Api.Services;

public sealed class FeatureFlagService(IConfiguration configuration)
{
    public FeatureResponse GetFeatures()
    {
        return new FeatureResponse(
            PublicFeedEnabled: GetBool("FEATURE_PUBLIC_FEED_ENABLED", "PUBLIC_FEED", defaultValue: true),
            PhotoUploadEnabled: GetBool("FEATURE_PHOTO_UPLOAD_ENABLED", "PHOTO_UPLOAD", defaultValue: true),
            MaintenanceModeEnabled: GetBool("FEATURE_MAINTENANCEMODE_ENABLED", "MaintenanceMode", defaultValue: false));
    }

    private bool GetBool(string environmentKey, string appConfigLikeKey, bool defaultValue)
    {
        var raw = configuration[environmentKey]
                  ?? configuration[appConfigLikeKey]
                  ?? configuration[$"FeatureManagement:{appConfigLikeKey}"];

        return bool.TryParse(raw, out var parsed) ? parsed : defaultValue;
    }
}
