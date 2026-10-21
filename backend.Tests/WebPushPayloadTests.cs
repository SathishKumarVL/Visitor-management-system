using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tiaano.Vms.Api.Services;
using Xunit;
using static Tiaano.Vms.Api.Services.WebPushProtocol;

namespace Tiaano.Vms.Api.Tests;

public class WebPushPayloadTests
{
    [Fact]
    public void Encryption_Matches_The_RFC_8291_Test_Vector()
    {
        var senderPublic = Base64UrlDecode("BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8");
        using var sender = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = Base64UrlDecode("yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw"),
            Q = new ECPoint { X = senderPublic[1..33], Y = senderPublic[33..65] }
        });

        var body = Encrypt(
            Base64UrlDecode("BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4"),
            Base64UrlDecode("BTBZMqHH6r4Tts7J_aSIgg"),
            Encoding.UTF8.GetBytes("When I grow up, I want to be a watermelon"),
            sender,
            Base64UrlDecode("DGv6ra1nlYgDCS1FRnbzlw"));

        Assert.Equal(
            "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPTpK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN",
            Base64UrlEncode(body));
    }

    [Fact]
    public void Vapid_Header_Is_A_Valid_ES256_Token_For_The_Push_Service()
    {
        var vapid = GenerateVapidKeys();
        using var browser = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var q = browser.ExportParameters(false).Q;

        using var request = CreateRequest(
            "https://web.push.apple.com/QGuQyavXutnMH",
            Base64UrlEncode([0x04, .. q.X!, .. q.Y!]),
            Base64UrlEncode(RandomNumberGenerator.GetBytes(16)),
            """{"title":"Tiaano VMS"}""",
            vapid,
            "mailto:noreply@tiaano.com");

        Assert.Contains("aes128gcm", request.Content!.Headers.ContentEncoding);
        var auth = request.Headers.GetValues("Authorization").Single();
        Assert.StartsWith("vapid t=", auth);
        Assert.EndsWith($", k={vapid.PublicKey}", auth);

        var jwt = auth["vapid t=".Length..auth.IndexOf(',')];
        var parts = jwt.Split('.');
        var claims = JsonDocument.Parse(Base64UrlDecode(parts[1])).RootElement;
        Assert.Equal("https://web.push.apple.com", claims.GetProperty("aud").GetString());
        Assert.Equal("mailto:noreply@tiaano.com", claims.GetProperty("sub").GetString());

        var publicKey = Base64UrlDecode(vapid.PublicKey);
        using var verifier = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = publicKey[1..33], Y = publicKey[33..65] }
        });
        Assert.True(verifier.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), Base64UrlDecode(parts[2]),
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }
}
