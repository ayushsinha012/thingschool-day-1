using QuotesApi.Models;

namespace QuotesApi.Services;

public sealed class QuoteFormatter
{
    public string Format(Quote quote)
    {
        ArgumentNullException.ThrowIfNull(quote);

        return $"\"{quote.Text}\" — {quote.Author}";
    }
}
