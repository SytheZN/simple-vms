using Server.Core.Services;
using Shared.Models;
using Shared.Models.Dto;

namespace Server.Api.Endpoints;

public static class EnrollmentEndpoints
{
  public static void Map(IEndpointRouteBuilder app)
  {
    app.MapPost("/api/v1/enroll", CompleteEnrollment);
    app.MapPost("/api/v1/clients/enroll", StartEnrollment);
  }

  private static IResult StartEnrollment(EnrollmentService enrollment)
  {
    var result = enrollment.StartEnrollment();
    return ApiResponse.Created(result, new DebugTag(ModuleIds.Enrollment, 0x0010));
  }

  private static async Task<IResult> CompleteEnrollment(
    EnrollRequest request,
    EnrollmentService enrollment,
    CancellationToken ct)
  {
    var result = await enrollment.CompleteEnrollmentAsync(request.Token, ct);
    return ApiResponse.Ok(result, new DebugTag(ModuleIds.Enrollment, 0x0011));
  }
}
