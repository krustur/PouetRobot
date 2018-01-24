using HtmlAgilityPack;

namespace PouetRobot
{
    public class DropboxUrlProber : IHtmlProber
    {
        public string GetProbeUrl(string url, HtmlDocument doc)
        {
            url = url.Trim();
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
}