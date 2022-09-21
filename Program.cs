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
app.MapPost("/proxy", async (HttpRequest request, ILoggerFactory loggerFactory, IHttpClientFactory httpClientFactory) => {
    var _logger = loggerFactory.CreateLogger("Proxy");
    var _httpClient = httpClientFactory.CreateClient();
    _logger.LogTrace("Entered handler");

        var forwardUrl = request.Query["ddforward"];
        if (String.IsNullOrEmpty(forwardUrl)) {
            _logger.LogWarning("Received empty string in ddforward");
            return Results.BadRequest();
        }

        var remoteIp = request.HttpContext.Connection.RemoteIpAddress;

        if (remoteIp == null) {
            _logger.LogWarning("Empty remote IP!");
            return Results.BadRequest();
        }

        Uri forwardUri;
        if (!Uri.TryCreate(forwardUrl, new UriCreationOptions{}, out forwardUri!)) {
            _logger.LogWarning("Bad URL!");
            return Results.BadRequest();
        }

        var requestBody = request.Body;

        var content = new StreamContent(requestBody);
        var requestMessage = new HttpRequestMessage{
            Method = HttpMethod.Post,
            RequestUri = forwardUri,
            Headers = {
                {"X-Forwarded-For", remoteIp.ToString()},
            },
            Content = content
        };
        await _httpClient.PostAsync(forwardUrl, content);
        return Results.Ok();
});
app.Run();
