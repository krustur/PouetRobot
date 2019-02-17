using System.Collections.Generic;
using System.Linq;

namespace PouetRobot
{
    public static class StringListExtensions
    {
        public static string ToSingleString(this IList<string> strings, string separator = "-", bool toCamelCase = true)
        {
            if (strings.Count == 0)
            {
                return string.Empty;
            }
            if (strings.Count == 1)
            {
                return toCamelCase ? strings[0].ToCamelCase() : strings[0];
            }

            var result = toCamelCase ? 
                strings.Aggregate((i, j) => i.ToCamelCase() + separator + j.ToCamelCase())
                : 
                strings.Aggregate((i, j) => i + separator + j);
                ;
            return result;
        }
    }
}