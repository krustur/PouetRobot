namespace PouetRobot
{
    public static class StringExtensions
    {
        public static string TrimNbsp(this string source)
        {
            return source.Replace("&nbsp;", "").Trim();
        }
    }
}