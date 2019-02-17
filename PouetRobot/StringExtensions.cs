using System.Collections.Generic;
using System.Text;

namespace PouetRobot
{
    public static class StringExtensions
    {
        private static Dictionary<string, string> _groupModifiers;
        private static Dictionary<string, string> _folderModifiers;

        static StringExtensions()
        {
            _groupModifiers = new Dictionary<string, string>
            {
                { "3 little elks", "3LE" },
                { "the black lotus", "TBL" },
                { "carillon & cyberiad", "CNCD" },
                { "Tristar & red sector Inc", "TRSI" },
            };

            _folderModifiers = new Dictionary<string, string>
            {
                { "IntroIntro", "Intro" },
                { "DemoDemo", "Demo" },
                { "4k4k", "4k" },
                { "40k40k", "40k" },
                { "64k64k", "64k" },
            };
        }

        public static string TrimNbsp(this string source)
        {
            return source.Replace("&nbsp;", "").Trim();
        }

        internal static string ToCamelCase(this string value)
        {
            if (value.Length == 0)
            {
                return value;
            }
            var builder = new StringBuilder(value);
            builder[0] = (builder[0].ToString().ToUpper())[0];

            for (int i = 0; i < builder.Length - 1; i++)
            {
                if (builder[i] == ' ')
                {
                    builder[i + 1] = (builder[i + 1].ToString().ToUpper())[0];
                }
            }

            return builder.Replace(" ", "").ToString();
            //return value.Replace(" ", "");
        }

        public static string FixGroupName(this string group)
        {
            foreach (var modifier in _groupModifiers)
            {
                if (group.ToLower() == modifier.Key.ToLower())
                {
                    group = modifier.Value;
                }
            }

            group = group.ToCamelCase();

            return group;
        }

        public static string RemoveFolderNameDupes(this string folder)
        {
            foreach (var modifier in _folderModifiers)
            {
                folder = folder.Replace(modifier.Key, modifier.Value);
            }

            return folder;
        }
    }
}