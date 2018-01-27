using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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

        //public static Task Main(string[] args)
        static void Main(string[] args)
        {
            var zip7Path = @"C:\Program files\7-Zip\7z.exe";
            var productionsFileName = $@"Productions.json";

            var productionsPath = @"D:\Temp\PouetDownload\";
            var webCachePath = @"D:\Temp\PouetDownload\WebCache\";
            var startPageUrl = "http://www.pouet.net/prodlist.php?platform[]=Amiga+AGA&platform[]=Amiga+OCS/ECS&platform[]=Amiga+PPC/RTG";
            //var productionsPath = @"D:\Temp\PouetDownload_PC\";
            //var webCachePath = @"D:\Temp\PouetDownload_PC\WebCache\";
            //var startPageUrl = "http://www.pouet.net/prodlist.php?platform[]=Windows&page=1";

            _logger = new LoggerConfiguration()
                .WriteTo.ColoredConsole()
                .WriteTo.RollingFile("PouetRobot{date}.log")
                .CreateLogger();

            _logger.Information("Begin work!");

            var robot = new Robot(startPageUrl, productionsPath, productionsFileName, webCachePath, zip7Path, _logger, 256);
            robot.LoadProductions(IndexScanMode.NoRescan);
            robot.DownloadMetadata(MetadataScanMode.NoRescan);
            robot.DownloadProductions(DownloadProductionsMode.NoRescan);
            robot.WriteOutput();


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
        private readonly string _zip7Path;
        private readonly Logger _logger;
        private readonly HttpClient _httpClient;
        private readonly List<IHtmlProber> _htmlProbers;
        private readonly int _saveProductionsHowOften;

        private int _writeOutputCounter;

        public Robot(string startPageUrl, string productionsPath, string productionsFileName,
            string webCachePath, string zip7Path, Logger logger, int saveProductionsHowOften)
        {
            _startPageUrl = startPageUrl;
            _productionsPath = productionsPath;
            _productionsFileName = productionsFileName;
            _webCachePath = webCachePath;
            _zip7Path = zip7Path;
            _logger = logger;
            _saveProductionsHowOften = saveProductionsHowOften;
            _httpClient = new HttpClient();
            _htmlProbers = new List<IHtmlProber>
            {
                // FilesSceneOrg
                new HtmlLinkProber("//li[contains(@id, 'mainDownload')]/a"),
                // DemoZoo
                new HtmlLinkProber("//div[contains(@class, 'primary')]/a"),
                new DropboxUrlProber(),
                new TinyCcProber(),
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
                    if (Productions == null)
                    {
                        Productions = DownloadIndex();
                    }
                    else
                    {
                        RescanIndex(Productions);
                    }
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
                    oldProduction.PouetId = newProduction.Value.PouetId;
                    oldProduction.Title = newProduction.Value.Title;                  
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
                    .SelectNodes("//div[@class='nextpage']/a")
                    ?.FirstOrDefault()
                    ?.Attributes["href"]
                    ?.Value
                ;
            nextPageUrl = HtmlDecode(nextPageUrl);

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
                var prodTitle = HtmlDecode(titleColumnParts[2].InnerText);
                var prodPageUrl = titleColumnParts[2].SelectNodes("a").First().Attributes["href"].Value;
                prodPageUrl = CombinePath(pageUrl, prodPageUrl);
                var prodPageUrlParts = prodPageUrl.Split('=');
                var prodPageUrlProdId = Convert.ToInt32(prodPageUrlParts[prodPageUrlParts.Length - 1]);

                var production = new Production
                {
                    Title = prodTitle,
                    PouetId = prodPageUrlProdId,
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
            if (System.IO.File.Exists(productionsFileName))
            {
                var productions = JsonConvert.DeserializeObject<IDictionary<int, Production>>(System.IO.File.ReadAllText((productionsFileName)));
                return productions;
            }

            return null;
        }

        private void SaveProductions()
        {
            _logger.Information("Saving Productions json file!");

            var productionsFileName = GetProductionsFileName();
            var productionsJson = JsonConvert.SerializeObject(Productions, Formatting.Indented, new StringEnumConverter());
            System.IO.File.WriteAllText(productionsFileName, productionsJson);
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
                    //_logger.Information("[{Progress}/{MaxProgress}] Already have metadata for [{Production}]", progress, maxProgress, production);
                }
                else if (DoICare(production))
                {
                    DownloadMetadataPage(productionPair.Key, production);
                    _logger.Information(
                        "[{progress}/{maxProgress}] Download metadata {DownloadMetadataStatus} [{Title} ({DownloadUrl})]",
                        progress, maxProgress, production.Title, production.Metadata.Status, production.Metadata.DownloadUrl);
                    if (saveCounter++ >= _saveProductionsHowOften)
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
            var mainDownloadUrl = HtmlDecode(doc.DocumentNode
                    .SelectNodes("//a[@id='mainDownloadLink']")
                    ?.FirstOrDefault()
                    ?.Attributes["href"]
                    ?.Value
                    ?.Trim());

            //public IList<string> Types { get; set; }
            var types = doc.DocumentNode.SelectNodes("//table[@id='stattable']//a[starts-with(@href, 'prodlist.php?type')]//span");
            production.Metadata.Types.Clear();
            if (types != null)
            {
                foreach (var htmlNode in types)
                {
                    production.Metadata.Types.Add(HtmlDecode(htmlNode.InnerText));
                }
            }
            //public IList<string> Platforms { get; set; }
            var platforms = doc.DocumentNode.SelectNodes("//table[@id='stattable']//a[starts-with(@href, 'prodlist.php?platform')]//span");
            production.Metadata.Platforms.Clear();
            if (platforms != null)
            {
                foreach (var htmlNode in platforms)
                {
                    production.Metadata.Platforms.Add(HtmlDecode(htmlNode.InnerText));
                }
            }

            //public IList<string> Groups { get; set; }
            var groups = doc.DocumentNode
                    .SelectNodes("//span[@id='title']//a[starts-with(@href, 'groups.php')]");
            production.Metadata.Groups.Clear();
            if (groups != null)
            {
                foreach (var htmlNode in groups)
                {
                    production.Metadata.Groups.Add(HtmlDecode(htmlNode.InnerText));
                }
            }

            //public string Party { get; set; }
            //public string PartyYear { get; set; }
            var party = doc.DocumentNode
                .SelectSingleNode("//table[@id='stattable']//a[starts-with(@href, 'party.php')]");
            if (party != null)
            {
                production.Metadata.Party = HtmlDecode(party.InnerText);
                //var year = party.ParentNode.ChildNodes[1].InnerText;
                var year = party.NextSibling == null ? HtmlDecode(party.InnerText) : HtmlDecode(party.NextSibling.InnerText);
                production.Metadata.PartyYear = year.Trim();
            }
            //public string PartyCompo { get; set; }
            var compo = doc.DocumentNode.SelectSingleNode("//table[@id='stattable']//td[starts-with(text(), 'compo')]");
            if (compo != null)
            {
                production.Metadata.PartyCompo = GetStringOrNaNull(HtmlDecode(compo.NextSibling.NextSibling.InnerText));
            }

            //public string PartyRank { get; set; }
            var ranked = doc.DocumentNode.SelectSingleNode("//table[@id='stattable']//td[starts-with(text(), 'ranked')]");
            if (ranked != null)
            {
                production.Metadata.PartyRank = GetStringOrNaNull(HtmlDecode(ranked.NextSibling.NextSibling.InnerText));
            }


            //public string ReleaseDate { get; set; }
            var releaseDate = doc.DocumentNode.SelectSingleNode("//table[@id='stattable']//td[starts-with(text(), 'release date')]");
            if (releaseDate != null)
            {
                production.Metadata.ReleaseDate = HtmlDecode(releaseDate.NextSibling.NextSibling.InnerText);
            }

            //public int Rulez { get; set; }
            var rulez = doc.DocumentNode.SelectSingleNode("//img[@alt='rulez']");
            if (rulez != null)
            {
                production.Metadata.Rulez = int.Parse(HtmlDecode(rulez.NextSibling.InnerText).Trim());
            }

            //public int IsOk { get; set; }
            var isOk = doc.DocumentNode.SelectSingleNode("//img[@alt='is ok']");
            if (isOk != null)
            {
                production.Metadata.IsOk = int.Parse(HtmlDecode(isOk.NextSibling.InnerText).Trim());
            }

            //public int Sucks { get; set; }
            var sucks = doc.DocumentNode.SelectSingleNode("//img[@alt='sucks']");
            if (sucks != null)
            {
                production.Metadata.Sucks = int.Parse(HtmlDecode(sucks.NextSibling.InnerText).Trim());
            }

            //public decimal Average { get; set; }
            var totalVotes = (production.Metadata.Rulez + production.Metadata.IsOk + production.Metadata.Sucks);
            production.Metadata.Average = totalVotes == 0 ? 0 : ((decimal)(production.Metadata.Rulez - production.Metadata.Sucks)) / totalVotes;

            //public int CoupDeCours { get; set; }
            var coupDeCour = doc.DocumentNode.SelectSingleNode("//img[@alt='cdcs']");
            if (coupDeCour != null)
            {
                production.Metadata.CoupDeCours = int.Parse(HtmlDecode(coupDeCour.NextSibling.InnerText).Trim());
            }

            //public int AllTimeRank { get; set; }
            var allTimeRank = doc.DocumentNode.SelectSingleNode("//div[@id='alltimerank']");
            if (allTimeRank != null)
            {
                var allTimeRankString = HtmlDecode(allTimeRank.InnerText.Split('#').Last());
                var couldParse = int.TryParse(allTimeRankString, out var allTimeRankInt);
                production.Metadata.AllTimeRank = couldParse ? allTimeRankInt : (int?)null;
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

        public void DownloadProductions(DownloadProductionsMode downloadProductionsMode)
        {
            _logger.Information("Downloading productions!");

            var saveCounter = 0;
            var progress = 0;
            var maxProgress = Productions.Count;

            foreach (var productionPair in Productions)
            {
                var production = productionPair.Value;

                progress++;

                if (downloadProductionsMode == DownloadProductionsMode.NoRescan && production.Download.Status == DownloadStatus.Ok)
                {
                    //_logger.Information("[{Progress}/{MaxProgress}] Already downloaded [{Production} ({DownloadUrl})]",
                        //progress, maxProgress, production, production.Metadata.DownloadUrl);
                }
                if (downloadProductionsMode != DownloadProductionsMode.RescanRetryError && production.Download.Status == DownloadStatus.Error)
                {
                    _logger.Information("[{Progress}/{MaxProgress}] Skipped due to previous Error [{Production} ({DownloadUrl})]",
                        progress, maxProgress, production, production.Metadata.DownloadUrl);
                }
                if (((downloadProductionsMode == DownloadProductionsMode.Rescan && production.Download.Status != DownloadStatus.Error)
                     || (downloadProductionsMode == DownloadProductionsMode.RescanRetryError ))
                    && DoICare(production))
                {
                    DownloadProduction(productionPair.Key, production);
                    _logger.Information("[{progress}/{maxProgress}] Downloaded production [{Production} ({DownloadProductionStatus})]",
                        progress, maxProgress, production, production.Download.Status); 
                    if (saveCounter++ >= _saveProductionsHowOften)
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
                case FileType.Gz:
                case FileType.Adf:
                case FileType.Dms:
                case FileType.AmigaExe:
                case FileType.Mkv:
                case FileType.Avi:
                case FileType.Amos:
                case FileType.Gif:
                case FileType.Png:
                case FileType.Txt:
                case FileType.Mpeg:
                    production.Download.FileType = fileType;
                    production.Download.FileIdentifiedByType = fileIdentifiedByType;
                    production.Download.FileName = fileName;
                    production.Download.FileSize= responseContent.Length;
                    production.Download.CacheFileName = cacheFileName;
                    production.Download.Status = DownloadStatus.Ok;
                    return;
                case FileType.Html:
                    var handleHtmlSuccess = HandleHtml(productionId, production, fileType, fileIdentifiedByType, fileName, cacheFileName, responseContent, url);
                    if (handleHtmlSuccess == false)
                    {
                        production.Download.Status = DownloadStatus.UnknownHtml;
                        _logger.Warning("Unable to probe html for [{Production}] [{url}]", production.Title, url);
                    }
                    return;
                default:
                    production.Download.FileName = fileName;
                    production.Download.FileSize= responseContent.Length;
                    production.Download.CacheFileName = cacheFileName;
                    production.Download.Status = DownloadStatus.UnknownFileType;
                    _logger.Warning("Unable to identify file type for [{Production}]", production.Title);
                    return;
            }
        }

        private bool HandleHtml(int productionId, Production production, FileType fileType, FileIdentifiedByType fileIdentifiedByType, string fileName, string cacheFileName, byte[] responseContent, string url)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(ByteArrayToString(responseContent));

            foreach (var htmlProber in _htmlProbers)
            {
                var probeUrl = htmlProber.GetProbeUrl(url, doc);
                if (probeUrl != null)
                {
                    DownloadProduction(productionId, production, probeUrl);
                    return true;
                }
            }

            return false;
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
                return FileType.Dms;
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

            if (content[0] == 0x1f && content[1] == 0x8b)
            {
                return FileType.Gz;
            }

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
                case ".mkv":
                    return FileType.Mkv;
                case ".avi":
                    return FileType.Avi;
                case ".amos":
                    return FileType.Amos;
                case ".gif":
                    return FileType.Gif;
                case ".png":
                    return FileType.Png;
                case ".txt":
                    return FileType.Txt;
                case ".mpg":
                case ".mpeg":
                    return FileType.Mpeg;

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
            return Encoding.UTF8.GetString(bytes, actualOffset, length);
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

        private (HttpResponseMessage HttpResponseMessage, string FileName, string CacheFileName, byte[] Content) GetUrl(int prefixId, string pageUrl)
        {
            var hash = Sha256Hash($"{prefixId}_{pageUrl}");
            var cacheFileName = prefixId == -1 ? $"{hash}.dat" : $"{prefixId}_{hash}.dat";
            var cacheFileFullPath = prefixId == -1 ? GetGlobalCacheFilePath(cacheFileName) : GetProductionCacheFullPath(cacheFileName);
            var pageUri = new Uri(pageUrl);
            var fileName = pageUri.Segments.Last();

            if (System.IO.File.Exists(cacheFileFullPath))
            {
                _logger.Information("Using cached file url [{CacheFileName}] {PageUrl}: ", cacheFileName, pageUrl);
                return (new HttpResponseMessage(HttpStatusCode.OK), fileName, cacheFileName, System.IO.File.ReadAllBytes(cacheFileFullPath));
            }

            _logger.Information("GET url {PageUrl}: ", pageUrl);
            if (pageUri.Scheme == Uri.UriSchemeFtp)
            {

                var request = new WebClient();

                var content = request.DownloadData(pageUri.ToString());
                System.IO.File.WriteAllBytes(cacheFileFullPath, content);
                return (new HttpResponseMessage(HttpStatusCode.OK), fileName, cacheFileName, content);
            }
            else
            {
                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12 |
                    SecurityProtocolType.Tls11 |
                    SecurityProtocolType.Tls; // comparable to modern browsers
                
                var responseMessage = _httpClient.GetAsync(pageUrl).GetAwaiter().GetResult();
                var content = responseMessage.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                
                if (responseMessage.StatusCode == HttpStatusCode.OK)
                {
                    System.IO.File.WriteAllBytes(cacheFileFullPath, content);
                }

                return (responseMessage, fileName, cacheFileName, content);
            }
        }

        private string GetGlobalCacheFilePath(string cacheFileName)
        {
            return Path.Combine(_webCachePath, "Global", cacheFileName);
        }

        private string GetProductionCacheFullPath(string cacheFileName)
        {
            return Path.Combine(_webCachePath, "Production", cacheFileName);
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

        private static string HtmlDecode(string html)
        {
            return System.Web.HttpUtility.HtmlDecode(html);
        }

        //private static string UrlDecode(string url)
        //{
        //    if (url == null)
        //    {
        //        return null;
        //    }

        //    string newUrl;
        //    while ((newUrl = Uri.UnescapeDataString(url)) != url)
        //        url = newUrl.Replace("&amp;", "&");
        //    return newUrl;
        //}

        private string ByteArrayToString(byte[] byteArray)
        {
            return Encoding.UTF8.GetString(byteArray);
        }

        public IList<Folder> GetMasterFolderStructure()
        {
            var folderStructure = new List<Folder>();

            //folderLayout.Add(new File("en fil.exe"));

            var productions = Productions.Select(x => x.Value)
                    .ToList()
                    .FilterDownloadStatus(DownloadStatus.Ok)
                    .FilterMetadataStatus(MetadataStatus.Ok)
                ;

            var generalTypeExcludeFilter = new List<string>
            {
                "artpack",
                "bbstro",
                "cracktro",
                "demotool",
                "diskmag",
                "game",
                "invitation",
                "liveact",
                "musicdisk",
                "procedural graphics",
                "report",
                "slideshow",
                "votedisk",
                "wild",
            };
            var agaProductions = productions
                    .FilterExcludeTypes(generalTypeExcludeFilter)
                    .FilterExcludeFileTypes(new List<string>
                    {
                        FileType.Adf.ToString(),
                        FileType.Dms.ToString(),
                    })
                    .FilterPlatform("Amiga AGA");
            var agaProductionsAdf = productions
                    .FilterExcludeTypes(generalTypeExcludeFilter)
                    .FilterFileTypes(new List<string>
                    {
                        FileType.Adf.ToString(),
                        FileType.Dms.ToString(),
                    })
                    .FilterPlatform("Amiga AGA");
            var ocsEcsProductions = productions
                    .FilterExcludeTypes(generalTypeExcludeFilter)
                    .FilterExcludeFileTypes(new List<string>
                    {
                        FileType.Adf.ToString(),
                        FileType.Dms.ToString(),
                    })
                    .FilterPlatform("Amiga OCS/ECS");
            var ocsEcsProductionsAdf = productions
                    .FilterExcludeTypes(generalTypeExcludeFilter)
                    .FilterFileTypes(new List<string>
                    {
                        FileType.Adf.ToString(),
                        FileType.Dms.ToString(),
                    })
                    .FilterPlatform("Amiga OCS/ECS");
            var ppcRtgProductions = productions
                    .FilterExcludeTypes(generalTypeExcludeFilter)
                    .FilterExcludeFileTypes(new List<string>
                    {
                        FileType.Adf.ToString(),
                        FileType.Dms.ToString(),
                    })
                    .FilterPlatform("Amiga PPC/RTG");
            var ppcRtgProductionsAdf = productions
                    .FilterExcludeTypes(generalTypeExcludeFilter)
                    .FilterFileTypes(new List<string>
                    {
                        FileType.Adf.ToString(),
                        FileType.Dms.ToString(),
                    })
                    .FilterPlatform("Amiga PPC/RTG");

            folderStructure.Add(new Folder("AGA", GetProductionsFolderStructure(agaProductions, includeTypes: true)));
            folderStructure.Add(new Folder("AGA Floppies", GetProductionsFolderStructure(agaProductionsAdf, includeTypes: true)));
            folderStructure.Add(new Folder("OCS/ECS", GetProductionsFolderStructure(ocsEcsProductions, includeTypes: true)));
            folderStructure.Add(new Folder("OCS/ECS Floppies", GetProductionsFolderStructure(ocsEcsProductionsAdf, includeTypes: true)));
            folderStructure.Add(new Folder("PPC/RTG", GetProductionsFolderStructure(ppcRtgProductions, includeTypes: true)));
            folderStructure.Add(new Folder("PPC/RTG Floppies", GetProductionsFolderStructure(ppcRtgProductionsAdf, includeTypes: true)));
            folderStructure.Add(new Folder("Artpacks", GetProductionsFolderStructure(productions.FilterType("artpack"), includeTypes: false)));
            folderStructure.Add(new Folder("BBStros", GetProductionsFolderStructure(productions.FilterType("bbstro"), includeTypes: false)));
            folderStructure.Add(new Folder("Cracktros", GetProductionsFolderStructure(productions.FilterType("cracktro"), includeTypes: false)));
            folderStructure.Add(new Folder("Demotools", GetProductionsFolderStructure(productions.FilterType("demotool"), includeTypes: false)));
            folderStructure.Add(new Folder("Diskmags", GetProductionsFolderStructure(productions.FilterType("diskmag"), includeTypes: false)));
            folderStructure.Add(new Folder("Games", GetProductionsFolderStructure(productions.FilterType("game"), includeTypes: false)));
            folderStructure.Add(new Folder("Invitations", GetProductionsFolderStructure(productions.FilterType("invitation"), includeTypes: false)));
            //folderStructure.Add(new Folder("Liveacts", GetProductionsFolderStructure(productions.FilterType("liveact"), includeTypes: false)));
            folderStructure.Add(new Folder("Music disks", GetProductionsFolderStructure(productions.FilterType("musicdisk"), includeTypes: false)));
            folderStructure.Add(new Folder("Procedural graphics", GetProductionsFolderStructure(productions.FilterType("procedural graphics"), includeTypes: false)));
            folderStructure.Add(new Folder("Reports", GetProductionsFolderStructure(productions.FilterType("report"), includeTypes: false)));
            folderStructure.Add(new Folder("Slideshows", GetProductionsFolderStructure(productions.FilterType("slideshow"), includeTypes: false)));
            folderStructure.Add(new Folder("Votedisks", GetProductionsFolderStructure(productions.FilterType("votedisk"), includeTypes: false)));
            folderStructure.Add(new Folder("Wild", GetProductionsFolderStructure(productions.FilterType("wild"), includeTypes: false)));

            return folderStructure;
        }

        private List<Folder> GetProductionsFolderStructure(IList<Production> productions, bool includeTypes)
        {
            var folderStructure = new List<Folder>();

            var groups = productions.GetGroups();
            foreach (var group in groups)
            {
                var groupProductions = productions.Where(x => x.Metadata.Groups.Contains(group));

                var groupFolders = new List<Folder>();
                foreach (var groupProduction in groupProductions)
                {
                    groupFolders.Add(new Folder(groupProduction.GetFolderName(includeGroups: false, includeTypes: includeTypes, fileSystemSafeNames: true), groupProduction));
                }
                folderStructure.Add(new Folder(group, groupFolders));
            }

            return folderStructure;
        }

        public void WriteOutput()
        {
            // TODO: How should we handle this?
            foreach (var production in Productions.Values)
            {
                production.OutputDetails = new List<OutputDetail>();
            }
            SaveProductions();

            _writeOutputCounter = 0;

            var masterFolderStructure = GetMasterFolderStructure();
            var dateTime = $"{DateTime.Now.ToString("yyMMdd_HHmm")}";
            WriteOutputFolder($"Output{dateTime}", masterFolderStructure);
        }

        private void WriteOutputFolder(string folderPath, IList<Folder> folderStructure)
        {
            //MakedirIfNotExists(folderPath);

            foreach (var folder in folderStructure)
            {
                var thisFolder = $"{folderPath}\\{folder.Name}";
                MakedirIfNotExists(thisFolder);
                WriteOutputFolder(thisFolder, folder.Childrens);
                if (folder.Production != null)
                {
                    WriteProductionToFolder(thisFolder, folder.Production);
                }
            }
        }

        private void WriteProductionToFolder(string outputFolder, Production production)
        {
            var cacheFilePath = GetProductionCacheFullPath(production.Download.CacheFileName);

            bool shouldExtract = false;
            bool shouldCopyCacheFile = false;
            switch (production.Download.FileType)
            {

                case FileType.Unknown:
                case FileType.Html:
                    break;
                case FileType.Lha:
                case FileType.Zip:
                case FileType.Zip7:
                case FileType.Rar:
                case FileType.Lzx:
                case FileType.Gz:
                    shouldExtract = true;
                    shouldCopyCacheFile = false;

                   

                    break;

                case FileType.AmigaExe:
                case FileType.Adf:
                case FileType.Dms:
                case FileType.Mkv:
                case FileType.Avi:
                case FileType.Amos:
                case FileType.Mpeg:
                case FileType.Txt:
                case FileType.Png:
                case FileType.Gif:
                    shouldExtract = false;
                    shouldCopyCacheFile = true;

                    break;

                default:
                    break;
            }


            if (shouldExtract)
            {
                var extractDestPath = GetExtractDestPath(outputFolder);
                var extractArchiveSuccess = ExtractArchive(extractDestPath, production, cacheFilePath);
                if (extractArchiveSuccess == false)
                {
                    shouldCopyCacheFile = true;
                }
            }

            if (shouldCopyCacheFile)
            {
                CopyCacheFile(outputFolder, production, cacheFilePath);
            }

            _writeOutputCounter++;
            if (_writeOutputCounter > _saveProductionsHowOften)
            {
                _writeOutputCounter = 0;
                SaveProductions();
            }
        }

        private bool ExtractArchive(string extractDestPath, Production production, string cacheFilePath)
        {
            var zip7Command2 = $"\"{_zip7Path}\" x \"{cacheFilePath}\" -o\"{extractDestPath}\"";
            _logger.Information($"Extracting to [{extractDestPath}]");
            try
            {
                Process cmd = new Process();
                cmd.StartInfo.FileName = "cmd.exe";
                cmd.StartInfo.RedirectStandardInput = true;
                cmd.StartInfo.RedirectStandardOutput = true;
                cmd.StartInfo.CreateNoWindow = true;
                cmd.StartInfo.UseShellExecute = false;
                cmd.Start();

                cmd.StandardInput.WriteLine($"{zip7Command2}");
                cmd.StandardInput.Flush();
                cmd.StandardInput.Close();
                cmd.WaitForExit();
                var cmdOutput = cmd.StandardOutput.ReadToEnd();
                if (cmdOutput.Contains("Everything is Ok"))
                {
                    _logger.Information(cmdOutput);
                    production.OutputDetails.Add(new OutputDetail(OutputStatus.Ok, extractDestPath));
                    return true;
                }

                _logger.Error(cmdOutput);
                production.OutputDetails.Add(new OutputDetail(OutputStatus.Error, extractDestPath));
                return false;
            }
            catch (Exception)
            {
                production.OutputDetails.Add(new OutputDetail(OutputStatus.Error, extractDestPath));
                return false;
            }
        }

        private string GetExtractDestPath(string outputFolder)
        {
            var extractDestPath = Path.Combine(_productionsPath, outputFolder);
            return extractDestPath;
        }

        private void CopyCacheFile(string outputFolder, Production production, string cacheFilePath)
        {
            var destPath = Path.Combine(_productionsPath, outputFolder, production.Download.FileName);
            var destFileNameOnly = Path.GetFileNameWithoutExtension(destPath);
            var destExtension = Path.GetExtension(destPath);
            var destDirectoryOnly = Path.GetDirectoryName(destPath);
            try
            {
                var dupeCount = 1;
                while (File.Exists(destPath))
                {
                    string tempFileName = $"{destFileNameOnly}({dupeCount++})";
                    destPath = Path.Combine(destDirectoryOnly, tempFileName + destExtension);
                    _logger.Warning($"File already exists! Duplicate files? [{destPath}]");
                }

                _logger.Information($"Copying file [{destPath}]");
                File.Copy(cacheFilePath, destPath);
                production.OutputDetails.Add(new OutputDetail(OutputStatus.Ok, destPath));
            }
            catch (Exception)
            {
                production.OutputDetails.Add(new OutputDetail(OutputStatus.Error, destPath));
            }
        }

        private void MakedirIfNotExists(string output)
        {
            var fullPath = Path.Combine(_productionsPath, output);
            var dir = new DirectoryInfo(fullPath);
            if (dir.Exists == false)
            {
                _logger.Information($"Creating folder [{fullPath}");
                dir.Create();
            }
        }
    }

    public class Folder
    {
        public string Name { get; set; }

        public Folder(string folderName, IList<Folder> children)
        {
            Name = folderName;
            Production = null;
            Childrens = children;
        }

        public Folder(string folderName, Production production)
        {
            Name = folderName;
            Production = production;
            Childrens = new List<Folder>();

        }

        public IList<Folder> Childrens { get; set; }
        public Production Production { get; set; }

        public override string ToString()
        {
            return $"{Name}";
        }
    }


    public class Production
    {
        public Production()
        {
            Metadata = new ProductionMetadata();
            Download = new ProductionDownload();
            OutputDetails = new List<OutputDetail>();
        }

        public int PouetId { get; set; }
        public string Title { get; set; }
        public string PouetUrl { get; set; }

        public ProductionMetadata Metadata { get; }
        public ProductionDownload Download { get; }
        public IList<OutputDetail> OutputDetails { get; set; }

        public override string ToString()
        {
            return GetFolderName(includeGroups: true, includeTypes: true, fileSystemSafeNames: false);
        }

        public string GetFolderName(bool includeGroups, bool includeTypes, bool fileSystemSafeNames)
        {
            var groups = includeGroups ? $@"_{Metadata.Groups.ToSingleString()}" : string.Empty;
            var types = includeTypes ? $@"_{Metadata.Types.ToSingleString()}" : string.Empty;
            var year = GetYear();
            var folderName = $"{Title}{groups}{types}{year}";
            if (fileSystemSafeNames)
            {
                folderName = MakeFileSystemSafeName(folderName);
            }
            return folderName;
        }

        private string GetYear()
        {
            if (string.IsNullOrWhiteSpace(Metadata.ReleaseDate))
                return string.Empty;
            var yearDigits = Regex.Replace(Metadata.ReleaseDate, @"[^0-9]", "");
            return string.IsNullOrWhiteSpace(yearDigits) ? string.Empty : yearDigits;
        }

        private string MakeFileSystemSafeName(string fileName)
        {
            foreach (var invalidFileNameChar in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(invalidFileNameChar.ToString(), "");
            }

            return fileName;
        }
    }

    public class OutputDetail
    {
        public OutputDetail()
        {
            OutputStatus = OutputStatus.Unknown;
            Path = null;
        }

        public OutputDetail(OutputStatus outputStatus)
        {
            OutputStatus = outputStatus;
            Path = null;
        }

        public OutputDetail(OutputStatus outputStatus, string path)
        {
            OutputStatus = outputStatus;
            Path = path;
        }

        public OutputStatus OutputStatus { get; set; }
        public string Path { get; set; }

        public override string ToString()
        {
            return $"{OutputStatus} [{Path}]";
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
        public int? AllTimeRank { get; set; }
    }

    public class ProductionDownload
    {
        public DownloadStatus Status { get; set; }
        public FileType FileType { get; set; }
        public FileIdentifiedByType FileIdentifiedByType { get; set; }
        public string FileName { get; set; }
        public int FileSize { get; set; }
        public string CacheFileName { get; set; }
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
        Error,
        UnknownHtml,
        UnknownFileType
    }

    public enum OutputStatus
    {
        Unknown = 0,
        Ok,
        Error,
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
        Lzx,
        Gz,
        Mkv,
        Avi,
        Amos,
        Mpeg,
        Txt,
        Png,
        Gif
    }

    public enum FileIdentifiedByType
    {
        Unknown = 0,
        Content,
        FileName,
        ContentLength
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

    public enum DownloadProductionsMode
    {
        Unknown = 0,
        Rescan,
        RescanRetryError,
        NoRescan
    }


}