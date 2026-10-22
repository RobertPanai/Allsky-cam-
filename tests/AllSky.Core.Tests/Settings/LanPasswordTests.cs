using System.Text;
using AllSky.Core.Settings;
using Xunit;

namespace AllSky.Core.Tests.Settings;

public sealed class LanPasswordTests
{
    [Fact]
    public void Check_RifiutaVuotaEBreve()
    {
        Assert.Equal(LanPasswordProblem.Empty, LanPassword.Check(null));
        Assert.Equal(LanPasswordProblem.Empty, LanPassword.Check("   "));
        Assert.Equal(LanPasswordProblem.TooShort, LanPassword.Check("1234567"));
        Assert.Equal(LanPasswordProblem.None, LanPassword.Check("12345678"));
    }

    [Fact]
    public void Hash_NonContieneLaPasswordInChiaroEdEVerificabile()
    {
        string hash = LanPassword.Hash("cielo-stellato");

        Assert.False(hash.Contains("cielo-stellato", StringComparison.Ordinal));
        Assert.True(hash.StartsWith("pbkdf2-sha256$", StringComparison.Ordinal));
        Assert.True(LanPassword.Verify("cielo-stellato", hash));
        Assert.False(LanPassword.Verify("cielo-stellat0", hash));
    }

    [Fact]
    public void Hash_UsaSaleDiversoOgniVolta()
    {
        string first = LanPassword.Hash("stessa-parola");
        string second = LanPassword.Hash("stessa-parola");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Hash_RifiutaPasswordNonAccettabile()
    {
        Assert.Throws<ArgumentException>(() => LanPassword.Hash("corta"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("non-un-hash")]
    [InlineData("pbkdf2-sha256$abc$AAAA$BBBB")]
    [InlineData("pbkdf2-sha256$1000$non-base64!$AAAA")]
    [InlineData("altro$600000$AAAA$BBBB")]
    public void Verify_HashMalformatoOMancante_NonAccetta(string? stored)
    {
        Assert.False(LanPassword.Verify("qualsiasi-cosa", stored));
    }

    [Fact]
    public void Verify_PasswordVuota_NonAccetta()
    {
        string hash = LanPassword.Hash("parola-valida");

        Assert.False(LanPassword.Verify(string.Empty, hash));
        Assert.False(LanPassword.Verify(null, hash));
    }

    [Fact]
    public void TryReadBasicPassword_LeggeLaPasswordDopoIDuePunti()
    {
        string header = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("robert:pa:ss"));

        Assert.True(LanPassword.TryReadBasicPassword(header, out string password));
        Assert.Equal("pa:ss", password);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer abc")]
    [InlineData("Basic ###")]
    public void TryReadBasicPassword_IntestazioneNonValida_Falso(string? header)
    {
        Assert.False(LanPassword.TryReadBasicPassword(header, out string password));
        Assert.Equal(string.Empty, password);
    }

    [Fact]
    public void TryReadBasicPassword_SenzaDuePunti_Falso()
    {
        string header = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("solo-utente"));

        Assert.False(LanPassword.TryReadBasicPassword(header, out _));
    }
}
