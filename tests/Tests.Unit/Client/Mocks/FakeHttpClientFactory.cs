namespace Tests.Unit.Client.Mocks;

public sealed class FakeHttpClientFactory : IHttpClientFactory
{
  private readonly HttpMessageHandler _handler;

  public FakeHttpClientFactory(HttpMessageHandler handler)
  {
    _handler = handler;
  }

  public HttpClient CreateClient(string name) => new(_handler);
}
