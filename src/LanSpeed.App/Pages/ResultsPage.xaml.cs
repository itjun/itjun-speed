using System.Collections.ObjectModel;
using LanSpeed.App.Services;
using LanSpeed.Core.Orchestration;
using LanSpeed.Core.Verdict;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LanSpeed.App.Pages;

public sealed class ResultRow
{
    public required string PairText { get; init; }

    public required string SpeedText { get; init; }

    public string Note { get; init; } = string.Empty;

    public required string GradeText { get; init; }

    public required Brush GradeBrush { get; init; }
}

/// <summary>结果页（§8）：最近一次分组的逐轮结果与结论（完整结论规则），可导出 CSV。</summary>
public sealed partial class ResultsPage : Page
{
    private readonly ObservableCollection<ResultRow> _rows = [];

    public ResultsPage()
    {
        InitializeComponent();
        ResultList.ItemsSource = _rows;
        AppServices.Current.GroupCompleted += OnGroupCompleted;
        Fill(AppServices.Current.LastGroup);
    }

    private void OnGroupCompleted()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => Fill(AppServices.Current.LastGroup));
            return;
        }
        Fill(AppServices.Current.LastGroup);
    }

    private void Fill(GroupResult? result)
    {
        _rows.Clear();
        if (result == null)
        {
            ResultHeader.Text = "暂无分组结果——到「分组」页发起一次测速。";
            ExportButton.Visibility = Visibility.Collapsed;
            return;
        }
        var p = AppServices.Current.LastGroupParams;
        ResultHeader.Text = $"{(result.Mode == GroupMode.Star ? "星形" : "矩阵")} · {(p.Protocol == "udp" ? "UDP" : "TCP")} {p.Duration}s × {p.Parallel} 流 · 完成 {result.DoneCount}/{result.Rounds.Count} 轮";
        ExportButton.Visibility = Visibility.Visible;
        foreach (var r in result.Rounds)
        {
            if (r.Status == "done" && r.Summary is { } s)
            {
                var v = VerdictEvaluator.Evaluate(s, p.Protocol, 0, p);
                _rows.Add(new ResultRow
                {
                    PairText = $"{r.Pair.A} → {r.Pair.B}",
                    SpeedText = $"A→B {s.AB / 1e6:F1} │ B→A {s.BA / 1e6:F1} Mbps",
                    Note = v.Title,
                    GradeText = v.GradeLabel,
                    GradeBrush = BrushFor(v.Grade),
                });
            }
            else
            {
                _rows.Add(new ResultRow
                {
                    PairText = $"{r.Pair.A} → {r.Pair.B}",
                    SpeedText = r.Status,
                    Note = r.Reason,
                    GradeText = r.Status,
                    GradeBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray),
                });
            }
        }
    }

    internal static Brush BrushFor(Grade grade) => grade switch
    {
        Grade.Great => new SolidColorBrush(Microsoft.UI.Colors.Green),
        Grade.Good => new SolidColorBrush(Microsoft.UI.Colors.Teal),
        Grade.Fair => new SolidColorBrush(Microsoft.UI.Colors.DarkOrange),
        _ => new SolidColorBrush(Microsoft.UI.Colors.Red),
    };

    private async void OnExportCsv(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = "内网测速结果" };
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
