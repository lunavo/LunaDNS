// Dựng logo LunaDNS (trăng lưỡi liềm + mạch + khiên) từ một nguồn hình duy nhất rồi xuất:
//   Assets/lunadns.ico, tray-{off,on,error}.ico  (khung PNG, như bản KH6)
//   Assets/Logo.axaml                              (DrawingImage cho UI, không cần Svg.Skia)
//   docs/assets/lunadns-mark.svg                   (README)
//   [thư mục xem thử]/*.png                        (tuỳ chọn, để duyệt bằng mắt)
// Lưới vẽ 256×256. PO chốt 14/11: mọi cỡ dùng chung một hình (SVG Gemini), không vẽ bản giản lược.
// Cách chạy: dotnet run --project build/icongen -- <thư mục repo> [thư mục xem thử]
using System.Globalization;
using System.Text;
using SkiaSharp;

var repo = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
var preview = args.Length > 1 ? args[1] : null;
var assets = Path.Combine(repo, "src", "LunaDns.App", "Assets");
var docsAssets = Path.Combine(repo, "docs", "assets");
Directory.CreateDirectory(docsAssets);

WriteIco(Path.Combine(assets, "lunadns.ico"), [16, 20, 24, 32, 48, 64, 256], Variant.App);
WriteIco(Path.Combine(assets, "tray-off.ico"), [16, 20, 24, 32], Variant.Off);
WriteIco(Path.Combine(assets, "tray-on.ico"), [16, 20, 24, 32], Variant.On);
WriteIco(Path.Combine(assets, "tray-error.ico"), [16, 20, 24, 32], Variant.Error);
WriteText(Path.Combine(assets, "Logo.axaml"), Art.ToXaml(Art.Mark()));
WriteText(Path.Combine(docsAssets, "lunadns-mark.svg"), Art.ToSvg(Art.Mark()));

if (preview is not null)
{
    Directory.CreateDirectory(preview);
    foreach (var v in Enum.GetValues<Variant>())
    {
        foreach (var size in new[] { 16, 24, 32, 256 })
        {
            File.WriteAllBytes(Path.Combine(preview, $"{v}-{size}.png".ToLowerInvariant()), Art.Png(size, v));
        }
    }
    // Bảng xem thử: mọi biến thể ở 16/24/32 px, phóng 6 lần kiểu điểm ảnh để soi độ nét.
    const int zoom = 6;
    using var sheet = new SKBitmap((16 + 24 + 32 + 24) * zoom, 4 * 36 * zoom);
    using (var c = new SKCanvas(sheet))
    {
        c.Clear(new SKColor(0x20, 0x20, 0x20));
        var row = 0;
        foreach (var v in Enum.GetValues<Variant>())
        {
            var x = 0;
            foreach (var size in new[] { 16, 24, 32 })
            {
                using var bmp = SKBitmap.Decode(Art.Png(size, v));
                c.DrawBitmap(bmp, new SKRect(x * zoom, row * 36 * zoom, (x + size) * zoom, ((row * 36) + size) * zoom), new SKPaint { FilterQuality = SKFilterQuality.None });
                x += size + 8;
            }
            row++;
        }
    }
    using var sheetData = sheet.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(Path.Combine(preview, "sheet.png"), sheetData.ToArray());
}

static void WriteIco(string path, int[] sizes, Variant variant)
{
    var images = sizes.Select(s => Art.Png(s, variant)).ToArray();
    using var output = new MemoryStream();
    using var w = new BinaryWriter(output);
    w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)sizes.Length);   // ICONDIR: reserved, type = icon, số khung
    var offset = 6 + (16 * sizes.Length);
    for (var i = 0; i < sizes.Length; i++)
    {
        var dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);                     // 0 nghĩa là 256
        w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
        w.Write((ushort)1); w.Write((ushort)32);                              // planes, bit/pixel
        w.Write((uint)images[i].Length); w.Write((uint)offset);
        offset += images[i].Length;
    }
    foreach (var image in images) { w.Write(image); }
    w.Flush();
    File.WriteAllBytes(path, output.ToArray());
    Console.WriteLine($"Đã ghi {path} ({sizes.Length} khung, {output.Length} byte)");
}

static void WriteText(string path, string text)
{
    File.WriteAllText(path, text.ReplaceLineEndings("\r\n"), new UTF8Encoding(false));
    Console.WriteLine($"Đã ghi {path}");
}

internal enum Variant { App, Off, On, Error }

/// <summary>Dải màu theo toạ độ tuyệt đối của lưới 256 (như gradientUnits="userSpaceOnUse" trong SVG).</summary>
internal sealed record Grad(SKPoint From, SKPoint To, float[] Offsets);

/// <summary>Một mảng tô: đường viền đã đổi nét thành vùng, cùng màu đặc hoặc dải màu.
/// Không có <see cref="Grad"/> thì dải màu chạy chéo khung bao của chính mảng, các mốc chia đều.</summary>
internal sealed record Part(SKPath Path, string[] Colors, Grad? Grad = null);

internal static class Art
{
    // Bảng màu theo ảnh tham khảo của PO: xanh ngọc → xanh dương → tím.
    private static readonly string[] Moon = ["#22E4F5", "#2979FF", "#7C5CFF"];
    private static readonly string[] Trace = ["#5EF2D6", "#22C3F0"];
    private static readonly string[] Shield = ["#3DF0E0", "#2979FF"];
    private static readonly string[] MoonOff = ["#A3AEC2", "#6B778C"];
    private const string Tile = "#0E1830";
    private const string TileEdge = "#1F3B66";
    private const string Red = "#EF4444";

    /// <summary>Logo dùng cho mọi cỡ, chép hình từ SVG PO tạo bằng Gemini (14/11/2026): trăng, 3 vệt mạch
    /// (vệt giữa là thân chìa khoá có mũi tên), khiên có tam giác "play", vệt quỹ đạo dưới chân trăng.</summary>
    public static List<Part> Mark()
    {
        var moon = SKPath.ParseSvgPathData(MoonPath);

        var traces = new SKPath();
        traces.AddPath(Line(96, 112, 132, 76, 10));
        traces.AddCircle(136, 72, 9);
        traces.AddPath(Line(74, 168, 178, 64, 11));
        traces.AddPath(Line(138, 104, 150, 116, 9));
        traces.AddPath(Line(152, 90, 162, 100, 9));
        traces.AddPath(SKPath.ParseSvgPathData("M 186 56 L 160 64 L 178 82 Z"));
        traces.AddPath(Line(142, 152, 188, 106, 10));
        traces.AddCircle(192, 102, 9);

        var shield = SKPath.ParseSvgPathData(ShieldData);
        var play = SKPath.ParseSvgPathData("M 197 41 L 211 49 L 197 57 Z");

        var orbit = Stroke(SKPath.ParseSvgPathData("M 48 164 C 24 196 66 226 144 188"), 9);

        return
        [
            new Part(moon, Moon, MoonGrad),
            new Part(Simplify(traces), Trace, TraceGrad),
            new Part(shield, Shield, ShieldGrad),
            new Part(play, ["#0A1120"]),
            new Part(Simplify(orbit), Moon, MoonGrad),
        ];
    }

    private static SKPath Line(float x1, float y1, float x2, float y2, float width)
    {
        var line = new SKPath();
        line.MoveTo(x1, y1);
        line.LineTo(x2, y2);
        return Stroke(line, width);
    }

    /// <summary>Điểm đầu, điểm cuối (toạ độ lưới) và các mốc của dải màu một mảng.</summary>
    private static Grad GradOf(Part part)
    {
        if (part.Grad is not null) { return part.Grad; }
        var b = part.Path.Bounds;
        return new Grad(new SKPoint(b.Left, b.Top), new SKPoint(b.Right, b.Bottom), Stops(part.Colors.Length));
    }

    private static List<Part> Compose(Variant variant, int size)
    {
        var parts = new List<Part>();
        var tile = new SKPath();
        tile.AddRoundRect(new SKRect(8, 8, 248, 248), 56, 56);
        parts.Add(new Part(tile, [Tile]));
        var edge = new SKPath();
        edge.AddRoundRect(new SKRect(8, 8, 248, 248), 56, 56);
        parts.Add(new Part(Stroke(edge, size >= 48 ? 6 : 10), [TileEdge]));

        // Hình gốc chạm sát mép lưới 256; trên ô nền thì thu vào cho có lề.
        var inset = SKMatrix.CreateScale(0.84f, 0.84f, 128, 128);
        var mark = Mark().Select(p => Transform(p, inset)).ToList();
        if (variant == Variant.Off)
        {
            mark = mark.Select(p => p.Colors.Length > 1 ? p with { Colors = MoonOff, Grad = null } : p).ToList();
        }
        parts.AddRange(mark);

        if (variant == Variant.Error)
        {
            var ring = new SKPath();
            ring.AddCircle(196, 196, 58);
            parts.Add(new Part(ring, [Tile]));
            var dot = new SKPath();
            dot.AddCircle(196, 196, 44);
            parts.Add(new Part(dot, [Red]));
        }
        return parts;
    }

    private const string MoonPath = "M 134 24 A 104 104 0 1 0 218 188 A 84 84 0 1 1 134 24 Z";
    private const string ShieldData = "M 202 22 L 228 32 V 50 C 228 65 216 74 202 79 C 188 74 176 65 176 50 V 32 Z";

    private static readonly Grad MoonGrad = new(new SKPoint(36, 24), new SKPoint(188, 228), [0f, 0.52f, 1f]);
    private static readonly Grad TraceGrad = new(new SKPoint(68, 196), new SKPoint(216, 48), [0f, 1f]);
    private static readonly Grad ShieldGrad = new(new SKPoint(176, 22), new SKPoint(228, 74), [0f, 1f]);

    private static Part Transform(Part part, SKMatrix m)
    {
        var path = new SKPath(part.Path);
        path.Transform(m);
        var grad = part.Grad is null ? null : part.Grad with { From = m.MapPoint(part.Grad.From), To = m.MapPoint(part.Grad.To) };
        return new Part(path, part.Colors, grad);
    }

    private static SKPath Stroke(SKPath path, float width)
    {
        using var paint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeCap = SKStrokeCap.Round };
        var result = new SKPath();
        paint.GetFillPath(path, result);
        return result;
    }

    private static SKPath Simplify(SKPath path) => path.Simplify() ?? path;

    public static byte[] Png(int size, Variant variant)
    {
        var info = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(size / 256f);
        foreach (var part in Compose(variant, size))
        {
            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            if (part.Colors.Length == 1)
            {
                paint.Color = SKColor.Parse(part.Colors[0]);
            }
            else
            {
                var g = GradOf(part);
                paint.Shader = SKShader.CreateLinearGradient(
                    g.From, g.To, part.Colors.Select(SKColor.Parse).ToArray(), g.Offsets, SKShaderTileMode.Clamp);
            }
            canvas.DrawPath(part.Path, paint);
        }
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static float[] Stops(int n) => Enumerable.Range(0, n).Select(i => i / (float)(n - 1)).ToArray();

    private static string Num(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public static string ToSvg(List<Part> parts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!-- Sinh bởi build/icongen, đừng sửa tay. -->");
        sb.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 256 256\" width=\"256\" height=\"256\">");
        sb.AppendLine("  <defs>");
        for (var i = 0; i < parts.Count; i++)
        {
            if (parts[i].Colors.Length < 2) { continue; }
            var g = GradOf(parts[i]);
            sb.AppendLine($"    <linearGradient id=\"g{i}\" gradientUnits=\"userSpaceOnUse\" x1=\"{Num(g.From.X)}\" y1=\"{Num(g.From.Y)}\" x2=\"{Num(g.To.X)}\" y2=\"{Num(g.To.Y)}\">");
            for (var s = 0; s < g.Offsets.Length; s++)
            {
                sb.AppendLine($"      <stop offset=\"{Num(g.Offsets[s])}\" stop-color=\"{parts[i].Colors[s]}\"/>");
            }
            sb.AppendLine("    </linearGradient>");
        }
        sb.AppendLine("  </defs>");
        for (var i = 0; i < parts.Count; i++)
        {
            var fill = parts[i].Colors.Length < 2 ? parts[i].Colors[0] : $"url(#g{i})";
            sb.AppendLine($"  <path fill=\"{fill}\" d=\"{parts[i].Path.ToSvgPathData()}\"/>");
        }
        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    public static string ToXaml(List<Part> parts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!-- Sinh bởi build/icongen (make-icon.ps1), đừng sửa tay. Logo LunaDNS dạng vector, lưới 256×256. -->");
        sb.AppendLine("<ResourceDictionary xmlns=\"https://github.com/avaloniaui\"");
        sb.AppendLine("                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">");
        sb.AppendLine("  <DrawingImage x:Key=\"Logo.Mark\">");
        sb.AppendLine("    <DrawingGroup>");
        foreach (var part in parts)
        {
            // F1 = tô theo NonZero như SVG; mặc định của Avalonia là EvenOdd.
            var geometry = "F1 " + part.Path.ToSvgPathData();
            if (part.Colors.Length < 2)
            {
                sb.AppendLine($"      <GeometryDrawing Brush=\"{part.Colors[0]}\" Geometry=\"{geometry}\" />");
                continue;
            }
            sb.AppendLine($"      <GeometryDrawing Geometry=\"{geometry}\">");
            sb.AppendLine("        <GeometryDrawing.Brush>");
            // Avalonia tính điểm % theo khung bao của hình, nên đổi toạ độ tuyệt đối sang % của khung bao.
            var g = GradOf(part);
            var b = part.Path.Bounds;
            string Rel(SKPoint pt) => $"{Num((pt.X - b.Left) / b.Width * 100)}%,{Num((pt.Y - b.Top) / b.Height * 100)}%";
            sb.AppendLine($"          <LinearGradientBrush StartPoint=\"{Rel(g.From)}\" EndPoint=\"{Rel(g.To)}\">");
            for (var s = 0; s < g.Offsets.Length; s++)
            {
                sb.AppendLine($"            <GradientStop Color=\"{part.Colors[s]}\" Offset=\"{Num(g.Offsets[s])}\" />");
            }
            sb.AppendLine("          </LinearGradientBrush>");
            sb.AppendLine("        </GeometryDrawing.Brush>");
            sb.AppendLine("      </GeometryDrawing>");
        }
        sb.AppendLine("    </DrawingGroup>");
        sb.AppendLine("  </DrawingImage>");
        sb.AppendLine("</ResourceDictionary>");
        return sb.ToString();
    }
}
