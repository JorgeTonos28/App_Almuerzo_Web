using System.Buffers.Binary;
using System.IO.Compression;
using Almuerzo.Web.Data;
using Almuerzo.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Almuerzo.Web.Services;

public record BrandingSettings(string FooterText, string FaviconUrl, string FooterSignatureUrl);
public class BrandingService(AppDbContext db)
{
    private const string DisabledFooterSignature = "__DISABLED__";
    private const string DefaultFooterText = "INFOTEP - DIAET © 2026";
    private const string DefaultFooterSignature = "/assets/signature.png";
    public static readonly string[] Keys = ["FOOTER_TEXT", "FAVICON_ASSET_ID", "FOOTER_STAMP_ASSET_ID"];
    public async Task<BrandingSettings> Get()
    {
        var cfg = await db.Settings.AsNoTracking().Where(x => Keys.Contains(x.Key)).ToDictionaryAsync(x => x.Key, x => x.Value);
        static string Url(string? id) => Guid.TryParseExact(id, "N", out _) ? "/branding/" + id + ".png" : "";
        var icon = Url(cfg.GetValueOrDefault("FAVICON_ASSET_ID"));
        var stampId = cfg.GetValueOrDefault("FOOTER_STAMP_ASSET_ID");
        var stamp = stampId == DisabledFooterSignature ? "" : Url(stampId);
        return new(cfg.GetValueOrDefault("FOOTER_TEXT", DefaultFooterText), icon.Length == 0 ? "/assets/favicon.svg" : icon, stamp.Length == 0 && stampId != DisabledFooterSignature ? DefaultFooterSignature : stamp);
    }

    public async Task<BrandingSettings> Save(string footer, IFormFile? favicon, IFormFile? stamp, bool resetFavicon, bool removeStamp, string actor, CancellationToken ct)
    {
        if (footer.Length > 200 || footer.Any(x => char.IsControl(x) && x is not ('\r' or '\n'))) throw new BusinessException("El texto del pie admite hasta 200 caracteres.");
        if (resetFavicon && favicon != null || removeStamp && stamp != null) throw new BusinessException("Selecciona una imagen o elimina la anterior.");
        var iconBytes = favicon == null ? null : await ReadImage(favicon, 512_000, 512, 512, ct);
        var stampBytes = stamp == null ? null : await ReadImage(stamp, 2_000_000, 2400, 1200, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        async Task Set(string key, string value)
        {
            var setting = await db.Settings.FindAsync([db.RegionId, key], ct);
            if (setting == null) { setting = new Setting { Key = key }; db.Settings.Add(setting); }
            setting.Value = value;
        }
        async Task Replace(string key, byte[]? content, bool clear)
        {
            if (content == null && !clear) return;
            var oldId = (await db.Settings.FindAsync([db.RegionId, key], ct))?.Value;
            var asset = content == null ? null : new BrandingAsset { Content = content };
            if (asset != null) db.BrandingAssets.Add(asset);
            await Set(key, clear ? DisabledFooterSignature : asset?.Id ?? "");
            if (!string.IsNullOrEmpty(oldId) && await db.BrandingAssets.FindAsync([oldId], ct) is { } old) db.BrandingAssets.Remove(old);
        }
        await Set("FOOTER_TEXT", footer.Trim());
        await Replace("FAVICON_ASSET_ID", iconBytes, resetFavicon);
        await Replace("FOOTER_STAMP_ASSET_ID", stampBytes, removeStamp);
        db.Audit.Add(new AuditEvent { Actor = actor, Action = "SaveBranding", EntityId = "branding" });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return await Get();
    }

    private static async Task<byte[]> ReadImage(IFormFile file, int limit, int maxWidth, int maxHeight, CancellationToken ct)
    {
        if (!Path.GetExtension(file.FileName).Equals(".png", StringComparison.OrdinalIgnoreCase) || file.Length is <= 0 || file.Length > limit) throw new BusinessException($"Usa una imagen PNG de hasta {limit / 1000} KB.");
        await using var source = file.OpenReadStream(); using var data = new MemoryStream(); var buffer = new byte[8192]; int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0) { if (data.Length + read > limit) throw new BusinessException("Imagen demasiado grande."); data.Write(buffer, 0, read); }
        return SanitizePng(data.ToArray(), maxWidth, maxHeight);
    }

    // Verify PNG structure, checksums, dimensions and bounded raster data; discard ancillary metadata.
    private static byte[] SanitizePng(byte[] data, int maxWidth, int maxHeight)
    {
        static BusinessException Invalid() => new("PNG inválido. Usa una imagen estática, sin entrelazado, con dimensiones dentro del límite indicado.");
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (data.Length < 45 || !data.AsSpan(0, 8).SequenceEqual(signature)) throw Invalid();
        using var output = new MemoryStream(); output.Write(signature); using var compressed = new MemoryStream();
        int position = 8, width = 0, height = 0, depth = 0, channels = 0, color = 0, paletteEntries = 0; bool ended = false, sawData = false;
        while (position + 12 <= data.Length)
        {
            var size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position, 4));
            if (size > data.Length - position - 12) throw Invalid();
            int length = (int)size; var type = System.Text.Encoding.ASCII.GetString(data, position + 4, 4); var chunk = data.AsSpan(position + 8, length);
            uint crc = 0xffffffff;
            foreach (var b in data.AsSpan(position + 4, length + 4)) { crc ^= b; for (int bit = 0; bit < 8; bit++) crc = crc >> 1 ^ (0xedb88320u & (uint)-(int)(crc & 1)); }
            if (~crc != BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position + 8 + length, 4))) throw Invalid();
            if (position == 8 && type != "IHDR" || width == 0 && type != "IHDR") throw Invalid();
            if (type == "IHDR")
            {
                if (position != 8 || length != 13) throw Invalid();
                uint w = BinaryPrimitives.ReadUInt32BigEndian(chunk), h = BinaryPrimitives.ReadUInt32BigEndian(chunk[4..]);
                if (w == 0 || h == 0 || w > maxWidth || h > maxHeight || w * h > 2_880_000) throw Invalid();
                width = (int)w; height = (int)h; depth = chunk[8]; color = chunk[9];
                channels = color switch { 0 or 3 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 0 };
                if (channels == 0 || (color == 3 ? depth is not (1 or 2 or 4 or 8) : color == 0 ? depth is not (1 or 2 or 4 or 8 or 16) : depth is not (8 or 16)) || chunk[10] != 0 || chunk[11] != 0 || chunk[12] != 0) throw Invalid();
            }
            else if (type == "PLTE") { if (sawData || length is 0 or > 768 || length % 3 != 0 || paletteEntries > 0) throw Invalid(); paletteEntries = length / 3; }
            else if (type == "tRNS") { if (sawData || (color == 3 ? paletteEntries == 0 || length > paletteEntries : color == 0 ? length != 2 : color == 2 ? length != 6 : true)) throw Invalid(); }
            else if (type == "IDAT") { if (color == 3 && paletteEntries == 0) throw Invalid(); compressed.Write(chunk); sawData = true; }
            else if (type == "IEND") { if (length != 0 || !sawData || position + 12 != data.Length) throw Invalid(); ended = true; }
            else if (type.Length != 4 || type.Any(x => !char.IsAsciiLetter(x)) || char.IsUpper(type[0])) throw Invalid();
            if (type is "IHDR" or "PLTE" or "tRNS" or "IDAT" or "IEND") output.Write(data, position, length + 12);
            position += length + 12;
            if (ended) break;
        }
        if (!ended) throw Invalid();
        compressed.Position = 0;
        try
        {
            using var raster = new ZLibStream(compressed, CompressionMode.Decompress);
            var row = new byte[(width * channels * depth + 7) / 8];
            for (int y = 0; y < height; y++) { var filter = raster.ReadByte(); if (filter is < 0 or > 4) throw Invalid(); raster.ReadExactly(row); }
            if (raster.ReadByte() != -1) throw Invalid();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { throw Invalid(); }
        return output.ToArray();
    }
}
