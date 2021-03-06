﻿using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using SecurePay.Messages;

namespace SecurePay.Runtime
{
    /// <summary>
    /// Base class for web service clients. Contains common functionality for accessing the server.
    /// </summary>
    public abstract class WebServiceClient : IWebServiceClient
    {
        public ClientConfig Config { get; private set; }
        private byte[] _lastRequest;
        private byte[] _lastResponse;

        public string LastRequest
        {
            get { return Encoding.UTF8.GetString(_lastRequest); }
        }

        public string LastResponse
        {
            get { return Encoding.UTF8.GetString(_lastResponse); }
        }

		public string ApiVersion { get; protected set; }

		public async Task<EchoResponseMessage> EchoAsync()
		{
			var echoRequestMessage = new EchoRequestMessage();
			EchoResponseMessage response = await PostAsync<EchoRequestMessage, EchoResponseMessage>(echoRequestMessage);
			return response;
		}

        protected WebServiceClient(ClientConfig config, string apiVersion)
        {
            Config = config ?? new ClientConfig();
	        ApiVersion = apiVersion;
        }

        protected async Task<TResponse> PostAsync<TRequest, TResponse>(TRequest requestMessage) 
            where TRequest: SecurePayMessage
            where TResponse: SecurePayResponseMessage
        {
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
            var url = GetServiceUrl();

	        requestMessage.MessageInfo.ApiVersion = ApiVersion;
            requestMessage.MessageInfo.MessageId = Guid.NewGuid().ToString();
            requestMessage.MessageInfo.MessageTimestamp = DateTimeOffset.Now.ToSecurePayTimestampString();
            requestMessage.MerchantInfo.MerchantId = Config.MerchantId;
            requestMessage.MerchantInfo.Password = Config.Password;
            var httpContent = CreateContent(requestMessage);

            var httpResponseMessage = await httpClient.PostAsync(url, httpContent);

            _lastResponse = await httpResponseMessage.Content.ReadAsByteArrayAsync();

            using (var stream = new MemoryStream(_lastResponse))
            {
                var serializer = new XmlSerializer(typeof (TResponse));
                var responseMessage = (TResponse)serializer.Deserialize(stream);

                if (responseMessage.Status == null)
                {
                    throw new SecurePayException("Missing status in response") {
                        Request = LastRequest,
                        Response = LastResponse
                    };
                }

                int statusCode;
                if (!int.TryParse(responseMessage.Status.StatusCode, out statusCode) || statusCode != 0)
                {
                    throw new SecurePayException(responseMessage.Status.StatusCode, responseMessage.Status.Description) {
                        Request = LastRequest,
                        Response = LastResponse
                    };
                }

                return responseMessage;
            }
        }

        protected HttpContent CreateContent<T>(T requestMessage)
        {
            using (var stream = new MemoryStream())
            {
                var serializer = new XmlSerializer(typeof (T));
                serializer.Serialize(stream, requestMessage);
                _lastRequest = stream.ToArray();
                var content = new ByteArrayContent(_lastRequest);
                // Content type of 'application/xml' is more normal for XML these days,
                // but the SecurePay documentation examples use 'text/xml'.
                content.Headers.ContentType = new MediaTypeHeaderValue("text/xml");
                return content;
            }
        }

        /// <summary>
        /// Implemented by derived classes to provide the service endpoint URL. Different SecurePay
        /// services have different absolute paths, though they share the same hostname in the URL.
        /// For example the periodic endpoint has an absolute path of "/xmlapi/periodic" and the payment
        /// endpoint has an absolute path of "/xmlapi/payment".
        /// </summary>
        protected abstract Uri GetServiceUrl();
    }
}
