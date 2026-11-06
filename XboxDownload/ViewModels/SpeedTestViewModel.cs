using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using DynamicData.Binding;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XboxDownload.Helpers.IO;
using XboxDownload.Helpers.Network;
using XboxDownload.Helpers.Resources;
using XboxDownload.Helpers.UI;
using XboxDownload.Helpers.Utilities;
using XboxDownload.Models.SpeedTest;
using XboxDownload.Services;

namespace XboxDownload.ViewModels;

public partial class SpeedTestViewModel : ViewModelBase
{
    private ServiceViewModel ServiceViewModel { get; }

    private readonly string _translationPath = PathHelper.GetResourceFilePath("Translation.json");

    public List<int> TimeoutOptions { get; } = [3, 5, 10];

    [ObservableProperty]
    private partial int SelectedTimeout { get; set; } = 3;

    [ObservableProperty]
    public partial string HeaderText { get; set; }

    [ObservableProperty]
    private partial string SearchLocation { get; set; } = App.Settings.SearchLocation;

    public ObservableCollection<LocationFilter> LocationFilters { get; } = [.. SpeedTestDataBuilder.BuildLocationFilters()];

    public ObservableCollection<ImportOption> ImportOptions { get; } = [.. SpeedTestDataBuilder.BuildImportOptions()];

    public ObservableCollection<SpeedTestFile> SpeedTestFiles { get; } = [.. SpeedTestDataBuilder.BuildSpeedTestFiles()];

    [ObservableProperty]
    public partial ImportOption? SelectedImportOption { get; set; }

    public SpeedTestViewModel(ServiceViewModel serviceViewModel)
    {
        ServiceViewModel = serviceViewModel;

        SelectedImportOption = ImportOptions.FirstOrDefault();
        HeaderText = SelectedImportOption!.Display;

        foreach (var option in LocationFilters)
        {
            option.IsVisible = App.Settings.Culture == "zh-Hans"
                ? option.Key is "ChinaTelecom" or "ChinaUnicom" or "ChinaMobile"
                : option.Key is not ("ChinaTelecom" or "ChinaUnicom" or "ChinaMobile");
        }

        foreach (var option in LocationFilters)
        {
            option.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(LocationFilter.IsSelected)) return;
                if (s is LocationFilter changedOption)
                {
                    ApplyLocationFilter(changedOption.Key);
                }
            };
        }

        WatermarkText = ImportOptions.FirstOrDefault()!.Hint;

        _sortSubject.Select(sortList => IpSource.Connect().Sort(GetMultiComparer(sortList))).Switch().Bind(out _ipItems).Subscribe();
    }

    [ObservableProperty]
    public partial bool IsSortingEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsImportEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsSpeedTest { get; set; }

    private readonly ReadOnlyObservableCollection<IpItem> _ipItems;

    private ReadOnlyObservableCollection<IpItem> IpItems => _ipItems;

    partial void OnSelectedImportOptionChanged(ImportOption? value)
    {
        if (value == null || string.IsNullOrEmpty(value.Key)) return;

        LoadIpCommand.Execute(null);
    }

    [GeneratedRegex(
        @"(?<IP>\d{0,3}\.\d{0,3}\.\d{0,3}\.\d{0,3})\s*\((?<Location>[^\)]*)\)|^[^\d]+(?<IP>\d{0,3}\.\d{0,3}\.\d{0,3}\.\d{0,3})(?<Location>[\u4e00-\u9fa5]+).+ms\d+|\s*(?<IP>\d{0,3}\.\d{0,3}\.\d{0,3}\.\d{0,3})\s*$|(?<IP>\d{0,3}\.\d{0,3}\.\d{0,3}\.\d{0,3})\s+(?<Location>[^\s]+)\s+\<?\d+ms|(?<IP>([\da-fA-F]{1,4}:){3}([\da-fA-F]{0,4}:)+[\da-fA-F]{1,4})\s*\((?<Location>[^\)]*)\)",
        RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex MatchIp();

    [ObservableProperty]
    public partial bool IsXboxCn1Visible { get; set; }

    [ObservableProperty]
    public partial bool IsXboxCn2Visible { get; set; }

    [ObservableProperty]
    public partial bool IsXboxAppVisible { get; set; }

    [ObservableProperty]
    public partial bool IsXboxSeparatorVisible { get; set; }

    [ObservableProperty]
    public partial bool IsPsVisible { get; set; }

    [ObservableProperty]
    public partial bool IsPsSeparatorVisible { get; set; }

    [ObservableProperty]
    public partial bool IsAkamaiVisible { get; set; }

    private string _selectedImportKey = string.Empty;
    [RelayCommand]
    private async Task LoadIpAsync()
    {
        if (string.IsNullOrEmpty(SelectedImportOption?.Target)) return;
        _selectedImportKey = SelectedImportOption.Key;

        CancelFetchAppDownloadUrl();
        ClearSort();
        IpSource.Clear();
        HeaderText = SelectedImportOption.Display;
        IsImportEnabled = false;
        TargetTestUrl = "";
        WatermarkText = SelectedImportOption.Hint;

        var filePath = PathHelper.GetResourceFilePath($"IP.{SelectedImportOption.Key}.txt");
        var fi = new FileInfo(filePath);
        if (!fi.Exists || fi.LastAccessTimeUtc < DateTime.UtcNow.AddHours(-24))
        {
            await UpdateService.DownloadIpAsync(fi);
        }
        var content = fi.Exists ? await File.ReadAllTextAsync(fi.FullName) : string.Empty;
        var items = MatchIp().Matches(content)
            .Where(m => m.Success)
            .Select(m => new IpItem
            {
                Ip = m.Groups["IP"].Value,
                Location = m.Groups["Location"].Value
            })
            .ToList();

        var isAkamai = SelectedImportOption.Key.StartsWith("Akamai");

        if (isAkamai && App.Settings.Culture != "zh-Hans" || App.Settings.Culture == "zh-Hant")
        {
            await TranslationLocation(items);
        }

        IpSource.AddRange(items);

        foreach (var option in LocationFilters)
        {
            option.IsVisible = isAkamai
                ? option.Key is not ("ChinaTelecom" or "ChinaUnicom" or "ChinaMobile")
                : option.Key is "ChinaTelecom" or "ChinaUnicom" or "ChinaMobile";
        }

        IsXboxCn1Visible = IsXboxCn2Visible = IsXboxAppVisible = IsXboxSeparatorVisible = IsPsVisible = IsPsSeparatorVisible = IsAkamaiVisible = UploadAkamaiIpsVisible = false;
        switch (SelectedImportOption.Key)
        {
            case "XboxCn1":
                IsXboxCn1Visible = IsXboxSeparatorVisible = true;
                break;
            case "XboxCn2":
                IsXboxCn2Visible = IsXboxSeparatorVisible = true;
                break;
            case "XboxApp":
                IsXboxAppVisible = IsXboxSeparatorVisible = true;
                break;
            case "Ps":
                IsPsVisible = IsPsSeparatorVisible = true;
                break;
            case "Akamai":
            case "AkamaiV2":
            case "AkamaiV6":
                IsAkamaiVisible = IsXboxAppVisible = IsXboxSeparatorVisible = IsPsVisible = IsPsSeparatorVisible = true;
                if (App.Settings.Culture == "zh-Hans") UploadAkamaiIpsVisible = true;
                break;
        }

        ApplyLocationFilter();
        DownloadFileFilter(SelectedImportOption.Target);
        IsImportEnabled = true;
    }

    public async Task TranslationLocation(List<IpItem> items)
    {
        if (App.Settings.Culture == "zh-Hant")
        {
            foreach (var ip in items)
            {
                var location = ChineseConverter.SimplifiedToTraditional(ip.Location);
                if (!string.IsNullOrEmpty(location))
                    ip.Location = location;
            }
        }
        else
        {
            var translationFile = new FileInfo(_translationPath);
            if (!translationFile.Exists || translationFile.LastAccessTimeUtc < DateTime.UtcNow.AddDays(-7))
            {
                await UpdateService.DownloadIpAsync(translationFile, "{");
            }
            if (translationFile.Exists)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_translationPath);
                    var translation = JsonSerializer.Deserialize<ConcurrentDictionary<string, string>>(json);
                    if (translation is { Count: > 0 })
                    {
                        foreach (var ip in items)
                        {
                            var location = translation?.GetValueOrDefault(ip.Location);
                            if (!string.IsNullOrEmpty(location))
                                ip.Location = location;
                        }
                    }
                }
                catch
                {
                    // ignored
                }
            }
        }
    }

    [ObservableProperty]
    public partial IpItem? SelectedItem { get; set; }

    [ObservableProperty]
    public partial bool UploadAkamaiIpsVisible { get; set; }

    [ObservableProperty]
    private partial bool UploadAkamaiIpsEnabled { get; set; } = App.Settings.UploadAkamaiIpsEnabled;

    partial void OnUploadAkamaiIpsEnabledChanged(bool value)
    {
        App.Settings.UploadAkamaiIpsEnabled = value;
        SettingsManager.Save(App.Settings);
    }

    [RelayCommand]
    private void FilterByLocation()
    {
        SearchLocation = SearchLocation.Replace("　", " ") /* full-width space */.Trim();
        if (!string.Equals(App.Settings.SearchLocation, SearchLocation))
        {
            App.Settings.SearchLocation = SearchLocation;
            SettingsManager.Save(App.Settings);
        }

        var keywords = SearchLocation.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(k => k.Trim()).ToList();

        if (keywords.Count == 0)
        {
            return;
        }

        foreach (var item in IpItems)
        {
            var match = keywords.All(k => item.Location.Contains(k, StringComparison.OrdinalIgnoreCase));
            if (match) item.IsSelected = true;
            item.IsFilterMatched = match;
        }

        if (!IpItems.Any(m => m.IsFilterMatched)) return;
        ClearSort();
        ApplySort("IsFilterMatched", SortDirection.Descending);
    }

    [RelayCommand]
    private void ToggleSelectAll(string isSelect)
    {
        var b = bool.Parse(isSelect);
        foreach (var item in LocationFilters)
        {
            item.IsSelected = b;
        }
        foreach (var ipItem in IpItems)
        {
            ipItem.IsSelected = b;
        }
    }

    [RelayCommand]
    private void ApplyLocationFilter(string? region = null)
    {
        // Full refresh: region is null or empty
        if (string.IsNullOrEmpty(region))
        {
            var isOtherSelected = LocationFilters.FirstOrDefault(f => f.Key == "Other")?.IsSelected == true;
            var knownFilters = LocationFilters
                .Where(f => f is { IsVisible: true, IsSelected: true } && f.Key != "Other")
                .ToList();

            var knownKeywords = isOtherSelected
                ? LocationFilters.Where(f => f.IsVisible && f.Key != "Other").SelectMany(f => f.Keywords).ToList()
                : null;

            foreach (var ip in IpItems)
            {
                // If it matches a selected known-region keyword
                ip.IsSelected = knownFilters.Any(f =>
                    f.Keywords.Any(kw => ip.Location.Contains(kw, StringComparison.OrdinalIgnoreCase)));

                // If this is the "Other" filter and no known keyword matched, set to true
                if (!ip.IsSelected && isOtherSelected && knownKeywords != null)
                {
                    ip.IsSelected = !knownKeywords.Any(kw => ip.Location.Contains(kw, StringComparison.OrdinalIgnoreCase));
                }
            }
            return;
        }

        // Partial update: only update the given region
        var filter = LocationFilters.FirstOrDefault(f => f.IsVisible && f.Key == region);
        if (filter == null) return;

        if (filter.Key == "Other")
        {
            var knownKeywords = LocationFilters
                .Where(f => f.IsVisible && f.Key != "Other")
                .SelectMany(f => f.Keywords)
                .ToList();

            foreach (var ip in IpItems)
            {
                var matchesKnown = knownKeywords.Any(kw => ip.Location.Contains(kw, StringComparison.OrdinalIgnoreCase));
                if (!matchesKnown)
                    ip.IsSelected = filter.IsSelected;
            }
        }
        else
        {
            foreach (var ip in IpItems)
            {
                if (filter.Keywords.Any(kw => ip.Location.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                {
                    ip.IsSelected = filter.IsSelected;
                }
            }
        }
    }

    private void DownloadFileFilter(string target)
    {
        foreach (var option in SpeedTestFiles)
        {
            option.IsVisible = option.Target == target;
        }
    }

    [ObservableProperty]
    public partial string TargetTestUrl { get; set; } = "";

    [ObservableProperty]
    public partial string WatermarkText { get; set; }

    [RelayCommand]
    private async Task FileClickedAsync(object? parameter)
    {
        if (parameter is not SpeedTestFile file) return;
        if (!string.IsNullOrEmpty(file.Url) && file.Url.StartsWith("http"))
        {
            TargetTestUrl = file.Url;
        }
        else
        {
            CancelFetchAppDownloadUrl();
            _fetchAppDownloadUrlCancellation = new CancellationTokenSource();
            var fetchToken = _fetchAppDownloadUrlCancellation.Token;
            TargetTestUrl = ResourceHelper.GetString("SpeedTest.GettingDownloadLink");
            TargetTestUrl = await UpdateService.FetchAppDownloadUrlAsync(file.Url, fetchToken);
        }
    }

    #region MenuItem

    [RelayCommand]
    private Task ExportDnsmasqAsync(Visual? visual) => ExportRulesAsync(visual, _selectedImportKey, SelectedItem?.Ip, "dnsmasq");

    [RelayCommand]
    private Task ExportHostsAsync(Visual? visual) => ExportRulesAsync(visual, _selectedImportKey, SelectedItem?.Ip, "hosts");

    private static async Task ExportRulesAsync(Visual? visual, string key, string? ip, string exportFormat)
    {
        if (visual == null || ip == null) return;

        var clipboard = TopLevel.GetTopLevel(visual)?.Clipboard;
        if (clipboard == null) return;

        var exportContent = DnsMappingGenerator.GenerateDnsMapping(key, ip, exportFormat, "Export");

        await ClipboardHelper.SetTextAsync(clipboard, exportContent);

        var lines = exportContent.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var count = lines.Count(line => !line.StartsWith('#')); // Exclude lines starting with # (ignoring leading whitespace)

        var cnXboxDownloadHint = App.Settings.Culture == "zh-Hans" && exportContent.Contains(".xboxlive.cn") ?
            $"{Environment.NewLine}Tip:{Environment.NewLine}Xbox in mainland China may download games from .com domains,{Environment.NewLine}so the listening-redirect mode must be enabled to accelerate those downloads." :
            "";

        await DialogHelper.ShowInfoDialogAsync(
            ResourceHelper.GetString("SpeedTest.MenuItem.ExportRules"),
            string.Format(ResourceHelper.GetString("SpeedTest.MenuItem.RulesCopiedToClipboard"), count) + Environment.NewLine + Environment.NewLine + exportContent + cnXboxDownloadHint,
            Icon.Success);
    }

    [RelayCommand]
    private async Task CustomIpAsync(string parameter)
    {
        var ip = SelectedItem?.Ip;
        if (ip == null) return;

        if (ServiceViewModel.IsListening && parameter != "Akamai")
            await ServiceViewModel.ToggleListeningAsync();

        var mainWindowVm = Ioc.Default.GetRequiredService<MainWindowViewModel>();
        const int tabIndex = 0;
        switch (parameter)
        {
            case "XboxGlobal":
                ServiceViewModel.XboxGlobalIp = ip;
                ServiceViewModel.FocusText($"{parameter}Ip");
                mainWindowVm.SelectedTabIndex = tabIndex;
                break;
            case "XboxCn1":
                ServiceViewModel.XboxCn1Ip = ip;
                ServiceViewModel.FocusText($"{parameter}Ip");
                mainWindowVm.SelectedTabIndex = tabIndex;
                break;
            case "XboxCn2":
                ServiceViewModel.XboxCn2Ip = ip;
                ServiceViewModel.FocusText($"{parameter}Ip");
                mainWindowVm.SelectedTabIndex = tabIndex;
                break;
            case "XboxApp":
                ServiceViewModel.XboxAppIp = ip;
                ServiceViewModel.FocusText($"{parameter}Ip");
                mainWindowVm.SelectedTabIndex = tabIndex;
                break;
            case "XboxGlobalCnApps":
                ServiceViewModel.XboxGlobalIp = ip;
                ServiceViewModel.XboxCn1Ip = ip;
                ServiceViewModel.XboxCn2Ip = ip;
                ServiceViewModel.XboxAppIp = ip;
                ServiceViewModel.FocusText("XboxGlobalIp");
                mainWindowVm.SelectedTabIndex = tabIndex;
                break;
            case "Ps":
                ServiceViewModel.PsIp = ip;
                ServiceViewModel.FocusText($"{parameter}Ip");
                mainWindowVm.SelectedTabIndex = tabIndex;
                break;
            case "Ns":
                ServiceViewModel.NsIp = ip;
                ServiceViewModel.FocusText($"{parameter}Ip");
                mainWindowVm.SelectedTabIndex = tabIndex;
                break;
            case "Ea":
                ServiceViewModel.EaIp = ip;
                ServiceViewModel.FocusText($"{parameter}Ip");
                mainWindowVm.SelectedTabIndex = tabIndex;
                break;
            case "Battle":
                ServiceViewModel.BattleIp = ip;
                ServiceViewModel.FocusText($"{parameter}Ip");
                mainWindowVm.SelectedTabIndex = tabIndex;
                break;
            case "Akamai":
                var cdnVm = Ioc.Default.GetRequiredService<CdnViewModel>();
                cdnVm.AkamaiCdnIp = ip;
                cdnVm.FocusText();
                mainWindowVm.SelectedTabIndex = 3;
                break;
        }
    }

    [RelayCommand]
    private async Task CopyIpAsync(Visual? visual)
    {
        if (visual == null) return;

        var clipboard = TopLevel.GetTopLevel(visual)?.Clipboard;
        if (clipboard == null) return;

        var ip = SelectedItem?.Ip;
        if (string.IsNullOrEmpty(ip)) return;
        await ClipboardHelper.SetTextAsync(clipboard, ip);
    }

    #endregion

    #region Host

    [RelayCommand]
    private async Task WriteHostsAsync()
    {
        var ip = SelectedItem?.Ip;
        if (string.IsNullOrEmpty(ip)) return;

        FileInfo fi = new(PathHelper.SystemHostsPath);

        try
        {
            string content;
            await using (var fs = fi.Open(FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite))
            {
                using StreamReader sr = new(fs);
                content = await sr.ReadToEndAsync();
            }

            var patterns = DnsMappingGenerator.GenerateHostRegexPattern(_selectedImportKey);

            content = Regex.Replace(content, patterns, "");

            var dnsMapping = DnsMappingGenerator.GenerateDnsMapping(_selectedImportKey, ip, "hosts", "Write");

            content = content.Trim() + Environment.NewLine + dnsMapping;

            if ((fi.Attributes & FileAttributes.ReadOnly) != 0)
                fi.Attributes &= ~FileAttributes.ReadOnly;

            await using (var fs = fi.Open(FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            {
                await using var sw = new StreamWriter(fs);
                await sw.WriteAsync(content);
            }

            var count = dnsMapping
                .Split(["\r\n", "\r", "\n"], StringSplitOptions.None)
                .Count(line => !string.IsNullOrWhiteSpace(line));

            var cnXboxDownloadHint = App.Settings.Culture == "zh-Hans" && dnsMapping.Contains(".xboxlive.cn") ?
                $"{Environment.NewLine}Tip:{Environment.NewLine}Xbox in mainland China may download games from .com domains,{Environment.NewLine}so the listening-redirect mode must be enabled to accelerate those downloads." :
                "";

            await DialogHelper.ShowInfoDialogAsync(
                ResourceHelper.GetString("SpeedTest.MenuItem.WriteToSystemHostsFileSuccessful"),
                string.Format(ResourceHelper.GetString("SpeedTest.MenuItem.WriteToSystemHostsFileSuccessfulMsg"), count) + Environment.NewLine + Environment.NewLine + dnsMapping + cnXboxDownloadHint,
                Icon.Success);
        }
        catch (Exception ex)
        {
            await DialogHelper.ShowInfoDialogAsync(
                ResourceHelper.GetString("SpeedTest.MenuItem.WriteToSystemHostsFileFailed"),
                ResourceHelper.GetString("SpeedTest.MenuItem.WriteToSystemHostsFileFailedMsg") + Environment.NewLine + Environment.NewLine + ex.Message,
                Icon.Error);
        }
    }

    [RelayCommand]
    private async Task ClearHostsAsync()
    {
        try
        {
            var content = await File.ReadAllTextAsync(PathHelper.SystemHostsPath);

            // Clean up leftovers from an abnormal exit
            if (!ServiceViewModel.IsListening && HostsHelper.RemoveAppSectionRegex().IsMatch(content))
            {
                content = HostsHelper.RemoveAppSectionRegex().Replace(content, "").Trim();
                await File.WriteAllTextAsync(PathHelper.SystemHostsPath, content);
            }

            // Split into lines
            var lines = content.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);

            // Find the lines that will be removed
            var removedLines = lines.Where(line => line.Contains($"# {nameof(XboxDownload)}")).ToList();

            if (removedLines.Count == 0)
            {
                await DialogHelper.ShowInfoDialogAsync(
                    ResourceHelper.GetString("SpeedTest.Dialog.ConfirmCleanupHostsTitle"),
                    string.Format(ResourceHelper.GetString("SpeedTest.Dialog.NothingToCleanHosts"), nameof(XboxDownload)),
                    Icon.Info);
                return;
            }

            // Ask for confirmation
            var preview = string.Join(Environment.NewLine, removedLines);
            var confirm = await DialogHelper.ShowConfirmDialogAsync(
                ResourceHelper.GetString("SpeedTest.Dialog.ConfirmCleanupHostsTitle", nameof(XboxDownload)),
                string.Format(ResourceHelper.GetString("SpeedTest.Dialog.ConfirmDeletionHosts"), removedLines.Count, nameof(XboxDownload)) + Environment.NewLine + Environment.NewLine + preview,
                Icon.Question);
            if (!confirm) return;

            // Keep the remaining content
            var cleaned = string.Join(Environment.NewLine, lines.Where(line => !line.Contains($"# {nameof(XboxDownload)}"))).Trim() + Environment.NewLine;

            // Write back to the hosts file
            await File.WriteAllTextAsync(PathHelper.SystemHostsPath, cleaned);
        }
        catch (Exception ex)
        {
            await DialogHelper.ShowInfoDialogAsync(
                ResourceHelper.GetString("SpeedTest.Dialog.WriteToSystemHostsFileFailed"),
                ResourceHelper.GetString("SpeedTest.Dialog.WriteToSystemHostsFileFailedMsg") + Environment.NewLine + Environment.NewLine + ex.Message,
                Icon.Error);
        }
    }

    #endregion

    #region Sorting

    public Action? ClearSortRequested { get; set; }

    public readonly SourceList<IpItem> IpSource = new();

    private readonly BehaviorSubject<List<(string Property, SortDirection Direction)>> _sortSubject = new([("Location", SortDirection.Ascending)]);

    private static readonly Dictionary<string, Func<IpItem, IComparable?>> PropertySelector = new()
    {
        ["IsSelected"] = p => p.IsSelected,
        ["Ip"] = p => p.Ip,
        ["Location"] = p => p.Location,
        ["Ttl"] = p => p.Ttl ?? int.MinValue,
        ["RoundtripTime"] = p => p.RoundtripTime ?? int.MinValue,
        ["Speed"] = p => p.Speed ?? double.MinValue,
        ["IsFilterMatched"] = p => p.IsFilterMatched
    };

    // Custom multi-field comparer
    private static MultiKeyComparer<IpItem> GetMultiComparer(List<(string Property, SortDirection Direction)> sortList)
    {
        var keys = new List<(Func<IpItem, IComparable?> Selector, SortDirection Direction)>();

        foreach (var (property, direction) in sortList)
        {
            if (PropertySelector.TryGetValue(property, out var selector))
                keys.Add((selector, direction));
        }

        if (keys.Count == 0)
        {
            // Default sort order
            keys.Add((p => p.Location, SortDirection.Ascending));
        }

        return new MultiKeyComparer<IpItem>(keys);
    }

    // Multi-field comparer implementation
    private class MultiKeyComparer<T>(List<(Func<T, IComparable?> Selector, SortDirection Direction)> keys) : IComparer<T>
    {
        public int Compare(T? x, T? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            foreach (var (selector, direction) in keys)
            {
                var a = selector(x);
                var b = selector(y);

                var result = a switch
                {
                    null when b == null => 0,
                    null => -1,
                    _ => b == null ? 1 : Comparer<IComparable>.Default.Compare(a, b)
                };

                if (result != 0)
                    return direction == SortDirection.Ascending ? result : -result;
            }
            return 0;
        }
    }

    public void ApplySort(string propertyName, SortDirection direction, bool append = false)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            // Clear sort rules and use the default
            _sortSubject.OnNext([("Location", SortDirection.Ascending)]);
            return;
        }

        var currentSortList = _sortSubject.Value.ToList();

        if (append)
        {
            // Append or replace the sort direction of an existing property
            var index = currentSortList.FindIndex(s => s.Property == propertyName);
            if (index >= 0)
            {
                currentSortList[index] = (propertyName, direction);
            }
            else
            {
                currentSortList.Add((propertyName, direction));
            }
        }
        else
        {
            // Use only the current sort criterion
            currentSortList = [(propertyName, direction)];
        }

        _sortSubject.OnNext(currentSortList);
    }

    private void ClearSort()
    {
        ClearSortRequested?.Invoke(); // Tell the View to clear the arrows
        _sortSubject.OnNext([("Location", SortDirection.Ascending)]);
    }

    #endregion

    #region SpeedTest

    /// <summary>
    /// Token source used to cancel the speed test and download-link retrieval.
    /// </summary>
    private CancellationTokenSource? _speedTestCancellation, _fetchAppDownloadUrlCancellation;

    /// <summary>
    /// Starts speed-testing all selected IP entries.
    /// Sorting and import are disabled while testing and the sort state is cleared.
    /// Each selected IP is pinged and download-speed tested in turn;
    /// the HTTP headers and Range length are adjusted automatically for the target URL and import type.
    /// When finished, if running in mainland China, preferred Akamai nodes may be uploaded.
    /// </summary>
    [RelayCommand]
    private async Task StartSpeedTestAsync()
    {
        if (IpItems.Count == 0 || !IpItems.Any(p => p.IsSelected)) return;

        StopSpeedTest();
        _speedTestCancellation = new CancellationTokenSource();
        var token = _speedTestCancellation.Token;

        IsSpeedTest = true;
        IsSortingEnabled = IsImportEnabled = false;
        ClearSortRequested?.Invoke();
        _sortSubject.OnNext([("IsSelected", SortDirection.Descending)]);

        var uri = await EnsureValidTargetTestUrlAsync();

        if (uri != null)
        {

            var rangeTo = SelectedImportOption!.Key.StartsWith("Akamai") ? 31457279 : 52428799; // Test 30 MB for foreign IPs, 50 MB for domestic IPs
            var timeout = TimeSpan.FromSeconds(SelectedTimeout);
            var userAgent = uri.Host.EndsWith(".nintendo.net") ? "XboxDownload/Nintendo NX" : "XboxDownload";

            foreach (var item in IpItems.Where(p => p.IsSelected))
            {
                if (token.IsCancellationRequested)
                    break;

                await SpeedTestService.PingAndTestAsync(item, uri, rangeTo, timeout, userAgent, token);
            }

            _ = UploadPreferredAkamaiIpsIfInChinaAsync();
        }
        else
        {
            foreach (var item in IpItems.Where(p => p.IsSelected))
            {
                item.Ttl = null;
                item.RoundtripTime = null;
                item.Speed = null;

                if (token.IsCancellationRequested)
                    break;

                if (!IPAddress.TryParse(item.Ip, out var ip)) return;
                await SpeedTestService.PingAsync(item, ip, token);
            }
        }

        _speedTestCancellation?.Dispose();
        _speedTestCancellation = null;
        IsSortingEnabled = IsImportEnabled = true;
        IsSpeedTest = false;
    }

    /// <summary>
    /// Speed-tests a single IP entry
    /// </summary>
    /// <param name="item">The IP entry to test.</param>
    [RelayCommand]
    private async Task RunSingleSpeedTestAsync(IpItem? item)
    {
        if (item == null || IsSpeedTest) return;

        StopSpeedTest();
        _speedTestCancellation = new CancellationTokenSource();
        var token = _speedTestCancellation.Token;

        var uri = await EnsureValidTargetTestUrlAsync();

        if (uri != null)
        {
            var rangeTo = SelectedImportOption!.Key.StartsWith("Akamai") ? 31457279 : 52428799; // Test 30 MB for foreign IPs, 50 MB for domestic IPs
            var timeout = TimeSpan.FromSeconds(SelectedTimeout);
            var userAgent = uri.Host.EndsWith(".nintendo.net") ? "XboxDownload/Nintendo NX" : "XboxDownload";

            await SpeedTestService.PingAndTestAsync(item, uri, rangeTo, timeout, userAgent, token);

            _ = UploadPreferredAkamaiIpsIfInChinaAsync();
        }
        else
        {
            item.Ttl = null;
            item.RoundtripTime = null;
            item.Speed = null;

            if (!IPAddress.TryParse(item.Ip, out var ip)) return;
            await SpeedTestService.PingAsync(item, ip, token);
        }
    }

    /// <summary>
    /// Ensures the configured speed-test target is a valid download URL.
    /// If <see cref="TargetTestUrl"/> is a valid absolute address it is returned as is;
    /// otherwise the address is looked up in the default speed-test entries;
    /// if it is an identifier, <see cref="UpdateService.FetchAppDownloadUrlAsync"/> builds the download address.
    /// </summary>
    /// <returns>A valid download <see cref="Uri"/>, or <c>null</c> if none can be obtained.</returns>
    private async Task<Uri?> EnsureValidTargetTestUrlAsync()
    {
        if (Uri.TryCreate(TargetTestUrl, UriKind.Absolute, out var baseUri))
            return baseUri;

        var url = SpeedTestFiles.FirstOrDefault(p => p.IsVisible)?.Url;
        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            TargetTestUrl = url;
        }
        else
        {
            CancelFetchAppDownloadUrl();
            _fetchAppDownloadUrlCancellation = new CancellationTokenSource();
            var fetchToken = _fetchAppDownloadUrlCancellation.Token;

            TargetTestUrl = ResourceHelper.GetString("SpeedTest.GettingDownloadLink");
            TargetTestUrl = await UpdateService.FetchAppDownloadUrlAsync(url, fetchToken);
        }

        return Uri.TryCreate(TargetTestUrl, UriKind.Absolute, out baseUri) ? baseUri : null;
    }

    private readonly Dictionary<string, DateTime> _uploadedIpTimes = [];

    /// <summary>
    /// Uploads Akamai nodes that performed well in mainland China.
    /// Only runs when uploading is enabled, the option is visible, the UI language is Simplified Chinese,
    /// and an IP in a listed Asian region (e.g. Hong Kong, Japan) scored >= 20.
    /// Before uploading, an API call checks the user is in mainland China (excluding HK/MO/TW).
    /// </summary>
    private async Task UploadPreferredAkamaiIpsIfInChinaAsync()
    {
        // Skip if the upload option is hidden/disabled or the language is not Simplified Chinese
        if (!(UploadAkamaiIpsVisible && UploadAkamaiIpsEnabled && App.Settings.Culture == "zh-Hans"))
            return;

        // Preferred regions: only the listed Asian locations (matched against Chinese location names in the data)
        var preferredRegions = new[] { "香港", "台湾", "日本", "韩国", "新加坡" }; // Hong Kong, Taiwan, Japan, South Korea, Singapore (the server data uses these Chinese names)
        var now = DateTime.UtcNow;

        // Filter: speed >= 20, preferred region, not uploaded within the last 48 hours
        var list = IpItems
            .Where(p =>
                p.Speed >= 20 &&
                preferredRegions.Any(r => p.Location.Contains(r)) &&
                (!_uploadedIpTimes.TryGetValue(p.Ip, out var lastTime) || (now - lastTime) > TimeSpan.FromHours(48))
            )
            .ToList();

        // Nothing qualifies: return
        if (list.Count == 0)
            return;

        // Query an API for geolocation (to detect mainland China)
        var result = await IpGeoHelper.GetCountryFromMultipleApisAsync();

        // Not in mainland China: skip the upload
        if (!string.Equals(result, "CN", StringComparison.OrdinalIgnoreCase))
            return;

        // Build the JSON array to upload
        var jsonArray = new JsonArray();
        foreach (var item in list)
        {
            jsonArray.Add(new JsonObject
            {
                ["ip"] = item.Ip,
                ["location"] = item.Location,
                ["speed"] = item.Speed
            });
            _uploadedIpTimes[item.Ip] = now;
        }

        // POST it to the server endpoint
        using var response = await HttpClientHelper.SendRequestAsync(
            "Akamai/Better",
            "POST",
            jsonArray.ToJsonString(),
            "application/json",
            name: HttpClientNames.XboxDownload);

        var filePath = PathHelper.GetResourceFilePath("IP.AkamaiV2.txt");
        if (File.Exists(filePath)) File.SetLastWriteTime(filePath, DateTime.UtcNow.AddDays(-7));
    }

    private void CancelFetchAppDownloadUrl()
    {
        _fetchAppDownloadUrlCancellation?.Cancel();
        _fetchAppDownloadUrlCancellation?.Dispose();
        _fetchAppDownloadUrlCancellation = null;
    }

    [RelayCommand]
    private void StopSpeedTest()
    {
        CancelFetchAppDownloadUrl();

        _speedTestCancellation?.Cancel();
        _speedTestCancellation?.Dispose();
        _speedTestCancellation = null;
    }

    #endregion
}
