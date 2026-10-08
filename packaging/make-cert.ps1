param()
$cert = New-SelfSignedCertificate -Type Custom -Subject "CN=LanSpeed" -KeyUsage DigitalSignature `
    -FriendlyName "LanSpeed MSIX Signing" -CertStoreLocation "Cert:\CurrentUser\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
if (-not $cert) { throw "证书创建失败" }
$pwd = ConvertTo-SecureString -String "lanspeed" -Force -AsPlainText
$out = "C:\Users\itjun\Documents\iLearn\itjun-speed\packaging\LanSpeed.pfx"
New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null
Export-PfxCertificate -Cert $cert -FilePath $out -Password $pwd | Out-Null
Write-Host ("证书指纹: " + $cert.Thumbprint)
Write-Host ("PFX: " + $out)
# 输出安装说明需要的命令
Write-Host "安装证书（管理员 PowerShell）: Import-Certificate -FilePath 导出的 .cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
Export-Certificate -Cert $cert -FilePath "C:\Users\itjun\Documents\iLearn\itjun-speed\packaging\LanSpeed.cer" | Out-Null
