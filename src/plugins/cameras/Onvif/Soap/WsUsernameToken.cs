using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Cameras.Onvif.Soap;

public static class WsUsernameToken
{
  private const string PasswordDigestType =
    "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest";
  private const string Base64EncodingType =
    "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary";

  public static XElement Build(string username, string password) =>
    Build(username, password, RandomNumberGenerator.GetBytes(16), DateTime.UtcNow);

  public static XElement Build(string username, string password, byte[] nonce, DateTime created)
  {
    var createdStr = created.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
    var digest = ComputeDigest(nonce, createdStr, password);

    return new XElement(XmlHelpers.NsWsse + "Security",
      new XAttribute(XNamespace.Xmlns + "wsse", XmlHelpers.NsWsse),
      new XAttribute(XNamespace.Xmlns + "wsu", XmlHelpers.NsWsu),
      new XElement(XmlHelpers.NsWsse + "UsernameToken",
        new XElement(XmlHelpers.NsWsse + "Username", username),
        new XElement(XmlHelpers.NsWsse + "Password",
          new XAttribute("Type", PasswordDigestType),
          digest),
        new XElement(XmlHelpers.NsWsse + "Nonce",
          new XAttribute("EncodingType", Base64EncodingType),
          Convert.ToBase64String(nonce)),
        new XElement(XmlHelpers.NsWsu + "Created", createdStr)));
  }

  public static string ComputeDigest(byte[] nonce, string created, string password)
  {
    var createdBytes = Encoding.UTF8.GetBytes(created);
    var passwordBytes = Encoding.UTF8.GetBytes(password);
    var combined = new byte[nonce.Length + createdBytes.Length + passwordBytes.Length];
    nonce.CopyTo(combined, 0);
    createdBytes.CopyTo(combined, nonce.Length);
    passwordBytes.CopyTo(combined, nonce.Length + createdBytes.Length);
    return Convert.ToBase64String(SHA1.HashData(combined));
  }
}
