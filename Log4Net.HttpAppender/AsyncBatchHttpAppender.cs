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
        private readonly Queue<LoggingEvent> pendingTasks;
        private readonly object lockObject = new object();
        private readonly ManualResetEvent manualResetEvent;
        private bool onClosing;

        // Configuration values (set in appender config)
        public string ServiceUrl { get; set; }
        public string ProjectKey { get; set; }
        public string Environment { get; set; }
        public int BatchMaxSize { get; set; }

        public AsyncBatchHttpAppender()
        {
            //initialize our queue
            pendingTasks = new Queue<LoggingEvent>();

            //put the event initially in non-signalled state
            manualResetEvent = new ManualResetEvent(false);

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
            if (!onClosing)
            {
                var thread = new Thread(processMessageQueue);
                thread.Start();
            }
        }

        private void processMessageQueue()
        {
            // we keep on processing tasks until shutdown on repository is called
            while (!onClosing)
            {
                LoggingEvent loggingEvent;

                var queuedLoggingEvents = new List<LoggingEvent>();
                int count = 0, max = BatchMaxSize > 0 ? BatchMaxSize : 40;

                while (deQueue(out loggingEvent))
                {
                    if (count == max) break;
                    queuedLoggingEvents.Add(loggingEvent);
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
                if (onClosing) break;

                // if they are no pending tasks sleep 10 seconds and try again
                Thread.Sleep(200);
                Debug.WriteLine("Sleeping for 100ms");
            }

            // we are done with our logging, sent the signal to the parent thread
            // so that it can commence shut down
            manualResetEvent.Set();
        }

        private void processQueuedLoggingEvents(IEnumerable<LoggingEvent> queuedLoggingEvents)
        {
            using (var client = new WebClient())
            {
                var start = DateTime.Now;
                try
                {
                    var loggingMessagesToSend = new List<dynamic>();

                    foreach (var loggingEvent in queuedLoggingEvents)
                        loggingMessagesToSend.Add(createLoggingEventModel(loggingEvent));

                    var json = new JavaScriptSerializer().Serialize(loggingMessagesToSend);

                    // Add headers required to submit request and additional data
                    client.Headers.Add("X-ProjectKey", ProjectKey);
                    client.Headers.Add("X-Environment", Environment ?? System.Environment.MachineName);

                    // Set content type and encoding
                    client.Headers.Add("Content-Type", "application/json; charset=utf-8");
                    client.Encoding = System.Text.Encoding.UTF8;

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

            // For errors attach additional information to provide greater insight into the context in
            // which the exception happened i.e. cookie values
            if (loggingEvent.Level.Name.ToLower() == "error" || loggingEvent.Level.Name.ToLower() == "fatal")
            {
                // attach http data
                if (null != HttpContext.Current)
                {
                    var context = HttpContext.Current;

                    // attach headers
                    var headers = new List<dynamic>();
                    foreach (var headerKey in context.Request.Headers.AllKeys)
                    {
                        var value = context.Request.Headers[headerKey];
                        if (!string.IsNullOrWhiteSpace(value))
                            headers.Add(new { key = headerKey, value });
                    }

                    loggingEventModel.http = new
                    {
                        url = context.Request.Url,
                        url_referrer = context.Request.UrlReferrer,
                        user_agent = context.Request.UserAgent,
                        user_host_address = context.Request.UserHostAddress,
                        user_host_name = context.Request.UserHostName,
                        http_method = context.Request.HttpMethod,
                        current_user = string.IsNullOrWhiteSpace(context.User.Identity.Name) ? null : context.User.Identity.Name,
                        authentication_type = string.IsNullOrWhiteSpace(context.User.Identity.AuthenticationType) ? null : context.User.Identity.AuthenticationType,
                        request_headers = headers.Count > 0 ? headers : null
                    };
                }
            }

            return loggingEventModel;
        }

        private void enqueue(LoggingEvent loggingEvent)
        {
            lock (lockObject)
            {
                pendingTasks.Enqueue(loggingEvent);
            }
        }

        private bool deQueue(out LoggingEvent loggingEvent)
        {
            lock (lockObject)
            {
                if (pendingTasks.Count > 0)
                {
                    loggingEvent = pendingTasks.Dequeue();
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
            onClosing = true;

            //wait till we receive signal from manualResetEvent
            //which is signalled from AppendLoggingEvents
            manualResetEvent.WaitOne(TimeSpan.FromSeconds(5));
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