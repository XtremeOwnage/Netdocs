using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;

namespace Netdocs.Core.Optimization;

/// <summary>
/// Converts raster images (png/jpg) to webp, caching encoded output by source-content hash
/// under <c>.cache/webp/</c> so warm builds skip re-encoding unchanged images. Encoding is the
/// expensive step, so the cache keeps large image-heavy sites fast to rebuild.
/// </summary>
public sealed class WebpConverter(string cacheDir, int quality)
{
    private static readonly string[] ConvertibleExtensions = [".png", ".jpg", ".jpeg"];

    /// <summary>True when the file extension is a raster format we can convert to webp.</summary>
    public static bool IsConvertible(string path) =>
        ConvertibleExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns webp-encoded bytes for <paramref name="sourceBytes"/>, using the on-disk cache
    /// when a matching entry exists. Returns null if the image cannot be decoded.
    /// </summary>
    public async Task<byte[]?> ConvertAsync(byte[] sourceBytes, CancellationToken ct)
    {
        var key = Convert.ToHexString(SHA256.HashData(sourceBytes)) + "-q" + quality;
        var cachePath = Path.Combine(cacheDir, key + ".webp");
        if (File.Exists(cachePath))
        {
            try { return await File.ReadAllBytesAsync(cachePath, ct); }
            catch (IOException) { /* fall through and re-encode */ }
        }

        byte[] encoded;
        try
        {
            using var image = Image.Load(sourceBytes);
            using var ms = new MemoryStream();
            await image.SaveAsWebpAsync(ms, new WebpEncoder { Quality = quality }, ct);
            encoded = ms.ToArray();
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(cacheDir);
            await File.WriteAllBytesAsync(cachePath, encoded, ct);
        }
        catch (IOException) { /* cache write is best-effort */ }

        return encoded;
    }
}
