using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapGet("/debug/routes", (IEnumerable<EndpointDataSource> endpointSources) =>
        string.Join("\n", endpointSources.SelectMany(source => source.Endpoints)));
}

app.UseForwardedHeaders(new ForwardedHeadersOptions{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.MapGet("/", () => "Hello World!");
app.MapPost("/proxy", async (HttpRequest request, ILoggerFactory loggerFactory, IHttpClientFactory httpClientFactory) =>
{
    var _logger = loggerFactory.CreateLogger("Proxy");
    var _httpClient = httpClientFactory.CreateClient();
    _logger.LogTrace("Entered handler");

    var forwardUrl = request.Query["ddforward"];
    if (String.IsNullOrEmpty(forwardUrl))
    {
        _logger.LogWarning("Received empty string in ddforward");
        return Results.BadRequest();
    }

    var remoteIp = request.HttpContext.Connection.RemoteIpAddress;

    if (remoteIp == null)
    {
        _logger.LogWarning("Empty remote IP!");
        return Results.BadRequest();
    }

    Uri forwardUri;
    if (!Uri.TryCreate(forwardUrl, new UriCreationOptions { }, out forwardUri!))
    {
        _logger.LogWarning("Bad URL!");
        return Results.BadRequest();
    }

    StreamContent content = getContent(request);

    var requestMessage = new HttpRequestMessage
    {
        Method = HttpMethod.Post,
        RequestUri = forwardUri,
        Content = content
    };
    addHeaders(request, remoteIp, requestMessage);

    var response = await _httpClient.SendAsync(requestMessage);

    if (!response.IsSuccessStatusCode)
    {
        _logger.LogWarning("Call to DD Failed with content: " + await response.Content.ReadAsStringAsync());
    }
    return Results.Ok();
});
app.Run();

static StreamContent getContent(HttpRequest request)
{
    
    var requestBody = request.Body;
    var content = new StreamContent(requestBody);
    var contentType = request.ContentType ?? "text/plain";
    string charSet = "";
    if (contentType.Contains(";"))
    {
        var splits = contentType.Split(";");
        contentType = splits[0];
        charSet = Encoding.UTF8.WebName;
    }
    content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
    if (!String.IsNullOrEmpty(charSet))
    {
        content.Headers.ContentType.CharSet = charSet;
    }

    return content;
}

static void addHeaders(HttpRequest request, System.Net.IPAddress remoteIp, HttpRequestMessage requestMessage)
{
    var headersToCopy = new[] { "User-Agent", "Origin", "Referer", "Cookie", "sec-ch-ua", "sec-ch-ua-mobile", "sec-ch-ua-platform" };
    var headerValues = new Dictionary<string, string>();

    foreach (var header in headersToCopy)
    {
        if (!String.IsNullOrEmpty(request.Headers[header]))
        {
            requestMessage.Headers.Add(header, request.Headers[header].ToString());
        }
    }
    requestMessage.Headers.Add("Accept", "*/*");
    requestMessage.Headers.Add("X-Forwarded-For", remoteIp.ToString());
}