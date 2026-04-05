namespace Tests.Unit.Client.Mocks;

public sealed class FakeHttpHandler : HttpMessageHandler
{
  private readonly HttpResponseMessage? _response;
  private readonly HttpRequestException? _exception;

  public HttpRequestMessage? LastRequest { get; private set; }

  public FakeHttpHandler(HttpResponseMessage response) => _response = response;
  public FakeHttpHandler(HttpRequestException exception) => _exception = exception;

  protected override Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request, CancellationToken ct)
  {
    LastRequest = request;
    if (_exception != null)
      throw _exception;
    return Task.FromResult(_response!);
  }
}
