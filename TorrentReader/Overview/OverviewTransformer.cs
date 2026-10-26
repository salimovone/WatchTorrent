using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HtmlAgilityPack;
using TorrentReader.Search.Models;

namespace TorrentReader.Overview
{
    internal class OverviewTransformer : IOverviewTransformer
    {
        public IReadOnlyList<SearchResultItem> Transform(HtmlDocument document)
        {
            var tableItemNodeCollection = document.DocumentNode.SelectNodes("//table/tbody/tr");

            if(tableItemNodeCollection == null)
            {
                return new List<SearchResultItem>();
            }

            return tableItemNodeCollection.Select(CreateSearchResultItem).ToList();
        }

        private static SearchResultItem CreateSearchResultItem(HtmlNode tableItem)
        {
            var tdNodes = tableItem.SelectNodes("td");

            var nameNode = tdNodes[0].SelectSingleNode("a[not(contains(@class, 'icon'))]");
            var name = nameNode.InnerText;
            var commentsNode = tdNodes[0].SelectSingleNode("span");

            var relativeUrl = nameNode.GetAttributeValue("href", string.Empty);
            var id = GetId(relativeUrl);
            var slug = GetSlug(relativeUrl);

            var amountComments = commentsNode != null ? int.Parse(commentsNode.InnerText) : 0;
            var amountSeeders = int.Parse(tdNodes[1].InnerText);
            var amountLeechers = int.Parse(tdNodes[2].InnerText);

            var uploadedOnCultureInfo = new CultureInfo("en-us");
            var uploadedOn = ParseDateTime(tdNodes, uploadedOnCultureInfo);

            var size = tdNodes[4].SelectSingleNode("text()").InnerText;
            var uploaderName = tdNodes[5].SelectSingleNode("a[1]").InnerText;

            return new SearchResultItem(name, amountComments, amountSeeders, amountLeechers, uploadedOn, size, uploaderName, id, slug);
        }

        private static DateTime ParseDateTime(HtmlNodeCollection tdNodes, CultureInfo uploadedOnCultureInfo)
        {
            var formats = new []  {@"MMM\. d \'yy", @"htt MMM\. d", "h:mmtt", @"htt MMM\. d" };

            return DateTime.ParseExact(StripDateAffix(tdNodes[3].InnerText), formats, uploadedOnCultureInfo, DateTimeStyles.None);
        }

        private static int GetId(string relativeUrl)
        {
            var indexSecondSlash = relativeUrl.IndexOf("/", 1, StringComparison.Ordinal);
            var indexThirdSlash = relativeUrl.IndexOf("/", indexSecondSlash + 1, StringComparison.Ordinal);

            return int.Parse(relativeUrl.Substring(indexSecondSlash + 1, indexThirdSlash - indexSecondSlash - 1));
        }

        private static string GetSlug(string relativeUrl)
        {
            var indexSecondToLastSlash = relativeUrl.LastIndexOf("/", relativeUrl.Length - 2, StringComparison.Ordinal);

            return relativeUrl.Substring(indexSecondToLastSlash + 1, relativeUrl.Length - indexSecondToLastSlash - 2);
        }

        private static string StripDateAffix(string date)
        {
            var affixes = new List<string> { "st", "nd", "th", "rd" };

            var modifiedDate = date;

            affixes.ForEach(affix => modifiedDate = modifiedDate.Replace(affix, string.Empty));

            return modifiedDate;
        }
    }
}
