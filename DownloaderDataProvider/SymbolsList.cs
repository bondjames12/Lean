
namespace QuantConnect.DownloaderDataProvider.Launcher

{
    //SymbolsList myDeserializedClass = JsonConvert.DeserializeObject<SymbolsList>(myJsonResponse);
    public class SymbolsListRaw
    {
        public List<SymbolJSON> Symbols { get; set; }
    }

    public class SymbolJSON
    {
        public string Symbol { get; set; }
        public string Type { get; set; }
        public string Action { get; set; }
    }

    public class SymbolsList
    {
        public List<SymbolObj> Symbols { get; set; }
    }

    public class SymbolObj
    {
        public string Symbol { get; set; }
        public SecurityType Type { get; set; }
        public string Action { get; set; }
    }
}

