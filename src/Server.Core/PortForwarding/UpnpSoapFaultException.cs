namespace Server.Core.PortForwarding;

public sealed class UpnpSoapFaultException : Exception
{
  public int? ErrorCode { get; }
  public string? ErrorDescription { get; }
  public string RawFault { get; }

  public UpnpSoapFaultException(int? code, string? description, string rawFault)
    : base(description != null
        ? $"UPnP fault {code?.ToString() ?? "?"}: {description}"
        : $"UPnP fault: {rawFault}")
  {
    ErrorCode = code;
    ErrorDescription = description;
    RawFault = rawFault;
  }
}
