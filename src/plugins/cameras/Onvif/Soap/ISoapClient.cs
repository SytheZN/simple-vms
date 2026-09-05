using System.Xml.Linq;
using Shared.Models;

namespace Cameras.Onvif.Soap;

public interface ISoapClient
{
  Task<XElement> SendAsync(
    string uri,
    XElement body,
    Credentials? credentials = null,
    CancellationToken ct = default,
    bool logFaults = true);
}
