using HtmlAgilityPack;

namespace PouetRobot
{
    public interface IHtmlProber
    {
        string GetProbeUrl(string url, HtmlDocument doc);

    }
}