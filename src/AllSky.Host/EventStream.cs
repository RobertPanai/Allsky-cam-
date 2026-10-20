using System.Text;
using System.Threading.Channels;
using AllSky.Engine;
using Microsoft.AspNetCore.Http;

namespace AllSky.Host;

/// <summary>
/// Server-Sent Events di <c>GET /api/events</c>: evento <c>status</c> (come <c>/api/status</c>) all'apertura e a
/// ogni cambio, evento <c>frame</c> (<c>{id,timestamp}</c>) per ogni frame archiviato. Un commento di keep-alive
/// ogni 15 secondi tiene aperta la connessione. La connessione si chiude quando il client si disconnette o
/// quando l'Host si arresta.
/// </summary>
internal static class EventStream
{
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(15);

    /// <summary>Gestisce una connessione SSE fino alla disconnessione del client o all'arresto dell'Host.</summary>
    /// <param name="context">Richiesta HTTP.</param>
    /// <param name="engine">Servizio da cui arrivano gli eventi.</param>
    /// <param name="stopping">Annullato all'arresto dell'Host.</param>
    public static async Task ServeAsync(HttpContext context, IAllSkyService engine, CancellationToken stopping)
    {
        var messages = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });

        // Gli eventi del motore arrivano da thread diversi: qui si accoda soltanto, la scrittura e' sul thread della richiesta.
        Action<StatusDto> onStatus = status => messages.Writer.TryWrite(Frame("status", status));
        Action<FrameEvent> onFrame = frame => messages.Writer.TryWrite(Frame("frame", frame));

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream; charset=utf-8";
        context.Response.Headers["Cache-Control"] = "no-cache";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        engine.StatusChanged += onStatus;
        engine.FrameProduced += onFrame;
        try
        {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, stopping);
            CancellationToken token = linked.Token;

            await context.Response.WriteAsync("retry: 3000\n\n", token);
            onStatus(engine.GetStatus());
            await FlushPendingAsync(context, messages.Reader, token);

            // Una sola attesa pendente alla volta: si ricrea solo dopo che e' stata completata.
            Task? signal = null;
            while (!token.IsCancellationRequested)
            {
                signal ??= messages.Reader.WaitToReadAsync(token).AsTask();
                Task keepAlive = Task.Delay(KeepAliveInterval, token);
                await Task.WhenAny(signal, keepAlive);

                if (token.IsCancellationRequested)
                {
                    break;
                }

                if (signal.IsCompleted)
                {
                    signal = null;
                    await FlushPendingAsync(context, messages.Reader, token);
                }
                else
                {
                    await context.Response.WriteAsync(": keep-alive\n\n", token);
                    await context.Response.Body.FlushAsync(token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Chiusura normale: client disconnesso o Host in arresto.
        }
        catch (IOException)
        {
            // Il client ha chiuso la connessione durante la scrittura.
        }
        finally
        {
            engine.StatusChanged -= onStatus;
            engine.FrameProduced -= onFrame;
        }
    }

    /// <summary>Scrive tutti i messaggi accodati e svuota il buffer della risposta.</summary>
    private static async Task FlushPendingAsync(HttpContext context, ChannelReader<string> reader, CancellationToken token)
    {
        while (reader.TryRead(out string? message))
        {
            await context.Response.WriteAsync(message, token);
        }

        await context.Response.Body.FlushAsync(token);
    }

    /// <summary>Formatta un evento SSE; il JSON compatto non contiene a capo, quindi una riga <c>data</c> basta.</summary>
    private static string Frame(string name, object payload)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(payload, HostJson.Options);
        return new StringBuilder()
            .Append("event: ").Append(name).Append('\n')
            .Append("data: ").Append(json).Append("\n\n")
            .ToString();
    }
}
