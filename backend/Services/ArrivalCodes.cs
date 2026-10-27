using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using QRCoder;

namespace Tiaano.Vms.Api.Services;

/// <summary>
/// The code in the QR emailed with an appointment. It only finds the booking at reception; the visitor's face
/// still has to match before they are let in, so a forwarded email is not a way through the door.
/// </summary>
public static class ArrivalCodes
{
    public const string Prefix = "TVMS-A:";

    public static string New() => Prefix + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16));

    /// <summary>Hash of the code as scanned, with or without the prefix; null when it cannot be one of ours.</summary>
    public static string? HashOf(string? scanned)
    {
        var code = scanned?.Trim() ?? "";
        if (code.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) code = code[Prefix.Length..];
        if (code.Length is < 16 or > 64) return null;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
    }
}

public static class QrImage
{
    public static byte[] Png(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(10);
    }
}
