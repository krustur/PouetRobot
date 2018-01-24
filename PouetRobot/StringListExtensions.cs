using System.Collections.Generic;
using System.Linq;

namespace PouetRobot
{
    public static class StringListExtensions
    {
        public static string ToSingleString(this IList<string> strings, string separator = "-")
        {
            if (strings.Count == 0)
            {
                return string.Empty;
            }

            var result = strings.Aggregate((i, j) => i + separator + j);
            return result;
        }
    }
}