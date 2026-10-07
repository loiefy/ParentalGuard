using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using ParentalGuard.UI.Services.IpcClient;

namespace ParentalGuard.UI.Services;

/// <summary>
/// `MISC-011` (2026-10-07) — xuất lịch sử ra PDF hoàn toàn ngoại tuyến bằng PDFsharp (MIT). Nội dung: tiêu đề, khoảng thời
/// gian, thời điểm xuất, tổng số lần chặn, bảng sự kiện (thời gian, loại sự kiện, ứng dụng, điểm tin cậy). Không có ảnh.
/// Font lấy từ thư mục Fonts của Windows qua <see cref="PdfFontResolver"/> (nhúng tập con vào PDF).
/// </summary>
public static class AuditPdfExporter
{
    private const double Margin = 40;
    private const double RowHeight = 16;
    private static readonly double[] _columnWidths = [120, 170, 150, 75];

    static AuditPdfExporter()
    {
        GlobalFontSettings.FontResolver ??= new PdfFontResolver();
    }

    /// <summary>Lọc <paramref name="entries"/> theo <paramref name="cutoffUnixMs"/> (null = toàn bộ) — tách riêng để test được.</summary>
    public static IReadOnlyList<AuditLogEntry> FilterRange(IEnumerable<AuditLogEntry> entries, long? cutoffUnixMs) =>
        [.. entries.Where(e => cutoffUnixMs is not long cutoff || e.TsUnixMs >= cutoff)];

    public static byte[] Build(IReadOnlyList<AuditLogEntry> entries, string rangeText, DateTimeOffset exportedAt, string languageCode)
    {
        ArgumentNullException.ThrowIfNull(entries);

        string family = languageCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? PdfFontResolver.CjkFamily : PdfFontResolver.LatinFamily;
        using var document = new PdfDocument();
        document.Info.Title = LocalizationService.Get("AuditPdfTitle");
        document.Info.Creator = "ParentalGuard";

        XFont titleFont = CreateFont(family, 16, XFontStyleEx.Bold);
        XFont headerFont = CreateFont(family, 9.5, XFontStyleEx.Bold);
        XFont bodyFont = CreateFont(family, 9, XFontStyleEx.Regular);

        PdfPage page = document.AddPage();
        XGraphics gfx = XGraphics.FromPdfPage(page);
        double y = Margin;

        gfx.DrawString(LocalizationService.Get("AuditPdfTitle"), titleFont, XBrushes.Black, new XPoint(Margin, y + 16));
        y += 30;
        int blockedCount = entries.Count(e => e.EventType == "ContentBlocked");
        foreach (string line in new[]
        {
            LocalizationService.GetFormatted("AuditPdfRange", rangeText),
            LocalizationService.GetFormatted("AuditPdfExportedAt", exportedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
            LocalizationService.GetFormatted("AuditPdfBlockedTotal", blockedCount),
            LocalizationService.GetFormatted("AuditPdfEventTotal", entries.Count),
        })
        {
            gfx.DrawString(line, bodyFont, XBrushes.Black, new XPoint(Margin, y + 10));
            y += RowHeight;
        }

        y += 8;
        string[] headers =
        [
            LocalizationService.Get("AuditPdfColumnTime"),
            LocalizationService.Get("AuditPdfColumnEvent"),
            LocalizationService.Get("AuditPdfColumnApp"),
            LocalizationService.Get("AuditPdfColumnScore"),
        ];
        y = DrawRow(gfx, headers, headerFont, y, shaded: true);

        if (entries.Count == 0)
        {
            gfx.DrawString(LocalizationService.Get("AuditLogEmptyText"), bodyFont, XBrushes.Gray, new XPoint(Margin, y + 12));
        }

        foreach (AuditLogEntry entry in entries)
        {
            if (y + RowHeight > page.Height.Point - Margin)
            {
                gfx.Dispose();
                page = document.AddPage();
                gfx = XGraphics.FromPdfPage(page);
                y = DrawRow(gfx, headers, headerFont, Margin, shaded: true);
            }

            bool blocked = entry.EventType == "ContentBlocked";
            string[] cells =
            [
                DateTimeOffset.FromUnixTimeMilliseconds(entry.TsUnixMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                ViewModels.EventTypeDisplay.ToText(entry.EventType),
                blocked ? entry.ProcessName : string.Empty,
                blocked ? $"{entry.RiskScore * 100:0}%" : string.Empty,
            ];
            y = DrawRow(gfx, cells, bodyFont, y, shaded: false);
        }

        gfx.Dispose();
        using var stream = new MemoryStream();
        document.Save(stream, closeStream: false);
        return stream.ToArray();
    }

    private static XFont CreateFont(string family, double size, XFontStyleEx style)
    {
        try
        {
            return new XFont(family, size, style);
        }
        catch (InvalidOperationException)
        {
            return new XFont(PdfFontResolver.LatinFamily, size, style);
        }
    }

    private static double DrawRow(XGraphics gfx, string[] cells, XFont font, double y, bool shaded)
    {
        double x = Margin;
        double total = _columnWidths.Sum();
        if (shaded)
        {
            gfx.DrawRectangle(XBrushes.Gainsboro, Margin, y, total, RowHeight);
        }

        for (int i = 0; i < cells.Length; i++)
        {
            string text = Fit(gfx, cells[i], font, _columnWidths[i] - 6);
            gfx.DrawString(text, font, XBrushes.Black, new XPoint(x + 3, y + 11.5));
            x += _columnWidths[i];
        }

        gfx.DrawLine(XPens.LightGray, Margin, y + RowHeight, Margin + total, y + RowHeight);
        return y + RowHeight;
    }

    /// <summary>Cắt bớt chữ dài cho vừa cột (thêm "…").</summary>
    private static string Fit(XGraphics gfx, string text, XFont font, double width)
    {
        if (gfx.MeasureString(text, font).Width <= width)
        {
            return text;
        }

        while (text.Length > 1 && gfx.MeasureString(text + "…", font).Width > width)
        {
            text = text[..^1];
        }

        return text + "…";
    }
}
