﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Hammock;
using Hammock.Retries;

namespace Couchbase
{
	/// <summary>
	/// Default implementation of the <see cref="IHttpClient"/> using the Hammock REST library.
	/// </summary>
	internal class HammockHttpClient : IHttpClient
	{
		private static readonly Enyim.Caching.ILog log = Enyim.Caching.LogManager.GetLogger(typeof(HammockHttpClient));

		private RestClient client;

		public HammockHttpClient(Uri baseUri)
		{
			this.client = new RestClient { Authority = baseUri.ToString() };

			client.AddHeader("Accept", "application/json");
			client.AddHeader("Content-Type", "application/json; charset=utf-8");

			client.ServicePoint = System.Net.ServicePointManager.FindServicePoint(baseUri);
			client.ServicePoint.SetTcpKeepAlive(true, 300, 30);

			client.RetryPolicy = new RetryPolicy
			{
				RetryConditions =
				{
					new Hammock.Retries.NetworkError(),
					new Hammock.Retries.Timeout(),
					new Hammock.Retries.ConnectionClosed()
				},
				RetryCount = 3
			};

			client.BeforeRetry += new EventHandler<RetryEventArgs>(client_BeforeRetry);
		}

		void client_BeforeRetry(object sender, RetryEventArgs e)
		{
			if (log.IsWarnEnabled)
				log.Warn("Retrying " + e.Request.Path);
		}

		IHttpRequest IHttpClient.CreateRequest(string path)
		{
			return new HammockRequestWrapper(this.client, path);
		}

		int IHttpClient.RetryCount
		{
			get { return this.client.RetryPolicy.RetryCount; }
			set { this.client.RetryPolicy.RetryCount = value; }
		}

		#region [ HammockRequestWrapper        ]

		private class HammockRequestWrapper : IHttpRequest
		{
			private RestRequest request;
			private RestClient client;
			private HttpMethod method;

			public HammockRequestWrapper(RestClient client, string path)
			{
				this.client = client;
				this.method = HttpMethod.Get;
				this.request = new RestRequest { Path = path };
			}

			void IHttpRequest.AddParameter(string name, string value)
			{
				this.request.AddParameter(name, value);
			}

			IHttpResponse IHttpRequest.GetResponse()
			{
				switch (this.method)
				{
					case HttpMethod.Delete: this.request.Method = Hammock.Web.WebMethod.Delete; break;
					case HttpMethod.Get: this.request.Method = Hammock.Web.WebMethod.Get; break;
					case HttpMethod.Head: this.request.Method = Hammock.Web.WebMethod.Head; break;
					case HttpMethod.Options: this.request.Method = Hammock.Web.WebMethod.Options; break;
					case HttpMethod.Post: this.request.Method = Hammock.Web.WebMethod.Post; break;
					case HttpMethod.Put: this.request.Method = Hammock.Web.WebMethod.Put; break;
					default: throw new ArgumentOutOfRangeException("method: " + this.method);
				}

				var r = new HammockResponseWrapper(request);
				r.ExecuteWith(this.client);

				return r;
			}

			HttpMethod IHttpRequest.Method
			{
				get { return this.method; }
				set { this.method = value; }
			}
		}

		#endregion
		#region [ HammockResponseWrapper       ]

		private class HammockResponseWrapper : IHttpResponse
		{
			private RestRequest request;
			private RestResponse response;

			public HammockResponseWrapper(RestRequest request)
			{
				this.request = request;
			}

			public void ExecuteWith(RestClient client)
			{
				this.response = client.Request(this.request);

				if (response.InnerException != null) throw response.InnerException;
				if (response.StatusCode != System.Net.HttpStatusCode.OK)
					throw new InvalidOperationException(String.Format("Server returned {0}: {1}, {2}", response.StatusCode, response.StatusDescription, response.Content));
			}

			Stream IHttpResponse.GetResponseStream()
			{
				return this.response.ContentStream;
			}
		}

		#endregion
	}

	/// <summary>
	/// Creates instances of the Hammock implementation of <see cref="IHttpClient"/>.
	/// </summary>
	public class HammockHttpClientFactory : IHttpClientFactory
	{
		public static readonly IHttpClientFactory Instance = new HammockHttpClientFactory();

		IHttpClient IHttpClientFactory.Create(Uri baseUri)
		{
			return new HammockHttpClient(baseUri);
		}
	}
}
