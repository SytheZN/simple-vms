using System.Collections.Concurrent;
using System.Xml.Linq;
using Shared.Models;

namespace Cameras.Onvif.Soap;

public sealed class PacedSoapClient : ISoapClient
{
  private readonly ISoapClient _inner;
  private readonly int _maxConcurrentPerHost;
  private readonly TimeSpan _minStartGap;
  private readonly ConcurrentDictionary<string, HostLimiter> _limiters = new();

  public PacedSoapClient(ISoapClient inner, int maxConcurrentPerHost, TimeSpan minStartGap)
  {
    _inner = inner;
    _maxConcurrentPerHost = maxConcurrentPerHost;
    _minStartGap = minStartGap;
  }

  public async Task<XElement> SendAsync(
    string uri,
    XElement body,
    Credentials? credentials = null,
    CancellationToken ct = default,
    bool logFaults = true)
  {
    var limiter = _limiters.GetOrAdd(
      SoapClient.GetHostKey(uri),
      _ => new HostLimiter(_maxConcurrentPerHost, _minStartGap));
    await limiter.AcquireAsync(ct);
    try
    {
      return await _inner.SendAsync(uri, body, credentials, ct, logFaults);
    }
    finally
    {
      limiter.Release();
    }
  }

  private sealed class HostLimiter
  {
    private readonly SemaphoreSlim _concurrency;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly TimeSpan _minGap;
    private DateTimeOffset _nextStartTime = DateTimeOffset.MinValue;

    public HostLimiter(int maxConcurrent, TimeSpan minGap)
    {
      _concurrency = new SemaphoreSlim(maxConcurrent, maxConcurrent);
      _minGap = minGap;
    }

    public async Task AcquireAsync(CancellationToken ct)
    {
      await _concurrency.WaitAsync(ct);
      try
      {
        await _startGate.WaitAsync(ct);
        try
        {
          var wait = _nextStartTime - DateTimeOffset.UtcNow;
          if (wait > TimeSpan.Zero)
            await Task.Delay(wait, ct);
          _nextStartTime = DateTimeOffset.UtcNow + _minGap;
        }
        finally
        {
          _startGate.Release();
        }
      }
      catch
      {
        _concurrency.Release();
        throw;
      }
    }

    public void Release() => _concurrency.Release();
  }
}
