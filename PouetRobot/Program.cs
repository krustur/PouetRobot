using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Serilog;
using Serilog.Core;


namespace PouetRobot
{
    class Program
    {
        private static Logger _logger;

        static void Main(string[] args)
        {
            var productionsPath = @"D:\Temp\PouetDownload\";
            var webCachePath = @"D:\Temp\PouetDownload\WebCache\";
            var productionsFileName = $@"Productions.json";
            //var startPageUrl = "http://www.pouet.net/prodlist.php?platform[0]=Amiga+AGA&platform[1]=Amiga+OCS/ECS&page=685";
            var startPageUrl = "http://www.pouet.net/prodlist.php?platform[]=Amiga+AGA&platform[]=Amiga+OCS/ECS&platform[]=Amiga+PPC/RTG";

            _logger = new LoggerConfiguration()
                .WriteTo.ColoredConsole()
                .WriteTo.RollingFile("PouetRobot{date}.log")
                .CreateLogger();

            _logger.Information("Begin work!");

            var robot = new Robot(startPageUrl, productionsPath, productionsFileName, webCachePath, _logger);
            robot.LoadProductions(IndexScanMode.Rescan);
            robot.DownloadMetadata(MetadataScanMode.Rescan);
            robot.DownloadProductions();

            _logger.Information("Work is done! Press enter!");
            Console.ReadLine();

        }
    }

    public class Robot : IDisposable
    {
        private readonly string _startPageUrl;
        private readonly string _productionsPath;
        private readonly string _productionsFileName;
        private readonly string _webCachePath;
        private readonly Logger _logger;
        private readonly HttpClient _httpClient;
        private readonly List<IHtmlProber> _htmlProbers;

        public Robot(string startPageUrl, string productionsPath, string productionsFileName,
            string webCachePath, Logger logger)
        {
            _startPageUrl = startPageUrl;
            _productionsPath = productionsPath;
            _productionsFileName = productionsFileName;
            _webCachePath = webCachePath;
            _logger = logger;
            _httpClient = new HttpClient();
            _htmlProbers = new List<IHtmlProber>
            {
                // FilesSceneOrg
                new HtmlLinkProber("//li[contains(@id, 'mainDownload')]/a"),
                // DemoZoo
                new HtmlLinkProber("//div[contains(@class, 'primary')]/a"),
                new DropboxUrlProber(),
            };
        }

        public IDictionary<int, Production> Productions { get; set; }


        public void LoadProductions(IndexScanMode indexScanMode)
        {
            _logger.Information("Fetching production index!");

            switch (indexScanMode)
            {
                case IndexScanMode.Rescan:
                    Productions = LoadProductions();
                    
                    RescanIndex(Productions);
                    SaveProductions();
                    break;
                case IndexScanMode.NoRescan:
                    Productions = LoadProductions();
                    if (Productions == null)
                    {
                        Productions = DownloadIndex();
                    }
                    SaveProductions();
                    break;
                case IndexScanMode.DisableScan:
                    Productions = LoadProductions();
                    break;
            }           
        }

        private void RescanIndex(IDictionary<int, Production> productions)
        {
            var newProductions = DownloadIndex();

            // TODO: Warn when productions exists in productions, but not in newProductions

            foreach (var newProduction in newProductions)
            {
                if (productions.ContainsKey(newProduction.Key))
                {
                    var oldProduction = productions[newProduction.Key];
                    oldProduction.PouetUrl = newProduction.Value.PouetUrl;                  
                }
                else
                {
                    _logger.Information("Rescan index: Added new production [{Production}]", newProduction.Value);
                    productions.Add(newProduction.Key, newProduction.Value);
                }
            }
        }

        private Dictionary<int, Production> DownloadIndex()
        {
            var nextUrl = _startPageUrl;
            var productions = new Dictionary<int, Production>();
            while (nextUrl != null
                   //&& nextUrl.EndsWith("6") == false
                )
            {
                nextUrl = DownloadIndexPage(productions, nextUrl);
            }

            return productions;
        }

        private string DownloadIndexPage(Dictionary<int, Production> productions, string pageUrl)
        {
            var doc = GetHtmlDocument(pageUrl);

            var nextPageUrl = doc.DocumentNode
                    .SelectNodes("//div[contains(@class, 'nextpage')]/a")
                    ?.FirstOrDefault()
                    ?.Attributes["href"]
                    ?.Value
                ;
            nextPageUrl = DecodeUrlString(nextPageUrl);

            var prodsRows = doc.DocumentNode
                    .SelectNodes(@"//*[@id=""pouetbox_prodlist""]/tr")
                ;

            var rowCount = 0;
            foreach (var prodsRow in prodsRows)
            {
                rowCount++;
                if (rowCount == 1 || rowCount == prodsRows.Count)
                {
                    continue;
                }

                var columns = prodsRow.SelectNodes("td");
                // Title/Type/Platform
                var titleColumn = columns[0];
                var titleColumnParts = titleColumn.SelectNodes("span");
                var prodTitle = titleColumnParts[2].InnerText;
                var prodPageUrl = titleColumnParts[2].SelectNodes("a").First().Attributes["href"].Value;
                prodPageUrl = CombinePath(pageUrl, prodPageUrl);
                var prodPageUrlParts = prodPageUrl.Split('=');
                var prodPageUrlProdId = Convert.ToInt32(prodPageUrlParts[prodPageUrlParts.Length - 1]);

                var production = new Production
                {
                    Title = prodTitle,
                    PouetUrl = prodPageUrl,                   
                };
                if (productions.ContainsKey(prodPageUrlProdId))
                {
                    _logger.Warning("Duplicate production found, will use latest found [{PouetId}] - [{Production}]", prodPageUrlProdId, production);
                    productions.Remove(prodPageUrlProdId);
                }
                else
                {
                    _logger.Information("Indexed production [{PouetId}] - [{Production}]", prodPageUrlProdId, production);
                }

                productions.Add(prodPageUrlProdId, production);
            }

            //Console.ReadLine();
            return nextPageUrl == null ? null : CombinePath(pageUrl, nextPageUrl);
        }

        private IDictionary<int, Production> LoadProductions()
        {
            var productionsFileName = GetProductionsFileName();
            if (File.Exists(productionsFileName))
            {
                var productions = JsonConvert.DeserializeObject<IDictionary<int, Production>>(File.ReadAllText((productionsFileName)));
                return productions;
            }

            return null;
        }

        private void SaveProductions()
        {
            _logger.Information("Saving Productions json file!");

            var productionsFileName = GetProductionsFileName();
            var productionsJson = JsonConvert.SerializeObject(Productions, Formatting.Indented, new StringEnumConverter());
            File.WriteAllText(productionsFileName, productionsJson);
        }

        private string GetProductionsFileName()
        {
            return Path.Combine(_productionsPath, _productionsFileName);
        }

        public void DownloadMetadata(MetadataScanMode rescan)
        {
            _logger.Information("Downloading meta data!");

            var saveCounter = 0;
            var progress = 0;
            var maxProgress = Productions.Count;

            foreach (var productionPair in Productions)
            {
                var production = productionPair.Value;

                progress++;
                if (production.Metadata.Status == MetadataStatus.Ok && rescan == MetadataScanMode.NoRescan)
                {
                    _logger.Information("[{Progress}/{MaxProgress}] Already have metadata for [{Title}]", progress, maxProgress);
                }
                else if (DoICare(production))
                {
                    DownloadMetadataPage(productionPair.Key, production);
                    _logger.Information(
                        "[{progress}/{maxProgress}] Download metadata {DownloadMetadataStatus} [{Title} ({DownloadUrl})]",
                        progress, maxProgress, production.Title, production.Metadata.Status, production.Metadata.DownloadUrl);
                    if (saveCounter++ >= 25)
                    {
                        saveCounter = 0;
                        SaveProductions();
                    }
                }
            }

            SaveProductions();

        }

        private void DownloadMetadataPage(int pouetId, Production production)
        {
            var doc = GetHtmlDocument(pouetId, production.PouetUrl);

            //public string DownloadUrl { get; set; }
            var mainDownloadUrl = doc.DocumentNode
                    .SelectNodes("//a[contains(@id, 'mainDownloadLink')]")
                    ?.FirstOrDefault()
                    ?.Attributes["href"]
                    ?.Value
                ;

            //public IList<string> Types { get; set; }
            var types = doc.DocumentNode.SelectNodes("//table[contains(@id, 'stattable')]//a[starts-with(@href, 'prodlist.php?type')]//span");
            production.Metadata.Types.Clear();
            if (types != null)
            {
                foreach (var htmlNode in types)
                {
                    production.Metadata.Types.Add(htmlNode.InnerText);
                }
            }
            //public IList<string> Platforms { get; set; }
            var platforms = doc.DocumentNode.SelectNodes("//table[contains(@id, 'stattable')]//a[starts-with(@href, 'prodlist.php?platform')]//span");
            production.Metadata.Platforms.Clear();
            if (platforms != null)
            {
                foreach (var htmlNode in platforms)
                {
                    production.Metadata.Platforms.Add(htmlNode.InnerText);
                }
            }

            //public IList<string> Groups { get; set; }
            var groups = doc.DocumentNode
                    .SelectNodes("//span[contains(@id, 'title')]//a[starts-with(@href, 'groups.php')]");
            production.Metadata.Groups.Clear();
            if (groups != null)
            {
                foreach (var htmlNode in groups)
                {
                    production.Metadata.Groups.Add(htmlNode.InnerText);
                }
            }

            //public string Party { get; set; }
            //public string PartyYear { get; set; }
            var party = doc.DocumentNode
                .SelectSingleNode("//table[contains(@id, 'stattable')]//a[starts-with(@href, 'party.php')]");
            if (party != null)
            {
                production.Metadata.Party = party.InnerText;
                //var year = party.ParentNode.ChildNodes[1].InnerText;
                var year = party.NextSibling.InnerText;
                production.Metadata.PartyYear = year.Trim();
            }
            //public string PartyCompo { get; set; }
            var compo = doc.DocumentNode.SelectSingleNode("//table[contains(@id, 'stattable')]//td[starts-with(text(), 'compo')]");
            if (compo != null)
            {
                production.Metadata.PartyCompo = GetStringOrNaNull(compo.NextSibling.NextSibling.InnerText);
            }

            //public string PartyRank { get; set; }
            var ranked = doc.DocumentNode.SelectSingleNode("//table[contains(@id, 'stattable')]//td[starts-with(text(), 'ranked')]");
            if (ranked != null)
            {
                production.Metadata.PartyRank = GetStringOrNaNull(ranked.NextSibling.NextSibling.InnerText);
            }


            //public string ReleaseDate { get; set; }
            var releaseDate = doc.DocumentNode.SelectSingleNode("//table[contains(@id, 'stattable')]//td[starts-with(text(), 'release date')]");
            if (releaseDate != null)
            {
                production.Metadata.ReleaseDate = releaseDate.NextSibling.NextSibling.InnerText;
            }

            //public int Rulez { get; set; }
            var rulez = doc.DocumentNode.SelectSingleNode("//img[contains(@alt, 'rulez')]");
            if (rulez != null)
            {
                production.Metadata.Rulez = int.Parse(rulez.NextSibling.InnerText.TrimNbsp());
            }

            //public int IsOk { get; set; }
            var isOk = doc.DocumentNode.SelectSingleNode("//img[contains(@alt, 'is ok')]");
            if (isOk != null)
            {
                production.Metadata.IsOk = int.Parse(isOk.NextSibling.InnerText.TrimNbsp());
            }

            //public int Sucks { get; set; }
            var sucks = doc.DocumentNode.SelectSingleNode("//img[contains(@alt, 'sucks')]");
            if (sucks != null)
            {
                production.Metadata.Sucks = int.Parse(sucks.NextSibling.InnerText.TrimNbsp());
            }

            //public decimal Average { get; set; }
            var totalVotes = (production.Metadata.Rulez + production.Metadata.IsOk + production.Metadata.Sucks);
            production.Metadata.Average = totalVotes == 0 ? 0 : ((decimal)(production.Metadata.Rulez - production.Metadata.Sucks)) / totalVotes;

            //public int CoupDeCours { get; set; }
            var coupDeCour = doc.DocumentNode.SelectSingleNode("//img[contains(@alt, 'cdcs')]");
            if (coupDeCour != null)
            {
                production.Metadata.CoupDeCours = int.Parse(coupDeCour.NextSibling.InnerText.TrimNbsp());
            }

            //public int AllTimeRank { get; set; }
            var allTimeRank = doc.DocumentNode.SelectSingleNode("//div[contains(@id, 'alltimerank')]");
            if (allTimeRank != null)
            {
                var allTimeRankString = allTimeRank.InnerText.Split('#').Last();
                production.Metadata.AllTimeRank = int.Parse(allTimeRankString);
            }

            //public MetadataStatus Status { get; set; }
            if (mainDownloadUrl != null)
            {
                production.Metadata.DownloadUrl = mainDownloadUrl;
                production.Metadata.Status = MetadataStatus.Ok;
            }
            else
            {
                production.Metadata.Status = MetadataStatus.Error;
            }
        }

        private string GetStringOrNaNull(string str)
        {
            if (str == null || str.ToLower() == "n/a")
            {
                return null;
            }

            return str;
        }

        private bool DoICare(Production production)
        {
            //if (production.Type.ToLower().Contains("game"))
            //{
            //    return false;
            //}

            return true;
        }

        public void DownloadProductions()
        {
            _logger.Information("Downloading productions!");

            var saveCounter = 0;
            var progress = 0;
            var maxProgress = Productions.Count;

            foreach (var productionPair in Productions)
            {
                var production = productionPair.Value;

                progress++;

                if (production.Download.Status == DownloadStatus.Ok)
                {
                    _logger.Information("[{Progress}/{MaxProgress}] Already downloaded [{Production} ({DownloadUrl})]",
                        progress, maxProgress, production, production.Metadata.DownloadUrl);
                }
                else if (production.Download.Status == DownloadStatus.Error)
                {
                    _logger.Information("[{Progress}/{MaxProgress}] Skipped due to previous Error [{Production} ({DownloadUrl})]",
                        progress, maxProgress, production, production.Metadata.DownloadUrl);
                }
                else if (DoICare(production))
                {
                    DownloadProduction(productionPair.Key, production);
                    _logger.Information("[{progress}/{maxProgress}] Downloaded production [{Production} ({DownloadProductionStatus})]",
                        progress, maxProgress, production, production.Download.Status); 
                    if (saveCounter++ >= 25)
                    {
                        saveCounter = 0;
                        SaveProductions();
                    }
                }

            }

            SaveProductions();


        }

        private void DownloadProduction(int productionId, Production production, string url = null)
        {
            try
            {
                url = url ?? production.Metadata.DownloadUrl;
                var response = GetUrl(productionId, url);

                switch (response.HttpResponseMessage.StatusCode)
                {
                    case HttpStatusCode.Found:
                        DownloadProduction(productionId, production, response.HttpResponseMessage.Headers.Location.AbsoluteUri);
                        return;
                    case HttpStatusCode.OK:
                        var fileTypeByContent = GetFileTypeByContent(response.Content);
                        var fileTypeByName = GetFileTypeByFileName(response.FileName);
                        var fileTypeByContentLength = GetFileTypeByContentLength(response.Content.Length);
                        var fileType = fileTypeByContent != FileType.Unknown ? fileTypeByContent : fileTypeByName != FileType.Unknown ? fileTypeByName : fileTypeByContentLength;
                        var fileIdentifiedByType = fileTypeByContent != FileType.Unknown ? FileIdentifiedByType.Content :
                            fileTypeByName != FileType.Unknown ? FileIdentifiedByType.FileName : FileIdentifiedByType.ContentLength;
                        var fileIdentifiedByType2 = fileTypeByContent != FileType.Unknown ? FileIdentifiedByType.Content : FileIdentifiedByType.FileName;
                        HandleProductionContent(productionId, production, fileType, fileIdentifiedByType, response.FileName, response.CacheFileName, response.Content, url);
                        return;
                    default:
                        return;
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Error when downloading production [{Production}] [{url}]", production.Title, url);
                production.Download.Status = DownloadStatus.Error;
            }
        }

        private void HandleProductionContent(int productionId, Production production, FileType fileType, FileIdentifiedByType fileIdentifiedByType, string fileName, string cacheFileName, byte[] responseContent, string url)
        {

            switch (fileType)
            {
                case FileType.Lha:
                case FileType.Zip:
                case FileType.Zip7:
                case FileType.Rar:
                case FileType.Lzx:
                case FileType.Adf:
                case FileType.Dms:
                case FileType.AmigaExe:
                    production.Download.FileType = fileType;
                    production.Download.FileIdentifiedByType = fileIdentifiedByType;
                    production.Download.FileName = fileName;
                    production.Download.CacheFileName = cacheFileName;
                    production.Download.Status = DownloadStatus.Ok;
                    return;
                case FileType.Html:
                    HandleHtml(productionId, production, fileType, fileIdentifiedByType, fileName, cacheFileName, responseContent, url);
                    return;
                default:
                    _logger.Warning("Unable to identify file type for [{Production}]", production.Title);
                    return;
            }
        }

        private void HandleHtml(int productionId, Production production, FileType fileType, FileIdentifiedByType fileIdentifiedByType, string fileName, string cacheFileName, byte[] responseContent, string url)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(ByteArrayToString(responseContent));

            foreach (var htmlProber in _htmlProbers)
            {
                var probeUrl = htmlProber.GetProbeUrl(url, doc);
                if (probeUrl != null)
                {
                    DownloadProduction(productionId, production, probeUrl);
                    return;
                }
            }

            _logger.Warning("Unable to probe html for [{Production}] [{url}]", production.Title, url);
        }

        private readonly string[] _lhaMethodIds =
        {
            "-lh0-", "-lh1-", "-lh2-", "-lh3-", "-lh4-", "-lh5-", "-lh6-", "-lh7-", "-lh8-",
            "-lhd-",
            "-lzs-", "-lz4-",
        };


        private FileType GetFileTypeByContent(byte[] content)
        {
            //var bytes = Encoding.ASCII.GetBytes(content);

            var lhaMethodId = GetIdString(content, 2, 5);
            if (_lhaMethodIds.Contains(lhaMethodId))
            {
                return FileType.Lha;
            }

            if (content[0] == 'P' && content[1] == 'K' && content[2] == 0x03 && content[3] == 0x04)
            {
                return FileType.Zip;
            }

            if (content[0] == 0x00 && content[1] == 0x00 && content[2] == 0x03 && content[3] == 0xf3)
            {
                return FileType.AmigaExe;
            }

            if (content[0] == '7' && content[1] == 'z')
            {
                return FileType.Zip7;
            }

            if (content[0] == 'D' && content[1] == 'M' && content[2] == 'S' && content[3] == '!')
            {
                return FileType.Zip;
            }

            if (content[0] == 'R' && content[1] == 'a' && content[2] == 'r' && content[3] == '!')
            {
                return FileType.Rar;
            }

            if (content[0] == 'L' && content[1] == 'Z' && content[2] == 'X')
            {
                return FileType.Lzx;
            }
            //else if (content.Length == 880 * 1024)
            //{
            //    return FileType.Adf;
            //}

            if (IsHtml(content))
            {
                return FileType.Html;
            }

            return FileType.Unknown;
        }

        private FileType GetFileTypeByFileName(string fileName)
        {
            var file = new FileInfo(fileName);
            switch (file.Extension.ToLower())
            {
                case ".lha":
                    return FileType.Lha;
                case ".zip":
                    return FileType.Zip;
                case ".adf":
                    return FileType.Adf;
                case ".htm":
                case ".html":
                    return FileType.Html;
            }

            return FileType.Unknown;
        }

        private FileType GetFileTypeByContentLength(int contentLength)
        {
            if (contentLength == 80 * 2 * 11 * 512)
            {
                return FileType.Adf;
            }

            return FileType.Unknown;
        }

        private bool IsHtml(byte[] content)
        {
            var contentString = ByteArrayToString(content);
            var tagRegex = new Regex(@"<\s*([^ >]+)[^>]*>.*?<\s*/\s*\1\s*>");
            var match = tagRegex.Match(contentString.Substring(0, Math.Min(contentString.Length, 3000)));
            return match.Success;
        }

        private string GetIdString(byte[] bytes, int offset, int length)
        {
            var actualOffset = Math.Min(bytes.Length, offset);
            return System.Text.Encoding.UTF8.GetString(bytes, actualOffset, length);
        }        

        private string CombinePath(string fullBaseUrl, string nextPageUrl)
        {
            var baseUri = new Uri(fullBaseUrl);
            var newPath = new Uri(new Uri($"{baseUri.Scheme}://{baseUri.Host}"), nextPageUrl);
            return newPath.ToString();
        }

        private HtmlDocument GetHtmlDocument(string pageUrl)
        {
            var response = GetUrl(-1, pageUrl);
            var doc = new HtmlDocument();
            doc.LoadHtml(ByteArrayToString(response.Content));

            return doc;
        }

        private HtmlDocument GetHtmlDocument(int pouetId, string pageUrl)
        {
            var response = GetUrl(pouetId, pageUrl);
            var doc = new HtmlDocument();
            doc.LoadHtml(ByteArrayToString(response.Content));

            return doc;
        }

        private (HttpResponseMessage HttpResponseMessage, string FileName, String CacheFileName, Byte[] Content) GetUrl(int prefixId, string pageUrl)
        {
            var hash = Sha256Hash($"{prefixId}_{pageUrl}");
            var cacheFileName = prefixId == -1 ? $"{hash}.dat" : $"{prefixId}_{hash}.dat";
            var cacheFileFullPath = prefixId == -1
                ?
                //$"{_webCachePath}Global\\{cacheFileName}"
                Path.Combine(_webCachePath, "Global", cacheFileName)
                : Path.Combine(_webCachePath, "Production", cacheFileName);
            var pageUri = new Uri(pageUrl);
            var fileName = pageUri.Segments.Last();

            if (File.Exists(cacheFileFullPath))
            {
                _logger.Information("Using cached file url [{CacheFileName}] {PageUrl}: ", cacheFileName, pageUrl);
                return (new HttpResponseMessage(HttpStatusCode.OK), fileName, cacheFileName, File.ReadAllBytes(cacheFileFullPath));
            }

            _logger.Information("GET url {PageUrl}: ", pageUrl);
            if (pageUri.Scheme == Uri.UriSchemeFtp)
            {

                var request = new WebClient();

                var content = request.DownloadData(pageUri.ToString());
                //var content = System.Text.Encoding.UTF8.GetString(newFileData);
                File.WriteAllBytes(cacheFileFullPath, content);
                return (new HttpResponseMessage(HttpStatusCode.OK), fileName, cacheFileName, content);
            }
            else
            {


                System.Net.ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12 |
                    SecurityProtocolType.Tls11 |
                    SecurityProtocolType.Tls; // comparable to modern browsers


                // TODO: Handle time outs
                var responseMessage = _httpClient.GetAsync(pageUrl).GetAwaiter().GetResult();
                var content = responseMessage.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult(); //.ReadAsStringAsync();

                if (responseMessage.StatusCode == HttpStatusCode.OK)
                {
                    File.WriteAllBytes(cacheFileFullPath, content);
                }

                return (responseMessage, fileName, cacheFileName, content);
            }
        }

        public void Dispose()
        {
            _logger?.Dispose();
            _httpClient?.Dispose();
        }

        public static string Sha256Hash(string value)
        {
            var sb = new StringBuilder();

            using (var hash = SHA256.Create())
            {
                var result = hash.ComputeHash(Encoding.UTF8.GetBytes(value));

                foreach (var b in result)
                    sb.Append(b.ToString("x2"));
            }

            return sb.ToString();
        }

        private static string DecodeUrlString(string url)
        {
            if (url == null)
            {
                return null;
            }

            string newUrl;
            while ((newUrl = Uri.UnescapeDataString(url)) != url)
                url = newUrl.Replace("&amp;", "&");
            return newUrl;
        }

        private string ByteArrayToString(byte[] byteArray)
        {
            return System.Text.Encoding.UTF8.GetString(byteArray);
        }
    }

    public interface IHtmlProber
    {
        string GetProbeUrl(string url, HtmlDocument doc);

    }

    public class HtmlLinkProber : IHtmlProber
    {
        private readonly string _linkXPath;

        public HtmlLinkProber(string linkXPath)
        {
            _linkXPath = linkXPath;
        }

        public string GetProbeUrl(string url, HtmlDocument doc)
        {
            var probeUrl = doc.DocumentNode
                .SelectNodes(_linkXPath)
                ?.FirstOrDefault()
                ?.Attributes["href"]
                ?.Value;
            return probeUrl;
        }
    }

    public class DropboxUrlProber : IHtmlProber
    {
        public string GetProbeUrl(string url, HtmlDocument doc)
        {

            if (url.StartsWith("http://www.dropbox.com") || url.StartsWith("https://www.dropbox.com"))
            {
                if (url.EndsWith("dl=0"))
                {
                    return url.Substring(0, url.Length - 1) + "1";
                }

                return url + "?dl=1";
            }
            return null;
        }
    }

    public class Production
    {
        public Production()
        {
            Metadata = new ProductionMetadata();
            Download = new ProductionDownload();
        }

        // Pass 1 (list)
        public string Title { get; set; }
        public string PouetUrl { get; set; }
        public ProductionMetadata Metadata { get; }
        public ProductionDownload Download { get; }

        public override string ToString()
        {
            return $"{Metadata.Groups.ToSingleString()} / {Title} [{Metadata.Types.ToSingleString()} - {Metadata.Platforms.ToSingleString()}]";
        }
    }

    public class ProductionMetadata
    {
        public ProductionMetadata()
        {
            Types = new List<string>();
            Platforms = new List<string>();
            Groups = new List<string>();
        }
        // Pass 2 (metadata)
        public MetadataStatus Status { get; set; }

        public string DownloadUrl { get; set; }

        public IList<string> Types { get; set; }
        public IList<string> Platforms { get; set; }
        public IList<string> Groups { get; set; }

        public string Party { get; set; }
        public string PartyYear { get; set; }
        public string PartyCompo { get; set; }
        public string PartyRank { get; set; }

        public string ReleaseDate { get; set; }
        public int Rulez { get; set; }
        public int IsOk { get; set; }
        public int Sucks { get; set; }
        public decimal Average { get; set; }
        public int CoupDeCours { get; set; }
        public int AllTimeRank { get; set; }
    }

    public class ProductionDownload
    {
        // Pass 3 (download)
        public DownloadStatus Status { get; set; }
        public FileType FileType { get; set; }
        public FileIdentifiedByType FileIdentifiedByType { get; set; }
        public string FileName { get; set; }
        public string CacheFileName { get; set; }
    }


    public static class StringListExtensions
    {
        public static string ToSingleString(this IList<string> strings)
        {
            if (strings.Count == 0)
            {
                return string.Empty;
            }

            var result = strings.Aggregate((i, j) => i + "," + j);
            return result;
        }
    }
    public enum MetadataStatus
    {
        Unknown = 0,
        Ok,
        Error
    }

    public enum DownloadStatus
    {
        Unknown = 0,
        Ok,

        Error
    }

    public enum FileType
    {
        Unknown = 0,
        Lha,
        Zip,
        Zip7,
        AmigaExe,
        Adf,
        Html,
        Dms,
        Rar,
        Lzx
    }

    public enum FileIdentifiedByType
    {
        Unknown = 0,
        Content,
        FileName,
        ContentLength
    }

    public static class EnumerableExtensions
    {
        public static IEnumerable<TSource> DistinctBy<TSource, TKey>
            (this IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
        {
            HashSet<TKey> seenKeys = new HashSet<TKey>();
            foreach (TSource element in source)
            {
                if (seenKeys.Add(keySelector(element)))
                {
                    yield return element;
                }
            }
        }
    }

    public static class StringExtensions
    {
        public static string TrimNbsp(this string source)
        {
            return source.Replace("&nbsp;", "").Trim();
        }
    }

    public enum IndexScanMode
    {
        Unknown = 0,
        Rescan,
        NoRescan,
        DisableScan
    }

    public enum MetadataScanMode
    {
        Unknown = 0,
        Rescan,
        NoRescan
    }
    

}