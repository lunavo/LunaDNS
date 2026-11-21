using System.Globalization;
using System.Security.Cryptography;
using LunaDns.Core.Domains;

// Ký danh sách lớp 2 trên máy PO (spec D4). Khoá bí mật: PEM PKCS#8 có mật khẩu, không bao giờ đưa vào repo hay CI.
// Mật khẩu đọc từ biến môi trường LUNADNS_KEY_PASSWORD, hoặc gõ ẩn khi được hỏi.
return args switch
{
    ["keygen", "--out", var pem] => KeyGen(pem),
    ["verify-source", var source] => VerifySource(source),
    ["sign", "--key", var pem, "--key-id", var keyId, "--in", var source, "--out", var output] => Sign(pem, keyId, source, output),
    ["verify", var file] => Verify(file),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("""
        DomainListSigner keygen --out <khoá.pem>
        DomainListSigner verify-source <data/domain-list.json>
        DomainListSigner sign --key <khoá.pem> --key-id <id> --in <data/domain-list.json> --out <domains.json>
        DomainListSigner verify <domains.json>
        """);
    return 2;
}

// Mật khẩu: LUNADNS_KEY_PASSWORD, hoặc gõ ẩn trên terminal (stdin bị chuyển hướng thì đọc một dòng).
// Không chấp nhận mật khẩu rỗng; khoá mới cần ít nhất 12 ký tự và gõ lại để khớp.
static string? Password(bool newKey)
{
    const int MinNewPasswordLength = 12;
    var fromEnv = Environment.GetEnvironmentVariable("LUNADNS_KEY_PASSWORD");
    var password = !string.IsNullOrEmpty(fromEnv) ? fromEnv : Prompt("Mật khẩu khoá: ");
    if (string.IsNullOrEmpty(password)) { Console.Error.WriteLine("Mật khẩu rỗng, dừng."); return null; }
    if (!newKey) return password;
    if (password.Length < MinNewPasswordLength)
    {
        Console.Error.WriteLine($"Mật khẩu cho khoá mới cần ít nhất {MinNewPasswordLength} ký tự.");
        return null;
    }
    if (string.IsNullOrEmpty(fromEnv) && Prompt("Gõ lại mật khẩu: ") != password)
    {
        Console.Error.WriteLine("Hai lần gõ không khớp, dừng.");
        return null;
    }
    return password;
}

static string Prompt(string label)
{
    Console.Error.Write(label);
    if (Console.IsInputRedirected) return Console.ReadLine() ?? string.Empty;
    var typed = new System.Text.StringBuilder();
    while (true)
    {
        var k = Console.ReadKey(intercept: true);
        if (k.Key == ConsoleKey.Enter) break;
        if (k.Key == ConsoleKey.Backspace) { if (typed.Length > 0) typed.Length--; continue; }
        if (!char.IsControl(k.KeyChar)) typed.Append(k.KeyChar);
    }
    Console.Error.WriteLine();
    return typed.ToString();
}

static int KeyGen(string pem)
{
    if (File.Exists(pem)) { Console.Error.WriteLine($"{pem} đã có, không ghi đè."); return 1; }
    var dir = Path.GetDirectoryName(Path.GetFullPath(pem))!;
    if (!Directory.Exists(dir))
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(dir);
        else Directory.CreateDirectory(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    if (Password(newKey: true) is not { } password) return 1;
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var encrypted = key.ExportEncryptedPkcs8PrivateKeyPem(password,
        new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 600_000));
    File.WriteAllText(pem, encrypted);
    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(pem, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    Console.WriteLine($"Khoá công khai (dán vào DomainListKeys.Production):\n{DomainListSigning.PublicKey(key)}");
    return 0;
}

static int VerifySource(string source)
{
    var errors = DomainListSigning.CheckSource(File.ReadAllBytes(source));
    foreach (var e in errors) Console.Error.WriteLine(e);
    return errors.Count == 0 ? 0 : 1;
}

static int Sign(string pem, string keyId, string source, string output)
{
    var bytes = File.ReadAllBytes(source);
    if (VerifySource(source) != 0) return 1;
    if (Password(newKey: false) is not { } password) return 1;
    using var key = ECDsa.Create();
    key.ImportFromEncryptedPem(File.ReadAllText(pem), password);
    if (!DomainListKeys.Production.TryGetValue(keyId, out var embedded) || embedded != DomainListSigning.PublicKey(key))
    {
        Console.Error.WriteLine($"Khoá {keyId} chưa nhúng trong DomainListKeys.Production của bản build này.");
        return 1;
    }
    var now = DateTimeOffset.UtcNow;
    var serial = long.Parse(now.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    var parsed = DomainListSigning.ReadSource(bytes)!;
    var signed = DomainListSigning.Sign(parsed, serial, now, key, keyId);
    var check = DomainListVerifier.Verify(signed, DomainListKeys.Production, 0);
    if (check.Outcome != DomainListOutcome.Accepted || check.Dropped != 0)
    {
        Console.Error.WriteLine($"Tự kiểm lỗi: {check.Outcome}, bỏ {check.Dropped} dòng.");
        return 1;
    }
    File.WriteAllBytes(output, signed);
    Console.WriteLine($"Đã ký serial {serial}, {signed.Length} byte → {output}");
    return 0;
}

static int Verify(string file)
{
    var result = DomainListVerifier.Verify(File.ReadAllBytes(file), DomainListKeys.Production, 0);
    Console.WriteLine(result.Update is { } u
        ? $"{result.Outcome}: serial {u.Serial}, {u.Groups.Sum(g => g.Value.Count)} tên miền, bỏ {result.Dropped}"
        : result.Outcome.ToString());
    return result.Outcome == DomainListOutcome.Accepted ? 0 : 1;
}
