namespace AzureQuotes.Api.Models;

public sealed class QuoteLike
{
    public int QuoteId { get; set; }
    public Quote Quote { get; set; } = null!;

    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
