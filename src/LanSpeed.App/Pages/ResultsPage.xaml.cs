using System.Collections.ObjectModel;
using LanSpeed.App.Services;
using LanSpeed.Core.Orchestration;
using LanSpeed.Core.Verdict;
using Microsoft.UI;
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

/// <summary>结果页：星形表 / 矩阵热力 + 逐轮结论列表。</summary>
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
        MatrixPanel.Children.Clear();
        MatrixScroll.Visibility = Visibility.Collapsed;
        if (result == null)
        {
            ResultHeader.Text = "暂无分组结果——到「分组」页发起一次测速。";
            ExportButton.Visibility = Visibility.Collapsed;
            return;
        }
        var p = AppServices.Current.LastGroupParams;
        ResultHeader.Text = $"{(result.Mode == GroupMode.Star ? "星形" : "矩阵")} · {(p.Protocol == "udp" ? "UDP" : "TCP")} {p.Duration}s × {p.Parallel} 流 · 完成 {result.DoneCount}/{result.Rounds.Count} 轮";
        ExportButton.Visibility = Visibility.Visible;

        if (result.Mode == GroupMode.Mesh)
        {
            BuildMatrix(result, p);
            MatrixScroll.Visibility = Visibility.Visible;
        }

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
                    GradeBrush = new SolidColorBrush(Colors.Gray),
                });
            }
        }
    }

    /// <summary>矩阵 N×N 热力：行→列吞吐，颜色按结论等级。</summary>
    private void BuildMatrix(GroupResult result, Core.Iperf.Params p)
    {
        var hosts = result.Rounds
            .SelectMany(r => new[] { r.Pair.A, r.Pair.B })
            .Distinct()
            .OrderBy(x => x)
            .ToList();
        if (hosts.Count == 0)
        {
            return;
        }

        var lookup = result.Rounds.ToDictionary(r => (r.Pair.A, r.Pair.B), r => r);

        // 表头行
        var header = new Grid { ColumnSpacing = 4, Margin = new Thickness(0, 0, 0, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        for (int c = 0; c < hosts.Count; c++)
        {
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
            var label = new TextBlock
            {
                Text = ShortIp(hosts[c]),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(label, c + 1);
            header.Children.Add(label);
        }
        MatrixPanel.Children.Add(header);

        for (int r = 0; r < hosts.Count; r++)
        {
            var row = new Grid { ColumnSpacing = 4, Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            var rowLabel = new TextBlock
            {
                Text = ShortIp(hosts[r]),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
            };
            row.Children.Add(rowLabel);

            for (int c = 0; c < hosts.Count; c++)
            {
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
                var cell = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(4, 8, 4, 8),
                    MinHeight = 44,
                };
                if (r == c)
                {
                    cell.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"];
                    cell.Child = new TextBlock
                    {
                        Text = "—",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    };
                }
                else if (lookup.TryGetValue((hosts[r], hosts[c]), out var round))
                {
                    if (round.Status == "done" && round.Summary is { } s)
                    {
                        var v = VerdictEvaluator.Evaluate(s, p.Protocol, 0, p);
                        cell.Background = BrushFor(v.Grade);
                        var tb = new TextBlock
                        {
                            Text = $"{s.AB / 1e6:F0}",
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = new SolidColorBrush(Colors.White),
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        };
                        ToolTipService.SetToolTip(tb, $"{hosts[r]} → {hosts[c]}\n{s.AB / 1e6:F1} Mbps · {v.GradeLabel}");
                        cell.Child = tb;
                    }
                    else
                    {
                        cell.Background = new SolidColorBrush(Colors.DimGray);
                        var tb = new TextBlock
                        {
                            Text = round.Status,
                            FontSize = 10,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = new SolidColorBrush(Colors.White),
                        };
                        ToolTipService.SetToolTip(tb, round.Reason);
                        cell.Child = tb;
                    }
                }
                else
                {
                    cell.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
                    cell.Child = new TextBlock
                    {
                        Text = "·",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                }
                Grid.SetColumn(cell, c + 1);
                row.Children.Add(cell);
            }
            MatrixPanel.Children.Add(row);
        }

        MatrixPanel.Children.Add(new TextBlock
        {
            Text = "单元格为行 → 列 吞吐（Mbps），颜色：很快 / 正常 / 偏慢 / 很差",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Margin = new Thickness(0, 4, 0, 0),
        });
    }

    private static string ShortIp(string ip)
    {
        var parts = ip.Split('.');
        return parts.Length == 4 ? $"…{parts[2]}.{parts[3]}" : ip;
    }

    internal static Brush BrushFor(Grade grade) => grade switch
    {
        Grade.Great => new SolidColorBrush(Colors.SeaGreen),
        Grade.Good => new SolidColorBrush(Colors.Teal),
        Grade.Fair => new SolidColorBrush(Colors.DarkOrange),
        _ => new SolidColorBrush(Colors.IndianRed),
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
