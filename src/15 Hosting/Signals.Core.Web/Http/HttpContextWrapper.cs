﻿using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Signals.Aspects.DI;
using Signals.Core.Common.Instance;
using Signals.Core.Common.Serialization;
using Signals.Core.Processing.Input.Http;
using Signals.Core.Processing.Input.Http.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Signals.Core.Web.Http
{
    /// <summary>
    /// Wrapper around real http context
    /// </summary>
    public class HttpContextWrapper : IHttpContextWrapper
    {
        /// <summary>
        /// Http method: GET, POST, DELETE..
        /// </summary>
        public string HttpMethod { get; set; }

        /// <summary>
        /// Url path: /api/process...
        /// </summary>
        public string RawUrl { get; set; }

        /// <summary>
        /// Streams of input files
        /// </summary>
        public IEnumerable<InputFile> Files { get; set; }

        /// <summary>
        /// Request query
        /// </summary>
        public IDictionary<string, object> Query { get; set; }

        /// <summary>
        /// Request body
        /// </summary>
        public Lazy<string> Body { get; set; }

        /// <summary>
        /// Headers collection manager
        /// </summary>
        public IHeaderCollection Headers { get; set; }

        /// <summary>
        /// Form collection manager
        /// </summary>
        public Processing.Input.Http.IFormCollection Form { get; set; }

        /// <summary>
        /// Cookies collection manager
        /// </summary>
        public ICookieCollection Cookies { get; set; }

        /// <summary>
        /// Session manager
        /// </summary>
        public ISessionProvider Session { get; set; }

        /// <summary>
        /// Reads body as string
        /// </summary>
        /// <param name="contentType"></param>
        /// <param name="inputStream"></param>
        /// <returns></returns>
        private string ExtractBody(string contentType, Stream inputStream)
        {
            if (contentType?.StartsWith("application/x-www-form-urlencoded") == true
             || contentType?.StartsWith("multipart/form-data") == true)
            {
                return Form.SerializeJson();
            }
            else
            {
                var content = new StreamReader(inputStream).ReadToEnd();
                return content;
            }
        }

        /// <summary>
        /// Extracts query string data
        /// </summary>
        /// <param name="queryString"></param>
        /// <returns></returns>
        private IDictionary<string, object> ExtractQuery(string queryString)
        {
            var result = new Dictionary<string, object>();
            if (queryString.IsNullOrEmpty()) return result;

            var arrayOrObjectRegex = new Regex("\\[[^\\[\\]]*\\]");

            var decodedQuery = WebUtility.UrlDecode(queryString).TrimStart('?');
            var querySegments = decodedQuery.Split('&');

            // process each key value pair from query string
            foreach (var segment in querySegments)
            {
                var segmentParts = segment.Split('=');

                var segmentKey = segmentParts.FirstOrDefault();
                var segmentValue = segmentParts.LastOrDefault();

                // is the key array or object
                var isArrayOrObjectMatches = arrayOrObjectRegex.Matches(segmentKey) as IEnumerable<Match>;

                if (isArrayOrObjectMatches.Any())
                {
                    var lastDictContainer = result;

                    // get array or object first sub key
                    var lastSubSegmentKey = segmentKey
                        .Split('[')
                        .FirstOrDefault();

                    foreach (var objMatch in isArrayOrObjectMatches)
                    {
                        // get array or object next sub key
                        var subSegmentkey = objMatch.Value.TrimStart('[').TrimEnd(']');

                        // create tree structure from dictionaries
                        if (!lastDictContainer.ContainsKey(lastSubSegmentKey))
                            lastDictContainer.Add(lastSubSegmentKey, new Dictionary<string, object>());

                        lastDictContainer = lastDictContainer[lastSubSegmentKey] as Dictionary<string, object>;
                        lastSubSegmentKey = subSegmentkey;
                    }

                    // get last array or object sub key
                    var lastDictKey = isArrayOrObjectMatches
                        .LastOrDefault()?
                        .Value?
                        .TrimStart('[')
                        .TrimEnd(']');

                    // edge case for arrays without key []
                    // cases for arrays with key [0] are not affected
                    if (lastDictKey.IsNullOrEmpty())
                    {
                        if (lastDictContainer.Any())
                            lastDictKey = (int.Parse(lastDictContainer.Keys.Last()) + 1).ToString();
                        else
                            lastDictKey = "0";
                    }

                    // add value into result
                    lastDictContainer[lastDictKey] = segmentValue;
                }
                else
                {
                    // add value into result
                    if (!result.ContainsKey(segmentKey))
                        result.Add(segmentKey, segmentValue);
                }
            }

            // restructure dictionaries into arrays an ddictionaries
            Queue<object> tree = new Queue<object>();
            tree.Enqueue(result);

            while (tree.Any())
            {
                var item = tree.Dequeue();

                // process enqueued dictionaries
                if (item is Dictionary<string, object> dictionary)
                {
                    Dictionary<string, object> newDict = new Dictionary<string, object>();

                    // process subdictionaries
                    // cannot habe sublists because sub items are still not processed
                    // unprocessed items are all either dictionaty or value
                    foreach (var pair in dictionary)
                    {
                        if (pair.Value is Dictionary<string, object> subDictionary)
                        {
                            var isRealDict = subDictionary.Any(x => !int.TryParse(x.Key, out _));

                            if (isRealDict)
                                // pass values as dictionary
                                newDict[pair.Key] = pair.Value;
                            else
                                // repack values into list
                                newDict[pair.Key] = subDictionary.Values.ToList();

                            tree.Enqueue(pair.Value);
                        }
                    }

                    // add result into existing dictionary
                    // cannot change existing dictionary into upper foreach
                    // enumerables cannot be changed while iterated
                    foreach (var pair in newDict)
                    {
                        dictionary[pair.Key] = pair.Value;
                    }
                }

                // process enqueued lists
                if (item is List<object> list)
                {
                    // process subdictionaries
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i] is Dictionary<string, object> subDictionary)
                        {
                            var isRealDict = subDictionary.Any(x => !int.TryParse(x.Key, out _));

                            if (!isRealDict)
                            {
                                // repack values into list
                                list[i] = subDictionary.Values.ToList();
                            }

                            tree.Enqueue(list[i]);
                        }
                    }
                }
            }

            return result;
        }

#if (NET461)

        /// <summary>
        /// CTOR
        /// </summary>
        public HttpContextWrapper()
        {
            var context = System.Web.HttpContext.Current;
            if (context == null) return;

            Headers = new HeaderCollection(context);
            Cookies = new CookieCollection(context);
            Form = new FormCollection(context);
            Session = new SessionProvider(context);
            
            // single lazy per http context
            if (!context.Items.Contains("body"))
                context.Items.Add("body", new Lazy<string>(() => ExtractBody(context.Request.ContentType, context.Request.InputStream)));

            Query = ExtractQuery(context?.Request?.QueryString?.ToString());

            Body = context.Items["body"] as Lazy<string>;

            HttpMethod = context.Request.HttpMethod.ToUpperInvariant();
            Files = context.Request.Files.AllKeys.Select(x => new InputFile
            {
                File = context.Request.Files[x]?.InputStream,
                MimeType = context.Request.Files[x]?.ContentType,
                FormInputName = x,
                FileName = context.Request.Files[x]?.FileName
            });
            RawUrl = context.Request.Url.AbsolutePath;
        }

        /// <summary>
        /// Write response
        /// </summary>
        /// <param name="httpResponse"></param>
        /// <returns></returns>
        public void PutResponse(HttpResponseMessage httpResponse)
        {
            var context = System.Web.HttpContext.Current;

            if (httpResponse?.Headers?.Any() == true)
                foreach (var header in httpResponse?.Headers)
                    context.Response.Headers.Add(header.Key, new StringValues(header.Value.ToArray()));


            if (httpResponse?.Content?.Headers?.Any() == true)
                foreach (var header in httpResponse?.Content?.Headers)
                    context.Response.Headers.Add(header.Key, new StringValues(header.Value.ToArray()));

            context.Response.StatusCode = (int)httpResponse.StatusCode;

            if (!httpResponse.Content.IsNull())
            {
                var body = httpResponse.Content.ReadAsStreamAsync().Result;
                body.CopyTo(context.Response.OutputStream);
                body.Close();
            }
        }

#else

        private IHttpContextAccessor _httpContextAccessor;

        /// <summary>
        /// CTOR
        /// </summary>
        public HttpContextWrapper(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
            var context = _httpContextAccessor.HttpContext;
            if (context == null) return;

            Headers = new HeaderCollection(context);
            Cookies = new CookieCollection(context);
            Form = new FormCollection(context);
            Session = new SessionProvider(context);

            // single lazy per http context
            if (!context.Items.ContainsKey("body"))
                context.Items.Add("body", new Lazy<string>(() => ExtractBody(context.Request.ContentType, context.Request.Body)));

            Query = ExtractQuery(context?.Request?.QueryString.ToString());

            Body = context.Items["body"] as Lazy<string>;
            HttpMethod = context.Request.Method.ToUpperInvariant();

            if (context.Request.HasFormContentType)
            {
                Files = context.Request?.Form?.Files?
                                .Select(x => new InputFile
                                {
                                    File = x.OpenReadStream(),
                                    FileName = x.FileName,
                                    FormInputName = x.Name,
                                    MimeType = x.ContentType,
                                    ContentLength = x.Length
                                })
                            ?? new List<InputFile>();
            }

            RawUrl = context.Request.Path.Value;
        }

        /// <summary>
        /// Write response
        /// </summary>
        /// <param name="httpResponse"></param>
        /// <returns></returns>
        public void PutResponse(HttpResponseMessage httpResponse)
        {
            var contextAccessor = _httpContextAccessor ?? SystemBootstrapper.GetInstance<IHttpContextAccessor>();
            var context = _httpContextAccessor.HttpContext;

            if (httpResponse?.Headers?.Any() == true)
                foreach (var header in httpResponse?.Headers)
                    context.Response.Headers.Add(header.Key, new StringValues(header.Value.ToArray()));

            if (httpResponse?.Content?.Headers?.Any() == true)
                foreach (var header in httpResponse?.Content?.Headers)
                    context.Response.Headers.Add(header.Key, new StringValues(header.Value.ToArray()));

            context.Response.StatusCode = (int)httpResponse.StatusCode;

            if (!httpResponse.Content.IsNull())
            {
                var body = httpResponse.Content.ReadAsStreamAsync().Result;
                body.CopyToAsync(context.Response.Body).Wait();
                body.Close();
            }
        }

#endif

    }
}
