using AllSky.InfoOverlay.Editor;
using Xunit;

namespace AllSky.InfoOverlay.Tests.Editor;

public sealed class MaskLogoStoreTests : IDisposable
{
    private readonly string _radice = Path.Combine(Path.GetTempPath(), "allsky-logo-test-" + Guid.NewGuid().ToString("N"));

    private string Sorgenti => Path.Combine(_radice, "sorgenti");

    private string Loghi => Path.Combine(_radice, "dati", "loghi");

    public void Dispose()
    {
        if (Directory.Exists(_radice))
        {
            Directory.Delete(_radice, recursive: true);
        }
    }

    /// <summary>PNG minimo: firma, blocco IHDR con le dimensioni indicate. Basta per il controllo; non è un'immagine da visualizzare.</summary>
    private static byte[] PngMinimo(uint larghezza, uint altezza, int lunghezzaTotale = 33)
    {
        var dati = new byte[Math.Max(lunghezzaTotale, 33)];
        byte[] firma = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        firma.CopyTo(dati, 0);
        dati[8] = 0; dati[9] = 0; dati[10] = 0; dati[11] = 13;
        dati[12] = (byte)'I'; dati[13] = (byte)'H'; dati[14] = (byte)'D'; dati[15] = (byte)'R';
        Scrivi32(dati, 16, larghezza);
        Scrivi32(dati, 20, altezza);
        return dati;
    }

    private static void Scrivi32(byte[] dati, int offset, uint valore)
    {
        dati[offset] = (byte)(valore >> 24);
        dati[offset + 1] = (byte)(valore >> 16);
        dati[offset + 2] = (byte)(valore >> 8);
        dati[offset + 3] = (byte)valore;
    }

    private string Sorgente(string nome, byte[] dati)
    {
        Directory.CreateDirectory(Sorgenti);
        string percorso = Path.Combine(Sorgenti, nome);
        File.WriteAllBytes(percorso, dati);
        return percorso;
    }

    [Fact]
    public void Importa_un_PNG_valido_lo_copia_nella_cartella_loghi_con_il_nome_pulito()
    {
        string sorgente = Sorgente("Osservatorio Alfa.png", PngMinimo(300, 120));

        MaskLogoImport esito = MaskLogoStore.Importa(sorgente, Loghi);

        Assert.True(esito.Ok);
        Assert.Equal("Osservatorio_Alfa.png", esito.NomeFile);
        Assert.True(File.Exists(Path.Combine(Loghi, "Osservatorio_Alfa.png")));
        Assert.True(File.Exists(sorgente), "il file sorgente non viene spostato né cancellato");
    }

    [Fact]
    public void Importa_due_volte_lo_stesso_file_aggiunge_un_suffisso_e_non_sovrascrive()
    {
        string sorgente = Sorgente("logo.png", PngMinimo(10, 10));

        string primo = MaskLogoStore.Importa(sorgente, Loghi).NomeFile!;
        string secondo = MaskLogoStore.Importa(sorgente, Loghi).NomeFile!;

        Assert.Equal("logo.png", primo);
        Assert.Equal("logo-2.png", secondo);
    }

    [Fact]
    public void Estensione_diversa_da_png_viene_rifiutata()
    {
        string sorgente = Sorgente("logo.jpg", PngMinimo(10, 10));

        Assert.Equal(MaskLogoErrore.EstensioneNonPng, MaskLogoStore.Importa(sorgente, Loghi).Errore);
    }

    [Fact]
    public void File_con_estensione_png_ma_senza_firma_viene_rifiutato()
    {
        string sorgente = Sorgente("finto.png", new byte[64]);

        Assert.Equal(MaskLogoErrore.FirmaNonPng, MaskLogoStore.Importa(sorgente, Loghi).Errore);
    }

    [Fact]
    public void File_troppo_grande_viene_rifiutato_prima_della_lettura()
    {
        string sorgente = Sorgente("grande.png", new byte[MaskLogoStore.DimensioneMassimaByte + 1]);

        Assert.Equal(MaskLogoErrore.TroppoGrande, MaskLogoStore.Importa(sorgente, Loghi).Errore);
        Assert.False(Directory.Exists(Loghi));
    }

    [Fact]
    public void Dimensioni_oltre_il_limite_vengono_rifiutate()
    {
        string sorgente = Sorgente("enorme.png", PngMinimo((uint)MaskLogoStore.LatoMassimoPx + 1, 100));

        Assert.Equal(MaskLogoErrore.DimensioniTroppoGrandi, MaskLogoStore.Importa(sorgente, Loghi).Errore);
    }

    [Fact]
    public void Dimensioni_zero_non_sono_leggibili()
    {
        string sorgente = Sorgente("zero.png", PngMinimo(0, 100));

        Assert.Equal(MaskLogoErrore.NonLeggibile, MaskLogoStore.Importa(sorgente, Loghi).Errore);
    }

    [Fact]
    public void File_inesistente_viene_segnalato()
    {
        Assert.Equal(
            MaskLogoErrore.FileMancante,
            MaskLogoStore.Importa(Path.Combine(_radice, "non-esiste.png"), Loghi).Errore);
    }

    [Fact]
    public void Nome_sorgente_con_caratteri_non_ammessi_viene_ripulito()
    {
        string sorgente = Sorgente("..cartella..esterna  (1).png", PngMinimo(10, 10));

        string nome = MaskLogoStore.Importa(sorgente, Loghi).NomeFile!;

        Assert.True(OverlayWidgetFormats.FileLogoValido(nome));
        Assert.DoesNotContain("..", nome);
        Assert.DoesNotContain(' ', nome);
    }

    [Fact]
    public void Nome_base_molto_lungo_viene_accorciato()
    {
        string nome = MaskLogoStore.NomeBase(new string('a', 200));

        Assert.Equal(60, nome.Length);
    }

    [Fact]
    public void Elenco_restituisce_solo_i_png_in_ordine_alfabetico()
    {
        Directory.CreateDirectory(Loghi);
        File.WriteAllBytes(Path.Combine(Loghi, "b.png"), PngMinimo(10, 10));
        File.WriteAllBytes(Path.Combine(Loghi, "a.PNG"), PngMinimo(10, 10));
        File.WriteAllText(Path.Combine(Loghi, "note.txt"), "x");

        Assert.Equal(new[] { "a.PNG", "b.png" }, MaskLogoStore.Elenco(Loghi));
    }

    [Fact]
    public void Elenco_di_cartella_assente_e_vuoto()
    {
        Assert.Empty(MaskLogoStore.Elenco(Loghi));
    }

    [Fact]
    public void Pulisci_elimina_solo_i_png_non_usati()
    {
        Directory.CreateDirectory(Loghi);
        File.WriteAllBytes(Path.Combine(Loghi, "usato.png"), PngMinimo(10, 10));
        File.WriteAllBytes(Path.Combine(Loghi, "orfano.png"), PngMinimo(10, 10));
        File.WriteAllText(Path.Combine(Loghi, "lascia.txt"), "resta");

        IReadOnlyList<string> eliminati = MaskLogoStore.PulisciNonUsati(Loghi, new[] { "USATO.png" });

        Assert.Equal(new[] { "orfano.png" }, eliminati);
        Assert.True(File.Exists(Path.Combine(Loghi, "usato.png")));
        Assert.False(File.Exists(Path.Combine(Loghi, "orfano.png")));
        Assert.True(File.Exists(Path.Combine(Loghi, "lascia.txt")));
    }

    [Fact]
    public void Pulisci_su_cartella_assente_non_crea_nulla()
    {
        Assert.Empty(MaskLogoStore.PulisciNonUsati(Loghi, Array.Empty<string>()));
        Assert.False(Directory.Exists(Loghi));
    }

    [Fact]
    public void Il_logo_importato_passa_la_validazione_del_layout_come_nome_di_file()
    {
        string sorgente = Sorgente("ok.png", PngMinimo(40, 40));
        string nome = MaskLogoStore.Importa(sorgente, Loghi).NomeFile!;

        var stato = new MaskEditorState();
        stato.AggiungiLogo(nome, OverlayPosition.TR);

        Assert.Empty(stato.Valida());
    }
}
