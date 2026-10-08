Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$proc = Get-Process LanSpeed.App -ErrorAction Stop
$root = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)

function Find([string]$id) {
  $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
  return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}
function FindByName([string]$name) {
  $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
  return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

# 导航到两机测试
(FindByName '两机测试').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Seconds 3

# 填 A=本机 B=zhetai
foreach ($pair in @(@('HostACombo', '本机'), @('HostBCombo', '192.168.210.222'))) {
  $combo = Find $pair[0]
  $editCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
  $edit = $combo.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $editCond)
  $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($pair[1])
}

# 选双向
$dir = Find 'Direction'
$dir.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
Start-Sleep -Milliseconds 600
$bidir = $dir.FindFirst([System.Windows.Automation.TreeScope]::Subtree, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '双向')))
if ($bidir) {
  $bidir.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Write-Host '方向=双向'
} else {
  Write-Host 'WARN: 未找到双向项，保持默认正向'
}
$dir.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()

# 时长改 6 秒，快点出结果
$dur = Find 'DurationBox'
$dur.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('6')

(FindByName '开始测速').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Write-Host '已开始测速'
Start-Sleep -Seconds 14
if ($proc.HasExited) { Write-Host 'FAIL: 崩溃'; exit 1 }
Write-Host 'PASS: 未崩溃'
Write-Host ('A→B 大数字: ' + (Find 'AbValue').Current.Name)
Write-Host ('B→A 大数字: ' + (Find 'BaValue').Current.Name)
Write-Host ('结论徽章: ' + (Find 'GradeText').Current.Name)
