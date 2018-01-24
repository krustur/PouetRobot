using System.Linq;
using HtmlAgilityPack;

namespace PouetRobot
{
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
}