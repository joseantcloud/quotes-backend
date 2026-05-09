namespace AzureQuotes.Api.Models;

public sealed class Quote
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsPublic { get; set; } = true;
    public string? PhotoUrl { get; set; }
    public string? PhotoStorageKey { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public ICollection<QuoteLike> Likes { get; set; } = new List<QuoteLike>();
}
