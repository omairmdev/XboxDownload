using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using XboxDownload.Helpers.Network;
using XboxDownload.Helpers.Resources;
using XboxDownload.Helpers.UI;
using XboxDownload.Helpers.Utilities;
using XboxDownload.Models.Store;
using XboxDownload.Services;

namespace XboxDownload.ViewModels.Dialog;

public partial class PriceComparisonDialogViewModel : ObservableObject
{
    private readonly StoreViewModel _storeViewModel;

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string UseCurrencyCode { get; set; }

    private readonly string? _productId;

    public ObservableCollection<MarketMappingEntry> MarketMappings { get; } = [];

    [ObservableProperty]
    public partial MarketMappingEntry? SelectedMarket { get; set; }

    public string ConvertedPrice => string.Format(ResourceHelper.GetString("Store.PriceComparison.ConvertedPrice"), UseCurrencyCode);

    public PriceComparisonDialogViewModel(StoreViewModel storeViewModel)
    {
        _storeViewModel = storeViewModel;
        Title = _storeViewModel.Title!;
        UseCurrencyCode = _storeViewModel.UseCurrencyCode;
        _productId = _storeViewModel.SelectedBundledIndex <= 0 ? _storeViewModel.GameData?.Products[0].ProductId : _storeViewModel.SelectedBundled?.ProductId;

        // Languages to exclude (by currency or unsupported regions)
        var excludedLanguages = new HashSet<string>
        {
            // Liechtenstein - Swiss franc
            "de-LI", 

            // Eurozone countries (EU members using the euro; language codes)
            // Language codes of 16 EU member states
            "de-AT", // Austria
            "nl-BE", // Belgium
            "de-DE", // Germany
            "fr-FR", // France
            "fi-FI", // Finland
            "nl-NL", // Netherlands
            "pt-PT", // Portugal
            "sk-SK", // Slovakia
            "es-ES", // Spain
            "el-GR", // Greece
            "it-IT", // Italy
            "en-IE", // Ireland
            "fr-LU", // Luxembourg (multilingual, mainly French)
            "mt-MT", // Malta (Maltese is one of the official languages)
            "en-CY", // Cyprus (official languages include Greek and Turkish)
            "et-EE", // Estonia 

            // Language codes of non-EU countries/regions that use the euro
            "ca-AD", // Andorra

            // US-dollar area (mostly small countries or territories)
            "ar-AE", "ru-RU",

            "fa-AF", "en-AG", "en-AI", "sq-AL", "hy-AM", "pt-AO", "en-AQ", "en-AS", "nl-AW","sv-AX", "az-AZ",
            "bs-BA", "en-BB", "fr-BF", "fr-BI", "fr-BJ", "fr-BL", "en-BM", "ms-BN", 
            
            // Regions that are unsupported or have no local store
            "ar-LB",
        };

        // Merge markets and filter
        var filteredMarkets = _storeViewModel.Markets
            .Union(MarketBuilder.BuildMarket2())
            .Where(m => !excludedLanguages.Contains(m.Code))
            .ToList();

        // Build the MarketMappingEntry list
        var marketMappings = filteredMarkets
            .Select(m => new MarketMappingEntry(m.Name, m.Code))
            .ToList();

        // Add the extra region entry
        marketMappings.Add(new MarketMappingEntry(ResourceHelper.GetString("Store.PriceComparison.Eurozone"), "de-DE"));

        // Sort and add the final list
        MarketMappings.AddRange(
            marketMappings.OrderBy(m => m.Market, StringComparer.CurrentCultureIgnoreCase));

    }

    [RelayCommand]
    private void UpdateSelection(string? parameter)
    {
        switch (parameter)
        {
            case "SelectAll":
                foreach (var option in MarketMappings)
                    option.IsSelect = true;
                break;

            case "InvertSelection":
                foreach (var option in MarketMappings)
                    option.IsSelect = !option.IsSelect;
                break;
        }
    }

    public event Action? RequestFocus;

    [RelayCommand]
    private async Task QueryAsync()
    {
        if (string.IsNullOrEmpty(_productId)) return;

        if (string.IsNullOrWhiteSpace(UseCurrencyCode))
        {
            UseCurrencyCode = _storeViewModel.UseCurrencyCode;
        }
        else
        {
            if (!string.Equals(UseCurrencyCode, _storeViewModel.UseCurrencyCode, StringComparison.OrdinalIgnoreCase))
            {
                if (!IsValidCurrencyCode(UseCurrencyCode))
                {
                    await DialogHelper.ShowInfoDialogAsync(
                        ResourceHelper.GetString("Store.PriceComparison.InvalidCurrencyTitle"),
                        ResourceHelper.GetString("Store.PriceComparison.InvalidCurrencyMessage"),
                        Icon.Error);
                    RequestFocus?.Invoke();
                    return;
                }
                UseCurrencyCode = UseCurrencyCode.ToUpperInvariant();
                _storeViewModel.UseCurrencyCode = UseCurrencyCode;

                App.Settings.CurrencyCode = UseCurrencyCode;
                SettingsManager.Save(App.Settings);
            }
            else UseCurrencyCode = UseCurrencyCode.ToUpperInvariant();
        }


        if (_storeViewModel.ExchangeRates.IsEmpty)
        {
            if (await ExchangeRateHelper.TryGetExchangeRatesAsync(UseCurrencyCode, _storeViewModel.ExchangeRates))
            {
                _storeViewModel.NextUpdatedRates = DateTime.UtcNow.AddHours(12);
            }
        }

        if (_storeViewModel.ExchangeRates.IsEmpty)
        {
            await DialogHelper.ShowInfoDialogAsync(
                ResourceHelper.GetString("Store.PriceComparison.ExchangeRateFetchFailedTitle"),
                ResourceHelper.GetString("Store.PriceComparison.ExchangeRateFetchFailedMessage"),
                Icon.Error);
            return;
        }

        foreach (var market in MarketMappings)
        {
            market.ConvertPrices = null;
            market.ExchangeRates = null;
        }

        OnPropertyChanged(nameof(ConvertedPrice));
        var tasks = MarketMappings.Where(s => s.IsSelect)
            .Select(market => Task.Run(async () =>
            {
                if (string.IsNullOrEmpty(market.Currency))
                {
                    var url = $"https://displaycatalog.mp.microsoft.com/v7.0/products?bigIds={_productId}&market={market.Code}&languages=neutral&MS-CV=DGU1mcuYo0WMMp+F.1";
                    var responseString = await HttpClientHelper.GetStringContentAsync(url);
                    if (string.IsNullOrEmpty(responseString.Trim())) return;

                    Game? gameData = null;
                    try
                    {
                        gameData = JsonSerializer.Deserialize<Game>(responseString, JsonHelper.PropertyNameInsensitive);
                    }
                    catch
                    {
                        // ignored
                    }

                    if (gameData?.Products.Count >= 1)
                    {
                        var product = gameData.Products[0];
                        var displaySkuAvailabilities = product.DisplaySkuAvailabilities.FirstOrDefault(displaySkuAvailabilities => displaySkuAvailabilities.Sku.SkuType == "full");
                        if (displaySkuAvailabilities != null)
                        {
                            var listPrice1 = product.DisplaySkuAvailabilities[0].Availabilities[0].OrderManagementData.Price.ListPrice;
                            var listPrice2 = product.DisplaySkuAvailabilities[0].Availabilities.Count >= 2 ? product.DisplaySkuAvailabilities[0].Availabilities[1].OrderManagementData.Price.ListPrice : 0;
                            var listPrice = listPrice2 > 0 && listPrice2 < listPrice1 ? listPrice2 : listPrice1;

                            if (listPrice > 0)
                            {
                                var currencyCode = displaySkuAvailabilities.Availabilities[0].OrderManagementData.Price.CurrencyCode.ToUpperInvariant();
                                market.Currency = currencyCode;
                                market.ListPrice = listPrice;
                                if (currencyCode != UseCurrencyCode)
                                {
                                    if (_storeViewModel.ExchangeRates.TryGetValue(currencyCode, out var value))
                                    {
                                        market.ConvertPrices = listPrice / value;
                                        market.ExchangeRates = 1 / value;
                                    }
                                }
                                else
                                {
                                    market.ConvertPrices = listPrice;
                                    market.ExchangeRates = 1;
                                }
                            }
                            else
                            {
                                market.Currency = ResourceHelper.GetString("Store.PriceComparison.Unavailable");
                            }
                        }
                    }
                }
                else
                {
                    var currencyCode = market.Currency;
                    var listPrice = market.ListPrice;
                    if (currencyCode != UseCurrencyCode)
                    {
                        if (_storeViewModel.ExchangeRates.TryGetValue(currencyCode, out var value))
                        {
                            market.ConvertPrices = listPrice / value;
                            market.ExchangeRates = 1 / value;
                        }
                    }
                    else
                    {
                        market.ConvertPrices = listPrice;
                        market.ExchangeRates = 1;
                    }
                }
            })).ToList();

        await Task.WhenAll(tasks);

        var sorted = MarketMappings
            .Where(m => m.ConvertPrices.HasValue)
            .OrderBy(m => m.ConvertPrices.GetValueOrDefault())
            .Concat(MarketMappings.Where(m => !m.ConvertPrices.HasValue))
            .ToList();

        MarketMappings.Clear();
        MarketMappings.AddRange(sorted);
    }

    [RelayCommand]
    private void GetSystemCurrencyCode()
    {
        _storeViewModel.UseCurrencyCode = UseCurrencyCode = ExchangeRateHelper.GetSystemCurrencyCode();

        App.Settings.CurrencyCode = string.Empty;
        SettingsManager.Save(App.Settings);

        RequestFocus?.Invoke();
    }

    [RelayCommand]
    private async Task VisitWebsite()
    {
        var language = SelectedMarket?.Language;
        if (language == "de-DE")
        {
            var raw = CultureInfo.CurrentUICulture.Name;
            language = CultureInfo.CurrentUICulture.Name switch
            {
                "de-AT" or "nl-BE" or "de-DE" or "fr-FR" or "fi-FI" or "nl-NL" or "pt-PT" or "sk-SK" or "es-ES"
                    or "el-GR" or "it-IT" or "en-IE" or "fr-LU" or "mt-MT" or "en-CY" or "et-EE" or "ca-AD" => raw,
                _ => language
            };
        }
        //var url = $"https://www.microsoft.com/{language}/p/_/{_productId}";
        var url = $"https://www.xbox.com/{language}/games/store/_/{_productId}";
        await HttpClientHelper.OpenUrlAsync(url);
    }

    private static bool IsValidCurrencyCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 3)
            return false;

        code = code.ToUpperInvariant();

        // Iterate over all known regions (cultures) to match the currency code
        var regions = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Select(culture =>
            {
                try
                {
                    return new RegionInfo(culture.Name);
                }
                catch
                {
                    return null;
                }
            })
            .Where(region => region != null)
            .DistinctBy(region => region!.ISOCurrencySymbol); // Avoid duplicates

        return regions.Any(r => r!.ISOCurrencySymbol.Equals(code, StringComparison.OrdinalIgnoreCase));
    }
}

public partial class MarketMappingEntry(string market, string language) : ObservableObject
{
    [ObservableProperty]
    public partial bool IsSelect { get; set; } = true;

    public string Market { get; set; } = market;

    public string Language { get; set; } = language;


    [ObservableProperty]
    public partial string Currency { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal? ListPrice { get; set; }

    [ObservableProperty]
    public partial decimal? ConvertPrices { get; set; }

    [ObservableProperty]
    public partial decimal? ExchangeRates { get; set; }

    public string Code =>
        string.IsNullOrEmpty(Language) ? string.Empty :
        Language.Split('-').Length > 1 ? Language.Split('-')[1] : string.Empty;
}
