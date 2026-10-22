namespace AllSky.Nina;

/// <summary>Sorgente dello stato di NINA. Sola lettura.</summary>
public interface INinaSource
{
    /// <summary>
    /// Legge lo stato. Non lancia eccezioni per guasti di rete, timeout o risposte errate: li descrive in
    /// <see cref="NinaState.Error"/>. Lancia <see cref="OperationCanceledException"/> solo se il chiamante annulla.
    /// </summary>
    Task<NinaState> ReadAsync(CancellationToken ct);
}
