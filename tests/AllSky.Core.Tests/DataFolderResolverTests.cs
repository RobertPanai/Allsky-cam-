using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace AllSky.Core.Tests;

public sealed class DataFolderResolverTests
{
    // Cartella di default di test: un percorso assoluto qualsiasi, diverso da quelli usati negli argomenti.
    private static readonly string Default = Path.Combine(Path.GetTempPath(), "AllSky-default");

    private static readonly string Custom = Path.Combine(Path.GetTempPath(), "AllSky-custom");

    [Fact]
    public void Argument_WithSpace_IsUsed()
    {
        string result = DataFolderResolver.Resolve(["--data-folder", Custom], null, Default);

        Assert.Equal(Path.GetFullPath(Custom), result);
    }

    [Fact]
    public void Argument_WithEquals_IsUsed()
    {
        string result = DataFolderResolver.Resolve([$"--data-folder={Custom}"], null, Default);

        Assert.Equal(Path.GetFullPath(Custom), result);
    }

    [Fact]
    public void Argument_WithPathContainingSpaces_IsUsed()
    {
        string spaced = Path.Combine(Path.GetTempPath(), "Cartella con spazi");

        string result = DataFolderResolver.Resolve(["--data-folder", spaced], null, Default);

        Assert.Equal(Path.GetFullPath(spaced), result);
    }

    [Fact]
    public void EnvironmentVariable_IsUsedWhenNoArgument()
    {
        string result = DataFolderResolver.Resolve([], Custom, Default);

        Assert.Equal(Path.GetFullPath(Custom), result);
    }

    [Fact]
    public void Argument_HasPriorityOverEnvironmentVariable()
    {
        string envFolder = Path.Combine(Path.GetTempPath(), "AllSky-env");

        string result = DataFolderResolver.Resolve(["--data-folder", Custom], envFolder, Default);

        Assert.Equal(Path.GetFullPath(Custom), result);
    }

    [Fact]
    public void EmptyArgumentValue_FallsBackToEnvironmentVariable()
    {
        string envFolder = Path.Combine(Path.GetTempPath(), "AllSky-env");

        Assert.Equal(Path.GetFullPath(envFolder), DataFolderResolver.Resolve(["--data-folder", ""], envFolder, Default));
        Assert.Equal(Path.GetFullPath(envFolder), DataFolderResolver.Resolve(["--data-folder="], envFolder, Default));
        Assert.Equal(Path.GetFullPath(envFolder), DataFolderResolver.Resolve(["--data-folder", "   "], envFolder, Default));
    }

    [Fact]
    public void MissingArgumentValue_FallsBackToDefault()
    {
        // "--data-folder" in fondo alla lista: nessun valore successivo.
        Assert.Equal(Default, DataFolderResolver.Resolve(["--altro", "--data-folder"], null, Default));
    }

    [Fact]
    public void EmptyEnvironmentVariable_FallsBackToDefault()
    {
        Assert.Equal(Default, DataFolderResolver.Resolve([], "", Default));
        Assert.Equal(Default, DataFolderResolver.Resolve([], "  ", Default));
    }

    [Fact]
    public void InvalidPath_IsIgnoredWithoutException()
    {
        // Il carattere NUL non è ammesso in un percorso: GetFullPath lancia, la risoluzione deve ignorarlo.
        string invalid = "percorso\0non-valido";

        Assert.Equal(Default, DataFolderResolver.Resolve(["--data-folder", invalid], null, Default));
        Assert.Equal(Path.GetFullPath(Custom), DataFolderResolver.Resolve(["--data-folder", invalid, $"--data-folder={Custom}"], null, Default));
        Assert.Equal(Default, DataFolderResolver.Resolve([], invalid, Default));
    }

    [Fact]
    public void RelativePath_BecomesAbsolute()
    {
        string relative = Path.Combine("relativa", "dati");

        string fromArgument = DataFolderResolver.Resolve(["--data-folder", relative], null, Default);
        string fromEnvironment = DataFolderResolver.Resolve([], relative, Default);

        Assert.True(Path.IsPathRooted(fromArgument));
        Assert.Equal(Path.GetFullPath(relative), fromArgument);
        Assert.Equal(Path.GetFullPath(relative), fromEnvironment);
    }

    [Fact]
    public void NoSource_ReturnsDefaultUnchanged()
    {
        Assert.Equal(Default, DataFolderResolver.Resolve([], null, Default));
        Assert.Equal(Default, DataFolderResolver.Resolve(null, null, Default));
        Assert.Equal(Default, DataFolderResolver.Resolve(["--altro", "valore"], null, Default));
    }

    [Fact]
    public void MutexName_ForDefaultFolder_IsUnchanged()
    {
        string name = DataFolderResolver.SingleInstanceMutexName(Default, Default);

        Assert.Equal(@"Local\AllSky.Tray.SingleInstance", name);
    }

    [Fact]
    public void MutexName_ForDefaultFolder_IgnoresCaseAndTrailingSeparator()
    {
        string name = DataFolderResolver.SingleInstanceMutexName(Default.ToUpperInvariant() + Path.DirectorySeparatorChar, Default);

        Assert.Equal(@"Local\AllSky.Tray.SingleInstance", name);
    }

    [Fact]
    public void MutexName_ForOtherFolder_DiffersFromDefaultAndHasHashSuffix()
    {
        string name = DataFolderResolver.SingleInstanceMutexName(Custom, Default);

        Assert.NotEqual(@"Local\AllSky.Tray.SingleInstance", name);
        Assert.StartsWith(@"Local\AllSky.Tray.SingleInstance.", name);
        string suffix = name["Local\\AllSky.Tray.SingleInstance.".Length..];
        Assert.Equal(8, suffix.Length);
        Assert.True(System.Text.RegularExpressions.Regex.IsMatch(suffix, "^[0-9a-f]{8}$"));
    }

    [Fact]
    public void MutexName_HashIsSha256OfLowercaseNormalizedPath()
    {
        string normalized = Path.GetFullPath(Custom).ToLowerInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        string expectedSuffix = Convert.ToHexString(hash)[..8].ToLowerInvariant();

        string name = DataFolderResolver.SingleInstanceMutexName(Custom, Default);

        Assert.Equal(@"Local\AllSky.Tray.SingleInstance." + expectedSuffix, name);
    }

    [Fact]
    public void MutexName_DifferentFolders_GiveDifferentNames()
    {
        string other = Path.Combine(Path.GetTempPath(), "AllSky-altra");

        string first = DataFolderResolver.SingleInstanceMutexName(Custom, Default);
        string second = DataFolderResolver.SingleInstanceMutexName(other, Default);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void MutexName_SamePathDifferentCase_GivesSameName()
    {
        string upper = Custom.ToUpperInvariant();
        string lower = Custom.ToLowerInvariant();

        Assert.Equal(
            DataFolderResolver.SingleInstanceMutexName(upper, Default),
            DataFolderResolver.SingleInstanceMutexName(lower, Default));
    }

    [Fact]
    public void MutexName_InvalidPath_DoesNotThrow()
    {
        string name = DataFolderResolver.SingleInstanceMutexName("percorso\0non-valido", Default);

        Assert.StartsWith(@"Local\AllSky.Tray.SingleInstance.", name);
    }
}
