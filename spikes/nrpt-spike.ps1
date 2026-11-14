# Chạy bằng PowerShell (Admin). Kiểm tra NRPT local + nameserver 127.0.0.1 trên máy hiện tại.
$ErrorActionPreference = 'Stop'
$ns = '.lunadns-spike.test'
$os = Get-CimInstance Win32_OperatingSystem
"Windows: $($os.Caption) $($os.Version)"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { "LỖI: cần chạy PowerShell bằng quyền Admin"; exit 1 }

$busy = @(Get-NetUDPEndpoint -LocalPort 53 -ErrorAction SilentlyContinue)
if ($busy.Count -gt 0) {
    "LỖI: cổng UDP 53 đang bị dùng:"
    $busy | ForEach-Object { "  $($_.LocalAddress) PID $($_.OwningProcess) $((Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue).ProcessName)" }
    exit 1
}

# 1. Responder DNS tối giản trên 127.0.0.1:53, trả A 10.9.8.7 cho câu hỏi A.
#    Chỉ giữ header + question (bỏ OPT/EDNS mà Windows gắn vào), nên phần answer nằm đúng chỗ.
#    Trả lời nhiều gói vì DNS client có thể gửi lại; job dừng khi hết 15 s không có gói.
$job = Start-Job {
    $udp = [System.Net.Sockets.UdpClient]::new([System.Net.IPEndPoint]::new([System.Net.IPAddress]::Loopback, 53))
    $udp.Client.ReceiveTimeout = 15000
    try {
        while ($true) {
            $ep = [System.Net.IPEndPoint]::new([System.Net.IPAddress]::Any, 0)
            $q = $udp.Receive([ref]$ep)
            $i = 12
            while ($q[$i] -ne 0) { $i += $q[$i] + 1 }
            $qtype = ($q[$i + 1] -shl 8) -bor $q[$i + 2]
            $end = $i + 5                                          # nhãn 0 + QTYPE + QCLASS
            $r = [byte[]]$q[0..($end - 1)]
            $r[2] = 0x81; $r[3] = 0x80                             # QR=1 RD RA, RCODE=0
            $r[6] = 0; $r[7] = 0; $r[8] = 0; $r[9] = 0; $r[10] = 0; $r[11] = 0
            if ($qtype -eq 1) {
                $r[7] = 1                                          # ANCOUNT=1
                $r = [byte[]]($r + [byte[]](0xC0,0x0C, 0,1, 0,1, 0,0,0,60, 0,4, 10,9,8,7))
            }
            $udp.Send($r, $r.Length, $ep) | Out-Null
            "đã trả lời qtype=$qtype từ $ep"
        }
    } catch [System.Net.Sockets.SocketException] {
        "responder dừng: $($_.Exception.Message)"
    } finally { $udp.Close() }
}
Start-Sleep -Milliseconds 800
if ($job.State -ne 'Running') { "LỖI: responder không chạy được"; Receive-Job $job; exit 1 }

# 2. Thêm quy tắc NRPT trỏ namespace về 127.0.0.1
Add-DnsClientNrptRule -Namespace $ns -NameServers '127.0.0.1' -Comment 'LunaDNS spike' | Out-Null
Clear-DnsClientCache
try {
    $res = Resolve-DnsName "probe$ns" -Type A -DnsOnly -QuickTimeout
    "KẾT QUẢ: $($res.IPAddress)   (mong đợi 10.9.8.7 = NRPT hoạt động)"
} catch { "KẾT QUẢ: LỖI - $($_.Exception.Message)" }
finally {
    Get-DnsClientNrptRule | Where-Object Comment -eq 'LunaDNS spike' | ForEach-Object { Remove-DnsClientNrptRule -Name $_.Name -Force }
    Clear-DnsClientCache
    Stop-Job $job -ErrorAction SilentlyContinue
    "Nhật ký responder:"; Receive-Job $job -ErrorAction SilentlyContinue | ForEach-Object { "  $_" }
    Remove-Job $job -Force
    "Còn quy tắc spike: $(@(Get-DnsClientNrptRule | Where-Object Comment -eq 'LunaDNS spike').Count) (mong đợi 0)"
}
