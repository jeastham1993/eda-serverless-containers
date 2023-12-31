﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using static Amazon.Lambda.SQSEvents.SQSBatchResponse;
using AWS.Lambda.Powertools.Logging;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace LambdaProxyHandler
{
    public class Function
    {
        private readonly HttpClient _client;

        public Function()
        {
            this._client = new HttpClient();
        }

        public async Task<SQSBatchResponse> FunctionHandler(SQSEvent evt, ILambdaContext context)
        {
            var failedMessages = new List<BatchItemFailure>();

            foreach (var record in evt.Records)
            {
                try
                {
                    Logger.LogInformation($"Processing message {record.Body}");
                    
                    Logger.LogInformation($"Making request to {Environment.GetEnvironmentVariable("FORWARDING_URL")}");

                    var postResult = await this._client.PostAsync(Environment.GetEnvironmentVariable("FORWARDING_URL"), new StringContent(record.Body, Encoding.UTF8, "application/json"));

                    if (!postResult.IsSuccessStatusCode)
                    {
                        throw new Exception($"Failed to forward message to forwarding URL. {postResult.StatusCode} - {await postResult.Content.ReadAsStringAsync()}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failure processing SQS message:");
                    
                    failedMessages.Add(new BatchItemFailure()
                    {
                        ItemIdentifier = record.MessageId,
                    });
                }
            }

            return new SQSBatchResponse(failedMessages);
        }
    }
}