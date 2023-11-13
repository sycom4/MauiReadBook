using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Formats.Asn1;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WM.EasyReading.Service.Dtos;

namespace WM.EasyReading.Service.Services
{
    public class SuperSearch : ISuperSearch, IDisposable
    {
        public SuperSearch()
        {
            _HttpClient = new HttpClient();
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            _HttpClient.DefaultRequestHeaders.Add("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

            _HttpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:73.0) Gecko/20100101 Firefox/73.0");

            // 先添加这一个，才能使用 UTF 以外的其他编码 

        }
        private HttpClient _HttpClient;
        public void Dispose()
        {
            _HttpClient?.Dispose();
        }


        private async Task<StreamReader> Http_Get(string url)
        {
            var response = await _HttpClient.GetAsync(url).ConfigureAwait(false);
            // 读取字符流
            var result = await response.Content.ReadAsStreamAsync();
            // 使用指定的字符编码读取字符流， 默认编码：UTF-8，其他如：GBK
            return new StreamReader(result, Encoding.GetEncoding("gbk"));
        }

        public async Task<List<BookSimpleDto>> SearchFromUrl(string search)
        {
            search = search.Replace("https://m.tcl-xyj.com/", "https://www.tcl-xyj.com/");

            StreamReader stream = await Http_Get(search);

            var list = new List<BookSimpleDto>();

            var doc = new HtmlDocument();
            doc.Load(stream);

            if (search.StartsWith("https://www.tcl-xyj.com/"))
            {
                var baseurl = "https://www.tcl-xyj.com/";
                var nodes = doc.DocumentNode.SelectNodes("//div[@class='book-text']");
                list.Add(new BookSimpleDto(nodes[0].SelectNodes("./h1")[0].InnerText, search.Substring(baseurl.Length), baseurl, nodes[0].SelectNodes("./span")[0].InnerText));
            }


            return list;
        }

        public async Task<List<BookSimpleDto>> Search(string search)
        {
            if (search.StartsWith("http"))
            {
                return await SearchFromUrl(search);
            }

            StreamReader stream = await Http_Get($"http://www.b520.cc/modules/article/search.php?searchkey={search}");

            var list = new List<BookSimpleDto>();

            var doc = new HtmlDocument();
            doc.Load(stream);
            //*[@id="hotcontent"]/table/tbody/tr[2]
            var nodes = doc.DocumentNode.SelectNodes("//div[@id='hotcontent']/table/tr");

            Console.WriteLine(nodes);
            for (int i = 1; i < nodes.Count(); i++)
            {
                var tdNodes = nodes[i].SelectNodes("./td");
                var title = tdNodes[0].InnerText;
                var urlNode = tdNodes[0].SelectSingleNode("./a");
                var url = urlNode.Attributes[0].Value;
                var author = tdNodes[2].InnerText;
                var baseurl = "http://www.b520.cc";

                list.Add(new BookSimpleDto(title, url, baseurl, author));

            }
            return list;
        }

        public async Task<BookDetailDto> SearchDetail_b520(BookSimpleDto dto)
        {
            StreamReader stream = await Http_Get($"{dto.BaseUrl}{dto.Url}");

            var chapters = new List<BookChapterDto>();
            var doc = new HtmlDocument();
            doc.Load(stream);
            //*[@id="hotcontent"]/table/tbody/tr[2]
            var ddNodes = doc.DocumentNode.SelectNodes("//div[@id='list']/dl/dd");
            foreach (var ddNode in ddNodes)
            {
                var anode = ddNode.SelectSingleNode("./a");
                var name = anode.InnerText;
                var url = anode.Attributes[0].Value;
                chapters.Add(new BookChapterDto(name, url, dto.BaseUrl));

            }
            return new BookDetailDto(dto, chapters);
        }

        public async Task<BookDetailDto> SearchDetail_tcl_xyj(BookSimpleDto dto)
        {
            StreamReader stream = await Http_Get($"{dto.BaseUrl}{dto.Url}");

            var chapters = new List<BookChapterDto>();
            var doc = new HtmlDocument();
            doc.Load(stream);
            //*[@id="hotcontent"]/table/tbody/tr[2]
            var ddNodes = doc.DocumentNode.SelectNodes("//div[@class='book-chapter-list']/ul[2]/li");
            foreach (var ddNode in ddNodes)
            {
                var anode = ddNode.SelectSingleNode("./a");
                var name = anode.InnerText;
                var url = anode.Attributes[0].Value;
                chapters.Add(new BookChapterDto(name, url, dto.BaseUrl));

            }
            return new BookDetailDto(dto, chapters);
        }

        public async Task<BookDetailDto> SearchDetail(BookSimpleDto dto)
        {
            switch (dto.BaseUrl)
            {
                case "http://www.b520.cc":
                    {
                        return await SearchDetail_b520(dto);
                    }
                case "https://www.tcl-xyj.com/":
                    {
                        return await SearchDetail_tcl_xyj(dto);
                    }
            }
            return null;
        }

        public async Task<BookChapterDto> SearchChapterContent_b520(BookChapterDto dto)
        {
            StreamReader stream = await Http_Get($"{dto.BaseUrl}{dto.Url}");
            var doc = new HtmlDocument();
            doc.Load(stream);


            var pnodes = doc.DocumentNode.SelectNodes("//div[@id='content']/p");
            var name = doc.DocumentNode.SelectSingleNode("//div[@class='bookname']/h1").InnerText;
            var chapter = pnodes.Select(e => e.InnerText).ToList();
            return new BookChapterDto(chapter, name, dto.Url, dto.BaseUrl);

        }

        public async Task<BookChapterDto> SearchChapterContent_tcl_xyj(BookChapterDto dto)
        {
            StreamReader stream = await Http_Get($"{dto.BaseUrl}{dto.Url}");
            string html = stream.ReadToEnd();
            string ssid = html.Split("ssid=")[1].Split(";")[0];
            string bookid = html.Split("bookid=")[1].Split(";")[0];
            string chapterid = html.Split("chapterid=")[1].Split(";")[0];
            string real_url = $"{dto.BaseUrl}/files/article/html{ssid}/{int.Parse(bookid) / 1000}/{bookid}/{chapterid}.html";

            stream = await Http_Get(real_url);
            html = stream.ReadToEnd();
            string[] cctxt = html.Split("cctxt");

            string first_str = cctxt[1].Split("'")[1];
            for (int i = 3; i < cctxt.Length; i += 2)
            {
                first_str = first_str.Replace(cctxt[i].Split("/")[1], cctxt[i].Split("'")[1]);
            }
            first_str = first_str.Replace("&nbsp;", "");

            var name = dto.Name;
            var chapter = first_str.Split("<br>").ToList();
            return new BookChapterDto(chapter, name, dto.Url, dto.BaseUrl);

        }

        public async Task<BookChapterDto> SearchChapterContent(BookChapterDto dto)
        {
            switch (dto.BaseUrl)
            {
                case "http://www.b520.cc":
                    {
                        return await SearchChapterContent_b520(dto);
                    }
                case "https://www.tcl-xyj.com/":
                    {
                        return await SearchChapterContent_tcl_xyj(dto);
                    }
            }
            return null;
        }

    }
}
