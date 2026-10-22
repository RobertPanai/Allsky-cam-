using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using AllSky.YouTube;
using Xunit;

namespace AllSky.YouTube.Tests;

public sealed class LoopbackCallbackListenerTests
{
    private static readonly TimeSpan LongTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void Start_SoloLoopbackSuPortaLibera()
    {
        using LoopbackCallbackListener listener = LoopbackCallbackListener.Start();

        Assert.StartsWith("http://127.0.0.1:", listener.RedirectUri);
        Assert.InRange(new Uri(listener.RedirectUri).Port, 1024, 65535);
    }

    [Fact]
    public async Task Callback_StateCorretto_RestituisceCodiceERispostaFissa()
    {
        using LoopbackCallbackListener listener = LoopbackCallbackListener.Start();
        using HttpClient http = LocalHttp.Create();

        Task<LoopbackCallback> wait = listener.WaitForCallbackAsync("stato-1", LongTimeout);
        using HttpResponseMessage response = await http.GetAsync(listener.RedirectUri + "/?code=codice-di-prova&state=stato-1");
        string body = await response.Content.ReadAsStringAsync();
        LoopbackCallback callback = await wait;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("codice-di-prova", callback.Code.Reveal());
        Assert.Null(callback.Error);
        Assert.Contains("Accesso completato", body);
        Assert.DoesNotContain("codice-di-prova", body);
    }

    [Fact]
    public async Task StateErrato_NonAccetta_eContinuaAdAttendere()
    {
        using LoopbackCallbackListener listener = LoopbackCallbackListener.Start();
        using HttpClient http = LocalHttp.Create();

        Task<LoopbackCallback> wait = listener.WaitForCallbackAsync("stato-giusto", LongTimeout);

        using HttpResponseMessage wrong = await http.GetAsync(listener.RedirectUri + "/?code=falso&state=stato-sbagliato");
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.False(wait.IsCompleted);

        using HttpResponseMessage right = await http.GetAsync(listener.RedirectUri + "/?code=vero&state=stato-giusto");
        LoopbackCallback callback = await wait;
        Assert.Equal("vero", callback.Code.Reveal());
    }

    [Fact]
    public async Task MetodoDiversoDaGet_E_RispostaSenzaAccettazione()
    {
        using LoopbackCallbackListener listener = LoopbackCallbackListener.Start();
        using HttpClient http = LocalHttp.Create();

        Task<LoopbackCallback> wait = listener.WaitForCallbackAsync("stato-1", LongTimeout);
        using HttpResponseMessage post = await http.PostAsync(listener.RedirectUri + "/?code=x&state=stato-1", new StringContent(string.Empty));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
        Assert.False(wait.IsCompleted);

        using HttpResponseMessage get = await http.GetAsync(listener.RedirectUri + "/?code=vero&state=stato-1");
        Assert.Equal("vero", (await wait).Code.Reveal());
    }

    [Fact]
    public async Task ErroreDelProvider_RestituisceErrorePerMaiCodice()
    {
        using LoopbackCallbackListener listener = LoopbackCallbackListener.Start();
        using HttpClient http = LocalHttp.Create();

        Task<LoopbackCallback> wait = listener.WaitForCallbackAsync("stato-1", LongTimeout);
        using HttpResponseMessage response = await http.GetAsync(listener.RedirectUri + "/?error=access_denied&state=stato-1");
        string body = await response.Content.ReadAsStringAsync();
        LoopbackCallback callback = await wait;

        Assert.Equal("access_denied", callback.Error);
        Assert.Equal(string.Empty, callback.Code.Reveal());
        Assert.Contains("non concessa", body);
        Assert.DoesNotContain("access_denied", body);
    }

    [Fact]
    public void ToString_DelCallback_NonMostraIlCodice()
    {
        var callback = new LoopbackCallback(new OAuthSecret("codice-segreto-prova"), null);

        Assert.DoesNotContain("codice-segreto-prova", callback.ToString());
    }

    [Fact]
    public async Task Timeout_LanciaTimeoutESiLiberaLaPorta()
    {
        string redirectUri;
        using (LoopbackCallbackListener listener = LoopbackCallbackListener.Start())
        {
            redirectUri = listener.RedirectUri;
            await Assert.ThrowsAsync<TimeoutException>(() => listener.WaitForCallbackAsync("stato", TimeSpan.FromMilliseconds(200)));
        }

        // Dopo Dispose la porta e' di nuovo libera: il listener si e' chiuso.
        var probe = new TcpListener(IPAddress.Loopback, new Uri(redirectUri).Port);
        probe.Start();
        probe.Stop();
    }

    [Fact]
    public async Task Cancellazione_LanciaOperationCanceled()
    {
        using LoopbackCallbackListener listener = LoopbackCallbackListener.Start();
        using var cancellation = new CancellationTokenSource();

        Task<LoopbackCallback> wait = listener.WaitForCallbackAsync("stato", LongTimeout, cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }

    [Fact]
    public void Dispose_EChiamateRipetute_NonLanciano()
    {
        var listener = LoopbackCallbackListener.Start();

        listener.Dispose();
        listener.Dispose();
    }
}
