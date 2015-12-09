namespace Log4Net.HttpAppender
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Net;
    using System.Threading;
    using System.Web;
    using log4net.Appender;
    using log4net.Core;
    using System.Web.Script.Serialization;

    public class AsyncBatchHttpAppender : AppenderSkeleton
    {
        private readonly Queue<LoggingEventModel> _queue;
        private readonly object _lockObject = new object();
        private readonly ManualResetEvent _manualResetEvent;
        private bool _onClosing;

        // Configuration values (set in appender config)
        public string ServiceUrl { get; set; }
        public string ProjectKey { get; set; }
        public string Environment { get; set; }
        public int BatchMaxSize { get; set; }
        public int BatchSleepTime { get; set; }

        public AsyncBatchHttpAppender()
        {
            //initialize our queue
            _queue = new Queue<LoggingEventModel>();

            //put the event initially in non-signalled state
            _manualResetEvent = new ManualResetEvent(false);

            //start the asyn process of handling pending tasks
            Start();
        }

        protected override void Append(LoggingEvent[] loggingEvents)
        {
            foreach (var loggingEvent in loggingEvents)
                Append(loggingEvent);
        }

        protected override void Append(LoggingEvent loggingEvent)
        {
            if (FilterEvent(loggingEvent))
                enqueue(loggingEvent);
        }

        private void Start()
        {
            // hopefully user doesnt open and close the GUI or CONSOLE OR WEBPAGE
            // right away. anyway lets add that condition too
            if (!_onClosing)
            {
                var thread = new Thread(processMessageQueue);
                thread.Start();
            }
        }

        private void processMessageQueue()
        {
            // we keep on processing tasks until shutdown on repository is called
            while (!_onClosing)
            {
                LoggingEventModel loggingEvent;

                var queuedLoggingEvents = new List<LoggingEventModel>();
                int count = 0, max = BatchMaxSize > 0 ? BatchMaxSize : 40;

                while (deQueue(out loggingEvent))
                {
                    if (count == max) break;
                    queuedLoggingEvents.Add(loggingEvent);
                    Debug.WriteLine("Dequeued " + loggingEvent.message);
                    count++;
                }

                if (queuedLoggingEvents.Any())
                {
                    Debug.WriteLine("++" + queuedLoggingEvents.Count + " EVENTS TO PUBLISH++");

                    try
                    {
                        processQueuedLoggingEvents(queuedLoggingEvents);
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine("There was an error attempting to submit the logging events to endpoint");
                        Debug.Write(e.Message);
                        Debug.Write(e.StackTrace);
                    }

                    Debug.WriteLine("+++");
                }
                else
                    Debug.WriteLine("++0 EVENTS++");

                // if closing is already initiated break
                if (_onClosing) break;

                // if they are no pending tasks sleep 10 seconds and try again
                var batchSleepTime = BatchSleepTime == 0 ? 200 : BatchSleepTime;
                Thread.Sleep(batchSleepTime);
                Debug.WriteLine("Sleeping for " + batchSleepTime + "ms");
            }

            // we are done with our logging, sent the signal to the parent thread
            // so that it can commence shut down
            _manualResetEvent.Set();
        }

        private void processQueuedLoggingEvents(IEnumerable<LoggingEventModel> queuedLoggingEvents)
        {
            using (var client = new WebClient())
            {
                var start = DateTime.Now;
                try
                {
                    var loggingMessagesToSend = new List<dynamic>();

                    foreach (var loggingEvent in queuedLoggingEvents)
                        loggingMessagesToSend.Add(loggingEvent);

                    var json = new JavaScriptSerializer().Serialize(loggingMessagesToSend);

                    // Add headers required to submit request and additional data
                    client.Headers.Add("X-ProjectKey", ProjectKey);
                    client.Headers.Add("X-Environment", Environment ?? System.Environment.MachineName);

                    // Set content type and encoding
                    client.Headers.Add("Content-Type", "application/json; charset=utf-8");
                    client.Encoding = System.Text.Encoding.UTF8;

                    Debug.Write("Sending JSON data: " + json);

                    // up the payload
                    client.UploadString(ServiceUrl, "POST", json);
                    Debug.WriteLine("Sent collection of events: " + loggingMessagesToSend.Count);
                }
                catch (WebException ex)
                {
                    Debug.WriteLine("Error sending logging event to logmon dropoff {0}. Response data: {1}", ex.Message, ex.StackTrace);
                    throw;
                }
                finally
                {
                    Debug.WriteLine("Took " + ((DateTime.Now) - start).TotalMilliseconds + "ms to submit");
                }
            }
        }

        private LoggingEventModel createLoggingEventModel(LoggingEvent loggingEvent)
        {
            var loggingEventModel = new LoggingEventModel
            {
                user = string.IsNullOrWhiteSpace(loggingEvent.Identity) ? null : loggingEvent.Identity,
                logger = loggingEvent.LoggerName,
                level = loggingEvent.Level.Name.ToLower(),
                message = loggingEvent.RenderedMessage,
                time_stamp = loggingEvent.TimeStamp.ToUniversalTime().ToString("u")
            };

            // Grab stack trace
            if (null != loggingEvent.ExceptionObject && !string.IsNullOrWhiteSpace(loggingEvent.ExceptionObject.StackTrace))
                loggingEventModel.stack_trace = loggingEvent.ExceptionObject.StackTrace;

            // Attach additional http request data when available
            if (null != HttpContext.Current)
            {
                var context = HttpContext.Current;

                // For fatal and error attach headers and any form data
                var headers = new List<dynamic>();
                var formParams = new List<dynamic>();

                if (loggingEvent.Level.Name.ToLower() == "error" || loggingEvent.Level.Name.ToLower() == "fatal")
                {
                    foreach (var headerKey in context.Request.Headers.AllKeys)
                    {
                        var value = context.Request.Headers[headerKey];
                        if (!string.IsNullOrWhiteSpace(value))
                            headers.Add(new { key = headerKey, value });
                    }

                    if (context.Request.Form.Keys.Count > 0)
                    {
                        foreach (var key in context.Request.Form.AllKeys)
                        {
                            var value = context.Request.Form[key];
                            if (!string.IsNullOrWhiteSpace(value))
                                formParams.Add(new { key, value = context.Request.Form[key] });
                        }
                    }
                }

                loggingEventModel.http = new
                {
                    session_id = null != context.Session ? context.Session.SessionID : null,
                    current_user = string.IsNullOrWhiteSpace(context.User.Identity.Name) ? null : context.User.Identity.Name,
                    http_method = context.Request.HttpMethod,
                    url = context.Request.Url,
                    url_referrer = context.Request.UrlReferrer,
                    user_agent = context.Request.UserAgent,
                    user_host_address = context.Request.UserHostAddress,
                    user_host_name = context.Request.UserHostName,
                    authentication_type = string.IsNullOrWhiteSpace(context.User.Identity.AuthenticationType) ? null : context.User.Identity.AuthenticationType,
                    request_headers = headers.Count > 0 ? headers : null,
                    form_params = formParams.Count > 0 ? formParams : null
                };
            }

            return loggingEventModel;
        }

        private void enqueue(LoggingEvent loggingEvent)
        {
            lock (_lockObject)
            {
                Debug.WriteLine("Enqued " + loggingEvent.MessageObject);
                _queue.Enqueue(createLoggingEventModel(loggingEvent));
            }
        }

        private bool deQueue(out LoggingEventModel loggingEvent)
        {
            lock (_lockObject)
            {
                if (_queue.Count > 0)
                {
                    loggingEvent = _queue.Dequeue();
                    return true;
                }

                loggingEvent = null;
                return false;
            }
        }

        protected override void OnClose()
        {
            //set the OnClosing flag to true, so that
            //AppendLoggingEvents would know it is time to wrap up
            //whatever it is doing
            _onClosing = true;

            //wait till we receive signal from manualResetEvent
            //which is signalled from AppendLoggingEvents
            _manualResetEvent.WaitOne(TimeSpan.FromSeconds(5));
            //manualResetEvent.WaitOne();

            base.OnClose();
        }
    }

    public class LoggingEventModel
    {
        public string user { get; set; }
        public string logger { get; set; }
        public string level { get; set; }
        public string message { get; set; }
        public string stack_trace { get; set; }
        public string time_stamp { get; set; }
        public dynamic http { get; set; }
    }
}