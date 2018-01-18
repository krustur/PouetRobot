using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace PouetRobot
{
    class Program
    {
        static void Main(string[] args)
        {
            var productionsPath = @"D:\Temp\PouetDownload\";
            var productionsFileName = $@"Productions.json";
            var startPageUrl = "http://www.pouet.net/prodlist.php?platform[]=Amiga+AGA&platform[]=Amiga+OCS/ECS&platform[]=Amiga+PPC/RTG";
            var whitelistFileSuffixes = new List<string>
            {
                "lha",
                "zip",
                "exe",
                "adf",
                "dms",
                "rar",
                "7z"
            };
            var whitelistBasePaths = new List<string>
            {
                "http://www.nukleus.nu/",
                "https://files.scene.org/get/",
                "http://heckmeck.de/",
                "ftp://",
                "http://aminet.net/",
                "https://amigafrance.com/",
                "https://www.dropbox.com/",
                "http://insane.demoscene.se/",
                "http://cyberpingui.free.fr/",
                "https://github.com/",
                "http://www.neoscientists.org/",
                "https://piwnica.ws/",
                "http://devkk.net/",
                "http://juicycube.net/",
                "http://crinkler.net/",
                "http://resistance.no/",
                "http://www.jokov.home.pl/",
                "https://decrunch.org/",
                "http://ftp.amigascne.org",
                "http://rift.team/",
                "http://jokov.com/",


                //"http://we.tl/",
                //"http://fatmagnus.ppa.pl/",
                //"http://www.homme3d.com/",
                //"http://dl.cloanto.com/",
        };
            var blacklistBasePaths = new List<string>
            {
                "http://www.speccy.pl/",
                "http://mega.szajb.us/",
                "https://sordan.ie/",
            };
            //var startPageUrl = "http://www.pouet.net/prodlist.php?platform[0]=Amiga+AGA&platform[1]=Amiga+OCS/ECS&page=685";

            Console.WriteLine("Begin download!");

            var robot = new Robot(startPageUrl, productionsPath, productionsFileName, whitelistFileSuffixes, whitelistBasePaths, blacklistBasePaths);
            robot.GetProdList();
            robot.GetDownloadLinks();

            Console.WriteLine("All is done! Press enter!");
            Console.ReadLine();
        }


        

    }

    public class Robot
    {
        private readonly string _startPageUrl;
        private readonly string _productionsPath;
        private readonly string _productionsFileName;
        private readonly List<string> _whitelistFileSuffixes;
        private readonly List<string> _whitelistBasePaths;
        private readonly List<string> _blacklistBasePaths;

        public Robot(string startPageUrl, string productionsPath, string productionsFileName, List<string> whitelistFileSuffixes, List<string> whitelistBasePaths,
            List<string> blacklistBasePaths)
        {
            _startPageUrl = startPageUrl;
            _productionsPath = productionsPath;
            _productionsFileName = productionsFileName;
            _whitelistFileSuffixes = whitelistFileSuffixes;
            _whitelistBasePaths = whitelistBasePaths;
            _blacklistBasePaths = blacklistBasePaths;
        }

        public List<Production> Productions { get; set; }

        public void GetProdList()
        {
            var nextUrl = _startPageUrl;
            Productions = new List<Production>();

            if (LoadProductions())
            {
                return;
            }

            while (nextUrl != null
                   //&& !nextUrl.EndsWith('5')
                   )
            {
                nextUrl = GetProdListPage(nextUrl);
            }

            SaveProductions();
        }

        private bool LoadProductions()
        {
            var productionsFileName = GetProductionsFileName();
            if (File.Exists(productionsFileName))
            {
                Productions = JsonConvert.DeserializeObject<List<Production>>(File.ReadAllText((productionsFileName)));
                return true;
            }

            return false;
        }

        private void SaveProductions()
        {
            Console.WriteLine("Saving Productions json file!");
            var productionsFileName = GetProductionsFileName();
            var productionsJson = JsonConvert.SerializeObject(Productions, Formatting.Indented, new JsonConverter[] { new StringEnumConverter() });
            File.WriteAllText(productionsFileName, productionsJson);
        }

        private string GetProductionsFileName()
        {
            return Path.Combine(_productionsPath, _productionsFileName);
        }

        private string GetProdListPage(string pageUrl)
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

            int rowCount = 0;
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
                var typeParts = titleColumnParts[0].SelectNodes("span");
                var type = typeParts.Aggregate(string.Empty, (current, typePart) => current + (" " + typePart.InnerText));
                type = type.TrimStart();
                var prodTitle = titleColumnParts[2].InnerText;
                var prodPageUrl = titleColumnParts[2].SelectNodes("a").First().Attributes["href"].Value;
                prodPageUrl = CombinePath(pageUrl, prodPageUrl);
                var prodPageUrlParts = prodPageUrl.Split('=');
                var prodPageUrlProdId = Convert.ToInt32(prodPageUrlParts[prodPageUrlParts.Length - 1]);

                var platformParts = titleColumnParts[1].SelectNodes("span");
                var platform = platformParts.Aggregate(string.Empty, (current, typePart) => current + (" " + (typePart.InnerText.Replace("Amiga ", ""))));
                platform = platform.TrimStart();

                // Group
                string groupName = null;
                var groupColumn = columns[1];
                if (groupColumn.ChildNodes.Count > 1)
                {
                    groupName = groupColumn.SelectNodes("a").First().InnerText;
                    var groupUrl = groupColumn.SelectNodes("a").First().Attributes["href"].Value;
                    var groupUrlParts = groupUrl.Split('=');
                    var groupPouetId = Convert.ToInt32(groupUrlParts[groupUrlParts.Length - 1]);
                }

                // Party
                string partyDescription = null;
                var partyColumn = columns[2];
                if (partyColumn.ChildNodes.Count > 1)
                {
                    partyDescription = partyColumn.SelectNodes("a").First().InnerText;
                }

                // Release date
                var releaseDateColumn = columns[3];
                var releaseDate = releaseDateColumn.InnerText;

                var addedColumn = columns[4];
                var thumbsUpColumn = columns[5];
                var pigColumn = columns[6];
                var thumbsDownColumn = columns[7];
                var averageColumn = columns[8];
                var popularityColumn = columns[9];

                var production = new Production
                {
                    PouetId = prodPageUrlProdId,
                    PouetUrl = prodPageUrl,
                    Title = prodTitle,
                    Type = type,
                    Platform = platform,

                    Group = groupName,
                    PartyDescription = partyDescription,
                    ReleaseDate = releaseDate
                };
                Productions.Add(production);

                Console.WriteLine($"Production: {production}");
            }

            //Console.ReadLine();
            return nextPageUrl == null ? null : CombinePath(pageUrl, nextPageUrl);
        }

        public void GetDownloadLinks()
        {
            var saveCounter = 0;
            var progress = 0;
            var maxProgress = Productions.Count;


            //Console.WriteLine("Clean start!!!");
            //Productions.ForEach(x => x.DownloadUrlStatus = DownloadUrlStatus.Unknown);
            //SaveProductions();


            foreach (var production in Productions)
            {
                progress++;
                if (production.DownloadUrlStatus == DownloadUrlStatus.Unknown)
                {
                    GetDownloadLink(production);
                    //production.Done = true;

                    Console.WriteLine($"[{progress}/{maxProgress}] Download url result [{production.Title} ({production.DownloadUrlStatus} {production.DownloadUrl})]");

                    if (saveCounter++ >= 10)
                    {
                        saveCounter = 0;
                        SaveProductions();
                    }
                }
                else
                {
                    Console.WriteLine($"[{progress}/{maxProgress}] Already have download url for [{production.Title} ({production.DownloadUrl})]");
                }
            }
        }

        private void GetDownloadLink(Production production)
        {
            var doc = GetHtmlDocument(production.PouetUrl);

            var mainDownloadUrl = doc.DocumentNode
                    .SelectNodes("//a[contains(@id, 'mainDownloadLink')]")
                    ?.FirstOrDefault()
                    ?.Attributes["href"]
                    ?.Value
                ;

            
            var probeResult = ProbeDownloadUrl(mainDownloadUrl);

            production.DownloadUrl = probeResult.DownloadUrl;
            production.DownloadUrlStatus = probeResult.Status;
        }

        private (string DownloadUrl, DownloadUrlStatus Status) ProbeDownloadUrl(string downloadUrl)
        {
            if (IsSkipLink(downloadUrl))
            {
                return (downloadUrl, DownloadUrlStatus.Skip);
            }

            if (IsTinyCcLink(downloadUrl))
            {
                return ProbeTinyCcLink(downloadUrl);
            }

            if (IsFilesSceneOrg(downloadUrl))
            {
                return ProbeFilesSceneOrg(downloadUrl);
            }

            if (IsDemoZooOrg(downloadUrl))
            {
                return ProbeDemoZooOrg(downloadUrl);
            }

            if (IsFileDownloadLink(downloadUrl))
            {
                return (downloadUrl, DownloadUrlStatus.Ok);
            }

            return (null, DownloadUrlStatus.Unknown);
        }

        private bool IsDemoZooOrg(string downloadUrl)
        {
            return downloadUrl.ToLower().StartsWith("https://demozoo.org/");

        }

        private (string DownloadUrl, DownloadUrlStatus Status) ProbeDemoZooOrg(string downloadUrl)
        {
            var doc = GetHtmlDocument(downloadUrl);

            var mainDownloadUrl = doc.DocumentNode
                    .SelectNodes("//div[contains(@class, 'primary')]/a")
                    ?.FirstOrDefault()
                    ?.Attributes["href"]
                    ?.Value
                ;

            return (mainDownloadUrl, DownloadUrlStatus.Ok);
        }

        private bool IsSkipLink(string downloadUrl)
        {
            foreach (var skipBasePath in _blacklistBasePaths)
            {
                if (downloadUrl.ToLower().StartsWith(skipBasePath))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsFilesSceneOrg(string downloadUrl)
        {
            return downloadUrl.ToLower().StartsWith("https://files.scene.org/view/");
        }

        private (string, DownloadUrlStatus) ProbeFilesSceneOrg(string downloadUrl)
        {
            var doc = GetHtmlDocument(downloadUrl);

            var mainDownloadUrl = doc.DocumentNode
                    .SelectNodes("//li[contains(@id, 'mainDownload')]/a")
                    ?.FirstOrDefault()
                    ?.Attributes["href"]
                    ?.Value
                ;

            return (mainDownloadUrl, DownloadUrlStatus.Ok);
        }

        private bool IsTinyCcLink(string mainDownLoadUrl)
        {
            return mainDownLoadUrl.ToLower().StartsWith(@"http://tiny.cc/");
        }

        private (string, DownloadUrlStatus) ProbeTinyCcLink(string downloadUrl)
        {
            var originalUrl = ResolveTinyCcOriginalUrl(downloadUrl);
            return ProbeDownloadUrl(originalUrl);
        }

        private string ResolveTinyCcOriginalUrl(string downloadUrl)
        {
            var httpClient = new HttpClient();
            var get = httpClient.GetAsync(downloadUrl);
            get.Wait();
            var xxx = get.Result;
            var yyy = xxx.RequestMessage.RequestUri;
            return yyy.ToString();
        }

        private bool IsFileDownloadLink(string linkUrl)
        {
            if (linkUrl == null)
            {
                return false;
            }

            foreach (var directDownloadBasePath in _whitelistBasePaths)
            {
                if (linkUrl.ToLower().StartsWith(directDownloadBasePath))
                {
                    return true;
                }
            }
            // Remove query strings
            var linkUrlUri = new Uri(linkUrl);
            linkUrl = $"{linkUrlUri.Scheme}://{linkUrlUri.Host}{linkUrlUri.AbsolutePath}";


            var linkUrlParts = linkUrl.ToLower().Split(".");
            if (linkUrlParts.Length < 1)
            {
                return false;
            }

            var suffix = linkUrlParts[linkUrlParts.Length - 1];

            if (_whitelistFileSuffixes.Contains(suffix))
            {
                //return true;
            }

            return false;
        }

        private string CombinePath(string fullBaseUrl, string nextPageUrl)
        {
            var baseUri = new Uri(fullBaseUrl);
            var newPath = new Uri(new Uri($"{baseUri.Scheme}://{baseUri.Host}"), nextPageUrl);
            return newPath.ToString();
        }

        private static HtmlDocument GetHtmlDocument(string pageUrl)
        {
            Console.WriteLine($"GET url [{pageUrl}]: ");

            var web = new HtmlWeb();
            var doc = web.Load(pageUrl);

            return doc;
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
    }

    public class Production  
    {
        public int PouetId { get; set; }
        public string PouetUrl { get; set; }
        public string Title { get; set; }
        public string Type { get; set; }
        public string Platform { get; set; }

        public string Group { get; set; }
        public string PartyDescription { get; set; }
        public string ReleaseDate { get; set; }

        public string DownloadUrl { get; set; }
        public DownloadUrlStatus DownloadUrlStatus { get; set; }

        public bool Done { get; set; }


        public override string ToString()
        {
            return $"{Title} / {Group} [{Type} - {Platform}] [{ReleaseDate} - {PartyDescription}]";
        }
    }

    public enum DownloadUrlStatus
    {
        Unknown = 0,
        Ok,
        Skip
    }
}