using Xunit;

namespace AllSky.Publish.Tests;

/// <summary>Test del sanificatore dei testi: segreti, URL, caratteri di controllo e lunghezza.</summary>
public class RedactorTests
{
    private static Redactor With(params string?[] secrets)
        => new(() => secrets.Where(s => s is not null).Select(s => s!).ToArray());

    [Fact]
    public void Sanitize_Null_ReturnsEmptyString()
    {
        Assert.Equal("", With(UplinkTestSupport.TokenValue).Sanitize(null));
    }

    [Fact]
    public void Sanitize_ReplacesSecret_InRawAndUrlEncodedForm()
    {
        const string secret = "ab+c/d=e&f";
        string encoded = Uri.EscapeDataString(secret);
        Redactor redactor = With(secret);

        string result = redactor.Sanitize($"raw {secret} and encoded {encoded} end");

        Assert.Equal("raw *** and encoded *** end", result);
    }

    [Fact]
    public void Sanitize_IgnoresEmptySecrets()
    {
        Assert.Equal("testo qualsiasi", With("", null).Sanitize("testo qualsiasi"));
    }

    [Fact]
    public void Sanitize_LongerSecretIsReplacedFirst()
    {
        // "abc" e' contenuto in "abcdef": il segreto piu' lungo deve sparire per intero.
        Assert.Equal("x***x", With("abc", "abcdef").Sanitize("xabcdefx"));
    }

    [Fact]
    public void Sanitize_StripsQueryAndFragment()
    {
        string result = With().Sanitize("errore su https://uplink.test/api/uplink/allsky?token=abc&x=1#frag fine");

        Assert.Equal("errore su https://uplink.test/api/uplink/allsky fine", result);
    }

    [Fact]
    public void Sanitize_StripsUserInfo()
    {
        string result = With().Sanitize("connessione a https://user:pass@uplink.test/x non riuscita");

        Assert.Equal("connessione a https://uplink.test/x non riuscita", result);
        Assert.DoesNotContain("user:pass", result);
    }

    [Fact]
    public void Sanitize_MasksBearerAndAuthorizationHeader()
    {
        Assert.Equal("Authorization: ***", With().Sanitize("Authorization: Bearer abc123"));
        Assert.Equal("errore Bearer *** end", With().Sanitize("errore Bearer xyz end"));
    }

    [Fact]
    public void Sanitize_RemovesControlCharacters()
    {
        // Tab e a capo diventano spazi; gli altri caratteri di controllo (BEL) vengono rimossi.
        Assert.Equal("riga1 riga2 fine", With().Sanitize("riga1\nriga2\tfine"));
        Assert.Equal("ab", With().Sanitize("a\u0007b"));
    }

    [Fact]
    public void Sanitize_TruncatesTo300Characters()
    {
        string result = With().Sanitize(new string('a', 1000));

        Assert.Equal(300, result.Length);
        Assert.EndsWith("...", result);
    }

    [Fact]
    public void Sanitize_TruncationDoesNotSplitSurrogatePair()
    {
        // Il carattere che cadrebbe sul taglio e' una coppia di surrogati: il taglio deve arretrare.
        string input = new string('a', 296) + "\U0001F600" + new string('b', 50);

        string result = With().Sanitize(input);

        Assert.True(result.Length <= 300);
        Assert.EndsWith("...", result);
        Assert.False(char.IsHighSurrogate(result[^4]));
    }

    [Fact]
    public void Sanitize_ProviderFailure_NeverThrowsAndHidesText()
    {
        var redactor = new Redactor(() => throw new InvalidOperationException("provider rotto"));

        string result = redactor.Sanitize("token tok-SECRET-0001 in chiaro");

        Assert.NotNull(result);
        Assert.DoesNotContain("tok-SECRET-0001", result);
    }

    [Fact]
    public void Sanitize_NullProvider_ResultIsSanitizedWithoutSecrets()
    {
        var redactor = new Redactor(() => null!);

        Assert.Equal("ok", redactor.Sanitize("ok"));
    }
}
