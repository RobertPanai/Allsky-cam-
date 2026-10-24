# Design desktop: palette, scale e regole d'uso

Specifica visiva: `docs/ralph/IMPLEMENTATION_PLAN.md` §5. Questo documento descrive ciò che è implementato in
`src/AllSky.Core/Design/Palette.cs` (colori, logica pura) e in `src/AllSky.Desktop/Theme/` (risorse WPF e stili).

## Palette

I colori sono definiti per ruolo in `Palette.Dark` e `Palette.NightRed`. Nel tema WPF ogni ruolo diventa due risorse:
`Brush.<ruolo>` (pennello) e `Color.<ruolo>` (colore, per gli sfumati).

| Ruolo | Dark | Notte rossa |
|---|---|---|
| Background | `#000000` | `#000000` |
| Surface1 | `#06080C` | `#080000` |
| Surface2 | `#0B0F17` | `#0E0000` |
| Surface3 | `#111723` | `#140000` |
| Border | `#182233` | `#3C0000` |
| BorderActive | `#357292` | `#E60000` |
| Text | `#E6EDF7` | `#FF0000` |
| TextSecondary | `#8C98AB` | `#F20000` |
| TextTertiary | `#7A8594` | `#EE0000` |
| Accent | `#5CC8FF` | `#F20000` (era `#E60000`: contrasto 4,4:1 con il testo del pulsante, sotto 4,5:1; senza verde né blu) |
| Accent2 | `#8A7DFF` | `#C00000` |
| Success | `#3DDC97` | `#F80000` |
| Warning | `#FFC24B` | `#FF0000` |
| Error | `#FF5C7A` | `#F00000` |
| Sun | `#FFC24B` | `#FF0000` |
| Moon | `#C9D4E5` | `#DC0000` |
| Satellite | `#5CC8FF` | `#C00000` |
| Meteor | `#FF8A3D` | `#E60000` |
| Fireball | `#FF5C7A` | `#FF0000` |
| TwilightCivil | `#1B2A4A` | `#2A0000` |
| TwilightNautical | `#111C33` | `#1A0000` |
| TwilightAstro | `#0A1020` | `#0E0000` |
| Night | `#000000` | `#000000` |

Note sui valori:

- **Bordi.** Il piano li indica come rgba. Qui sono già miscelati sulla superficie 1 (`#06080C`), perché i ruoli sono opachi.
- **TextTertiary.** Il piano indica `#5D6778`, che misura 3.1–3.7:1 e non rispetta la sua stessa regola (4.5:1 dove compare).
  Il valore usato è `#7A8594`, che supera 4.5:1 su tutte e quattro le superfici.
- **Notte rossa.** Nessun colore ha componenti verde o blu. Il testo scende al minimo che tiene 4.5:1 su ogni superficie
  (rosso ≥ 238). Stati e codici astronomici condividono la stessa tonalità: si distinguono per luminosità, icona ed etichetta,
  mai solo per colore.

### Contrasto (WCAG 2.x, verificato dai test in `tests/AllSky.Core.Tests/PaletteTests.cs`)

| Coppia | Dark | Notte rossa | Soglia |
|---|---|---|---|
| Text su Background / Surface1–3 | 15.2–17.8:1 | 5.1–5.3:1 | 4.5:1 |
| TextSecondary su Background / Surface1–3 | 6.2–7.2:1 | 4.6–4.8:1 | 4.5:1 |
| TextTertiary su Background / Surface1–3 | 4.8–5.6:1 | 4.5–4.6:1 | 4.5:1 |
| Accent su Background | 11.2:1 | 4.4:1 | 3:1 |
| Success / Warning / Error su Background | 7.1–13.1:1 | 4.7–5.3:1 | 4.5:1 |

Il contrasto è garantito alla luminosità del 100%. Con la luminosità ridotta (`Palette.Scaled`) i rapporti scendono.

## Luminosità

`ThemeManager.SetBrightness` applica `Palette.Scaled(fattore)`: moltiplica i canali RGB, con fattore da 0.2 a 1.0
(20%–100%). Il metodo `Scaled` limita il valore nello stesso intervallo.

## Scale

- **Tipografia** (risorse `Size.*`, in pixel WPF): Caption 12, Body 14, Subtitle 16, Title 20, Display 28, Hero 40.
- **Spaziatura** (risorse `Space.*`): 1 = 4, 2 = 8, 3 = 12, 4 = 16, 5 = 24, 6 = 32, 7 = 48.
- **Font** (risorse `Font.Ui`, `Font.Mono`): Inter e JetBrains Mono sono incorporati in `src/AllSky.Desktop/Assets/Fonts/`
  (licenza SIL OFL 1.1, fonte e SHA-256 in `assets/brand/README.md`). Se i file mancano nel pacchetto, si usano
  `Segoe UI Variable Text` / `Segoe UI` e `Cascadia Mono` / `Consolas`.
- **Raggi:** pannelli 10 px, controlli 8 px, suggerimenti 6 px.

## Identità visiva

Marchio e icona sono in `assets/brand/` (SVG, `.ico` multi-risoluzione, README con origine e licenza). Le geometrie WPF
sono in `src/AllSky.Desktop/Theme/Brand.xaml`; tutto si genera con `tools/brand/build_brand.py`.
- Icona dell'exe: `ApplicationIcon` nel csproj. Icona del tray: risorsa incorporata (`TrayIcon.cs`). Installer: `--icon` in `release-stable.yml`.
- Splash: `Views/SplashWindow.cs`, chiusa quando la finestra principale è caricata.
- Sfondo stellare statico: `Theme/StarfieldBackground.cs` con la logica pura in `AllSky.Core/Design/StarfieldGenerator.cs`.
- Illustrazioni degli stati vuoti: `Theme/Illustrations.cs` (testi `Illus*` in `DesktopTexts`).

## Stili e regole d'uso

Gli stili sono in `src/AllSky.Desktop/Theme/Styles.xaml`. Tutti i colori usano `{DynamicResource ...}`.

- **Pulsanti.** `Button.Primary` per l'unica azione dominante di una schermata. `Button.Secondary` è il predefinito
  (anche senza stile esplicito). `Button.Danger` per le azioni distruttive, che chiedono sempre conferma.
  Altezza minima 32 px.
- **Campi e interruttori.** `TextBox` con bordo che si accende al passaggio del mouse e al focus. `CheckBox` è un interruttore
  a pista e pallino.
- **Slider e barra.** `Slider` con parte attiva in accento. `ProgressBar` sottile, senza animazioni continue.
- **Scorrimento.** `ScrollBar` da 8 px, verticale e orizzontale, con pollice che si accende al mouse.
- **Pannelli.** Lo stile `Panel` (Border) ha bordo 1 px, sfondo `Surface1`, angoli 10 px e una linea luminosa di 1 px in alto.
  Il titolo usa `PanelTitle`: maiuscoletto (`Typography.Capitals="AllSmallCaps"`), colore secondario.
- **Focus da tastiera.** Anello ciano di 2 px (`Focus.Ring`, usato come `FocusVisualStyle`), su tutti i controlli interattivi.
- **Stati.** Hover, pressed e disabled sono previsti per ogni stile. Nessuna animazione: gli stati cambiano solo con i trigger.
- **Testi.** Le scritte passano dalle risorse IT/EN. Nei pannelli di sviluppo (come la galleria) i testi sono fissi in italiano.

## Modalità notte rossa

- `ThemeManager.SetNightMode(true)` o `ToggleNightMode()` (scorciatoia Ctrl+R, collegata nella galleria; la shell
  principale la collegherà in P1.3).
- Il cambio aggiorna i pennelli esistenti, quindi i controlli già creati si ridisegnano senza essere ricostruiti.
- `ThemeManager.ThemeChanged` segnala ogni cambio effettivo.

## Galleria componenti

La galleria mostra tutti gli stili, la scala tipografica e di spaziatura, la palette con sigla e valore esadecimale di ogni
ruolo, l'interruttore della modalità notte e lo slider della luminosità.

Per aprirla:

```
AllSkyBrain.exe --gallery
```

Il programma avvia normalmente l'host e apre la galleria al posto della finestra principale. La finestra principale resta
disponibile dall'icona nell'area di notifica. Con `--data-folder` la cartella dati resta quella indicata.

La galleria è compilata in ogni build (non dietro `#if DEBUG`): serve per verificare il tema anche in rilascio.
Il file è `src/AllSky.Desktop/Views/ComponentGalleryWindow.cs`.

## Limiti noti

- Il progetto WPF non si compila su Linux: la verifica dei file XAML e C# del desktop è fatta sulla CI Windows.
- Nella notte rossa stati e codici astronomici si distinguono solo per luminosità e per l'etichetta testuale.
- Il pulsante primario in stato di hover usa il bordo bianco invece di cambiare sfondo, per non introdurre un sesto colore.
