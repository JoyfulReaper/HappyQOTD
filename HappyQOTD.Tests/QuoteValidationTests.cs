using HappyQOTD.Quotes;

namespace HappyQOTD.Tests;

public sealed class QuoteValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateQuote_RejectsMissingOrWhitespaceText(string? text)
    {
        var errors = QuoteValidator.ValidateQuote(new CreateQuoteRequest(text, null, null));

        Assert.Equal(["Quote text is required."], errors["text"]);
    }

    [Fact]
    public void ValidateQuote_AllowsTextAtMaximumLength()
    {
        var errors = QuoteValidator.ValidateQuote(
            new CreateQuoteRequest(new string('a', 1_000), null, null));

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateQuote_RejectsTextAboveMaximumLength()
    {
        var errors = QuoteValidator.ValidateQuote(
            new CreateQuoteRequest(new string('a', 1_001), null, null));

        Assert.Equal(["Quote text cannot exceed 1,000 characters."], errors["text"]);
    }

    [Fact]
    public void ValidateQuote_AllowsAuthorAtMaximumLength()
    {
        var errors = QuoteValidator.ValidateQuote(
            new CreateQuoteRequest("Quote", new string('a', 200), null));

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateQuote_RejectsAuthorAboveMaximumLength()
    {
        var errors = QuoteValidator.ValidateQuote(
            new CreateQuoteRequest("Quote", new string('a', 201), null));

        Assert.Equal(["Author cannot exceed 200 characters."], errors["author"]);
    }

    [Fact]
    public void ValidateQuote_AllowsSourceAtMaximumLength()
    {
        var errors = QuoteValidator.ValidateQuote(
            new CreateQuoteRequest("Quote", null, new string('s', 300)));

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateQuote_RejectsSourceAboveMaximumLength()
    {
        var errors = QuoteValidator.ValidateQuote(
            new CreateQuoteRequest("Quote", null, new string('s', 301)));

        Assert.Equal(["Source cannot exceed 300 characters."], errors["source"]);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public void ValidateQuote_AllowsNullOrEmptyOptionalFields(string? author, string? source)
    {
        var errors = QuoteValidator.ValidateQuote(
            new CreateQuoteRequest("Quote", author, source));

        Assert.Empty(errors);
    }
}
