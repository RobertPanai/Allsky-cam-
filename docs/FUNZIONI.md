# Funzioni del progetto AllSky

AllSky è un motore locale per una camera all-sky: cattura i frame, raggruppa le immagini per notte e crea timelapse, keogram e startrails. Il motore è scritto in C# e si divide in tre librerie: **Core** (configurazione, tipi di frame e di camera, interfacce), **Astro** (posizione del Sole e della Luna, fasi del cielo, notte osservativa) e **Imaging** (demosaicing, bilanciamento del bianco, stretch e levigatura). L'interfaccia web è in JavaScript a moduli, in `web/js` (servizi comuni) e `web/js/views` (schermate); `web/mock/server.mjs` è un motore di prova con dati finti che implementa lo stesso contratto HTTP.

Convenzioni del documento:

- **Riga**: la prima riga della dichiarazione (firma), non quella del commento XML o JSDoc.
- **Funzioni anonime** (callback, metodi di oggetti restituiti come `focus` o `refresh`): sono descritte nella voce della funzione che le crea, con la loro riga.
- **Enum, interfacce, costanti e tabelle**: sono descritti nelle note della sezione, non contati come funzioni. Le proprietà con logica (getter, proprietà statiche calcolate) sono contate.
- **Costruttori**: quelli espliciti sono voci a sé; per i `record` il costruttore implicito è descritto come tale.
- **Da verificare**: elencate in fondo al documento.

---

## AllSky.Core (src/AllSky.Core)

### AppConfig.cs

Contiene l'enum `Language` (`It`, `En`), `WhiteBalanceMode`, `Stretch` (`Mtf`, `Asinh`) e `Theme` (`Auto`, `Day`, `Night`, `Red`), che si serializzano come stringhe camelCase. Il `record AppConfig` rispecchia il JSON della configurazione (sezioni `site`, `camera`, `color`, `mask`, `overlay`, `capture`, `products`, `storage`, `ui`) e ha i valori di default dell'esempio.

#### AppConfig

- **AppConfig** (costruttore implicito, riga 37) — `AppConfig()`. Crea una configurazione con tutti i valori di default: sito "Osservatorio" a 45.07, 7.69 (quota 240 m, fuso Europe/Rome), camera `sim-1`, guadagno 250, cattura ogni 30 secondi, porta 5810, accesso dalla rete locale spento. Le sezioni annidate `SiteConfig` (riga 61), `CameraConfig` (74), `ColorConfig` (87), `MaskConfig` (102), `OverlayConfig` (109), `CaptureConfig` (120), `ProductsConfig` (127), `StorageConfig` (138) e `UiConfig` (146) sono record con costruttore implicito e proprietà `init`. La cartella di default di `StorageConfig` è `AllSky` nella cartella utente.

### CameraInfo.cs

#### CameraInfo

- **CameraInfo** (costruttore del record posizionale, riga 4) — `CameraInfo(int Width, int Height, BayerPattern Pattern, int MaxAdu, double PixelSizeUm, bool CanSetGain, double MinExposureMs, double MaxExposureMs)`. Descrive le caratteristiche statiche di una camera: dimensioni in pixel, matrice di Bayer, valore massimo in ADU, dimensione del pixel in micrometri, se il guadagno è regolabile, limiti di esposizione in millisecondi.

### BayerPattern.cs

Contiene solo l'enum `BayerPattern` (`None`, `RGGB`, `BGGR`, `GRBG`, `GBRG`). Non ha funzioni. Attenzione: l'enum omonimo in `AllSky.Imaging` non ha il valore `None`.

### ConfigStore.cs

Contiene la classe `ConfigStore`, che carica, salva e valida `AppConfig`. Il file è in JSON; il salvataggio è atomico.

#### ConfigStore

- **ConfigStore.SerializerOptions** (proprietà statica, riga 14) — `static JsonSerializerOptions SerializerOptions`. Opzioni JSON condivise: nomi di proprietà in camelCase, enum come stringhe camelCase, JSON indentato.
- **ConfigStore.Load** (riga 20) — `AppConfig Load(string path)`. Legge la configurazione dal file. Se il file non esiste restituisce i default senza crearlo. Se il JSON non è leggibile, o se il deserializzatore restituisce `null`, rinomina il file in `<path>.bad` (sovrascrivendo un eventuale precedente) e restituisce i default. Eccezioni: `ArgumentException` se il percorso è vuoto o solo spazi.
- **ConfigStore.Save** (riga 53) — `void Save(string path, AppConfig config)`. Crea la cartella se manca, scrive il JSON in `<path>.tmp` (UTF-8 senza BOM) e lo sposta sul file finale. In caso di errore cancella il file temporaneo e rilancia l'eccezione. Eccezioni: `ArgumentException` (percorso vuoto), `ArgumentNullException` (configurazione nulla), eccezioni di I/O.
- **ConfigStore.Validate** (riga 84) — `IReadOnlyList<ValidationError> Validate(AppConfig config)`. Controlla: latitudine tra -90 e 90, longitudine tra -180 e 180, intervallo di cattura di almeno 1 secondo, notti da conservare di almeno 1, porta tra 1024 e 65535, smoothing tra 0 e 1. Restituisce una lista vuota se la configurazione è valida. Eccezione: `ArgumentNullException` se la configurazione è nulla.
- **ConfigStore.InRange** (riga 124, privato statico) — `bool InRange(double value, double min, double max)`. Verifica `min <= value <= max`. Con NaN il confronto è falso, quindi restituisce `false`.
- **ConfigStore.CreateSerializerOptions** (riga 129, privato statico) — `JsonSerializerOptions CreateSerializerOptions()`. Crea le opzioni usate da `SerializerOptions`: camelCase, indentazione, enum come stringhe senza valori numerici.

### ICameraSource.cs

Interfaccia `ICameraSource : IAsyncDisposable`, implementata dalle sorgenti di frame (simulatore, ASCOM COM, ASCOM Alpaca). Espone le proprietà `Id`, `Name` e `Info` (di tipo `CameraInfo`), senza logica. Le implementazioni non sono in questi file.

#### ICameraSource

- **ICameraSource.ConnectAsync** (riga 10) — `Task ConnectAsync(CancellationToken cancellationToken)`. Apre la connessione alla camera. Il comportamento preciso dipende dall'implementazione.
- **ICameraSource.CaptureAsync** (riga 12) — `Task<RawFrame> CaptureAsync(double exposureMs, double gain, CancellationToken cancellationToken)`. Esegue un'esposizione con la durata in millisecondi e il guadagno indicati, e restituisce il frame grezzo.

### IClock.cs

Interfaccia `IClock` con la sola proprietà `UtcNow`. Serve a iniettare un orologio nei test. Nessuna funzione.

### IPipelineStage.cs

#### IPipelineStage

- **IPipelineStage.Process** (riga 6) — `TOut Process(TIn input)`. Trasforma un valore di ingresso nel valore di uscita di una fase di elaborazione. Non ci sono implementazioni in questi file.

### RawFrame.cs

#### RawFrame

- **RawFrame** (costruttore del record posizionale, riga 7) — `RawFrame(int Width, int Height, ushort[] Pixels, BayerPattern Pattern, DateTimeOffset Timestamp, double ExposureMs, double Gain, double? SensorTempC)`. Frame grezzo a 16 bit, in ordine row-major (indice = `y * Width + x`). La temperatura del sensore è facoltativa (`null` se non disponibile). Gli array si confrontano per riferimento.

### SystemClock.cs

#### SystemClock

- **SystemClock.UtcNow** (proprietà, riga 6) — `DateTimeOffset UtcNow`. Restituisce l'ora UTC del sistema, implementando `IClock`.

### ValidationError.cs

#### ValidationError

- **ValidationError** (costruttore del record posizionale, riga 4) — `ValidationError(string Path, string Message)`. Un errore di validazione: `Path` è il percorso JSON del campo (per esempio `site.latitude`), `Message` è il testo per l'utente.

---

## AllSky.Astro (src/AllSky.Astro)

Calcoli astronomici a bassa precisione, senza librerie esterne. Gli angoli delle funzioni pubbliche sono in gradi; l'altezza è geometrica, cioè senza rifrazione atmosferica. Le precisioni citate sono quelle dichiarate nei commenti del codice: questa documentazione non le verifica.

### AstroTime.cs

Classe statica `internal AstroTime`: funzioni di supporto (giorno giuliano, secoli, angoli, controlli di input) usate dagli altri file del modulo.

#### AstroTime

- **AstroTime.JulianDay** (riga 10) — `double JulianDay(DateTimeOffset utc)`. Giorno giuliano UT dell'istante, ricavato dai millisecondi Unix (epoca JD 2440587.5).
- **AstroTime.Centuries** (riga 16) — `double Centuries(double julianDay)`. Secoli giuliani trascorsi da J2000.0 (JD 2451545.0), cioè la differenza in giorni divisa per 36525.
- **AstroTime.NormalizeDeg** (riga 22) — `double NormalizeDeg(double deg)`. Riporta un angolo nell'intervallo [0, 360) con `NormalizeRange`.
- **AstroTime.NormalizeRange** (riga 28) — `double NormalizeRange(double value, double period)`. Riporta il valore in [0, period) usando il resto della divisione; i valori negativi vengono riportati in positivo.
- **AstroTime.ToRad** (riga 38) — `double ToRad(double deg)`. Converte gradi in radianti.
- **AstroTime.ToDeg** (riga 40) — `double ToDeg(double rad)`. Converte radianti in gradi.
- **AstroTime.SinDeg** (riga 42) — `double SinDeg(double deg)`. Seno di un angolo espresso in gradi.
- **AstroTime.CosDeg** (riga 44) — `double CosDeg(double deg)`. Coseno di un angolo espresso in gradi.
- **AstroTime.CheckLatLon** (riga 46) — `void CheckLatLon(double latDeg, double lonDeg)`. Verifica latitudine in [-90, 90] e longitudine in [-180, 180]; un NaN non passa il controllo. Eccezione: `ArgumentOutOfRangeException` se un valore è fuori intervallo.

### MoonPosition.cs

Classe statica pubblica `MoonPosition`. Usa la serie di Meeus (cap. 47) troncata ai termini principali, più i termini additivi. Le tabelle `LongDistTerms` e `LatTerms` contengono i coefficienti.

#### MoonPosition

- **MoonPosition.Altitude** (riga 81) — `double Altitude(DateTimeOffset utc, double latDeg, double lonDeg)`. Altezza della Luna in gradi sull'orizzonte geometrico, con la correzione di parallasse diurna e senza rifrazione. Calcola la posizione eclittica geocentrica, la converte in ascensione retta e declinazione, ricava l'angolo orario dal tempo sidereo di Greenwich e la longitudine dell'osservatore. Precisione dichiarata nel commento: circa 0.5°–1°, non verificata su un intervallo ampio di date. Eccezione: `ArgumentOutOfRangeException` da `AstroTime.CheckLatLon`.
- **MoonPosition.IlluminatedFraction** (riga 118) — `double IlluminatedFraction(DateTimeOffset utc)`. Frazione illuminata del disco lunare, da 0 (luna nuova) a 1 (luna piena), con k = (1 − cos ψ) / 2, dove ψ è l'elongazione Sole-Luna. Il Sole è trattato sull'eclittica. Il risultato è limitato a [0, 1].
- **MoonPosition.GeocentricEcliptic** (riga 133, privato) — `(double LonDeg, double LatDeg, double DistanceKm) GeocentricEcliptic(double t)`. Longitudine e latitudine eclittiche geocentriche della Luna e distanza in chilometri, per i secoli giuliani `t`. Somma i termini periodici delle tabelle, pesati dal fattore di eccentricità, e i termini additivi.
- **MoonPosition.EFactor** (riga 187, privato) — `double EFactor(int mCoef, double e)`. Fattore di eccentricità terrestre per i termini che contengono l'anomalia solare: `e²` se il coefficiente è ±2, `e` se è ±1, altrimenti 1.
- **MoonPosition.MeanObliquityDeg** (riga 202, privato) — `double MeanObliquityDeg(double t)`. Obliquità media dell'eclittica in gradi (Meeus, cap. 22).
- **MoonPosition.GreenwichMeanSiderealDeg** (riga 208, privato) — `double GreenwichMeanSiderealDeg(double jd, double t)`. Tempo sidereo medio di Greenwich in gradi, normalizzato in [0, 360) (Meeus, eq. 12.4).

### ObservingNight.cs

Notte osservativa: va da mezzogiorno locale a mezzogiorno locale del giorno dopo. Il suo identificatore è la data di inizio in formato `YYYY-MM-DD`. Contiene anche il record struct `PhaseBand`, cioè un intervallo di una fase del cielo.

#### PhaseBand

- **PhaseBand** (costruttore del record struct, riga 9) — `PhaseBand(SkyPhase Phase, DateTimeOffset From, DateTimeOffset To)`. Un intervallo continuo di una stessa fase. Gli intervalli di una notte sono contigui: il `To` di uno coincide con il `From` del successivo.

#### ObservingNight

- **ObservingNight.GetNightId** (riga 24) — `string GetNightId(DateTimeOffset utc, TimeZoneInfo tz)`. Identificatore della notte a cui appartiene un istante. Converte l'istante nel fuso indicato: prima delle 12 locali l'istante appartiene alla notte iniziata il giorno prima. Esempi con Europe/Rome: 19/10 alle 03:00 locali restituisce "2026-10-18"; 19/10 alle 13:00 restituisce "2026-10-19". Eccezione: `ArgumentNullException` se il fuso è nullo.
- **ObservingNight.GetBands** (riga 40, versione breve) — `IReadOnlyList<PhaseBand> GetBands(string nightId, double latDeg, double lonDeg, TimeZoneInfo tz)`. Chiama la versione completa con un passo di campionamento di 1 minuto.
- **ObservingNight.GetBands** (riga 55, versione completa) — `IReadOnlyList<PhaseBand> GetBands(string nightId, double latDeg, double lonDeg, TimeZoneInfo tz, TimeSpan step)`. Campiona l'altezza del Sole da mezzogiorno locale a mezzogiorno del giorno dopo, con il passo indicato. Classifica ogni campione con `SkyPhases.Classify` e unisce i campioni consecutivi con la stessa fase. Il confine tra due fasi è noto con un'approssimazione pari al passo. Eccezioni: `ArgumentNullException` (identificatore o fuso nulli), `ArgumentOutOfRangeException` (coordinate fuori intervallo o passo non positivo), `FormatException` se `nightId` non è nel formato `YYYY-MM-DD`.
- **ObservingNight.LocalNoonUtc** (riga 104, privato) — `DateTimeOffset LocalNoonUtc(DateOnly day, TimeZoneInfo tz)`. Mezzogiorno locale del giorno indicato, convertito in UTC.

### SkyPhase.cs

Contiene l'enum `SkyPhase` (`Day`, `CivilTwilight`, `NauticalTwilight`, `AstronomicalTwilight`, `Night`) e la classe statica `SkyPhases`.

#### SkyPhases

- **SkyPhases.Classify** (riga 23) — `SkyPhase Classify(double sunAltitudeDeg)`. Fase del cielo dall'altezza del Sole: da 0° in su è giorno; da −6° (incluso) a 0° crepuscolo civile; da −12° a −6° nautico; da −18° a −12° astronomico; sotto −18° notte. Eccezione: `ArgumentException` se il valore è NaN.
- **SkyPhases.Simplify** (riga 53) — `string Simplify(SkyPhase phase)`. Riduce la fase alle tre categorie dell'API locale: `"day"`, `"twilight"` (tutti e tre i crepuscoli) o `"night"`. Eccezione: `ArgumentOutOfRangeException` per un valore fuori dall'enum.

### SolarPosition.cs

Classe statica pubblica `SolarPosition`, versione semplificata dell'algoritmo solare NOAA (Meeus). Precisione dichiarata nel commento: 0.01°–0.1°.

#### SolarPosition

- **SolarPosition.Altitude** (riga 21) — `double Altitude(DateTimeOffset utc, double latDeg, double lonDeg)`. Altezza geometrica del Sole in gradi, negativa sotto l'orizzonte. Calcola il tempo solare vero dall'equazione del tempo e dalla longitudine, poi l'angolo orario, la declinazione e l'altezza. Eccezione: `ArgumentOutOfRangeException` da `AstroTime.CheckLatLon`.
- **SolarPosition.ComputeGeometry** (riga 54, interno) — `SunGeometry ComputeGeometry(double t)`. Calcola longitudine eclittica apparente, declinazione ed equazione del tempo (in minuti) per i secoli giuliani `t`. Usata anche da `MoonPosition.IlluminatedFraction`.
- **SunGeometry** (costruttore del record struct interno, riga 101) — `SunGeometry(double ApparentLongitudeDeg, double DeclinationDeg, double EquationOfTimeMin)`. Grandezze solari in un istante, usate solo dentro il modulo.

---

## AllSky.Imaging (src/AllSky.Imaging)

Elaborazione dell'immagine: demosaicing, bilanciamento del bianco, stretch in sRGB. Nota: esistono due tipi con lo stesso nome logico. `Stretch` qui è la classe statica di funzioni; `Core.Stretch` è l'enum della configurazione. `BayerPattern` qui non ha il valore `None`.

### BayerPattern.cs

Contiene solo l'enum `BayerPattern` (`RGGB`, `BGGR`, `GRBG`, `GBRG`), ordinato per la posizione dei pixel (0,0), (1,0), (0,1), (1,1). Nessuna funzione.

### Debayer.cs

#### Debayer

- **Debayer.Bilinear** (riga 20) — `RgbImage Bilinear(ushort[] raw, int width, int height, BayerPattern pattern)`. Demosaicing bilineare. Il canale nativo di ogni pixel conserva il valore grezzo; gli altri due canali sono la media dei pixel di quel colore nell'intorno 3×3. Ai bordi si usa la riflessione, che conserva la parità del pattern. Eccezioni: `ArgumentNullException` (raw nullo); `ArgumentOutOfRangeException` se larghezza o altezza sono inferiori a 2 o il pattern non è definito; `ArgumentException` se `raw.Length` non è uguale a `width * height`.
- **Debayer.Mirror** (riga 92, privato) — `int Mirror(int i, int n)`. Riflette un indice fuori dai bordi: −1 diventa 1, `n` diventa `n − 2`. La riflessione non ripete il bordo, quindi la parità del pattern si conserva.

### PixelSampling.cs

#### PixelSampling

- **PixelSampling.Step** (riga 13) — `int Step(long count)`. Passo di campionamento per le statistiche: 1 se `count` è al massimo 200000 (`MaxSampleSize`), altrimenti il quoziente arrotondato per eccesso, così il campione non supera mai `MaxSampleSize` elementi.

### RgbImage.cs

#### RgbImage

- **RgbImage** (costruttore del record posizionale, riga 7) — `RgbImage(int Width, int Height, float[] R, float[] G, float[] B)`. Immagine RGB a tre piani separati, in ordine row-major, con valori float nell'intervallo 0–65535. Il costruttore non copia gli array.

### Smoother.cs

#### Smoother

- **Smoother** (costruttore, riga 14) — `Smoother(double alpha)`. Crea lo smoother dei guadagni. `alpha` è il peso del nuovo campione e deve essere in (0, 1]. Eccezione: `ArgumentOutOfRangeException` se fuori intervallo.
- **Smoother.Update** (riga 25) — `(double R, double G, double B) Update((double R, double G, double B) gains)`. Al primo campione restituisce i guadagni invariati e li memorizza. Dai successivi restituisce la media esponenziale `alpha * nuovo + (1 − alpha) * precedente`, per ogni canale. Il valore memorizzato è quello restituito.

### StretchOptions.cs

Contiene l'enum `StretchKind` (`Mtf`, `Asinh`) e il record `StretchOptions`.

#### StretchOptions

- **StretchOptions** (costruttore implicito del record, riga 11) — `StretchOptions()`. Opzioni dello stretch con i default: curva MTF, midtone automatico con fondo cielo target 0.2, β = 10 per asinh, percentili di nero 0.5 e di bianco 99.8, punto bianco comune ai tre canali (`LinkedWhitePoint = true`), saturazione 1. Il midtone fisso (`Midtone`) è `null` per impostazione predefinita, cioè automatico.

### StretchParams.cs

#### StretchParams

- **StretchParams** (costruttore del record struct, riga 7) — `StretchParams(float BlackR, float BlackG, float BlackB, float White, float Midtone)`. Parametri di stretch: punto nero per ogni canale, punto bianco comune e midtone.

### StretchParamsSmoother.cs

#### StretchParamsSmoother

- **StretchParamsSmoother** (costruttore, riga 15) — `StretchParamsSmoother(double alpha)`. Crea lo smoother dei parametri. `alpha` è il peso del nuovo campione, in (0, 1]. Eccezione: `ArgumentOutOfRangeException` se fuori intervallo.
- **StretchParamsSmoother.Update** (riga 26) — `StretchParams Update(StretchParams parameters)`. Come `Smoother.Update`, ma applicata a ogni campo. Il primo campione passa invariato. La combinazione convessa mantiene `White − Black >= 1` e il midtone in (0, 1), se valgono per i parametri di partenza.
- **StretchParamsSmoother.Mix** (riga 45, privato) — `float Mix(float fresh, float previous)`. Media esponenziale di un singolo valore con il peso `alpha`.

### Stretch.cs

#### Stretch

Classe statica con le funzioni di stretch (MTF e asinh), la stima dei parametri e la conversione in sRGB a 8 bit. Il punto bianco viene calcolato con percentili interpolati; le statistiche usano un campione a passo fisso (al massimo 200000 pixel).

- **Stretch.Mtf** (riga 12) — `float Mtf(float x, float midtone)`. Midtone transfer function: `(m − 1)·x / ((2m − 1)·x − m)`, con `x` in 0–1 e `midtone` in (0, 1). Vale 0 per `x <= 0` e 1 per `x >= 1`. Eccezione: `ArgumentOutOfRangeException` se il midtone è fuori da (0, 1).
- **Stretch.Asinh** (riga 33) — `float Asinh(float x, float beta)`. Stretch asinh: `asinh(beta·x) / asinh(beta)`, con valori 0 e 1 agli estremi. Eccezione: `ArgumentOutOfRangeException` se `beta` non è maggiore di zero.
- **Stretch.AutoMidtone** (riga 57) — `float AutoMidtone(float[] channelSample, float targetBackground)`. Cerca con bisezione (100 iterazioni) il midtone per cui la mediana del campione, dopo la MTF, vale `targetBackground`. Se la mediana non è in (0, 1) restituisce 0.5. Eccezioni: `ArgumentNullException`; `ArgumentException` se il campione è vuoto; `ArgumentOutOfRangeException` se il target non è in (0, 1).
- **Stretch.Estimate** (riga 103) — `StretchParams Estimate(RgbImage img, StretchOptions opts)`. Stima i parametri di stretch: punto nero per canale al percentile `BlackPercentile`, punto bianco comune pari al massimo dei percentili di bianco dei canali non piatti, midtone automatico dal verde (o quello fisso). Eccezioni: `InvalidOperationException` se `LinkedWhitePoint` è falso, perché `StretchParams` ha un solo punto bianco; `ArgumentException` se immagine o opzioni non sono coerenti.
- **Stretch.ToSrgb8** (riga 124, con opzioni) — `byte[] ToSrgb8(RgbImage img, StretchOptions opts)`. Converte in RGB sRGB a 8 bit, interleaved (3 byte per pixel). Con `LinkedWhitePoint` (default) stima i parametri e chiama la versione con parametri. Altrimenti calcola nero e bianco per ogni canale. Poi applica lo stretch, la saturazione sulla luminanza Rec.709 e la gamma sRGB.
- **Stretch.ToSrgb8** (riga 150, con parametri) — `byte[] ToSrgb8(RgbImage img, StretchOptions opts, StretchParams p)`. Converte usando parametri già noti, per esempio stimati e poi levigati dall'host. Richiede `White − Black >= 1` su ogni canale. Eccezione: `ArgumentException` se la condizione non vale.
- **Stretch.ValidateInputs** (riga 162, privato) — `void ValidateInputs(RgbImage img, StretchOptions opts)`. Controlla che i tre canali abbiano `Width * Height` elementi, che i percentili siano in `0 <= nero < bianco <= 100`, che la saturazione sia almeno 0 e che un midtone fisso sia in (0, 1).
- **Stretch.ValidateParams** (riga 191, privato) — `void ValidateParams(StretchParams p, StretchOptions opts)`. Controlla `White − Black >= 1` per ogni canale e, con la curva MTF, che il midtone sia in (0, 1).
- **Stretch.LinkedLevels** (riga 212, privato) — `void LinkedLevels(RgbImage img, StretchOptions opts, out float blackR, out float blackG, out float blackB, out float white)`. Calcola i neri per canale e il bianco comune. Il bianco è il massimo dei percentili di bianco dei canali non piatti. Un canale piatto (intervallo minore di 1) ha nero 0. Se tutti i canali sono piatti usa 0 e 65535.
- **Stretch.ChannelLevels** (riga 255, privato) — `void ChannelLevels(float[] channel, StretchOptions opts, out float black, out float white)`. Nero e bianco di un singolo canale. Se il canale è piatto, nero 0 e bianco 65535.
- **Stretch.ChannelStats** (riga 266, privato) — `void ChannelStats(float[] channel, StretchOptions opts, out float black, out float white, out bool flat)`. Percentili di nero e di bianco su un campione ordinato, e il flag `flat` che vale `true` se `white − black < 1`.
- **Stretch.ResolveMidtone** (riga 280, privato) — `float ResolveMidtone(float[] green, float black, float white, StretchOptions opts)`. Midtone da usare: 0.5 per la curva asinh; il valore fisso se è impostato; altrimenti quello automatico calcolato sul canale verde normalizzato.
- **Stretch.Render** (riga 296, privato) — `byte[] Render(RgbImage img, StretchOptions opts, float[] black, float[] white, float midtone)`. Produce i byte sRGB: normalizza ogni canale tra nero e bianco, applica la curva, applica la saturazione sulla luminanza, applica la gamma sRGB e arrotonda a 8 bit.
- **Stretch.MtfD** (riga 324, privato) — `double MtfD(double x, double m)`. Versione in `double` della MTF, senza controlli di intervallo. La usano `Mtf` e `AutoMidtone`.
- **Stretch.Shape** (riga 329, privato) — `float Shape(float x, StretchOptions opts, float midtone)`. Applica la curva scelta in `opts` (asinh o MTF) a un valore in 0–1.
- **Stretch.SortedSample** (riga 337, privato) — `float[] SortedSample(float[] channel)`. Copia di un canale a passo fisso (`PixelSampling.Step`), ordinata in modo crescente.
- **Stretch.NormalizedSample** (riga 351, privato) — `float[] NormalizedSample(float[] channel, float black, float white)`. Campione a passo fisso normalizzato in 0–1 tra nero e bianco, usato per il midtone automatico.
- **Stretch.PercentileSorted** (riga 365, privato) — `double PercentileSorted(float[] sorted, double p)`. Percentile con interpolazione lineare su un array già ordinato; `p` è in 0–1.
- **Stretch.SrgbEncode** (riga 375, privato) — `double SrgbEncode(double v)`. Curva di gamma sRGB a tratti su un valore in 0–1, con clamp.
- **Stretch.ToByte** (riga 381, privato) — `byte ToByte(double v01)`. Converte un valore in 0–1 in un byte da 0 a 255, con clamp e arrotondamento.

### WhiteBalance.cs

Classe statica `WhiteBalance`: guadagni di bilanciamento del bianco per il cielo notturno (mediana sigma-clippata) e per il giorno (grey-world), interpolazione e applicazione. Il verde è sempre il riferimento, con guadagno 1.

#### WhiteBalance

- **WhiteBalance.EstimateNightGains** (riga 16) — `(double R, double G, double B) EstimateNightGains(RgbImage img, bool[]? skyMask)`. Guadagni dal fondo cielo notturno. Per ogni canale calcola la mediana sigma-clippata dei pixel in maschera (o di tutti i pixel) e restituisce guadagni che portano rosso e blu alla mediana del verde. Eccezioni: `ArgumentException` se la maschera non ha `Width * Height` elementi; `InvalidOperationException` se una mediana non è positiva o non ci sono pixel in maschera.
- **WhiteBalance.EstimateDayGains** (riga 41) — `(double R, double G, double B) EstimateDayGains(RgbImage img)`. Guadagni diurni grey-world. Esclude i pixel saturi (un canale maggiore o uguale a 65000), tiene i pixel la cui luminanza è tra il 5° e il 95° percentile, e calcola il guadagno di ogni canale come rapporto tra la media del verde e la media del canale. Eccezioni: `InvalidOperationException` se tutti i pixel sono saturi, se non ci sono pixel utili o se una media non è positiva.
- **WhiteBalance.Blend** (riga 115) — `(double R, double G, double B) Blend((double R, double G, double B) g1, (double R, double G, double B) g2, double t)`. Interpolazione lineare tra due set di guadagni. `t` è limitato a [0, 1]; con 0 restituisce `g1`, con 1 restituisce `g2`.
- **WhiteBalance.Apply** (riga 128) — `void Apply(RgbImage img, (double R, double G, double B) gains)`. Moltiplica in place i piani R, G e B per i rispettivi guadagni, con clamp a 0–65535. Eccezione: `ArgumentNullException` se l'immagine è nulla.
- **WhiteBalance.Scale** (riga 136, privato) — `void Scale(float[] plane, double gain)`. Moltiplica ogni valore di un piano per il guadagno, con clamp a 0–65535.
- **WhiteBalance.SigmaClippedMedian** (riga 148, privato) — `double SigmaClippedMedian(float[] channel, bool[]? mask)`. Mediana di un canale sui pixel in maschera (o su tutti). Esegue 3 iterazioni di sigma clipping: scarta i valori più lontani di 2.5 sigma dalla mediana, dove sigma = 1.4826 · MAD. Sopra 200000 pixel selezionati usa un campione a passo fisso. Eccezione: `InvalidOperationException` se la maschera non seleziona alcun pixel.
- **WhiteBalance.Median** (riga 218, privato) — `double Median(double[] values)`. Mediana di un array, che viene ordinato in place. L'array non deve essere vuoto.
- **WhiteBalance.Percentile** (riga 225, privato) — `double Percentile(double[] sorted, double p)`. Percentile con interpolazione lineare su un array già ordinato; `p` è in 0–1.
- **WhiteBalance.IsSaturated** (riga 234, privato) — `bool IsSaturated(RgbImage img, int i)`. Vero se almeno uno dei tre canali del pixel `i` è maggiore o uguale a 65000.
- **WhiteBalance.Luma** (riga 239, privato) — `double Luma(RgbImage img, int i)`. Luminanza Rec.709 del pixel `i`: 0.2126·R + 0.7152·G + 0.0722·B.

---

## Interfaccia web (web/js)

Moduli ES caricati dal browser. `app.js` avvia l'interfaccia; `api.js` parla con il motore; `i18n.js` e `time.js` gestiscono testi, numeri, orari e il calcolo solare per il nastro; `form-kit.js` fornisce i pezzi dei moduli. Le schermate sono in `web/js/views`.

### web/js/api.js

Accesso al motore locale (contratto in `docs/API.md`), solo su richieste a `/api` della stessa origine. Le costanti sono `DEFAULT_TIMEOUT_MS` (8 s) e `POLL_MS` (5 s).

#### ApiError

- **ApiError** (costruttore, riga 8) — `constructor(kind, message = null, status = 0)`. Errore di accesso al motore. `kind` è `'network'`, `'timeout'`, `'http'` o `'parse'`; `message` è il testo del motore, se c'è; `status` è il codice HTTP, 0 se assente. Imposta anche `hasMessage`.

#### Richieste

- **request** (riga 17) — `request(path, { method = 'GET', body, timeout = 8000 } = {})`. Esegue una richiesta JSON con timeout (AbortController) e corpo JSON se presente. Restituisce il JSON della risposta, o `null` se il corpo è vuoto. Lancia `ApiError`: `'timeout'` se scade il tempo; `'network'` se la richiesta o la lettura del corpo falliscono; `'parse'` se una risposta 2xx non è JSON valido; `'http'` se la risposta non è 2xx, con il messaggio `error` del motore quando c'è.

#### api (oggetto dei metodi)

- **api.status** (riga 61) — `GET /api/status`. Stato del motore.
- **api.config** (riga 62) — `GET /api/config`. Configurazione corrente.
- **api.saveConfig** (riga 63) — `saveConfig(config)`, `PUT /api/config`. Salva la configurazione; il motore risponde con `{ ok, errors }`.
- **api.cameras** (riga 64) — `GET /api/cameras/discover`. Elenco delle camere trovate.
- **api.connectCamera** (riga 65) — `connectCamera(id)`, `POST /api/camera/connect` con `{ id }`.
- **api.nights** (riga 66) — `GET /api/nights`. Elenco delle notti.
- **api.frames** (riga 67) — `frames(nightId, { limit = 1000, offset = 0 })`. Frame di una notte, a pagine. L'identificatore della notte è codificato nell'URL.
- **api.startTimelapse** (riga 69) — `startTimelapse(nightId)`, `POST /api/nights/{id}/timelapse` con corpo vuoto. Avvia la creazione del timelapse.
- **api.deleteNight** (riga 71) — `deleteNight(nightId)`, `DELETE /api/nights/{id}`.

#### api.urls

Costruiscono gli URL delle immagini, usati come `src` di `<img>` senza passare da `fetch`.

- **api.urls.latestFrame** (riga 75) — `latestFrame(version = '')`. URL del frame più recente. Con `version` aggiunge `?v=` per evitare la cache del browser.
- **api.urls.nightFrame** (riga 76) — `nightFrame(nightId, frameId)`. URL JPEG di un frame di una notte.
- **api.urls.keogram** (riga 78) — `keogram(nightId)`. URL del keogram della notte.
- **api.urls.startrails** (riga 79) — `startrails(nightId)`. URL dello startrails della notte.

#### Eventi

- **openEvents** (riga 85) — `openEvents({ onStatus, onFrame, onConnection }) → () => void`. Apre `EventSource` su `/api/events`: l'evento `status` aggiorna lo stato e ferma il polling; l'evento `frame` passa l'id e il timestamp del nuovo frame. Se la connessione SSE cade, o se `EventSource` non esiste nel browser, parte il polling di `/api/status` ogni 5 secondi. Restituisce la funzione che chiude tutto. Gli handler dell'`EventSource` sono alle righe 116 (status), 128 (frame), 136 (`onopen`), 141 (`onerror`); la funzione di chiusura è alla riga 147.
- **openEvents.poll** (riga 90, interna) — `async poll()`. Legge `/api/status`: se riesce chiama `onConnection(true)` e `onStatus`, altrimenti `onConnection(false)`.
- **openEvents.startPolling** (riga 100, interna) — `startPolling()`. Avvia subito una lettura e poi una ogni 5 secondi. Non fa nulla se il polling è già attivo o se la connessione è chiusa.
- **openEvents.stopPolling** (riga 106, interna) — `stopPolling()`. Ferma il timer del polling.

---

### web/js/app.js

Avvio dell'interfaccia: carica configurazione, lingua e stato; applica il tema; gestisce il router a hash (`#/live`, `#/nights`, `#/color`, `#/camera`, `#/site`, `#/wizard`). Le viste si caricano solo quando servono. Il contesto `ctx` passato alle viste è definito nell'oggetto alle righe 57–96.

#### Rotte

- **ROUTES.live** (riga 9) — `() => import('./views/live.js')`. Carica la schermata Live.
- **ROUTES.nights** (riga 10) — `() => optionalView('nights')`. Carica la schermata Notti, o il segnaposto se il file manca.
- **ROUTES.color** (riga 11) — `() => optionalView('color')`. Carica la schermata Colore, o il segnaposto.
- **ROUTES.camera** (riga 12) — `() => import('./views/camera.js')`. Carica la schermata Camera.
- **ROUTES.site** (riga 13) — `() => import('./views/site.js')`. Carica la schermata Sito.
- **ROUTES.wizard** (riga 14) — `() => import('./views/wizard.js')`. Carica la procedura guidata.
- **optionalView** (riga 17) — `async optionalView(name)`. Importa `views/{name}.js`. Se il file manca il browser lancia un `TypeError` e viene restituito il segnaposto; gli altri errori (per esempio un errore di sintassi) vengono rilanciati.

#### Contesto (ctx)

- **ctx.config** (getter, riga 58) — restituisce la configurazione in memoria.
- **ctx.getStatus** (riga 61) — restituisce l'ultimo stato ricevuto dal motore.
- **ctx.on** (riga 64) — `on(kind, fn) → () => void`. Registra un ascoltatore per `'status'` o `'frame'`. Restituisce la funzione per rimuoverlo.
- **ctx.setConfig** (riga 69) — `setConfig(config)`. Dopo un salvataggio riuscito rende la configurazione in memoria quella salvata, annulla l'anteprima del tema e riapplica il tema.
- **ctx.previewTheme** (riga 75) — `previewTheme(pref)`. Applica subito un tema senza salvarlo. Con `null` annulla l'anteprima.
- **ctx.setLanguage** (riga 80) — `async setLanguage(code)`. Carica la lingua, applica le stringhe, aggiorna il titolo della pagina e ridisegna la vista corrente, mantenendo il fuoco sul campo che l'aveva.
- **ctx.navigate** (riga 87) — `navigate(hash)`. Cambia l'hash della pagina, quindi la rotta.
- **ctx.wizardDone** (riga 90) — `wizardDone()`. Restituisce il flag di procedura guidata completata.
- **ctx.setWizardDone** (riga 93) — `setWizardDone(value)`. Scrive o rimuove il flag.

#### Funzioni del modulo

- **emit** (riga 47) — `emit(kind, payload)`. Chiama tutti gli ascoltatori registrati per `kind`. Un errore di un ascoltatore viene scritto in console e non blocca gli altri.
- **readWizardFlag** (riga 100) — `readWizardFlag()`. Legge `allsky.wizardDone` da `localStorage`. Restituisce `false` se l'accesso è bloccato.
- **writeWizardFlag** (riga 108) — `writeWizardFlag(value)`. Scrive il flag o lo rimuove. Gli errori di archiviazione vengono ignorati.
- **needsWizard** (riga 118) — `needsWizard(config)`. Vero se la configurazione c'è, il flag non è impostato, il nome del sito è "Osservatorio" e la camera non è scelta (assente o `sim-1`). In quel caso il router va a `#/wizard` al primo avvio.
- **resolveTheme** (riga 127) — `resolveTheme()`. Tema da mostrare: l'anteprima, poi `config.ui.theme`. Se è `'auto'`, mostra `'day'` quando il Sole è sopra l'orizzonte (`status.phase === 'day'`), altrimenti `'night'`.
- **applyTheme** (riga 133) — `applyTheme()`. Imposta `data-theme` sull'elemento `html`.
- **setOnline** (riga 139) — `setOnline(ok)`. Mostra o nasconde l'avviso di motore non raggiungibile.
- **placeholderModule** (riga 147) — `placeholderModule(key)`. Restituisce un modulo di vista sostitutivo, con titolo e testo presi da `placeholder.{key}`.
- **placeholderModule.mount** (riga 149, interna) — `mount(root)`. Sostituisce il contenuto di `root` con la sezione segnaposto.
- **placeholderModule.unmount** (riga 166, interna) — `unmount()`. Non fa nulla.
- **routeFromHash** (riga 173) — `routeFromHash()`. Ricava il nome della rotta dall'hash (per esempio `#/nights/2026-10-18` dà `nights`). Restituisce `null` se la rotta non esiste.
- **setActiveNav** (riga 178) — `setActiveNav(name)`. Imposta `aria-current="page"` sulla voce di menu della rotta attiva e lo rimuove dalle altre.
- **showViewError** (riga 188) — `showViewError(err)`. Mostra il messaggio d'errore nella vista, con `role="alert"`.
- **render** (riga 197) — `async render({ focusId = '' } = {})`. Ridisegna la rotta corrente: smonta la vista precedente, carica il modulo e lo monta. Un token scarta i rendering superati da un altro cambio di rotta. Il fuoco va al campo indicato o al primo `h1`.
- **onStatus** (riga 237) — `onStatus(status)`. Salva lo stato, applica il tema, segna il motore come raggiungibile e avvisa gli ascoltatori `'status'`.
- **onFrame** (riga 244) — `onFrame(frame)`. Avvisa gli ascoltatori `'frame'`.
- **boot** (riga 250) — `async boot()`. Avvio: legge configurazione, lingua e stato; applica tema e avviso di connessione; porta a `#/wizard` se serve; apre gli eventi del motore; registra il cambio di hash (riga 283, anonimo) e fa il primo `render`. Un errore non gestito mostra `errors.unknown` (riga 287, anonimo).

---

### web/js/i18n.js

Caricamento delle stringhe (file `i18n/{lang}.json` più le parti `forms`, `nights`, `color` unite in profondità), formattazione di numeri e orari, messaggi d'errore. L'italiano è la lingua di riserva.

- **normalizeLang** (riga 14) — `normalizeLang(code)`. Restituisce `code` se è una lingua supportata (`it`, `en`), altrimenti `'it'`.
- **isObject** (riga 18, interna) — `isObject(value)`. Vero per oggetti non nulli e non array.
- **mergeDeep** (riga 23) — `mergeDeep(base, extra)`. Unione profonda: i valori di `extra` prevalgono e le sezioni con lo stesso nome si fondono. Non modifica gli argomenti.
- **fetchJson** (riga 31, interna) — `async fetchJson(file, { optional })`. Scarica `i18n/{file}` come JSON. Se il file è facoltativo e manca o non è JSON valido, restituisce `null` e scrive un avviso; altrimenti lancia un errore.
- **fetchBundle** (riga 54, interna) — `async fetchBundle(code)`. Carica `{code}.json` e le parti facoltative `forms`, `nights`, `color`, unendole con `mergeDeep`.
- **loadLocale** (riga 62) — `async loadLocale(code)`. Carica il dizionario della lingua; l'italiano viene caricato una sola volta come riserva. Se la lingua richiesta non si carica usa l'italiano. Imposta `lang` sull'elemento `html` e restituisce la lingua effettiva.
- **locale** (riga 80) — `locale()`. Restituisce il codice della lingua corrente.
- **lookup** (riga 84, interna) — `lookup(obj, key)`. Legge un valore con chiave a punti (`"a.b.c"`); `undefined` se il percorso non esiste.
- **t** (riga 88) — `t(key, vars?)`. Restituisce la stringa della chiave nella lingua corrente, altrimenti quella in italiano, altrimenti la chiave stessa. Sostituisce i segnaposto `{nome}` con i valori di `vars`.
- **applyI18n** (riga 99) — `applyI18n(root = document)`. Imposta il testo degli elementi con `data-i18n` e l'`aria-label` di quelli con `data-i18n-aria`.
- **errorMessage** (riga 109) — `errorMessage(err)`. Usa il messaggio del motore se c'è; altrimenti la stringa `errors.{kind}` (`errors.unknown` se manca anche il tipo).
- **tag** (riga 115, interna) — `tag()`. Tag BCP-47 della lingua corrente (`it-IT` o `en-GB`).
- **localeTag** (riga 120) — `localeTag()`. Esporta il tag BCP-47 per `Intl`.
- **safeTz** (riga 124, interna) — `safeTz(timeZone)`. Restituisce il fuso se `Intl` lo riconosce, altrimenti `undefined` (fuso del browser).
- **fmt.number** (riga 135) — `fmt.number(value, { digits = 0, grouping = true })`. Numero con cifre decimali fisse e separatori della lingua. Restituisce `'–'` per `null`, `undefined` o NaN.
- **fmt.signed** (riga 144) — `fmt.signed(value, digits = 1)`. Come `fmt.number`, ma con il segno meno tipografico (−).
- **fmt.time** (riga 149) — `fmt.time(date, timeZone, { seconds = false })`. Orario HH:MM (o HH:MM:SS) nel fuso indicato, in formato 24 ore.
- **fmt.date** (riga 159) — `fmt.date(date, timeZone)`. Giorno e mese per esteso nella lingua corrente, per esempio "18 ottobre".

---

### web/js/time.js

Calcoli di orario nel fuso dell'osservatorio, senza librerie. Una notte va da mezzogiorno locale a mezzogiorno del giorno dopo; l'identificatore è la data di inizio `YYYY-MM-DD`. Il calcolo solare qui è un duplicato semplificato di quello in C#, usato dal browser per il nastro.

- **formatterFor** (riga 6, interna) — `formatterFor(timeZone)`. Restituisce un `Intl.DateTimeFormat` per il fuso, messo in cache.
- **localParts** (riga 26) — `localParts(date, timeZone)`. Restituisce anno, mese, giorno, ora (0–23), minuti e secondi locali dell'istante. Esportata.
- **offsetMs** (riga 41, interna) — `offsetMs(instant, timeZone)`. Differenza in millisecondi tra l'ora locale del fuso e UTC in quell'istante.
- **zonedDate** (riga 48) — `zonedDate(y, m, d, h, timeZone)`. Restituisce il `Date` corrispondente all'ora locale indicata (`m` da 1 a 12), correggendo l'offset di fuso e l'ora legale.
- **currentNightId** (riga 58) — `currentNightId(now, timeZone)`. Identificatore della notte in corso. Prima di mezzogiorno locale è la notte iniziata il giorno prima.
- **nightWindow** (riga 66) — `nightWindow(nightId, timeZone)`. Restituisce `{ start, end }`: da mezzogiorno locale del giorno a mezzogiorno del giorno dopo.
- **sampleBands** (riga 75) — `sampleBands(nightId, timeZone)`. Fascia di esempio con fasi fisse: giorno 12–18, crepuscolo 18–19, notte 19–5, crepuscolo 5–6, giorno 6–12. Si usa solo se il motore non fornisce `sunBands` e non ci sono coordinate del sito.
- **sampleBands.at** (riga 77, interna) — `at(day, hour)`. `Date` dell'ora locale indicata, tramite `zonedDate`.
- **sunAltitude** (riga 89) — `sunAltitude(date, latDeg, lonDeg)`. Altezza geometrica del Sole in gradi, con algoritmo solare semplificato e senza rifrazione. Il commento dichiara un errore di pochi centesimi di grado, che qui non è verificato.
- **sunAltitude.mod360** (riga 90, interna) — `mod360(x)`. Riporta un angolo nell'intervallo [0, 360).
- **sunCurve** (riga 107) — `sunCurve(nightId, timeZone, latDeg, lonDeg, stepMin = 5)`. Altezza del Sole campionata ogni `stepMin` minuti da mezzogiorno a mezzogiorno, con il punto finale. Restituisce `[{ ms, alt }]`.
- **sunCrossings** (riga 119) — `sunCrossings(curve, thresholds)`. Istanti in cui la curva attraversa una delle soglie, con interpolazione lineare tra due campioni. Restituisce `[{ ms, deg }]`.

---

### web/js/form-kit.js

Pezzi di form condivisi da Procedura guidata, Camera e Sito: campi con etichetta, aiuto ed errore, interruttori, scelte, barra di salvataggio, selezione della camera con anteprima, posizione del dispositivo. Il DOM si costruisce con `h()`, senza `innerHTML` con dati dell'utente. I campi restituiti hanno `path`, `node`, `validate`, `showError`, `focus` e `refresh`; `focus` e `refresh` sono funzioni anonime descritte nella voce del campo.

- **uid** (riga 14, interna) — `uid(base)`. Genera un identificatore univoco `base-N`, con contatore del modulo.
- **h** (riga 17) — `h(tag, props = {}, ...children)`. Costruttore minimo di elementi DOM. Chiavi speciali: `class`, `text`, e `on<evento>` per i listener. `false`, `null` e `undefined` vengono saltati; `true` diventa un attributo vuoto. I figli `null`, `undefined` e `false` vengono ignorati.
- **clone** (riga 33) — `clone(value)`. Copia profonda tramite JSON.
- **sameValue** (riga 34) — `sameValue(a, b)`. Confronto di uguaglianza tramite JSON.
- **withDefaults** (riga 37) — `withDefaults(config)`. Copia della configurazione in cui `site` esiste e il fuso è quello del browser, se mancante.
- **getPath** (riga 46) — `getPath(obj, path)`. Legge un valore a percorso puntato (`"site.name"`); `undefined` se un segmento manca.
- **setPath** (riga 50) — `setPath(obj, path, value)`. Imposta un valore a percorso puntato, creando gli oggetti intermedi mancanti.
- **numberText** (riga 63) — `numberText(value, maxDigits = 6)`. Numero nella lingua corrente, senza raggruppamento. Restituisce `''` se il valore non è finito.
- **parseNumber** (riga 69) — `parseNumber(text)`. Legge un numero digitato a mano, con virgola o punto e con il segno meno tipografico. Restituisce `null` se il testo non è un numero.
- **isTimeZone** (riga 75) — `isTimeZone(zone)`. Vero se `Intl` riconosce il fuso.
- **errorHandles** (riga 86, interna) — `errorHandles(node, input, errEl)`. Restituisce `{ showError, clearError, isShowing }` per il messaggio d'errore di un campo.
- **errorHandles.showError** (riga 87, interna) — `showError(message)`. Mostra il messaggio, marca il campo con `is-invalid` e imposta `aria-invalid`.
- **errorHandles.clearError** (riga 93, interna) — `clearError()`. Nasconde il messaggio e rimuove i marcatori di errore.
- **errorHandles.isShowing** (riga 99, interna) — `isShowing()`. Vero se il messaggio è visibile.
- **inputField** (riga 105) — `inputField({ draft, path, label, help, kind, unit, min, max, integer, scale, maxLength, required, autocomplete, onChange })`. Campo di testo o numero legato a `draft[path]`. Un numero è mostrato diviso per `scale` (per esempio millisecondi mostrati in secondi con `scale: 1000`). Un valore valido aggiorna subito la bozza; il controllo completo avviene al cambio. Restituisce `{ path, node, validate, showError, focus, refresh }`. `focus` (riga 189) porta il fuoco sull'input; `refresh` (riga 190) ricarica il valore dalla bozza. Gli handler `input` e `change` sono alle righe 169 e 179.
- **inputField.toText** (riga 132, interna) — `toText()`. Valore della bozza come testo, con i numeri divisi per `scale`.
- **inputField.read** (riga 139, interna) — `read()`. Legge l'input e restituisce `{ value }` oppure `{ error }`, con i messaggi per campo obbligatorio, lunghezza massima, intero, numero non valido e fuori intervallo. Per i numeri il valore è moltiplicato per `scale`.
- **inputField.validate** (riga 158, interna) — `validate()`. Applica `read`: mostra l'errore, oppure aggiorna la bozza e rimuove l'errore. Restituisce `true` se il campo è valido.
- **selectField** (riga 199) — `selectField({ draft, path, label, help, options, check, onChange, numeric, id })`. Menu a tendina legato a `draft[path]`. `check(valore)` restituisce un messaggio d'errore o `null`. Con `numeric` salva il valore come numero. `id` fisso serve a ritrovare il campo dopo un ridisegno. Restituisce `{ path, node, validate, showError, focus, refresh }`, con `focus` e `refresh` anonimi alle righe 241 e 242.
- **selectField.message** (riga 217, interna) — `message(value)`. Applica `check` e restituisce il messaggio o `null`.
- **selectField.validate** (riga 219, interna) — `validate()`. Se `check` passa salva il valore nella bozza e rimuove l'errore; altrimenti mostra l'errore. Restituisce `true` se valido.
- **switchField** (riga 251) — `switchField({ draft, path, label, help, disabled, disabledHelp, warning, onChange })`. Interruttore checkbox con `role="switch"` legato a `draft[path]`. Con `disabled` mostra `disabledHelp` al posto dell'aiuto. `warning` compare solo quando l'interruttore è acceso. Restituisce `{ path, node, validate, showError, focus, refresh }`; `focus` (riga 284) e `refresh` (uguale a `sync`) sono anonimi.
- **switchField.sync** (riga 268, interna) — `sync()`. Aggiorna lo stato del checkbox e la visibilità dell'avviso a partire dalla bozza.
- **choiceField** (riga 290) — `choiceField({ draft, path, legend, help, options, layout, onChange })`. Gruppo di radio esclusivi, a elenco (`list`) o a griglia (`grid`). Ogni opzione è `{ value, label, help? }`; la scelta va in `draft[path]`. Restituisce `{ path, node, validate, showError, focus, refresh }`.
- **choiceField.sync** (riga 313, interna) — `sync()`. Seleziona la radio che corrisponde al valore della bozza e aggiorna la classe `is-checked` delle etichette.
- **validateAll** (riga 342) — `validateAll(fields) → boolean`. Valida tutti i campi, mostra tutti gli errori e porta il fuoco sul primo non valido. Restituisce `true` se tutti sono validi.
- **applyServerErrors** (riga 352) — `applyServerErrors(fields, errors = [])`. Mostra sotto ciascun campo l'errore del motore (`{ path, message }`) e restituisce i messaggi che non corrispondono a nessun campo.
- **formSection** (riga 364) — `formSection({ title, lede }, ...children)`. Sezione con titolo `h2` collegato con `aria-labelledby` e una descrizione facoltativa.
- **fieldGrid** (riga 373) — `fieldGrid(...children)`. Contenitore a griglia per affiancare i campi.
- **saveBar** (riga 378) — `saveBar({ onSave })`. Barra in fondo al form, con lo stato (modifiche non salvate, esito) e il pulsante Salva, che chiama `onSave`. Restituisce `{ node, say, setDirty, setBusy }`. Il click è alla riga 382.
- **saveBar.say** (riga 388, interna) — `say(text, kind = null)`. Mostra un messaggio di stato; `kind` `'ok'` o `'error'` ne colora il testo.
- **saveBar.setDirty** (riga 397, interna) — `setDirty(on)`. Mostra o nasconde "modifiche non salvate". Un esito "ok" precedente viene cancellato quando si modifica di nuovo.
- **saveBar.setBusy** (riga 401, interna) — `setBusy(on)`. Disabilita il pulsante e mostra il testo di salvataggio in corso, o il testo normale.
- **timeZoneOptions** (riga 410) — `timeZoneOptions(current)`. Elenco di fusi per il menu: quelli di `Intl.supportedValuesOf` o una lista di riserva; aggiunge il fuso corrente se manca.
- **siteFields** (riga 423) — `siteFields(draft, onChange)`. Restituisce i campi del sito: nome, latitudine (−90…90), longitudine (−180…180), quota (−500…9000 m) e fuso.
- **siteFields.f** (riga 424, interna) — `f(key)`. Etichetta del campo, da `forms.fields.{key}.label`.
- **siteFields.help** (riga 425, interna) — `help(key)`. Testo di aiuto del campo, da `forms.fields.{key}.help`.
- **archiveFields** (riga 444) — `archiveFields(draft, onChange)`. Campi della cartella di salvataggio (obbligatoria, fino a 260 caratteri) e delle notti da conservare (intero da 1 a 3650).
- **productFields** (riga 452) — `productFields(draft, onChange)`. Un interruttore per ciascun prodotto: `timelapse`, `keogram`, `startrails`.
- **locationTool** (riga 463) — `locationTool({ onLocated })`. Pulsante che legge la posizione del dispositivo, arrotondata a 5 decimali, e chiama `onLocated({ latitude, longitude, elevation })`. La quota viene inviata solo se disponibile. Il pulsante è disabilitato se il browser non ha la geolocalizzazione. Callback: riga 471 (click), 476 (successo), 486 (errore).
- **livePreview** (riga 504) — `livePreview()`. Anteprima dell'ultimo frame, ricaricata con `start()` ogni 10 secondi e fermata con `stop()`. Se l'immagine non si carica mostra un testo al suo posto. Restituisce `{ node, start, stop }`. Gli handler `load` e `error` dell'immagine sono alle righe 512 e 516.
- **livePreview.reload** (riga 526, interna) — `reload()`. Ricarica l'immagine con un parametro di tempo, per aggirare la cache.
- **livePreview.start** (riga 531, interna) — `start()`. Ricarica subito e poi a intervalli di 10 secondi. Non fa nulla se il timer è già attivo.
- **livePreview.stop** (riga 536, interna) — `stop()`. Ferma il timer.
- **kindLabel** (riga 543, interna) — `kindLabel(kind)`. Etichetta del tipo di camera, da `forms.cameras.kind.{kind}`; se manca restituisce il tipo grezzo.
- **cameraPicker** (riga 551) — `cameraPicker({ selectedId, connected, onConnected, preview = true })`. Elenco delle camere trovate, con i pulsanti Cerca e Connetti, e anteprima dopo la connessione. `connected` è la camera già connessa, `{ id, name }`, oppure `null`. `onConnected(id)` viene chiamata dopo una connessione riuscita. Restituisce `{ node, init, destroy }`, dove `init` è la ricerca iniziale.
- **cameraPicker.say** (riga 569, interna) — `say(text, kind = null)`. Mostra un messaggio di stato, `'ok'` o `'error'`.
- **cameraPicker.setBusy** (riga 575, interna) — `setBusy(on)`. Blocca i pulsanti durante un'operazione; `busy` ricorda lo stato.
- **cameraPicker.renderList** (riga 581, interna) — `renderList()`. Ridisegna l'elenco delle camere come gruppo di radio. Il pulsante Connetti è attivo solo se una camera è scelta.
- **cameraPicker.load** (riga 615, interna) — `async load()`. Chiama `api.cameras()`. Se c'è una sola camera la sceglie; se la camera connessa è quella scelta mostra lo stato e avvia l'anteprima, altrimenti la ferma. In caso di errore mostra il messaggio.
- **cameraPicker.connect** (riga 642, interna) — `async connect()`. Chiama `api.connectCamera(chosen)`. Se la risposta ha `ok: false` mostra `error`; in caso di successo chiama `onConnected`, avvia l'anteprima e mostra lo stato. Il pulsante torna attivo in ogni caso.
- **cameraPicker.destroy** (riga 669) — `destroy()`. Ferma l'anteprima. Chiamata quando la schermata viene smontata.

---

## Schermate (web/js/views)

Ogni schermata esporta `mount(root, ctx)`, che costruisce il contenuto in `root` e restituisce `{ unmount() }` (oppure `null` se la configurazione non è disponibile). Le schermate Camera, Sito e Procedura guidata usano i campi di `form-kit.js`; Colore, Live e Notti costruiscono l'HTML a mano, con testi da `data-i18n` o da `t()`.

### web/js/views/camera.js

Schermata Camera: scelta della camera, guadagno, esposizione diurna e notturna, binning, intervallo di cattura, cattura diurna. La bozza (`session`) resta anche dopo un cambio di lingua, finché la configurazione salvata non cambia.

#### camera

- **camera.mount** (riga 13) — `mount(root, ctx)`. Monta la schermata. Crea la bozza dalla configurazione, costruisce i campi (guadagno, esposizione massima in secondi, esposizione diurna in ms, binning 1–4, intervallo di cattura, cattura diurna) e il selettore della camera. Il selettore riceve `onConnected(id)`, che imposta `camera.id` nella bozza (riga 35, anonima). Senza configurazione mostra un errore di rete.
- **camera.add** (riga 25, interna a mount) — `add(field)`. Registra un campo nell'elenco usato per validare e per mostrare gli errori del motore. Restituisce il campo.
- **camera.refreshDirty** (riga 30, interna a mount) — `refreshDirty()`. Aggiorna lo stato "modifiche non salvate" confrontando la bozza con la configurazione di partenza.
- **camera.onChange** (riga 41, interna a mount) — `onChange()`. Callback dei campi: richiama `refreshDirty`.
- **camera.save** (riga 70, interna a mount) — `async save()`. Valida tutti i campi; se sono validi invia la bozza con `PUT /api/config`. Gli errori del motore vanno sotto i campi, quelli senza campo nella barra. A successo aggiorna la configurazione di partenza e quella in memoria.
- **camera.unmount** (riga 117) — `unmount()`. Ferma il selettore della camera (anteprima).
- **camera.connectedCamera** (riga 123) — `connectedCamera(ctx)`. Restituisce `{ id, name }` della camera connessa secondo lo stato del motore, oppure `null`.

---

### web/js/views/site.js

Schermata Sito: dati del sito con posizione del dispositivo, lingua (cambia subito, si salva con Salva), tema (con anteprima), porta e accesso dalla rete locale, cartella e notti da conservare, testo sulle immagini. La porta si applica al riavvio.

#### site

- **site.mount** (riga 14) — `mount(root, ctx)`. Monta la schermata con la bozza, i campi del sito, la posizione del dispositivo, lingua, tema, porta, accesso dalla rete locale, archivio e testo sulle immagini. Callback anonime: `onLocated` (riga 39) copia latitudine, longitudine e quota e aggiorna i campi del sito; lingua (riga 53) richiama `ctx.setLanguage` quando cambia; tema (riga 68) richiama `ctx.previewTheme`. Al montaggio applica l'anteprima del tema. Un link alla procedura guidata va a `#/wizard`.
- **site.add** (riga 26, interna a mount) — `add(field)`. Registra un campo e lo restituisce.
- **site.addAll** (riga 30, interna a mount) — `addAll(list)`. Registra più campi.
- **site.refreshDirty** (riga 32, interna a mount) — `refreshDirty()`. Aggiorna lo stato "modifiche non salvate".
- **site.onChange** (riga 33, interna a mount) — `onChange()`. Callback dei campi: richiama `refreshDirty`.
- **site.save** (riga 109, interna a mount) — `async save()`. Valida i campi, invia la bozza con `PUT /api/config`, mostra gli errori del motore e, a successo, aggiorna la configurazione in memoria.
- **site.unmount** (riga 173) — `unmount()`. Annulla l'anteprima del tema, così torna quello salvato.

Nota: il campo `ui.allowLan` è disabilitato quando l'accesso dalla rete locale è spento (`disabled: !draft.ui?.allowLan`). Dalla schermata quindi non si può attivarlo; il testo di aiuto dice che è disabilitato finché non c'è una password.

---

### web/js/views/wizard.js

Procedura guidata del primo avvio, in tre passi: camera (`camera`), posizione e sito (`site`), cartella e prodotti (`archive`). Nulla viene inviato al motore finché non si preme Fine; "Salta" chiude la procedura senza salvare.

#### wizard

- **wizard.mount** (riga 12) — `mount(root, ctx)`. Crea la copia di lavoro della configurazione, i campi di ogni passo, la barra dei passi e i pulsanti Indietro, Avanti e Salta. Senza configurazione mostra un errore di rete. Callback: `onLocated` (riga 33) copia la posizione nella bozza; `onConnected` (riga 102) imposta la camera scelta; click di Indietro (riga 181), Avanti (riga 184) e Salta (riga 186, anonimi). Il Salta segna la procedura come completata e va a `#/live`.
- **wizard.connectedCamera** (riga 68, interna a mount) — `connectedCamera()`. Camera connessa secondo lo stato del motore, o `null`.
- **wizard.renderProgress** (riga 73, interna a mount) — `renderProgress()`. Ridisegna la lista dei passi (corrente, completati) e la barra di avanzamento.
- **wizard.showStep** (riga 86, interna a mount) — `showStep(n)`. Mostra il passo `n`: distrugge il selettore camera precedente, costruisce il contenuto del passo, aggiorna titolo, descrizione e pulsanti. Dal secondo render sposta il fuoco sul titolo.
- **wizard.forward** (riga 128, interna a mount) — `forward()`. Dal passo della posizione valida i campi del sito e, se c'è un errore, resta nel passo. Negli altri casi va al passo successivo; all'ultimo passo chiama `finish`.
- **wizard.finish** (riga 141, interna a mount) — `async finish()`. Valida i passi sito e archivio; se uno non è valido torna lì e porta il fuoco sul campo. Invia la bozza con `PUT /api/config`. Se il motore restituisce errori torna al passo del primo errore. A successo segna la procedura come completata e va a `#/live`.
- **wizard.unmount** (riga 195) — `unmount()`. Distrugge il selettore camera.

---

### web/js/views/color.js

Schermata Colore: confronto fra il frame originale e quello elaborato con un divisore trascinabile, istogramma del fondo cielo, cursori per bilanciamento del bianco, stabilità, gradiente, curva, saturazione. Ogni modifica si salva dopo 500 ms con `PUT /api/config`, leggendo prima la configurazione attuale per non sovrascrivere le altre sezioni. I valori consigliati sono quelli di `docs/API.md`.

#### Funzioni del modulo

- **color.clamp** (riga 41) — `clamp(v, lo, hi)`. Limita `v` nell'intervallo [lo, hi].
- **color.num** (riga 45) — `num(v, fallback)`. Restituisce `v` se è un numero finito, altrimenti `fallback`.
- **color.normalizeColor** (riga 50) — `normalizeColor(c = {})`. Copia pulita della sezione `color`: valori mancanti o fuori intervallo tornano ai valori consigliati o ai limiti (guadagni tra 0.5 e 2, stabilità tra 0 e 1, saturazione tra 0 e 2). `whiteBalance` e `stretch` accettano solo i valori previsti.
- **color.template** (riga 62) — `template(uid)`. Restituisce l'HTML della schermata, con id univoci costruiti da `uid` e i testi marcati con `data-i18n`.
- **color.sampleSky** (riga 217) — `sampleSky(img)`. Disegna l'immagine elaborata su un canvas 128×128 e considera solo il cerchio del cielo (raggio pari al 47% del lato). Restituisce gli istogrammi R, G, B a 256 livelli, il numero di pixel e le mediane. Restituisce `null` se il cerchio non ha pixel. Se il canvas non può essere letto la funzione lancia un'eccezione, che `analyse` cattura.
- **color.sampleSky.median** (riga 243, interna) — `median(bins)`. Mediana di un istogramma: il primo livello in cui il conteggio cumulato arriva almeno alla metà dei pixel.

#### color.mount

- **color.mount** (riga 254) — `async mount(root, ctx)`. Monta la schermata: legge la configurazione (o la chiede al motore), inserisce il modello HTML e raccoglie i nodi con `data-role`. Gestisce il confronto, l'istogramma, i cursori, le radio e il salvataggio. Si iscrive agli eventi `status` (riga 719, anonimo) e osserva il cambio di tema per ridisegnare l'istogramma (riga 729, anonimo). Restituisce `{ unmount }`. Altri gestori anonimi: tastiera sul divisore (riga 371), pulsanti "Solo originale" e "Solo elaborata" (righe 403 e 408), Esc nella conferma di ripristino (riga 710).
- **color.frameUrl** (riga 287, interna a mount) — `frameUrl(raw)`. URL del frame del motore; con `raw` aggiunge `stage=raw` per l'originale. Il parametro `v` combina id del frame e revisione, così il browser non usa una copia vecchia.
- **color.setMessage** (riga 292, interna a mount) — `setMessage(kind, text)`. Mostra o nasconde il messaggio sopra il confronto (per esempio "nessun frame" o errore).
- **color.loadImages** (riga 300, interna a mount) — `loadImages()`. Carica originale ed elaborato del frame corrente. Senza frame nasconde le immagini, scrive il messaggio di vuoto e disegna l'istogramma vuoto.
- **color.applySplit** (riga 332, interna a mount) — `applySplit()`. Applica il divisore: `clip-path` sull'originale, posizione della maniglia, `aria-valuenow` e `aria-valuetext`, stato `aria-pressed` dei pulsanti di modalità, etichette "Originale" e "Elaborata".
- **color.setSplitFromX** (riga 345, interna a mount) — `setSplitFromX(clientX)`. Converte la posizione orizzontale del puntatore in percentuale e applica il divisore. Esce dalla modalità "solo" (`mode = null`).
- **color.onComparePointerDown** (riga 353, interna a mount) — `onComparePointerDown(event)`. Inizia il trascinamento del divisore, cattura il puntatore e sposta il divisore. Ignora i click che non sono quello principale del mouse.
- **color.onComparePointerMove** (riga 360, interna a mount) — `onComparePointerMove(event)`. Sposta il divisore mentre si trascina.
- **color.onComparePointerEnd** (riga 363, interna a mount) — `onComparePointerEnd()`. Termina il trascinamento.
- **color.analyse** (riga 416, interna a mount) — `analyse()`. Chiamata quando l'immagine elaborata è caricata: calcola gli istogrammi dal cielo (`sampleSky`), aggiorna la nota e ridisegna.
- **color.updateHistNote** (riga 426, interna a mount) — `updateHistNote()`. Scrive il giudizio sul fondo cielo: "neutro" se la differenza tra le mediane è al massimo 4, altrimenti il canale dominante; aggiunge le mediane e aggiorna l'`aria-label` del grafico.
- **color.drawHist** (riga 447, interna a mount) — `drawHist()`. Disegna l'istogramma su canvas: griglia, assi con 0, 128 e 255, tre curve (R continua, G tratteggiata, B punteggiata) a 64 barre, con l'etichetta del canale vicino al picco. Usa le dimensioni e il `devicePixelRatio` correnti, e i colori dai token CSS.
- **color.updateSwatch** (riga 542, interna a mount) — `updateSwatch()`. Aggiorna il campione di colore dei guadagni manuali e il testo che li descrive.
- **color.syncControls** (riga 553, interna a mount) — `syncControls()`. Riporta sui controlli i valori della copia locale: radio, cursori, valori mostrati e campione di colore.
- **color.setupInputs** (riga 572, interna a mount) — `setupInputs()`. Imposta `min`, `max` e `step` dei cursori.
- **color.edit** (riga 586, interna a mount) — `edit(fn)`. Applica la funzione `fn` alla copia locale della sezione `color`, riallinea i controlli e programma il salvataggio.
- **color.setStatus** (riga 615, interna a mount) — `setStatus(kind, detail)`. Mostra lo stato del salvataggio: "in corso", "salvato alle HH:MM" o l'errore. Mostra il pulsante Riprova solo in caso di errore.
- **color.scheduleSave** (riga 627, interna a mount) — `scheduleSave()`. Segna la copia come modificata e avvia il salvataggio dopo 500 ms; una nuova modifica entro quel tempo riavvia il conteggio.
- **color.errorFromResponse** (riga 636, interna a mount) — `errorFromResponse(res)`. Unisce i messaggi di `res.errors`; se mancano usa `errors.unknown`.
- **color.save** (riga 642, interna a mount) — `async save()`. Ciclo di salvataggio: finché ci sono modifiche in attesa legge la configurazione attuale, unisce la sezione `color` alla copia locale e invia `PUT /api/config`. A successo incrementa la revisione, ricarica le immagini e mostra "salvato". Un secondo salvataggio mentre il primo è in corso viene accodato dal ciclo.
- **color.openConfirm** (riga 689, interna a mount) — `openConfirm()`. Mostra la conferma del ripristino dei valori consigliati e porta il fuoco su "Ripristina".
- **color.closeConfirm** (riga 695, interna a mount) — `closeConfirm(focusReset)`. Nasconde la conferma; con `focusReset` riporta il fuoco sul pulsante Ripristina.
- **color.unmount** (riga 744, interna a mount) — `unmount()`. Disattiva la vista, scollega gli osservatori e l'ascoltatore di stato. Le modifiche in attesa vengono salvate subito.

---

### web/js/views/live.js

Schermata Live: vista circolare del cielo con l'ultimo frame o quello scelto sul nastro; stato in una frase; dati chiave (esposizione, guadagno, mediana ADU, disco libero); nastro della notte con la curva dell'altezza del Sole. Il colore del nastro segue l'altezza reale del Sole, calcolata dalle coordinate del sito.

#### Funzioni del modulo

- **live.polar** (riga 27) — `polar(radius, deg)`. Coordinate di un punto sull'anello del cielo, in un riquadro 480×480 con il nord in alto.
- **live.ringMarkup** (riga 32) — `ringMarkup()`. SVG dell'anello: tacche ogni 5°, più lunghe ogni 30°, e le lettere N, E, S, O.
- **live.template** (riga 56) — `template()`. HTML della schermata Live.
- **live.clamp** (riga 144) — `clamp(v, lo, hi)`. Limita `v` nell'intervallo [lo, hi].
- **live.normalizeFrame** (riga 148) — `normalizeFrame(f)`. Converte un frame del motore in `{ id, ts, phase }`, con `ts` in millisecondi.
- **live.parseBands** (riga 152) — `parseBands(list)`. Converte gli intervalli di fase del motore in oggetti con `Date`. Scarta le fasi sconosciute e le date non valide.
- **live.siteCoords** (riga 160) — `siteCoords(site)`. Restituisce `{ lat, lon }` se le coordinate del sito sono numeri validi, altrimenti `null`.
- **live.hexToRgb** (riga 169) — `hexToRgb(value)`. Converte un colore `#rrggbb` in `[r, g, b]`; `null` se il formato non è questo.
- **live.mixRgb** (riga 176) — `mixRgb(a, b, w)`. Interpolazione lineare tra due colori RGB, con peso `w`.
- **live.cssColor** (riga 180) — `cssColor(rgb)`. Converte `[r, g, b]` nella stringa `rgb(r,g,b)` con valori arrotondati.
- **live.readRamp** (riga 185) — `readRamp(node)`. Legge i token `--phase-day`, `--phase-twilight`, `--phase-night` e restituisce `{ day, twilight, night, at(alt) }`. `at(alt)` dà il colore per un'altezza del Sole: giorno sopra 0°, transizione morbida fino alla notte a −18°.
- **live.readRamp.pick** (riga 187, interna) — `pick(name, fallback)`. Colore da un token CSS, oppure il valore di riserva se il token non è leggibile.
- **live.readRamp.at** (riga 201, interna) — `at(alt)`. Colore per un'altezza, interpolato tra le soglie 0°, −6°, −12° e −18°.
- **live.sunY** (riga 213) — `sunY(alt)`. Posizione verticale, in percentuale, di un'altezza del Sole nel riquadro della curva (da +40° in alto a −60° in basso).

#### live.mount

- **live.mount** (riga 217) — `mount(root, ctx)`. Monta la schermata e disegna subito i dati già noti, poi carica la notte. Si iscrive agli eventi `status` e `frame` del contesto e osserva il cambio di tema per ridisegnare il nastro. Gestori anonimi: link "Vai a Camera" (riga 314), caricamento ed errore dell'immagine del cielo (righe 359 e 365), "Riprova" (riga 693), "Torna all'ultimo frame" (riga 692). Restituisce `{ unmount }`.
- **live.timeText** (riga 249, interna a mount) — `timeText(ms, seconds = true)`. Orario locale del sito, o `'–'` se `ms` non è valido.
- **live.renderStatus** (riga 253, interna a mount) — `renderStatus()`. Aggiorna titolo, frase di stato, esposizione, guadagno, mediana ADU, spazio libero su disco, camera e avvisi a partire dallo stato del motore. Senza stato mostra il caricamento.
- **live.formatExposure** (riga 286, interna a mount) — `formatExposure(ms)`. Esposizione in secondi se è almeno 1000 ms, altrimenti in millisecondi.
- **live.renderCamera** (riga 291, interna a mount) — `renderCamera(cam)`. Riga dello stato della camera con un punto colorato (verde se connessa) e il nome.
- **live.renderProblem** (riga 306, interna a mount) — `renderProblem(st)`. Mostra l'avviso se lo stato è in errore, se c'è un messaggio di errore o se la camera è disconnessa. Usa il messaggio del motore, o quello generico.
- **live.renderSky** (riga 320, interna a mount) — `renderSky()`. Mostra nel cielo il frame scelto sul nastro, oppure l'ultimo frame. La didascalia con l'ora compare solo per un frame scelto. Senza immagine mostra il messaggio di caricamento, di inattività o di assenza di frame.
- **live.pctOf** (riga 371, interna a mount) — `pctOf(ms)`. Posizione in percentuale di un istante sul nastro, limitata tra 0 e 100.
- **live.sunDegAt** (riga 376, interna a mount) — `sunDegAt(ms)`. Altezza del Sole in quell'istante, calcolata con `sunAltitude`; `null` senza coordinate.
- **live.renderRibbonText** (riga 380, interna a mount) — `renderRibbonText()`. Scrive il titolo del nastro con la data e, se la fascia è di esempio, la nota relativa.
- **live.renderBands** (riga 391, interna a mount) — `renderBands()`. Colora il nastro: con coordinate, con un gradiente che segue la curva reale; senza, con fasi a gradini. Disegna anche la curva del Sole con `sunMarkup`.
- **live.sunMarkup** (riga 422, interna a mount) — `sunMarkup(pct)`. Restituisce l'SVG della curva del Sole, delle linee di passaggio di fase (tratteggiate, con alone) e dell'orizzonte a 0°.
- **live.renderAxis** (riga 444, interna a mount) — `renderAxis()`. Scrive le etichette di inizio, metà e fine della notte sotto il nastro, e le tacche orarie.
- **live.nearestFrameIndex** (riga 475, interna a mount) — `nearestFrameIndex(ms)`. Indice del frame più vicino a un istante, con ricerca binaria sull'elenco ordinato.
- **live.setPicked** (riga 488, interna a mount) — `setPicked(index)`. Sceglie il frame con indice `index`; `null` o l'ultimo indice significano "ultimo frame". Aggiorna cielo e cursore.
- **live.renderCursor** (riga 495, interna a mount) — `renderCursor()`. Posiziona il cursore sul frame scelto, scrive ora e altezza del Sole, aggiorna `aria-valuenow` e `aria-valuetext` e mostra o nasconde "Torna all'ultimo frame".
- **live.showBanner** (riga 534, interna a mount) — `showBanner(message)`. Mostra o nasconde l'avviso con il messaggio dato.
- **live.fetchAllFrames** (riga 539, interna a mount) — `async fetchAllFrames(nightId)`. Scarica tutti i frame della notte a pagine di 1000, fino a 20000. Lancia `ApiError('parse')` se una risposta non è un elenco.
- **live.reloadNight** (riga 550, interna a mount) — `async reloadNight()`. Ricarica l'elenco delle notti, individua la notte in corso, scarica i suoi frame, calcola la curva o le fasi e aggiorna il nastro. Una risposta arrivata dopo un'altra richiesta più recente viene scartata con il token.
- **live.appendNewFrames** (riga 603, interna a mount) — `async appendNewFrames()`. Scarica solo i frame successivi a quelli già presenti e li aggiunge in coda. Se la notte non è ancora nota ricarica tutto con `reloadNight`.
- **live.scheduleAppend** (riga 618, interna a mount) — `scheduleAppend()`. Avvia `appendNewFrames` dopo 400 ms, eliminando la richiesta precedente.
- **live.pickFromClientX** (riga 625, interna a mount) — `pickFromClientX(clientX)`. Sceglie il frame più vicino alla posizione orizzontale del puntatore sul nastro.
- **live.onPointerDown** (riga 634, interna a mount) — `onPointerDown(event)`. Inizia la selezione con il puntatore sul nastro, cattura il puntatore, dà il fuoco al nastro e sceglie il frame.
- **live.onPointerMove** (riga 643, interna a mount) — `onPointerMove(event)`. Aggiorna la selezione durante il trascinamento.
- **live.onPointerEnd** (riga 647, interna a mount) — `onPointerEnd()`. Termina il trascinamento.
- **live.onKeyDown** (riga 651, interna a mount) — `onKeyDown(event)`. Tastiera sul nastro: frecce di un frame, PageUp e PageDown di dieci frame, Home e Fine per il primo e l'ultimo, Esc per tornare all'ultimo.
- **live.onStatus** (riga 697, interna a mount) — `onStatus(status)`. Salva lo stato, aggiorna la vista e ricarica la notte se è cambiato il giorno del calendario notturno.
- **live.onFrame** (riga 705, interna a mount) — `onFrame()`. Se la notte non è ancora nota la ricarica; altrimenti accoda i frame nuovi con un ritardo.
- **live.unmount** (riga 731, interna a mount) — `unmount()`. Disattiva la vista, annulla i timer, scollega gli osservatori e gli ascoltatori.

---

### web/js/views/nights.js

Schermata Notti: elenco delle notti, dalla più recente, e pannello di dettaglio (un `<dialog>` modale, aperto con `#/nights/{id}`) con timelapse, keogram, startrails, nastro della notte, miniature ed eliminazione.

#### Funzioni del modulo

- **nights.clamp** (riga 17) — `clamp(v, lo, hi)`. Limita `v` nell'intervallo [lo, hi].
- **nights.esc** (riga 20) — `esc(value)`. Converte in testo sicuro per l'HTML i caratteri `& < > " '`.
- **nights.urls.keogram** (riga 25) — `keogram(id)`. URL del keogram di una notte.
- **nights.urls.startrails** (riga 26) — `startrails(id)`. URL dello startrails di una notte.
- **nights.urls.timelapse** (riga 27) — `timelapse(id)`. URL del file `timelapse.mp4` della notte.
- **nights.urls.thumb** (riga 28) — `thumb(id, frameId)`. URL della miniatura JPEG di un frame.
- **nights.validId** (riga 31) — `validId(id)`. Vero se `id` è una stringa nel formato `YYYY-MM-DD`.
- **nights.nightDate** (riga 36) — `nightDate(id)`. Data della notte per esteso, per esempio "18 ottobre". Usa mezzogiorno UTC per evitare sfasamenti di fuso.
- **nights.sizeText** (riga 41) — `sizeText(mb)`. Dimensione in MB, o in GB sopra 1024 MB; `'–'` se non è un numero.
- **nights.countText** (riga 47) — `countText(n)`. Numero di frame con l'unità; `'–'` se non è un numero.
- **nights.hashNightId** (riga 51) — `hashNightId()`. Legge l'id della notte da `#/nights/{id}`; `null` se assente o non valido.
- **nights.normalizeFrame** (riga 62) — `normalizeFrame(f)`. Converte un frame in `{ id, ts, phase }`. Una fase sconosciuta diventa `'night'`.
- **nights.parseBands** (riga 66) — `parseBands(list)`. Stessa funzione di `live.parseBands`: converte e filtra gli intervalli di fase.
- **nights.alertMarkup** (riga 73) — `alertMarkup(text, kind = 'warn')`. HTML di un avviso (`notice`) con il testo escapato.
- **nights.listTemplate** (riga 79) — `listTemplate()`. HTML della lista delle notti: titolo, stato, errore con "Riprova", caricamento, stato vuoto, griglia.
- **nights.tabsMarkup** (riga 107) — `tabsMarkup()`. HTML dei tre pulsanti di scheda (timelapse, keogram, startrails).
- **nights.panelTemplate** (riga 114) — `panelTemplate()`. HTML del pannello di dettaglio: intestazione, schede, sezione del nastro con anteprima, sezione di eliminazione, e le due finestre di conferma ed ingrandimento.
- **nights.flagMarkup** (riga 185) — `flagMarkup(product, state, value)`. Etichetta di stato di un prodotto (nome e valore) con l'attributo `data-state`.
- **nights.flagsMarkup** (riga 189) — `flagsMarkup(n)`. Le tre etichette di stato (timelapse, keogram, startrails) di una notte.
- **nights.cardMarkup** (riga 201) — `cardMarkup(n, { latest, current })`. HTML della scheda di una notte nella lista: miniatura del keogram, data, numero di frame e dimensione, stato dei prodotti. `latest` e `current` scelgono l'etichetta in alto.
- **nights.mount** (riga 222) — `mount(root, ctx)`. Monta l'elenco e il pannello, collega gli eventi e carica l'elenco. Se l'hash contiene una notte apre il pannello. Gestori anonimi: click sulle schede della griglia che apre il pannello (riga 788), "Riprova" (riga 795), chiusura del pannello (riga 617), chiusura dell'ingrandimento che riporta il fuoco (riga 738), pulsante Chiudi dell'ingrandimento (riga 737), click e tastiera sulle schede del pannello (righe 797 e 802), click sui pulsanti `data-act` nel pannello (riga 825), errori di immagini e video nel pannello (riga 833), pulsanti di eliminazione (righe 845–847), caricamento ed errore dell'anteprima (righe 849 e 852), click sulla striscia (riga 858), trascinamento sul nastro (righe 888, 896, 902, 903), tastiera sul nastro (riga 906). Restituisce `{ unmount }`.
- **nights.mount.timeText** (riga 254, interna a mount) — `timeText(ms, seconds = false)`. Orario locale del sito, o `'–'`.
- **nights.mount.entryOf** (riga 255, interna a mount) — `entryOf(id)`. Notta con quell'id nell'elenco in memoria, o `null`.
- **nights.mount.currentId** (riga 256, interna a mount) — `currentId()`. Id della notte in corso nel fuso del sito.
- **nights.mount.renderList** (riga 260, interna a mount) — `renderList()`. Ridisegna la griglia delle notti, dalla più recente. L'etichetta sopra la data (la notte più recente o quella in corso) viene dalle stringhe `nights.card`.
- **nights.mount.announce** (riga 275, interna a mount) — `announce(message)`. Scrive un messaggio nell'area di stato, letta dagli screen reader.
- **nights.mount.refreshList** (riga 280, interna a mount) — `async refreshList()`. Chiama `api.nights()`, aggiorna l'elenco o l'errore, e aggiorna l'intestazione se il pannello è aperto. Una richiesta più recente scarta quelle precedenti.
- **nights.mount.renderHeader** (riga 303, interna a mount) — `renderHeader()`. Scrive titolo, numero di frame, dimensione e orario del primo e dell'ultimo frame nel pannello.
- **nights.mount.renderTabs** (riga 319, interna a mount) — `renderTabs()`. Imposta `aria-selected` e `tabIndex` delle schede secondo la scheda attiva.
- **nights.mount.selectTab** (riga 328, interna a mount) — `selectTab(name, { focus = false } = {})`. Passa alla scheda `name` se è valida, ridisegna il contenuto e, con `focus`, porta il fuoco sul pulsante.
- **nights.mount.timelapseMarkup** (riga 336, interna a mount) — `timelapseMarkup(entry)`. HTML della scheda timelapse secondo lo stato: `ready` mostra il video e il download; `generating` mostra la barra di progresso; `failed` mostra l'errore con "Riprova"; `none` offre il pulsante per crearlo.
- **nights.mount.imageMarkup** (riga 379, interna a mount) — `imageMarkup(entry, kind)`. HTML della scheda keogram o startrails: immagine, ingrandimento e download; se il prodotto manca mostra un messaggio.
- **nights.mount.renderTabBody** (riga 394, interna a mount) — `renderTabBody()`. Riempie il pannello della scheda attiva e, per il timelapse, aggiorna il progresso.
- **nights.mount.updateProgress** (riga 405, interna a mount) — `updateProgress()`. Aggiorna solo la barra e il testo di progresso mentre il lavoro è in corso, senza ricostruire il pannello.
- **nights.mount.pctOf** (riga 421, interna a mount) — `pctOf(ms)`. Posizione in percentuale di un istante sul nastro, tra 0 e 100.
- **nights.mount.renderBands** (riga 426, interna a mount) — `renderBands(entry)`. Imposta la finestra della notte, disegna i segmenti colorati per fase (o la fascia di esempio, con una nota) e le etichette orarie.
- **nights.mount.defaultPick** (riga 448, interna a mount) — `defaultPick(frames)`. Indice del frame predefinito: il frame di mezzo tra quelli di notte, o quello di mezzo dell'elenco se non ce ne sono.
- **nights.mount.setPick** (riga 458, interna a mount) — `setPick(index)`. Sceglie il frame con l'indice dato, limitato all'intervallo valido, e aggiorna il pannello.
- **nights.mount.renderPick** (riga 464, interna a mount) — `renderPick()`. Aggiorna cursore, ora, fase, anteprima e attributi ARIA del nastro. Senza frame mostra il caricamento o il messaggio di vuoto.
- **nights.mount.renderStrip** (riga 505, interna a mount) — `renderStrip()`. Crea la striscia di miniature con al massimo 72 elementi, presi a passo regolare dai frame.
- **nights.mount.highlightStrip** (riga 525, interna a mount) — `highlightStrip()`. Evidenzia la miniatura più vicina al frame scelto e la porta in vista; con "riduci movimento" lo scorrimento è immediato.
- **nights.mount.loadFrames** (riga 543, interna a mount) — `async loadFrames(id)`. Carica tutti i frame della notte a pagine di 1000, fino a 20000, poi sceglie il frame predefinito e disegna striscia e anteprima. Scarta le risposte di una notte o di una richiesta superate.
- **nights.mount.loadPanel** (riga 574, interna a mount) — `async loadPanel(id)`. Carica l'elenco se serve, poi intestazione, schede, nastro e scheda del timelapse; avvia il polling se il timelapse è in corso; carica i frame.
- **nights.mount.openPanel** (riga 594, interna a mount) — `openPanel(id, { fromHash = false } = {})`. Apre la finestra di dettaglio per l'id. Se la notte cambia, azzera lo stato del pannello. Aggiorna l'hash con `pushState` se l'apertura non viene dall'hash.
- **nights.mount.startTimelapse** (riga 635, interna a mount) — `async startTimelapse(btn)`. Avvia la creazione o il riprovo del timelapse con `POST`. In caso di errore mostra il messaggio; altrimenti passa allo stato `generating`, aggiorna l'elenco e avvia il polling.
- **nights.mount.startPolling** (riga 666, interna a mount) — `startPolling()`. Avvia il polling ogni 2 secondi, con una prima lettura subito. Non fa nulla se è già attivo.
- **nights.mount.stopPolling** (riga 672, interna a mount) — `stopPolling()`. Ferma il polling.
- **nights.mount.pollTick** (riga 678, interna a mount) — `async pollTick()`. Legge `/api/status`. Se c'è un job di timelapse per la notte aperta aggiorna il progresso. Altrimenti rilegge l'elenco e ferma il polling, a meno che il timelapse risulti ancora in corso. Un errore di lettura non ferma il lavoro.
- **nights.mount.openZoom** (riga 724, interna a mount) — `openZoom(kind, opener)`. Apre la finestra di ingrandimento di keogram o startrails. Quando si chiude il fuoco torna su `opener`.
- **nights.mount.openConfirm** (riga 747, interna a mount) — `openConfirm()`. Apre la conferma di eliminazione con data, numero di frame e dimensione.
- **nights.mount.confirmDelete** (riga 761, interna a mount) — `async confirmDelete()`. Chiama `api.deleteNight`. A successo rimuove la notte dall'elenco, annuncia l'eliminazione e chiude il pannello. In caso di errore mostra il messaggio nella conferma.
- **nights.mount.pickFromClientX** (riga 865, interna a mount) — `pickFromClientX(clientX)`. Sceglie il frame più vicino alla posizione orizzontale del puntatore sul nastro.
- **nights.mount.nearestFrameIndex** (riga 874, interna a mount) — `nearestFrameIndex(ms)`. Indice del frame più vicino a un istante, con ricerca binaria.
- **nights.mount.endDrag** (riga 899, interna a mount) — `endDrag()`. Termina il trascinamento sul nastro.
- **nights.mount.unmount** (riga 949, interna a mount) — `unmount()`. Ferma il polling e chiude le finestre di dialogo aperte.

---

## Motore di prova (web/mock/server.mjs)

Server HTTP di prova che implementa il contratto di `docs/API.md` con dati finti e serve i file statici di `web/` su `/`. Usa solo moduli integrati di Node. Il Sole è un modello di comodo (massimo di 50° verso le 13:00 locali) e la Luna una sinusoide: non sono effemeridi. Le immagini sono SVG di prova in `web/mock/assets`. Il colore elaborato è una simulazione con filtri SVG, non elaborazione reale. Le route `/api/__mock/*` sono solo per le prove.

Costanti principali: `PORT` (5810), `TZ` (Europe/Rome), `CADENCE_MS` (30 s, intervallo dei frame), `TIMELAPSE_MS` (12 s, durata finta della creazione del timelapse), `MOCK_STATE` (`capturing`, `idle` o `error`).

- **localParts** (riga 74) — `localParts(ms)`. Anno, mese, giorno, ora (0–23), minuti e secondi locali di un istante nel fuso Europe/Rome.
- **offsetMs** (riga 82) — `offsetMs(ms)`. Differenza in millisecondi tra l'ora locale e UTC in quell'istante.
- **zonedToMs** (riga 87) — `zonedToMs(y, m, d, h)`. Istante UTC corrispondente all'ora locale indicata, corretto per l'ora legale. `m` va da 1 a 12.
- **nightIdFor** (riga 96) — `nightIdFor(ms)`. Identificatore `YYYY-MM-DD` della notte di un istante: prima di mezzogiorno locale è il giorno precedente.
- **nightSpan** (riga 102) — `nightSpan(id)`. `{ start, end }` in millisecondi: da mezzogiorno locale del giorno a mezzogiorno del giorno dopo.
- **localHour** (riga 107) — `localHour(ms)`. Ora locale come numero decimale (per esempio 13.5 per le 13:30).
- **sunAltitude** (riga 113) — `sunAltitude(ms)`. Modello di comodo: `50·cos(2π(ora − 13)/24)` gradi. Non è un calcolo astronomico.
- **moonAltitude** (riga 117) — `moonAltitude(ms)`. Modello sinusoidale di comodo, ampiezza 31°, periodo di 27.3 giorni. Non è un calcolo astronomico.
- **phaseAt** (riga 121) — `phaseAt(ms)`. Fase dal modello del Sole: `'day'` sopra 0°, `'twilight'` sopra −6°, altrimenti `'night'`.
- **iso** (riga 128) — `iso(ms)`. Data in formato ISO 8601 UTC senza millisecondi.
- **round1** (riga 132) — `round1(n)`. Arrotonda a una cifra decimale.
- **bandsFor** (riga 142) — `bandsFor(nightId)`. Intervalli di fase della notte, campionati ogni minuto e uniti quando la fase non cambia. Il risultato è in cache. Restituisce `[{ phase, from, to }]` con date ISO.
- **framesFor** (riga 161) — `framesFor(nightId)`. Frame della notte ogni 30 secondi, fino all'istante corrente. Il risultato è in cache per intervallo di 30 secondi; le voci di intervalli precedenti vengono eliminate.
- **finishJobs** (riga 179) — `finishJobs()`. Chiude i job di timelapse la cui durata finta è scaduta, li segna come pronti e invalida la cache delle notti.
- **timelapseOf** (riga 190) — `timelapseOf(id)`. Stato del timelapse: prima l'override di prova, poi `'generating'` se c'è un job, `'ready'` se è stato creato; altrimenti `'none'` per la notte in corso e `'ready'` per le notti passate.
- **nightsList** (riga 198) — `nightsList()`. Elenco delle ultime sei notti (escluse quelle eliminate) con frame, dimensione, primo e ultimo frame, prodotti, stato del timelapse e fasi. Il risultato è in cache per intervallo di 30 secondi.
- **asset** (riga 231) — `asset(name)`. Legge un file da `web/mock/assets` e lo tiene in cache.
- **frameAssetFor** (riga 239) — `frameAssetFor(phase, nightId)`. Nome dell'immagine di prova per la fase: `frame-day.svg`, `frame-dusk.svg`, oppure `frame-night-a.svg` o `frame-night-b.svg` per la notte, a seconda della parità del giorno.
- **activeJobs** (riga 248) — `activeJobs()`. Elenco dei job in corso con il progresso, calcolato sul tempo trascorso e limitato a 99.
- **status** (riga 259) — `status()`. Stato del motore finto: versione, stato (`MOCK_STATE`), fase, altezze del Sole e della Luna, camera, ultimo frame (esposizione, guadagno, mediana ADU), disco, errore se `MOCK_STATE` è `error`, job in corso.
- **validateConfig** (riga 293) — `validateConfig(body)`. Controlla `schemaVersion`, lingua, tema, latitudine e longitudine. Restituisce la lista degli errori `{ path, message }`, vuota se la configurazione è valida.
- **sendJson** (riga 320) — `sendJson(res, code, body)`. Invia una risposta JSON con `Content-Length` e `Cache-Control: no-store`.
- **sendBuffer** (riga 331) — `sendBuffer(req, res, data, type, cache)`. Invia un file binario con il supporto alle richieste `Range` (necessario al video per lo spostamento). Risponde 206 per un intervallo valido, 416 se l'intervallo non è valido, 200 senza intervallo.
- **sendSvg** (riga 366) — `sendSvg(res, name, cache)`. Invia un'immagine di prova SVG.
- **sendSvgText** (riga 385) — `sendSvgText(res, text, cache)`. Invia un SVG generato come testo UTF-8.
- **colorParams** (riga 395) — `colorParams()`. Legge i parametri colore da `config.color`, con i valori di default e i limiti (guadagni 0.5–2, stabilità 0–1, saturazione 0–2).
- **processedSvg** (riga 408) — `processedSvg(name)`. Crea la versione "elaborata" del frame di prova, avvolgendo il contenuto SVG in un gruppo con filtro: sfocatura per la stabilità, matrice di saturazione, guadagni per canale (manuali, o una correzione fissa sulle notti in automatico) e un velo di orizzonte che dipende dal gradiente. È una simulazione visiva. La cache viene svuotata quando supera 50 voci.
- **readBody** (riga 443) — `readBody(req)`. Legge il corpo della richiesta come testo UTF-8. Rifiuta con errore i corpi oltre 1 MB.
- **handleEvents** (riga 463) — `handleEvents(req, res)`. Endpoint SSE `/api/events`: invia subito lo stato e il frame corrente, poi lo stato ogni 5 secondi e il frame quando cambia id. Il timer viene fermato alla chiusura della connessione. Il timer che invia gli eventi è alla riga 485 (anonimo).
- **sendStatus** (riga 472, interna a handleEvents) — `sendStatus()`. Scrive l'evento `status` con lo stato corrente.
- **checkFrame** (riga 473, interna a handleEvents) — `checkFrame()`. Scrive l'evento `frame` se l'id dell'ultimo frame è cambiato dall'invio precedente.
- **nightExists** (riga 496) — `nightExists(id)`. Vero se la notte è nell'elenco di `nightsList`.
- **framesPage** (riga 500) — `framesPage(url, nightId)`. Pagina di frame della notte. `limit` va da 1 a 1000 (predefinito 200), `offset` è almeno 0. Restituisce `{ id, timestamp, phase }`.
- **handleApi** (riga 507) — `async handleApi(req, res, url)`. Router dell'API: stato (`GET /api/status`), eventi, configurazione (`GET` e `PUT /api/config`, con validazione), camere (`GET /api/cameras/discover`, `POST /api/camera/connect`), frame più recente (`GET /api/frame/latest.jpg`, con `stage=raw` per l'originale), notti (`GET /api/nights`, frame, miniature, keogram, startrails, `timelapse.mp4`, `POST` per creare il timelapse, `DELETE` per eliminare una notte), route `/api/__mock/*` per le prove. Ogni altro percorso riceve `404` in JSON.
- **serveStatic** (riga 661) — `serveStatic(req, res, pathname)`. Serve i file di `web/` con il tipo MIME ricavato dall'estensione. Accetta solo `GET` e `HEAD` (altrimenti `405`), blocca i percorsi fuori da `web/` e quelli in `web/mock` (`404`).
- **server** (riga 700, funzione anonima passata a `http.createServer`) — `async (req, res)`. Instrada le richieste: `/api` a `handleApi`, il resto a `serveStatic`. Un errore non gestito produce `500` in JSON. Il server ascolta su `127.0.0.1` alla porta `PORT`.

---

## Da verificare

Nessuna funzione è stata marcata "Da verificare": il comportamento di ogni voce è ricavato dal codice sorgente, senza parti rimaste incerte.

Restano da verificare, fuori dal perimetro di questo documento, le precisioni dichiarate nei commenti: `MoonPosition.Altitude` (0.5°–1°), `SolarPosition.Altitude` (0.01°–0.1°) e `sunAltitude` in `time.js` (pochi centesimi di grado). Il commento di `MoonPosition` dichiara esplicitamente che l'intervallo di validità non è stato verificato.

---

## Riepilogo dei conteggi

| File | Funzioni documentate |
|---|---|
| AllSky.Core (C#) | 14 |
| AllSky.Astro (C#) | 25 |
| AllSky.Imaging (C#) | 41 |
| web/js/api.js | 19 |
| web/js/app.js | 33 |
| web/js/i18n.js | 18 |
| web/js/time.js | 12 |
| web/js/form-kit.js | 52 |
| web/js/views/camera.js | 7 |
| web/js/views/site.js | 7 |
| web/js/views/wizard.js | 7 |
| web/js/views/color.js | 29 |
| web/js/views/live.js | 43 |
| web/js/views/nights.js | 55 |
| web/mock/server.mjs | 36 |
| **Totale** | **398** |
