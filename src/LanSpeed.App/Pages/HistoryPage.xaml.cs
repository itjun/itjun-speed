using System.Collections.ObjectModel;
using LanSpeed.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LanSpeed.App.Pages;

/// <summary>历史行视图模型（经典 Binding）。</summary>
public sealed class HistoryRow
{
    public required string Id { get; init; }

    public required string Time { get; init; }

    public required string Mode { get; init; }

    public required string Protocol { get; init; }

    public required string ParamsText { get; init; }

    public required string PairsText { get; init; }
}

/// <summary>历史页（§8）：列表、删除、导出 CSV。</summary>
public sealed partial class HistoryPage : Page
{
    private readonly ObservableCollection<HistoryRow> _rows = [];

    public HistoryPage()
    {
        InitializeComponent();
        HistoryList.ItemsSource = _rows;
        Fill();
    }

    private void Fill()
    {
        _rows.Clear();
        foreach (var r in AppServices.Current.History.Load())
        {
            _rows.Add(new HistoryRow
            {
                Id = r.Id,
                Time = r.Time.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                Mode = r.Mode,
                Protocol = r.Protocol.ToUpperInvariant(),
                ParamsText = $"{r.Duration}s × {r.Parallel} {r.Direction}",
                PairsText = $"{r.Pairs.Count} 对：" + string.Join("；", r.Pairs.Take(3).Select(p => $"{p.A}→{p.B} {p.Grade}")) + (r.Pairs.Count > 3 ? " …" : string.Empty),
            });
        }
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            AppServices.Current.History.Delete(id);
            Fill();
        }
    }

    private async void OnExportCsv(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = "内网测速历史" };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWnd));
        picker.FileTypeChoices.Add("CSV", new List<string> { ".csv" });
        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
            AppServices.Current.History.WriteCsv(file.Path);
            var dialog = new ContentDialog
            {
                Title = "已导出",
                Content = file.Path,
                CloseButtonText = "关闭",
                XamlRoot = XamlRoot,
            };
            _ = await dialog.ShowAsync();
        }
    }
}
