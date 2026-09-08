using System.Diagnostics.CodeAnalysis;
using System.Web;

namespace Client.Core;

public static class EnrollmentLink
{
  public const string Scheme = "svms";
  public const string Host = "enroll";
  private const string AddressParam = "address";
  private const string TokenParam = "token";

  public static bool TryParse(
    string? link,
    [NotNullWhen(true)] out string? address,
    [NotNullWhen(true)] out string? token)
  {
    address = null;
    token = null;

    if (!Uri.TryCreate(link, UriKind.Absolute, out var uri)) return false;
    if (!uri.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase)) return false;
    if (!uri.Host.Equals(Host, StringComparison.OrdinalIgnoreCase)) return false;

    var query = HttpUtility.ParseQueryString(uri.Query);
    var parsedAddress = query[AddressParam];
    var parsedToken = query[TokenParam];
    if (string.IsNullOrEmpty(parsedAddress) || string.IsNullOrEmpty(parsedToken)) return false;

    address = parsedAddress;
    token = parsedToken;
    return true;
  }
}
