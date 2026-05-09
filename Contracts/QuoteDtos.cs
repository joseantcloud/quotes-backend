using System.Text.Json.Serialization;

namespace AzureQuotes.Api.Contracts;

public sealed record QuoteResponse(
    [property: JsonPropertyName("quote_id")] int QuoteId,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("is_public")] bool IsPublic,
    [property: JsonPropertyName("photo_url")] string? PhotoUrl,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTime UpdatedAt,
    [property: JsonPropertyName("owner_email")] string OwnerEmail,
    [property: JsonPropertyName("likes_count")] int LikesCount,
    [property: JsonPropertyName("liked_by_me")] bool LikedByMe);
