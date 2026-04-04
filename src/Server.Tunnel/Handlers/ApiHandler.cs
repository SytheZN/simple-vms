using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using MessagePack;
using Microsoft.Extensions.Logging;
using Server.Core.Routing;
using Shared.Models;
using Shared.Protocol;

namespace Server.Tunnel.Handlers;

internal static class ApiHandler
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
  };

  public static async Task RunAsync(
    ChannelReader<MuxMessage> reader,
    Func<ushort, ReadOnlyMemory<byte>, CancellationToken, Task> writeFn,
    ApiDispatcher dispatcher,
    IServiceProvider services,
    ILogger logger,
    CancellationToken ct)
  {
    var msg = await reader.ReadAsync(ct);
    var request = MessagePackSerializer.Deserialize<ApiRequestMessage>(
      msg.Payload, ProtocolSerializer.Options);

    var apiRequest = new ApiRequest
    {
      Method = request.Method,
      Path = request.Path,
      Services = services,
      BodyDeserializer = request.Body is { Length: > 0 }
        ? type => JsonSerializer.Deserialize(request.Body, type, JsonOptions)
        : null
    };

    ResponseEnvelope envelope;
    try
    {
      var result = dispatcher.TryDispatch(apiRequest, ct);
      if (result != null)
      {
        envelope = await result;
      }
      else
      {
        envelope = new ResponseEnvelope
        {
          Result = Result.NotFound,
          DebugTag = new DebugTag(ModuleIds.Tunnel, 0x0001),
          Message = $"No route for {request.Method} {request.Path}"
        };
      }
    }
    catch (Exception ex) when (
      ex is FormatException or KeyNotFoundException or JsonException)
    {
      envelope = new ResponseEnvelope
      {
        Result = Result.BadRequest,
        DebugTag = new DebugTag(ModuleIds.Tunnel, 0x0002),
        Message = ex.Message
      };
    }

    var response = new ApiResponseMessage
    {
      Result = (byte)envelope.Result,
      DebugTag = envelope.DebugTag.Value,
      Message = envelope.Message,
      Body = envelope.Body != null
        ? JsonSerializer.SerializeToUtf8Bytes(envelope.Body, envelope.Body.GetType(), JsonOptions)
        : null
    };

    var responseFlags = response.Body != null
      ? ApiResponseFlags.HasBody
      : ApiResponseFlags.None;

    var responsePayload = MessagePackSerializer.Serialize(
      response, ProtocolSerializer.Options);
    await writeFn((ushort)responseFlags, responsePayload, ct);
  }
}
