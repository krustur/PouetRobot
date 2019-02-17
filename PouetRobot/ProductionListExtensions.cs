using System.Collections.Generic;
using System.Linq;

namespace PouetRobot
{
    public static class ProductionListExtensions
    {        
        public static IList<Production> FilterFileTypes(this IList<Production> productions, IList<string> filter)
        {
            return filter.Count > 0
                ? productions
                    .Where(x => filter.Contains(x.Download.FileType.ToString()))
                    .ToList()
                : productions;
        }

        public static IList<Production> FilterExcludeFileTypes(this IList<Production> productions, IList<string> filter)
        {
            return filter.Count > 0
                ? productions
                    .Where(x => filter.Contains(x.Download.FileType.ToString()) == false)
                    .ToList()
                : productions;
        }


        public static IList<Production> FilterPlatform(this IList<Production> productions, string filter)
        {
            IList<Production> result;
            if (filter != null)
            {
                result = FilterPlatforms(productions, new List<string> {filter});
            }
            else
            {
                result = productions;
            }

            return result;
        }

        public static IList<Production> FilterPlatforms(this IList<Production> productions, IList<string> filter)
        {
            return filter.Count > 0
                ? productions
                    .Where(x => x.Metadata.Platforms.Any(filter.Contains))
                    .ToList()
                : productions;
        }

        public static IList<Production> FilterType(this IList<Production> productions, string filter)
        {
            IList<Production> result;
            if (filter != null)
            {
                result = FilterTypes(productions, new List<string> {filter});
            }
            else
            {
                result = productions;
            }
            return result;

        }

        public static IList<Production> FilterTypes(this IList<Production> productions, IList<string> filter)
        {
            return filter.Count > 0
                ? productions
                    .Where(x => x.Metadata.Types.Any(filter.Contains))
                    .ToList()
                : productions;
        }

        public static IList<Production> FilterExcludePlatforms(this IList<Production> productions, IList<string> filter)
        {
            return filter.Count > 0
                ? productions
                    .Where(x => x.Metadata.Platforms.All(y => filter.Contains(y) == false))
                    .ToList()
                : productions;
        }


        public static IList<Production> FilterExcludeTypes(this IList<Production> productions, IList<string> filter)
        {
            return filter.Count > 0
                ? productions
                    .Where(x => x.Metadata.Types.Any(y => filter.Contains(y) == false))
                    .ToList()
                : productions;
        }

        public static IList<Production> FilterExcludeNonCoupDeCours(this IList<Production> productions)
        {
            //return productions;
            return productions
                    .Where(x => x.Metadata.CoupDeCours > 0)
                    .ToList()
                ;
        }

        public static IList<Production> FilterMetadataStatus(this IList<Production> productions, MetadataStatus metadataStatus)
        {
            return FilterMetadataStatuses(productions, new List<string> { metadataStatus.ToString() });
        }

        public static IList<Production> FilterMetadataStatuses(this IList<Production> productions, IList<string> filter)
        {
            return filter.Count > 0
                ? productions
                    .Where(x => filter.Contains(x.Metadata.Status.ToString()))
                    .ToList()
                : productions;
        }

        public static IList<Production> FilterDownloadStatus(this IList<Production> productions, DownloadStatus downloadStatus)
        {
            return FilterDownloadStatuses(productions, new List<string> {downloadStatus.ToString()});
        }

        public static IList<Production> FilterDownloadStatuses(this IList<Production> productions, IList<string> filter)
        {
            return filter.Count > 0
                ? productions
                    .Where(x => filter.Contains(x.Download.Status.ToString()))
                    .ToList()
                : productions;
        }

        public static IList<string> GetGroups(this IList<Production> productions)
        {
            return productions.SelectMany(x => x.Metadata.Groups).DistinctBy(x => x).OrderBy(x => x).ToList();
        }
    }
}