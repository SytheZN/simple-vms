namespace Shared.Models;

public static class ResultExtensions
{
  public static int ToHttpStatusCode(this Result result) => result switch
  {
    Result.Success => 200,
    Result.Created => 201,
    Result.NotFound => 404,
    Result.BadRequest => 400,
    Result.Conflict => 409,
    Result.Unauthorized => 401,
    Result.Forbidden => 403,
    Result.InternalError => 500,
    Result.Unavailable => 503,
    _ => 500
  };
}
