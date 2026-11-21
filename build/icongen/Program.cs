// Dựng logo LunaDNS (khiên + chữ L + đường định tuyến) từ một nguồn hình duy nhất rồi xuất:
//   Assets/lunadns.ico, tray-{off,on,error}.ico  (khung PNG, như bản KH6)
//   Assets/Logo.axaml                              (DrawingImage cho UI, không cần Svg.Skia)
//   docs/assets/lunadns-mark.svg                   (README)
//   [thư mục xem thử]/*.png                        (tuỳ chọn, để duyệt bằng mắt)
// Lưới vẽ 256×256. PO chốt 14/11: mọi cỡ dùng chung một hình, không vẽ bản giản lược.
// PO chốt 21/11: bỏ trăng lưỡi liềm ("Luna" là tên riêng, không phải Moon), dùng phương án 2 —
// khiên + chữ L, chân chữ L nối ra đường định tuyến kết thúc bằng nút tròn.
// Cách chạy: dotnet run --project build/icongen -- <thư mục repo> [thư mục xem thử]
// Bản macOS: dotnet run --project build/icongen -- --macos <thư mục repo> [thư mục iconset]
//   chỉ ghi Assets/tray-{off,on,error}-mac.png (template đen + alpha cho thanh menu) và thư mục .iconset
//   để iconutil dựng LunaDNS.icns (build/make-icns.sh). Không đụng .ico đang có.
using System.Globalization;
using System.Text;
using SkiaSharp;

if (args.Length > 0 && args[0] == "--macos")
{
    var macRepo = args.Length > 1 ? args[1] : Directory.GetCurrentDirectory();
    var macAssets = Path.Combine(macRepo, "src", "LunaDns.App", "Assets");
    foreach (var v in new[] { Variant.Off, Variant.On, Variant.Error })
    {
        var file = Path.Combine(macAssets, $"tray-{v.ToString().ToLowerInvariant()}-mac.png");
        File.WriteAllBytes(file, Art.TemplatePng(36, v));
        Console.WriteLine($"Đã ghi {file}");
    }
    if (args.Length > 2)
    {
        var iconset = args[2];
        Directory.CreateDirectory(iconset);
        foreach (var pt in new[] { 16, 32, 128, 256, 512 })
        {
            File.WriteAllBytes(Path.Combine(iconset, $"icon_{pt}x{pt}.png"), Art.MacAppPng(pt));
            File.WriteAllBytes(Path.Combine(iconset, $"icon_{pt}x{pt}@2x.png"), Art.MacAppPng(pt * 2));
        }
        Console.WriteLine($"Đã ghi {iconset}");
    }
    return;
}

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
    private static readonly string[] Brand = ["#22E4F5", "#2979FF", "#7C5CFF"];
    private static readonly string[] Trace = ["#5EF2D6", "#22C3F0"];
    private static readonly string[] BrandOff = ["#A3AEC2", "#6B778C"];
    private static readonly string[] TraceOff = ["#E2E8F0", "#CBD5E1"];
    private const string Tile = "#0E1830";
    private const string TileEdge = "#1F3B66";
    private const string Red = "#EF4444";

    /// <summary>Logo dùng cho mọi cỡ (PO chốt 21/11/2026): khiên chuyển màu, đường định tuyến xanh ngọc
    /// đi ra từ chân chữ L (vẽ trước để đầu tròn của chữ L che chỗ nối), chữ L trắng.
    /// Thứ tự phần tử: [0] khiên, [1] đường định tuyến, [2] chữ L — <see cref="TemplatePng"/> dựa vào thứ tự này.</summary>
    public static List<Part> Mark()
    {
        var outline = SKPath.ParseSvgPathData(ShieldData);
        var shield = Simplify(Union(outline, Stroke(outline, 14, SKStrokeJoin.Round)));

        var route = Stroke(SKPath.ParseSvgPathData("M 136 158 H 160 V 112 H 176"), 10, SKStrokeJoin.Round);
        route.AddCircle(180, 112, 12);

        var letter = Stroke(SKPath.ParseSvgPathData("M 100 74 V 158 H 136"), 30, SKStrokeJoin.Round);

        return
        [
            new Part(shield, Brand, BrandGrad),
            new Part(Simplify(route), Trace, TraceGrad),
            new Part(Simplify(letter), ["#FFFFFF"]),
        ];
    }

    private static SKPath Union(SKPath a, SKPath b) => a.Op(b, SKPathOp.Union) ?? a;

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
            mark = mark.Select(p =>
                p.Colors == Brand ? p with { Colors = BrandOff, Grad = null }
                : p.Colors == Trace ? p with { Colors = TraceOff, Grad = null }
                : p).ToList();
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

    // Đường viền khiên chưa bo; Mark() cộng thêm nét 14 bo tròn nên khiên thật phủ 17…239 × 17…235.
    private const string ShieldData = "M 128 24 L 208 52 V 116 C 208 166 176 202 128 228 C 80 202 48 166 48 116 V 52 Z";

    private static readonly Grad BrandGrad = new(new SKPoint(40, 20), new SKPoint(216, 236), [0f, 0.52f, 1f]);
    private static readonly Grad TraceGrad = new(new SKPoint(80, 190), new SKPoint(200, 60), [0f, 1f]);

    private static Part Transform(Part part, SKMatrix m)
    {
        var path = new SKPath(part.Path);
        path.Transform(m);
        var grad = part.Grad is null ? null : part.Grad with { From = m.MapPoint(part.Grad.From), To = m.MapPoint(part.Grad.To) };
        return new Part(path, part.Colors, grad);
    }

    private static SKPath Stroke(SKPath path, float width, SKStrokeJoin join = SKStrokeJoin.Miter)
    {
        using var paint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = width, StrokeCap = SKStrokeCap.Round, StrokeJoin = join };
        var result = new SKPath();
        paint.GetFillPath(path, result);
        return result;
    }

    private static SKPath Simplify(SKPath path) => path.Simplify() ?? path;

    /// <summary>Icon .app cho macOS: ô nền thu về khoảng 80% khung (lề chuẩn icon macOS 11+, 824/1024), còn lại như bản App.</summary>
    public static byte[] MacAppPng(int size)
    {
        var info = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(size / 256f);
        canvas.Scale(0.855f, 0.855f, 128, 128);
        Draw(canvas, Compose(Variant.App, size));
        return Encode(surface);
    }

    /// <summary>
    /// Icon template cho thanh menu macOS: chỉ đen + alpha, macOS tự tô theo nền sáng/tối. Ba trạng thái khác nhau bằng hình:
    /// tắt = hình mờ, bật = hình đặc, lỗi = hình đặc bị khoét một góc đặt chấm tròn. Chữ L và đường định tuyến được khoét trong suốt.
    /// </summary>
    public static byte[] TemplatePng(int size, Variant variant)
    {
        var info = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(size / 256f);
        var parts = Mark();
        using var ink = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColors.Black };
        using var hole = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, BlendMode = SKBlendMode.Clear };
        for (var i = 0; i < parts.Count; i++)
        {
            // Phần tử [0] của Mark() là khiên; các phần tử sau (đường định tuyến, chữ L) nằm trên khiên nên khoét thủng.
            canvas.DrawPath(parts[i].Path, i == 0 ? ink : hole);
        }
        if (variant == Variant.Error)
        {
            canvas.DrawCircle(204, 204, 62, hole);
            canvas.DrawCircle(204, 204, 44, ink);
        }
        if (variant == Variant.Off)
        {
            // Giảm alpha toàn hình còn ~45%.
            using var snapshot = surface.Snapshot();
            canvas.Clear(SKColors.Transparent);
            canvas.ResetMatrix();
            using var fade = new SKPaint { Color = SKColors.Black.WithAlpha(115) };
            canvas.DrawImage(snapshot, 0, 0, fade);
        }
        return Encode(surface);
    }

    private static void Draw(SKCanvas canvas, List<Part> parts)
    {
        foreach (var part in parts)
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
    }

    private static byte[] Encode(SKSurface surface)
    {
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

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
