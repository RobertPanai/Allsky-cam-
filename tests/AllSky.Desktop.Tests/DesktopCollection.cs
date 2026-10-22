using Xunit;

namespace AllSky.Desktop.Tests;

/// <summary>
/// Collection condivisa dei test UI: un solo <see cref="DesktopFixture"/> per tutto il processo di test.
/// WPF consente una sola <c>Application</c> per AppDomain, quindi due fixture separate fanno fallire tutti i casi.
/// </summary>
[CollectionDefinition(DesktopCollection.Name)]
public sealed class DesktopCollection : ICollectionFixture<DesktopFixture>
{
    /// <summary>Nome della collection, da usare nell'attributo <see cref="CollectionAttribute"/> di ogni classe UI.</summary>
    public const string Name = "Desktop UI";
}
