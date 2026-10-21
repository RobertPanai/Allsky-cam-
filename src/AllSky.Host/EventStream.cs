using System.Text;
using System.Threading.Channels;
using AllSky.Engine;
using AllSky.Publish;
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
    /// <param name="host">Dipendenze dell'Host: motore, arresto, servizi della pubblicazione e filtro dei segreti.
    /// Con la pubblicazione presente, l'evento <c>publish</c> segue ogni cambio di stato.</param>
    public static async Task ServeAsync(HttpContext context, HostContext host)
    {
        IAllSkyService engine = host.Engine;
        CancellationToken stopping = host.Stopping;
        PublishServices? publishing = host.Publishing;
        var messages = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });

        // Gli eventi del motore arrivano da thread diversi: qui si accoda soltanto, la scrittura e' sul thread della richiesta.
        Action<StatusDto> onStatus = status => messages.Writer.TryWrite(Frame("status", host.FilterStatus(status)));
        Action<FrameEvent> onFrame = frame => messages.Writer.TryWrite(Frame("frame", frame));
        Action<PublishStatus>? onPublish = publishing is null
            ? null
            : status => messages.Writer.TryWrite(Frame("publish", PublishRoutes.ViewOf(status, publishing.Redactor)));

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/event-stream; charset=utf-8";
        context.Response.Headers["Cache-Control"] = "no-cache";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        engine.StatusChanged += onStatus;
        engine.FrameProduced += onFrame;
        if (publishing is not null && onPublish is not null)
        {
            Subscribe(publishing, onPublish);
        }

        try
        {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, stopping);
            CancellationToken token = linked.Token;

            await context.Response.WriteAsync("retry: 3000\n\n", token);
            onStatus(engine.GetStatus());
            if (publishing is not null)
            {
                messages.Writer.TryWrite(Frame("publish", PublishRoutes.ViewOf(publishing.Publisher.GetStatus(), publishing.Redactor)));
            }

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
            if (publishing is not null && onPublish is not null)
            {
                Unsubscribe(publishing, onPublish);
            }
        }
    }

    /// <summary>Si iscrive agli stati della pubblicazione. Un errore non deve chiudere lo stream: si prosegue senza.</summary>
    private static void Subscribe(PublishServices publishing, Action<PublishStatus> handler)
    {
        try
        {
            publishing.Publisher.StatusChanged += handler;
        }
        catch (Exception)
        {
            // Nessun evento publish su questa connessione: lo stato resta leggibile da GET /api/publish/status.
        }
    }

    /// <summary>Annulla l'iscrizione alla chiusura dello stream.</summary>
    private static void Unsubscribe(PublishServices publishing, Action<PublishStatus> handler)
    {
        try
        {
            publishing.Publisher.StatusChanged -= handler;
        }
        catch (Exception)
        {
            // Il publisher non deve far cadere la chiusura della connessione.
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
