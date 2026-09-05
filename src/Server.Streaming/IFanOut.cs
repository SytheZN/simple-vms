using Microsoft.Extensions.Logging;
using Shared.Models;

namespace Server.Streaming;

public interface IDataStreamFanOut : IAsyncDisposable
{
  int SubscriberCount { get; }
  int GetDemand();
  Action? Changed { get; set; }
  ILogger? Logger { get; set; }
  void Write(IDataUnit item);
  IDataStream Subscribe(int capacity = 256);
  IDataStream SubscribePassive(int capacity = 256);
}

public interface IMuxStreamFanOut : IMuxStream, IAsyncDisposable
{
  int SubscriberCount { get; }
  int GetDemand();
  Action? Changed { get; set; }
  ILogger? Logger { get; set; }
  IMuxStream Subscribe(int capacity = 256);
}
