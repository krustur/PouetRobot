using System.Net.Http;
using HtmlAgilityPack;

namespace PouetRobot
{
    public class TinyCcProber : IHtmlProber
    {
        public string GetProbeUrl(string url, HtmlDocument doc)
        {
            if (url.ToLower().StartsWith(@"http://tiny.cc/"))
            {
                var httpClient = new HttpClient();
                var result = httpClient.GetAsync(url).GetAwaiter().GetResult();
                var requestUri = result.RequestMessage.RequestUri;
                return requestUri.ToString();
            }

            return null;
        }
    }
}