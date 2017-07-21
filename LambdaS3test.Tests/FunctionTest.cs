using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Xunit;
using Amazon.Lambda.S3Events;

using Amazon;
using Amazon.Lambda.Core;
using Amazon.Lambda.TestUtilities;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Configuration;
using Moq;
using Newtonsoft.Json;
using Xunit.Sdk;

namespace LambdaS3test.Tests
{
    public class FunctionTest
    {
        private IConfigurationRoot Configuration { get; set; }
        public FunctionTest()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json");

            Configuration = builder.Build();
        }

        [Fact]
        public async Task TestS3EventLambdaFunction()
        {
            var accessKey = Configuration["AWS:AccessKeyId"];
            var secretKey = Configuration["AWS:SecretKey"];

            IAmazonS3 s3Client = new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), RegionEndpoint.APSoutheast2);

            var bucketName = "lambda-LambdaS3test-".ToLower() + DateTime.Now.Ticks;
            var key = "abcd1234";

            var contextMock = new Mock<ILambdaContext>();
            contextMock.Setup(x => x.Logger).Returns(new TestLambdaLogger());

            // Create a bucket an object to setup a test data.
            await s3Client.PutBucketAsync(bucketName);
            try
            {
                var imageData = new Models.ObjectModel();
                imageData.ImageData = await ReadImageBase64("TestData\\borders.jpg");

                await s3Client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = key,
                    ContentBody = JsonConvert.SerializeObject(imageData)
                });

                // Setup the S3 event object that S3 notifications would create with the fields used by the Lambda function.
                var s3Event = new S3Event
                {
                    Records = new List<S3EventNotification.S3EventNotificationRecord>
                    {
                        new S3EventNotification.S3EventNotificationRecord
                        {
                            S3 = new S3EventNotification.S3Entity
                            {
                                Bucket = new S3EventNotification.S3BucketEntity {Name = bucketName},
                                Object = new S3EventNotification.S3ObjectEntity {Key = key}
                            },
                            EventName = EventType.ObjectCreatedPut
                        }
                    }
                };

                // Invoke the lambda function and confirm the content type was returned.
                var function = new Function(s3Client);
                var contentType = await function.FunctionHandler(s3Event, contextMock.Object);

                Assert.Equal(null, contentType);
                Assert.True(true, "Succeeded");
            }
            catch (Exception e)
            {
                Assert.True(false, "Not succeeded");
            }
            finally
            {
                // Clean up the test data
                await AmazonS3Util.DeleteS3BucketWithObjectsAsync(s3Client, bucketName);
            }
        }

        private static async Task<string> ReadImageBase64(string filePath)
        {
            var stream = File.Open(filePath, FileMode.Open);
            var bytes = new byte[stream.Length];

            var read = await stream.ReadAsync(bytes, 0, (int) stream.Length);
            if (read != (int) stream.Length)
                throw new BadImageFormatException("Could not read test image file");

            return Convert.ToBase64String(bytes);
        }
    }
}
