using AllSky.Archive;

namespace AllSky.Timelapse;

/// <summary>
/// Maschera informativa applicata a un frame prima del montaggio. Riceve il JPEG archiviato (pulito) e il sidecar
/// del frame, e restituisce il JPEG da montare. Il timelapse non conosce WPF: l'implementazione concreta sta nel Desktop.
/// </summary>
public interface IFrameOverlay
{
    /// <summary>Restituisce il JPEG con la maschera. Lancia un'eccezione se il frame non puo' essere composto.</summary>
    /// <param name="jpeg">JPEG archiviato del frame.</param>
    /// <param name="sidecar">Metadati del frame: lo stesso istante del JPEG. Null se il frame non esiste.</param>
    /// <param name="ct">Cancellazione.</param>
    Task<byte[]> ApplyAsync(byte[] jpeg, FrameSidecar? sidecar, CancellationToken ct);
}

/// <summary>Implementazione nulla: restituisce il JPEG invariato. Il comportamento predefinito senza maschera.</summary>
public sealed class NullFrameOverlay : IFrameOverlay
{
    /// <summary>Istanza condivisa, senza stato.</summary>
    public static NullFrameOverlay Instance { get; } = new();

    /// <inheritdoc />
    public Task<byte[]> ApplyAsync(byte[] jpeg, FrameSidecar? sidecar, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(jpeg);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(jpeg);
    }
}

/// <summary>
/// Come applicare la maschera a un montaggio: l'overlay, il sidecar del frame di indice N (frame N, orario N) e
/// il gestore degli errori per frame. Il chiamante fornisce gli indici nello stesso ordine dei JPEG.
/// </summary>
/// <param name="Overlay">Maschera da applicare.</param>
/// <param name="SidecarAt">Sidecar del frame di indice dato. Lancia o restituisce null se il frame non ha metadati.</param>
/// <param name="OnError">Chiamato per ogni frame che non e' stato mascherato: indice e causa. Il frame viene montato senza maschera.</param>
public sealed record FrameOverlayPlan(
    IFrameOverlay Overlay,
    Func<int, FrameSidecar?> SidecarAt,
    Action<int, Exception> OnError);

/// <summary>Passo del montaggio: produce i byte JPEG da inviare a ffmpeg per un frame.</summary>
public static class FrameOverlayStep
{
    /// <summary>
    /// Legge il JPEG e lo compone con la maschera. Se la maschera fallisce per questo frame, segnala l'errore con
    /// <see cref="FrameOverlayPlan.OnError"/> e restituisce il JPEG originale: un frame difettoso non ferma il timelapse.
    /// La cancellazione viene propagata. Un errore di lettura del file viene propagato.
    /// </summary>
    /// <param name="jpegPath">Percorso del JPEG archiviato.</param>
    /// <param name="index">Posizione del frame nella sequenza (0 = primo).</param>
    /// <param name="plan">Maschera e sidecar del montaggio.</param>
    /// <param name="ct">Cancellazione.</param>
    public static async Task<byte[]> ComposeAsync(string jpegPath, int index, FrameOverlayPlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        byte[] original = await File.ReadAllBytesAsync(jpegPath, ct);
        try
        {
            FrameSidecar? sidecar = plan.SidecarAt(index);
            byte[] composed = await plan.Overlay.ApplyAsync(original, sidecar, ct);
            if (composed is null || composed.Length == 0)
            {
                throw new InvalidOperationException("la maschera non ha restituito dati.");
            }

            return composed;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            plan.OnError(index, ex);
            return original;
        }
    }
}
