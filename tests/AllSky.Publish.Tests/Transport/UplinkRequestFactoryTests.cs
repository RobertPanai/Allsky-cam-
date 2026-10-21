using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace AllSky.Publish.Tests;

/// <summary>Test della richiesta multipart: parti, URL, header, profilo e assenza del token nei ToString.</summary>
public class UplinkRequestFactoryTests
{
    /// <summary>Legge il JSON della parte "meta" della richiesta (la richiesta resta valida).</summary>
    private static async Task<JsonObject> MetaOfAsync(HttpRequestMessage request)
    {
        byte[] body = await request.Content!.ReadAsByteArrayAsync();
        string boundary = UplinkTestSupport.BoundaryOf(request.Content.Headers.ContentType!.ToString())!;
        MultipartPart meta = UplinkTestSupport.ParseMultipart(body, boundary).Single(p => p.Name == "meta");
        return (JsonObject)JsonNode.Parse(meta.Text)!;
    }

    [Fact]
    public async Task Create_BuildsMultipartPostWithMetaAndImageParts()
    {
        byte[] jpeg = UplinkTestSupport.Jpeg();
        using HttpRequestMessage request = UplinkRequestFactory.Create(UplinkTestSupport.Item(), jpeg, UplinkTestSupport.Settings());

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(UplinkTestSupport.ServerUrl + "/api/uplink/allsky", request.RequestUri!.ToString());
        Assert.Equal("multipart/form-data", request.Content!.Headers.ContentType!.MediaType);

        byte[] body = await request.Content.ReadAsByteArrayAsync();
        string boundary = UplinkTestSupport.BoundaryOf(request.Content.Headers.ContentType.ToString())!;
        List<MultipartPart> parts = UplinkTestSupport.ParseMultipart(body, boundary);

        Assert.Equal(2, parts.Count);

        MultipartPart meta = parts[0];
        Assert.Equal("meta", meta.Name);
        Assert.Equal("application/json; charset=utf-8", meta.Header("Content-Type"));

        MultipartPart image = parts[1];
        Assert.Equal("image", image.Name);
        Assert.Equal("image/jpeg", image.Header("Content-Type"));
        Assert.Equal("frame.jpg", image.FileName);
        Assert.True(jpeg.AsSpan().SequenceEqual(image.Content), "Il contenuto dell'immagine non coincide con i byte del jpeg.");
    }

    [Fact]
    public async Task Create_MetaPartMatchesFixtureExceptCaptureIdAndCapturedAt()
    {
        // Il profilo della fixture arriva dalle impostazioni, come in produzione.
        PublishSettings settings = UplinkTestSupport.Settings(profileName: UplinkTestSupport.FixtureString("profileName"));
        using HttpRequestMessage request = UplinkRequestFactory.Create(UplinkTestSupport.Item(), UplinkTestSupport.Jpeg(), settings);
        JsonObject actual = await MetaOfAsync(request);
        JsonObject expected = UplinkTestSupport.LoadFixtureMeta();

        Assert.True(JsonNode.DeepEquals(
                UplinkTestSupport.Without(expected, "captureId", "capturedAt"),
                UplinkTestSupport.Without(actual, "captureId", "capturedAt")),
            "La parte meta non coincide con la fixture: " + actual.ToJsonString());

        Assert.Equal(UplinkTestSupport.FixtureString("captureId"), actual["captureId"]!.GetValue<string>());
        Assert.Equal("2026-10-21T21:30:00.000Z", actual["capturedAt"]!.GetValue<string>());
    }

    [Fact]
    public async Task Create_ContentLengthMatchesBodyWhenKnown()
    {
        // Il codice non imposta Content-Length: se il framework lo calcola (contenuto in memoria), deve coincidere col corpo.
        using HttpRequestMessage request = UplinkRequestFactory.Create(UplinkTestSupport.Item(), UplinkTestSupport.Jpeg(), UplinkTestSupport.Settings());
        byte[] body = await request.Content!.ReadAsByteArrayAsync();

        long? declared = request.Content.Headers.ContentLength;
        if (declared is not null)
            Assert.Equal((long)body.Length, declared.Value);
    }

    [Theory]
    [InlineData("https://uplink.test", "https://uplink.test/api/uplink/allsky")]
    [InlineData("https://uplink.test/", "https://uplink.test/api/uplink/allsky")]
    [InlineData("https://uplink.test///", "https://uplink.test/api/uplink/allsky")]
    [InlineData("https://uplink.test/sub/", "https://uplink.test/sub/api/uplink/allsky")]
    public void Create_JoinsUrlWithoutDoubleSlashes(string serverUrl, string expected)
    {
        PublishSettings settings = UplinkTestSupport.Settings() with { ServerUrl = serverUrl };
        using HttpRequestMessage request = UplinkRequestFactory.Create(UplinkTestSupport.Item(), UplinkTestSupport.Jpeg(), settings);

        Assert.Equal(expected, request.RequestUri!.ToString());
    }

    [Fact]
    public void Create_SetsBearerAuthorization()
    {
        using HttpRequestMessage request = UplinkRequestFactory.Create(UplinkTestSupport.Item(), UplinkTestSupport.Jpeg(), UplinkTestSupport.Settings());

        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal(UplinkTestSupport.TokenValue, request.Headers.Authorization.Parameter);
    }

    [Fact]
    public void Create_ExtraHeaderIsAddedWhenNameIsSet()
    {
        PublishSettings settings = UplinkTestSupport.Settings(extraHeaderName: "X-Uplink-Key");
        using HttpRequestMessage request = UplinkRequestFactory.Create(UplinkTestSupport.Item(), UplinkTestSupport.Jpeg(), settings);

        Assert.True(request.Headers.TryGetValues("X-Uplink-Key", out IEnumerable<string>? values), "Header extra assente.");
        Assert.Equal(UplinkTestSupport.ExtraValue, values!.Single());
    }

    [Fact]
    public void Create_NoExtraHeaderWhenNameIsNotSet()
    {
        using HttpRequestMessage request = UplinkRequestFactory.Create(UplinkTestSupport.Item(), UplinkTestSupport.Jpeg(), UplinkTestSupport.Settings());

        Assert.False(request.Headers.Contains("X-Uplink-Key"));
    }

    [Fact]
    public void Create_ReservedExtraHeaderNameIsIgnored()
    {
        // Un header extra non puo' sovrascrivere l'autenticazione.
        PublishSettings settings = UplinkTestSupport.Settings(extraHeaderName: "Authorization");
        using HttpRequestMessage request = UplinkRequestFactory.Create(UplinkTestSupport.Item(), UplinkTestSupport.Jpeg(), settings);

        Assert.Equal(UplinkTestSupport.TokenValue, request.Headers.Authorization!.Parameter);
        Assert.DoesNotContain(UplinkTestSupport.ExtraValue, request.ToString());
    }

    [Fact]
    public async Task Create_ProfileNameFromSettingsIsUsedInMeta()
    {
        PublishSettings settings = UplinkTestSupport.Settings(profileName: "Profilo impostazioni");
        using HttpRequestMessage request = UplinkRequestFactory.Create(UplinkTestSupport.Item(), UplinkTestSupport.Jpeg(), settings, "Profilo dispositivo");

        JsonObject meta = await MetaOfAsync(request);
        Assert.Equal("Profilo impostazioni", meta["profileName"]!.GetValue<string>());
    }

    [Fact]
    public async Task Create_DeviceProfileNameIsFallbackWhenSettingsHaveNone()
    {
        using HttpRequestMessage request = UplinkRequestFactory.Create(UplinkTestSupport.Item(), UplinkTestSupport.Jpeg(), UplinkTestSupport.Settings(), "Profilo dispositivo");

        JsonObject meta = await MetaOfAsync(request);
        Assert.Equal("Profilo dispositivo", meta["profileName"]!.GetValue<string>());
    }

    [Fact]
    public async Task Create_ProfileNameOmittedWhenNoneAvailable()
    {
        using HttpRequestMessage request = UplinkRequestFactory.Create(UplinkTestSupport.Item(), UplinkTestSupport.Jpeg(), UplinkTestSupport.Settings());

        JsonObject meta = await MetaOfAsync(request);
        Assert.False(meta.ContainsKey("profileName"));
    }

    [Fact]
    public void ToString_NeverContainsTokenOrExtraHeaderValue()
    {
        PublishSettings settings = UplinkTestSupport.Settings(extraHeaderName: "X-Uplink-Key");
        using HttpRequestMessage request = UplinkRequestFactory.Create(UplinkTestSupport.Item(), UplinkTestSupport.Jpeg(), settings);

        string text = request.ToString();

        Assert.DoesNotContain(UplinkTestSupport.TokenValue, text);
        Assert.DoesNotContain(UplinkTestSupport.ExtraValue, text);
        Assert.DoesNotContain("Authorization", text);
        Assert.Contains("POST", text);
    }

    [Fact]
    public void Uri_NeverContainsToken()
    {
        using HttpRequestMessage request = UplinkRequestFactory.Create(UplinkTestSupport.Item(), UplinkTestSupport.Jpeg(), UplinkTestSupport.Settings());

        Assert.DoesNotContain(UplinkTestSupport.TokenValue, request.RequestUri!.ToString());
    }

    [Fact]
    public void CreateHealth_IsGetWithBearerAndHealthUrl()
    {
        using HttpRequestMessage request = UplinkRequestFactory.CreateHealth(UplinkTestSupport.Settings());

        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(UplinkTestSupport.ServerUrl + "/api/uplink/health", request.RequestUri!.ToString());
        Assert.Equal(UplinkTestSupport.TokenValue, request.Headers.Authorization!.Parameter);
        Assert.DoesNotContain(UplinkTestSupport.TokenValue, request.ToString());
    }

    [Fact]
    public void Create_InvalidServerUrl_Throws()
    {
        PublishSettings settings = UplinkTestSupport.Settings() with { ServerUrl = "" };

        Assert.ThrowsAny<Exception>(() => UplinkRequestFactory.Create(UplinkTestSupport.Item(), UplinkTestSupport.Jpeg(), settings));
    }
}
