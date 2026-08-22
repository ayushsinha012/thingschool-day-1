namespace QuotesApi.Models;

public sealed class Quote
{
    private Quote()
    {
        
    }

    private Quote(string author, string text)
    {
        Author = author;
        Text = text;
        IsDeleted = false;
    }

    public int Id { get; private set; }

    public string Author { get; private set; } = string.Empty;

    public string Text { get; private set; } = string.Empty;

    public bool IsDeleted { get; private set; }

    public static Quote Create(string author, string text)
    {
        if (string.IsNullOrWhiteSpace(author))
        {
            throw new ArgumentException(
                "Author is required.",
                nameof(author));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException(
                "Text is required.",
                nameof(text));
        }

        author = author.Trim();
        text = text.Trim();

        if (author.Length > 200)
        {
            throw new ArgumentException(
                "Author must be between 1 and 200 characters.",
                nameof(author));
        }

        if (text.Length > 1000)
        {
            throw new ArgumentException(
                "Text must be between 1 and 1000 characters.",
                nameof(text));
        }

        return new Quote(author, text);
    }

    public void SoftDelete()
    {
        IsDeleted = true;
    }
}