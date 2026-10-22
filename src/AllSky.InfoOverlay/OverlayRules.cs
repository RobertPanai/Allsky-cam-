using System.Globalization;

namespace AllSky.InfoOverlay;

/// <summary>Limiti dei valori. Usati sia dalla validazione sia dalla normalizzazione.</summary>
public static class OverlayLimits
{
    public const double CentroMin = 0;
    public const double CentroMax = 1;
    public const double RaggioMin = 0.01;
    public const double RaggioMax = 1;
    public const double SfumaturaMax = 0.2;
    public const double BandaMax = 0.5;
    public const double ScalaTestoMin = 0.005;
    public const double ScalaTestoMax = 0.1;
    public const double MargineMax = 0.1;
    public const int OrdineMax = 99;
    public const int MaxElementiMin = 1;
    public const int MaxElementiMax = 10;
    public const int DecimaliMax = 2;
    public const double AltezzaLogoMin = 1;
    public const double AltezzaLogoMax = 50;
    public const double OpacitaMin = 0;
    public const double OpacitaMax = 1;
    public const int PatternMaxLength = 40;
    public const int TestoMaxLength = 120;
    public const int FileLogoMaxLength = 120;
}

/// <summary>Campi di <see cref="OverlayWidgetFormat"/>, come flag per indicare quali sono previsti da un tipo.</summary>
[Flags]
public enum OverlayFormatField
{
    Nessuno = 0,
    Pattern = 1 << 0,
    Fuso = 1 << 1,
    Decimali = 1 << 2,
    MostraFase = 1 << 3,
    MaxElementi = 1 << 4,
    Campi = 1 << 5,
    FileLogo = 1 << 6,
    AltezzaMaxPercento = 1 << 7,
    Opacita = 1 << 8,
    Testo = 1 << 9,
    MostraGain = 1 << 10,
}

public static class OverlayWidgetKinds
{
    /// <summary>Campi di formato validi per ciascun tipo di widget. Tutti gli altri devono restare null.</summary>
    public static OverlayFormatField CampiPrevisti(OverlayWidgetKind tipo) => tipo switch
    {
        OverlayWidgetKind.DataOra => OverlayFormatField.Pattern | OverlayFormatField.Fuso,
        OverlayWidgetKind.Sole => OverlayFormatField.Decimali,
        OverlayWidgetKind.Luna => OverlayFormatField.Decimali | OverlayFormatField.MostraFase,
        OverlayWidgetKind.Satelliti => OverlayFormatField.MaxElementi,
        OverlayWidgetKind.Meteo => OverlayFormatField.Campi,
        OverlayWidgetKind.Logo => OverlayFormatField.FileLogo | OverlayFormatField.AltezzaMaxPercento | OverlayFormatField.Opacita,
        OverlayWidgetKind.TestoLibero => OverlayFormatField.Testo,
        OverlayWidgetKind.Esposizione => OverlayFormatField.MostraGain,
        _ => OverlayFormatField.Nessuno,
    };

    /// <summary>Solo loghi ed etichette libere possono comparire più volte.</summary>
    public static bool AmmetteIstanzeMultiple(OverlayWidgetKind tipo) =>
        tipo is OverlayWidgetKind.Logo or OverlayWidgetKind.TestoLibero;
}

public static class OverlayWidgetFormats
{
    /// <summary>Formato completo con i valori predefiniti per il tipo indicato.</summary>
    public static OverlayWidgetFormat Predefinito(OverlayWidgetKind tipo) => tipo switch
    {
        OverlayWidgetKind.DataOra => new OverlayWidgetFormat(Pattern: "yyyy-MM-dd HH:mm:ss", Fuso: OverlayTimeZone.Locale),
        OverlayWidgetKind.Sole => new OverlayWidgetFormat(Decimali: 0),
        OverlayWidgetKind.Luna => new OverlayWidgetFormat(Decimali: 0, MostraFase: true),
        OverlayWidgetKind.Satelliti => new OverlayWidgetFormat(MaxElementi: 3),
        OverlayWidgetKind.Meteo => new OverlayWidgetFormat(Campi: new[]
        {
            OverlayMeteoField.Temperatura,
            OverlayMeteoField.Umidita,
            OverlayMeteoField.Vento,
        }),
        OverlayWidgetKind.Logo => new OverlayWidgetFormat(FileLogo: "logo.png", AltezzaMaxPercento: 8, Opacita: 0.9),
        OverlayWidgetKind.TestoLibero => new OverlayWidgetFormat(Testo: "Osservatorio"),
        OverlayWidgetKind.Esposizione => new OverlayWidgetFormat(MostraGain: true),
        _ => new OverlayWidgetFormat(),
    };

    public static bool Presente(OverlayWidgetFormat formato, OverlayFormatField campo) => campo switch
    {
        OverlayFormatField.Pattern => formato.Pattern is not null,
        OverlayFormatField.Fuso => formato.Fuso is not null,
        OverlayFormatField.Decimali => formato.Decimali is not null,
        OverlayFormatField.MostraFase => formato.MostraFase is not null,
        OverlayFormatField.MaxElementi => formato.MaxElementi is not null,
        OverlayFormatField.Campi => formato.Campi is not null,
        OverlayFormatField.FileLogo => formato.FileLogo is not null,
        OverlayFormatField.AltezzaMaxPercento => formato.AltezzaMaxPercento is not null,
        OverlayFormatField.Opacita => formato.Opacita is not null,
        OverlayFormatField.Testo => formato.Testo is not null,
        OverlayFormatField.MostraGain => formato.MostraGain is not null,
        _ => false,
    };

    /// <summary>Un formato di DataOra è valido se il pattern è lungo al massimo 40 caratteri e .NET lo accetta.</summary>
    public static bool PatternValido(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > OverlayLimits.PatternMaxLength)
        {
            return false;
        }

        try
        {
            _ = DateTime.UnixEpoch.ToString(pattern, CultureInfo.InvariantCulture);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Il logo è solo il nome di un file PNG, che deve stare nella cartella loghi della cartella dati.
    /// Non sono ammessi percorsi, separatori o riferimenti a cartelle esterne.
    /// </summary>
    public static bool FileLogoValido(string? nomeFile)
    {
        if (string.IsNullOrWhiteSpace(nomeFile) || nomeFile.Length > OverlayLimits.FileLogoMaxLength)
        {
            return false;
        }

        if (nomeFile.Contains('/') || nomeFile.Contains('\\') || nomeFile.Contains("..") || nomeFile.Contains(':'))
        {
            return false;
        }

        if (nomeFile.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        return nomeFile.Length > 4 && nomeFile.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TestoValido(string? testo) =>
        !string.IsNullOrWhiteSpace(testo) && testo.Length <= OverlayLimits.TestoMaxLength;
}
