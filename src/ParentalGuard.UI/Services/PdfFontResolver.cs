using System.Buffers.Binary;
using PdfSharp.Fonts;

namespace ParentalGuard.UI.Services;

/// <summary>
/// `MISC-011` (2026-10-07) — font cho PDF lấy thẳng từ thư mục Fonts của Windows (không kèm font trong bản cài):
/// <see cref="LatinFamily"/> = Arial (đủ dấu tiếng Việt/Pháp/Tây Ban Nha/Bồ Đào Nha), <see cref="CjkFamily"/> = Microsoft YaHei.
/// YaHei chỉ có dạng tập hợp <c>.ttc</c> mà PDFsharp không đọc trực tiếp — tách font đầu tiên ra thành TTF độc lập.
/// </summary>
internal sealed class PdfFontResolver : IFontResolver
{
    public const string LatinFamily = "PG Latin";
    public const string CjkFamily = "PG CJK";

    private static readonly string _fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        bool cjk = string.Equals(familyName, CjkFamily, StringComparison.Ordinal) && File.Exists(Path.Combine(_fontsDir, "msyh.ttc"));
        string face = (cjk ? "cjk" : "latin") + (isBold ? "-bold" : string.Empty);
        return new FontResolverInfo(face);
    }

    public byte[]? GetFont(string faceName) => faceName switch
    {
        "cjk" => ExtractFirstFont(File.ReadAllBytes(Path.Combine(_fontsDir, "msyh.ttc"))),
        "cjk-bold" => ExtractFirstFont(File.ReadAllBytes(Path.Combine(_fontsDir, File.Exists(Path.Combine(_fontsDir, "msyhbd.ttc")) ? "msyhbd.ttc" : "msyh.ttc"))),
        "latin-bold" => File.ReadAllBytes(Path.Combine(_fontsDir, "arialbd.ttf")),
        _ => File.ReadAllBytes(Path.Combine(_fontsDir, "arial.ttf")),
    };

    /// <summary>TTC → TTF: chép bảng thư mục của font số 0 và từng bảng nó tham chiếu, đánh lại offset (căn 4 byte).</summary>
    internal static byte[] ExtractFirstFont(byte[] data)
    {
        if (data.Length < 12 || BinaryPrimitives.ReadUInt32BigEndian(data) != 0x74746366) // "ttcf"
        {
            return data; // vốn là TTF
        }

        int fontOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12));
        int numTables = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(fontOffset + 4));
        int headerSize = 12 + (16 * numTables);

        byte[] result = new byte[headerSize];
        Array.Copy(data, fontOffset, result, 0, headerSize);

        var tables = new List<byte[]>(numTables);
        int nextOffset = headerSize;
        for (int i = 0; i < numTables; i++)
        {
            int record = 12 + (16 * i);
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(result.AsSpan(record + 8));
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(result.AsSpan(record + 12));
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(record + 8), (uint)nextOffset);
            byte[] table = new byte[(length + 3) & ~3];
            Array.Copy(data, offset, table, 0, length);
            tables.Add(table);
            nextOffset += table.Length;
        }

        byte[] font = new byte[nextOffset];
        result.CopyTo(font, 0);
        int position = headerSize;
        foreach (byte[] table in tables)
        {
            table.CopyTo(font, position);
            position += table.Length;
        }

        return font;
    }
}
