using Server.Core;
using Shared.Models;

namespace Server.Api;

public static class ApiResponse
{
  public static IResult Err(Error error) =>
    Results.Json(
      new ResponseEnvelope
      {
        Result = error.Result,
        DebugTag = error.Tag,
        Message = error.Message
      },
      ServerJsonContext.Default.ResponseEnvelope,
      statusCode: error.Result.ToHttpStatusCode());
}
