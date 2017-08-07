using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.TagHelpers;
using Microsoft.AspNetCore.Razor.Runtime.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Caching.Memory;

namespace MiningCore.TagHelpers
{
    [HtmlTargetElement("script", Attributes = "[" + Helper.WebpackAttribute + "], " + "[src]")]
    public class WebPackScriptSourceHelper : ScriptTagHelper
    {
        public WebPackScriptSourceHelper(IHostingEnvironment hostingEnvironment, IMemoryCache cache, HtmlEncoder htmlEncoder, JavaScriptEncoder javaScriptEncoder, IUrlHelperFactory urlHelperFactory) :
            base(hostingEnvironment, cache, htmlEncoder, javaScriptEncoder, urlHelperFactory)
        {
            this.isProduction = hostingEnvironment.IsProduction();
        }

        private readonly bool isProduction;

        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            output.Attributes.RemoveAll(Helper.WebpackAttribute);

            if (!isProduction)
                output.Attributes.SetAttribute("src", Helper.BuildWebpackDevServerUrl(Src));

            base.Process(context, output);
        }
    }

    [HtmlTargetElement("link", Attributes = "[" + Helper.WebpackAttribute + "], " + "[href]")]
    public class WebPackLinkSourceHelper : LinkTagHelper
    {
        public WebPackLinkSourceHelper(IHostingEnvironment hostingEnvironment, IMemoryCache cache, HtmlEncoder htmlEncoder, JavaScriptEncoder javaScriptEncoder, IUrlHelperFactory urlHelperFactory) : 
            base(hostingEnvironment, cache, htmlEncoder, javaScriptEncoder, urlHelperFactory)
        {
            this.isProduction = hostingEnvironment.IsProduction();
        }

        private readonly bool isProduction;

        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            output.Attributes.RemoveAll(Helper.WebpackAttribute);

            if(!isProduction)
                output.Attributes.SetAttribute("href", Helper.BuildWebpackDevServerUrl(Href));

            base.Process(context, output);
        }
    }

    class Helper
    {
        public const string WebpackAttribute = "webpack";

        public static string BuildWebpackDevServerUrl(string uri)
        {
            if (uri.StartsWith("~"))
                uri = uri.Substring(1);

            var builder = new UriBuilder(AppConstants.WebPackDevServerBaseUri.Scheme, 
                AppConstants.WebPackDevServerBaseUri.Host, AppConstants.WebPackDevServerBaseUri.Port, uri);

            return builder.Uri.AbsoluteUri;
        }
    }
}
