using System.Collections.Generic;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace TorrentReader.Http
{
    internal interface IHttpProvider
    {
        CloudflareClearance Clearance { get; set; }

        Task<HtmlDocument> GetHtmlAsync(string url);

        Task<IReadOnlyList<TEntity>> GetAsync<TEntity>(string relativeUrl) where TEntity : class;
    }
}
