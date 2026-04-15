using System.Net;

namespace ManInBlack.AI.Tests.Helpers;

public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        _responseFactory = responseFactory;
    }

    public MockHttpMessageHandler(string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK)
        : this(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseBody, new System.Text.UTF8Encoding(false), "application/json")
        })
    { }

    public MockHttpMessageHandler(Stream responseStream, HttpStatusCode statusCode = HttpStatusCode.OK)
        : this(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StreamContent(responseStream)
        })
    { }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastRequestBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        return _responseFactory(request);
    }
}
