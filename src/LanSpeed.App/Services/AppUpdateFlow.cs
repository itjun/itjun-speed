using System.Net.Http;
using LanSpeed.Core.Update;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LanSpeed.App.Services;

/// <summary>启动时与设置页共用的更新提示（§10.1）。低于最低版本不可跳过。</summary>
public static class AppUpdateFlow
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static int _startupDone;

    public static UpdateCheckResult? Last { get; private set; }

    public static async Task PromptAsync(XamlRoot? root, bool interactive)
    {
        if (root is null || AppServices.Exiting)
        {
            return;
        }

        if (!interactive && Interlocked.Exchange(ref _startupDone, 1) != 0)
        {
            return;
        }

        await Gate.WaitAsync();
        try
        {
            await PromptCoreAsync(root, interactive);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static string Describe(UpdateCheckResult? result)
    {
        if (result is null)
        {
            return "尚未检查更新。";
        }

        if (result.Error is not null)
        {
            return $"检查更新失败：{result.Error}";
        }

        if (result.Offer is null)
        {
            return "尚无 GitHub 发布。";
        }

        UpdateOffer offer = result.Offer;
        return offer.Requirement switch
        {
            UpdateRequirement.Required when offer.DownloadUrl is null =>
                $"当前版本 {offer.Current} 低于最低版本 {Floor(offer)}，但发布里没有可用的 amd64 安装包。",
            UpdateRequirement.Required =>
                $"当前版本 {offer.Current} 低于最低版本 {Floor(offer)}，必须更新到 {offer.Latest}。",
            UpdateRequirement.Optional when offer.DownloadUrl is null =>
                $"发现新版本 {offer.Latest}（当前 {offer.Current}），但发布里没有可用的 amd64 安装包。",
            UpdateRequirement.Optional =>
                $"发现新版本 {offer.Latest}（当前 {offer.Current}），可以稍后更新。",
            _ => $"已是最新版本 {offer.Current}。",
        };
    }

    private static async Task PromptCoreAsync(XamlRoot root, bool interactive)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        var checker = new UpdateChecker(http);
        UpdateCheckResult result = await CheckAsync(checker);
        if (result.Failed || result.Offer is null || result.Offer.Requirement == UpdateRequirement.None)
        {
            if (interactive)
            {
                await ShowMessageAsync(root, "检查更新", Describe(result));
            }

            return;
        }

        UpdateOffer offer = result.Offer;
        while (!AppServices.Exiting)
        {
            bool required = offer.Requirement == UpdateRequirement.Required;
            var dialog = new ContentDialog
            {
                Title = required ? "必须更新" : "发现新版本",
                Content = new TextBlock
                {
                    Text = Body(offer),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 460,
                },
                XamlRoot = root,
                PrimaryButtonText = offer.DownloadUrl is null ? "重试" : "立即更新",
                SecondaryButtonText = required ? "退出" : "稍后",
                DefaultButton = required ? ContentDialogButton.Primary : ContentDialogButton.Secondary,
            };

            ContentDialogResult choice = await dialog.ShowAsync();
            if (choice == ContentDialogResult.Primary)
            {
                if (offer.DownloadUrl is null || offer.FileName is null)
                {
                    UpdateOffer? retried = await RetryAsync(root, checker, offer, required);
                    if (retried is null)
                    {
                        return;
                    }

                    offer = retried;
                    continue;
                }

                string? error = await DownloadAndLaunchAsync(root, http, offer);
                if (error is null || AppServices.Exiting)
                {
                    return;
                }

                offer = offer with { Notes = error };
                continue;
            }

            if (required)
            {
                App.ExitApp();
            }

            return;
        }
    }

    private static async Task<UpdateOffer?> RetryAsync(XamlRoot root, UpdateChecker checker, UpdateOffer previous, bool required)
    {
        UpdateCheckResult again = await CheckAsync(checker);
        if (again.Offer is not null && !again.Failed)
        {
            if (again.Offer.Requirement == UpdateRequirement.None)
            {
                await ShowMessageAsync(root, "检查更新", Describe(again));
                return null;
            }

            return again.Offer;
        }

        if (!required)
        {
            await ShowMessageAsync(root, "检查更新", Describe(again));
            return null;
        }

        return previous with { Notes = Describe(again) };
    }

    private static async Task<string?> DownloadAndLaunchAsync(XamlRoot root, HttpClient http, UpdateOffer offer)
    {
        var text = new TextBlock { Text = "正在下载安装包…", TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
        var dialog = new ContentDialog
        {
            Title = "正在更新",
            Content = text,
            XamlRoot = root,
        };
        string? error = null;
        bool allowClose = false;
        dialog.Closing += (_, e) =>
        {
            if (!allowClose)
            {
                e.Cancel = true;
            }
        };
        dialog.Opened += async (_, _) =>
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LanSpeed",
                    "updates");
                string path = await UpdateDownloader.DownloadAsync(http, offer.DownloadUrl!, offer.FileName!, dir);
                text.Text = "正在打开安装程序…";
                UpdateInstaller.Launch(path);
                allowClose = true;
                App.ExitApp();
            }
            catch (Exception ex)
            {
                error = ex.Message;
                allowClose = true;
                dialog.Hide();
            }
        };
        await dialog.ShowAsync();
        return error;
    }

    private static async Task<UpdateCheckResult> CheckAsync(UpdateChecker checker)
    {
        try
        {
            UpdateCheckResult result = await checker.CheckAsync(
                AppServices.Current.Node.Version,
                AppServices.Current.Settings.UpdateCheckUrl);
            Last = result;
            return result;
        }
        catch (Exception ex)
        {
            var result = new UpdateCheckResult($"检查更新失败：{ex.Message}", null);
            Last = result;
            return result;
        }
    }

    private static async Task ShowMessageAsync(XamlRoot root, string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 },
            CloseButtonText = "确定",
            XamlRoot = root,
        };
        await dialog.ShowAsync();
    }

    private static string Body(UpdateOffer offer)
    {
        string text = Describe(new UpdateCheckResult(null, offer));
        if (string.IsNullOrWhiteSpace(offer.Notes))
        {
            return text;
        }

        string notes = offer.Notes.Trim();
        if (notes.Length > 400)
        {
            notes = notes[..400] + "…";
        }

        return text + "\n\n" + notes;
    }

    private static string Floor(UpdateOffer offer) => offer.Minimum?.ToString() ?? "未声明";
}
