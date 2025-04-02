/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
*/

using NodaTime;
using QuantConnect.Util;
using QuantConnect.Data;
using QuantConnect.Logging;
using QuantConnect.Interfaces;
using QuantConnect.Securities;
using QuantConnect.Configuration;
using QuantConnect.Lean.Engine.DataFeeds;
using QuantConnect.Data.UniverseSelection;
using DataFeeds = QuantConnect.Lean.Engine.DataFeeds;
using QuantConnect.DownloaderDataProvider.Launcher.Models;
using QuantConnect.DownloaderDataProvider.Launcher.Models.Constants;
using QuantConnect.Lean.Engine.HistoricalData;
using System.Globalization;
using System.IO.Compression;
using Newtonsoft.Json;
using System.Security.Policy;

namespace QuantConnect.DownloaderDataProvider.Launcher;
public static class Program
{
    /// <summary>
    /// Synchronizer in charge of guaranteeing a single operation per file path
    /// </summary>
    private readonly static KeyStringSynchronizer DiskSynchronizer = new();

    /// <summary>
    /// The provider used to cache history data files
    /// </summary>
    private static readonly IDataCacheProvider _dataCacheProvider = new DiskDataCacheProvider(DiskSynchronizer);

    /// <summary>
    /// Represents the time interval of 5 seconds.
    /// </summary>
    private static TimeSpan _logDisplayInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Provides access to exchange hours and raw data times zones in various markets
    /// </summary>
    private static readonly MarketHoursDatabase _marketHoursDatabase = MarketHoursDatabase.FromDataFolder();

    private static IApi _api;

    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    /// <param name="args">Command-line arguments passed to the application.</param>
    public static void Main(string[] args)
    {
        // Parse report arguments and merge with config to use in the optimizer
        if (args.Length > 0)
        {
            Config.MergeCommandLineArgumentsWithConfiguration(DownloaderDataProviderArgumentParser.ParseArguments(args));
        }

        InitializeConfigurations();

        var dataDownloader = Composer.Instance.GetExportedValueByTypeName<IDataDownloader>(Config.Get(DownloaderCommandArguments.CommandDownloaderDataDownloader));
        var commandDataType = Config.Get(DownloaderCommandArguments.CommandDataType).ToUpperInvariant();

        switch (commandDataType)
        {
            case "UNIVERSE":
                RunUniverseDownloader(dataDownloader, new DataUniverseDownloadConfig());
                break;
            case "TRADE":
            case "QUOTE":
            case "OPENINTEREST":
                RunDownload(dataDownloader, new DataDownloadConfig(), Globals.DataFolder, _dataCacheProvider);
                break;
            default:
                Log.Error($"QuantConnect.DownloaderDataProvider.Launcher: Unsupported command data type '{commandDataType}'. Valid options: UNIVERSE, TRADE, QUOTE, OPENINTEREST.");
                break;
        }
    }

    /// <summary>
    /// Executes a data download operation using the specified data downloader.
    /// </summary>
    /// <param name="dataDownloader">An instance of an object implementing the <see cref="IDataDownloader"/> interface, responsible for downloading data.</param>
    /// <param name="dataDownloadConfig">Configuration settings for the data download operation.</param>
    /// <param name="dataDirectory">The directory where the downloaded data will be stored.</param>
    /// <param name="dataCacheProvider">The provider used to cache history data files</param>
    /// <param name="mapSymbol">True if the symbol should be mapped while writing the data</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dataDownloader"/> is null.</exception>
    public static void RunDownload(IDataDownloader dataDownloader, DataDownloadConfig dataDownloadConfig, string dataDirectory, IDataCacheProvider dataCacheProvider, bool mapSymbol = true)
    {
        if (dataDownloader == null)
        {
            throw new ArgumentNullException(nameof(dataDownloader), "The data downloader instance cannot be null. Please ensure that a valid instance of data downloader is provided.");
        }

        if (Config.GetBool(DownloaderCommandArguments.CommandWebSymbols, false))
        {
            //Download symbol list from web instead
            string url = Config.Get(DownloaderCommandArguments.CommandWebSymbolsURL);
            SymbolsList symbolList = new SymbolsList();
            try
            {
                var content = _api.Download(url, new List<KeyValuePair<string, string>>() { new KeyValuePair<string, string>("Key", Config.Get(DownloaderCommandArguments.CommandWebSymbolsKey)) }, null, null);
                if (string.IsNullOrWhiteSpace(content))
                {
                    Log.Error($"No Trading Symbols were returned from symbols URL {url}");
                    return;
                }
                var list = JsonConvert.DeserializeObject<SymbolsListRaw>(content);
                if (list == null || list.Symbols == null || list.Symbols.Count == 0)
                {
                    Log.Error($"No Trading Symbols were deserialized from URL {url}");
                    return;
                }
                symbolList.Symbols = list.Symbols.Select(s => new SymbolObj() { Symbol = s.Symbol, Type = Enum.Parse<SecurityType>(s.Type), Action = s.Action }).ToList();
                List<Symbol> symList = new List<Symbol>();
                
                foreach (var symbol in symbolList.Symbols)
                {
                    symList.Add(Symbol.Create(symbol.Symbol, symbol.Type, Market.USA));
                }
                dataDownloadConfig.Symbols = symList;
            }
            catch (Exception ex)
            {
                Log.Error($"Exception occurs while getting Trading Symbols were from URL {url} : {ex.Message} {ex.StackTrace}");
                return;
            }
        }

        var totalDownloadSymbols = dataDownloadConfig.Symbols.Count;
        var completeSymbolCount = 0;
        var startDownloadUtcTime = DateTime.UtcNow;

        foreach (var symbol in dataDownloadConfig.Symbols)
        {
            var downloadParameters = new DataDownloaderGetParameters(symbol, dataDownloadConfig.Resolution, dataDownloadConfig.StartDate, dataDownloadConfig.EndDate, dataDownloadConfig.TickType);

            if (Config.GetBool(DownloaderCommandArguments.CommandUpdate,false))
            {
                var date = GetLastDataDate(symbol, dataDownloadConfig.TickType, dataDownloadConfig.Resolution);
                if (date.HasValue)
                {
                    downloadParameters.StartUtc = date.Value.AddDays(-1);
                    downloadParameters.EndUtc = DateTime.UtcNow;
                    if (downloadParameters.StartUtc >= downloadParameters.EndUtc)
                    {
                        Log.Trace($"DownloaderDataProvider.Main(): No data available for the following parameters: {downloadParameters}");
                        continue;
                    }
                }
            }

            Log.Trace($"DownloaderDataProvider.Main(): Starting download {downloadParameters}");
            var downloadedData = dataDownloader.Get(downloadParameters);

            if (downloadedData == null)
            {
                completeSymbolCount++;
                Log.Trace($"DownloaderDataProvider.Main(): No data available for the following parameters: {downloadParameters}");
                continue;
            }

            var (dataTimeZone, exchangeTimeZone) = GetDataAndExchangeTimeZoneBySymbol(symbol);

            var writer = new LeanDataWriter(dataDownloadConfig.Resolution, symbol, dataDirectory, dataDownloadConfig.TickType, dataCacheProvider, mapSymbol: mapSymbol);

            var groupedData = DataFeeds.DownloaderDataProvider.FilterAndGroupDownloadDataBySymbol(
                downloadedData,
                symbol,
                dataDownloadConfig.DataType,
                exchangeTimeZone,
                dataTimeZone,
                downloadParameters.StartUtc,
                downloadParameters.EndUtc);

            var lastLogStatusTime = DateTime.UtcNow;

            foreach (var data in groupedData)
            {
                writer.Write(data.Select(data =>
                {
                    var utcNow = DateTime.UtcNow;
                    if (utcNow - lastLogStatusTime >= _logDisplayInterval)
                    {
                        lastLogStatusTime = utcNow;
                        Log.Trace($"Downloading data for {downloadParameters.Symbol}. Please hold on...");
                    }
                    return data;
                }));
            }

            completeSymbolCount++;
            var symbolPercentComplete = (double)completeSymbolCount / totalDownloadSymbols * 100;
            Log.Trace($"DownloaderDataProvider.RunDownload(): {symbolPercentComplete:F2}% complete ({completeSymbolCount} out of {totalDownloadSymbols} symbols)");

            Log.Trace($"DownloaderDataProvider.RunDownload(): Download completed for {downloadParameters.Symbol} at {downloadParameters.Resolution} resolution, " +
                $"covering the period from {dataDownloadConfig.StartDate} to {dataDownloadConfig.EndDate}.");
        }
        Log.Trace($"All downloads completed in {(DateTime.UtcNow - startDownloadUtcTime).TotalSeconds:F2} seconds.");
    }

    /// <summary>
    /// Initiates the universe downloader using the provided configuration.
    /// </summary>
    /// <param name="dataDownloader">The data downloader instance.</param>
    /// <param name="dataUniverseDownloadConfig">The universe download configuration.</param>
    private static void RunUniverseDownloader(IDataDownloader dataDownloader, DataUniverseDownloadConfig dataUniverseDownloadConfig)
    {
        foreach (var symbol in dataUniverseDownloadConfig.Symbols)
        {
            var universeDownloadParameters = new DataUniverseDownloaderGetParameters(symbol, dataUniverseDownloadConfig.StartDate, dataUniverseDownloadConfig.EndDate);
            UniverseExtensions.RunUniverseDownloader(dataDownloader, universeDownloadParameters);
        }
    }

    /// <summary>
    /// Retrieves the data time zone and exchange time zone associated with the specified symbol.
    /// </summary>
    /// <param name="symbol">The symbol for which to retrieve time zones.</param>
    /// <returns>
    /// A tuple containing the data time zone and exchange time zone.
    /// The data time zone represents the time zone for data related to the symbol.
    /// The exchange time zone represents the time zone for trading activities related to the symbol.
    /// </returns>
    private static (DateTimeZone dataTimeZone, DateTimeZone exchangeTimeZone) GetDataAndExchangeTimeZoneBySymbol(Symbol symbol)
    {
        var entry = _marketHoursDatabase.GetEntry(symbol.ID.Market, symbol, symbol.SecurityType);
        return (entry.DataTimeZone, entry.ExchangeHours.TimeZone);
    }

    /// <summary>
    /// Initializes various configurations for the application.
    /// This method sets up logging, data providers, map file providers, and factor file providers.
    /// </summary>
    /// <remarks>
    /// The method reads configuration values to determine whether debugging is enabled,
    /// which log handler to use, and which data, map file, and factor file providers to initialize.
    /// </remarks>
    /// <seealso cref="Log"/>
    /// <seealso cref="Config"/>
    /// <seealso cref="Composer"/>
    /// <seealso cref="ILogHandler"/>
    /// <seealso cref="IDataProvider"/>
    /// <seealso cref="IMapFileProvider"/>
    /// <seealso cref="IFactorFileProvider"/>
    public static void InitializeConfigurations()
    {
        Log.DebuggingEnabled = Config.GetBool("debug-mode", false);
        Log.LogHandler = Composer.Instance.GetExportedValueByTypeName<ILogHandler>(Config.Get("log-handler", "CompositeLogHandler"));
        Globals.ResultsDestinationFolder = Config.Get("results-destination-folder", Directory.GetCurrentDirectory());
        var dataProvider = Composer.Instance.GetExportedValueByTypeName<IDataProvider>("DefaultDataProvider");
        var mapFileProvider = Composer.Instance.GetExportedValueByTypeName<IMapFileProvider>(Config.Get("map-file-provider", "LocalDiskMapFileProvider"));
        var factorFileProvider = Composer.Instance.GetExportedValueByTypeName<IFactorFileProvider>(Config.Get("factor-file-provider", "LocalDiskFactorFileProvider"));
        _api = Composer.Instance.GetExportedValueByTypeName<IApi>(Config.Get("api-handler"));
        var optionChainProvider = Composer.Instance.GetPart<IOptionChainProvider>();
        if (optionChainProvider == null)
        {
            var historyManager = Composer.Instance.GetExportedValueByTypeName<HistoryProviderManager>(nameof(HistoryProviderManager));
            historyManager.Initialize(new HistoryProviderInitializeParameters(null, null, dataProvider, _dataCacheProvider,
                mapFileProvider, factorFileProvider, _ => { }, false, new DataPermissionManager(), null, new AlgorithmSettings()));
            var baseOptionChainProvider = new LiveOptionChainProvider();
            baseOptionChainProvider.Initialize(new(mapFileProvider, historyManager));
            optionChainProvider = new CachingOptionChainProvider(baseOptionChainProvider);
            Composer.Instance.AddPart(optionChainProvider);
        }

        mapFileProvider.Initialize(dataProvider);
        factorFileProvider.Initialize(mapFileProvider, dataProvider);
        _api.Initialize(0, "", Globals.DataFolder);
    }

    public static DateTime? GetLastDataDate(Symbol symbol, TickType tickType, Resolution res)
    {
        var zipFileName = LeanData.GenerateZipFilePath(Globals.DataFolder, symbol, DateTime.Now, res, tickType);
        if (string.IsNullOrWhiteSpace(zipFileName)) return null;
        string dir = Path.GetDirectoryName(zipFileName);

        //if resolution is hourly
        switch (res)
        {
            case Resolution.Daily:
            case Resolution.Hour:
                var last = GetLastAvailableHourlyData(zipFileName);
                if(last == DateTime.MinValue) return null;
                return last;
            case Resolution.Minute:
            case Resolution.Second:
            case Resolution.Tick:
                if (!Directory.Exists(dir)) return null;
                var files = Directory.EnumerateFiles(dir, "*.zip").OrderDescending();
                if (files == null) return null;
                string newestFile = files.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(newestFile))
                {
                    DateTime newest = DateTime.ParseExact(Path.GetFileName(newestFile).Split("_").First(), DateFormat.EightCharacter, CultureInfo.InvariantCulture);
                    return newest;
                }
                break;
        }
        return null;
    }


    private static DateTime GetLastAvailableHourlyData(string zipPath)
    {
        if (!File.Exists(zipPath))
        {
            return DateTime.MinValue;
        }

        DateTime lastDateTime = DateTime.MinValue;

        try
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in zip.Entries)
                {
                    using (var reader = new StreamReader(entry.Open()))
                    {
                        string line;
                        DateTime? lastEntryTime = null;

                        // Read all lines to find the last timestamp
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;

                            var parts = line.Split(',');
                            if (parts.Length < 2) continue;

                            // Parse the timestamp from the first column
                            if (DateTime.TryParseExact(parts[0], "yyyyMMdd HH:mm",
                                        CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime timestamp))
                            {
                                lastEntryTime = timestamp;
                            }
                        }

                        if (lastEntryTime.HasValue && lastEntryTime.Value > lastDateTime)
                        {
                            lastDateTime = lastEntryTime.Value;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing zip file {zipPath}: {ex.Message}");
        }

        return lastDateTime;
    }


}
