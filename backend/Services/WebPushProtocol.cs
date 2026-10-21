using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tiaano.Vms.Api.Services;

/// <summary>
/// Web Push wire format: payload encryption (RFC 8291, aes128gcm) and VAPID authorization (RFC 8292).
/// These are the versions every current push service accepts, Apple's included.
/// </summary>
public static class WebPushProtocol
{
    private const int RecordSize = 4096;

    public sealed record VapidKeys(string PublicKey, string PrivateKey);

    public static VapidKeys GenerateVapidKeys()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = key.ExportParameters(true);
        return new VapidKeys(Base64UrlEncode(UncompressedPoint(p.Q)), Base64UrlEncode(p.D!));
    }

    /// <summary>Builds the POST to the subscription's push service.</summary>
    public static HttpRequestMessage CreateRequest(
        string endpoint, string p256dh, string auth, string payload, VapidKeys vapid, string subject, int ttlSeconds = 86400)
    {
        var body = Encrypt(Base64UrlDecode(p256dh), Base64UrlDecode(auth), Encoding.UTF8.GetBytes(payload));
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new("application/octet-stream");
        request.Content.Headers.ContentEncoding.Add("aes128gcm");
        request.Headers.TryAddWithoutValidation("TTL", ttlSeconds.ToString());
        request.Headers.TryAddWithoutValidation("Urgency", "high");
        request.Headers.TryAddWithoutValidation("Authorization",
            $"vapid t={CreateVapidJwt(new Uri(endpoint), vapid, subject)}, k={vapid.PublicKey}");
        return request;
    }

    /// <summary>One aes128gcm record: salt | record size | key id length | sender public key | ciphertext.</summary>
    /// <param name="senderKey">Fixed only to reproduce test vectors; a fresh key per message otherwise.</param>
    /// <param name="salt">Fixed only to reproduce test vectors; random otherwise.</param>
    public static byte[] Encrypt(
        byte[] receiverPublicKey, byte[] authSecret, byte[] plaintext, ECDiffieHellman? senderKey = null, byte[]? salt = null)
    {
        if (receiverPublicKey.Length != 65 || receiverPublicKey[0] != 0x04)
            throw new InvalidOperationException("The browser's push key is not a P-256 public key.");
        if (authSecret.Length < 16)
            throw new InvalidOperationException("The browser's push auth secret is too short.");
        // 16-byte tag + 1 delimiter byte must fit in one record.
        if (plaintext.Length > RecordSize - 103)
            throw new InvalidOperationException("Push message is too long.");

        using var receiver = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = receiverPublicKey[1..33], Y = receiverPublicKey[33..65] }
        });
        using var ownedSender = senderKey is null ? ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256) : null;
        var sender = senderKey ?? ownedSender!;
        var senderPublicKey = UncompressedPoint(sender.ExportParameters(false).Q);
        var sharedSecret = sender.DeriveRawSecretAgreement(receiver.PublicKey);

        var keyInfo = Concat(Encoding.ASCII.GetBytes("WebPush: info\0"), receiverPublicKey, senderPublicKey);
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32, authSecret, keyInfo);

        salt ??= RandomNumberGenerator.GetBytes(16);
        var cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salt, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salt, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

        var padded = new byte[plaintext.Length + 1];
        plaintext.CopyTo(padded, 0);
        padded[^1] = 0x02; // last-record delimiter
        var ciphertext = new byte[padded.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(cek, 16))
            aes.Encrypt(nonce, padded, ciphertext, tag);

        var header = new byte[16 + 4 + 1];
        salt.CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16), RecordSize);
        header[20] = (byte)senderPublicKey.Length;
        return Concat(header, senderPublicKey, ciphertext, tag);
    }

    public static string CreateVapidJwt(Uri endpoint, VapidKeys vapid, string subject)
    {
        var publicKey = Base64UrlDecode(vapid.PublicKey);
        using var key = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = Base64UrlDecode(vapid.PrivateKey),
            Q = new ECPoint { X = publicKey[1..33], Y = publicKey[33..65] }
        });
        var header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new { typ = "JWT", alg = "ES256" }));
        var claims = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            aud = endpoint.GetLeftPart(UriPartial.Authority),
            exp = DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds(),
            sub = subject
        }));
        var signingInput = $"{header}.{claims}";
        var signature = key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{signingInput}.{Base64UrlEncode(signature)}";
    }

    public static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Base64UrlDecode(string value)
    {
        var s = value.Trim().Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }

    private static byte[] UncompressedPoint(ECPoint q) => Concat([0x04], q.X!, q.Y!);

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }
        return result;
    }
}
