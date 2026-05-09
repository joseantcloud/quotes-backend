using System.Text.Json.Serialization;

namespace AzureQuotes.Api.Contracts;

public sealed record FeatureResponse(
    [property: JsonPropertyName("public_feed_enabled")] bool PublicFeedEnabled,
    [property: JsonPropertyName("photo_upload_enabled")] bool PhotoUploadEnabled,
    [property: JsonPropertyName("maintenance_mode_enabled")] bool MaintenanceModeEnabled);
