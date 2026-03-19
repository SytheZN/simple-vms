using System.Security.Cryptography;
using System.Text;
using Cameras.Onvif.Soap;

namespace Tests.Unit.Onvif;

[TestFixture]
public class WsUsernameTokenTests
{
  [Test]
  public void ComputeDigest_KnownVector_MatchesExpected()
  {
    var nonce = Convert.FromBase64String("LKqI6G/AikKCQrN0zqZFlg==");
    var created = "2010-09-16T07:50:45.000Z";
    var password = "userpassword";

    var expected = ComputeExpectedDigest(nonce, created, password);
    var actual = WsUsernameToken.ComputeDigest(nonce, created, password);

    Assert.That(actual, Is.EqualTo(expected));
  }

  [Test]
  public void ComputeDigest_EmptyPassword_Succeeds()
  {
    var nonce = new byte[16];
    var created = "2024-01-01T00:00:00.000Z";

    var digest = WsUsernameToken.ComputeDigest(nonce, created, "");

    Assert.That(digest, Is.Not.Null.And.Not.Empty);
  }

  [Test]
  public void Build_ProducesValidSecurityElement()
  {
    var nonce = new byte[16];
    Array.Fill<byte>(nonce, 0xAB);
    var created = new DateTime(2024, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    var element = WsUsernameToken.Build("admin", "secret", nonce, created);

    Assert.That(element.Name, Is.EqualTo(XmlHelpers.NsWsse + "Security"));

    var token = element.Element(XmlHelpers.NsWsse + "UsernameToken");
    Assert.That(token, Is.Not.Null);

    var username = token!.Element(XmlHelpers.NsWsse + "Username")!.Value;
    Assert.That(username, Is.EqualTo("admin"));

    var nonceEl = token.Element(XmlHelpers.NsWsse + "Nonce")!.Value;
    Assert.That(nonceEl, Is.EqualTo(Convert.ToBase64String(nonce)));

    var createdEl = token.Element(XmlHelpers.NsWsu + "Created")!.Value;
    Assert.That(createdEl, Is.EqualTo("2024-06-15T12:00:00.000Z"));
  }

  [Test]
  public void Build_DifferentNonces_ProduceDifferentDigests()
  {
    var nonce1 = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
    var nonce2 = new byte[] { 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 };
    var created = "2024-01-01T00:00:00.000Z";

    var digest1 = WsUsernameToken.ComputeDigest(nonce1, created, "pass");
    var digest2 = WsUsernameToken.ComputeDigest(nonce2, created, "pass");

    Assert.That(digest1, Is.Not.EqualTo(digest2));
  }

  private static string ComputeExpectedDigest(byte[] nonce, string created, string password)
  {
    var createdBytes = Encoding.UTF8.GetBytes(created);
    var passwordBytes = Encoding.UTF8.GetBytes(password);
    var combined = new byte[nonce.Length + createdBytes.Length + passwordBytes.Length];
    Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
    Buffer.BlockCopy(createdBytes, 0, combined, nonce.Length, createdBytes.Length);
    Buffer.BlockCopy(passwordBytes, 0, combined, nonce.Length + createdBytes.Length, passwordBytes.Length);
    return Convert.ToBase64String(SHA1.HashData(combined));
  }
}
