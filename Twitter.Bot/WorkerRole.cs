using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using LinqToTwitter;
using Microsoft.WindowsAzure.ServiceRuntime;
using Newtonsoft.Json;
using RestSharp;

namespace Twitter.Bot
{
    public class WorkerRole : RoleEntryPoint
    {
        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private readonly ManualResetEvent runCompleteEvent = new ManualResetEvent(false);
        private SingleUserAuthorizer _singleUserAuthorizer;
        private List<string> _moods;

        public override void Run()
        {
            Trace.TraceInformation("Twitter.Bot is running");

            try
            {
                this.RunAsync(this.cancellationTokenSource.Token).Wait();
            }
            finally
            {
                this.runCompleteEvent.Set();
            }
        }

        public override bool OnStart()
        {
            // Set the maximum number of concurrent connections
            ServicePointManager.DefaultConnectionLimit = 12;

            // For information on handling configuration changes
            // see the MSDN topic at http://go.microsoft.com/fwlink/?LinkId=166357.

            bool result = base.OnStart();

            _singleUserAuthorizer = new SingleUserAuthorizer
            {
                CredentialStore = new SingleUserInMemoryCredentialStore
                {
                    ConsumerKey = ConfigurationManager.AppSettings["consumerKey"],
                    ConsumerSecret = ConfigurationManager.AppSettings["consumerSecret"],
                    AccessToken = ConfigurationManager.AppSettings["accessToken"],
                    AccessTokenSecret = ConfigurationManager.AppSettings["accessTokenSecret"]
                }
            };

            _moods = GetMoods().ToList();

            Trace.TraceInformation("Twitter.Bot has been started");

            return result;
        }

        public override void OnStop()
        {
            Trace.TraceInformation("Twitter.Bot is stopping");

            this.cancellationTokenSource.Cancel();
            this.runCompleteEvent.WaitOne();

            base.OnStop();

            Trace.TraceInformation("Twitter.Bot has stopped");
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            var context = new TwitterContext(_singleUserAuthorizer);
            await
                (from stream in context.Streaming
                    where stream.Type == StreamingType.User
                    select stream)
                    .StartAsync(async stream =>
                    {
                        if (!string.IsNullOrEmpty(stream.Content))
                            DoWork(stream.Content);

                        Thread.Sleep(1000);
                        if (cancellationToken.IsCancellationRequested)
                            stream.CloseStream();
                    });

            await Task.Delay(1000, cancellationToken);
        }

        private IEnumerable<string> GetMoods()
        {
            var client = new RestClient("http://191.238.115.5:8081");
            var request = new RestRequest("/getallmood", Method.GET);
            var response = client.Execute(request);
            dynamic content = JsonConvert.DeserializeObject(response.Content);

            foreach (var mood in content)
            {
                yield return mood;
            }
        }

        private void DoWork(string content)
        {
            dynamic thing = JsonConvert.DeserializeObject(content);
            string text = thing.text;
            if (thing.user == null) return;
            string username = thing.user.screen_name;
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(username)) return;
            foreach (var mood in _moods)
            {
                if (!text.ToLower().Contains(mood.ToLower())) continue;
                var client = new RestClient("http://191.238.115.5:8081");
                var calendarEndpoint = string.Format("/userwithoutroom");
                var request = new RestRequest(calendarEndpoint, Method.PUT);

                request.AddObject(new
                {
                    twitterHandle = username, mood
                });

                client.Execute(request);
                break;
            }
        }
    }
}
