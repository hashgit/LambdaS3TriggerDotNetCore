using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using LambdaS3test.Models;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace LambdaS3test
{
    public class Function
    {
        private ILambdaLogger logger;
        private IConfigurationRoot Configuration { get; set; }

        IAmazonS3 S3Client { get; set; }

        /// <summary>
        /// Default constructor. This constructor is used by Lambda to construct the instance. When invoked in a Lambda environment
        /// the AWS credentials will come from the IAM role associated with the function and the AWS region will be set to the
        /// region the Lambda function is executed in.
        /// </summary>
        public Function()
            : this(new AmazonS3Client())
        {
        }

        /// <summary>
        /// Constructs an instance with a preconfigured S3 client. This can be used for testing the outside of the Lambda environment.
        /// </summary>
        /// <param name="s3Client"></param>
        public Function(IAmazonS3 s3Client)
        {
            this.S3Client = s3Client;
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json");

            Configuration = builder.Build();
        }

        /// <summary>
        /// This method is called for every Lambda invocation. This method takes in an S3 event object and can be used 
        /// to respond to S3 notifications.
        /// </summary>
        /// <param name="evnt"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task<string> FunctionHandler(S3Event evnt, ILambdaContext context)
        {
            logger = context.Logger;
            var s3Event = evnt.Records?[0].S3;
            if(s3Event == null)
            {
                return null;
            }

            if (evnt.Records?[0].EventName != EventType.ObjectCreatedPut)
                return null;

            try
            {
                var bucketName = s3Event.Bucket.Name;
                var key = s3Event.Object.Key;

                var response = await this.S3Client.GetObjectAsync(bucketName, key);
                logger.LogLine("Retrived Object");
                var stringReader = new StreamReader(response.ResponseStream);
                var data = await stringReader.ReadToEndAsync();
                var dataObject = JsonConvert.DeserializeObject<Models.ObjectModel>(data);
                logger.LogLine("Deserialized Object");

                if (dataObject.State != ObjectState.UNKNOWN)
                    return null;

                await ProcessData(dataObject, bucketName, key);

                return null;
            }
            catch(Exception e)
            {
                logger.LogLine($"Error getting object {s3Event.Object.Key} from bucket {s3Event.Bucket.Name}. Make sure they exist and your bucket is in the same region as this function.");
                logger.LogLine(e.Message);
                logger.LogLine(e.StackTrace);
                throw;
            }
        }

        private async Task ProcessData(ObjectModel dataObject, string bucketName, string key)
        {
            switch (dataObject.State)
            {
                case ObjectState.UNKNOWN:
                    await SetStatus(dataObject, bucketName, key, ObjectState.PROCESSING);
                    var content = await MakeRequest(dataObject.ImageData);
                    if (content == null)
                    {
                        await SetStatus(dataObject, bucketName, key, ObjectState.FAILED);
                    }
                    else
                    {
                        var ocr = JsonConvert.DeserializeObject<OcrResult>(content);
                        dataObject.OcrResult = ocr;
                        await SetStatus(dataObject, bucketName, key, ObjectState.SUCCESS);
                    }
                    break;
                default:
                    break;
            }
        }

        private async Task SetStatus(ObjectModel dataObject, string bucketName, string key, ObjectState state)
        {
            await S3Client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucketName,
                Key = key,
                ContentBody = JsonConvert.SerializeObject(new ObjectModel
                {
                    ImageData = dataObject.ImageData,
                    State = state,
                    OcrResult = dataObject.OcrResult
                })
            });
        }

        private async Task<string> MakeRequest(string dataObjectImageData)
        {
            var client = new HttpClient();

            var azureSubscriptionKey = Configuration["Azure:SubscriptionKey"];
            // Request headers
            client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", azureSubscriptionKey);

            // Request parameters
            var uri = "https://southeastasia.api.cognitive.microsoft.com/vision/v1.0/ocr?language=en";

            HttpResponseMessage response;

            using (var content = new ByteArrayContent(Convert.FromBase64String(dataObjectImageData)))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                response = await client.PostAsync(uri, content);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var contentString = await response.Content.ReadAsStringAsync();
                    return contentString;
                }
            }

            return null;
        }
    }
}
