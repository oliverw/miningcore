using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using CodeContracts;
using Newtonsoft.Json;

namespace MiningCore.Net
{
    public class RestRequest : IDisposable
    {
        protected RestRequest(string resource, HttpMethod method, HttpContent content = null)
        {
            this.resource = resource;
            this.method = method;
            Content = content;
        }

        protected RestRequest(HttpMethod method)
        {
            this.resource = string.Empty;
            this.method = method;
        }

        public void Dispose()
        {
            Content?.Dispose();
            Content = null;
        }

        public static RestRequest Create(string resource, HttpMethod method, HttpContent content = null)
        {
            Contract.RequiresNonNull(resource, nameof(resource));

            return new RestRequest(resource, method, content);   
        }

        public static RestRequest Create(HttpMethod method)
        {
            return new RestRequest(method);
        }

        public static RestRequest<T> Create<T>(string resource, HttpMethod method, T content = default(T), bool compress = false)
        {
            Contract.RequiresNonNull(resource, nameof(resource));

            return RestRequest<T>.Create(resource, method, content, compress);
        }

        internal readonly string resource;
        internal readonly HttpMethod method;
        internal List<KeyValuePair<string, string>> parameters;
        internal Dictionary<string, string> headers;

        public HttpContent Content { get; set; }
        public HttpMethod Method => method;
        public List<KeyValuePair<string, string>> Parameters => parameters;
        public Dictionary<string, string> Headers => headers;

        public void AddParameter<T>(string key, T value)
        {
            Contract.RequiresNonNull(key, nameof(key));

            if (parameters == null)
                parameters = new List<KeyValuePair<string, string>>();

            parameters.Add(new KeyValuePair<string, string>(key, value.ToString()));
        }

        public void AddHeader<T>(string key, T value)
        {
            Contract.RequiresNonNull(key, nameof(key));

            if (headers == null)
                headers = new Dictionary<string, string>();

            headers[key] = value.ToString();
        }
    }

    public class RestRequest<T> : RestRequest
    {
        protected RestRequest(string resource, HttpMethod method, T content, bool compress = false) : base(resource, method)
        {
            SetContent(content, compress);
        }

        protected RestRequest(HttpMethod method, T content, bool compress = false) : base(method)
        {
            SetContent(content, compress);
        }

        public static RestRequest<T> Create(string resource, HttpMethod method, T content = default(T), bool compress = false)
        {
            return new RestRequest<T>(resource, method, content, compress);
        }

        private void SetContent(T content, bool compress = false)
        {
            var serializer = new JsonSerializer();

            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, content);
                writer.Flush();

                if (!compress)
                    Content = new StringContent(writer.ToString(), Encoding.UTF8, "application/json");
                else
                {
                    using (var source = new MemoryStream(Encoding.UTF8.GetBytes(writer.ToString())))
                    {
                        var destination = new MemoryStream();

                        using (var gzip = new DeflateStream(destination, CompressionLevel.Optimal, true))
                        {
                            source.CopyTo(gzip);
                        }

                        if (destination.Length < source.Length)
                        {
                            destination.Seek(0, SeekOrigin.Begin);
                            var streamContent = new StreamContent(destination);
                            streamContent.Headers.Add("Content-Encoding", "deflate");
                            streamContent.Headers.Add("Content-Type", "application/json");
                            streamContent.Headers.ContentLength = destination.Length;

                            Content = streamContent;
                        }

                        else
                            Content = new StringContent(writer.ToString(), Encoding.UTF8, "application/json");
                    }
                }
            }
        }
    }
}
